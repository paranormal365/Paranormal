using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Deciding about files people added to a public place (item 250's other half, 10/09/2026).
/// </summary>
/// <remarks>
/// <para>Ben added a video to Cragfont, was told "somebody will look at this before it appears on the
/// page", and then could find nowhere to look at it. <c>PlaceEvidenceController</c> held anything the
/// screener did not clear, and nothing anywhere listed or released it: the moderation pages covered the
/// feed, published field sessions and event evidence, but not a file added straight to a place. Held was
/// permanent.</para>
///
/// <para><b>Who decides.</b> SuperAdmins and moderators, for every place. And, Ben's rule, the people who
/// answer for a place's confirmed venue — the owners and administrators of the group whose claim to the
/// property was approved (<c>VenueNotices.PeopleWhoAnswerForAsync</c>, the same people who answer the
/// venue's other questions) — for that place only.</para>
///
/// <para>Nothing here deletes. Holding keeps the file with a note, so a decision can be changed.</para>
/// </remarks>
[ApiController]
[Route("api/place-files/review")]
[Authorize]
public sealed class PlaceFileReviewController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PlaceFileReviewController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Files waiting on a decision that this person may make, oldest first.</summary>
    /// <param name="includeHeld">Also list what was held, so a decision can be revisited.</param>
    /// <param name="placeId">Only this place — the venue page's own list.</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlaceFileReviewRow>>> List(
        [FromQuery] bool includeHeld, [FromQuery] Guid? placeId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = Reviewable(db, userId, IsModerator())
            .Where(e => e.ReviewState == FeedMediaReviewState.Pending
                     || (includeHeld && e.ReviewState == FeedMediaReviewState.Held));
        if (placeId is Guid only) rows = rows.Where(e => e.PlaceId == only);

        return Ok(await rows
            .OrderBy(e => e.DateCreated)
            .Select(e => new PlaceFileReviewRow(
                e.Id, e.PlaceId, e.Place!.Name,
                e.AddedByAppUser!.DisplayName ?? e.AddedByAppUser.Email ?? "Unknown",
                e.Caption, e.MediaKind,
                e.UploadFile!.FileName, e.UploadFile.ContentType,
                e.DateCreated, e.ReviewState, e.ReviewNote))
            .ToListAsync(ct));
    }

    /// <summary>How many files are waiting for this person, by place — for the "waiting for you" notice.</summary>
    [HttpGet("waiting")]
    public async Task<ActionResult<IReadOnlyList<PlaceFilesWaiting>>> Waiting(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var counts = await Reviewable(db, userId, IsModerator())
            .Where(e => e.ReviewState == FeedMediaReviewState.Pending)
            .GroupBy(e => new { e.PlaceId, e.Place!.Name })
            .Select(g => new { g.Key.PlaceId, g.Key.Name, Count = g.Count() })
            .ToListAsync(ct);

        var placeIds = counts.Select(c => c.PlaceId).ToList();
        var venues = await db.OrganizationVenueProfiles.AsNoTracking()
            .Where(v => placeIds.Contains(v.PlaceId) && v.VerifiedUtc != null)
            .Select(v => new { v.PlaceId, v.OrganizationId })
            .ToListAsync(ct);

        return Ok(counts
            .Select(c => new PlaceFilesWaiting(c.PlaceId, c.Name, c.Count,
                venues.FirstOrDefault(v => v.PlaceId == c.PlaceId)?.OrganizationId))
            .OrderBy(w => w.PlaceName)
            .ToList());
    }

    /// <summary>The file itself, whatever its state, for the person deciding about it.</summary>
    /// <remarks>
    /// A route of its own: the place page's route serves only what is approved, which is its whole job,
    /// so a file could never be watched before deciding about it through that one.
    /// </remarks>
    [HttpGet("{evidenceId:guid}/file")]
    public async Task<IActionResult> GetFile(
        Guid evidenceId,
        [FromServices] IMediaIngestService mediaIngest,
        [FromServices] IFileStorageService storage,
        CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var file = await Reviewable(db, userId, IsModerator())
            .Where(e => e.Id == evidenceId)
            .Select(e => new { e.UploadFile!.StoragePath, e.UploadFile.ContentType })
            .FirstOrDefaultAsync(ct);
        if (file?.StoragePath is not { Length: > 0 } storagePath) return NotFound();

        var servingPath = mediaIngest.ServingPathFor(storagePath);
        if (!storage.Exists(servingPath)) return NotFound();

        return File(await storage.OpenReadAsync(servingPath, ct),
                    file.ContentType ?? "application/octet-stream",
                    enableRangeProcessing: true);
    }

    /// <summary>Approves a file onto its place's page, or holds it there with a note.</summary>
    /// <remarks>Deciding the other way later is how a mistake is undone.</remarks>
    [HttpPost("{evidenceId:guid}")]
    public async Task<IActionResult> Decide(
        Guid evidenceId, [FromBody] PlaceFileDecision request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var mayDecide = await Reviewable(db, userId, IsModerator()).AnyAsync(e => e.Id == evidenceId, ct);
        if (!mayDecide) return NotFound();

        var row = await db.PlaceEvidence.FirstAsync(e => e.Id == evidenceId, ct);
        row.ReviewState = request.Approve ? FeedMediaReviewState.Approved : FeedMediaReviewState.Held;
        if (!string.IsNullOrWhiteSpace(request.Note)) row.ReviewNote = request.Note.Trim();
        row.DateUpdated = DateTime.UtcNow;
        row.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private bool IsModerator() => RoleNames.Moderators.Any(User.IsInRole);

    /// <summary>
    /// The files this person may decide about: every one for a moderator; otherwise those at a public
    /// place whose confirmed venue is a group they own or administer.
    /// </summary>
    internal static IQueryable<Source.Entities.PlaceEvidence> Reviewable(BenDataContext db, Guid userId, bool isModerator)
    {
        var files = db.PlaceEvidence.AsNoTracking()
            .Where(e => e.Place!.Kind == PlaceKind.PublicLocation);
        if (isModerator) return files;

        var answerFor = db.OrganizationUserMemberships
            .Where(m => m.AppUserId == userId && m.IsActive
                     && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator))
            .Select(m => m.OrganizationId);
        var theirPlaces = db.OrganizationVenueProfiles
            .Where(v => v.VerifiedUtc != null && answerFor.Contains(v.OrganizationId))
            .Select(v => v.PlaceId);

        return files.Where(e => theirPlaces.Contains(e.PlaceId));
    }
}

/// <summary>A file added to a place, waiting on (or held by) a decision.</summary>
public sealed record PlaceFileReviewRow(
    Guid EvidenceId,
    Guid PlaceId,
    string PlaceName,
    string ContributorName,
    string? Caption,
    PlaceMediaKind Kind,
    string FileName,
    string? ContentType,
    DateTime AddedUtc,
    FeedMediaReviewState State,
    string? Note);

/// <summary>How many files are waiting at one place, and the group confirmed as its venue, if any.</summary>
public sealed record PlaceFilesWaiting(Guid PlaceId, string PlaceName, int Count, Guid? VenueOrganizationId);

/// <summary>Approve, or hold with an optional note.</summary>
public sealed record PlaceFileDecision(bool Approve, string? Note = null);
