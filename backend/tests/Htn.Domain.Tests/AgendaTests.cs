using Htn.Domain.Agenda;
using Htn.Domain.Common;

namespace Htn.Domain.Tests;

public class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Event = Guid.CreateVersion7(Now);
    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static NewSession Data(int dur = 30) =>
        new("Opening", SessionKind.Talk, new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.FromHours(8)), dur, null, true, null);

    [Fact]
    public void Ends_at_is_start_plus_duration()
    {
        var s = Session.Create(Event, Data(45), Actor, Now);
        Assert.Equal(s.StartsAt.AddMinutes(45), s.EndsAt);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(601)]
    public void Duration_is_bounded(int dur) =>
        Assert.Throws<DomainException>(() => Session.Create(Event, Data(dur), Actor, Now));

    [Fact]
    public void A_title_is_required() =>
        Assert.Throws<DomainException>(() => Session.Create(Event, Data() with { Title = "  " }, Actor, Now));
}

public class EventRoomTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Event = Guid.CreateVersion7(Now);

    [Fact]
    public void A_room_needs_a_valid_hex_colour()
    {
        Assert.Throws<DomainException>(() => EventRoom.Create(Event, "Main Hall", "red", 0, Now));
        var room = EventRoom.Create(Event, "Main Hall", "#b71c1c", 0, Now);
        Assert.Equal("#B71C1C", room.ColorHex); // normalized upper-case
    }

    [Fact]
    public void A_deactivated_room_cannot_be_edited()
    {
        var room = EventRoom.Create(Event, "A", "#111111", 0, Now);
        room.Deactivate(Now);
        Assert.Throws<DomainException>(() => room.Update("B", "#222222", 1));
    }
}
