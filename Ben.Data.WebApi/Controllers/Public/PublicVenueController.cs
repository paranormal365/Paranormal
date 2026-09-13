using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A venue's own page, for anybody (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Only a verified, published profile has one.</b> A page headed "the venue" is itself a
/// claim to be the building, so it is not shown until the claim has been proved, and not until the
/// venue chooses to show it.</para>
///
/// <para><b>Its rooms are the ones it made public</b>, not every room it has named — a staff room can
/// be named for readings and still be nobody's business — and its events are every event on the
/// public site at that address, whoever runs them, because "what is on here" is the question a
/// visitor to a venue's page is asking.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/venues")]
public sealed class PublicVenueController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicVenueController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet("{orgUrlName}/{placeId:guid}")]
    public async Task<ActionResult<PublicVenueRecord>> Get(string orgUrlName, Guid placeId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var profile = await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(v => v.PlaceId == placeId && v.Organization.UrlName == orgUrlName
                     && v.IsPublished && v.VerifiedUtc != null)
            .Select(v => new
            {
                v.OrganizationId, OrgName = v.Organization.Name, OrgUrlName = v.Organization.UrlName,
                PlaceName = v.Place.Name, v.Place.City, v.Place.State,
                v.History, v.HouseRules, v.MaxOvernightGuests,
            })
            .FirstOrDefaultAsync(ct);
        if (profile is null) return NotFound();

        var rooms = await db.PlaceRooms.AsNoTracking()
            .Where(r => r.OrganizationId == profile.OrganizationId && r.PlaceId == placeId
                     && r.IsActive && r.IsPublic)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new PublicVenueRoomRecord(r.Name, r.Floor, r.Description, r.Capacity))
            .ToListAsync(ct);

        var today = DateTime.UtcNow.Date;
        var events = await db.HostedEvents.AsNoTracking()
            .Where(e => e.PlaceId == placeId
                     && HostedEventStates.OnThePublicSite.Contains(e.LifecycleState)
                     && e.EndsOn >= today)
            .OrderBy(e => e.StartsOn)
            .Take(20)
            .Select(e => new PublicVenueEventRecord(
                e.Name, e.Organization.Name, $"/o/{e.Organization.UrlName}/events/{e.UrlName}",
                e.StartsOn, e.EndsOn))
            .ToListAsync(ct);

        return Ok(new PublicVenueRecord(
            profile.OrgName, profile.OrgUrlName, placeId, profile.PlaceName ?? "", profile.City, profile.State,
            profile.History, profile.HouseRules, profile.MaxOvernightGuests, rooms, events));
    }

    /// <summary>
    /// The verified venue at a place, for the place page's "run as a venue by" line. Empty when none.
    /// </summary>
    [HttpGet("~/api/public/places/{placeId:guid}/venue")]
    public async Task<ActionResult<PlaceVenueRecord>> ForPlace(Guid placeId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var venue = await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(v => v.PlaceId == placeId && v.VerifiedUtc != null)
            .Select(v => new PlaceVenueRecord(
                v.Organization.Name, v.Organization.UrlName,
                v.IsPublished ? $"/o/{v.Organization.UrlName}/venues/{v.PlaceId}" : null))
            .FirstOrDefaultAsync(ct);

        // Empty rather than 404: "nobody runs this place as a venue" is the ordinary answer, and
        // the website reads a 404 as a refusal it must show.
        return venue is null ? NoContent() : Ok(venue);
    }
}
