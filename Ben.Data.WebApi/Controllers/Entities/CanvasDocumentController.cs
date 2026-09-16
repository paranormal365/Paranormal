using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Canvas;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Case canvas boards: list, open, save (with revision conflicts), delete and publish.
/// </summary>
/// <remarks>
/// <para><b>Writes are case-scoped, unlike video projects.</b> A video project is its author's:
/// anybody on the case may read it, only the author may change it, because a shared list is a way to
/// continue somebody's work, not a licence to overwrite it. A board on a case is different in kind —
/// it is the case's working document, the thing the team lays its evidence out on — so anybody
/// holding <b>Cases Update</b> in the case's group may save it, <b>Cases Create</b> may start one and
/// <b>Cases Delete</b> may remove one. A read grant reads and does nothing else
/// (<c>ReadDoesNotGrantDestructionTests</c> scans this file for that). A board with no case is
/// personal: only its author sees or changes it, and anybody else is told it does not exist.</para>
///
/// <para><b>Why an integer revision.</b> Case-scoped writes mean two investigators can save the same
/// board minutes apart, and without a check the second silently erases the first. So a save must
/// send <c>If-Match: "&lt;revision it loaded&gt;"</c>: missing or unreadable is 428, stale is 409
/// carrying the server's copy so the editor can offer "keep mine / take theirs". The compare in C#
/// answers the plainly stale save; the EF concurrency token on <c>Revision</c> answers two saves that
/// both passed the compare, because the UPDATE itself carries <c>WHERE Revision = @loaded</c>. An
/// <c>int</c> rather than a <c>rowversion</c> because the browser carries it back as text.</para>
///
/// <para><b>Message HTML is sanitised on the way in</b> (canvas plan review R2b). The editor cleans
/// pasted markup, but a board also arrives from an import, another tab or a hand-written request, and
/// the site's cookie is SameSite=None. Every message node's <c>html</c> goes through
/// <see cref="ICmsMarkupSanitizer"/> before it is stored, so what is in the database is what is safe
/// to render and no reader has to remember.</para>
///
/// <para><b>The document never enters the audit log.</b> A board can be megabytes and holds witness
/// names; each audit row records ids, name, revision and the JSON's length.</para>
/// </remarks>
[ApiController]
[Route("api/canvas-documents")]
[Authorize]
public sealed class CanvasDocumentController : BenControllerBase
{
    /// <summary>What a board with no usable title is called.</summary>
    public const string UntitledBoardName = "Untitled board";

    /// <summary>The 428 sentence.</summary>
    public const string IfMatchRequired = "Send If-Match with the revision you loaded.";

    private const int MaxNameLength = 256;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;
    private readonly SubscriptionLimitGuard _limits;
    private readonly IOrganizationSecurityService _security;
    private readonly IAuditLogService _audit;
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly ILogger<CanvasDocumentController>? _logger;

    public CanvasDocumentController(
        IDbContextFactory<BenDataContext> db, IMapper mapper, IFileStorageService fileStorage,
        IMediaIngestService mediaIngest, SubscriptionLimitGuard limits,
        IOrganizationSecurityService security, IAuditLogService audit,
        ICmsMarkupSanitizer sanitizer, ILogger<CanvasDocumentController>? logger = null)
    {
        _db          = db;
        _mapper      = mapper;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
        _limits      = limits;
        _security    = security;
        _audit       = audit;
        _sanitizer   = sanitizer;
        _logger      = logger;
    }

