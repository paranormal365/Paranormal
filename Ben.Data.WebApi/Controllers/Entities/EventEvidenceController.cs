using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Evidence from public-event attendees: the visitor's door in, and the group's review of it.
/// </summary>
/// <remarks>
/// <para>Item 111, the shape Ben chose: attendees may <b>submit</b>, a member must <b>accept</b>.
/// The submitter proves attendance, not membership — a confirmed
/// <see cref="EventAttendanceInvite"/> for this event, or being on the org's roster (a member who
/// attended submits through the same door rather than a privileged side one, so the record of who
/// offered what stays uniform).</para>
///
/// <para><b>Nothing careless with strangers' uploads:</b> a submission is visible to the
/// submitter and the group until accepted; acceptance at a public event makes it public, which
/// the submitter is told BEFORE submitting (the UI carries the sentence, and the public read
/// endpoint only ever serves accepted rows of public events).</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/events/{eventId:guid}/evidence")]
public sealed class EventEvidenceController : BenControllerBase
{
    /// <summary>Same stored type as case evidence — it is evidence, stored under the org.</summary>
    private static readonly Guid EvidenceFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly PlatformMessageService _messages;

    public EventEvidenceController(
        IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage, PlatformMessageService messages)
    {
        _db          = db;
        _fileStorage = fileStorage;
        _messages    = messages;
    }

    public sealed record EvidenceSubmissionRecord(
        Guid Id, Guid OrgCalendarEventId, string EventTitle,
        string SubmitterDisplayName, Guid UploadFileId, string FileName, string ContentType,
        string? Note, EvidenceSubmissionStatus Status, string? RejectionReason,
        DateTime DateCreated);

    // ── the visitor's door ────────────────────────────────────────────────────

