using Htn.Api.Common;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Auth;

/// <summary>What a pop-up-scoped check is asking about: <c>PopUps.Manage</c> (create/run) or <c>PopUps.Sell</c> (POS).</summary>
public enum PopUpAccess
{
    Manage,
    Sell,
}

public sealed class PopUpScopedRequirement(PopUpAccess access) : IAuthorizationRequirement
{
    public PopUpAccess Access { get; } = access;
}

/// <summary>
/// The authorization resource for a pop-up-scoped check, once the row is loaded. <see cref="OwnerCommitteeId"/>
/// drives the "lead of the owning committee" branch (both <c>Manage</c> and <c>Sell</c>, but only when
/// <see cref="CommitteeAllowed"/> — per permissions.md, both policies' lead branch is conditioned on "if committee
/// [still] allowed"); <see cref="PopUpId"/> additionally drives <c>Sell</c>'s "on the store team" branch, which is
/// unconditional on the committee flag (the team was assigned back when the pop-up was created).
/// </summary>
public sealed record PopUpScope(Guid OwnerCommitteeId, Guid PopUpId, bool CommitteeAllowed);

/// <summary>
/// Who is on a pop-up's cashier team (<c>pop_up_team_members</c>), keyed off the caller's <see cref="ICurrentUser.PersonId"/>.
/// A separate, tiny service from <see cref="ICommitteeContext"/> because team membership isn't a committee concept.
/// </summary>
public interface IPopUpTeamContext
{
    Task<bool> IsOnTeamAsync(Guid popUpId, CancellationToken ct = default);

    /// <summary>"May be allowed" for the resource-less route gate: on at least one pop-up's team, anywhere.</summary>
    Task<bool> IsOnAnyTeamAsync(CancellationToken ct = default);
}

public sealed class PopUpTeamContext(HtnDbContext db, ICurrentUser me) : IPopUpTeamContext
{
    public Task<bool> IsOnTeamAsync(Guid popUpId, CancellationToken ct = default) =>
        me.PersonId is { } personId
            ? db.PopUpTeamMembers.AsNoTracking().AnyAsync(t => t.PopUpId == popUpId && t.PersonId == personId, ct)
            : Task.FromResult(false);

    public Task<bool> IsOnAnyTeamAsync(CancellationToken ct = default) =>
        me.PersonId is { } personId
            ? db.PopUpTeamMembers.AsNoTracking().AnyAsync(t => t.PersonId == personId, ct)
            : Task.FromResult(false);
}

/// <summary>
/// The handler behind <c>PopUps.Manage</c>/<c>PopUps.Sell</c>. Board always succeeds. Otherwise, the same two-layer
/// shape as <c>Suppliers.Rate</c> (<see cref="CommitteeScopedHandler"/>'s remarks): the route carries the coarse
/// "may be allowed" gate (no resource — leads something, or for <c>Sell</c>, is on some pop-up's team), and the
/// endpoint re-runs the policy with the real <see cref="PopUpScope"/> once the row is loaded, which is where
/// <c>Sell</c>'s third "on the team" branch is actually decided precisely (lead of some *other* allowed committee
/// passes the coarse gate but fails the precise one, exactly like a lead of the wrong committee fails
/// <c>Suppliers.Rate</c>'s step 2).
/// </summary>
public sealed class PopUpScopedHandler(ICommitteeContext committees, IPopUpTeamContext team)
    : AuthorizationHandler<PopUpScopedRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PopUpScopedRequirement requirement)
    {
        if (context.User.IsInRole(Roles.Board))
        {
            context.Succeed(requirement);
            return;
        }

        var granted = context.Resource switch
        {
            PopUpScope s => (s.CommitteeAllowed && await committees.IsLeadOfAsync(s.OwnerCommitteeId))
                || (requirement.Access == PopUpAccess.Sell && await team.IsOnTeamAsync(s.PopUpId)),
            // No resource: the route-level gate and nothing more precise is known yet. "May be allowed".
            _ => await committees.LeadsAnythingAsync()
                || (requirement.Access == PopUpAccess.Sell && await team.IsOnAnyTeamAsync()),
        };
        if (granted) context.Succeed(requirement);
    }
}