    // GET /api/canvas-documents[?caseId=]
    /// <summary>
    /// The boards on a case (anybody who can read the case), or without a case the caller's own
    /// personal boards. Summaries only, newest first.
    /// </summary>
    /// <response code="200">The boards, without their documents.</response>
    /// <response code="403">The caller cannot read that case.</response>
    /// <response code="404">No such case, or the canvas editor is switched off.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CanvasDocumentSummaryRecord>), 200)]
    public async Task<ActionResult<IEnumerable<CanvasDocumentSummaryRecord>>> GetAll(
        [FromQuery] Guid? caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        IQueryable<CanvasDocument> query;
        if (caseId is { } onCase)
        {
            if (await OrgOfCaseAsync(db, onCase, ct) is not { } orgId) return NotFound();
            if (!await CanReadCaseAsync(orgId, ct)) return Forbid();
            // A board nobody has published is its writer's own: research is thought about before it is evidence, and a
            // case list full of other people's unfinished boards is worse than a short one (Ben, 2026-09-16).
            query = db.CanvasDocuments.AsNoTracking().Include(d => d.Case)
                .Where(d => d.CaseId == onCase && (d.PublishedJson != null || d.CreatedByAppUserId == userId));
        }
        else
        {
            query = db.CanvasDocuments.AsNoTracking().Where(d => d.CaseId == null && d.CreatedByAppUserId == userId);
        }

        var entities = await query.OrderByDescending(d => d.DateUpdated ?? d.DateCreated).ToListAsync(ct);
        // Every board in one list has the same answer: all on one case, or all the caller's own.
        var canEdit = caseId is null || await MayChangeCaseAsync(entities.FirstOrDefault()?.Case?.OrganizationId, ct);
        var records = entities.Select(e => _mapper.Map<CanvasDocumentSummaryRecord>(e) with
        {
            CanEdit = canEdit,
            IsPublished = e.PublishedJson is not null,
            HasUnpublishedChanges = e.PublishedJson is not null && e.Revision > (e.PublishedRevision ?? 0),
        }).ToList();

        // Names only where the list holds more than one person's work; one query for all of them.
        if (caseId.HasValue && records.Count > 0)
        {
            var authorIds = records.Select(r => r.CreatedByAppUserId).Distinct().ToList();
            var names = await db.AppUsers.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
            records = records
                .Select(r => r with { CreatedByName = names.GetValueOrDefault(r.CreatedByAppUserId) ?? "Member" })
                .ToList();
        }

        return Ok(records);
    }

    // GET /api/canvas-documents/{id}
    /// <summary>Opens one board. The revision is in the body and in <c>ETag</c>.</summary>
    /// <response code="200">The board, with its document.</response>
    /// <response code="404">No such board, or not one the caller may read.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CanvasDocumentRecord), 200)]
    public async Task<ActionResult<CanvasDocumentRecord>> GetById(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.CanvasDocuments.AsNoTracking().Include(d => d.Case)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        // Not found rather than forbidden: whether a board exists is not an outsider's to learn — and an unpublished
        // board is not there for anybody but the person writing it.
        if (entity is null || !await MayReadAsync(entity, userId, ct)) return NotFound();
        if (entity.PublishedJson is null && entity.CreatedByAppUserId != userId) return NotFound();

        SetETag(entity.Revision);
        var canEdit = entity.Case is { } onCase
            ? await MayChangeCaseAsync(onCase.OrganizationId, ct)
            : entity.CreatedByAppUserId == userId;

        // Somebody who may only read gets what was published, not what is being written now. The draft is the writer's
        // until they say otherwise, and they say so by publishing.
        var access = canEdit ? await AccessToAsync(entity, userId, ct) : CanvasBoardAccess.Read;

        // Somebody who may only add to the board is told which pieces are theirs, so the editor can leave those alone
        // and lock the rest. Nobody else needs the list: full access changes anything, a reader changes nothing.
        var mine = access == CanvasBoardAccess.Append
            ? PieceOwners.Owned(PieceOwners.Read(entity.PieceOwnersJson), userId)
                .Select(id => Guid.TryParse(id, out var pieceId) ? pieceId : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList()
            : [];

        var record = _mapper.Map<CanvasDocumentRecord>(entity) with
        {
            CanEdit = canEdit,
            Access = access,
            MyPieceIds = mine,
            IsPublished = entity.PublishedJson is not null,
            HasUnpublishedChanges = entity.PublishedJson is not null && entity.Revision > (entity.PublishedRevision ?? 0),
        };
        if (!canEdit && entity.PublishedJson is { } published)
            record = record with { DocumentJson = published, Revision = entity.PublishedRevision ?? entity.Revision };

        return Ok(record);
    }

    // POST /api/canvas-documents[?caseId=]
    /// <summary>
    /// Saves a new board, on a case (Cases Create) or personal. The body is the editor's document
    /// JSON; its <c>title</c> names the board, its own <c>caseId</c> and <c>revision</c> are ignored.
    /// </summary>
    /// <response code="201">Created at revision 1.</response>
    /// <response code="400">Not a JSON object, or the group's subscription has ended.</response>
    /// <response code="403">The caller may not create on that case.</response>
    /// <response code="404">No such case.</response>
    [HttpPost]
    [RequestSizeLimit(10_000_000)]
    [ProducesResponseType(typeof(CanvasDocumentRecord), 201)]
    public async Task<ActionResult<CanvasDocumentRecord>> Create(
        [FromQuery] Guid? caseId, [FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (body.ValueKind != JsonValueKind.Object) return BadRequest(NotAnObject);

        await using var db = await _db.CreateDbContextAsync(ct);
        Guid? orgId = null;
        if (caseId is { } onCase)
        {
            orgId = await OrgOfCaseAsync(db, onCase, ct);
            if (orgId is null) return NotFound("Case not found.");
            if (await _limits.WhyReadOnlyAsync(orgId.Value, ct) is { } readOnly) return BadRequest(readOnly);
            if (!await MayOnCaseAsync(orgId.Value, OrganizationSecurityAction.Create, ct)) return Forbid();
        }

        var now = DateTime.UtcNow;
        var entity = new CanvasDocument
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            Name               = NameOf(body),
            DocumentJson       = SanitizeDocument(body),
            Revision           = 1,
            DateCreated        = now,
            CreatedByAppUserId = userId,
        };
        db.CanvasDocuments.Add(entity);
        await db.SaveChangesAsync(ct);

        await TryAuditAsync(_audit.LogCreateAsync(nameof(CanvasDocument), entity.Id, Slim(entity), userId, AppSources.WebApi));

        SetETag(entity.Revision);
        var record = _mapper.Map<CanvasDocumentRecord>(entity) with { OrganizationId = orgId, CanEdit = true };
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, record);
    }

