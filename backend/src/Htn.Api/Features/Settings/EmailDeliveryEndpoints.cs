using System.Globalization;
using System.Net.Mail;
using FluentValidation;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Api.Features.Settings;

/// <summary>GET/PUT /api/settings/email. Values are the effective ones; the token itself is never returned.</summary>
public sealed record EmailDeliveryDto(
    EmailDeliveryMode Mode, string? InboxId, string FromEmail, string FromName, string? ReplyTo,
    bool TokenSet, string? TokenLast4, bool TokenUnreadable, EmailConfigSource Source, DateTimeOffset? UpdatedAt);

/// <summary>PUT body. A null or blank <see cref="ApiToken"/> keeps the stored token; <see cref="ClearToken"/> removes it.</summary>
public sealed record UpdateEmailDeliveryRequest(
    EmailDeliveryMode? Mode, string? InboxId, string? FromEmail, string? FromName, string? ReplyTo, string? ApiToken, bool ClearToken);

public sealed record SendDeliveryTestRequest(string? To);

public sealed record DeliveryTestQueuedDto(string To);

public sealed class UpdateEmailDeliveryRequestValidator : AbstractValidator<UpdateEmailDeliveryRequest>
{
    public UpdateEmailDeliveryRequestValidator()
    {
        RuleFor(x => x.Mode).NotNull().WithMessage("Choose sandbox or live.")
            .Must(m => m is null || Enum.IsDefined(m.Value)).WithMessage("Choose sandbox or live.");
        RuleFor(x => x.InboxId).NotEmpty().When(x => x.Mode == EmailDeliveryMode.Sandbox).WithMessage("Sandbox mode needs a Mailtrap inbox ID.");
        RuleFor(x => x.InboxId).Must(EmailDeliverySettings.IsValidInboxId).When(x => !string.IsNullOrWhiteSpace(x.InboxId))
            .WithMessage($"The inbox ID is up to {EmailDeliverySettings.InboxIdMaxLength} letters and digits.");
        RuleFor(x => x.FromEmail).Must(EmailDeliverySettings.IsValidEmail).WithMessage("Enter a valid from address.");
        RuleFor(x => x.FromName).NotEmpty().WithMessage("Enter the from name.")
            .MaximumLength(EmailDeliverySettings.FromNameMaxLength).WithMessage($"Keep the from name under {EmailDeliverySettings.FromNameMaxLength} characters.");
        RuleFor(x => x.ReplyTo).Must(EmailDeliverySettings.IsValidEmail).When(x => !string.IsNullOrWhiteSpace(x.ReplyTo))
            .WithMessage("Enter a valid reply-to address.");
        RuleFor(x => x.ApiToken).Must(EmailDeliverySettings.IsValidToken).When(x => !string.IsNullOrWhiteSpace(x.ApiToken))
            .WithMessage($"The API token is {EmailDeliverySettings.TokenMinLength}–{EmailDeliverySettings.TokenMaxLength} characters without spaces.");
        RuleFor(x => x.ClearToken).Must(c => !c).When(x => !string.IsNullOrWhiteSpace(x.ApiToken))
            .WithMessage("Either enter a new token or remove the saved one, not both.");
    }
}

public sealed class SendDeliveryTestRequestValidator : AbstractValidator<SendDeliveryTestRequest>
{
    public SendDeliveryTestRequestValidator()
    {
        RuleFor(x => x.To).Must(EmailDeliverySettings.IsValidEmail).WithMessage("Enter a valid email address.");
    }
}

public static class EmailDeliveryEndpoints
{
    public const string AuditUpdate = "settings.email_update";
    public const string AuditTest = "settings.email_test";

    /// <summary>The organization's mail domain; test sends may go here or to the caller's own address. Override with Email:OrgDomain.</summary>
    public const string DefaultOrgDomain = "hackthenorth.ph";

    public static RouteGroupBuilder MapEmailDelivery(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings/email").WithTags("Settings");
        g.MapGet("/", Get).RequireAuthorization(Policies.SettingsManage);
        g.MapPut("/", Update).RequireAuthorization(Policies.SettingsManage).Validate<UpdateEmailDeliveryRequest>();
        g.MapPost("/test", SendTest).RequireAuthorization(Policies.SettingsManage).Validate<SendDeliveryTestRequest>()
            .RequireRateLimiting(SettingsRateLimiting.EmailTest);
        return g;
    }

