using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The tours a business runs — the thing it actually pays for (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "The $29 per month is for a single tour no matter how many times
/// scheduled. If they have a tour on one street and need another tour for another street, that is
/// a different tour." A <see cref="Tour"/> is therefore a product, and a date on the calendar is
/// that product happening.</para>
///
/// <para><b>What tells two tours apart.</b> Ben named the first half — the start address, which is
/// required. The second half is the name, unique within the business without regard to case. Two
/// tours may leave from the same corner as long as the business calls them different things; the
/// same name from the same corner is one tour scheduled twice. The name rather than the route,
/// because a route is prose the site cannot compare and a name is already on the leaflet.</para>
///
/// <para><b>Reading is open to members; changing costs money, so it takes the settings key</b> —
/// the same permission that opens billing, because adding a tour can charge the card.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/tours")]
public sealed class TourController : OrgCmsControllerBase
{
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly TourAddOnService _addOn;

    public TourController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        ICmsMarkupSanitizer sanitizer, TourAddOnService addOn)
        : base(dbFactory, mapper, security)
    { _sanitizer = sanitizer; _addOn = addOn; }

    // ── reading ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TourRecord>>> GetAll(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var tours = await LoadAsync(db, orgId, null, ct);
        return Ok(tours);
    }

    [HttpGet("{tourId:guid}")]
    public async Task<ActionResult<TourRecord>> GetOne(Guid orgId, Guid tourId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var tour = (await LoadAsync(db, orgId, tourId, ct)).FirstOrDefault();
        return tour is null ? NotFound() : Ok(tour);
    }

    /// <summary>What the plan says a tour costs right now — read before the business commits.</summary>
    [HttpGet("plan")]
    public async Task<ActionResult<TourPlanRecord>> GetPlan(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        // The plan is money, so it takes the same key the billing page does — a guide who can
        // see the tours they walk has no business reading what the business is charged.
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound();

        var d = await TourAddOnService.DescribeAsync(db, org, ct);
        return Ok(new TourPlanRecord(d.IsPerTour, d.TierName, d.UnitPrice, d.Interval,
            d.Covered, d.Active, d.NextCostsToday, d.PeriodEnd, d.HasCard));
    }

    // ── writing ──────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<TourRecord>> Create(
        Guid orgId, [FromBody] UpsertTourRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound();

        if (await ValidateAsync(db, orgId, request, null, ct) is string refusal)
            return BadRequest(refusal);

        var now = DateTime.UtcNow;
        var name = request.Name.Trim();
        var tour = new Tour
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = name,
            UrlName = await UrlSlug.MakeUniqueAsync(
                UrlSlug.From(name) ?? "tour",
                slug => db.Tours.AnyAsync(t => t.OrganizationId == orgId && t.UrlName == slug, ct)),
            Description = Clean(request.Description),
            StartOrganizationAddressId = request.StartOrganizationAddressId,
            DurationMinutes = request.DurationMinutes,
            DefaultCapacity = request.DefaultCapacity,
            TimeZoneId = request.TimeZoneId?.Trim() is { Length: > 0 } tz ? tz : "America/Chicago",
            AllowReviews = request.AllowReviews,
            IsBookable = request.IsBookable,
            ContactLine = Trimmed(request.ContactLine),
            MailSubjectTemplate = Trimmed(request.MailSubjectTemplate),
            MailBodyTemplate = Clean(request.MailBodyTemplate),
            DateCreated = now,
            CreatedByAppUserId = userId.Value,
        };
        db.Tours.Add(tour);
        await db.SaveChangesAsync(ct);

        // The money, after the tour exists: a payment problem must never be why a business cannot
        // define its own product. The note is the sentence the page shows them.
        var outcome = await _addOn.ChargeRemainderAsync(db, org, userId.Value, ct);

        var record = (await LoadAsync(db, orgId, tour.Id, ct)).First() with { PlanNote = outcome.Note };
        return CreatedAtAction(nameof(GetOne), new { orgId, tourId = tour.Id }, record);
    }

    [HttpPut("{tourId:guid}")]
    public async Task<ActionResult<TourRecord>> Update(
        Guid orgId, Guid tourId, [FromBody] UpsertTourRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var tour = await db.Tours.FirstOrDefaultAsync(t => t.Id == tourId && t.OrganizationId == orgId, ct);
        if (tour is null) return NotFound();

        if (await ValidateAsync(db, orgId, request, tourId, ct) is string refusal)
            return BadRequest(refusal);

        // The slug is not regenerated: every link already shared points at it, and fixing a typo
        // in a name must not break a leaflet.
        tour.Name = request.Name.Trim();
        tour.Description = Clean(request.Description);
        tour.StartOrganizationAddressId = request.StartOrganizationAddressId;
        tour.DurationMinutes = request.DurationMinutes;
        tour.DefaultCapacity = request.DefaultCapacity;
        if (request.TimeZoneId?.Trim() is { Length: > 0 } tz) tour.TimeZoneId = tz;
        tour.AllowReviews = request.AllowReviews;
        tour.IsBookable = request.IsBookable;
        tour.ContactLine = Trimmed(request.ContactLine);
        tour.MailSubjectTemplate = Trimmed(request.MailSubjectTemplate);
        tour.MailBodyTemplate = Clean(request.MailBodyTemplate);
        tour.DateUpdated = DateTime.UtcNow;
        tour.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok((await LoadAsync(db, orgId, tourId, ct)).First());
    }

    /// <summary>Stops a tour being scheduled or counted, without touching what it has already run.</summary>
    [HttpPost("{tourId:guid}/retire")]
    public async Task<ActionResult<TourRecord>> Retire(Guid orgId, Guid tourId, CancellationToken ct)
        => await SetRetiredAsync(orgId, tourId, retired: true, ct);

    [HttpPost("{tourId:guid}/restore")]
    public async Task<ActionResult<TourRecord>> Restore(Guid orgId, Guid tourId, CancellationToken ct)
        => await SetRetiredAsync(orgId, tourId, retired: false, ct);

    private async Task<ActionResult<TourRecord>> SetRetiredAsync(
        Guid orgId, Guid tourId, bool retired, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound();
        var tour = await db.Tours.FirstOrDefaultAsync(t => t.Id == tourId && t.OrganizationId == orgId, ct);
        if (tour is null) return NotFound();

        var now = DateTime.UtcNow;
        tour.RetiredAtUtc = retired ? now : null;
        tour.DateUpdated = now;
        tour.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        // Retiring never refunds — the period is paid for — but restoring can owe money again.
        var note = retired
            ? "Retired. Dates already on the calendar stand; your next renewal counts one tour "
              + "fewer."
            : (await _addOn.ChargeRemainderAsync(db, org, userId.Value, ct)).Note;

        return Ok((await LoadAsync(db, orgId, tourId, ct)).First() with { PlanNote = note });
    }

    // ── guides ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces a tour's guides.
    /// </summary>
    /// <remarks>
    /// <b>Ben, 2026-09-10:</b> "The tour owner is not necessarily the tour guide." So the list is
    /// kept by hand rather than derived from who runs the business — and everybody on it must be
    /// an active member, because their name and face go to guests as the person they will meet.
    /// </remarks>
    [HttpPut("{tourId:guid}/guides")]
    public async Task<ActionResult<TourRecord>> SetGuides(
        Guid orgId, Guid tourId, [FromBody] SetTourGuidesRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var tour = await db.Tours.Include(t => t.Guides)
            .FirstOrDefaultAsync(t => t.Id == tourId && t.OrganizationId == orgId, ct);
        if (tour is null) return NotFound();

        var wanted = (request.AppUserIds ?? []).Distinct().ToList();
        if (await WhoIsNotAMemberAsync(db, orgId, wanted, ct) is string notMember)
            return BadRequest(notMember);

        db.TourGuides.RemoveRange(tour.Guides);
        var now = DateTime.UtcNow;
        for (var i = 0; i < wanted.Count; i++)
            db.TourGuides.Add(new TourGuide
            {
                Id = Guid.NewGuid(), TourId = tourId, AppUserId = wanted[i], SortOrder = i,
                DateCreated = now, CreatedByAppUserId = userId.Value,
            });
        tour.DateUpdated = now;
        tour.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok((await LoadAsync(db, orgId, tourId, ct)).First());
    }

    /// <summary>The refusal naming whoever is not an active member, or null.</summary>
    internal static async Task<string?> WhoIsNotAMemberAsync(
        BenDataContext db, Guid orgId, IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return null;

        var members = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.IsActive && userIds.Contains(m.AppUserId))
            .Select(m => m.AppUserId).ToListAsync(ct);

        var stranger = userIds.FirstOrDefault(id => !members.Contains(id));
        if (stranger == Guid.Empty) return null;

        var name = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == stranger).Select(u => u.DisplayName ?? u.Email).FirstOrDefaultAsync(ct);

        return $"{name ?? "That person"} isn't a member of this group, so they can't be listed as "
             + "a guide. Invite them first — a guest is told who they are meeting, and that has to "
             + "be somebody you have actually taken on.";
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Anybody in the business may read its tours.
    /// </summary>
    /// <remarks>
    /// Membership rather than a permission grant, the same rule the calendar follows: a guide who
    /// cannot see the tour they are walking has been given a rota and no map. Changing a tour is
    /// the act that costs money, and that keeps the settings key.
    /// </remarks>
    private async Task<bool> IsMemberAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await Services.Access.FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);

    private string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var cleaned = _sanitizer.SanitizeHtml(html).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? Trimmed(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Everything wrong with an upsert, or null. First problem only, like the resolver.</summary>
    private static async Task<string?> ValidateAsync(
        BenDataContext db, Guid orgId, UpsertTourRequest request, Guid? existingId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return "A tour needs a name — it is what tells it apart from your other tours and what "
                 + "guests will look for.";
        if (name.Length > 120)
            return "That name is longer than 120 characters.";

        var clash = await db.Tours.AsNoTracking().AnyAsync(
            t => t.OrganizationId == orgId && t.Name == name && (existingId == null || t.Id != existingId), ct);
        if (clash)
            return $"You already have a tour called \"{name}\". Two tours that leave from the same "
                 + "place are told apart by their names, so this one needs a different one.";

        var addressBelongs = await db.OrganizationAddresses.AsNoTracking()
            .AnyAsync(a => a.Id == request.StartOrganizationAddressId && a.OrganizationId == orgId, ct);
        if (!addressBelongs)
            return "A tour starts at one of your own addresses, and that one isn't yours. Add the "
                 + "meeting point under Addresses first.";

        if (request.DurationMinutes is { } minutes && (minutes < 15 || minutes > 720))
            return "A tour runs between 15 minutes and 12 hours.";

        if (request.DefaultCapacity is { } capacity && (capacity < 1 || capacity > 500))
            return "A tour takes between 1 and 500 people.";

        if (request.TimeZoneId?.Trim() is { Length: > 0 } tz)
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return $"\"{tz}\" isn't a time zone this server knows. Pick one from the list.";
            }
        }

        return null;
    }

    /// <summary>One tour or all of a business's, with the counts the page shows.</summary>
    private static async Task<List<TourRecord>> LoadAsync(
        BenDataContext db, Guid orgId, Guid? tourId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var tours = await db.Tours.AsNoTracking()
            .Include(t => t.StartOrganizationAddress)
            .Include(t => t.Guides).ThenInclude(g => g.AppUser)
            .Where(t => t.OrganizationId == orgId && (tourId == null || t.Id == tourId))
            .OrderBy(t => t.RetiredAtUtc == null ? 0 : 1).ThenBy(t => t.Name)
            .ToListAsync(ct);

        var ids = tours.Select(t => t.Id).ToList();
        var dates = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.TourId != null && ids.Contains(e.TourId.Value))
            .Select(e => new { TourId = e.TourId!.Value, e.StartDateTime })
            .ToListAsync(ct);

        // The guides' public photographs, so the page can show the face a guest will be sent.
        var guideIds = tours.SelectMany(t => t.Guides).Select(g => g.AppUserId).Distinct().ToList();
        var photos = await db.AppUserPhotos.AsNoTracking()
            .Where(p => guideIds.Contains(p.AppUserId) && p.IsPublic && p.IsActive)
            .Select(p => new { p.AppUserId, p.UploadFileId })
            .ToListAsync(ct);

        return [.. tours.Select(t =>
        {
            var mine = dates.Where(d => d.TourId == t.Id).ToList();
            var upcoming = mine.Where(d => d.StartDateTime > now).ToList();
            return new TourRecord(
                t.Id, t.OrganizationId, t.Name, t.UrlName, t.Description,
                t.StartOrganizationAddressId,
                AddressLabel(t.StartOrganizationAddress),
                t.StartOrganizationAddress?.Latitude, t.StartOrganizationAddress?.Longitude,
                t.DurationMinutes, t.DefaultCapacity, t.TimeZoneId,
                t.AllowReviews, t.IsBookable, t.ContactLine,
                t.MailSubjectTemplate, t.MailBodyTemplate, t.RetiredAtUtc,
                [.. t.Guides.OrderBy(g => g.SortOrder).Select(g => new TourGuideRecord(
                    g.AppUserId,
                    g.AppUser.DisplayName ?? g.AppUser.Email ?? "A guide",
                    g.AppUser.Handle,
                    photos.FirstOrDefault(p => p.AppUserId == g.AppUserId)?.UploadFileId))],
                upcoming.Count, mine.Count,
                upcoming.Count == 0 ? null : upcoming.Min(d => d.StartDateTime));
        })];
    }

    /// <summary>The meeting point on one line, as a guest would read it.</summary>
    internal static string AddressLabel(OrganizationAddress? address)
        => address is null
            ? "No meeting point"
            : $"{address.StreetAddress1}, {address.City} {address.State}".Trim();
}
