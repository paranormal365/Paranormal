using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Returns files available to the current user across every scope the universal media library
/// aggregates: owned, shared directly with them, shared with an investigation team they're on,
/// shared with an organization they belong to (via either the tiered
/// <see cref="Ben.Data.Source.Entities.UploadFileOrganizationShare"/> table or the newer
/// <see cref="Ben.Data.Source.Entities.UploadFileShare"/> table), public files, and files linked
/// to a case in an accessible org (via CaseFile, CaseTimelineEntryFile, or a published video).
/// </summary>
/// <remarks>
/// Also consumed by <c>BenMediaLibraryProvider</c> (Ben.Video.Editor's asset picker), which passes
/// <c>contentTypePrefixes=video/,audio/,image/</c> explicitly to preserve its original narrower
/// behavior — the default (no filter) is used by the new standalone `/media-library` page.
/// </remarks>
[ApiController]
[Route("api/media-library")]
[Authorize]
public sealed class MediaLibraryController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public MediaLibraryController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    {
        _db     = db;
        _mapper = mapper;
    }

    // GET /api/media-library/files[?contentTypePrefixes=video/,audio/,image/]
    [HttpGet("files")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetFiles(
        [FromQuery] string? contentTypePrefixes, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var orgMemberships = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync(ct);
        var orgIds = orgMemberships.Select(m => m.OrganizationId).ToList();
        var adminOrgIds = orgMemberships
            .Where(m => m.Role <= OrganizationMemberRole.Administrator)
            .Select(m => m.OrganizationId)
            .ToHashSet();

        var investigationIds = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .Select(a => a.InvestigationId)
            .ToListAsync(ct);

        var caseIds = await db.Cases.AsNoTracking()
            .Where(c => orgIds.Contains(c.OrganizationId))
            .Select(c => c.Id)
            .ToListAsync(ct);

        var idSet = new HashSet<Guid>();

        // 1. Owned
        idSet.UnionWith(await db.UploadFiles.AsNoTracking()
            .Where(f => f.AppUserId == userId)
            .Select(f => f.Id)
            .ToListAsync(ct));

        // 2. Shared with me personally
        idSet.UnionWith(await db.UploadFileShares.AsNoTracking()
            .Where(s => s.IsActive && s.TargetType == ShareTargetType.Person && s.TargetAppUserId == userId)
            .Select(s => s.UploadFileId)
            .ToListAsync(ct));

        // 3. Shared with an investigation team I'm on
        if (investigationIds.Count > 0)
        {
            idSet.UnionWith(await db.UploadFileShares.AsNoTracking()
                .Where(s => s.IsActive && s.TargetType == ShareTargetType.InvestigationTeam
                         && investigationIds.Contains(s.TargetInvestigationId!.Value))
                .Select(s => s.UploadFileId)
                .ToListAsync(ct));
        }

        if (orgIds.Count > 0)
        {
            // 4a. Shared with my org(s) — tiered table, respecting visibility vs. my role
            var tieredOrgShares = await db.UploadFileOrganizationShares.AsNoTracking()
                .Where(s => s.IsActive && orgIds.Contains(s.OrganizationId))
                .Select(s => new { s.UploadFileId, s.OrganizationId, s.Visibility })
                .ToListAsync(ct);
            idSet.UnionWith(tieredOrgShares
                .Where(s => s.Visibility == FileShareVisibility.Public
                         || s.Visibility == FileShareVisibility.OrgMembers
                         || (s.Visibility == FileShareVisibility.OrgAdminsOnly && adminOrgIds.Contains(s.OrganizationId)))
                .Select(s => s.UploadFileId));

            // 4b. Shared with my org(s) — new generalized table
            idSet.UnionWith(await db.UploadFileShares.AsNoTracking()
                .Where(s => s.IsActive && s.TargetType == ShareTargetType.Organization
                         && orgIds.Contains(s.TargetOrganizationId!.Value))
                .Select(s => s.UploadFileId)
                .ToListAsync(ct));
        }

        // 5. Public
        idSet.UnionWith(await db.UploadFiles.AsNoTracking()
            .Where(f => f.IsPublic)
            .Select(f => f.Id)
            .ToListAsync(ct));
        idSet.UnionWith(await db.UploadFileShares.AsNoTracking()
            .Where(s => s.IsActive && s.TargetType == ShareTargetType.Public)
            .Select(s => s.UploadFileId)
            .ToListAsync(ct));

        // 6. Case-scope — published videos, Files-tab links, and timeline evidence, for any accessible case
        if (caseIds.Count > 0)
        {
            idSet.UnionWith(await db.VideoProjects.AsNoTracking()
                .Where(p => p.CaseId.HasValue && caseIds.Contains(p.CaseId.Value) && p.PublishedUploadFileId.HasValue)
                .Select(p => p.PublishedUploadFileId!.Value)
                .ToListAsync(ct));
            idSet.UnionWith(await db.CaseFiles.AsNoTracking()
                .Where(cf => caseIds.Contains(cf.CaseId))
                .Select(cf => cf.UploadFileId)
                .ToListAsync(ct));
            idSet.UnionWith(await db.CaseTimelineEntryFiles.AsNoTracking()
                .Where(ef => caseIds.Contains(ef.CaseTimelineEntry.CaseId))
                .Select(ef => ef.UploadFileId)
                .ToListAsync(ct));
        }

        var query = db.UploadFiles.AsNoTracking().Where(f => idSet.Contains(f.Id));

        var prefixes = contentTypePrefixes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (prefixes is { Length: > 0 })
        {
            var files = await query.ToListAsync(ct);
            files = files.Where(f => prefixes.Any(p => f.ContentType.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
            return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(files.OrderByDescending(f => f.DateCreated)));
        }

        var all = await query.OrderByDescending(f => f.DateCreated).ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(all));
    }
}
