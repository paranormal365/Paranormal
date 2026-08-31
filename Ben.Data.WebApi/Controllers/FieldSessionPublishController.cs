using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Places;
using Ben.Service.RepositoryService.Services;
using Ben.Data.WebApi.Services.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Putting one of your own field sessions into a place's public archive, and taking it back out.
/// </summary>
/// <remarks>
/// <para><b>What the archive is for.</b> One person's readings at a location are an anecdote.
/// Twelve people's readings at the same location, over two years, are evidence of something
/// persistent — or evidence that the one spike was noise. Nothing in this field can currently
/// tell those apart, because every recording dies on the phone that made it. This endpoint is
/// the door that ends that.</para>
///
/// <para><b>Public places only, and that is the safety hinge.</b> <see cref="PlaceKind"/>
/// defaults to <see cref="PlaceKind.PrivateResidence"/>, so the refusal here is what stops
/// somebody publishing sensor readings, timings and coordinates taken inside a stranger's home.
/// The paid lane exists precisely so private-residence work never has to be public; this is the
/// same rule arriving from the other side.</para>
///
/// <para><b>Publishing is reversible, deliberately.</b> Somebody who realises they published the
/// wrong night, or simply changes their mind, must be able to take it back without asking
/// anybody — so retraction is a plain DELETE, not a support request. What has already been read
/// by others cannot be unread, and the UI says so before the first publish rather than after the
/// retraction.</para>
///
/// <para><b>The document only, for now.</b> Publishing shares the session's readings — the
/// numbers, the markers, the timeline. Photos and audio stay private until the archive has the
/// same moderation the feed has (screening, reporting, blocking); shipping strangers' media on a
/// public page without it would be the feed's problem with none of the feed's answers.</para>
/// </remarks>
[ApiController]
[Route("api/field-sessions/{id:guid}/publish")]
[Authorize]
public sealed class FieldSessionPublishController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFeedMediaScreener _screener;
    private readonly ILogger<FieldSessionPublishController> _log;

    public FieldSessionPublishController(
        IDbContextFactory<BenDataContext> db, IFeedMediaScreener screener,
        ILogger<FieldSessionPublishController> log)
    {
        _db = db;
        _screener = screener;
        _log = log;
    }

    /// <param name="PlaceId">An existing public location, when the person picked one.</param>
    /// <param name="NewPlace">
    /// Details for a location with no record yet. Exactly one of the two is used, and this one is
    /// why the archive can exist at all: until now a place could ONLY be created through the
    /// investigation flow, which needs an investigation, which needs an organization — so the free
    /// individual the archive is FOR had no way to say where they had been.
    /// </param>
    public sealed record PublishRequest(Guid? PlaceId, NewArchivePlace? NewPlace = null);

    /// <summary>A public location somebody is naming for the first time.</summary>
    /// <remarks>
    /// No <c>Kind</c>: everything created here is a <see cref="PlaceKind.PublicLocation"/> by
    /// construction. Letting the caller choose would hand them the one switch this whole feature's
    /// safety rests on.
    /// </remarks>
    public sealed record NewArchivePlace(
        string Name, string? StreetAddress1, string? City, string? State, string? ZipCode,
        decimal? Latitude, decimal? Longitude);

    /// <summary>
    /// Public locations near a point, so somebody publishing is OFFERED the archive that already
    /// exists there instead of starting a second one beside it.
    /// </summary>
    /// <remarks>
    /// <para><b>This endpoint is the duplicate-prevention.</b> Matching on submitted text catches
    /// the easy cases and missed a real one on the feature's first three-person run — two people
    /// described one cave differently and got two pages. A person shown "Bell Witch Cave · 3
    /// sessions · 300 ft away" picks it; no string comparison has to be clever.</para>
    ///
    /// <para><b>Offered wider than merged.</b> A mile here against the matcher's tenth: showing
    /// somebody a place they then decline costs a glance, while auto-merging at a mile would pool
    /// half a town. Offering is allowed to be generous precisely because a person decides.</para>
    ///
    /// <para>Session counts are included because they are the reason to pick one — a place with
    /// an archive is a place worth adding to.</para>
    /// </remarks>
    [HttpGet("/api/places/archive-candidates")]
    public async Task<ActionResult<IReadOnlyList<ArchivePlaceCandidate>>> Candidates(
        [FromQuery] decimal? latitude, [FromQuery] decimal? longitude,
        [FromQuery] string? name, CancellationToken ct)
    {
        if (GetCurrentUserId() == Guid.Empty) return Unauthorized();

        // Without coordinates there is nothing to be near. An empty list rather than a refusal:
        // a session recorded with location declined is a normal session, and its owner should
        // meet "name where you were" rather than an error.
        if (latitude is not { } lat || longitude is not { } lon)
            return Ok(Array.Empty<ArchivePlaceCandidate>());

        await using var db = await _db.CreateDbContextAsync(ct);

        // A coarse degree box first so the database does the volume, then the real distance in
        // memory — a mile is ~0.0145 degrees of latitude, and this box is deliberately generous.
        const decimal box = 0.03m;
        var nearby = await db.Places.AsNoTracking()
            .Where(p => p.Kind == PlaceKind.PublicLocation
                     && p.Latitude != null && p.Longitude != null
                     && p.Latitude > lat - box && p.Latitude < lat + box
                     && p.Longitude > lon - box && p.Longitude < lon + box)
            .Select(p => new
            {
                p.Id, p.Name, p.City, p.State, p.Latitude, p.Longitude,
                Sessions = db.FieldSessionUploads
                    .Count(s => s.PlaceId == p.Id && s.PublishedAtUtc != null),
            })
            .ToListAsync(ct);

        var candidates = nearby
            .Select(p => new
            {
                p.Id, p.Name, p.City, p.State, p.Sessions,
                Miles = PlaceMatcher.DistanceMiles(
                    (double)p.Latitude!.Value, (double)p.Longitude!.Value, (double)lat, (double)lon),
            })
            .Where(p => p.Miles <= CandidateRadiusMiles)
            .OrderBy(p => p.Miles)
            .Take(10)
            .Select(p => new ArchivePlaceCandidate(
                p.Id, p.Name, p.City, p.State, Math.Round(p.Miles, 2), p.Sessions))
            .ToList();

        return Ok(candidates);
    }

    /// <summary>How far out the picker looks. Ten times the radius at which two records merge.</summary>
    public const double CandidateRadiusMiles = 1.0;

    /// <summary>
    /// Flags a published session, hiding its media immediately and until somebody looks.
    /// </summary>
    /// <remarks>
    /// <para><b>The flag acts, then a person decides — not the other way round.</b> Waiting for a
    /// moderator before hiding leaves the thing somebody objected to up for however long that
    /// takes, which is the failure a report exists to prevent. Hiding first costs a contributor
    /// some visibility for a while; not hiding costs somebody whatever the picture was.</para>
    ///
    /// <para><b>The readings stay.</b> A flag is about what a photograph shows; magnetic-field
    /// numbers cannot be objectionable, and pulling the whole session would let one flag erase a
    /// contribution to the archive rather than hide a picture.</para>
    ///
    /// <para><b>One flag is enough, for now.</b> It is trivially abusable at scale — anybody can
    /// suppress anybody — and the answer then is to require several distinct reporters. At this
    /// size the abuse does not exist yet and the latency does, so the balance sits here, and is
    /// worth revisiting the first time somebody games it.</para>
    /// </remarks>
    [HttpPost("/api/field-sessions/{id:guid}/flag")]
    public async Task<IActionResult> Flag(Guid id, [FromBody] FlagRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var session = await db.FieldSessionUploads
            .FirstOrDefaultAsync(s => s.Id == id && s.PublishedAtUtc != null, ct);
        if (session is null) return NotFound();

        // Already held is already held. Flagging twice is one flag, and the reporter is told the
        // same thing either way — whether somebody else already objected is not their business.
        if (session.MediaReviewState != FeedMediaReviewState.Held)
        {
            session.MediaReviewState = FeedMediaReviewState.Held;
            session.MediaReviewNote = string.IsNullOrWhiteSpace(request?.Reason)
                ? "Flagged by a reader."
                : $"Flagged: {request!.Reason!.Trim()}";
            // Cleared so the queue shows this as awaiting a decision even if a reviewer had
            // approved it before.
            session.MediaReviewedUtc = null;
            session.MediaReviewedByAppUserId = null;
            session.DateUpdated = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            _log.LogInformation(
                "Field session {SessionId} media held after a flag from {UserId}.", id, userId);
        }

        return NoContent();
    }

    /// <param name="Reason">Optional, and shown to the moderator rather than to the contributor.</param>
    public sealed record FlagRequest(string? Reason);

    /// <param name="Miles">Distance from where the session was recorded, for ordering and for
    /// the person to judge — "0.02 miles" is the same cave, "0.8 miles" is probably not.</param>
    /// <param name="PublishedSessions">How much archive is already here. The reason to pick it.</param>
    public sealed record ArchivePlaceCandidate(
        Guid Id, string? Name, string? City, string? State, double Miles, int PublishedSessions);

    /// <summary>Publishes this session to a place's archive.</summary>
    [HttpPost]
    public async Task<IActionResult> Publish(
        Guid id, [FromBody] PublishRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var session = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == id, ct);
        // Whether somebody else's session exists is not a thing to let an outsider probe for —
        // the same answer as absent, as everywhere else in this controller family.
        if (session is null || session.SubmittedByAppUserId != userId) return NotFound();

        var (place, refusal) = await ResolvePlaceAsync(db, request, userId, ct);
        if (refusal is not null) return BadRequest(refusal);
        if (place is null) return NotFound("That place doesn't exist.");

        if (place.Kind != PlaceKind.PublicLocation)
            return BadRequest(
                "Only public locations have an open archive. A session recorded at somebody's "
              + "home stays with you and your group — that is what the private lane is for.");

        session.PlaceId = place.Id;
        // Re-publishing an already-public session keeps its original date: the answer to "when
        // did this become public" must not move because somebody pressed the button twice.
        session.PublishedAtUtc ??= DateTime.UtcNow;

        // ── the media, if there is any ────────────────────────────────────────
        // Post-moderation, Ben's call 2026-08-30: publish first, pull on the first flag. Pre-
        // screening every night means a queue somebody must work daily before anything appears,
        // and a solo operator either staffs it or the archive silently never fills. Flagging
        // moves that cost onto the rare bad case — which is how essentially every UGC platform
        // actually runs, and Apple's 1.2 asks for a report path and TIMELY action rather than for
        // pre-approval.
        //
        // Only ever screened on the FIRST publish: a decision must not be undone by its owner
        // republishing.
        if (session.MediaReviewState == FeedMediaReviewState.Pending
            && session.MediaReviewedUtc is null)
        {
            await ScreenMediaAsync(db, session, ct);
        }
        session.DateUpdated = DateTime.UtcNow;
        session.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Field session {SessionId} published to place {PlaceId} by {UserId}.",
            id, place.Id, userId);

        return NoContent();
    }

    /// <summary>
    /// Asks the screener about this session's captures, and records the strictest answer.
    /// </summary>
    /// <remarks>
    /// <para><b>The strictest wins.</b> A night is reviewed as a night: one file a screener holds
    /// holds the whole session, because a page showing four of somebody's five photographs invites
    /// exactly the question the fifth was withheld to avoid.</para>
    /// <para>A screener that throws leaves the state Pending — the fail-closed default — rather
    /// than approving by accident. An outage must never publish a photograph.</para>
    /// </remarks>
    private async Task ScreenMediaAsync(
        BenDataContext db, Ben.Data.Source.Entities.FieldSessionUpload session, CancellationToken ct)
    {
        // With no automatic screener, publishing wins. The manual screener approves nothing by
        // design — deferring to it here would leave every night waiting on a person forever,
        // which is the pre-moderation cost this feature deliberately does not pay. A real
        // classifier, if one is ever wired up, still gets to hold the obvious cases below.
        if (!_screener.IsAutomatic)
        {
            session.MediaReviewState = FeedMediaReviewState.Approved;
            return;
        }

        var files = await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => f.FieldSessionUploadId == session.Id)
            .Join(db.UploadFiles.AsNoTracking(), f => f.UploadFileId, u => u.Id,
                  (f, u) => new { u.StoragePath, u.ContentType })
            .ToListAsync(ct);

        if (files.Count == 0)
        {
            // Nothing to review. Approved is honest here and keeps a readings-only session out of
            // a queue that would otherwise fill with sessions containing no media at all.
            session.MediaReviewState = FeedMediaReviewState.Approved;
            return;
        }

        foreach (var file in files)
        {
            FeedMediaVerdict verdict;
            try
            {
                verdict = await _screener.ScreenAsync(file.StoragePath, file.ContentType, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Screening failed for session {SessionId}; media stays private.",
                    session.Id);
                session.MediaReviewState = FeedMediaReviewState.Pending;
                return;
            }

            if (verdict.State != FeedMediaReviewState.Approved)
            {
                session.MediaReviewState = verdict.State;
                session.MediaReviewNote = verdict.Reason;
                return;
            }
        }

        session.MediaReviewState = FeedMediaReviewState.Approved;
    }

    /// <summary>
    /// Finds the place being named, creating it when it is genuinely new.
    /// </summary>
    /// <remarks>
    /// <para><b>Matching before creating is what makes the archive an archive.</b> Two people who
    /// describe the same cave differently — "Bell Witch Cave" and "Bell Witch Cave, Adams TN" —
    /// must land on ONE place, or the readings never accumulate and the whole feature is a pile of
    /// single-visit pages. <see cref="PlaceMatcher"/> already owns that rule (same address AND
    /// within 0.1 miles); this reuses it rather than inventing a second one.</para>
    ///
    /// <para>A match wins even when it is a private residence — the caller then meets the ordinary
    /// refusal above. Creating a duplicate public place at a home's coordinates to get around that
    /// is exactly the hole this ordering closes.</para>
    /// </remarks>
    private static async Task<(Place? Place, string? Refusal)> ResolvePlaceAsync(
        BenDataContext db, PublishRequest request, Guid userId, CancellationToken ct)
    {
        if (request.PlaceId is { } id && id != Guid.Empty)
            return (await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct), null);

        if (request.NewPlace is not { } fresh)
            return (null, "Say where this was recorded.");

        if (string.IsNullOrWhiteSpace(fresh.Name))
            return (null, "A place needs a name people will recognise.");

        // Candidates are narrowed in the database by state, then judged by the shared matcher —
        // the radius test needs coordinates the query cannot compare.
        var nearby = await db.Places
            .Where(p => fresh.State == null || p.State == fresh.State)
            .ToListAsync(ct);

        var existing = nearby.FirstOrDefault(p => PlaceMatcher.IsProbableMatch(
            p, fresh.StreetAddress1, fresh.City, fresh.State, fresh.ZipCode, fresh.Name,
            fresh.Latitude, fresh.Longitude));

        // The archive's own, looser rule — same spot, one name inside the other — applied only to
        // public locations. Without it "Bell Witch Cave" and "Bell Witch Cave, Adams" become two
        // pages that each look like nobody has been there, which is the one failure this feature
        // cannot survive. Observed on the very first three-person test.
        existing ??= nearby.FirstOrDefault(p => p.Kind == PlaceKind.PublicLocation
            && PlaceMatcher.IsProbableArchiveMatch(p, fresh.Name, fresh.Latitude, fresh.Longitude));

        if (existing is not null) return (existing, null);

        var place = new Place
        {
            Id = Guid.NewGuid(),
            Name = fresh.Name.Trim(),
            StreetAddress1 = fresh.StreetAddress1?.Trim(),
            City = fresh.City?.Trim(),
            State = fresh.State?.Trim(),
            ZipCode = fresh.ZipCode?.Trim(),
            Country = "US",
            Latitude = fresh.Latitude,
            Longitude = fresh.Longitude,
            // Never from the caller: see NewArchivePlace.
            Kind = PlaceKind.PublicLocation,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        await PlaceGeocoder.GeocodeAsync(place, trustSuppliedCoordinates: true, ct);
        db.Places.Add(place);
        return (place, null);
    }

    /// <summary>Takes it back out of the archive. The place stays; only the publication ends.</summary>
    [HttpDelete]
    public async Task<IActionResult> Retract(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var session = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || session.SubmittedByAppUserId != userId) return NotFound();

        // Retraction is the paid half of the archive's bargain. Publishing stays a deliberate act
        // anybody may perform; pulling it back afterwards is what a free account cannot do,
        // because publish-then-hide is the whole exploit — take the credit for a contribution,
        // then remove it. Choosing never to publish is not gaming anything and is not refused.
        if (await Services.Billing.PaidPlan.WhyCannotKeepPrivateAsync(db, userId, ct) is { } paidOnly)
            return StatusCode(StatusCodes.Status402PaymentRequired, paidOnly);

        // The PLACE is left alone on purpose: where a session happened is a fact about the
        // recording, not a consequence of having shared it, and somebody who retracts today may
        // republish tomorrow without re-answering "where were you".
        session.PublishedAtUtc = null;
        session.DateUpdated = DateTime.UtcNow;
        session.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