    // PUT /api/canvas-documents/{id}
    /// <summary>
    /// Saves over a board. Requires <c>If-Match</c> with the revision the caller loaded
    /// (<c>"3"</c> or <c>3</c>). Case boards need Cases Update; personal boards, their author.
    /// </summary>
    /// <response code="200">Saved; the body carries the new revision.</response>
    /// <response code="400">Not a JSON object, or the group's subscription has ended.</response>
    /// <response code="403">The caller can read the case but may not change it.</response>
    /// <response code="404">No such board, or not one the caller may read.</response>
    /// <response code="409">Somebody saved first; the body is the server's copy.</response>
    /// <response code="428">No readable If-Match.</response>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(10_000_000)]
    [ProducesResponseType(typeof(CanvasDocumentRecord), 200)]
    [ProducesResponseType(typeof(CanvasDocumentRecord), 409)]
    public async Task<ActionResult<CanvasDocumentRecord>> Update(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (body.ValueKind != JsonValueKind.Object) return BadRequest(NotAnObject);

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.CanvasDocuments.Include(d => d.Case).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity is null || !await MayReadAsync(entity, userId, ct)) return NotFound();

        if (entity.Case is { } onCase)
        {
            if (await _limits.WhyReadOnlyAsync(onCase.OrganizationId, ct) is { } readOnly) return BadRequest(readOnly);
            if (!await MayOnCaseAsync(onCase.OrganizationId, OrganizationSecurityAction.Update, ct)) return Forbid();

            // Before it is published the board is the writer's draft, so only they may write on it. Ben chose the rest
            // (2026-09-16): once published it is the case's, and anybody who may update the case may work on it.
            if (entity.PublishedJson is null && entity.CreatedByAppUserId != userId) return NotFound();
        }

        if (ReadIfMatch() is not { } loaded) return StatusCode(428, IfMatchRequired);
        // Every answer from here on passed the write checks above, so it can say the board is editable.
        if (loaded != entity.Revision) return Conflict(_mapper.Map<CanvasDocumentRecord>(entity) with { CanEdit = true });

        // Ben, 2026-09-16: somebody who may edit the case may add to a board; changing what is already on it is for
        // the person who put it there, a group administrator, or an administrator of the site.
        var access = await AccessToAsync(entity, userId, ct);

