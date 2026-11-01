using System.Net;
using System.Text.Json;
using Htn.Api.Features.Agenda;
using Htn.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Htn.Domain.Organization;
using static Htn.Api.Tests.Committees.CommitteeTestData;
using static Htn.Api.Tests.Events.EventTestData;

namespace Htn.Api.Tests.Agenda;

public sealed class AgendaTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private sealed record Scene(ApiClient Board, ApiClient ProgramsLead, ApiClient LogisticsLead, ApiClient PlainGoon, Guid EventId);

    private static async Task<Scene> SceneAsync(HtnApiFactory f)
    {
        var (_, board) = await SignedInUserAsync(f, PersonLevel.Board);
        var (programs, programsLead) = await SignedInUserAsync(f, PersonLevel.Goon);
        var (logistics, logisticsLead) = await SignedInUserAsync(f, PersonLevel.Goon);
        var (_, plainGoon) = await SignedInUserAsync(f, PersonLevel.Goon);
        var eventId = await OnSaleAsync(board);

        var programsC = await AddCommitteeAsync(board, eventId, "Programs");
        await board.PostAsync($"/api/events/{eventId}/committees/{programsC}/members", new { personId = programs.PersonId });
        await SetLeadAsync(board, eventId, programsC, programs.PersonId);

        var logisticsC = await AddCommitteeAsync(board, eventId, "Logistics");
        await board.PostAsync($"/api/events/{eventId}/committees/{logisticsC}/members", new { personId = logistics.PersonId });
        await SetLeadAsync(board, eventId, logisticsC, logistics.PersonId);

        return new Scene(board, programsLead, logisticsLead, plainGoon, eventId);
    }

    private static async Task<Guid> AddRoomAsync(ApiClient c, Guid eventId, string name, string color = "#B71C1C")
    {
        var res = await c.PostAsync($"/api/events/{eventId}/rooms", new { name, colorHex = color, sortOrder = 0 });
        if (res.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"room: {res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await ApiClient.JsonAsync(res)).GetProperty("id").GetGuid();
    }

    private static object Session(Guid roomId, string title = "Opening remarks", string kind = "Talk", string start = "2026-11-22T09:00:00+08:00", int dur = 30) =>
        new { title, kind, startsAt = start, durationMinutes = dur, isPublic = true, roomIds = new[] { roomId }, speakers = Array.Empty<object>() };

    [Fact]
    public async Task Anyone_on_the_event_can_read_the_agenda_but_only_board_or_programs_lead_writes()
    {
        var s = await SceneAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await s.PlainGoon.GetAsync($"/api/events/{s.EventId}/agenda")).StatusCode);

        var roomId = await AddRoomAsync(s.Board, s.EventId, "Main Hall");

        // Programs lead may create a session.
        var byPrograms = await s.ProgramsLead.PostAsync($"/api/events/{s.EventId}/sessions", Session(roomId, start: "2026-11-22T09:00:00+08:00"));
        Assert.Equal(HttpStatusCode.OK, byPrograms.StatusCode);

        // A logistics lead and a plain goon may not.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.LogisticsLead.PostAsync($"/api/events/{s.EventId}/sessions", Session(roomId, start: "2026-11-22T13:00:00+08:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.PlainGoon.PostAsync($"/api/events/{s.EventId}/rooms", new { name = "X", colorHex = "#111111" })).StatusCode);
    }

    [Fact]
    public async Task A_room_cannot_hold_two_overlapping_sessions()
    {
        var s = await SceneAsync(factory);
        var roomId = await AddRoomAsync(s.Board, s.EventId, "Hall " + Guid.NewGuid().ToString("N")[..6]);
        (await s.Board.PostAsync($"/api/events/{s.EventId}/sessions", Session(roomId, start: "2026-11-22T10:00:00+08:00", dur: 60))).EnsureSuccessStatusCode();

        var clash = await s.Board.PostAsync($"/api/events/{s.EventId}/sessions", Session(roomId, title: "Clash", start: "2026-11-22T10:30:00+08:00", dur: 30));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(AgendaEndpoints.RoomBusy, (await ApiClient.JsonAsync(clash)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_non_break_session_needs_a_room()
    {
        var s = await SceneAsync(factory);
        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/sessions",
            new { title = "No room", kind = "Talk", startsAt = "2026-11-22T15:00:00+08:00", durationMinutes = 30, roomIds = Array.Empty<Guid>(), speakers = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_session_can_be_deleted()
    {
        var s = await SceneAsync(factory);
        var roomId = await AddRoomAsync(s.Board, s.EventId, "Del " + Guid.NewGuid().ToString("N")[..6]);
        var created = await s.Board.PostAsync($"/api/events/{s.EventId}/sessions", Session(roomId, start: "2026-11-22T16:00:00+08:00"));
        created.EnsureSuccessStatusCode();
        var id = (await ApiClient.JsonAsync(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await s.Board.DeleteAsync($"/api/events/{s.EventId}/sessions/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_sessions_owner_committee_must_belong_to_the_same_event()
    {
        var s = await SceneAsync(factory);
        var otherEvent = await OnSaleAsync(s.Board);
        var otherCommittee = await AddCommitteeAsync(s.Board, otherEvent, "Foreign " + Guid.NewGuid().ToString("N")[..6]);
        var roomId = await AddRoomAsync(s.Board, s.EventId, "Owner " + Guid.NewGuid().ToString("N")[..6]);

        var res = await s.Board.PostAsync($"/api/events/{s.EventId}/sessions", new
        {
            title = "Cross-event", kind = "Talk", startsAt = "2026-11-22T14:00:00+08:00", durationMinutes = 30,
            ownerCommitteeId = otherCommittee, roomIds = new[] { roomId }, speakers = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_voided_speaker_no_longer_appears_on_the_agenda()
    {
        var s = await SceneAsync(factory);
        var roomId = await AddRoomAsync(s.Board, s.EventId, "Void " + Guid.NewGuid().ToString("N")[..6]);
        var spRes = await s.Board.PostAsync($"/api/events/{s.EventId}/speakers", new { name = "Temp Speaker", email = $"temp.{Guid.NewGuid():N}@x.ph" });
        spRes.EnsureSuccessStatusCode();
        var speakerId = (await ApiClient.JsonAsync(spRes)).GetProperty("id").GetGuid();

        var created = await s.Board.PostAsync($"/api/events/{s.EventId}/sessions", new
        {
            title = "Voided speaker session", kind = "Talk", startsAt = "2026-11-22T17:00:00+08:00", durationMinutes = 30,
            roomIds = new[] { roomId }, speakers = new[] { new { speakerId, role = "Speaker", sortOrder = 0 } },
        });
        created.EnsureSuccessStatusCode();
        var sessionId = (await ApiClient.JsonAsync(created)).GetProperty("id").GetGuid();

        await factory.DbAsync(async db =>
        {
            var speaker = await db.Speakers.FirstAsync(sp => sp.Id == speakerId);
            speaker.Void("No longer available", Guid.NewGuid(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return 0;
        });

        var agenda = await ApiClient.JsonAsync(await s.Board.GetAsync($"/api/events/{s.EventId}/agenda"));
        var speakers = agenda.GetProperty("days").EnumerateArray()
            .SelectMany(d => d.GetProperty("slots").EnumerateArray())
            .SelectMany(sl => sl.GetProperty("sessions").EnumerateArray())
            .First(se => se.GetProperty("id").GetGuid() == sessionId)
            .GetProperty("speakers");
        Assert.Equal(0, speakers.GetArrayLength());
    }

    [Fact]
    public async Task A_session_with_an_unconfirmed_speaker_reads_as_tentative()
    {
        var s = await SceneAsync(factory);
        var roomId = await AddRoomAsync(s.Board, s.EventId, "Tent " + Guid.NewGuid().ToString("N")[..6]);
        // A directly-created speaker is Invited (not Confirmed), so a session with them is tentative.
        var spRes = await s.Board.PostAsync($"/api/events/{s.EventId}/speakers", new { name = "Mae Torres", email = $"mae.{Guid.NewGuid():N}@devtools.io" });
        spRes.EnsureSuccessStatusCode();
        var speakerId = (await ApiClient.JsonAsync(spRes)).GetProperty("id").GetGuid();

        var created = await s.Board.PostAsync($"/api/events/{s.EventId}/sessions", new
        {
            title = "Workshop", kind = "Workshop", startsAt = "2026-11-22T11:00:00+08:00", durationMinutes = 90,
            roomIds = new[] { roomId }, speakers = new[] { new { speakerId, role = "Speaker", sortOrder = 0 } },
        });
        created.EnsureSuccessStatusCode();
        Assert.True((await ApiClient.JsonAsync(created)).GetProperty("tentative").GetBoolean());
    }
}
