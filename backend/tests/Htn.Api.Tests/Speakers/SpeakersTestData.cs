using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Speakers;

internal sealed record SpeakersScene(ApiClient Board, ApiClient ProgramsLead, ApiClient LogisticsLead, ApiClient Goon, Guid EventId, Guid ProgramsCommitteeId);

internal static partial class SpeakersTestData
{
    public static async Task<SpeakersScene> SceneAsync(HtnApiFactory f)
    {
        var (_, board) = await SignedInUserAsync(f, PersonLevel.Board);
        var (programs, programsLead) = await SignedInUserAsync(f, PersonLevel.Goon);
        var (logistics, logisticsLead) = await SignedInUserAsync(f, PersonLevel.Goon);
        var (_, goon) = await SignedInUserAsync(f, PersonLevel.Goon);
        var eventId = await OnSaleAsync(board);

        var programsC = await AddCommitteeAsync(board, eventId, "Programs");
        await board.PostAsync($"/api/events/{eventId}/committees/{programsC}/members", new { personId = programs.PersonId });
        await SetLeadAsync(board, eventId, programsC, programs.PersonId);
        var logisticsC = await AddCommitteeAsync(board, eventId, "Logistics");
        await board.PostAsync($"/api/events/{eventId}/committees/{logisticsC}/members", new { personId = logistics.PersonId });
        await SetLeadAsync(board, eventId, logisticsC, logistics.PersonId);

        return new SpeakersScene(board, programsLead, logisticsLead, goon, eventId, programsC);
    }

    public static async Task<Guid> CreateSpeakerAsync(ApiClient client, Guid eventId, string? name = null, string? email = null)
    {
        var res = await client.PostAsync($"/api/events/{eventId}/speakers",
            new { name = name ?? "Dr. Lara Gomez", email = email ?? $"lara.{Guid.NewGuid():N}@dict.gov.ph" });
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Create speaker failed: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    public static async Task<string> IssueLinkAsync(ApiClient board, Guid eventId, Guid speakerId, HtnApiFactory factory)
    {
        var res = await board.PostAsync($"/api/events/{eventId}/agenda/speakers/{speakerId}/link");
        res.EnsureSuccessStatusCode();
        await factory.ProcessOutboxAsync();
        return LastLinkToken(factory);
    }

    public static string LastLinkToken(HtnApiFactory factory)
    {
        var mail = factory.Emails.Sent.Last(m => m.Category == "speaker-link");
        var match = TokenInLink().Match(mail.Text);
        if (!match.Success) throw new InvalidOperationException("No link token found in email: " + mail.Text);
        return match.Groups[1].Value;
    }

    public static async Task<string> LastOtpCodeAsync(HtnApiFactory factory)
    {
        await factory.ProcessOutboxAsync();
        var mail = factory.Emails.Sent.Last(m => m.Category == "speaker-otp");
        var match = CodeInBody().Match(mail.Text);
        if (!match.Success) throw new InvalidOperationException("No OTP code found in email: " + mail.Text);
        return match.Groups[1].Value;
    }

    [GeneratedRegex(@"/s/([A-Za-z0-9_-]+)")]
    private static partial Regex TokenInLink();

    [GeneratedRegex(@"code is: (\d{6})")]
    private static partial Regex CodeInBody();

}
