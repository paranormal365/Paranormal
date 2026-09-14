using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The events an organization puts on — the thing it pays for, not a date in its diary (item 235).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-11:</b> "The idea is to allow someone to schedule and track and organize
/// an event that is not ghost hunting related." A weekend at a haunted hotel, a writers' retreat, a
/// monthly murder-mystery dinner. Nothing here is about ghosts, and the one surface that is —
/// collecting evidence — is a switch, off by default.</para>
///
/// <para><b>Reading is open to members; changing costs money eventually, so it takes the settings
/// key</b>, the same permission that opens billing. That is the rule the tours controller follows
/// for the same reason, and it moves to the new <c>Events</c> permission area in phase 5 when
/// per-event staff arrive.</para>
///
/// <para><b>Publishing has its own endpoint.</b> It is the only act on this screen that spends
/// somebody's money — a credit, or a slot on their plan — and a checkbox on a save form is not a
/// place to do that.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events")]
public sealed class HostedEventController : OrgCmsControllerBase
{
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly HostedEventCalendarSync _sync;
    private readonly HostedEventEntitlement _entitlement;

    /// <summary>Longer than any real event and short enough to catch a typed year.</summary>
    public const int MaximumDates = 366;

    private readonly SiteSettingsService _settings;

    public HostedEventController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        ICmsMarkupSanitizer sanitizer,
        HostedEventCalendarSync sync,
        HostedEventEntitlement entitlement,
        SiteSettingsService settings)
        : base(dbFactory, mapper, security)
    { _sanitizer = sanitizer; _sync = sync; _entitlement = entitlement; _settings = settings; }

    // ── reading ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HostedEventRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        return Ok(await LoadAsync(db, orgId, null, ct));
    }

    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<HostedEventRecord>> GetOne(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var found = await LoadAsync(db, orgId, eventId, ct);
        return found.Count == 0 ? NotFound() : Ok(found[0]);
    }

    /// <summary>
    /// What publishing an event will cost this organization, before anybody presses anything.
    /// </summary>
    /// <remarks>
    /// The settings key, not membership: this is a question about money, and the same permission
    /// that opens the billing page answers it.
    /// </remarks>
    [HttpGet("plan")]
    public async Task<ActionResult<HostedEventPlanRecord>> GetPlan(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        return Ok(ToPlan(await _entitlement.DescribeAsync(db, orgId, null, ct)));
    }

    // ── writing ──────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<HostedEventRecord>> Create(
        Guid orgId, [FromBody] UpsertHostedEventRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct)) return NotFound();

        var (venue, venueRefusal) = await ResolveVenueAsync(db, request, userId.Value, ct);
        if (venueRefusal is not null) return BadRequest(venueRefusal);

        if (await ValidateAsync(db, orgId, request, venue!, null, ct) is string refusal)
            return BadRequest(refusal);

        var now = DateTime.UtcNow;
        var name = request.Name.Trim();

        // Created as a DRAFT, always. Nothing about creating an event costs anything, which is what
        // lets somebody build the whole thing — dates, programme, page — and walk away from it.
        var hosted = new HostedEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = name,
            UrlName = await UrlSlug.MakeUniqueAsync(
                UrlSlug.From(name) ?? "event",
                slug => db.HostedEvents.AnyAsync(e => e.OrganizationId == orgId && e.UrlName == slug, ct)),
            Tagline = Trimmed(request.Tagline),
            Description = Clean(request.Description),
            PlaceId = venue!.Id,
            HideExactLocation = request.HideExactLocation,
            TimeZoneId = request.TimeZoneId?.Trim() is { Length: > 0 } tz ? tz : "America/Chicago",
            StartsOn = request.StartsOn.Date,
            EndsOn = request.EndsOn.Date,
            DatesAreSeparate = request.DatesAreSeparate,
            DefaultStartLocal = request.DefaultStartLocal,
            DefaultEndLocal = request.DefaultEndLocal,
            LifecycleState = HostedEventLifecycleState.Draft,
            DayPassCapacity = request.DayPassCapacity,
            DayPassPrice = request.DayPassPrice,
            BookingsCloseAtUtc = request.BookingsCloseAtUtc,
            BookingMode = request.BookingMode,
            HoldMinutes = request.HoldMinutes,
            VenueArrangement = request.VenueArrangement,
            VenueContactName = Trimmed(request.VenueContactName),
            VenueAgreedOnUtc = request.VenueAgreedOnUtc,
            VenueReference = Trimmed(request.VenueReference),
            MinimumGuests = request.MinimumGuests,
            GoNoGoDeadlineUtc = request.GoNoGoDeadlineUtc,
            ContactLine = Trimmed(request.ContactLine),
            AccessNotes = Trimmed(request.AccessNotes),
            CoverUploadFileId = request.CoverUploadFileId,
            MailSubjectTemplate = Trimmed(request.MailSubjectTemplate),
            MailBodyTemplate = Clean(request.MailBodyTemplate),
            CollectsEvidence = request.CollectsEvidence,
            DateCreated = now,
            CreatedByAppUserId = userId.Value,
        };
        db.HostedEvents.Add(hosted);

        foreach (var night in BuildNights(hosted, request.Dates, userId.Value, now))
            db.HostedEventNights.Add(night);

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await db.SaveChangesAsync(ct);

        var record = (await LoadAsync(db, orgId, hosted.Id, ct))[0];
        return CreatedAtAction(nameof(GetOne), new { orgId, eventId = hosted.Id }, record);
    }

    [HttpPut("{eventId:guid}")]
    public async Task<ActionResult<HostedEventRecord>> Update(
        Guid orgId, Guid eventId, [FromBody] UpsertHostedEventRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        var (venue, venueRefusal) = await ResolveVenueAsync(db, request, userId.Value, ct);
        if (venueRefusal is not null) return BadRequest(venueRefusal);

        if (await ValidateAsync(db, orgId, request, venue!, eventId, ct) is string refusal)
            return BadRequest(refusal);

        // The slug is not regenerated. Every link already shared points at it, and fixing a typo in
        // a name must not break a poster.
        hosted.Name = request.Name.Trim();
        hosted.Tagline = Trimmed(request.Tagline);
        hosted.Description = Clean(request.Description);
        hosted.PlaceId = venue!.Id;
        hosted.HideExactLocation = request.HideExactLocation;
        if (request.TimeZoneId?.Trim() is { Length: > 0 } tz) hosted.TimeZoneId = tz;
        hosted.StartsOn = request.StartsOn.Date;
        hosted.EndsOn = request.EndsOn.Date;
        hosted.DatesAreSeparate = request.DatesAreSeparate;
        hosted.DefaultStartLocal = request.DefaultStartLocal;
        hosted.DefaultEndLocal = request.DefaultEndLocal;
        hosted.DayPassCapacity = request.DayPassCapacity;
        hosted.DayPassPrice = request.DayPassPrice;
        hosted.BookingsCloseAtUtc = request.BookingsCloseAtUtc;
        hosted.HoldMinutes = request.HoldMinutes;
        hosted.VenueArrangement = request.VenueArrangement;
        hosted.VenueContactName = Trimmed(request.VenueContactName);
        hosted.VenueAgreedOnUtc = request.VenueAgreedOnUtc;
        hosted.VenueReference = Trimmed(request.VenueReference);
        hosted.MinimumGuests = request.MinimumGuests;
        hosted.GoNoGoDeadlineUtc = request.GoNoGoDeadlineUtc;
        // Not the booking mode: changing it once people hold seats would silently convert their
        // holds into nothing, or their asks into places they never picked. It has its own endpoint
        // with its own refusal.
        hosted.ContactLine = Trimmed(request.ContactLine);
        hosted.AccessNotes = Trimmed(request.AccessNotes);
        hosted.CoverUploadFileId = request.CoverUploadFileId;
        hosted.MailSubjectTemplate = Trimmed(request.MailSubjectTemplate);
        hosted.MailBodyTemplate = Clean(request.MailBodyTemplate);
        hosted.CollectsEvidence = request.CollectsEvidence;
        hosted.DateUpdated = DateTime.UtcNow;
        hosted.UpdatedByAppUserId = userId.Value;

        await ReconcileNightsAsync(db, hosted, request.Dates, userId.Value, ct);
        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await db.SaveChangesAsync(ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);
    }

    /// <summary>Changing one date: what it is called, when it runs, what to say about it.</summary>
    [HttpPut("{eventId:guid}/nights/{nightId:guid}")]
    public async Task<ActionResult<HostedEventRecord>> UpdateNight(
        Guid orgId, Guid eventId, Guid nightId,
        [FromBody] UpsertHostedEventNightRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        var night = hosted.Nights.FirstOrDefault(n => n.Id == nightId);
        if (night is null) return NotFound();

        if (request.StartsLocal is { } from && request.EndsLocal is { } to && to <= from
            // Past midnight is ordinary for this kind of event and must not be refused: a lock-in
            // that ends at two in the morning is the normal case, not the mistake.
            && to > TimeSpan.FromHours(6))
            return BadRequest("That ends before it starts. If it runs past midnight, "
                            + "an end time in the small hours is what to put.");

        night.Title = Trimmed(request.Title);
        night.StartLocal = request.StartsLocal;
        night.EndLocal = request.EndsLocal;
        night.Notes = Trimmed(request.Notes);
        night.DateUpdated = DateTime.UtcNow;
        night.UpdatedByAppUserId = userId.Value;

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await db.SaveChangesAsync(ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);
    }

    /// <summary>
    /// Puts an event live: on the public site, taking bookings, and costing something.
    /// </summary>
    /// <remarks>
    /// <para>The one act on this screen that spends money, which is why it is an endpoint of its own
    /// rather than a field on the save form.</para>
    ///
    /// <para><b>Only the first time costs.</b> An event taken down and put back up is the same event
    /// and was paid for once — <c>FirstPublishedUtc</c> is what remembers, so the entitlement is
    /// only asked when it is null.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/publish")]
    public async Task<ActionResult<HostedEventRecord>> Publish(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        // Both collections, because the readiness list asks about both and a missing Include
        // would read as "no rooms on the plan" and refuse a perfectly ready event.
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .Include(e => e.LayoutUnits)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        if (hosted.IsOnThePublicSite) return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);

        if (hosted.LifecycleState is HostedEventLifecycleState.Cancelled)
            return BadRequest("This event has been called off. Un-cancel it first, "
                            + "or create a new one — the people who had places were told it was off.");

        if (hosted.LifecycleState is HostedEventLifecycleState.VenueWithdrawn)
            return BadRequest("The venue withdrew from this event. It cannot be put back up "
                            + "without them, so make a new one wherever it is now happening.");

        if (hosted.LifecycleState is HostedEventLifecycleState.Removed)
            return BadRequest("IsHaunted removed this event. If you think that was a mistake, appeal "
                            + "from the event's page; an upheld appeal brings it back as a draft.");

        if (hosted.LifecycleState is HostedEventLifecycleState.Archived)
            return BadRequest("This event has been archived. Restore it first.");

        // Every reason it is not ready, as the checklist on the page words them. One list, read by
        // the button and by the card above it, so the page can never offer a publish the server is
        // about to refuse — the failure mode a server guard with no UI path always becomes.
        var venue = await Services.Venues.VenueGrants.ForEventAsync(db, hosted, ct);
        if (HostedEventReadiness.Describe(hosted, venue).FirstOrDefault(i => !i.Done) is { } blocker)
            return BadRequest(blocker.Sentence);

        var note = (string?)null;

        // Asked only for an event that has never been live. The second publish of the same event is
        // free, for ever, because the first one paid for it.
        //
        // The credit is taken in THIS save, not checked and then taken: two tabs pressing the
        // button would otherwise both see one credit and both spend it, and the loser would have
        // paid for nothing.
        if (hosted.FirstPublishedUtc is null)
        {
            var (spent, refusal) = await _entitlement.TakeForAsync(db, hosted, userId.Value, ct);
            if (refusal is not null) return BadRequest(refusal);

            hosted.FirstPublishedUtc = DateTime.UtcNow;

            if (spent is not null)
            {
                var left = await EventCredits.SpendableCountAsync(
                    db, orgId, appUserId: null, DateTime.UtcNow, ct);
                // Minus the one being spent in this same save, which the count above cannot see.
                note = $"One event credit spent — {Math.Max(0, left - 1)} left.";
            }
            else
            {
                note = "It's live. Nothing extra was charged.";
            }
        }

        hosted.LifecycleState = HostedEventLifecycleState.Published;
        hosted.DateUpdated = DateTime.UtcNow;
        hosted.UpdatedByAppUserId = userId.Value;

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await SaveAndAuditAsync(db, hosted, hosted.Id, userId.Value, ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0] with { PlanNote = note });
    }

    /// <summary>Takes it off the public site. Nothing is refunded, and nothing is destroyed.</summary>
    /// <remarks>
    /// Back to Draft, which is the state it can be edited and published from again. The credit is
    /// not spent a second time: <c>FirstPublishedUtc</c> remembers that it was paid for.
    /// </remarks>
    [HttpPost("{eventId:guid}/unpublish")]
    public async Task<ActionResult<HostedEventRecord>> Unpublish(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        // AN EVENT THAT HAPPENED IS FINISHED. Ben, 2026-09-12: "Unless credit refunded, we can
        // assume the event completed and is finalized." Nothing that has started or ended may be
        // walked back into a draft — it took bookings, people came, and the credit it was paid for
        // stands. Without this the endpoint would take an ended event back to Draft, and because
        // its credit was never returned it would then publish again for nothing: one credit, two
        // events.
        if (hosted.LifecycleState is HostedEventLifecycleState.Live
                                  or HostedEventLifecycleState.Ended
                                  or HostedEventLifecycleState.Archived)
            return BadRequest("This event has already happened, so it cannot be taken back to a "
                            + "draft. Archive it if you want it off your list, or make a new event "
                            + "for the next one.");

        if (hosted.LifecycleState is not HostedEventLifecycleState.Published)
            return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);

        hosted.LifecycleState = HostedEventLifecycleState.Draft;
        hosted.DateUpdated = DateTime.UtcNow;
        hosted.UpdatedByAppUserId = userId.Value;

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await SaveAndAuditAsync(db, hosted, hosted.Id, userId.Value, ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);
    }

    /// <summary>Stops it counting and takes it off the list. What happened is untouched.</summary>
    [HttpPost("{eventId:guid}/archive")]
    public Task<ActionResult<HostedEventRecord>> Archive(Guid orgId, Guid eventId, CancellationToken ct)
        => SetAsync(orgId, eventId, e =>
        {
            e.ArchivedAtUtc = DateTime.UtcNow;
            e.LifecycleState = HostedEventLifecycleState.Archived;
        }, ct);

    /// <summary>
    /// Brings an archived event back. It publishes again for nothing.
    /// </summary>
    /// <remarks>
    /// To Ended when it has already happened and to Draft when it has not, rather than always to
    /// Draft: restoring last spring's weekend must not put it back on the public site as something
    /// people can book.
    /// </remarks>
    [HttpPost("{eventId:guid}/restore")]
    public Task<ActionResult<HostedEventRecord>> Restore(Guid orgId, Guid eventId, CancellationToken ct)
        => SetAsync(orgId, eventId, e =>
        {
            // Only an archived event comes back. Restoring anything else would be a side door out of a
            // called-off or removed state that skips the rules on the way out — the credit re-spend on
            // un-cancelling, and the appeal on a removal (phase 17a audit).
            if (e.LifecycleState is not HostedEventLifecycleState.Archived) return;
            e.ArchivedAtUtc = null;
            // Ended when it already happened AND was live at the time, so last spring's weekend
            // comes back as the record it is rather than as something bookable. Anything else
            // comes back as a draft. Read from the event's own dates, not from a lifecycle
            // timestamp: those say when a thing happened, never what the event is.
            e.LifecycleState = e.HasEverBeenLive && e.EndsOn.Date < DateTime.UtcNow.Date
                ? HostedEventLifecycleState.Ended
                : HostedEventLifecycleState.Draft;
        }, ct);

    /// <summary>
    /// Calls it off, keeping the row so the people who had places can see that it is off.
    /// </summary>
    /// <remarks>
    /// The umbrella row's title says CANCELLED, because that is the one line every list, share card
    /// and phone notification shows. Deleting the event instead would take the page down and leave
    /// everybody who was coming with a dead link and no explanation.
    /// </remarks>
    [HttpPost("{eventId:guid}/cancel")]
    public async Task<ActionResult<HostedEventRecord>> Cancel(
        Guid orgId, Guid eventId, [FromBody] CancelHostedEventRequest request,
        [FromServices] Services.Events.EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        if (HostedEventStates.CalledOff.Contains(hosted.LifecycleState))
            return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);

        var now = DateTime.UtcNow;

        // Both, and the state is the one anything reads. The stamp says when; it is not the answer
        // to "is this off", which is what it used to be and what let the door go on taking bookings
        // for a cancelled event the day the state column arrived.
        hosted.LifecycleState = HostedEventLifecycleState.Cancelled;
        hosted.CancelledAtUtc = now;
        hosted.CancelledReason = request.Reason?.Trim() is { Length: > 0 } r ? r : null;
        hosted.DateUpdated = now;
        hosted.UpdatedByAppUserId = userId.Value;

        var note = (await CreditBackAsync(db, hosted, userId.Value, now, ct)).Sentence;

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await SaveAndAuditAsync(db, hosted, hosted.Id, userId.Value, ct);

        // AFTER the save, and best effort. Until phase 6 this endpoint told the organizer that
        // everybody with a place had been told, and nothing had been sent to anybody: the guests
        // found out by opening the page, or by turning up.
        var told = await mail.SendCalledOffAsync(db, hosted.Id, hosted.CancelledReason, ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]
            with { PlanNote = $"{note} {Told(told)}" });
    }

    /// <summary>What to say about the letters, without claiming any that did not go.</summary>
    private static string Told(int sent) => sent switch
    {
        0 => "Nobody had a place to lose.",
        1 => "The one person with a place has been told.",
        _ => $"All {sent} people with places have been told.",
    };

    /// <summary>
    /// What calling this event off would do to its credit, without doing anything.
    /// </summary>
    /// <remarks>
    /// So the confirmation can say which is about to happen BEFORE the button is pressed. Finding
    /// out afterwards that ninety-nine dollars did not come back is the conversation this exists to
    /// avoid, and the sentence here is the one the cancel itself will answer with.
    /// </remarks>
    [HttpGet("{eventId:guid}/cancellation-effect")]
    public async Task<ActionResult<HostedEventCancellationEffect>> CancellationEffect(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        var (returns, sentence) = await WeighTheCreditAsync(db, hosted, DateTime.UtcNow, ct);
        return Ok(new HostedEventCancellationEffect(returns, sentence));
    }

    /// <summary>Whether the credit comes back, and the sentence to say about it.</summary>
    private async Task<(bool Returns, string Sentence)> WeighTheCreditAsync(
        BenDataContext db, HostedEvent hosted, DateTime now, CancellationToken ct)
    {
        var spent = await EventCredits.SpentOnAsync(db, hosted.Id, ct);
        var (startsUtc, _) = HostedEventCalendarSync.Window(hosted, [.. hosted.Nights]);

        var hours = (int)await _settings.GetDecimalAsync(
            SiteSettingKeys.EventCancellationCreditWindowHours,
            (decimal)EventCredits.CancellationWindow.TotalHours, ct);

        return EventCredits.WhatCancellingDoesToTheCredit(
            spent, startsUtc, now, TimeSpan.FromHours(hours),
            theVenueWithdrew: hosted.LifecycleState == HostedEventLifecycleState.VenueWithdrawn);
    }

    /// <summary>Puts the credit back when the rules say it comes back. The caller saves.</summary>
    private async Task<(bool Returned, string Sentence)> CreditBackAsync(
        BenDataContext db, HostedEvent hosted, Guid userId, DateTime now, CancellationToken ct)
    {
        var (returns, sentence) = await WeighTheCreditAsync(db, hosted, now, ct);
        if (!returns) return (false, sentence);

        var spent = await EventCredits.SpentOnAsync(db, hosted.Id, ct);
        if (spent is null) return (false, sentence);

        EventCredits.Unspend(spent, userId, now);

        // AND the event becomes unpaid again. Without this, cancelling inside the window would
        // hand back the credit while FirstPublishedUtc went on saying the event had been paid
        // for — so republishing it would cost nothing and the host would have both the credit and
        // the event. The two facts are one fact: either this event is paid for, or the credit is
        // back in their pocket.
        //
        // Outside the window the credit does NOT come back and this is left alone, which is also
        // right: they paid, they got nothing, and putting it back up is free.
        hosted.FirstPublishedUtc = null;

        return (true, sentence);
    }

    /// <summary>
    /// Says the event is going ahead, whatever the numbers came to.
    /// </summary>
    [HttpPost("{eventId:guid}/go")]
    public Task<ActionResult<HostedEventRecord>> Go(
        Guid orgId, Guid eventId,
        [FromServices] Services.Events.EventGuestMailer mail, CancellationToken ct)
        => DecideAsync(orgId, eventId, HostedEventGoNoGo.Go, mail, ct);

    /// <summary>
    /// Says it is not, which calls it off under exactly the same rules as any other cancellation.
    /// </summary>
    /// <remarks>
    /// Routed through cancelling rather than being a state of its own: the people with places have
    /// to be told, the listing has to say so, and the credit has to follow the forty-eight hour
    /// rule. A separate "did not reach its numbers" state would have had to reimplement all three
    /// and would eventually have got one of them wrong.
    /// </remarks>
    [HttpPost("{eventId:guid}/no-go")]
    public Task<ActionResult<HostedEventRecord>> NoGo(
        Guid orgId, Guid eventId,
        [FromServices] Services.Events.EventGuestMailer mail, CancellationToken ct)
        => DecideAsync(orgId, eventId, HostedEventGoNoGo.NoGo, mail, ct);

    private async Task<ActionResult<HostedEventRecord>> DecideAsync(
        Guid orgId, Guid eventId, HostedEventGoNoGo decision,
        Services.Events.EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        if (hosted.MinimumGuests is null)
            return BadRequest("This event has no minimum number, so there is nothing to decide. "
                            + "It runs whatever the numbers come to.");

        var now = DateTime.UtcNow;
        hosted.GoNoGoDecision = decision;
        hosted.GoNoGoDecidedUtc = now;
        hosted.GoNoGoDecidedByAppUserId = userId.Value;
        hosted.DateUpdated = now;
        hosted.UpdatedByAppUserId = userId.Value;

        string note;
        if (decision == HostedEventGoNoGo.NoGo)
        {
            hosted.LifecycleState = HostedEventLifecycleState.Cancelled;
            hosted.CancelledAtUtc = now;
            hosted.CancelledReason ??= "Not enough people to run it.";
            note = "Called off. "
                 + (await CreditBackAsync(db, hosted, userId.Value, now, ct)).Sentence;
        }
        else
        {
            note = "It's going ahead.";
        }

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await SaveAndAuditAsync(db, hosted, hosted.Id, userId.Value, ct);

        // The letters go after the save, and what the note claims is counted rather than assumed.
        // "Everybody with a place has been told" was written here in phase 3 and was not true of
        // anybody until phase 6 wrote the letters.
        var told = decision == HostedEventGoNoGo.NoGo
            ? await mail.SendCalledOffAsync(db, hosted.Id, hosted.CancelledReason, ct)
            : await mail.SendGoingAheadAsync(db, hosted.Id, ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]
            with { PlanNote = $"{note} {Told(told)}" });
    }

    // ── the layout: what this event allocates (phase 2.4) ────────────────────

    /// <summary>What this event allocates, and what each of them holds.</summary>
    /// <remarks>
    /// Readable by any member: knowing what is in play is not a billing question, and the booking
    /// board needs it to draw its grid.
    /// </remarks>
    [HttpGet("{eventId:guid}/layout")]
    public async Task<ActionResult<HostedEventLayoutRecord>> GetLayout(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var ev = await db.HostedEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        return Ok(await LayoutAsync(db, ev, ct));
    }

    /// <summary>
    /// The rooms this event's plan may place: the group's own at the venue, and the venue's when it
    /// lent them (phase 9).
    /// </summary>
    /// <remarks>
    /// Its own read, rather than the designer asking for the group's rooms at the place, because
    /// the group's rooms are the wrong list at somebody else's hotel — the Thomas House's rooms are
    /// the Thomas House's, and a society holding a weekend there never typed them in.
    /// </remarks>
    [HttpGet("{eventId:guid}/layout/rooms")]
    public async Task<ActionResult<IEnumerable<PlaceRoomRecord>>> GetOfferableRooms(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var ev = await db.HostedEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var owners = new List<Guid> { orgId };
        if (await Services.Venues.VenueGrants.RoomsLentToAsync(db, ev, ct) is Guid venueOrgId)
            owners.Add(venueOrgId);

        return Ok(await db.PlaceRooms.AsNoTracking()
            .Where(r => owners.Contains(r.OrganizationId) && r.PlaceId == ev.PlaceId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new PlaceRoomRecord(
                r.Id, r.PlaceId, r.Name, r.Floor, r.Description, r.IsPublic, r.SortOrder, r.IsActive,
                r.Capacity, r.IsBookable, r.BedNote))
            .ToListAsync(ct));
    }

    /// <summary>
    /// Sets the whole plan: what kind it is, and every room or seat on it.
    /// </summary>
    /// <remarks>
    /// <para><b>Replaces rather than merges</b>, because the screen is one designer somebody
    /// arranges and saves. A unit left out is removed — except one with confirmed bookings against
    /// it, which is refused rather than quietly dropped: the beds are occupied, and dropping the
    /// offer would leave a confirmed party sleeping somewhere the event says it is not using.</para>
    ///
    /// <para><b>A room must belong to this event's own place and to this organization.</b> Rooms
    /// are per-group per-place (item 197), so offering another group's description of the building
    /// would put their words on your weekend.</para>
    ///
    /// <para><b>Changing the KIND of a plan that has bookings is refused.</b> A party confirmed
    /// into the Blue Room cannot be silently reinterpreted as holding seat H9, and there is no
    /// answer to what their room becomes — so the venue is told to release the bookings first and
    /// decide deliberately.</para>
    ///
    /// <para><b>A choice that carries an <c>Id</c> IS that unit, whatever its label or room now
    /// says.</b> Before the id existed, "the same one" was decided by the room on a Rooms plan and
    /// the label on a Seats plan — which made renaming a booked seat read as delete C4, create C4a,
    /// and the delete was refused because C4 is booked. The designer's first save has no ids yet,
    /// so a choice without one still matches by room or label. An id that is not one of this
    /// event's units is refused in words rather than matched: it is either another event's seat or
    /// one somebody removed since the designer was opened, and quietly creating a new unit for it
    /// would hide both.</para>
    ///
    /// <para><b>The booked-removal refusal is a 409 carrying <see cref="LayoutRefusalRecord"/></b>
    /// — the same sentence a person reads, plus the ids of the units still holding parties. Every
    /// other refusal here is a 400 with a sentence, and the website shows those verbatim; this one
    /// is different because a designer with four hundred seats has to RING the two that are the
    /// problem, and a sentence alone would leave the venue hunting for row C. The distinct status
    /// is what lets the website tell "read this" from "ring these".</para>
    /// </remarks>
    [HttpPut("{eventId:guid}/layout")]
    public async Task<ActionResult<HostedEventLayoutRecord>> SetLayout(
        Guid orgId, Guid eventId, [FromBody] SetHostedEventLayoutRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var wanted = request.Units ?? [];
        // The venue's rooms by name, on a Rooms plan; empty on a Seats one. Kept so a refusal about
        // a room can say "Blue Room" rather than quote a guid at somebody.
        var roomNames = new Dictionary<Guid, string>();

        // ── the kind, and what it demands of each unit ───────────────────────
        if (request.Kind != ev.LayoutKind)
        {
            var held = await db.HostedEventBookingNights.AnyAsync(
                n => n.HostedEventBooking.HostedEventId == eventId
                  && n.HostedEventLayoutUnitId != null
                  && n.HostedEventBooking.Status == HostedEventBookingStatus.Confirmed, ct);
            if (held)
                return BadRequest(
                    "Parties are already confirmed into this plan, so it can't change from "
                  + $"{Describe(ev.LayoutKind)} to {Describe(request.Kind)}. Release those bookings "
                  + "first if you really mean to start again.");
            ev.LayoutKind = request.Kind;
        }

        if (request.Kind == HostedEventLayoutKind.Rooms)
        {
            if (wanted.Any(u => u.PlaceRoomId is null))
                return BadRequest("Every room on the plan has to be one of the venue's own rooms.");

            // The group's own rooms, and the venue's when the venue lent them (phase 9): a society
            // holding a weekend at the Thomas House books the Thomas House's rooms, not a list it
            // typed out itself.
            var roomOwners = new List<Guid> { orgId };
            if (await Services.Venues.VenueGrants.RoomsLentToAsync(db, ev, ct) is Guid venueOrgId)
                roomOwners.Add(venueOrgId);

            roomNames = await db.PlaceRooms
                .Where(r => roomOwners.Contains(r.OrganizationId) && r.PlaceId == ev.PlaceId && r.IsActive)
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
            if (wanted.Select(u => u.PlaceRoomId!.Value).Except(roomNames.Keys).Any())
                return BadRequest("One of those rooms is not a room this group, or the venue, has defined for the place.");

            if (wanted.GroupBy(u => u.PlaceRoomId).Any(g => g.Count() > 1))
                return BadRequest("A room can only be on the plan once.");
        }
        else
        {
            if (wanted.Any(u => u.PlaceRoomId is not null))
                return BadRequest("A seat isn't one of the venue's rooms.");
            if (wanted.Any(u => Trimmed(u.Label) is null))
                return BadRequest("Every seat needs a label — \"H9\", \"Row C, 4\".");
        }

        // ── what is going, and what will not go ──────────────────────────────
        var existing = await db.HostedEventLayoutUnits
            .Include(u => u.PlaceRoom)
            .Where(u => u.HostedEventId == eventId)
            .ToListAsync(ct);

        // ── an id that is not one of ours ────────────────────────────────────
        // Refused, not matched and not created. A stale designer sending a seat somebody removed,
        // and a request carrying another event's seat, would both otherwise become a brand-new unit
        // wearing an old identity — and a booking board that quietly showed it.
        var byId = existing.ToDictionary(u => u.Id);
        var claimed = wanted.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToList();
        if (wanted.FirstOrDefault(c => c.Id is Guid id && !byId.ContainsKey(id)) is { } stranger)
        {
            var elsewhere = await db.HostedEventLayoutUnits.AnyAsync(u => u.Id == stranger.Id, ct);
            return BadRequest(elsewhere
                ? $"{NameForRefusal(stranger)} belongs to another event's plan, so it can't be saved "
                  + "on this one. Reload the plan and try again."
                : $"{NameForRefusal(stranger)} is no longer on this plan — somebody may have removed "
                  + "it since you opened the designer. Reload the plan and try again.");
        }
        if (claimed.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return BadRequest(
                $"{EventCapacity.NameOf(byId[twice.Key])} was sent twice. A unit can only be on the plan once.");

        // Matched on the id when the choice carries one, because that is the unit whatever it is
        // now called: a rename is a rename, not a removal the booking refuses. A choice with no id —
        // the designer's first save, or an older client — matches on the room for a Rooms plan and
        // on the label for a Seats one, the two things a person means by "the same one", and only
        // among the units no id-carrying choice has already claimed, so a new seat given a booked
        // seat's old label cannot become a second write to the same row. Matching on nothing would
        // delete and recreate every unit on every save, and every booking would lose its room.
        var unclaimed = existing.Where(u => !claimed.Contains(u.Id)).ToList();
        HostedEventLayoutUnit? Match(HostedEventLayoutUnitChoice choice)
            => choice.Id is Guid id ? byId[id]
             : request.Kind == HostedEventLayoutKind.Rooms
                ? unclaimed.FirstOrDefault(u => u.PlaceRoomId == choice.PlaceRoomId)
                : unclaimed.FirstOrDefault(u => u.Label != null
                    && string.Equals(u.Label, Trimmed(choice.Label), StringComparison.OrdinalIgnoreCase));

        // What a refusal calls a choice that is not (yet) a unit: its label, else its room's name.
        string NameForRefusal(HostedEventLayoutUnitChoice choice)
            => Trimmed(choice.Label)
            ?? (choice.PlaceRoomId is Guid r && roomNames.TryGetValue(r, out var room) ? room : null)
            ?? "One of those units";

        var keeping = wanted.Select(Match).Where(u => u is not null).Select(u => u!.Id).ToHashSet();
        var removing = existing.Where(u => !keeping.Contains(u.Id)).ToList();

        if (removing.Count > 0)
        {
            var removingIds = removing.Select(u => u.Id).ToList();
            var stillBooked = await db.HostedEventBookingNights
                .Include(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
                .Where(n => n.HostedEventBooking.HostedEventId == eventId
                         && n.HostedEventBooking.Status == HostedEventBookingStatus.Confirmed
                         && n.HostedEventLayoutUnitId != null
                         && removingIds.Contains(n.HostedEventLayoutUnitId.Value))
                .Select(n => n.HostedEventLayoutUnit!)
                .ToListAsync(ct);

            if (stillBooked.Count > 0)
            {
                // One entry per unit, in plan order, so the sentence and the ids name the same
                // seats in the same order and a party booked for three nights is not "C4 and C4
                // and C4". A 409 with the record, not a 400 with the sentence: the designer needs
                // the ids to ring the seats, and the status is how it knows they are there.
                var booked = stillBooked
                    .GroupBy(u => u.Id).Select(g => g.First())
                    .OrderBy(u => u.SortOrder).ToList();
                var names = booked.Select(EventCapacity.NameOf).Distinct().ToList();
                return Conflict(new LayoutRefusalRecord(
                    $"{string.Join(" and ", names)} still {(names.Count == 1 ? "has" : "have")} "
                  + "confirmed bookings. Move those parties first.",
                    booked.Select(u => u.Id).Distinct().ToList()));
            }

            db.HostedEventLayoutUnits.RemoveRange(removing);
        }

        // A choice that names an existing unit AND a different room is asking for the swap the
        // comment below refuses. Said out loud here rather than quietly ignored, because a
        // designer that appears to save a change it did not make is worse than one that explains.
        if (request.Kind == HostedEventLayoutKind.Rooms)
        {
            var repointed = wanted
                .Select(c => (Choice: c, Unit: Match(c)))
                .Where(x => x.Unit is not null
                         && x.Choice.PlaceRoomId is not null
                         && x.Unit!.PlaceRoomId != x.Choice.PlaceRoomId)
                .Select(x => x.Unit!)
                .ToList();
            if (repointed.Count > 0)
            {
                var names = repointed.Select(EventCapacity.NameOf).Distinct().ToList();
                return Conflict(new LayoutRefusalRecord(
                    $"{string.Join(" and ", names)} cannot be changed into a different room. "
                  + "Take it off the plan and add the other room instead, so anybody booked into it "
                  + "is moved on purpose.",
                    repointed.Select(u => u.Id).Distinct().ToList()));
            }
        }

        // ── writing the plan ─────────────────────────────────────────────────
        for (var i = 0; i < wanted.Count; i++)
        {
            var choice = wanted[i];
            var row = Match(choice);
            if (row is null)
            {
                row = new HostedEventLayoutUnit
                {
                    Id = Guid.NewGuid(),
                    HostedEventId = eventId,
                    DateCreated = DateTime.UtcNow,
                    CreatedByAppUserId = userId.Value,
                };
                db.HostedEventLayoutUnits.Add(row);
            }
            else
            {
                row.DateUpdated = DateTime.UtcNow;
                row.UpdatedByAppUserId = userId.Value;
            }

            // Written only when the row is NEW. A unit's room is its identity, not a property of
            // it: repointing an existing unit at another room would move whoever is confirmed into
            // it without telling anybody, and the honest way to stop offering the Blue Room and
            // start offering the Red one is to take one off the plan and add the other — where the
            // removal is refused by name if a party is in it. It also cannot be allowed
            // mechanically: (HostedEventId, PlaceRoomId) is uniquely indexed, so two units swapping
            // rooms in one save collides inside a single SaveChanges, and the guest would see a
            // database error rather than a sentence. A Seats unit never has a room at all.
            if (row.PlaceRoomId is null && request.Kind == HostedEventLayoutKind.Rooms)
                row.PlaceRoomId = choice.PlaceRoomId;
            // A Rooms unit deliberately keeps no label, so renaming the venue's room renames it
            // everywhere at once instead of leaving last year's name on this year's plan.
            row.Label = request.Kind == HostedEventLayoutKind.Rooms ? null : Trimmed(choice.Label);
            row.Section = Trimmed(choice.Section);
            // A seat holds exactly one person, whatever was sent. A seat that holds three is not a
            // seat, and one existing would quietly break every count that trusts the number.
            row.Capacity = request.Kind == HostedEventLayoutKind.Seats
                ? 1
                : choice.Capacity is int c && c >= 0 ? c : null;
            row.Price = choice.Price is decimal p && p >= 0 ? decimal.Round(p, 2) : null;
            row.Note = Trimmed(choice.Note);
            // Taken as a pair: half a position is not a place on a plan, and storing one would
            // leave a unit the designer could draw but never find again.
            var placed = choice.LayoutRow is not null && choice.LayoutColumn is not null;
            row.LayoutRow = placed ? choice.LayoutRow : null;
            row.LayoutColumn = placed ? choice.LayoutColumn : null;
            // Position in the list is the order, as everywhere else a screen sends a list it has
            // already arranged.
            row.SortOrder = i;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await LayoutAsync(db, ev, ct));
    }

    /// <summary>The plan as every screen reads it.</summary>
    internal static async Task<HostedEventLayoutRecord> LayoutAsync(
        BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var units = await db.HostedEventLayoutUnits
            .AsNoTracking()
            .Include(u => u.PlaceRoom)
            .Where(u => u.HostedEventId == ev.Id)
            .OrderBy(u => u.SortOrder)
            .ToListAsync(ct);

        return new HostedEventLayoutRecord(
            ev.Id,
            ev.LayoutKind,
            ev.DayPassCapacity,
            ev.DayPassPrice,
            units.Select(ToUnitRecord).ToList());
    }

    internal static HostedEventLayoutUnitRecord ToUnitRecord(HostedEventLayoutUnit u) => new(
        u.Id,
        u.PlaceRoomId,
        EventCapacity.NameOf(u),
        u.Section,
        u.PlaceRoom?.Floor,
        u.PlaceRoom?.BedNote,
        EventCapacity.CapacityOf(u),
        u.Capacity,
        u.Price,
        u.Note,
        u.LayoutRow,
        u.LayoutColumn,
        u.SortOrder);

    /// <summary>The kind, in the words a refusal uses.</summary>
    private static string Describe(HostedEventLayoutKind kind)
        => kind == HostedEventLayoutKind.Seats ? "seating" : "rooms";

    /// <summary>
    /// Changes how guests get a place: they pick one, or they ask and are placed.
    /// </summary>
    /// <remarks>
    /// <para><b>Its own endpoint, not a field on the save form</b>, because it is the one setting
    /// on this screen that would change the meaning of bookings that already exist. Switching a
    /// Pick event to Ask turns everybody's held seats into nothing; switching the other way turns
    /// their asks into places they never chose. Both are silent, and both are found out on the
    /// night.</para>
    ///
    /// <para>So it is refused once anybody is waiting on an answer, and the refusal says how many
    /// and what to do — decide them, and the switch is free again.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/booking-mode")]
    public async Task<ActionResult<HostedEventRecord>> SetBookingMode(
        Guid orgId, Guid eventId, [FromBody] SetHostedEventBookingModeRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        if (hosted.BookingMode == request.Mode)
            return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);

        var live = await db.HostedEventBookings.CountAsync(
            b => b.HostedEventId == eventId
              && (b.Status == HostedEventBookingStatus.Requested
               || b.Status == HostedEventBookingStatus.Confirmed), ct);

        if (live > 0)
            return Conflict(
                $"{live} {(live == 1 ? "party is" : "parties are")} already booked or waiting on "
                + "this event, and changing how places are chosen would change what their bookings "
                + "mean. Decide the ones that are waiting first, or make a new event.");

        hosted.BookingMode = request.Mode;
        hosted.DateUpdated = DateTime.UtcNow;
        hosted.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);
    }

    /// <summary>Everything standing between this event and going live.</summary>
    /// <remarks>
    /// The same list the publish button refuses from, so the page can never offer a publish the
    /// server is about to bounce. Readable by any member: knowing what is missing is not a billing
    /// question.
    /// </remarks>
    [HttpGet("{eventId:guid}/readiness")]
    public async Task<ActionResult<IReadOnlyList<HostedEventReadinessItem>>> Readiness(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .Include(e => e.LayoutUnits)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        return Ok(HostedEventReadiness.Describe(
            hosted, await Services.Venues.VenueGrants.ForEventAsync(db, hosted, ct)));
    }

    /// <summary>
    /// Un-does a cancellation, for the one that was a mis-click.
    /// </summary>
    /// <remarks>
    /// <para>Back to Draft, not back to Published. Everybody who had a place has already been told
    /// it was off, so putting it back in front of strangers is a decision somebody makes on
    /// purpose. Publishing again costs nothing: <c>FirstPublishedUtc</c> remembers it was paid for.
    /// </para>
    ///
    /// <para><b>The state, not only the stamp.</b> Clearing the timestamp alone left the event
    /// Cancelled for ever — the same fault the cancel endpoint had, found by auditing rather than
    /// by a test, because nothing yet un-cancels anything.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/uncancel")]
    public async Task<ActionResult<HostedEventRecord>> Uncancel(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        if (hosted.LifecycleState is HostedEventLifecycleState.VenueWithdrawn)
            return BadRequest("The venue withdrew from this event, so it is not yours to put back "
                            + "on. Make a new one wherever it is happening now.");

        if (hosted.LifecycleState is HostedEventLifecycleState.Removed)
            return BadRequest("IsHaunted removed this event, so it can't be un-cancelled. Appeal from "
                            + "the event's page instead.");

        if (hosted.LifecycleState is not HostedEventLifecycleState.Cancelled)
            return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);

        var now = DateTime.UtcNow;

        hosted.LifecycleState = HostedEventLifecycleState.Draft;
        hosted.CancelledAtUtc = null;
        hosted.CancelledReason = null;
        // Bringing it back reopens the decision it was called off by, so the reminders can run
        // again. Leaving it decided would leave a live event permanently marked "no go".
        if (hosted.GoNoGoDecision == HostedEventGoNoGo.NoGo)
        {
            hosted.GoNoGoDecision = HostedEventGoNoGo.Undecided;
            hosted.GoNoGoDecidedUtc = null;
            hosted.GoNoGoDecidedByAppUserId = null;
            hosted.GoNoGoWeekReminderSent = false;
            hosted.GoNoGoDayReminderSent = false;
        }
        hosted.DateUpdated = now;
        hosted.UpdatedByAppUserId = userId.Value;

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await SaveAndAuditAsync(db, hosted, hosted.Id, userId.Value, ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0] with
        {
            PlanNote = "It's a draft again. Publishing it costs nothing — "
                     + "it was paid for the first time.",
        });
    }

    // ── the work behind the endpoints ────────────────────────────────────────

    private async Task<ActionResult<HostedEventRecord>> SetAsync(
        Guid orgId, Guid eventId, Action<HostedEvent> change, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (hosted is null) return NotFound();

        // A removed event is IsHaunted's to bring back, through an appeal, and nothing on this page changes it.
        if (hosted.LifecycleState is HostedEventLifecycleState.Removed)
            return BadRequest("IsHaunted removed this event. If you think that was a mistake, appeal from the event's page.");

        change(hosted);
        hosted.DateUpdated = DateTime.UtcNow;
        hosted.UpdatedByAppUserId = userId.Value;

        await _sync.SyncAsync(db, hosted, userId.Value, ct);
        await SaveAndAuditAsync(db, hosted, hosted.Id, userId.Value, ct);

        return Ok((await LoadAsync(db, orgId, eventId, ct))[0]);
    }

    /// <summary>
    /// The venue: one already on the site, or one being entered here for the first time.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-11:</b> "They may need to enter the name and address and information
    /// about a new venue we have not listed before." Most will: a hotel, a theatre or a hall
    /// nobody has typed in yet, entered by the person about to run something in it.</para>
    ///
    /// <para><b>An existing place is reused rather than duplicated.</b> The same matcher the
    /// investigation flow uses decides, so entering "The Thomas House Hotel" a second time finds
    /// the first one. Places are shared — the whole site's map, archive and history hang off them,
    /// and two rows for one building splits all of that in half.</para>
    ///
    /// <para><b>A venue is public, always.</b> Every other inline place defaults to a private
    /// residence, the cautious answer where somebody's home is the likely subject. Here the
    /// cautious answer is the opposite, and a residence is refused outright a few lines later.</para>
    /// </remarks>
    private static async Task<(Place? Venue, string? Refusal)> ResolveVenueAsync(
        BenDataContext db, UpsertHostedEventRequest request, Guid userId, CancellationToken ct)
    {
        if (request.PlaceId != Guid.Empty)
        {
            var chosen = await db.Places.FirstOrDefaultAsync(p => p.Id == request.PlaceId, ct);
            return chosen is null ? (null, "That venue could not be found.") : (chosen, null);
        }

        if (request.NewVenue is not { } venue || !venue.HasAnything)
            return (null, "An event needs a venue. Search for one, or enter a new one.");

        if (string.IsNullOrWhiteSpace(venue.Name) && string.IsNullOrWhiteSpace(venue.StreetAddress1))
            return (null, "A new venue needs at least a name or a street address.");

        // Offered as an existing place rather than made again. The matcher wants coordinates to be
        // sure, and treats "nobody could geocode either of these" as close enough to match — which
        // is right, because a place nobody can geocode is exactly the one typed in twice.
        var nearby = await db.Places
            .Where(p => p.City == venue.City && p.State == venue.State)
            .Take(200)
            .ToListAsync(ct);

        var existing = nearby.FirstOrDefault(p => Services.Places.PlaceMatcher.IsProbableMatch(
            p, venue.StreetAddress1, venue.City, venue.State, venue.ZipCode, venue.Name,
            venue.Latitude, venue.Longitude));

        if (existing is not null) return (existing, null);

        var place = new Place
        {
            Id = Guid.NewGuid(),
            Name = Trimmed(venue.Name),
            StreetAddress1 = Trimmed(venue.StreetAddress1),
            StreetAddress2 = Trimmed(venue.StreetAddress2),
            City = Trimmed(venue.City),
            State = Trimmed(venue.State),
            ZipCode = Trimmed(venue.ZipCode),
            Country = Trimmed(venue.Country) ?? "US",
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        // Best effort, and never a reason to refuse: a venue with no coordinates is a venue that
        // does not appear on the map yet, which is a smaller problem than not being able to run an
        // event there at all.
        await Ben.Service.RepositoryService.Services.PlaceGeocoder.GeocodeAsync(
            place, trustSuppliedCoordinates: true, ct);

        db.Places.Add(place);

        // The place comes back rather than just its id, because it has not been saved yet and the
        // validation below would not find it by querying — a new venue would be refused as one
        // that could not be found, which is a confusing way to be told nothing is wrong.
        return (place, null);
    }

    /// <summary>Why this event cannot be saved as asked, or null.</summary>
    private async Task<string?> ValidateAsync(
        BenDataContext db, Guid orgId, UpsertHostedEventRequest request, Place venue,
        Guid? existingId, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return "An event needs a name.";
        if (name.Length > 160) return "That name is too long — 160 characters at most.";

        if (await db.HostedEvents.AnyAsync(
                e => e.OrganizationId == orgId && e.Name == name && e.Id != existingId, ct))
            return $"You already have an event called {name}. "
                 + "Two events are told apart by their names, so give this one a name of its own.";

        if (request.EndsOn.Date < request.StartsOn.Date)
            return "The last date is before the first one.";

        var span = (request.EndsOn.Date - request.StartsOn.Date).Days + 1;
        if (span > MaximumDates)
            return $"That spans {span} days. An event runs for at most {MaximumDates}.";

        if (request.TimeZoneId?.Trim() is { Length: > 0 } tz)
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            { return $"'{tz}' is not a time zone this server knows."; }
        }

        // The same rule a public calendar event follows. An event is published by definition, and
        // publishing somebody's home is not the host's to decide.
        if (venue.Kind == PlaceKind.PrivateResidence)
            return "That venue is a private residence, so an event cannot be held there publicly. "
                 + "Events are for venues, landmarks and businesses.";

        if (request.AccessNotes is { Length: > 2000 })
            return "Keep what guests need to know about getting in and around under 2,000 characters.";

        if (request.DayPassCapacity is < 0)
            return "A number of day passes cannot be negative. Leave it empty for no limit, "
                 + "or zero for none at all.";

        if (request.DayPassPrice is < 0)
            return "A price cannot be negative. Leave it empty for \"ask us\", or zero for free.";

        // The database has this as a check constraint, which would answer a 500. A person typing
        // five minutes into a box deserves a sentence.
        if (request.HoldMinutes is < 15 or > 20160)
            return "A hold lasts between 15 minutes and 14 days. Less than a quarter of an hour is "
                 + "not long enough to type a party's names into; more than a fortnight is a "
                 + "booking nobody has confirmed.";

        if (request.MinimumGuests is < 1)
            return "A minimum of nobody is not a minimum. Leave it empty if the event runs whatever "
                 + "the numbers come to.";

        if (request.GoNoGoDeadlineUtc is { } deadline && deadline.Date > request.StartsOn.Date)
            return "The go or no-go date is after the event starts, which is too late to be a "
                 + "decision. Pick a date before it.";

        if (request.MinimumGuests is null && request.GoNoGoDeadlineUtc is not null)
            return "A go or no-go date needs a minimum number to decide against.";

        if (request.VenueArrangement == HostedEventVenueArrangement.Self
            && (!string.IsNullOrWhiteSpace(request.VenueContactName)
                || request.VenueAgreedOnUtc is not null))
            return "You have said this is your own venue and also named somebody who agreed to it. "
                 + "Pick one: your own venue, or an arrangement made with somebody else.";

        if (request.DatesAreSeparate)
        {
            var dates = request.Dates ?? [];
            if (dates.Count == 0)
                return "A run needs its dates. Add at least one performance date, "
                     + "or turn off separate dates to make this one stay.";

            if (dates.Count > MaximumDates)
                return $"That is {dates.Count} dates. An event runs on at most {MaximumDates}.";

            if (dates.Any(d => d.Date < request.StartsOn.Date || d.Date > request.EndsOn.Date))
                return "Every date has to fall between the first and last dates of the run.";
        }

        return null;
    }

    /// <summary>
    /// The dates an event should have: every day of a stay, or the ones a run names.
    /// </summary>
    private static IEnumerable<HostedEventNight> BuildNights(
        HostedEvent hosted, IReadOnlyList<DateTime>? dates, Guid userId, DateTime now)
    {
        var wanted = hosted.DatesAreSeparate
            ? (dates ?? []).Select(d => d.Date).Distinct().OrderBy(d => d)
            : HostedEventCalendarSync.DatesOfAStay(hosted.StartsOn, hosted.EndsOn);

        var order = 0;
        foreach (var date in wanted)
        {
            yield return new HostedEventNight
            {
                Id = Guid.NewGuid(),
                HostedEventId = hosted.Id,
                Date = date,
                SortOrder = order++,
                DateCreated = now,
                CreatedByAppUserId = userId,
            };
        }
    }

    /// <summary>
    /// Brings the dates in line with the event's span, keeping the ones that survive it.
    /// </summary>
    /// <remarks>
    /// Kept rather than rebuilt, because a date carries its own title, times and notes, and in
    /// later phases its menu and its bookings. Moving a weekend by a day must not throw away what
    /// the host wrote about the Saturday.
    /// </remarks>
    private static async Task ReconcileNightsAsync(
        BenDataContext db, HostedEvent hosted, IReadOnlyList<DateTime>? dates, Guid userId,
        CancellationToken ct)
    {
        var wanted = (hosted.DatesAreSeparate
                ? (dates ?? hosted.Nights.Select(n => n.Date).ToList()).Select(d => d.Date)
                : HostedEventCalendarSync.DatesOfAStay(hosted.StartsOn, hosted.EndsOn))
            .Distinct().OrderBy(d => d).ToList();

        var existing = hosted.Nights.ToList();
        var now = DateTime.UtcNow;

        foreach (var gone in existing.Where(n => !wanted.Contains(n.Date.Date)))
        {
            // A letter sent to the people there that night stays in the history, addressed to the whole event now.
            foreach (var letter in await db.HostedEventAnnouncements.Where(a => a.HostedEventNightId == gone.Id).ToListAsync(ct))
                letter.HostedEventNightId = null;
            db.HostedEventNights.Remove(gone);
            hosted.Nights.Remove(gone);
        }

        var order = 0;
        foreach (var date in wanted)
        {
            var night = hosted.Nights.FirstOrDefault(n => n.Date.Date == date);
            if (night is null)
            {
                night = new HostedEventNight
                {
                    Id = Guid.NewGuid(),
                    HostedEventId = hosted.Id,
                    Date = date,
                    DateCreated = now,
                    CreatedByAppUserId = userId,
                };
                db.HostedEventNights.Add(night);
                hosted.Nights.Add(night);
            }
            night.SortOrder = order++;
        }

        await Task.CompletedTask;
    }

    private async Task<IReadOnlyList<HostedEventRecord>> LoadAsync(
        BenDataContext db, Guid orgId, Guid? eventId, CancellationToken ct)
    {
        var query = db.HostedEvents.AsNoTracking()
            .Where(e => e.OrganizationId == orgId);
        if (eventId is { } id) query = query.Where(e => e.Id == id);

        var rows = await query
            // Filed-away events last. Read from the state, like everything else, so an event
            // archived by the job and one archived by hand sort the same way.
            .OrderBy(e => e.LifecycleState == HostedEventLifecycleState.Archived)
            .ThenBy(e => e.StartsOn)
            .Select(e => new
            {
                Event = e,
                OrganizationName = e.Organization.Name,
                Place = e.Place,
                Nights = e.Nights.OrderBy(n => n.Date).ToList(),
                UmbrellaId = db.OrgCalendarEvents
                    .Where(c => c.HostedEventId == e.Id)
                    .Select(c => (Guid?)c.Id).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new HostedEventRecord(
            r.Event.Id, r.Event.OrganizationId, r.OrganizationName,
            r.Event.Name, r.Event.UrlName, r.Event.Tagline, r.Event.Description,
            r.Event.PlaceId, PlaceLabel(r.Place), r.Event.HideExactLocation,
            r.Event.TimeZoneId, r.Event.StartsOn, r.Event.EndsOn,
            r.Event.DatesAreSeparate, DateNoun(r.Event),
            r.Event.DefaultStartLocal, r.Event.DefaultEndLocal,
            r.Event.LifecycleState, r.Event.FirstPublishedUtc,
            r.Event.DayPassCapacity, r.Event.ContactLine, r.Event.CoverUploadFileId,
            r.Event.MailSubjectTemplate, r.Event.MailBodyTemplate, r.Event.CollectsEvidence,
            r.Event.ArchivedAtUtc, r.Event.CancelledAtUtc, r.Event.CancelledReason,
            r.UmbrellaId,
            [.. r.Nights.Select(n => ToNight(n, r.Event))],
            PlanNote: null,
            BookingMode: r.Event.BookingMode,
            HoldMinutes: r.Event.HoldMinutes,
            DayPassPrice: r.Event.DayPassPrice,
            BookingsCloseAtUtc: r.Event.BookingsCloseAtUtc,
            VenueArrangement: r.Event.VenueArrangement,
            VenueContactName: r.Event.VenueContactName,
            VenueAgreedOnUtc: r.Event.VenueAgreedOnUtc,
            VenueReference: r.Event.VenueReference,
            MinimumGuests: r.Event.MinimumGuests,
            GoNoGoDeadlineUtc: r.Event.GoNoGoDeadlineUtc,
            GoNoGoDecision: r.Event.GoNoGoDecision,
            GoNoGoDecidedUtc: r.Event.GoNoGoDecidedUtc,
            LiveAtUtc: r.Event.LiveAtUtc,
            EndedAtUtc: r.Event.EndedAtUtc,
            AccessNotes: r.Event.AccessNotes))];
    }

    internal static HostedEventNightRecord ToNight(HostedEventNight night, HostedEvent hosted)
        => new(night.Id, night.Date,
               night.Title?.Trim() is { Length: > 0 } t ? t : night.Date.ToString("dddd, MMMM d"),
               night.Title, night.StartLocal ?? hosted.DefaultStartLocal,
               night.EndLocal ?? hosted.DefaultEndLocal, night.Notes, night.SortOrder);

    /// <summary>
    /// "night" for a stay, "date" for a run.
    /// </summary>
    /// <remarks>
    /// Decided once on the server so nine screens do not each pick a word. A performance of a
    /// monthly play is not a "night" of anything, and calling it one reads as though the audience
    /// were staying over.
    /// </remarks>
    internal static string DateNoun(HostedEvent hosted) => hosted.DatesAreSeparate ? "date" : "night";

    private static string PlaceLabel(Place? place)
    {
        if (place is null) return "";
        var parts = new[] { place.Name, place.City, place.State }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }

    private static HostedEventPlanRecord ToPlan(HostedEventEntitlement.Verdict v)
        => new(
            v.Kind == HostedEventEntitlement.EntitlementKind.Plan ? "plan" : "credit",
            v.Refusal,
            PlanSentence(v),
            v.LiveNow, v.Ceiling, v.CreditsAvailable,
            v.MayPublish, v.SpendsACredit);

    /// <summary>What the page says before anybody presses publish.</summary>
    private static string PlanSentence(HostedEventEntitlement.Verdict v)
    {
        if (v.Refusal is { } refusal) return refusal;

        if (v.Kind == HostedEventEntitlement.EntitlementKind.Plan)
        {
            return v.Ceiling is { } cap
                ? $"Your plan covers {cap} event(s) running at once, and {v.LiveNow} of them are live."
                : "Your plan covers as many events as you want to run at once.";
        }

        return $"Publishing spends one of your {v.CreditsAvailable} event credit(s). "
             + "It covers this event from the day you publish it, and is not returned if you "
             + "take the event down.";
    }

    /// <summary>
    /// Any active member may READ the events; changing them keeps the settings key.
    /// </summary>
    /// <remarks>
    /// The same split tours use. Seeing what the group is putting on is ordinary membership;
    /// creating one is a step towards spending money, and that keeps the key that opens billing.
    /// </remarks>
    private async Task<bool> IsMemberAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await Services.Access.FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);

    private string? Clean(string? markup)
        => string.IsNullOrWhiteSpace(markup) ? null : _sanitizer.SanitizeHtml(markup);

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
