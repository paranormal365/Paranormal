using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// How often a person hears about a group's bookings (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>Per group, because the answer differs by group.</b> The manager of a hotel that runs
/// one weekend a year wants every request as it comes; somebody who also helps at a theatre's
/// hundred-night run wants that one as a morning letter. One switch for both is a switch somebody
/// turns off, and then hears about neither.</para>
///
/// <para><b>Only groups the person actually decides for</b>, asked the same way the letters are
/// addressed: a setting for letters nobody would ever send them is a control that does nothing,
/// and a list of every group they belong to would bury the one that matters.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/event-booking-alerts")]
public sealed class EventBookingAlertSettingsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;

    public EventBookingAlertSettingsController(
        IDbContextFactory<BenDataContext> dbFactory, Services.Access.HostedEventAccess access)
    { _dbFactory = dbFactory; _access = access; }

    [HttpGet]
    public async Task<ActionResult<EventBookingAlertSettingsRecord>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(new EventBookingAlertSettingsRecord(await GroupsAsync(db, userId, ct)));
    }

    [HttpPut("{orgId:guid}")]
    public async Task<ActionResult<EventBookingAlertSettingsRecord>> Set(
        Guid orgId, [FromBody] SetEventBookingAlertModeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (!Enum.IsDefined(request.Mode))
            return BadRequest("Choose as it happens, a daily letter, or nothing.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var groups = await GroupsAsync(db, userId, ct);
        if (groups.All(g => g.OrganizationId != orgId))
            return NotFound("You don't decide bookings for that group, so it sends you nothing to change.");

        var preference = await db.EventBookingAlertPreferences
            .FirstOrDefaultAsync(p => p.AppUserId == userId && p.OrganizationId == orgId, ct);

        var now = DateTime.UtcNow;
        if (preference is null)
        {
            db.EventBookingAlertPreferences.Add(new EventBookingAlertPreference
            {
                Id = Guid.NewGuid(), AppUserId = userId, OrganizationId = orgId, Mode = request.Mode,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        else
        {
            preference.Mode = request.Mode;
            preference.DateUpdated = now;
            preference.UpdatedByAppUserId = userId;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new EventBookingAlertSettingsRecord(await GroupsAsync(db, userId, ct)));
    }

    /// <summary>
    /// The groups whose booking letters reach this person: as somebody the group lets decide, or
    /// as a helper at one of its events handed the deciding.
    /// </summary>
    private async Task<IReadOnlyList<EventBookingAlertGroupRecord>> GroupsAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
    {
        var memberships = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id,
                  (m, o) => new { m.OrganizationId, o.Name })
            .ToListAsync(ct);

        var helping = await db.HostedEventStaff.AsNoTracking()
            .Where(s => s.AppUserId == userId && s.DateConfirmed != null && s.Decides)
            .Select(s => new { s.HostedEvent.OrganizationId, OrgName = s.HostedEvent.Organization.Name,
                               EventName = s.HostedEvent.Name })
            .ToListAsync(ct);

        var modes = await db.EventBookingAlertPreferences.AsNoTracking()
            .Where(p => p.AppUserId == userId)
            .ToDictionaryAsync(p => p.OrganizationId, p => p.Mode, ct);

        var groups = new List<EventBookingAlertGroupRecord>();

        foreach (var m in memberships)
        {
            if (!await _access.CanDecideBookingsAsync(userId, m.OrganizationId, ct)) continue;
            groups.Add(new(m.OrganizationId, m.Name,
                modes.GetValueOrDefault(m.OrganizationId, EventBookingAlertMode.AsItHappens),
                "You decide bookings for this group."));
        }

        foreach (var g in helping.GroupBy(h => h.OrganizationId))
        {
            if (groups.Any(x => x.OrganizationId == g.Key)) continue;
            var events = string.Join(", ", g.Select(h => h.EventName).Distinct().OrderBy(n => n));
            groups.Add(new(g.Key, g.First().OrgName,
                modes.GetValueOrDefault(g.Key, EventBookingAlertMode.AsItHappens),
                $"You're helping at {events}."));
        }

        return [.. groups.OrderBy(g => g.OrganizationName)];
    }
}
