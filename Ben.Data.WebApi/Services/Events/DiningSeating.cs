using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The rules for seating confirmed parties at tables (item 235 phase 13). Pure, so each is tested without a database.
/// </summary>
/// <remarks>
/// Two rules and no more: a table seats no more people than it has chairs, and a party is never seated as more
/// people than it has. Who sits with whom is the booking's note for now — the plan says so, and a matching engine
/// nobody asked for would be the wrong thing to build first.
/// </remarks>
public static class DiningSeating
{
    /// <summary>People already at a table for one sitting.</summary>
    public static int AtTable(IEnumerable<HostedEventDiningSeat> seats, Guid tableId)
        => seats.Where(s => s.HostedEventDiningTableId == tableId).Sum(s => s.People);

    /// <summary>People of a party already seated for one sitting.</summary>
    public static int OfParty(IEnumerable<HostedEventDiningSeat> seats, Guid bookingId)
        => seats.Where(s => s.HostedEventBookingId == bookingId).Sum(s => s.People);

    /// <summary>
    /// How many to seat, or why not, in words that name the table and the party.
    /// </summary>
    /// <param name="asked">Null seats everybody in the party not already seated.</param>
    public static (int People, string? Refusal) HowMany(
        HostedEventDiningTable table, string partyName, int partySize,
        IReadOnlyList<HostedEventDiningSeat> sittingSeats, Guid bookingId, int? asked)
    {
        var unseated = partySize - OfParty(sittingSeats, bookingId);
        if (unseated <= 0) return (0, $"Everybody in {partyName}'s party is already seated for this sitting.");

        var people = asked ?? unseated;
        if (people < 1) return (0, "Seat at least one person.");
        if (people > unseated)
            return (0, $"{partyName}'s party has {unseated} {(unseated == 1 ? "person" : "people")} still to seat, not {people}.");

        var free = table.Seats - AtTable(sittingSeats, table.Id);
        if (people > free)
            return (0, free == 0
                ? $"{table.Name} is full."
                : $"{table.Name} seats {table.Seats} and has {free} {(free == 1 ? "chair" : "chairs")} left; {partyName}'s party needs {people}. "
                  + $"Seat {free} here and the rest at another table.");

        return (people, null);
    }
}
