using System.Net;
using System.Text.Json;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using Htn.Domain.Settings;
using Htn.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Tests.Settings;

public sealed class EmailDeliveryEndpointsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private const string Token = "mt-live-token-abcdef0123456789WXYZ";

    private static object Body(string mode = "Live", string? inboxId = null, string fromEmail = "no-reply@hackthenorth.ph",
        string fromName = "HackTheNorth.PH", string? replyTo = "team@hackthenorth.ph", string? apiToken = null, bool clearToken = false) =>
        new { mode, inboxId, fromEmail, fromName, replyTo, apiToken, clearToken };

    [Fact]
    public async Task Non_managers_are_forbidden_on_every_route()
    {
        foreach (var level in new[] { PersonLevel.Goon, PersonLevel.Apprentice })
        {
            var client = await factory.SignedInAsync(level);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/settings/email")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("/api/settings/email", Body(apiToken: Token))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/settings/email/test", new { to = "a@example.ph" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/settings/email")).StatusCode);
    }

    [Fact]
    public async Task Put_round_trips_and_never_returns_the_token()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var put = await board.PutAsync("/api/settings/email", Body(apiToken: Token));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putText = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, putText, StringComparison.Ordinal);

        var res = await board.GetAsync("/api/settings/email");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("protected", text, StringComparison.OrdinalIgnoreCase);
        var dto = await ApiClient.JsonAsync(res);
        Assert.Equal("Live", dto.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("inboxId").ValueKind);
        Assert.Equal("no-reply@hackthenorth.ph", dto.GetProperty("fromEmail").GetString());
        Assert.Equal("HackTheNorth.PH", dto.GetProperty("fromName").GetString());
        Assert.Equal("team@hackthenorth.ph", dto.GetProperty("replyTo").GetString());
        Assert.True(dto.GetProperty("tokenSet").GetBoolean());
        Assert.Equal("WXYZ", dto.GetProperty("tokenLast4").GetString());
        Assert.Equal("Database", dto.GetProperty("source").GetString());
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("updatedAt").ValueKind);

        // Stored encrypted, not in clear.
        var stored = await factory.DbAsync(db => db.OrgSettings.AsNoTracking().Select(s => s.Email.ProtectedApiToken).SingleAsync());
        Assert.NotNull(stored);
        Assert.DoesNotContain(Token, stored, StringComparison.Ordinal);

        // Blank token keeps the saved one; switching to sandbox with an inbox works.
        var keep = await ApiClient.JsonAsync(await board.PutAsync("/api/settings/email", Body(mode: "Sandbox", inboxId: "123456", apiToken: "")));
        Assert.Equal("Sandbox", keep.GetProperty("mode").GetString());
        Assert.Equal("123456", keep.GetProperty("inboxId").GetString());
        Assert.Equal("WXYZ", keep.GetProperty("tokenLast4").GetString());

        // Clearing falls back to the environment (none configured in tests).
        var cleared = await ApiClient.JsonAsync(await board.PutAsync("/api/settings/email", Body(clearToken: true)));
        Assert.False(cleared.GetProperty("tokenSet").GetBoolean());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("tokenLast4").ValueKind);
        Assert.Equal("None", cleared.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Validation_rejects_sandbox_without_inbox_and_bad_input()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PutAsync("/api/settings/email", Body(mode: "Sandbox", inboxId: null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PutAsync("/api/settings/email", Body(fromEmail: "not-an-email"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PutAsync("/api/settings/email", Body(fromName: ""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PutAsync("/api/settings/email", Body(replyTo: "nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PutAsync("/api/settings/email", Body(apiToken: "short"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PutAsync("/api/settings/email", Body(apiToken: Token, clearToken: true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await board.PostAsync("/api/settings/email/test", new { to = "nope" })).StatusCode);
    }

    [Fact]
    public async Task Update_is_audited_without_the_token()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        Assert.Equal(HttpStatusCode.OK, (await board.PutAsync("/api/settings/email", Body(apiToken: Token))).StatusCode);
        var audit = (await factory.DbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Action == "settings.email_update")
            .OrderBy(a => a.At).ToListAsync()))[^1];
        Assert.DoesNotContain(Token, audit.AfterJson ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(Token, audit.BeforeJson ?? "", StringComparison.Ordinal);
        Assert.True(JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("tokenChanged").GetBoolean());
        Assert.Contains("WXYZ", audit.AfterJson, StringComparison.Ordinal);
        Assert.DoesNotContain("protected", audit.AfterJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_send_queues_an_outbox_email()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var to = $"delivery.{Guid.NewGuid():N}@hackthenorth.ph";
        var res = await board.PostAsync("/api/settings/email/test", new { to });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal(Htn.Domain.Common.EmailMask.Mask(to), (await ApiClient.JsonAsync(res)).GetProperty("to").GetString());

        var queued = await factory.DbAsync(db => db.Outbox.AsNoTracking().Where(m => m.Type == EmailTypes.DeliveryTest).Select(m => m.PayloadJson).ToListAsync());
        Assert.Single(queued, p => p.Contains(to, StringComparison.Ordinal));

        await factory.ProcessOutboxAsync();
        var sent = Assert.Single(factory.Emails.To(to));
        Assert.Equal(EmailCategories.Test, sent.Category);
        Assert.Contains("delivery", sent.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_send_only_goes_to_own_or_org_address_and_is_rate_limited()
    {
        var user = await factory.CreateUserAsync(PersonLevel.Board);
        var board = factory.NewClient();
        await board.LoginOkAsync(user.Email, HtnApiFactory.Password);

        var other = await board.PostAsync("/api/settings/email/test", new { to = "stranger@gmail.com" });
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        var errors = (await ApiClient.JsonAsync(other)).GetProperty("errors").GetProperty("to");
        Assert.Equal("Test emails can only go to your own address or an @hackthenorth.ph address.", errors[0].GetString());

        // Own address (example.ph in tests) is fine; that is request 2 of 5 this hour.
        Assert.Equal(HttpStatusCode.Accepted, (await board.PostAsync("/api/settings/email/test", new { to = user.Email })).StatusCode);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await board.PostAsync("/api/settings/email/test", new { to = "ops@hackthenorth.ph" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await board.PostAsync("/api/settings/email/test", new { to = "ops@hackthenorth.ph" })).StatusCode);
    }

    [Fact]
    public async Task Get_reports_token_unreadable_flag()
    {
        var board = await factory.SignedInAsync(PersonLevel.Board);
        var dto = await ApiClient.JsonAsync(await board.GetAsync("/api/settings/email"));
        Assert.Equal(JsonValueKind.False, dto.GetProperty("tokenUnreadable").ValueKind);
    }

    [Fact]
    public void Mode_enum_names_are_the_contract()
    {
        Assert.Equal(["Sandbox", "Live"], Enum.GetNames<EmailDeliveryMode>());
        Assert.Equal(["Database", "Environment", "None"], Enum.GetNames<EmailConfigSource>());
    }
}
