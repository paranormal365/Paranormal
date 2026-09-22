using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// Everything that points at a place, counted once, for both the question and the answer.
/// </summary>
/// <remarks>
/// <para><b>One function, two callers, on purpose.</b> The admin catalogue offers to delete a
/// place, and the rule is that it may go only while nothing holds it. That rule needs stating
/// twice — once to tell somebody what is in the way before they press anything, and once to
/// enforce it when they do — and the two statements must not be able to disagree.</para>
///
/// <para>They have disagreed before. Item 220's person purge previewed a row removal the database
/// then refused, because the preview counted one way and the delete another; the screen promised
/// something it could not do and the failure arrived after the click. So there is no second
/// count here. <see cref="ForAsync"/> is what the preview shows and what the delete consults, and
/// a table added to one is added to both because there is only one.</para>
///
/// <para><b>The list is the merge's list.</b> Ten things carry a PlaceId, and
/// <c>AdminPlaceMergeController</c> already had to find all ten the hard way — each one was added
/// to it after a merge failed or silently destroyed something. Anything missing here would let a
/// place be deleted out from under rows that still name it, which is the same class of bug from
/// the other end.</para>
/// </remarks>
public static class PlaceUsageCensus
{
    /// <summary>What is holding a place, and whether that means it can be removed.</summary>
    /// <param name="Cases">Cases sited here.</param>
    /// <param name="Investigations">Visits sited here.</param>
    /// <param name="Evidence">Photographs, recordings and video added straight to the place.</param>
    /// <param name="FieldSessions">Recorded sessions whose readings are sited here.</param>
    /// <param name="CalendarEvents">Diary entries pointing here.</param>
    /// <param name="HostedEvents">Public events whose venue this is.</param>
    /// <param name="Posts">Posts written about the place.</param>
    /// <param name="Rooms">Rooms named inside it.</param>
    /// <param name="VenueProfiles">Groups describing it as a venue.</param>
    /// <param name="VenueGrants">Permissions given to groups to host here.</param>
    /// <param name="Contacts">Recorded ways to reach whoever runs it.</param>
    /// <param name="Claims">Outstanding or settled claims to run it.</param>
    public sealed record Usage(
        int Cases, int Investigations, int Evidence, int FieldSessions, int CalendarEvents,
        int HostedEvents, int Posts, int Rooms, int VenueProfiles, int VenueGrants,
        int Contacts, int Claims)
    {
        private int[] All =>
            [Cases, Investigations, Evidence, FieldSessions, CalendarEvents, HostedEvents,
             Posts, Rooms, VenueProfiles, VenueGrants, Contacts, Claims];

        /// <summary>Nothing anywhere points at this place.</summary>
        public bool IsEmpty => Total == 0;

        /// <summary>The total, for a screen that wants one number.</summary>
        public int Total => All.Sum();

        /// <summary>
        /// What is in the way, in words, or null when nothing is.
        /// </summary>
        /// <remarks>
        /// Named rather than totalled. "10 things point at this place" tells somebody they cannot
        /// delete it; "3 cases, 6 pieces of evidence and a room" tells them what they would have to
        /// move first, which is the only version they can act on.
        /// </remarks>
        public string? WhatHoldsIt()
        {
            if (IsEmpty) return null;

            var parts = new List<string>();
            void Say(int n, string one, string many)
            {
                if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}");
            }

            Say(Cases, "case", "cases");
            Say(Investigations, "investigation", "investigations");
            Say(Evidence, "piece of evidence", "pieces of evidence");
            Say(FieldSessions, "recorded session", "recorded sessions");
            Say(CalendarEvents, "diary entry", "diary entries");
            Say(HostedEvents, "public event", "public events");
            Say(Posts, "post", "posts");
            Say(Rooms, "room", "rooms");
            Say(VenueProfiles, "venue profile", "venue profiles");
            Say(VenueGrants, "hosting permission", "hosting permissions");
            Say(Contacts, "contact", "contacts");
            Say(Claims, "claim", "claims");

            return parts.Count == 1
                ? parts[0]
                : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        }
    }

    /// <summary>Counts everything pointing at one place.</summary>
    public static async Task<Usage> ForAsync(BenDataContext db, Guid placeId, CancellationToken ct)
        => new(
            await db.Cases.CountAsync(x => x.PlaceId == placeId, ct),
            await db.Investigations.CountAsync(x => x.PlaceId == placeId, ct),
            await db.PlaceEvidence.CountAsync(x => x.PlaceId == placeId, ct),
            await db.FieldSessionUploads.CountAsync(x => x.PlaceId == placeId, ct),
            await db.OrgCalendarEvents.CountAsync(x => x.PlaceId == placeId, ct),
            await db.HostedEvents.CountAsync(x => x.PlaceId == placeId, ct),
            await db.OrgMessages.CountAsync(x => x.PlaceId == placeId, ct),
            await db.PlaceRooms.CountAsync(x => x.PlaceId == placeId, ct),
            await db.OrganizationVenueProfiles.CountAsync(x => x.PlaceId == placeId, ct),
            await db.OrganizationVenueGrants.CountAsync(x => x.PlaceId == placeId, ct),
            await db.PlaceContacts.CountAsync(x => x.PlaceId == placeId, ct),
            await db.VenuePlaceClaims.CountAsync(x => x.PlaceId == placeId, ct));
}
