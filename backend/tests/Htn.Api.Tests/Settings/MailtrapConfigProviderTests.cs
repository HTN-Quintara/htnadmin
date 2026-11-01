using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Settings;
using Htn.Infrastructure.Email;

namespace Htn.Api.Tests.Settings;

public sealed class MailtrapConfigProviderTests
{
    private static readonly MailtrapOptions Env = new()
    {
        ApiToken = "env-token-0123456789", Mode = "sandbox", InboxId = "111", From = "Env <env@example.ph>", ReplyTo = "reply@example.ph",
    };

    private static EmailDeliverySettings Saved(bool withToken)
    {
        var s = OrgSettings.Defaults();
        s.UpdateEmailDelivery(EmailDeliveryMode.Live, null, "db@example.ph", "Db Sender", null, DateTimeOffset.UtcNow, null);
        if (withToken) s.SetEmailApiToken("p:db-token-0123456789", "6789", DateTimeOffset.UtcNow, null);
        return s.Email;
    }

    [Fact]
    public void Database_token_and_fields_win_over_environment()
    {
        var (o, source, _) = MailtrapConfigProvider.Resolve(Saved(withToken: true), Env, new FakeTokenProtector());
        Assert.Equal(EmailConfigSource.Database, source);
        Assert.Equal("db-token-0123456789", o.ApiToken);
        Assert.True(o.IsLive);
        Assert.Equal("db@example.ph", o.FromAddress().Address);
        Assert.Equal("Db Sender", o.FromAddress().DisplayName);
        Assert.Null(o.ReplyTo);
    }

    [Fact]
    public void Environment_applies_when_nothing_is_saved()
    {
        var (o, source, _) = MailtrapConfigProvider.Resolve(EmailDeliverySettings.Default(), Env, new FakeTokenProtector());
        Assert.Equal(EmailConfigSource.Environment, source);
        Assert.Equal(Env.ApiToken, o.ApiToken);
        Assert.Equal("111", o.InboxId);
        Assert.Equal("env@example.ph", o.FromAddress().Address);
    }

    [Fact]
    public void Saved_fields_without_a_token_use_the_environment_token()
    {
        var (o, source, _) = MailtrapConfigProvider.Resolve(Saved(withToken: false), Env, new FakeTokenProtector());
        Assert.Equal(EmailConfigSource.Environment, source);
        Assert.Equal(Env.ApiToken, o.ApiToken);
        Assert.Equal("db@example.ph", o.FromAddress().Address);
    }

    [Fact]
    public void Unreadable_database_token_falls_back_and_no_token_anywhere_is_none()
    {
        var s = OrgSettings.Defaults();
        s.UpdateEmailDelivery(EmailDeliveryMode.Live, null, "db@example.ph", "Db", null, DateTimeOffset.UtcNow, null);
        s.SetEmailApiToken("bad:cipher", "abcd", DateTimeOffset.UtcNow, null);
        // Saved mode is live, env is sandbox: no fallback (see Unreadable_token_uses_env_only_when_the_mode_matches).
        Assert.Equal(EmailConfigSource.None, MailtrapConfigProvider.Resolve(s.Email, Env, new FakeTokenProtector()).Source);

        var none = MailtrapConfigProvider.Resolve(EmailDeliverySettings.Default(), new MailtrapOptions(), new FakeTokenProtector());
        Assert.Equal(EmailConfigSource.None, none.Source);
        Assert.False(none.Options.IsConfigured);
    }

    [Fact]
    public void Unreadable_token_uses_env_only_when_the_mode_matches()
    {
        var live = OrgSettings.Defaults();
        live.UpdateEmailDelivery(EmailDeliveryMode.Live, null, "db@example.ph", "Db", null, DateTimeOffset.UtcNow, null);
        live.SetEmailApiToken("bad:cipher", "abcd", DateTimeOffset.UtcNow, null);

        // Env is sandbox, saved is live: never send a sandbox token to the live endpoint.
        var mismatch = MailtrapConfigProvider.Resolve(live.Email, Env, new FakeTokenProtector());
        Assert.Equal(EmailConfigSource.None, mismatch.Source);
        Assert.True(mismatch.TokenUnreadable);
        Assert.False(mismatch.Options.IsConfigured);

        var liveEnv = new MailtrapOptions { ApiToken = "env-live-token-0123456789", Mode = "live" };
        var match = MailtrapConfigProvider.Resolve(live.Email, liveEnv, new FakeTokenProtector());
        Assert.Equal(EmailConfigSource.Environment, match.Source);
        Assert.True(match.TokenUnreadable);
        Assert.Equal(liveEnv.ApiToken, match.Options.ApiToken);

        Assert.False(MailtrapConfigProvider.Resolve(Saved(withToken: true), Env, new FakeTokenProtector()).TokenUnreadable);
    }

    [Fact]
    public void Mailtrap_error_summary_masks_addresses_and_drops_raw_bodies()
    {
        var s = MailtrapEmailSender.ErrorSummary("{\"success\":false,\"errors\":[\"Recipient mika@hackthenorth.ph is not allowed\"]}");
        Assert.Contains("m•••@hackthenorth.ph", s, StringComparison.Ordinal);
        Assert.DoesNotContain("mika@", s, StringComparison.Ordinal);
        Assert.Equal("no error details", MailtrapEmailSender.ErrorSummary("<html>secret a@b.ph</html>"));
        Assert.True(MailtrapEmailSender.ErrorSummary("{\"error\":\"" + new string('x', 900) + "\"}").Length <= MailtrapEmailSender.ErrorSummaryMaxLength + 1);
    }

    [Fact]
    public void Sandbox_needs_an_inbox()
    {
        var s = OrgSettings.Defaults();
        Assert.Throws<Htn.Domain.Common.DomainException>(() =>
            s.UpdateEmailDelivery(EmailDeliveryMode.Sandbox, null, "a@example.ph", "A", null, DateTimeOffset.UtcNow, null));
    }
}
