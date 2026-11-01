using Htn.Domain.Common;

namespace Htn.Domain.PopUps;

/// <summary>
/// One cashier assigned to run a pop-up's POS (<c>PopUps.Sell</c>'s "on the store team" carve-out), mirroring
/// <c>Htn.Domain.Committees.CommitteeMember</c>'s shape but simpler: there is no lead/member role distinction here,
/// and no "leave" — a pop-up's team is fixed while it is <see cref="PopUpStatus.Planned"/>/<see cref="PopUpStatus.Open"/>
/// and the row becomes permanent once the pop-up leaves <see cref="PopUpStatus.Planned"/> (<c>PopUps_Guards</c>).
/// </summary>
public sealed class PopUpTeamMember
{
    private PopUpTeamMember() { }

    public Guid Id { get; private set; }
    public Guid PopUpId { get; private set; }
    public Guid PersonId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static PopUpTeamMember Create(Guid popUpId, Guid personId, DateTimeOffset at)
    {
        if (popUpId == Guid.Empty) throw new DomainException("A pop-up is required.");
        if (personId == Guid.Empty) throw new DomainException("A person is required.");
        return new PopUpTeamMember
        {
            Id = Guid.CreateVersion7(at),
            PopUpId = popUpId,
            PersonId = personId,
            CreatedAt = at,
        };
    }
}