    /// <summary>Offers one file of evidence for the event the caller attended.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<EvidenceSubmissionRecord>> Submit(
        Guid eventId, [FromForm] string? note, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var evt = await db.OrgCalendarEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt is null) return NotFound();

        if (!evt.IsPublic)
            return BadRequest("Only public events take visitor evidence.");

        if (!await AttendedAsync(db, eventId, evt.OrganizationId, userId, ct))
            return BadRequest("Evidence can be offered by people who attended this event.");

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.OrgFilePath(evt.OrganizationId, $"event-evidence/{storedName}");
        await _fileStorage.WriteFormFileAsync(storagePath, file, ct);

        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = EvidenceFileTypeId, AppUserId = userId,
            FileName = file.FileName, StoredFileName = storedName,
            ContentType = file.ContentType, FileSize = file.Length,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

        var submission = new EventEvidenceSubmission
        {
            Id                   = Guid.NewGuid(),
            OrgCalendarEventId   = eventId,
            SubmittedByAppUserId = userId,
            UploadFileId         = uploadFile.Id,
            Note                 = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status               = EvidenceSubmissionStatus.Pending,
            DateCreated          = DateTime.UtcNow,
            CreatedByAppUserId   = userId,
        };
        db.EventEvidenceSubmissions.Add(submission);
        await db.SaveChangesAsync(ct);

        return Ok(await ToRecordAsync(db, submission.Id, ct));
    }

    /// <summary>The caller's own submissions for this event, with their review state.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<EvidenceSubmissionRecord>>> Mine(
        Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        return Ok(await ProjectAsync(db.EventEvidenceSubmissions.AsNoTracking()
            .Where(s => s.OrgCalendarEventId == eventId && s.SubmittedByAppUserId == userId), ct));
    }

    // ── the group's review ────────────────────────────────────────────────────

    /// <summary>Everything waiting on this organization's answer, oldest first.</summary>
    [HttpGet("~/api/organizations/{orgId:guid}/evidence-submissions")]
    public async Task<ActionResult<IEnumerable<EvidenceSubmissionRecord>>> Queue(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await IsOrgMemberAsync(db, orgId, userId, ct)) return Forbid();

        return Ok(await ProjectAsync(db.EventEvidenceSubmissions.AsNoTracking()
            .Where(s => s.OrgCalendarEvent.OrganizationId == orgId
                     && s.Status == EvidenceSubmissionStatus.Pending)
            .OrderBy(s => s.DateCreated), ct));
    }

    public sealed record ReviewEvidenceRequest(bool Accept, string? Reason);

    /// <summary>A member's verdict. Accepting at a public event makes the file public.</summary>
    [HttpPut("~/api/organizations/{orgId:guid}/evidence-submissions/{id:guid}/review")]
    public async Task<ActionResult<EvidenceSubmissionRecord>> Review(
        Guid orgId, Guid id, [FromBody] ReviewEvidenceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await IsOrgMemberAsync(db, orgId, userId, ct)) return Forbid();

        var submission = await db.EventEvidenceSubmissions
            .Include(s => s.OrgCalendarEvent).Include(s => s.UploadFile)
            .FirstOrDefaultAsync(s => s.Id == id && s.OrgCalendarEvent.OrganizationId == orgId, ct);
        if (submission is null) return NotFound();

        if (submission.Status != EvidenceSubmissionStatus.Pending)
            return BadRequest("This submission has already been reviewed.");

        if (!request.Accept && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Give the submitter a reason — a bare no helps nobody.");

        submission.Status              = request.Accept
            ? EvidenceSubmissionStatus.Accepted
            : EvidenceSubmissionStatus.Rejected;
        submission.ReviewedByAppUserId = userId;
        submission.DateReviewed        = DateTime.UtcNow;
        submission.RejectionReason     = request.Accept ? null : request.Reason!.Trim();
        submission.DateUpdated         = submission.DateReviewed;
        submission.UpdatedByAppUserId  = userId;

        // Item 87's bargain, enforced at the byte level: accepted evidence at a public event is
        // public, so the file itself becomes fetchable on the anonymous path the public event
        // page uses. Rejected files stay private to the submitter.
        if (request.Accept)
            submission.UploadFile.IsPublic = true;

        await db.SaveChangesAsync(ct);

        await _messages.SendAsync(
            request.Accept
                ? $"Your evidence from \"{submission.OrgCalendarEvent.Title}\" was accepted"
                : $"Your evidence from \"{submission.OrgCalendarEvent.Title}\" was declined",
            request.Accept
                ? "The group has accepted your submission into the event's record. Evidence at a "
                + "public investigation is part of the public record — thank you for contributing."
                : $"The group declined this submission: {submission.RejectionReason}",
            [submission.SubmittedByAppUserId], userId, ct);

        return Ok(await ToRecordAsync(db, submission.Id, ct));
    }

    // ── the public record ─────────────────────────────────────────────────────

    /// <summary>Accepted evidence at a public event — the visitor half of the public record.</summary>
    [HttpGet("accepted")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<EvidenceSubmissionRecord>>> Accepted(
        Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        // Anonymous, so the event's own publicity is the gate — a private event's accepted
        // submissions are not served here regardless of their state.
        var isPublic = await db.OrgCalendarEvents.AsNoTracking()
            .AnyAsync(e => e.Id == eventId && e.IsPublic, ct);
        if (!isPublic) return NotFound();

        return Ok(await ProjectAsync(db.EventEvidenceSubmissions.AsNoTracking()
            .Where(s => s.OrgCalendarEventId == eventId
                     && s.Status == EvidenceSubmissionStatus.Accepted)
            .OrderBy(s => s.DateCreated), ct));
    }

    /// <summary>
    /// Streams one ACCEPTED submission's bytes anonymously, or 404.
    /// </summary>
    /// <remarks>
    /// The gate runs entirely on the review state before any file row is read — the
    /// authors-see-what-visitors-cannot rule's mechanical half: the public event page is read
    /// signed out, so the bytes must be too, and only acceptance opens them. A pending or
    /// rejected submission, or any submission at a private event, is a 404 indistinguishable
    /// from an id that never existed.
    /// </remarks>
    [HttpGet("{submissionId:guid}/file")]
    [AllowAnonymous]
    public async Task<IActionResult> FileBytes(Guid eventId, Guid submissionId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var allowed = await db.EventEvidenceSubmissions.AsNoTracking()
            .AnyAsync(s => s.Id == submissionId
                        && s.OrgCalendarEventId == eventId
                        && s.Status == EvidenceSubmissionStatus.Accepted
                        && s.OrgCalendarEvent.IsPublic, ct);
        if (!allowed) return NotFound();

        var file = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => s.UploadFile)
            .FirstAsync(ct);

        if (!string.IsNullOrEmpty(file.StoragePath))
        {
            var stream = await _fileStorage.OpenReadAsync(file.StoragePath, ct);
            return File(stream, file.ContentType, file.FileName);
        }

        return file.FileData is not null
            ? File(file.FileData, file.ContentType, file.FileName)
            : NotFound();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Attendance: a confirmed invite for this event, or being on the group's roster.</summary>
    private static async Task<bool> AttendedAsync(
        BenDataContext db, Guid eventId, Guid orgId, Guid userId, CancellationToken ct)
    {
        if (await db.EventAttendanceInvites.AnyAsync(i =>
                i.OrgCalendarEventId == eventId && i.ConfirmedByAppUserId == userId, ct))
            return true;

        return await IsOrgMemberAsync(db, orgId, userId, ct);
    }

    private static Task<bool> IsOrgMemberAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => db.OrganizationUserMemberships.AnyAsync(m =>
            m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

    private static async Task<List<EvidenceSubmissionRecord>> ProjectAsync(
        IQueryable<EventEvidenceSubmission> query, CancellationToken ct) =>
        await query.Select(s => new EvidenceSubmissionRecord(
            s.Id, s.OrgCalendarEventId, s.OrgCalendarEvent.Title,
            s.SubmittedByAppUser.DisplayName ?? s.SubmittedByAppUser.UserName ?? "Attendee",
            s.UploadFileId, s.UploadFile.FileName, s.UploadFile.ContentType,
            s.Note, s.Status, s.RejectionReason, s.DateCreated)).ToListAsync(ct);

    private static async Task<EvidenceSubmissionRecord> ToRecordAsync(
        BenDataContext db, Guid id, CancellationToken ct) =>
        (await ProjectAsync(db.EventEvidenceSubmissions.AsNoTracking().Where(s => s.Id == id), ct)).Single();
}
