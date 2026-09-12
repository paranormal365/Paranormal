using Ben.Data.Common.Enums;
using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// What is being served, on which night, at a hosted event (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>A menu belongs to a night, not to the event.</b> A weekend serves a different dinner
/// on each of its nights, and a guest reading "the menu" wants tonight's.</para>
///
/// <para><b>A night holds as many sittings as the venue serves</b> (Ben, 2026-09-12): dinner, a
/// late supper, snacks on the table, and the breakfast the next morning are four rows against one
/// night, each with its own name and time. A night here is the whole stay-period — the evening
/// people arrive through the morning they come down — which is why breakfast has a night to belong
/// to at all and why the LAST night of a weekend still serves Sunday breakfast.</para>
///
/// <para><b>They print in the host's own order</b>, not by the clock. Sorting a night by serving
/// time would put an eight o'clock breakfast before the previous evening's dinner, which is
/// exactly backwards; the host arranges the night's sittings as they happen and that is what a
/// guest reads.</para>
///
/// <para><b>Dietary tags here describe the dish.</b> "Vegan", "contains nuts" belong to the food
/// and go to everybody who can see the menu. A guest's own allergy is health information about a
/// named person, lives on their booking, and never appears here — the two are deliberately
/// different rows with different audiences.</para>
///
/// <para>Reading takes membership; writing takes the settings key, the same permission that
/// decides a booking. It moves to the <c>Events</c> area in phase 5 with everything else.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/menus")]
public sealed class HostedEventMenuController : OrgCmsControllerBase
{
    public HostedEventMenuController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    /// <summary>Every sitting of this event, in the order they are served.</summary>
    [HttpGet]
    public async Task<ActionResult<HostedEventMenusRecord>> Get(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        return Ok(await MenusAsync(db, eventId, ct));
    }

    /// <summary>
    /// Sets the whole set of menus for this event, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// <para><b>Replaces rather than merges</b>, exactly as the offered rooms do: the screen is one
    /// card a host fills in and saves, and a half-saved service with the pudding missing is worse
    /// than no menu at all.</para>
    ///
    /// <para><b>A sitting must belong to a night of this event.</b> Otherwise a menu would outlive
    /// the night it was written for, and a host who removed Sunday would go on serving Sunday
    /// dinner to nobody.</para>
    /// </remarks>
    [HttpPut]
    public async Task<ActionResult<HostedEventMenusRecord>> Set(
        Guid orgId, Guid eventId, [FromBody] SetHostedEventMenusRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        var wanted = request.Menus ?? [];

        var nights = await db.HostedEventNights
            .Where(n => n.HostedEventId == eventId)
            .Select(n => n.Id)
            .ToListAsync(ct);
        if (wanted.Any(m => !nights.Contains(m.HostedEventNightId)))
            return BadRequest("One of those sittings is on a night that isn't part of this event.");

        var untitled = wanted.FirstOrDefault(m => Trimmed(m.Title) is null);
        if (untitled is not null)
            return BadRequest(
                "Every sitting needs a name — \"Breakfast\", \"Lunch\", \"Dinner\", \"Snacks\".");

        // Replace-the-set: the old sittings go, items and all, and the new ones are written fresh.
        // Held ids are not carried across, because nothing outside this event points at a menu row.
        var existing = await db.HostedEventMenus
            .Include(m => m.Items)
            .Where(m => m.HostedEventNight.HostedEventId == eventId)
            .ToListAsync(ct);
        db.HostedEventMenuItems.RemoveRange(existing.SelectMany(m => m.Items));
        db.HostedEventMenus.RemoveRange(existing);

        // Position in the list IS the order, for sittings and for dishes alike. Reading a sent
        // SortOrder as well would let a screen that sends both disagree with itself, and every
        // screen that sends this is a list somebody has already dragged into the right order.
        for (var i = 0; i < wanted.Count; i++)
        {
            var input = wanted[i];
            var menu = new HostedEventMenu
            {
                Id = Guid.NewGuid(),
                HostedEventNightId = input.HostedEventNightId,
                Title = Trimmed(input.Title)!,
                ServedAtLocal = WithinADay(input.ServedAtLocal),
                Notes = Trimmed(input.Notes),
                SortOrder = i,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId.Value,
            };
            db.HostedEventMenus.Add(menu);

            var itemOrder = 0;
            foreach (var item in input.Items ?? [])
            {
                var name = Trimmed(item.Name);
                if (name is null) continue;   // a nameless dish is a blank row somebody left behind

                db.HostedEventMenuItems.Add(new HostedEventMenuItem
                {
                    Id = Guid.NewGuid(),
                    HostedEventMenuId = menu.Id,
                    Course = Trimmed(item.Course),
                    Name = name,
                    Description = Trimmed(item.Description),
                    DietaryTags = Trimmed(item.DietaryTags),
                    SortOrder = itemOrder++,
                    DateCreated = DateTime.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(await MenusAsync(db, eventId, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every sitting of one event, ordered the way a guest reads a weekend: night by night, and
    /// within a night in the order the host arranged them.
    /// </summary>
    /// <remarks>
    /// <b>Not by serving time.</b> A night runs from the evening through the next morning, so its
    /// eight o'clock breakfast comes after its seven o'clock dinner — sorting on the clock would
    /// print the weekend backwards.
    /// </remarks>
    public static async Task<HostedEventMenusRecord> MenusAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var menus = await db.HostedEventMenus
            .AsNoTracking()
            .Include(m => m.Items)
            .Include(m => m.HostedEventNight)
            .Where(m => m.HostedEventNight.HostedEventId == eventId)
            .ToListAsync(ct);

        return new HostedEventMenusRecord(
            eventId,
            menus
                .OrderBy(m => m.HostedEventNight.Date)
                .ThenBy(m => m.SortOrder)
                .Select(m => new HostedEventMenuRecord(
                    m.Id, m.HostedEventNightId, m.HostedEventNight.Date, m.HostedEventNight.Title,
                    m.Title, m.ServedAtLocal, m.Notes, m.SortOrder,
                    m.Items
                        .OrderBy(i => i.SortOrder)
                        .Select(i => new HostedEventMenuItemRecord(
                            i.Id, i.Course, i.Name, i.Description, i.DietaryTags, i.SortOrder))
                        .ToList()))
                .ToList());
    }

    /// <summary>
    /// A serving time that is a time of day, or nothing.
    /// </summary>
    /// <remarks>
    /// A negative or twenty-six-hour value is a form slip, and storing it would print "served at
    /// 1.02:00" on somebody's weekend. Dropped rather than refused: the sitting is still real
    /// without a clock beside it.
    /// </remarks>
    private static TimeSpan? WithinADay(TimeSpan? at)
        => at is { } t && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    private async Task<bool> IsMemberAsync(
        BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await Services.Access.FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
