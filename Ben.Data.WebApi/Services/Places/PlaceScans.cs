using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Venues;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// The rules for a place's room scans: who may scan, who looks after them, and when one may be
/// replaced (LiDAR plan, phase 1, §2).
/// </summary>
/// <remarks>
/// <para><b>Ben, 10/28/2026:</b> scans are "per location, not per person", "so there are not
/// multiple people uploading the same rooms", and after "like a year" a scan may be updated. So a
/// room has one current scan, the people who come after it compare against it, and a replacement
/// comes through one of three doors — the custodian at any time, anybody once the scan is a year
/// old, or otherwise an offer the custodian decides.</para>
///
/// <para>Every answer that refuses carries a sentence the phone shows as it is: a person standing
/// in a room with a scan in their hand should be told what will happen to it and why.</para>
/// </remarks>
public static class PlaceScans
{
    /// <summary>How old a current scan must be before anybody who may scan may replace it.</summary>
    /// <remarks>A year, the same lifetime <see cref="EventCredits.Life"/> gives a credit.</remarks>
    public static readonly TimeSpan RefreshAfter = EventCredits.Life;

    /// <summary>How long tapping Scan keeps a room for the person scanning it.</summary>
    public static readonly TimeSpan HoldLength = TimeSpan.FromMinutes(20);

    /// <summary>The longest room name kept, the same as a group's room.</summary>
    public const int MaxRoomNameLength = 120;

    /// <summary>
    /// The room's name as a key: trimmed, lower-cased, inner whitespace collapsed — "bedroom 1" and
    /// " Bedroom  1" are one room. Empty when there is no name.
    /// </summary>
    /// <remarks>
    /// The phone normalizes the same way (<c>RoomKey.swift</c>), held together by the vectors in
    /// <c>Fixtures/place-scans/room-key-vectors.json</c>, which both test suites read. Lower-cased
    /// invariantly, so a phone set to Turkish does not make a different key from the server.
    /// </remarks>
    public static string NormalizeRoomKey(string? name)
        => CleanRoomName(name)?.ToLowerInvariant() ?? "";

