using System.Net.Mail;
using System.Security.Cryptography;
using Htn.Domain.Settings;
using Htn.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Email;

/// <summary>Encrypts the Mailtrap API token for storage in <c>org_settings</c>. The raw token never leaves memory otherwise.</summary>
public interface IEmailTokenProtector
{
    string Protect(string rawToken);

    /// <returns>The raw token, or null when the ciphertext can't be read (e.g. the Data Protection keys were lost).</returns>
    string? TryUnprotect(string protectedToken);
}

public sealed class DataProtectionEmailTokenProtector(IDataProtectionProvider provider, ILogger<DataProtectionEmailTokenProtector> logger)
    : IEmailTokenProtector
{
    public const string Purpose = "Htn.Email.MailtrapToken.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string rawToken) => _protector.Protect(rawToken.Trim());

    public string? TryUnprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "The stored Mailtrap API token can't be decrypted; falling back to MAILTRAP_API_TOKEN.");
            return null;
        }
    }
}

/// <summary>Where the effective Mailtrap token comes from.</summary>
public enum EmailConfigSource
{
    Database,
    Environment,
    None,
}

/// <param name="TokenUnreadable">A token is saved but can't be decrypted (e.g. Data Protection keys lost or rotated away).</param>
public sealed record EffectiveMailtrapConfig(MailtrapOptions Options, EmailConfigSource Source, bool TokenUnreadable = false);

/// <summary>Resolves the Mailtrap settings in effect: the board's saved settings, with the environment as fallback.</summary>
public interface IMailtrapConfigProvider
{
    /// <summary>Cached for <see cref="MailtrapConfigProvider.CacheFor"/>; <see cref="Invalidate"/> after a save.</summary>
    Task<EffectiveMailtrapConfig> GetAsync(CancellationToken ct);

    void Invalidate();
}

public sealed class MailtrapConfigProvider(
    IServiceScopeFactory scopes,
    IOptions<MailtrapOptions> env,
    IEmailTokenProtector protector,
    TimeProvider clock) : IMailtrapConfigProvider
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Cached? _cached;

    private sealed record Cached(EffectiveMailtrapConfig Config, DateTimeOffset Until);

    public async Task<EffectiveMailtrapConfig> GetAsync(CancellationToken ct)
    {
        if (_cached is { } c && c.Until > clock.GetUtcNow()) return c.Config;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is { } again && again.Until > clock.GetUtcNow()) return again.Config;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HtnDbContext>();
            var saved = await db.OrgSettings.AsNoTracking().Where(s => s.Id == OrgSettings.SingletonId).Select(s => s.Email).SingleOrDefaultAsync(ct);
            var config = Resolve(saved, env.Value, protector);
            _cached = new Cached(config, clock.GetUtcNow() + CacheFor);
            return config;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cached = null;

    /// <summary>
    /// Saved fields (mode, inbox, sender, reply-to) apply once an admin has saved them. The token is the saved one when
    /// present and readable, else MAILTRAP_API_TOKEN. Nothing saved: the environment as before. A saved token that can't
    /// be decrypted falls back to MAILTRAP_API_TOKEN only when MAILTRAP_MODE matches the saved mode (a sandbox token
    /// must never be sent to the live endpoint, or the other way round); otherwise there is no token.
    /// </summary>
    public static EffectiveMailtrapConfig Resolve(EmailDeliverySettings? saved, MailtrapOptions env, IEmailTokenProtector protector)
    {
        var dbToken = saved is { HasToken: true } ? protector.TryUnprotect(saved.ProtectedApiToken!) : null;
        var unreadable = saved is { HasToken: true } && string.IsNullOrWhiteSpace(dbToken);
        var useDbFields = saved is not null && (saved.IsSaved || dbToken is not null);
        var options = useDbFields
            ? new MailtrapOptions
            {
                Mode = saved!.Mode == EmailDeliveryMode.Live ? "live" : "sandbox",
                InboxId = saved.InboxId,
                From = new MailAddress(saved.FromEmail, saved.FromName).ToString(),
                ReplyTo = saved.ReplyTo,
            }
            : new MailtrapOptions { Mode = env.Mode, InboxId = env.InboxId, From = env.From, ReplyTo = env.ReplyTo };

        if (!string.IsNullOrWhiteSpace(dbToken))
        {
            options.ApiToken = dbToken;
            return new EffectiveMailtrapConfig(options, EmailConfigSource.Database);
        }
        var envUsable = env.IsConfigured && (!unreadable || env.IsLive == (saved!.Mode == EmailDeliveryMode.Live));
        if (envUsable)
        {
            options.ApiToken = env.ApiToken;
            return new EffectiveMailtrapConfig(options, EmailConfigSource.Environment, unreadable);
        }
        return new EffectiveMailtrapConfig(options, EmailConfigSource.None, unreadable);
    }
}
