using Htn.Domain.Speakers;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Email;

/// <summary>
/// The three speaker emails, built from current database state when the outbox sends them. Each returns null when
/// the message no longer applies (the link was revoked, the plus-one was removed), so a stale link or code is never
/// delivered. The raw token/OTP travel only in <c>OutboxMessage.Secret</c>, never in the payload.
/// </summary>
public sealed class SpeakerEmails(HtnDbContext db, EmailRenderer renderer, IOptions<AppLinkOptions> links)
{
    /// <summary>The initial link, or a resend. <paramref name="rawToken"/> comes from the outbox's Secret, not the payload.</summary>
    public async Task<EmailMessage?> SpeakerLinkAsync(SpeakerLinkEmailPayload p, string rawToken)
    {
        var row = await LinkAsync(p.LinkId);
        if (row is null || row.Link.IsRevoked) return null;

        var link = links.Value.SpeakerForm(rawToken);
        var logo = await renderer.LogoAsync();
        var firstName = FirstName(row.Speaker.PersonName);
        var html = await renderer.RenderAsync<SpeakerLinkEmail>(new Dictionary<string, object?>
        {
            [nameof(SpeakerLinkEmail.FirstName)] = firstName,
            [nameof(SpeakerLinkEmail.EventName)] = row.EventName,
            [nameof(SpeakerLinkEmail.Link)] = link,
            [nameof(SpeakerLinkEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(SpeakerLinkEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {firstName},

            Thank you for speaking at {row.EventName}. Please confirm your travel details using the private link below:
            {link}

            This link is only for you and expires in 14 days.

            {Brand.OrgName}
            """;
        return new EmailMessage(row.Speaker.Email, firstName, $"Confirm your details for {row.EventName}", html, text,
            EmailCategories.SpeakerLink, logo is null ? null : [logo]);
    }

    public async Task<EmailMessage?> SpeakerOtpAsync(SpeakerOtpEmailPayload p, string code)
    {
        var row = await LinkAsync(p.LinkId);
        if (row is null || row.Link.IsRevoked) return null;

        var logo = await renderer.LogoAsync();
        var firstName = FirstName(row.Speaker.PersonName);
        var html = await renderer.RenderAsync<SpeakerOtpEmail>(new Dictionary<string, object?>
        {
            [nameof(SpeakerOtpEmail.FirstName)] = firstName,
            [nameof(SpeakerOtpEmail.EventName)] = row.EventName,
            [nameof(SpeakerOtpEmail.Code)] = code,
            [nameof(SpeakerOtpEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(SpeakerOtpEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {firstName},

            Your verification code is: {code}

            It expires in 10 minutes.

            {Brand.OrgName}
            """;
        return new EmailMessage(row.Speaker.Email, firstName, "Your verification code", html, text,
            EmailCategories.SpeakerOtp, logo is null ? null : [logo]);
    }

    public async Task<EmailMessage?> PlusOneVoucherAsync(PlusOneVoucherEmailPayload p)
    {
        var plusOne = await db.SpeakerPlusOnes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == p.PlusOneId);
        if (plusOne is null || plusOne.IsRemoved) return null;
        var speaker = await db.Speakers.AsNoTracking().SingleOrDefaultAsync(sp => sp.Id == plusOne.SpeakerId);
        if (speaker is null) return null;
        var eventName = await db.Events.AsNoTracking().Where(e => e.Id == plusOne.EventId).Select(e => e.Name).SingleOrDefaultAsync();
        if (eventName is null) return null;
        var voucherCode = plusOne.VoucherId is { } vid
            ? await db.Vouchers.AsNoTracking().Where(v => v.Id == vid).Select(v => v.Code).SingleOrDefaultAsync()
            : null;
        if (voucherCode is null) return null;

        var slug = await db.Events.AsNoTracking().Where(e => e.Id == plusOne.EventId).Select(e => e.Slug).SingleOrDefaultAsync();
        var link = slug is null ? "" : links.Value.Register(slug);
        var logo = await renderer.LogoAsync();
        var firstName = FirstName(plusOne.Name);
        var speakerFirstName = FirstName(speaker.PersonName);
        var html = await renderer.RenderAsync<PlusOneVoucherEmail>(new Dictionary<string, object?>
        {
            [nameof(PlusOneVoucherEmail.FirstName)] = firstName,
            [nameof(PlusOneVoucherEmail.SpeakerName)] = speaker.PersonName,
            [nameof(PlusOneVoucherEmail.SpeakerFirstName)] = speakerFirstName,
            [nameof(PlusOneVoucherEmail.EventName)] = eventName,
            [nameof(PlusOneVoucherEmail.VoucherCode)] = voucherCode,
            [nameof(PlusOneVoucherEmail.RegisterLink)] = link,
            [nameof(PlusOneVoucherEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(PlusOneVoucherEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {firstName},

            {speaker.PersonName} is speaking at {eventName} and invited you as their plus-one. Your accommodation is
            free, shared with {speaker.PersonName}; your ticket is 50% off with this voucher:

            {voucherCode}

            Register:
            {link}

            {Brand.OrgName}
            """;
        return new EmailMessage(plusOne.Email, firstName, $"Your 50% off voucher for {eventName}", html, text,
            EmailCategories.Voucher, logo is null ? null : [logo]);
    }

    private sealed record LinkRow(SpeakerLink Link, Speaker Speaker, string EventName);

    private async Task<LinkRow?> LinkAsync(Guid linkId)
    {
        var link = await db.SpeakerLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId);
        if (link is null) return null;
        var speaker = await db.Speakers.AsNoTracking().SingleOrDefaultAsync(sp => sp.Id == link.SpeakerId);
        if (speaker is null) return null;
        var eventName = await db.Events.AsNoTracking().Where(e => e.Id == link.EventId).Select(e => e.Name).SingleOrDefaultAsync();
        return eventName is null ? null : new LinkRow(link, speaker, eventName);
    }

    private static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }
}
