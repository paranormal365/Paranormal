using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Comments on an UploadFile (item #6 phase 2). Reading the thread only requires being able to
/// see the file at all (<see cref="FileAudienceAccess.CanViewFileAsync"/>); posting additionally
/// requires the author to be the file's owner, or to match an audience the owner has enabled via
/// the file's <c>Allow*Comments</c> toggles — see <see cref="FileAudienceAccess"/> for exactly how
/// audience membership is determined.
/// </summary>
[ApiController]
[Route("api/upload-files/{fileId:guid}/comments")]
[Authorize]
public sealed class UploadFileCommentController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public UploadFileCommentController(IDbContextFactory<BenDataContext> db, IMapper mapper, IAuditLogService auditLog)
    {
        _db = db;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileCommentRecord>>> GetAll(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return NotFound();

        var comments = await db.UploadFileComments.AsNoTracking()
            .Where(c => c.UploadFileId == fileId)
            .OrderBy(c => c.DateCreated)
            .ToListAsync(ct);
        var authorNames = await db.AppUsers.AsNoTracking()
            .Where(a => comments.Select(c => c.AuthorAppUserId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.DisplayName, ct);
        var records = comments.Select(c => _mapper.Map<UploadFileCommentRecord>(c)
            with { AuthorDisplayName = authorNames.GetValueOrDefault(c.AuthorAppUserId) });
        return Ok(records);
    }

    [HttpPost]
    public async Task<ActionResult<UploadFileCommentRecord>> Create(
        Guid fileId, [FromBody] CreateFileCommentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Comment text is required.");

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return NotFound();

        var membership = await FileAudienceAccess.GetMembershipAsync(db, fileId, userId, ct);
        var canPost = membership.IsOwner
            || (membership.IsInvestigationTeamMember && file.AllowInvestigationTeamComments)
            || (membership.IsClient && file.AllowClientComments)
            || (membership.IsOrganizationMember && file.AllowOrganizationComments)
            || (membership.IsPublicCommenter && file.AllowPublicComments);
        if (!canPost) return Forbid();

        var entity = new UploadFileComment
        {
            Id = Guid.NewGuid(),
            UploadFileId = fileId,
            AuthorAppUserId = userId,
            Text = request.Text.Trim(),
            IsOwner = membership.IsOwner,
            IsInvestigationTeamMember = membership.IsInvestigationTeamMember,
            IsClient = membership.IsClient,
            IsOrganizationMember = membership.IsOrganizationMember,
            IsPublicCommenter = membership.IsPublicCommenter,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFileComments.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFileComment), entity.Id, entity, userId, AppSources.WebApi, ct));
        var authorName = await db.AppUsers.AsNoTracking().Where(a => a.Id == userId).Select(a => a.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(_mapper.Map<UploadFileCommentRecord>(entity) with { AuthorDisplayName = authorName });
    }

    [HttpPut("{commentId:guid}")]
    public async Task<ActionResult<UploadFileCommentRecord>> Update(
        Guid fileId, Guid commentId, [FromBody] UpdateFileCommentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("Comment text is required.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var before = await db.UploadFileComments.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == commentId && c.UploadFileId == fileId, ct);
        if (before is null) return NotFound();
        if (before.AuthorAppUserId != userId) return Forbid();

        var entity = await db.UploadFileComments.FirstAsync(c => c.Id == commentId, ct);
        entity.Text = request.Text.Trim();
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileComment), commentId, before, entity, userId, AppSources.WebApi, ct));
        var authorName = await db.AppUsers.AsNoTracking().Where(a => a.Id == entity.AuthorAppUserId).Select(a => a.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(_mapper.Map<UploadFileCommentRecord>(entity) with { AuthorDisplayName = authorName });
    }

    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, Guid commentId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.UploadFileComments.FirstOrDefaultAsync(c => c.Id == commentId && c.UploadFileId == fileId, ct);
        if (entity is null) return NotFound();

        // Author can delete their own comment; the file owner can moderate/delete any comment.
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (entity.AuthorAppUserId != userId && file?.AppUserId != userId) return Forbid();

        db.UploadFileComments.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UploadFileComment), commentId, entity, userId, AppSources.WebApi, ct));
        return NoContent();
    }

    [HttpGet("settings")]
    public async Task<ActionResult<FileCommentSettingsRecord>> GetSettings(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return NotFound();

        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return NotFound();
        return Ok(new FileCommentSettingsRecord(
            file.AllowInvestigationTeamComments, file.AllowClientComments,
            file.AllowOrganizationComments, file.AllowPublicComments));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<FileCommentSettingsRecord>> UpdateSettings(
        Guid fileId, [FromBody] FileCommentSettingsRecord request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var file = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return NotFound();
        if (file.AppUserId != userId) return Forbid();

        file.AllowInvestigationTeamComments = request.AllowInvestigationTeamComments;
        file.AllowClientComments = request.AllowClientComments;
        file.AllowOrganizationComments = request.AllowOrganizationComments;
        file.AllowPublicComments = request.AllowPublicComments;
        file.DateUpdated = DateTime.UtcNow;
        file.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(request);
    }
}
