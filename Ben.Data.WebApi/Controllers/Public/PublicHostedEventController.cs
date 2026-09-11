using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Hosted events as a visitor sees them (item 235).
/// </summary>
/// <remarks>
/// <para><b>Anonymous, like every other public surface here.</b> Somebody deciding whether to come
/// to a weekend is exactly the person with no account, and asking them to make one before they can
/// read what it is would be the wrong way round.</para>
///
/// <para><b>Published only.</b> A draft has no page at all — not a page that says "not yet", which
/// would leak that something is being planned and when. It answers 404, as though it did not
/// exist, because to a visitor it does not.</para>
///
/// <para><b>The umbrella row is still the one that carries sign-ups and reminders.</b> This adds
/// what an umbrella cannot say: the separate dates, the venue's own name, and whether it is a stay
/// or a run.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/hosted-events")]
public sealed class PublicHostedEventController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicHostedEventController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>One event, by its id.</summary>
    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<PublicHostedEventRecord>> GetOne(Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var found = await LoadAsync(db, e => e.Id == eventId, ct);
        return found is null ? NotFound() : Ok(found);
    }

    /// <summary>
    /// One event, by the organization and the slug — the address on the poster.
    /// </summary>
    /// <remarks>
    /// The same slug the umbrella calendar row carries, so <c>/o/{org}/events/{slug}</c> resolves
    /// whichever way a reader arrives at it.
    /// </remarks>
    [HttpGet("~/api/public/organizations/{orgUrlName}/hosted-events/{slug}")]
    public async Task<ActionResult<PublicHostedEventRecord>> GetBySlug(
        string orgUrlName, string slug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var found = await LoadAsync(
            db, e => e.UrlName == slug && e.Organization.UrlName == orgUrlName, ct);
        return found is null ? NotFound() : Ok(found);
    }

    /// <summary>Upcoming published events, newest date first, optionally for one organization.</summary>
    /// <remarks>
    /// An event that finished yesterday is not "what's on", so the list is bounded by the last
    /// date rather than the first: a weekend already under way is still worth showing on its
    /// Saturday.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicHostedEventRecord>>> GetUpcoming(
        [FromQuery] string? organization, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var today = DateTime.UtcNow.Date.AddDays(-1);
        var query = Visible(db).Where(e => e.EndsOn >= today);

        if (!string.IsNullOrWhiteSpace(organization))
            query = query.Where(e => e.Organization.UrlName == organization);

        var rows = await query
            .OrderBy(e => e.StartsOn)
            .Take(Math.Clamp(take, 1, 200))
            .Select(e => new Row(e, e.Organization.Name, e.Organization.UrlName, e.Place,
                                 db.OrgCalendarEvents.Where(c => c.HostedEventId == e.Id)
                                     .Select(c => c.Id).FirstOrDefault(),
                                 e.Nights.OrderBy(n => n.Date).ToList()))
            .ToListAsync(ct);

        return Ok(rows.Select(ToRecord).ToList());
    }

    private static IQueryable<HostedEvent> Visible(BenDataContext db)
        => db.HostedEvents.AsNoTracking()
            .Where(e => e.IsPublished && e.ArchivedAtUtc == null);

    private static async Task<PublicHostedEventRecord?> LoadAsync(
        BenDataContext db, System.Linq.Expressions.Expression<Func<HostedEvent, bool>> match,
        CancellationToken ct)
    {
        var row = await Visible(db).Where(match)
            .Select(e => new Row(e, e.Organization.Name, e.Organization.UrlName, e.Place,
                                 db.OrgCalendarEvents.Where(c => c.HostedEventId == e.Id)
                                     .Select(c => c.Id).FirstOrDefault(),
                                 e.Nights.OrderBy(n => n.Date).ToList()))
            .FirstOrDefaultAsync(ct);

        return row is null ? null : ToRecord(row);
    }

    private sealed record Row(
        HostedEvent Event, string OrgName, string OrgUrlName, Place? Place,
        Guid UmbrellaId, List<HostedEventNight> Nights);

    private static PublicHostedEventRecord ToRecord(Row r)
    {
        // The address is withheld the same way a public calendar event withholds it: a visitor
        // who has not got a place sees the town, and the host decides whether that is enough.
        var hidden = r.Event.HideExactLocation;
        var exact = hidden || r.Place is null
            ? null
            : string.Join(", ", new[]
                {
                    r.Place.StreetAddress1, r.Place.StreetAddress2,
                    r.Place.City, r.Place.State, r.Place.ZipCode,
                }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return new PublicHostedEventRecord(
            r.Event.Id,
            r.UmbrellaId,
            r.OrgName,
            r.OrgUrlName,
            r.Event.Name,
            r.Event.UrlName,
            r.Event.Tagline,
            r.Event.Description,
            r.Event.TimeZoneId,
            r.Event.StartsOn,
            r.Event.EndsOn,
            r.Event.DatesAreSeparate,
            HostedEventController.DateNoun(r.Event),
            r.Place?.Name,
            r.Place?.City,
            r.Place?.State,
            exact is { Length: > 0 } ? exact : null,
            hidden,
            r.Place?.Latitude,
            r.Place?.Longitude,
            r.Event.DayPassCapacity,
            r.Event.ContactLine,
            r.Event.CoverUploadFileId,
            r.Event.CancelledAtUtc is not null,
            r.Event.CancelledReason,
            [.. r.Nights.Select(n => HostedEventController.ToNight(n, r.Event))]);
    }
}