    private static Task<OrgSettings> Load(HtnDbContext db, CancellationToken ct) =>
        db.OrgSettings.SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);

    internal static EmailDeliveryDto Dto(EmailDeliverySettings saved, MailtrapOptions env, IEmailTokenProtector protector)
    {
        var (o, source, unreadable) = MailtrapConfigProvider.Resolve(saved, env, protector);
        var (fromEmail, fromName) = SplitAddress(o.From);
        var replyTo = string.IsNullOrWhiteSpace(o.ReplyTo) ? null : SplitAddress(o.ReplyTo).Email;
        var last4 = source switch
        {
            EmailConfigSource.Database => saved.TokenLast4,
            EmailConfigSource.Environment when o.ApiToken!.Trim().Length >= 4 => EmailDeliverySettings.Last4(o.ApiToken),
            _ => null,
        };
        return new EmailDeliveryDto(o.IsLive ? EmailDeliveryMode.Live : EmailDeliveryMode.Sandbox,
            string.IsNullOrWhiteSpace(o.InboxId) ? null : o.InboxId, fromEmail, fromName, replyTo,
            source != EmailConfigSource.None, last4, unreadable, source, saved.UpdatedAt);
    }

    private static (string Email, string Name) SplitAddress(string value)
    {
        try
        {
            var a = new MailAddress(value);
            return (a.Address, a.DisplayName);
        }
        catch (FormatException)
        {
            return (value, "");
        }
    }

    private static object Snapshot(EmailDeliverySettings e) => new
    {
        mode = e.Mode, inboxId = e.InboxId, fromEmail = e.FromEmail, fromName = e.FromName, replyTo = e.ReplyTo,
        tokenSet = e.HasToken, tokenLast4 = e.TokenLast4,
    };

    private static async Task<IResult> Get(
        HtnDbContext db, IOptions<MailtrapOptions> env, IEmailTokenProtector protector, CancellationToken ct)
    {
        var saved = await db.OrgSettings.AsNoTracking().Where(s => s.Id == OrgSettings.SingletonId).Select(s => s.Email).SingleAsync(ct);
        return TypedResults.Ok(Dto(saved, env.Value, protector));
    }

    private static async Task<IResult> Update(
        UpdateEmailDeliveryRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        IOptions<MailtrapOptions> env, IEmailTokenProtector protector, IMailtrapConfigProvider config, CancellationToken ct)
    {
        var s = await Load(db, ct);
        var before = Snapshot(s.Email);
        var beforeLast4 = s.Email.TokenLast4;
        var beforeProtected = s.Email.ProtectedApiToken;
        var now = clock.GetUtcNow();

        s.UpdateEmailDelivery(req.Mode!.Value, req.InboxId, req.FromEmail!, req.FromName!, req.ReplyTo, now, me.Id);
        if (req.ClearToken)
            s.ClearEmailApiToken(now, me.Id);
        else if (!string.IsNullOrWhiteSpace(req.ApiToken))
            s.SetEmailApiToken(protector.Protect(req.ApiToken), EmailDeliverySettings.Last4(req.ApiToken), now, me.Id);

        var tokenChanged = s.Email.ProtectedApiToken != beforeProtected;
        audit.Record(me.Id, AuditUpdate, nameof(OrgSettings), s.Id.ToString(CultureInfo.InvariantCulture), before, new
        {
            mode = s.Email.Mode, inboxId = s.Email.InboxId, fromEmail = s.Email.FromEmail, fromName = s.Email.FromName,
            replyTo = s.Email.ReplyTo, tokenSet = s.Email.HasToken, tokenLast4 = s.Email.TokenLast4, tokenChanged,
            tokenCleared = req.ClearToken && beforeLast4 is not null,
        });
        await db.SaveChangesAsync(ct);
        config.Invalidate();
        return TypedResults.Ok(Dto(s.Email, env.Value, protector));
    }

    /// <summary>Queues a test email through the outbox, so it goes the same way (and with the same settings) as real mail.</summary>
    private static async Task<IResult> SendTest(
        SendDeliveryTestRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, IConfiguration config,
        CancellationToken ct)
    {
        var to = req.To!.Trim();
        var domain = (config["Email:OrgDomain"] is { Length: > 0 } d ? d : DefaultOrgDomain).Trim().TrimStart('@');
        var own = me.Id is { } uid
            ? await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.Email).SingleOrDefaultAsync(ct)
            : null;
        var allowed = string.Equals(to, own, StringComparison.OrdinalIgnoreCase)
                      || to.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            return Problems.Field("to", $"Test emails can only go to your own address or an @{domain} address.");
        var requestedBy = me.PersonId is { } pid
            ? await db.People.AsNoTracking().Where(p => p.Id == pid).Select(p => p.FirstName).SingleOrDefaultAsync(ct)
            : null;
        var message = OutboxProcessor.DeliveryTestEmail(new DeliveryTestEmailPayload(to, requestedBy ?? "an admin"), clock.GetUtcNow());
        db.Outbox.Add(message);
        audit.Record(me.Id, AuditTest, nameof(OrgSettings), OrgSettings.SingletonId.ToString(CultureInfo.InvariantCulture), null,
            new { to = EmailMask.Mask(to), outboxId = message.Id });
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted((string?)null, new DeliveryTestQueuedDto(EmailMask.Mask(to)));
    }
}
