using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The record a loan leaves behind: condition photos at each end, requests for more time, and the
/// merged per-item history that ties those together with the service log.
/// </summary>
/// <remarks>
/// <para>Everything here is scoped to the two parties of a loan — its borrower and whoever may
/// review it — resolved through <c>EquipmentAccess.CanReviewCheckoutAsync</c> so the answer matches
/// the checkout endpoints exactly. Anyone else gets <c>404</c>, not <c>403</c>.</para>
///
/// <para>A renewal is a child row rather than a state of the loan: the gear never changes hands, so
/// the loan stays <c>CheckedOut</c> and only its due date moves. That also keeps what was asked
/// and answered, which editing the due date in place would erase.</para>
/// </remarks>
[ApiController]
[Authorize]
public sealed class EquipmentLoanHistoryController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;
    private readonly IAuditLogService _auditLog;

    public EquipmentLoanHistoryController(
        IDbContextFactory<BenDataContext> db,
        IOrganizationSecurityService security,
        IFileStorageService fileStorage,
        IMediaIngestService mediaIngest,
        IAuditLogService auditLog)
    {
        _db          = db;
        _security    = security;
        _mediaIngest = mediaIngest;
        _fileStorage = fileStorage;
        _auditLog    = auditLog;
    }

    private bool IsSuperAdmin() => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

    /// <summary>Loads a loan and works out how the caller relates to it, or null if they do not.</summary>
    private async Task<(EquipmentCheckout Checkout, bool IsBorrower, bool IsApprover)?> LoadPartyAsync(
        BenDataContext db, Guid checkoutId, Guid userId, CancellationToken ct)
    {
        var checkout = await db.EquipmentCheckouts
            .Include(c => c.EquipmentItem)
            .Include(c => c.Photos)
            .Include(c => c.Renewals)
            .FirstOrDefaultAsync(c => c.Id == checkoutId, ct);
        if (checkout is null) return null;

        var isApprover = await EquipmentAccess.CanReviewCheckoutAsync(
            _security, checkout.EquipmentItem, userId, IsSuperAdmin(), ct);
        var isBorrower = checkout.BorrowerAppUserId == userId;

        return isApprover || isBorrower ? (checkout, isBorrower, isApprover) : null;
    }

    // ── Condition photos ─────────────────────────────────────────────────────

    [HttpGet("api/equipment-checkouts/{checkoutId:guid}/photos")]
    public async Task<ActionResult<IEnumerable<EquipmentCheckoutPhotoRecord>>> GetPhotos(
        Guid checkoutId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var party = await LoadPartyAsync(db, checkoutId, userId, ct);
        if (party is null) return NotFound();

        var names = await NamesAsync(db, party.Value.Checkout.Photos.Select(p => (Guid?)p.CreatedByAppUserId), ct);

        return Ok(party.Value.Checkout.Photos
            .OrderBy(p => p.Stage).ThenBy(p => p.DateCreated)
            .Select(p => ToRecord(p, names)));
    }

    /// <summary>
    /// Attaches a condition photo to one end of a loan.
    /// </summary>
    /// <remarks>
    /// Either party may add one, because either may be the person holding the camera. The stage is
    /// gated on where the loan has got to: a hand-off photo only makes sense once the loan is
    /// approved and before it is over, and a return photo only once the gear is coming back.
    /// Photographing a hand-off that never happened would be recording a fact about nothing.
    /// </remarks>
    [HttpPost("api/equipment-checkouts/{checkoutId:guid}/photos")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<EquipmentCheckoutPhotoRecord>> AttachPhoto(
        Guid checkoutId, [FromQuery] EquipmentPhotoStage stage, IFormFile file,
        [FromQuery] string? caption, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (file is null || file.Length == 0) return BadRequest("No file was uploaded.");
        if (!Enum.IsDefined(stage)) return BadRequest("Unknown photo stage.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var party = await LoadPartyAsync(db, checkoutId, userId, ct);
        if (party is null) return NotFound();

        var status = party.Value.Checkout.Status;
        var stageIsMeaningful = stage switch
        {
            EquipmentPhotoStage.Handoff => status is EquipmentCheckoutStatus.Approved or EquipmentCheckoutStatus.CheckedOut,
            EquipmentPhotoStage.Return  => status is EquipmentCheckoutStatus.CheckedOut or EquipmentCheckoutStatus.Returned,
            _                           => false,
        };
        if (!stageIsMeaningful)
            return Conflict($"A {stage.ToString().ToLowerInvariant()} photo doesn't apply to a loan that is {status.ToString().ToLowerInvariant()}.");

        var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.UserFilePath(userId, storedName);
        // Ben's rule (2026-08-24): strip on ANY upload, keep what came off beside the record.
        var uploadFileId = Guid.NewGuid();
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        var uploadFile = new UploadFile
        {
            Id                 = uploadFileId,
            UploadFileTypeId   = UploadFileTypeSeeder.EquipmentPhotoFileTypeId,
            AppUserId          = userId,
            FileName           = file.FileName,
            StoredFileName     = storedName,
            ContentType        = ingested.ServedContentType,
            FileSize           = ingested.ServedFileSize,
            StoragePath        = storagePath,
            IsPublic           = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        db.UploadFileMetadata.Add(ingested.Metadata);

        var photo = new EquipmentCheckoutPhoto
        {
            Id                  = Guid.NewGuid(),
            EquipmentCheckoutId = checkoutId,
            UploadFileId        = uploadFile.Id,
            Stage               = stage,
            Caption             = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            DateCreated         = DateTime.UtcNow,
            CreatedByAppUserId  = userId,
        };
        db.EquipmentCheckoutPhotos.Add(photo);

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentCheckoutPhoto), photo.Id, photo, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        var names = await NamesAsync(db, [(Guid?)userId], ct);
        return Ok(ToRecord(photo, names));
    }

    /// <summary>Removes a condition photo. Only whoever took it, or the loan's approver, may.</summary>
    [HttpDelete("api/equipment-checkouts/{checkoutId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DeletePhoto(Guid checkoutId, Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var party = await LoadPartyAsync(db, checkoutId, userId, ct);
        if (party is null) return NotFound();

        var photo = await db.EquipmentCheckoutPhotos
            .Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.EquipmentCheckoutId == checkoutId, ct);
        if (photo is null) return NotFound();

        if (photo.CreatedByAppUserId != userId && !party.Value.IsApprover) return Forbid();

        var storagePath = photo.UploadFile.StoragePath;
        db.EquipmentCheckoutPhotos.Remove(photo);
        db.UploadFiles.Remove(photo.UploadFile);
        await db.SaveChangesAsync(ct);

        if (storagePath is not null)
        {
            try { await _fileStorage.DeleteAsync(storagePath, ct); }
            catch { /* the rows are gone; a stranded blob is not worth failing the request over */ }
        }

        return NoContent();
    }

    /// <summary>Bytes for one condition photo, for data:-URI rendering.</summary>
    [HttpGet("api/equipment-checkouts/photos/{photoId:guid}/content")]
    public async Task<IActionResult> GetPhotoContent(Guid photoId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var photo = await db.EquipmentCheckoutPhotos.AsNoTracking()
            .Include(p => p.UploadFile)
            .FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo is null) return NotFound();

        // Same audience as the loan itself — condition photos are never public.
        if (await LoadPartyAsync(db, photo.EquipmentCheckoutId, userId, ct) is null) return NotFound();

        if (photo.UploadFile.StoragePath is null) return NotFound();
        var stream = await _fileStorage.OpenReadAsync(photo.UploadFile.StoragePath, ct);
        return File(stream, photo.UploadFile.ContentType);
    }

    // ── Renewals ─────────────────────────────────────────────────────────────

    [HttpGet("api/equipment-checkouts/{checkoutId:guid}/renewals")]
    public async Task<ActionResult<IEnumerable<EquipmentCheckoutRenewalRecord>>> GetRenewals(
        Guid checkoutId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var party = await LoadPartyAsync(db, checkoutId, userId, ct);
        if (party is null) return NotFound();

        var names = await NamesAsync(db, party.Value.Checkout.Renewals.Select(r => r.ReviewedByAppUserId), ct);

        return Ok(party.Value.Checkout.Renewals
            .OrderByDescending(r => r.DateCreated)
            .Select(r => ToRecord(r, names, party.Value.IsBorrower, party.Value.IsApprover)));
    }

    /// <summary>
    /// Asks for more time on a loan that is out.
    /// </summary>
    /// <remarks>
    /// Borrower only, and only while the gear is actually with them — asking for an extension on a
    /// loan you have not collected is really just a different request. One pending ask at a time,
    /// so a queue of competing dates cannot build up.
    /// </remarks>
    [HttpPost("api/equipment-checkouts/{checkoutId:guid}/renewals")]
    public async Task<ActionResult<EquipmentCheckoutRenewalRecord>> RequestRenewal(
        Guid checkoutId, [FromBody] RequestEquipmentRenewalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var party = await LoadPartyAsync(db, checkoutId, userId, ct);
        if (party is null) return NotFound();
        if (!party.Value.IsBorrower) return Forbid();

        var checkout = party.Value.Checkout;
        if (checkout.Status != EquipmentCheckoutStatus.CheckedOut)
            return Conflict("Only equipment you currently have out can be renewed.");
        if (checkout.Renewals.Any(r => r.Status == EquipmentRenewalStatus.Requested))
            return Conflict("You already have a renewal request waiting on this loan.");
        if (checkout.DateDue is not null && request.RequestedDateDue <= checkout.DateDue)
            return BadRequest("Ask for a date later than the one it is already due back.");

        var renewal = new EquipmentCheckoutRenewal
        {
            Id                  = Guid.NewGuid(),
            EquipmentCheckoutId = checkoutId,
            RequestedDateDue    = request.RequestedDateDue,
            Status              = EquipmentRenewalStatus.Requested,
            RequestNotes        = string.IsNullOrWhiteSpace(request.RequestNotes) ? null : request.RequestNotes.Trim(),
            DateCreated         = DateTime.UtcNow,
            CreatedByAppUserId  = userId,
        };
        db.EquipmentCheckoutRenewals.Add(renewal);

        await NotifyApproversAsync(db, checkout, userId,
            "Request for more time on borrowed equipment",
            $"A renewal has been asked for on {NotificationText.Safe(checkout.EquipmentItem.DisplayName)}, until {request.RequestedDateDue:MMM d, yyyy}.", ct);

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentCheckoutRenewal), renewal.Id, renewal, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        var names = await NamesAsync(db, [(Guid?)userId], ct);
        return Ok(ToRecord(renewal, names, isBorrower: true, isApprover: party.Value.IsApprover));
    }

    /// <summary>
    /// Approves or refuses a renewal. Approving moves the loan's due date to the requested one.
    /// </summary>
    [HttpPost("api/equipment-checkouts/{checkoutId:guid}/renewals/{renewalId:guid}/review")]
    public async Task<ActionResult<EquipmentCheckoutRenewalRecord>> ReviewRenewal(
        Guid checkoutId, Guid renewalId, [FromBody] ReviewEquipmentRenewalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!request.Approve && string.IsNullOrWhiteSpace(request.ReviewNotes))
            return BadRequest("Please give a reason when refusing more time.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var party = await LoadPartyAsync(db, checkoutId, userId, ct);
        if (party is null) return NotFound();
        if (!party.Value.IsApprover) return Forbid();

        var renewal = party.Value.Checkout.Renewals.FirstOrDefault(r => r.Id == renewalId);
        if (renewal is null) return NotFound();
        if (renewal.Status != EquipmentRenewalStatus.Requested)
            return Conflict($"That renewal has already been {renewal.Status.ToString().ToLowerInvariant()}.");

        var tracked = await db.EquipmentCheckoutRenewals.FirstAsync(r => r.Id == renewalId, ct);
        tracked.Status              = request.Approve ? EquipmentRenewalStatus.Approved : EquipmentRenewalStatus.Denied;
        tracked.ReviewNotes         = string.IsNullOrWhiteSpace(request.ReviewNotes) ? null : request.ReviewNotes.Trim();
        tracked.ReviewedByAppUserId = userId;
        tracked.DateReviewed        = DateTime.UtcNow;
        tracked.DateUpdated         = DateTime.UtcNow;
        tracked.UpdatedByAppUserId  = userId;

        var checkout = await db.EquipmentCheckouts.FirstAsync(c => c.Id == checkoutId, ct);
        if (request.Approve)
        {
            // The granted date IS the new deadline — the loan carries one due date, the renewals
            // carry the story of how it got there.
            checkout.DateDue           = tracked.RequestedDateDue;
            checkout.DateUpdated       = DateTime.UtcNow;
            checkout.UpdatedByAppUserId = userId;
        }

        NotifyUser(db, checkout.BorrowerAppUserId, userId,
            request.Approve ? "More time granted on borrowed equipment" : "Request for more time declined",
            request.Approve
                ? $"Your loan now runs until {tracked.RequestedDateDue:MMM d, yyyy}."
                : $"Your request for more time was declined. Reason given: {NotificationText.Safe(tracked.ReviewNotes)}");

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(EquipmentCheckoutRenewal), tracked.Id, renewal, tracked, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        var names = await NamesAsync(db, [(Guid?)userId], ct);
        return Ok(ToRecord(tracked, names, party.Value.IsBorrower, isApprover: true));
    }

    // ── The merged history ───────────────────────────────────────────────────

    /// <summary>
    /// One piece of equipment's whole story: loans, renewals, service and defects, merged in time
    /// order.
    /// </summary>
    /// <remarks>
    /// Assembled in memory from three small per-item queries rather than as a database union: each
    /// list is short for a single piece of gear, and the alternative is a query nobody can read.
    /// Carries no serial number — history is visible to people the serial deliberately is not.
    /// </remarks>
    [HttpGet("api/equipment/{itemId:guid}/history")]
    public async Task<ActionResult<IEnumerable<EquipmentHistoryEntryRecord>>> GetItemHistory(
        Guid itemId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();

        var maySee = await EquipmentAccess.CanReviewCheckoutAsync(_security, item, userId, IsSuperAdmin(), ct);
        if (!maySee && item.OwningOrganizationId is Guid orgId)
            maySee = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
        if (!maySee) return NotFound();

        var checkouts = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.Photos)
            .Include(c => c.Renewals)
            .Where(c => c.EquipmentItemId == itemId)
            .ToListAsync(ct);

        var serviceLog = await db.EquipmentServiceLogs.AsNoTracking()
            .Where(l => l.EquipmentItemId == itemId)
            .ToListAsync(ct);

        var actorIds = checkouts.Select(c => (Guid?)c.BorrowerAppUserId)
            .Concat(checkouts.SelectMany(c => c.Renewals.Select(r => r.ReviewedByAppUserId)))
            .Concat(serviceLog.Select(l => (Guid?)l.CreatedByAppUserId));
        var names = await NamesAsync(db, actorIds, ct);

        string? Name(Guid? id) => id is not null && names.TryGetValue(id.Value, out var n) ? n : null;

        var entries = new List<EquipmentHistoryEntryRecord>();

        foreach (var c in checkouts)
        {
            var borrower = Name(c.BorrowerAppUserId) ?? "someone";
            var handoffPhotos = c.Photos.Count(p => p.Stage == EquipmentPhotoStage.Handoff);
            var returnPhotos  = c.Photos.Count(p => p.Stage == EquipmentPhotoStage.Return);

            entries.Add(new EquipmentHistoryEntryRecord(
                c.DateCreated, EquipmentHistoryKind.Loan,
                $"{borrower} asked to borrow it", borrower, c.Id, handoffPhotos));

            if (c.DateCheckedOut is DateTime out_)
                entries.Add(new EquipmentHistoryEntryRecord(
                    out_, EquipmentHistoryKind.Loan,
                    c.DateDue is null ? $"{borrower} took it out" : $"{borrower} took it out, due back {c.DateDue:MMM d, yyyy}",
                    borrower, c.Id, handoffPhotos));

            if (c.DateReturned is DateTime back)
                entries.Add(new EquipmentHistoryEntryRecord(
                    back, EquipmentHistoryKind.Loan,
                    string.IsNullOrWhiteSpace(c.ReturnConditionNotes)
                        ? $"{borrower} returned it"
                        : $"{borrower} returned it — {c.ReturnConditionNotes}",
                    borrower, c.Id, returnPhotos));

            if (c.Status is EquipmentCheckoutStatus.Denied or EquipmentCheckoutStatus.Cancelled && c.DateReviewed is DateTime decided)
                entries.Add(new EquipmentHistoryEntryRecord(
                    decided, EquipmentHistoryKind.Loan,
                    c.Status == EquipmentCheckoutStatus.Denied
                        ? $"The request from {borrower} was declined"
                        : $"{borrower} withdrew the request",
                    borrower, c.Id, 0));

            foreach (var r in c.Renewals)
            {
                entries.Add(new EquipmentHistoryEntryRecord(
                    r.DateCreated, EquipmentHistoryKind.Renewal,
                    $"{borrower} asked to keep it until {r.RequestedDateDue:MMM d, yyyy}", borrower, c.Id, 0));

                if (r.DateReviewed is DateTime reviewed)
                    entries.Add(new EquipmentHistoryEntryRecord(
                        reviewed, EquipmentHistoryKind.Renewal,
                        r.Status == EquipmentRenewalStatus.Approved
                            ? $"More time granted, until {r.RequestedDateDue:MMM d, yyyy}"
                            : $"More time refused — {r.ReviewNotes}",
                        Name(r.ReviewedByAppUserId), c.Id, 0));
            }
        }

        foreach (var l in serviceLog)
        {
            var kind = l.EntryType == EquipmentServiceLogType.Service
                ? EquipmentHistoryKind.Service
                : EquipmentHistoryKind.Defect;
            var summary = l.EntryType switch
            {
                EquipmentServiceLogType.Service        => $"Serviced — {l.Notes}",
                EquipmentServiceLogType.DefectReported => $"Fault reported — {l.Notes}",
                EquipmentServiceLogType.DefectResolved => $"Fault fixed — {l.Notes}",
                _                                      => l.Notes,
            };
            entries.Add(new EquipmentHistoryEntryRecord(
                l.EntryDate, kind, summary, Name(l.CreatedByAppUserId), null, 0));
        }

        return Ok(entries.OrderByDescending(e => e.DateUtc).ToList());
    }

    // ── Shared bits ──────────────────────────────────────────────────────────

    private static async Task<Dictionary<Guid, string?>> NamesAsync(
        BenDataContext db, IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.AppUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    private async Task NotifyApproversAsync(
        BenDataContext db, EquipmentCheckout checkout, Guid actingUserId, string subject, string body, CancellationToken ct)
    {
        var item = checkout.EquipmentItem;
        var recipients = new List<Guid>();

        if (item.OwnerAppUserId is Guid ownerId)
        {
            recipients.Add(ownerId);
        }
        else if (item.OwningOrganizationId is Guid orgId)
        {
            var memberIds = await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.OrganizationId == orgId && m.IsActive)
                .Select(m => m.AppUserId)
                .ToListAsync(ct);

            foreach (var memberId in memberIds)
            {
                if (await _security.HasAccessAsync(memberId, orgId,
                        OrganizationSecurityTable.EquipmentCheckout, OrganizationSecurityAction.Update, ct))
                    recipients.Add(memberId);
            }
        }

        foreach (var recipientId in recipients.Distinct().Where(r => r != actingUserId))
            NotifyUser(db, recipientId, actingUserId, subject, body);
    }

    /// <summary>
    /// Queues a notice on the caller's change set, so it commits with the thing it announces.
    /// </summary>
    private static void NotifyUser(BenDataContext db, Guid toUserId, Guid fromUserId, string subject, string body)
    {
        if (toUserId == fromUserId) return;

        var message = new UserMessage
        {
            Id                 = Guid.NewGuid(),
            UserMessageTypeId  = OrganizationSeeder.EquipmentCheckoutMessageTypeId,
            MessageSubject     = subject,
            MessageBody        = body,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = fromUserId,
        };
        db.UserMessages.Add(message);
        db.UserMessageTos.Add(new UserMessageTo
        {
            Id          = Guid.NewGuid(),
            MessageId   = message.Id,
            ToAppUserId = toUserId,
        });
    }

    private static EquipmentCheckoutPhotoRecord ToRecord(EquipmentCheckoutPhoto p, Dictionary<Guid, string?> names)
        => new(p.Id, p.EquipmentCheckoutId, p.UploadFileId, p.Stage, p.Caption, p.DateCreated,
               p.CreatedByAppUserId,
               names.TryGetValue(p.CreatedByAppUserId, out var n) ? n : null);

    private static EquipmentCheckoutRenewalRecord ToRecord(
        EquipmentCheckoutRenewal r, Dictionary<Guid, string?> names, bool isBorrower, bool isApprover)
        => new(r.Id, r.EquipmentCheckoutId, r.RequestedDateDue, r.Status, r.RequestNotes, r.ReviewNotes,
               r.ReviewedByAppUserId,
               r.ReviewedByAppUserId is not null && names.TryGetValue(r.ReviewedByAppUserId.Value, out var n) ? n : null,
               r.DateReviewed, r.DateCreated,
               CanReview: isApprover && r.Status == EquipmentRenewalStatus.Requested,
               CanCancel: isBorrower && r.Status == EquipmentRenewalStatus.Requested);
}
