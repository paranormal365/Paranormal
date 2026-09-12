using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// What the kitchen has to cook differently at a hosted event (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Pure, and separate from the controller, for the same reason <see cref="EventCapacity"/>
/// is.</b> The counting is the part worth being sure about, and it is worth being sure about
/// without a web request around it.</para>
///
/// <para><b>The tally groups on the note as typed</b>, lower-cased and with its spacing tidied, and
/// on nothing cleverer. "No nuts" and "nut allergy" are one requirement to a cook and two strings
/// here; a tally that guessed they were the same thing would sooner or later merge two that are
/// not, and a cook acting on a wrong merge poisons somebody. Two people who typed the same words
/// count as two, which is the only claim this makes.</para>
///
/// <para><b>The unnamed count is the number that matters most.</b> A party of four who listed two
/// names leaves two people the kitchen knows nothing about, and a sheet showing only the notes
/// would read as complete when it was half a weekend's guests.</para>
/// </remarks>
public static class EventDietary
{
    /// <summary>
    /// Reads a set of bookings into the kitchen's sheet.
    /// </summary>
    /// <param name="bookings">
    /// Every booking for the event, with guests and nights loaded. Which of them count is decided
    /// here rather than by the caller's query, so the rule lives in one place.
    /// </param>
    /// <param name="includeRequests">
    /// Folds in parties the venue has not decided on, for a host ordering ahead of a weekend that
    /// is not settled. The answer carries this back, so a cook cannot read a provisional number as
    /// a settled one.
    /// </param>
    public static HostedEventDietaryRecord Summarise(
        Guid eventId, IEnumerable<HostedEventBooking> bookings, bool includeRequests)
    {
        var counted = bookings
            .Where(b => b.Status == HostedEventBookingStatus.Confirmed
                     || (includeRequests && b.Status == HostedEventBookingStatus.Requested))
            .OrderBy(b => b.DateCreated)
            .ToList();

        var lines = new List<HostedEventDietaryLineRecord>();
        var expected = 0;
        var unnamed = 0;

        foreach (var booking in counted)
        {
            var party = EventCapacity.ClampPartySize(booking.PartySize);
            expected += party;

            // A party that named MORE people than it booked for is somebody's typing, not eight
            // extra dinners; the shortfall never goes negative.
            unnamed += Math.Max(0, party - booking.Guests.Count);

            var nights = booking.Nights
                .Select(n => n.HostedEventNight?.Date ?? default)
                .Where(d => d != default)
                .OrderBy(d => d)
                .ToList();

            foreach (var guest in booking.Guests.OrderBy(g => g.SortOrder))
            {
                if (guest.DietaryNotes?.Trim() is not { Length: > 0 } notes) continue;

                lines.Add(new HostedEventDietaryLineRecord(
                    booking.Id,
                    booking.LeadAppUser?.DisplayName ?? "Somebody",
                    booking.Status,
                    guest.DisplayName,
                    notes,
                    nights));
            }
        }

        var tally = lines
            .GroupBy(l => Tidied(l.Notes), StringComparer.OrdinalIgnoreCase)
            .Select(g => new HostedEventDietaryTallyRecord(g.First().Notes, g.Count()))
            .OrderByDescending(t => t.People)
            .ThenBy(t => t.Notes, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HostedEventDietaryRecord(
            eventId, includeRequests, expected, lines.Count, unnamed, tally, lines);
    }

    /// <summary>The same words written the same way, so two people saying one thing count as two.</summary>
    private static string Tidied(string notes)
        => string.Join(' ', notes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