        // A board saved before this was recorded — or created in one go — has nobody written against its pieces. They
        // belong to whoever wrote the board, not to the next person who saves it, which is what claiming the unclaimed
        // would amount to.
        var owners = PieceOwners.Read(entity.PieceOwnersJson);
        if (entity.PieceOwnersJson is null)
        {
            using var stored = JsonDocument.Parse(entity.DocumentJson);
            owners = PieceOwners.Read(PieceOwners.Write([], CanvasAdditiveGuard.IdsIn(stored.RootElement), entity.CreatedByAppUserId));
        }
        if (access == CanvasBoardAccess.Append
            && CanvasAdditiveGuard.WhyRefused(entity.DocumentJson, body, PieceOwners.Owned(owners, userId)) is { } refusal)
        {
            return BadRequest(refusal);
        }

        var before = Slim(entity);
        entity.Name               = NameOf(body);
        entity.DocumentJson       = SanitizeDocument(body);
        entity.PieceOwnersJson    = PieceOwners.Write(owners, CanvasAdditiveGuard.IdsIn(body), userId);
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        // The atomic half of the check: the UPDATE only matches a row still at the revision the
        // caller loaded. Setting OriginalValue explicitly (rather than trusting the value EF read a
        // moment ago) makes the If-Match the caller sent the thing the database compares against.
        db.Entry(entity).Property(e => e.Revision).OriginalValue = loaded;
        entity.Revision = loaded + 1;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another save landed between our read and our write. Answer exactly as a stale
            // If-Match would, with the copy that won.
            await using var fresh = await _db.CreateDbContextAsync(ct);
            var server = await fresh.CanvasDocuments.AsNoTracking().Include(d => d.Case)
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (server is null) return NotFound();
            return Conflict(_mapper.Map<CanvasDocumentRecord>(server) with { CanEdit = true });
        }

        await TryAuditAsync(_audit.LogUpdateAsync(nameof(CanvasDocument), entity.Id, before, Slim(entity), userId, AppSources.WebApi));

        SetETag(entity.Revision);
        return Ok(_mapper.Map<CanvasDocumentRecord>(entity) with { CanEdit = true });
    }

    // DELETE /api/canvas-documents/{id}
    /// <summary>
    /// Deletes a board and its published snapshot. Case boards need Cases Delete; personal boards,
    /// their author or a SuperAdmin.
    /// </summary>
    /// <response code="204">Deleted.</response>
    /// <response code="403">The caller can read the case but may not delete from it.</response>
    /// <response code="404">No such board, or not one the caller may read.</response>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.CanvasDocuments.Include(d => d.Case).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity is null) return NotFound();

        if (entity.Case is { } onCase)
        {
            if (!await MayReadAsync(entity, userId, ct)) return NotFound();
            if (!await MayOnCaseAsync(onCase.OrganizationId, OrganizationSecurityAction.Delete, ct)) return Forbid();
        }
        else if (entity.CreatedByAppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin))
        {
            return NotFound();
        }

        var slim = Slim(entity);
        await RemovePublishedSnapshotAsync(db, entity.PublishedUploadFileId, ct);

        db.CanvasDocuments.Remove(entity);
        await db.SaveChangesAsync(ct);

        await TryAuditAsync(_audit.LogDeleteAsync(nameof(CanvasDocument), id, slim, userId, AppSources.WebApi));
        return NoContent();
    }

    // POST /api/canvas-documents/{id}/publish
    /// <summary>
    /// Publishes a PNG picture of a case board to the case's Files tab, replacing the board's
    /// previous snapshot. Form field <c>file</c>, <c>image/png</c>, at most 25 MB. Cases Update.
    /// </summary>
    /// <remarks>
    /// <para><b>The picture stays inside the case</b> (canvas plan review R22). It bakes in whatever
    /// the board held — names, a client's address, pinned places — so it is filed as a
    /// <b>Board Snapshot</b> upload, never public, and <see cref="BoardSnapshots"/> refuses the two
    /// routes by which a case file could reach a visitor.</para>
    ///
    /// <para><b>Replace-after-save ordering</b>, for VideoProjectController's reason: the previous
    /// snapshot is removed only after the new one is recorded, so a failure part-way leaves an extra
    /// file rather than losing both. Publishing does not change the document, so it does not move
    /// the revision and never conflicts with a save.</para>
    ///
    /// <para>Ingest keeps the PNG as the original and serves a sanitised derivative, as for every
    /// other upload (Ben's rule, 2026-08-24).</para>
    /// </remarks>
    /// <response code="200">Published; the body is the board with its new snapshot id.</response>
    /// <response code="400">Empty, not a PNG, a personal board, or the group's subscription has ended.</response>
    /// <response code="403">The caller can read the case but may not change it.</response>
    /// <response code="404">No such board, or not one the caller may read.</response>
    [HttpPost("{id:guid}/publish")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CanvasDocumentRecord), 200)]
    public async Task<ActionResult<CanvasDocumentRecord>> Publish(Guid id, IFormFile file, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (file is null || file.Length == 0) return BadRequest("The snapshot is empty.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var board = await db.CanvasDocuments.Include(d => d.Case).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (board is null || !await MayReadAsync(board, userId, ct)) return NotFound();
        if (board.Case is not { } onCase) return BadRequest("Save this board to a case before publishing it.");

        if (await _limits.WhyReadOnlyAsync(onCase.OrganizationId, ct) is { } readOnly) return BadRequest(readOnly);
        // Publishing changes what the case holds, so it is Update, not Create.
        if (!await MayOnCaseAsync(onCase.OrganizationId, OrganizationSecurityAction.Update, ct)) return Forbid();

        if (!string.Equals(file.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)
            || !await StartsWithPngSignatureAsync(file, ct))
            return BadRequest("The snapshot must be a PNG.");

        var storedName   = $"{Guid.NewGuid():N}.png";
        var storagePath  = _fileStorage.CaseFilePath(onCase.Id, $"files/{storedName}");
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

        var now = DateTime.UtcNow;
        db.UploadFiles.Add(new UploadFile
        {
            Id                 = uploadFileId,
            UploadFileTypeId   = SeedData.UploadFileTypeSeeder.BoardSnapshotFileTypeId,
            AppUserId          = userId,
            FileName           = SnapshotFileName(board.Name),
            StoredFileName     = storedName,
            StoragePath        = storagePath,
            ContentType        = ingested.ServedContentType,
            FileSize           = ingested.ServedFileSize,
            IsPublic           = false,
            SortOrder          = 0,
            DateCreated        = now,
            CreatedByAppUserId = userId,
        });
        db.UploadFileMetadata.Add(ingested.Metadata);
        db.CaseFiles.Add(new CaseFile
        {
            Id                 = Guid.NewGuid(),
            CaseId             = onCase.Id,
            UploadFileId       = uploadFileId,
            Description        = $"Board snapshot: \"{board.Name}\"",
            DateCreated        = now,
            CreatedByAppUserId = userId,
        });

        var before = Slim(board);
        var previousUploadId = board.PublishedUploadFileId;
        board.PublishedUploadFileId = uploadFileId;
        board.PublishedAtUtc        = now;
        board.PublishedByAppUserId  = userId;

        // Publishing is what shows the board to the group at all (Ben, 2026-09-16), so the board itself is copied
        // across, not only its picture. The revision does not move: publishing is not a save, and a save racing with
        // it must still be judged against the revision its writer loaded.
        board.PublishedJson     = board.DocumentJson;
        board.PublishedRevision = board.Revision;

        board.DateUpdated           = now;
        board.UpdatedByAppUserId    = userId;
        await db.SaveChangesAsync(ct);

        await RemovePublishedSnapshotAsync(db, previousUploadId, ct);
        await TryAuditAsync(_audit.LogUpdateAsync(nameof(CanvasDocument), board.Id, before, Slim(board), userId, AppSources.WebApi));

        SetETag(board.Revision);
        return Ok(_mapper.Map<CanvasDocumentRecord>(board) with
        {
            CanEdit = true,
            IsPublished = true,
            HasUnpublishedChanges = false,
        });
    }

    /// <summary>The eight bytes every PNG starts with. The content type is the client's claim; this is the file's.</summary>
    private static async Task<bool> StartsWithPngSignatureAsync(IFormFile file, CancellationToken ct)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var head = new byte[8];
        await using var stream = file.OpenReadStream();
        var read = 0;
        while (read < head.Length)
        {
            var n = await stream.ReadAsync(head.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        return read == head.Length && head.AsSpan().SequenceEqual(signature);
    }

    /// <summary>"{board name}.png", with characters no file system accepts replaced.</summary>
    private static string SnapshotFileName(string boardName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(boardName.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return (string.IsNullOrEmpty(safe) ? UntitledBoardName : safe) + ".png";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string NotAnObject = "The board must be a JSON object.";

    /// <summary>The group a case belongs to, or null when there is no such case.</summary>
    private static Task<Guid?> OrgOfCaseAsync(BenDataContext db, Guid caseId, CancellationToken ct)
        => db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => (Guid?)c.OrganizationId)
            .FirstOrDefaultAsync(ct);

    /// <summary>May the caller take this action on this group's cases? SuperAdmin always may.</summary>
    private Task<bool> MayOnCaseAsync(Guid orgId, OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId, OrganizationPermissionArea.Cases, action, ct);

    /// <summary>
    /// May the caller change this group's case boards right now: Cases Update, and a subscription that
    /// allows writes? The answer the editor shows as an editable or view-only board (canvas plan R33).
    /// </summary>
    private async Task<bool> MayChangeCaseAsync(Guid? orgId, CancellationToken ct)
    {
        if (orgId is not { } org) return false;
        if (await _limits.WhyReadOnlyAsync(org, ct) is not null) return false;
        return await MayOnCaseAsync(org, OrganizationSecurityAction.Update, ct);
    }

    /// <summary>May the caller read this group's cases? The only question a read grant answers.</summary>
    private async Task<bool> CanReadCaseAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var readAction = OrganizationSecurityAction.Read;
        return await _security.HasAccessAsync(GetCurrentUserId(), orgId, OrganizationSecurityTable.Case, readAction, ct);
    }

    /// <summary>
    /// What this person may do with this board: read it, add to it, or change anything on it.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-16: "only the author or organization admin or site admin or super admin can edit the board and
    /// change existing pieces", and somebody who may edit the case may add to it. A personal board has one answer —
    /// its author's — because nobody else can reach it at all.
    /// </remarks>
    private async Task<CanvasBoardAccess> AccessToAsync(CanvasDocument entity, Guid userId, CancellationToken ct)
    {
        if (entity.Case is not { } onCase)
            return entity.CreatedByAppUserId == userId ? CanvasBoardAccess.Full : CanvasBoardAccess.Read;

        if (entity.CreatedByAppUserId == userId) return CanvasBoardAccess.Full;
        if (User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.Admin)) return CanvasBoardAccess.Full;

        await using var db = await _db.CreateDbContextAsync(ct);
        var role = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == onCase.OrganizationId && m.AppUserId == userId && m.IsActive)
            .Select(m => (OrganizationMemberRole?)m.Role)
            .FirstOrDefaultAsync(ct);
        if (role is OrganizationMemberRole.Owner or OrganizationMemberRole.Administrator) return CanvasBoardAccess.Full;

        return await MayChangeCaseAsync(onCase.OrganizationId, ct)
            ? CanvasBoardAccess.Append
            : CanvasBoardAccess.Read;
    }

    /// <summary>A personal board is its author's; a case board is anybody's who can read the case.</summary>
    private async Task<bool> MayReadAsync(CanvasDocument entity, Guid userId, CancellationToken ct)
        => entity.Case is { } onCase
            ? await CanReadCaseAsync(onCase.OrganizationId, ct)
            : entity.CreatedByAppUserId == userId;

    /// <summary>
    /// The revision in <c>If-Match</c>, quoted or not, or null when absent or not a number.
    /// </summary>
    /// <remarks>
    /// <c>*</c> ("any version") is deliberately unreadable: accepting it would let a client opt out
    /// of the one check this header exists for.
    /// </remarks>
    private int? ReadIfMatch()
    {
        var raw = Request.Headers.IfMatch.ToString().Trim();
        if (raw.StartsWith("W/", StringComparison.Ordinal)) raw = raw[2..];
        raw = raw.Trim('"');
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }

    private void SetETag(int revision)
        => Response.Headers.ETag = "\"" + revision.ToString(CultureInfo.InvariantCulture) + "\"";

    /// <summary>The board's own <c>title</c>, trimmed and cut to 256, or "Untitled board".</summary>
    private static string NameOf(JsonElement body)
    {
        var title = TryGetPropertyIgnoreCase(body, "title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()?.Trim()
            : null;
        if (string.IsNullOrEmpty(title)) return UntitledBoardName;
        return title.Length > MaxNameLength ? title[..MaxNameLength] : title;
    }

    /// <summary>
    /// The document with every message node's <c>html</c> cleaned by the markup sanitiser.
    /// </summary>
    /// <remarks>
    /// <para>Only <c>nodes[].data</c> with <c>kind</c> "message" carries markup; a text block's text
    /// is text and is left exactly as typed. Property names are matched case-insensitively so a
    /// PascalCase document cannot walk past the check.</para>
    ///
    /// <para>When nothing changed the original bytes are stored as they arrived, so a board without
    /// hostile markup round-trips byte for byte.</para>
    /// </remarks>
    private string SanitizeDocument(JsonElement body)
    {
        var raw = body.GetRawText();
        if (JsonNode.Parse(raw) is not JsonObject root) return raw;

        var changed = false;
        if (GetIgnoreCase(root, "nodes") is JsonArray nodes)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (GetIgnoreCase(node, "data") is not JsonObject data) continue;
                if (GetIgnoreCase(data, "kind") is not JsonValue kind
                    || !kind.TryGetValue<string>(out var kindText)
                    || !string.Equals(kindText, "message", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var key in data.Select(p => p.Key).Where(k => string.Equals(k, "html", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    var before = data[key] is JsonValue v && v.TryGetValue<string>(out var html) ? html : null;
                    var after = _sanitizer.SanitizeHtml(before);
                    if (!string.Equals(before, after, StringComparison.Ordinal))
                    {
                        data[key] = after;
                        changed = true;
                    }
                }
            }
        }

        return changed ? root.ToJsonString() : raw;
    }

    private static JsonNode? GetIgnoreCase(JsonObject obj, string name)
        => obj.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>What the audit log records about a board: never the document itself.</summary>
    private static object Slim(CanvasDocument d) => new
    {
        d.Id, d.CaseId, d.Name, d.Revision, d.PublishedUploadFileId, d.PublishedAtUtc,
        JsonLength = d.DocumentJson?.Length ?? 0,
        // Whether the group can see it, and which revision they are reading, are the facts an audit row is asked for
        // afterwards — "when did this stop being a draft" (Ben, 2026-09-16). Never the document itself.
        IsPublished = d.PublishedJson is not null,
        d.PublishedRevision,
    };

    /// <summary>
    /// Removes a published snapshot, from the database and from disk.
    /// </summary>
    /// <remarks>
    /// Best-effort on the bytes, for VideoProjectController's reason: a row removed without its bytes
    /// costs disk, bytes removed without the row leave a record pointing at nothing. The case link
    /// goes with it — a CaseFile pointing at a deleted upload is a file on the Files tab that cannot
    /// be opened.
    /// </remarks>
    private async Task RemovePublishedSnapshotAsync(BenDataContext db, Guid? uploadFileId, CancellationToken ct)
    {
        if (uploadFileId is not { } id) return;

        var upload = await db.UploadFiles.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (upload is null) return;

        db.CaseFiles.RemoveRange(await db.CaseFiles.Where(f => f.UploadFileId == id).ToListAsync(ct));
        db.UploadFileMetadata.RemoveRange(await db.UploadFileMetadata.Where(m => m.UploadFileId == id).ToListAsync(ct));
        db.UploadFiles.Remove(upload);
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(upload.StoragePath)) return;
        try { await _mediaIngest.DeleteAllAsync(upload.StoragePath, ct); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "Removed the record for board snapshot {UploadId} but could not delete its files.", id);
        }
    }
}
