namespace Ben.Service.Models.Entities;

// ── dining tables (item 235 phase 13) ────────────────────────────────────────

/// <summary>The dining room for one sitting: the tables, the confirmed parties there that night, and who sits where.</summary>
/// <param name="SittingId">The sitting shown, or null when the event has no menus yet.</param>
public sealed record HostedEventDiningRecord(
    Guid HostedEventId,
    IReadOnlyList<HostedEventDiningTableRecord> Tables,
    IReadOnlyList<HostedEventDiningSittingRecord> Sittings,
    Guid? SittingId,
    IReadOnlyList<HostedEventDiningPartyRecord> Parties,
    IReadOnlyList<HostedEventDiningSeatRecord> Seats,
    string? Sentence = null);

public sealed record HostedEventDiningTableRecord(Guid Id, string Name, int Seats, int SortOrder);

/// <summary>One sitting, as the pills at the top of the page name it.</summary>
public sealed record HostedEventDiningSittingRecord(Guid Id, DateTime NightDate, string Title, TimeSpan? ServedAtLocal, int Seated);

/// <summary>A confirmed party that is there on the sitting's night.</summary>
/// <param name="DietaryNotes">"Charles: no nuts" lines, for the kitchen's table list.</param>
public sealed record HostedEventDiningPartyRecord(
    Guid BookingId, string LeadName, int PartySize, int Seated, IReadOnlyList<string> DietaryNotes);

public sealed record HostedEventDiningSeatRecord(Guid Id, Guid TableId, Guid BookingId, int People);

/// <summary>Replacing the room's tables. A table sent back with its id keeps its seating.</summary>
public sealed record SetHostedEventDiningTablesRequest(IReadOnlyList<HostedEventDiningTableInput> Tables);

public sealed record HostedEventDiningTableInput(string Name, int Seats, Guid? Id = null);

/// <summary>Seating some of a party at a table. <paramref name="People"/> null seats everybody not yet seated.</summary>
public sealed record SeatHostedEventPartyRequest(Guid BookingId, Guid TableId, int? People = null);
