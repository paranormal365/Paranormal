using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Data.WebApi.Services.Venues;

/// <summary>Entity to record, in one place, for both sides of a venue request (item 235 phase 9).</summary>
public static class VenueRecords
{
    public static VenueRequestRecord Request(VenueHostingRequest r, HostedEvent hosted, string placeName)
        => new(
            r.Id, r.HostedEventId, hosted.Name,
            r.RequestingOrganizationId, r.RequestingOrganization?.Name ?? "",
            placeName, r.FromDate, r.ToDate,
            [.. hosted.Nights.Select(n => n.Date.Date).OrderBy(d => d)],
            hosted.DayPassCapacity,
            r.Message, r.Status, r.DateCreated, r.DecidedUtc, r.DecisionNote, r.OrganizationVenueGrantId);

    public static VenueGrantRecord Grant(
        OrganizationVenueGrant g, string granteeName, string placeName, IReadOnlyList<VenueGrantEventRecord> events)
        => new(
            g.Id, g.GranteeOrganizationId, granteeName, placeName,
            g.ValidFrom, g.ValidTo, g.AllowRooms, g.AllowHistory, g.AllowStaff,
            events, g.RevokedUtc, g.RevokedReason);
}
