using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Invitations.InvitationsTestData;

namespace Htn.Api.Tests.Invitations;

/// <summary>
/// The database half of the invitation rules, exercised as if the API had been bypassed entirely — a
/// hand-written <c>UPDATE</c>/<c>DELETE</c> at a psql prompt. Mirrors <c>DirectoryIntegrityGuardsTests</c>.
/// </summary>
public sealed class InvitationsIntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    private async Task<Guid> SentInvitationIdAsync(InvitationsScene s)
    {
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SetTemplateAsync(s.Board, s.EventId);
        var send = await s.Board.PostAsync($"{InvitationsUrl(s.EventId)}/{invitationId}/send");
        send.EnsureSuccessStatusCode();
        return invitationId;
    }

    [Fact]
    public async Task A_sent_invitation_can_never_be_hard_deleted()
    {
        var s = await SceneAsync(factory);
        var invitationId = await SentInvitationIdAsync(s);
        await AssertRejectedAsync($"DELETE FROM event_invitations WHERE id = {invitationId}", "never deleted");
    }

    [Fact]
    public async Task A_not_sent_invitation_can_be_hard_deleted_directly()
    {
        var s = await SceneAsync(factory);
        var (orgId, _) = await VerifiedOrgAsync(s.Board);
        var invitationId = await CreateInvitationAsync(s.Board, s.EventId, orgId);
        await SqlAsync($"DELETE FROM event_invitations WHERE id = {invitationId}");
        var remaining = await factory.DbAsync(db => db.EventInvitations.AsNoTracking().CountAsync(i => i.Id == invitationId));
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task The_sent_snapshot_fields_are_frozen_once_set()
    {
        var s = await SceneAsync(factory);
        var invitationId = await SentInvitationIdAsync(s);
        await AssertRejectedAsync(
            $"UPDATE event_invitations SET letter_subject_snapshot = 'Hacked' WHERE id = {invitationId}", "frozen");
        await AssertRejectedAsync(
            $"UPDATE event_invitations SET sent_at = now() WHERE id = {invitationId}", "frozen");
    }

    [Fact]
    public async Task The_status_transition_whitelist_is_enforced_at_the_database_too()
    {
        var s = await SceneAsync(factory);
        var invitationId = await SentInvitationIdAsync(s);
        await SqlAsync($"UPDATE event_invitations SET status = 'Confirmed' WHERE id = {invitationId}");
        await AssertRejectedAsync($"UPDATE event_invitations SET status = 'Declined' WHERE id = {invitationId}");
    }

    [Fact]
    public async Task An_invitation_event_row_is_never_updated_or_deleted()
    {
        var s = await SceneAsync(factory);
        var invitationId = await SentInvitationIdAsync(s);
        var eventRowId = await factory.DbAsync(db => db.InvitationEvents.AsNoTracking()
            .Where(e => e.InvitationId == invitationId).Select(e => e.Id).FirstAsync());

        await AssertRejectedAsync($"UPDATE invitation_events SET note = 'x' WHERE id = {eventRowId}", "append-only");
        await AssertRejectedAsync($"DELETE FROM invitation_events WHERE id = {eventRowId}", "append-only");
    }
}
