using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The events the caller may run the door at (item 235 phase 14c).
/// </summary>
/// <remarks>
/// <para><b>Why the phone needs to ask.</b> A weekend helper added by email belongs to no group, so nothing
/// else the app reads leads to the door; a hotel porter lent by the venue belongs to the wrong group. Without
/// this list the door on the phone could only be reached by somebody who already knew the event's address.</para>
///
/// <para><b>Every row is decided by the door's own rule</b> — <see cref="HostedEventAccess.CanRunTheDoorAsync(Guid, Guid, Guid, BenDataContext, CancellationToken)"/>,
/// the same one the door's endpoints ask. The candidates below only narrow what gets asked: events of groups
/// the caller belongs to, events they accepted a helper's place at, and events held at a venue whose people
/// they are. A row here is therefore never a door that then refuses them.</para>
///
/// <para><b>Only events still to run, or running.</b> Published, live or just ended, and not more than two
/// days past the last night — a door is for the night, and a list of last year's doors is noise on a phone.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/hosted-event-duties")]
public sealed class MyHostedEventDutiesController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly HostedEventAccess _access;

    public MyHostedEventDutiesController(IDbContextFactory<BenDataContext> dbFactory, HostedEventAccess access)
    {
        _dbFactory = dbFactory;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MyHostedEventDutyRecord>>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var since = now.Date.AddDays(-2);

        var myGroups = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        var helping = await db.HostedEventStaff.AsNoTracking()
            .Where(s => s.AppUserId == userId && s.DateConfirmed != null)
            .Select(s => new { s.HostedEventId, s.RoleLabel })
            .ToListAsync(ct);
        var helpingAt = helping.Select(h => h.HostedEventId).ToList();

        var candidates = await db.HostedEvents.AsNoTracking()
            .Where(e => HostedEventStates.OnThePublicSite.Contains(e.LifecycleState) && e.EndsOn >= since)
            .Where(e => myGroups.Contains(e.OrganizationId)
                     || helpingAt.Contains(e.Id)
                     || (e.VenueGrant != null && e.VenueGrant.RevokedUtc == null && e.VenueGrant.AllowStaff
                         && myGroups.Contains(e.VenueGrant.VenueOrganizationId)))
            .OrderBy(e => e.StartsOn)
            .Select(e => new
            {
                e.Id, e.OrganizationId, e.Name, OrganizationName = e.Organization.Name, VenueName = e.Place.Name,
                e.StartsOn, e.EndsOn, e.TimeZoneId, Nights = e.Nights.Select(n => n.Date).ToList(),
            })
            .ToListAsync(ct);

        var duties = new List<MyHostedEventDutyRecord>();
        foreach (var e in candidates)
        {
            if (!await _access.CanRunTheDoorAsync(userId, e.OrganizationId, e.Id, db, ct)) continue;

            var today = TimeZoneInfo.ConvertTimeFromUtc(now, HostedEventCalendarSync.ZoneOf(e.TimeZoneId)).Date;
            duties.Add(new MyHostedEventDutyRecord(
                e.Id, e.OrganizationId, e.Name, e.OrganizationName, e.VenueName, e.StartsOn, e.EndsOn, e.TimeZoneId,
                helping.FirstOrDefault(h => h.HostedEventId == e.Id)?.RoleLabel,
                e.Nights.Any(n => n.Date == today)));
        }

        return Ok(duties);
    }
}