    /// <summary>The name as it should be stored for showing: trimmed, inner whitespace collapsed. Null when empty.</summary>
    public static string? CleanRoomName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var text = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var c in name.Trim())
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = true; continue; }
            if (pendingSpace) { text.Append(' '); pendingSpace = false; }
            text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>What the caller may do at a place, worked out once per request.</summary>
    /// <param name="Place">The place itself.</param>
    /// <param name="MayView">May see the place's scans.</param>
    /// <param name="MayScan">May scan a room there, or compare against one.</param>
    /// <param name="ActingFor">The group the caller scans for there, when there is one.</param>
    /// <param name="WhyNot">The sentence for a caller who may not scan; null when they may.</param>
    public sealed record Standing(Place Place, bool MayView, bool MayScan, Guid? ActingFor, string? WhyNot);

    /// <summary>The caller's standing at a place, or null when there is no such place.</summary>
    /// <remarks>
    /// <para><b>A public location:</b> anybody signed in may scan and see.</para>
    ///
    /// <para><b>A private residence:</b> only an active member of a group with an investigation,
    /// case, event, rooms or contact details there (<see cref="VenueClaims.InterestedGroupsAsync"/>),
    /// or of its verified venue or the group holding its scans. Somebody's home is not a building
    /// strangers walk through, even as a model. A viewer may look; only a member who may change the
    /// group's work may scan.</para>
    /// </remarks>
    public static async Task<Standing?> StandingAsync(
        BenDataContext db, Guid placeId, Guid userId, CancellationToken ct)
    {
        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(p => p.Id == placeId, ct);
        if (place is null) return null;

        var venue = await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct);
        var grant = await OpenGrantAsync(db, placeId, ct);

        // Groups the caller belongs to, with the best role they hold in each.
        var memberships = await (
                from m in db.OrganizationUserMemberships.AsNoTracking()
                join o in db.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
                where m.AppUserId == userId && m.IsActive
                select new { m.OrganizationId, m.Role, o.Name })
            .ToListAsync(ct);

        // Who they scan for: the building's own venue first, then the group given its scans, then
        // any other group that may scan here, by name so the answer does not change between calls.
        Guid? ActingFor(IReadOnlyCollection<Guid> allowed)
        {
            var mine = memberships.Where(m => m.Role <= OrganizationMemberRole.Member && allowed.Contains(m.OrganizationId)).ToList();
            if (venue is not null && mine.Any(m => m.OrganizationId == venue.OrganizationId)) return venue.OrganizationId;
            if (grant is not null && mine.Any(m => m.OrganizationId == grant.OrganizationId)) return grant.OrganizationId;
            return mine.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m => (Guid?)m.OrganizationId).FirstOrDefault();
        }

        if (place.Kind == PlaceKind.PublicLocation)
        {
            var own = new List<Guid>();
            if (venue is not null) own.Add(venue.OrganizationId);
            if (grant is not null) own.Add(grant.OrganizationId);
            return new Standing(place, MayView: true, MayScan: true, ActingFor(own), WhyNot: null);
        }

        var groups = await VenueClaims.InterestedGroupsAsync(db, placeId, Guid.Empty, ct);
        if (venue is not null) groups.Add(venue.OrganizationId);
        if (grant is not null) groups.Add(grant.OrganizationId);

        var mayView = memberships.Any(m => groups.Contains(m.OrganizationId));
        var actingFor = ActingFor(groups);
        var mayScan = actingFor is not null;

        return new Standing(place, mayView, mayScan, actingFor,
            mayScan ? null
            : mayView ? "You can look at the scans here, but a viewer in a group can't add to its work."
            : "This is somebody's home, so only the groups working here can scan its rooms.");
    }

    /// <summary>Whether the caller may scan a room at this place, or compare against one.</summary>
    public static async Task<bool> MayScanAsync(BenDataContext db, Guid placeId, Guid userId, CancellationToken ct)
        => (await StandingAsync(db, placeId, userId, ct))?.MayScan == true;

    /// <summary>Whether the caller may see this place's scans.</summary>
    public static async Task<bool> MayViewAsync(BenDataContext db, Guid placeId, Guid userId, CancellationToken ct)
        => (await StandingAsync(db, placeId, userId, ct))?.MayView == true;

    /// <summary>Who looks after a place's scans, and why.</summary>
    public enum CustodianKind
    {
        /// <summary>The place's verified venue — it speaks for its own building.</summary>
        Venue,
        /// <summary>A group a SuperAdmin handed the scans to.</summary>
        Grant,
        /// <summary>At a public location with neither, whoever made the room's current scan.</summary>
        Scanner,
        /// <summary>At a private residence with neither, the group whose member made it.</summary>
        ScannersGroup,
    }

    /// <summary>The custodian: a group or a person, and the name to show.</summary>
    public sealed record Custodian(CustodianKind Kind, Guid? OrganizationId, Guid? AppUserId, string Name);

    /// <summary>
    /// Who looks after this room's scans, or null when nobody does yet — no venue, no grant, and no
    /// current scan to take the fallback from (§2 rule 3).
    /// </summary>
    public static async Task<Custodian?> CustodianAsync(
        BenDataContext db, Place place, PlaceScan? current, CancellationToken ct)
    {
        if (await VenueGrants.VerifiedVenueAtAsync(db, place.Id, ct) is { } venue)
            return new Custodian(CustodianKind.Venue, venue.OrganizationId, null, venue.Organization.Name);

        if (await OpenGrantAsync(db, place.Id, ct) is { } grant)
            return new Custodian(CustodianKind.Grant, grant.OrganizationId, null, grant.Organization.Name);

        if (current is null) return null;

        if (place.Kind == PlaceKind.PrivateResidence && current.ScannedForOrganizationId is Guid group)
        {
            var name = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == group).Select(o => o.Name).FirstOrDefaultAsync(ct);
            if (name is not null) return new Custodian(CustodianKind.ScannersGroup, group, null, name);
        }

        return new Custodian(CustodianKind.Scanner, null, current.ScannedByAppUserId,
            await PersonNameAsync(db, current.ScannedByAppUserId, ct));
    }

    /// <summary>Whether the caller is, or answers for, the custodian.</summary>
    /// <remarks>
    /// A group answers through its owners and administrators — the people who can accept an offer
    /// or take a scan down for it, the same people <see cref="VenueNotices"/> tells.
    /// </remarks>
    public static async Task<bool> IsCustodianAsync(
        BenDataContext db, Custodian? custodian, Guid userId, CancellationToken ct)
    {
        if (custodian is null) return false;
        if (custodian.AppUserId is Guid person) return person == userId;
        if (custodian.OrganizationId is not Guid org) return false;

        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == org && m.AppUserId == userId && m.IsActive
                        && m.Role <= OrganizationMemberRole.Administrator, ct);
    }

    /// <summary>The people who hear about an offer or a scan's first birthday.</summary>
    public static async Task<List<Guid>> CustodianRecipientsAsync(
        BenDataContext db, Custodian custodian, CancellationToken ct)
        => custodian.AppUserId is Guid person
            ? [person]
            : custodian.OrganizationId is Guid org
                ? await VenueNotices.PeopleWhoAnswerForAsync(db, org, ct)
                : [];

    /// <summary>
    /// Why the caller may not replace this room's current scan, or null when they may (§2 rule 5).
    /// </summary>
    /// <remarks>
    /// The custodian may at any time; whoever made the current scan may correct it while nobody
    /// else looks after the place; anybody who may scan may once it is
    /// <see cref="RefreshAfter"/> old. Everybody else is told what happens instead.
    /// </remarks>
    public static async Task<string?> WhyCannotReplaceAsync(
        BenDataContext db, Place place, PlaceScan current, Guid userId, DateTime now, CancellationToken ct)
    {
        var custodian = await CustodianAsync(db, place, current, ct);
        if (await IsCustodianAsync(db, custodian, userId, ct)) return null;

        // Their own scan, at a place nobody else looks after: a scan somebody has just found they
        // made badly is theirs to make again.
        if (custodian?.Kind is CustodianKind.Scanner or CustodianKind.ScannersGroup
            && current.ScannedByAppUserId == userId) return null;

        var refreshable = current.ScannedUtc + RefreshAfter;
        if (now >= refreshable) return null;

        var by = await PersonNameAsync(db, current.ScannedByAppUserId, ct);
        var keeper = custodian is null ? "Whoever looks after this place's scans"
            : custodian.Kind == CustodianKind.Scanner ? by
            : custodian.Name;

        return $"{current.RoomName} was scanned on {current.ScannedUtc:MM/dd/yyyy} by {by}. "
             + $"{keeper} decides whether a new scan replaces it, so yours can be offered to them "
             + $"or kept as a comparison. From {refreshable:MM/dd/yyyy}, anyone may replace it.";
    }

    /// <summary>The open grant of this place's scans to a group, if a SuperAdmin made one.</summary>
    public static Task<PlaceScanCustody?> OpenGrantAsync(BenDataContext db, Guid placeId, CancellationToken ct)
        => db.PlaceScanCustodies.AsNoTracking()
            .Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.PlaceId == placeId && c.ClosedUtc == null, ct);

    /// <summary>
    /// Closes the grant at a place now that a venue is verified there — the venue speaks for its own
    /// building. The caller saves.
    /// </summary>
    public static async Task CloseGrantForVenueAsync(BenDataContext db, Guid placeId, DateTime now, CancellationToken ct)
    {
        var open = await db.PlaceScanCustodies
            .FirstOrDefaultAsync(c => c.PlaceId == placeId && c.ClosedUtc == null, ct);
        if (open is null) return;

        open.ClosedUtc = now;
        open.ClosedReason = PlaceScanCustody.ClosedByVenue;
    }

    /// <summary>
    /// A person's name as other people see it — never their email address, which strangers at a
    /// public location have no business reading.
    /// </summary>
    public static async Task<string> PersonNameAsync(BenDataContext db, Guid appUserId, CancellationToken ct)
    {
        var person = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == appUserId)
            .Select(u => new { u.DisplayName, u.FirstName, u.LastName, u.Handle })
            .FirstOrDefaultAsync(ct);

        return PersonName(person?.DisplayName, person?.FirstName, person?.LastName, person?.Handle);
    }

    /// <summary>Display name, then first and last, then the handle, then "Someone".</summary>
    public static string PersonName(string? displayName, string? firstName, string? lastName, string? handle)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        var full = $"{firstName} {lastName}".Trim();
        if (full.Length > 0) return full;
        return string.IsNullOrWhiteSpace(handle) ? "Someone" : $"@{handle.Trim()}";
    }
}
