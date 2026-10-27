using Htn.Domain.Common;

namespace Htn.Domain.Agenda;

/// <summary>
/// A room in an event's programme (Main Hall, Workshop Room A). A table, not a string list, so a room has a stable
/// colour, can be counted, and renaming it never rewrites the sessions that point at it. Deactivated, never deleted.
/// </summary>
public sealed class EventRoom
{
    public const int NameMaxLength = 80;

    private EventRoom() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary>A #RRGGBB colour for the room's chip and timeline column.</summary>
    public string ColorHex { get; private set; } = default!;

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static EventRoom Create(Guid eventId, string name, string colorHex, int sortOrder, DateTimeOffset at)
    {
        if (eventId == Guid.Empty) throw new DomainException("An event is required.");
        return new EventRoom
        {
            Id = Guid.CreateVersion7(at),
            EventId = eventId,
            Name = Name_(name),
            ColorHex = Color(colorHex),
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = at,
        };
    }

    public void Update(string name, string colorHex, int sortOrder)
    {
        if (!IsActive) throw new DomainException("This room is inactive.");
        Name = Name_(name);
        ColorHex = Color(colorHex);
        SortOrder = sortOrder;
    }

    public void Deactivate(DateTimeOffset at)
    {
        if (!IsActive) throw new DomainException("This room is already inactive.");
        IsActive = false;
        DeactivatedAt = at;
    }

    private static string Name_(string? name)
    {
        var v = name?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException("A room name is required.");
        if (v.Length > NameMaxLength) throw new DomainException($"Name must be at most {NameMaxLength} characters.");
        return v;
    }

    private static string Color(string? hex)
    {
        var v = hex?.Trim().ToUpperInvariant();
        if (v is null || !System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9A-F]{6}$"))
            throw new DomainException("Colour must be a hex value like #B71C1C.");
        return v;
    }
}
