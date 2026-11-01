using Htn.Infrastructure.Email;

namespace Htn.Api.Tests.Infrastructure;

/// <summary>A config provider that always returns the given options (environment source).</summary>
public sealed class FixedMailtrapConfig(MailtrapOptions options) : IMailtrapConfigProvider
{
    public Task<EffectiveMailtrapConfig> GetAsync(CancellationToken ct) =>
        Task.FromResult(new EffectiveMailtrapConfig(options, options.IsConfigured ? EmailConfigSource.Environment : EmailConfigSource.None));

    public void Invalidate() { }
}

/// <summary>Reversible fake "encryption" for unit tests; <c>bad:</c> prefixes simulate an unreadable ciphertext.</summary>
public sealed class FakeTokenProtector : IEmailTokenProtector
{
    public string Protect(string rawToken) => "p:" + rawToken;
    public string? TryUnprotect(string protectedToken) => protectedToken.StartsWith("p:", StringComparison.Ordinal) ? protectedToken[2..] : null;
}
