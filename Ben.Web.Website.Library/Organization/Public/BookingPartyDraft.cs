using Ben.Service.Models.Entities;

namespace Ben.Web.Website.Library.Organization.Public;

/// <summary>
/// Who is in a party, by name, and what each of them can't eat, as a guest types it.
/// </summary>
/// <remarks>
/// <para><b>One row per person in the party.</b> The server keeps no more names than the party has
/// people, so a form with more rows would be throwing typing away; one with fewer would leave
/// somebody with no way to say they can't eat nuts.</para>
///
/// <para><b>A note needs a name.</b> The server drops a row with no name, and a dietary note with
/// nobody attached to it is one the kitchen can't act on, so the form says so instead of losing
/// it.</para>
/// </remarks>
public sealed class BookingPartyDraft
{
    public sealed class Person
    {
        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";

        /// <summary>Kept from the booking so the lead's own row stays linked to their account.</summary>
        public Guid? AppUserId { get; set; }
    }

    private readonly List<Person> _people = [new()];

    public IReadOnlyList<Person> People => _people;

    /// <summary>Has the guest typed into any row yet? Until then the lead's name may be put in.</summary>
    public bool Touched { get; private set; }

    public void Touch() => Touched = true;

    /// <summary>One row for each person, keeping what was typed into the rows that stay.</summary>
    public void Resize(int partySize)
    {
        var size = Math.Max(1, partySize);
        while (_people.Count < size) _people.Add(new Person());
        if (_people.Count > size) _people.RemoveRange(size, _people.Count - size);
    }

    /// <summary>The first row is the person booking, so their name goes in unless something is typed there.</summary>
    public void FillLead(string? firstName, string? lastName)
    {
        if (Touched || _people[0].Name.Length > 0) return;
        _people[0].Name = $"{firstName?.Trim()} {lastName?.Trim()}".Trim();
    }

    /// <summary>A draft of a booking that already exists, for changing it.</summary>
    public static BookingPartyDraft From(MyHostedEventBookingRecord booking)
    {
        var draft = new BookingPartyDraft();
        draft._people.Clear();
        draft._people.AddRange(booking.Guests
            .OrderBy(g => g.SortOrder)
            .Select(g => new Person { Name = g.DisplayName, Notes = g.DietaryNotes ?? "", AppUserId = g.AppUserId }));
        if (draft._people.Count == 0) draft._people.Add(new Person());
        draft.Resize(booking.PartySize);
        return draft;
    }

    /// <summary>A row with a note and no name: the one thing the form won't send.</summary>
    public bool HasANoteWithoutAName
        => _people.Any(p => p.Name.Trim().Length == 0 && p.Notes.Trim().Length > 0);

    /// <summary>
    /// The named rows, in order, as the requests want them. Empty when nobody is named, which on a
    /// change clears the names rather than leaving the old ones.
    /// </summary>
    public IReadOnlyList<HostedEventBookingGuestInput> Sent()
        => [.. _people
            .Where(p => p.Name.Trim().Length > 0)
            .Select(p => new HostedEventBookingGuestInput(
                p.Name.Trim(), p.AppUserId, BookingContactDraft.Sent(p.Notes)))];
}
