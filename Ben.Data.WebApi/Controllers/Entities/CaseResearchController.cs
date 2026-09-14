using Ben.Data.Common.Blocks;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Research;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A case's research: quick links and files, and research pages.
/// </summary>
/// <remarks>
/// <para><b>Research pages</b> (beta feedback, 2026-09-14). A note is a page of blocks with a side rail of files and links.
/// It is written as a draft that only its author sees; <c>publish</c> makes that draft what the rest of the group reads.
/// While somebody holds an unpublished draft nobody else can save over it, and a save made against an older revision is
/// refused rather than silently replacing newer work. Everything a page contains is cleaned by
/// <see cref="ResearchPages.Clean"/> before it is stored.</para>
/// <para>Notes written before pages existed have no stored page; they read as published, their body shown as one text
/// block, and become pages the first time somebody saves one.</para>
/// </remarks>
[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/research")]
[Authorize]
public sealed class CaseResearchController : BenControllerBase
{
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;

    private readonly IMediaIngestService _mediaIngest;
    private readonly IAvMetadataStripper _avStripper;
    private readonly ICmsMarkupSanitizer _sanitizer;

    public CaseResearchController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage,
        IMediaIngestService mediaIngest, IAvMetadataStripper avStripper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security,
        ICmsMarkupSanitizer sanitizer)
    { _db = db; _fileStorage = fileStorage; _mediaIngest = mediaIngest;
        _avStripper  = avStripper; _security = security; _sanitizer = sanitizer; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseResearchEntryDto>>> GetAll(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        // The page JSON stays in the database: a list needs a title and a summary, not every page on the case.
        var entries = await db.CaseResearchEntries.AsNoTracking()
            .Include(e => e.UploadFile)
            .Where(e => e.CaseId == caseId)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DateCreated)
            .Select(e => new CaseResearchEntry
            {
                Id = e.Id, CaseId = e.CaseId, ResearchType = e.ResearchType, Title = e.Title, Body = e.Body, Url = e.Url,
                UploadFileId = e.UploadFileId, UploadFile = e.UploadFile, SortOrder = e.SortOrder, DateCreated = e.DateCreated,
                CreatedByAppUserId = e.CreatedByAppUserId, EventDateTime = e.EventDateTime, DraftRevision = e.DraftRevision,
                PublishedRevision = e.PublishedRevision, DraftSavedUtc = e.DraftSavedUtc, DraftAuthorAppUserId = e.DraftAuthorAppUserId,
                PublishedUtc = e.PublishedUtc, Excerpt = e.Excerpt,
                // Only whether each copy exists, as a marker: the rules below ask nothing more of them.
                DraftBlocksJson = e.DraftBlocksJson == null ? null : "",
                PublishedBlocksJson = e.PublishedBlocksJson == null ? null : "",
            })
            .ToListAsync(ct);

        // A page nobody has published is its author's alone.
        var visible = entries.Where(e => e.ResearchType != CaseResearchType.Note
                                         || ResearchPages.IsPublished(e)
                                         || e.DraftAuthorAppUserId == userId
                                         || (e.DraftAuthorAppUserId is null && e.CreatedByAppUserId == userId));

        return Ok(visible.Select(e => ToDto(e, userId)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<CaseResearchEntryDto>> Create(
        Guid orgId, Guid caseId, [FromBody] UpsertResearchRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Give it a title.");

        var maxOrder = await db.CaseResearchEntries.Where(e => e.CaseId == caseId).MaxAsync(e => (int?)e.SortOrder, ct) ?? 0;
        var entry = new CaseResearchEntry
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            ResearchType = request.ResearchType, Title = request.Title.Trim(),
            Url = request.Url?.Trim(), EventDateTime = request.EventDateTime,
            SortOrder = maxOrder + 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };

        if (request.ResearchType == CaseResearchType.Note)
        {
            // A new note is a page, and starts as its author's draft. Any body sent with it becomes the first text block.
            var doc = new BlockDocument();
            if (Ben.Data.Common.Text.PlainTextHtml.HasText(request.Body))
                doc.Blocks.Add(new Block { Id = Guid.NewGuid().ToString("N"), Kind = BlockKinds.Text, Html = _sanitizer.SanitizeHtml(request.Body) });
            entry.DraftBlocksJson = BlockDocumentSerializer.Serialize(doc);
            entry.DraftRevision = 1;
            entry.DraftSavedUtc = entry.DateCreated;
            entry.DraftAuthorAppUserId = userId;
        }
        else
        {
            // A link's or file's description was stored as sent and drawn as markup.
            entry.Body = request.Body is null ? null : _sanitizer.SanitizeHtml(request.Body).Trim();
        }

        db.CaseResearchEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(entry, userId));
    }

    [HttpPost("files")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<CaseResearchEntryDto>> UploadFile(
        Guid orgId, Guid caseId, [FromForm] string title, [FromForm] string? description,
        IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var (uploadFile, refusal) = await IngestAsync(db, orgId, caseId, userId, file, ct);
        if (uploadFile is null) return BadRequest(refusal);

        var maxOrder = await db.CaseResearchEntries.Where(e => e.CaseId == caseId).MaxAsync(e => (int?)e.SortOrder, ct) ?? 0;
        var entry = new CaseResearchEntry
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            ResearchType = CaseResearchType.File,
            Title = title.Trim(), Body = description is null ? null : _sanitizer.SanitizeHtml(description).Trim(),
            UploadFileId = uploadFile.Id,
            SortOrder = maxOrder + 10,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseResearchEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        entry.UploadFile = uploadFile;
        return Ok(ToDto(entry, userId));
    }

    [HttpPut("{entryId:guid}")]
    public async Task<ActionResult<CaseResearchEntryDto>> Update(
        Guid orgId, Guid caseId, Guid entryId, [FromBody] UpsertResearchRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Give it a title.");

        var entry = await db.CaseResearchEntries.Include(e => e.UploadFile)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null) return NotFound();

        entry.Title = request.Title.Trim();
        entry.Url   = request.Url?.Trim();
        entry.EventDateTime = request.EventDateTime;
        // A page's words live in its blocks and change through the draft; Body is a link's or file's description, or the
        // text of a note from before pages that nobody has turned into one yet.
        if (entry.ResearchType != CaseResearchType.Note || ResearchPages.IsLegacyNote(entry))
            entry.Body = request.Body is null ? null : _sanitizer.SanitizeHtml(request.Body).Trim();
        entry.DateUpdated = DateTime.UtcNow;
        entry.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(entry, userId));
    }

    [HttpDelete("{entryId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid entryId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Delete, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var entry = await db.CaseResearchEntries.Include(e => e.UploadFile).Include(e => e.Attachments).ThenInclude(a => a.UploadFile)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null) return NotFound();

        var paths = new List<string>();
        if (entry.UploadFile?.StoragePath is { } own) paths.Add(own);
        foreach (var a in entry.Attachments)
        {
            if (a.UploadFile is { } f)
            {
                if (f.StoragePath is { } p) paths.Add(p);
                db.UploadFiles.Remove(f);
            }
            db.CaseResearchAttachments.Remove(a);
        }
        db.CaseResearchEntries.Remove(entry);
        if (entry.UploadFile is not null) db.UploadFiles.Remove(entry.UploadFile);
        await db.SaveChangesAsync(ct);

        foreach (var path in paths) await _fileStorage.DeleteAsync(path, ct);
        return NoContent();
    }

    // ── Research pages ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The page as this person should see it: their own unpublished draft, or else the published page.</summary>
    [HttpGet("{entryId:guid}/page")]
    public async Task<ActionResult<CaseResearchPageDto>> GetPage(Guid orgId, Guid caseId, Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var entry = await LoadPageEntryAsync(db, caseId, entryId, ct);
        if (entry is null) return NotFound();

        var mine = ResearchPages.HasUnpublishedDraft(entry) && entry.DraftAuthorAppUserId == userId;
        if (!mine && !ResearchPages.IsPublished(entry)) return NotFound();

        try
        {
            return Ok(await PageDtoAsync(db, entry, userId, showDraft: mine, ct));
        }
        catch (BlockDocumentFormatException ex)
        {
            return Problem(ex.Message, statusCode: 500);
        }
    }

    /// <summary>
    /// Saves the draft. Refused when someone else holds an unpublished draft, or when the page has been saved since the
    /// revision the editor started from.
    /// </summary>
    [HttpPost("{entryId:guid}/draft")]
    public async Task<ActionResult<ResearchDraftSavedDto>> SaveDraft(
        Guid orgId, Guid caseId, Guid entryId, [FromBody] SaveResearchDraftRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Give the page a title.");

        var entry = await LoadPageEntryAsync(db, caseId, entryId, ct);
        if (entry is null) return NotFound();

        // The same save sent twice — a retry after a dropped connection — is answered, not counted again.
        if (entry.DraftSaveId == request.ClientSaveId && entry.DraftAuthorAppUserId == userId && entry.DraftSavedUtc is { } already)
            return Ok(new ResearchDraftSavedDto(entry.DraftRevision, already));

        if (await HeldByOtherAsync(db, entry, userId, ct) is { } holder)
            return Conflict($"{holder} has unpublished changes on this page. They need to publish them before anyone else edits it.");
        if (request.BaseRevision != entry.DraftRevision)
            return Conflict("This page has been saved somewhere else since you opened it. Reload to see the latest before editing.");

        var (clean, refusal) = ResearchPages.Clean(request.Document ?? new BlockDocument(), _sanitizer, await UploadsOfAsync(db, entryId, ct));
        if (clean is null) return BadRequest(refusal);

        var json = BlockDocumentSerializer.Serialize(clean);
        if (json.Length > 2_000_000) return BadRequest("The page is too large to save. Split it into two pages.");

        var now = DateTime.UtcNow;
        var title = request.Title.Trim();
        if (title.Length > 300) title = title[..300];

        // Conditional on the revision it was checked against: two saves racing from the same revision cannot both land.
        var updated = await db.CaseResearchEntries
            .Where(e => e.Id == entryId && e.DraftRevision == request.BaseRevision)
            .ExecuteUpdateAsync(u => u
                .SetProperty(e => e.DraftBlocksJson, json)
                .SetProperty(e => e.DraftRevision, request.BaseRevision + 1)
                .SetProperty(e => e.DraftSavedUtc, now)
                .SetProperty(e => e.DraftAuthorAppUserId, userId)
                .SetProperty(e => e.DraftSaveId, request.ClientSaveId)
                .SetProperty(e => e.Title, title)
                .SetProperty(e => e.EventDateTime, request.EventDateTime)
                .SetProperty(e => e.DateUpdated, now)
                .SetProperty(e => e.UpdatedByAppUserId, userId), ct);
        if (updated == 0)
            return Conflict("This page has been saved somewhere else since you opened it. Reload to see the latest before editing.");

        return Ok(new ResearchDraftSavedDto(request.BaseRevision + 1, now));
    }

    /// <summary>Makes the draft the page everybody reads.</summary>
    [HttpPost("{entryId:guid}/publish")]
    public async Task<ActionResult<CaseResearchPageDto>> Publish(Guid orgId, Guid caseId, Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var entry = await db.CaseResearchEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId, ct);
        if (entry is null || entry.ResearchType != CaseResearchType.Note) return NotFound();
        if (!ResearchPages.HasUnpublishedDraft(entry))
            return BadRequest("There are no unpublished changes on this page.");
        if (await HeldByOtherAsync(db, entry, userId, ct) is { } holder)
            return Conflict($"{holder} has unpublished changes on this page. Only they can publish them.");

        var doc = BlockDocumentSerializer.Parse(entry.DraftBlocksJson);
        var excerpt = BlockDocumentSerializer.PlainTextExcerpt(doc);

        entry.PublishedBlocksJson = entry.DraftBlocksJson;
        entry.PublishedRevision = entry.DraftRevision;
        entry.PublishedUtc = DateTime.UtcNow;
        entry.PublishedByAppUserId = userId;
        entry.Excerpt = excerpt.Length == 0 ? null : excerpt;
        entry.DraftAuthorAppUserId = null;   // released: the next person to save starts the next draft
        entry.DateUpdated = entry.PublishedUtc;
        entry.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        entry.Attachments = await db.CaseResearchAttachments.AsNoTracking().Include(a => a.UploadFile)
            .Where(a => a.ResearchEntryId == entryId).ToListAsync(ct);
        return Ok(await PageDtoAsync(db, entry, userId, showDraft: false, ct));
    }

    // ── The rail ─────────────────────────────────────────────────────────────────────────────────────────────

    [HttpPost("{entryId:guid}/attachments/files")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<CaseResearchAttachmentDto>> UploadAttachment(
        Guid orgId, Guid caseId, Guid entryId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("The file is empty.");
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (!await db.CaseResearchEntries.AnyAsync(e => e.Id == entryId && e.CaseId == caseId && e.ResearchType == CaseResearchType.Note, ct))
            return NotFound();

        var (uploadFile, refusal) = await IngestAsync(db, orgId, caseId, userId, file, ct);
        if (uploadFile is null) return BadRequest(refusal);

        var attachment = new CaseResearchAttachment
        {
            Id = Guid.NewGuid(), ResearchEntryId = entryId, Kind = CaseResearchAttachmentKind.File,
            UploadFileId = uploadFile.Id, Title = Truncate(Path.GetFileName(file.FileName), 300),
            SortOrder = await NextRailOrderAsync(db, entryId, ct),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseResearchAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        attachment.UploadFile = uploadFile;
        return Ok(ToAttachmentDto(attachment));
    }

    [HttpPost("{entryId:guid}/attachments/links")]
    public async Task<ActionResult<CaseResearchAttachmentDto>> AddLinkAttachment(
        Guid orgId, Guid caseId, Guid entryId, [FromBody] AddResearchLinkRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (!await db.CaseResearchEntries.AnyAsync(e => e.Id == entryId && e.CaseId == caseId && e.ResearchType == CaseResearchType.Note, ct))
            return NotFound();

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.AbsoluteUri.Length > 2000)
            return BadRequest("That isn't a web address. It should start with http:// or https://.");

        // The same address pasted twice is one entry in the rail.
        var existing = await db.CaseResearchAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ResearchEntryId == entryId && a.Kind == CaseResearchAttachmentKind.Link && a.Url == url.AbsoluteUri, ct);
        if (existing is not null) return Ok(ToAttachmentDto(existing));

        var attachment = new CaseResearchAttachment
        {
            Id = Guid.NewGuid(), ResearchEntryId = entryId, Kind = CaseResearchAttachmentKind.Link,
            Url = url.AbsoluteUri, Title = Truncate(string.IsNullOrWhiteSpace(request.Title) ? url.Host : request.Title.Trim(), 300),
            SortOrder = await NextRailOrderAsync(db, entryId, ct),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseResearchAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return Ok(ToAttachmentDto(attachment));
    }

    /// <summary>Removes a file or link from the rail. A file still shown on the page is refused, with why.</summary>
    [HttpDelete("{entryId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid orgId, Guid caseId, Guid entryId, Guid attachmentId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var attachment = await db.CaseResearchAttachments.Include(a => a.UploadFile).Include(a => a.ResearchEntry)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ResearchEntryId == entryId && a.ResearchEntry.CaseId == caseId, ct);
        if (attachment is null) return NotFound();

        if (attachment.UploadFileId is { } fileId)
        {
            var entry = attachment.ResearchEntry;
            var shown = new[] { entry.DraftBlocksJson, entry.PublishedBlocksJson }
                .Where(j => j is not null)
                .SelectMany(j => BlockDocumentSerializer.Parse(j).Blocks)
                .Any(b => b.UploadFileId == fileId);
            if (shown)
                return Conflict("This file is shown on the page. Remove it from the page, and publish if it's on the published page, before deleting it.");
        }

        var path = attachment.UploadFile?.StoragePath;
        db.CaseResearchAttachments.Remove(attachment);
        if (attachment.UploadFile is not null) db.UploadFiles.Remove(attachment.UploadFile);
        await db.SaveChangesAsync(ct);
        if (path is not null) await _fileStorage.DeleteAsync(path, ct);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<CaseResearchEntry?> LoadPageEntryAsync(BenDataContext db, Guid caseId, Guid entryId, CancellationToken ct) =>
        db.CaseResearchEntries.AsNoTracking()
            .Include(e => e.Attachments).ThenInclude(a => a.UploadFile)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId && e.ResearchType == CaseResearchType.Note, ct);

    /// <summary>The name of whoever else holds an unpublished draft of this page, or null.</summary>
    private static async Task<string?> HeldByOtherAsync(BenDataContext db, CaseResearchEntry entry, Guid userId, CancellationToken ct)
    {
        if (!ResearchPages.HasUnpublishedDraft(entry) || entry.DraftAuthorAppUserId is not { } holder || holder == userId) return null;
        return await db.Users.AsNoTracking().Where(u => u.Id == holder).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
               ?? "Someone else";
    }

    private static async Task<IReadOnlyDictionary<Guid, ResearchPages.AllowedUpload>> UploadsOfAsync(
        BenDataContext db, Guid entryId, CancellationToken ct) =>
        await db.CaseResearchAttachments.AsNoTracking()
            .Where(a => a.ResearchEntryId == entryId && a.UploadFileId != null)
            .Select(a => new { Id = a.UploadFileId!.Value, a.UploadFile!.FileName, a.UploadFile.ContentType, a.UploadFile.FileSize })
            .ToDictionaryAsync(x => x.Id, x => new ResearchPages.AllowedUpload(x.FileName, x.ContentType, x.FileSize), ct);

    private async Task<CaseResearchPageDto> PageDtoAsync(BenDataContext db, CaseResearchEntry entry, Guid userId, bool showDraft, CancellationToken ct)
    {
        var names = await db.Users.AsNoTracking()
            .Where(u => u.Id == entry.PublishedByAppUserId || u.Id == entry.DraftAuthorAppUserId)
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var held = ResearchPages.HasUnpublishedDraft(entry) ? entry.DraftAuthorAppUserId : null;
        return new CaseResearchPageDto(
            entry.Id, entry.CaseId, entry.Title, entry.EventDateTime,
            Document: showDraft ? ResearchPages.DraftOf(entry, _sanitizer) : ResearchPages.PublishedOf(entry, _sanitizer),
            IsDraft: showDraft,
            Revision: entry.DraftRevision,
            PublishedUtc: entry.PublishedUtc ?? (ResearchPages.IsLegacyNote(entry) ? entry.DateCreated : null),
            PublishedByName: entry.PublishedByAppUserId is { } pub && names.TryGetValue(pub, out var pn) ? pn : null,
            DraftSavedUtc: entry.DraftSavedUtc,
            HasUnpublishedDraft: held is not null,
            DraftHeldByName: held is { } h && h != userId && names.TryGetValue(h, out var hn) ? hn : null,
            DraftHeldByMe: held == userId,
            Attachments: entry.Attachments.OrderBy(a => a.SortOrder).ThenBy(a => a.DateCreated).Select(ToAttachmentDto).ToList());
    }

    private async Task<(UploadFile? File, string? Refusal)> IngestAsync(
        BenDataContext db, Guid orgId, Guid caseId, Guid userId, IFormFile file, CancellationToken ct)
    {
        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"research/{storedName}");
        // Ben's rule (2026-08-24): strip on ANY upload, keep what came off in the metadata table.
        // The ORIGINAL stays untouched; the derivative is what serve paths return (item 179).
        var uploadFileId = Guid.NewGuid();
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct,
                (await MediaStrippingPolicy.ForOrganizationAsync(db, _avStripper, orgId, ct)).Strips);
        }
        catch (UnreadableImageException ex)
        {
            return (null, ex.Message);
        }

        var evidenceTypeId = new Guid("20000000-0000-0000-0000-000000000001"); // Case Evidence upload type
        var uploadFile = new UploadFile
        {
            Id = uploadFileId, UploadFileTypeId = evidenceTypeId, AppUserId = userId,
            FileName = Path.GetFileName(file.FileName), StoredFileName = storedName,
            ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        db.UploadFileMetadata.Add(ingested.Metadata);
        return (uploadFile, null);
    }

    private static async Task<int> NextRailOrderAsync(BenDataContext db, Guid entryId, CancellationToken ct) =>
        (await db.CaseResearchAttachments.Where(a => a.ResearchEntryId == entryId).MaxAsync(a => (int?)a.SortOrder, ct) ?? 0) + 10;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // Item 156 Phase D: bare membership stopped being the rule here — see CaseFileController.
    /// <summary>
    /// May the caller take this action here?
    /// </summary>
    /// <remarks>
    /// Create, update and delete used to ask for Case.READ — through a helper named for
    /// membership, which is neither what it asked nor what it meant: anybody who could SEE a case
    /// could destroy the things hanging off it. Survivable while every member was auto-granted
    /// case read; not survivable now that Ben ended the grandfathering (2026-08-26) and a read
    /// grant is a deliberate act. Owners and administrators still pass above this.
    /// </remarks>
    private Task<bool> MayAsync(Guid orgId, Ben.Data.Common.Enums.OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId,
                Ben.Data.Common.Enums.OrganizationPermissionArea.Cases, action, ct);

    private async Task<bool> IsOrgMember(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(userId, orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);

    private CaseResearchEntryDto ToDto(CaseResearchEntry e, Guid userId)
    {
        var isPage = e.ResearchType == CaseResearchType.Note;
        return new CaseResearchEntryDto(
            e.Id, e.CaseId, e.ResearchType, e.Title,
            // A page's words are its blocks; the list shows its summary instead of a body.
            isPage && !ResearchPages.IsLegacyNote(e) ? null : e.Body,
            e.Url,
            e.UploadFile is null ? null : new ResearchFileInfo(e.UploadFile.Id, e.UploadFile.FileName, e.UploadFile.ContentType, e.UploadFile.FileSize),
            e.SortOrder, e.DateCreated,
            EventDateTime: e.EventDateTime,
            PublishedUtc: isPage ? e.PublishedUtc ?? (ResearchPages.IsLegacyNote(e) ? e.DateCreated : null) : null,
            DraftSavedUtc: e.DraftSavedUtc,
            HasUnpublishedDraft: isPage && ResearchPages.HasUnpublishedDraft(e),
            DraftIsMine: isPage && ResearchPages.HasUnpublishedDraft(e) && e.DraftAuthorAppUserId == userId,
            Excerpt: isPage ? e.Excerpt ?? (ResearchPages.IsLegacyNote(e) ? ResearchPages.ExcerptOf(e, _sanitizer) : null) : null);
    }

    private static CaseResearchAttachmentDto ToAttachmentDto(CaseResearchAttachment a) => new(
        a.Id, a.Kind, a.Title,
        a.UploadFileId, a.UploadFile?.FileName, a.UploadFile?.ContentType, a.UploadFile?.FileSize,
        a.Url, a.SortOrder, a.DateCreated);
}

public sealed record UpsertResearchRequest(
    Ben.Data.Common.Enums.CaseResearchType ResearchType,
    string  Title,
    string? Body,
    string? Url,
    // Research pages (2026-09-14): when what the page is about happened. Optional and last; older callers send nothing.
    DateTime? EventDateTime = null);

public sealed record CaseResearchEntryDto(
    Guid                                        Id,
    Guid                                        CaseId,
    Ben.Data.Common.Enums.CaseResearchType      ResearchType,
    string                                      Title,
    string?                                     Body,
    string?                                     Url,
    ResearchFileInfo?                           File,
    int                                         SortOrder,
    DateTime                                    DateCreated,
    // Research pages (2026-09-14), all optional and appended.
    DateTime?                                   EventDateTime = null,
    DateTime?                                   PublishedUtc = null,
    DateTime?                                   DraftSavedUtc = null,
    bool                                        HasUnpublishedDraft = false,
    bool                                        DraftIsMine = false,
    string?                                     Excerpt = null);

public sealed record ResearchFileInfo(Guid FileId, string FileName, string ContentType, long FileSize);

/// <summary>A research page, as one reader sees it.</summary>
/// <param name="IsDraft">True when <paramref name="Document"/> is the reader's own unpublished draft.</param>
/// <param name="Revision">The draft revision a save from this page must name as its base.</param>
/// <param name="DraftHeldByName">Someone else holds unpublished changes; this reader sees the published page.</param>
public sealed record CaseResearchPageDto(
    Guid Id,
    Guid CaseId,
    string Title,
    DateTime? EventDateTime,
    BlockDocument Document,
    bool IsDraft,
    int Revision,
    DateTime? PublishedUtc,
    string? PublishedByName,
    DateTime? DraftSavedUtc,
    bool HasUnpublishedDraft,
    string? DraftHeldByName,
    bool DraftHeldByMe,
    IReadOnlyList<CaseResearchAttachmentDto> Attachments);

public sealed record CaseResearchAttachmentDto(
    Guid Id,
    CaseResearchAttachmentKind Kind,
    string Title,
    Guid? FileId,
    string? FileName,
    string? ContentType,
    long? FileSize,
    string? Url,
    int SortOrder,
    DateTime DateCreated);

/// <param name="ClientSaveId">A new id for each save the editor makes; the same id again is a retry.</param>
public sealed record SaveResearchDraftRequest(
    BlockDocument? Document,
    int BaseRevision,
    Guid ClientSaveId,
    string Title,
    DateTime? EventDateTime);

public sealed record ResearchDraftSavedDto(int Revision, DateTime SavedUtc);

public sealed record AddResearchLinkRequest(string Url, string? Title = null, bool RefreshPreview = false);
