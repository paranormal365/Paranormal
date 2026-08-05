using AutoMapper;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Returns files available to the current user in the Ben.Video media library:
/// their own media uploads plus published videos from cases they have org access to.
/// </summary>
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

    // GET /api/media-library/files
    [HttpGet("files")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetFiles(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        // Org IDs the user belongs to
        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        // Case IDs from those orgs — for published video lookup
        var caseIds = await db.Cases.AsNoTracking()
            .Where(c => orgIds.Contains(c.OrganizationId))
            .Select(c => c.Id)
            .ToListAsync(ct);

        // Published video UploadFile IDs from accessible cases
        var publishedIds = await db.VideoProjects.AsNoTracking()
            .Where(p => p.CaseId.HasValue
                     && caseIds.Contains(p.CaseId.Value)
                     && p.PublishedUploadFileId.HasValue)
            .Select(p => p.PublishedUploadFileId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // User's own media files (video / audio / image)
        var myFiles = await db.UploadFiles.AsNoTracking()
            .Where(f => f.AppUserId == userId
                     && (f.ContentType.StartsWith("video/")
                      || f.ContentType.StartsWith("audio/")
                      || f.ContentType.StartsWith("image/")))
            .ToListAsync(ct);

        // Published files from cases (deduplicate against owned)
        var myIds = myFiles.Select(f => f.Id).ToHashSet();
        var sharedFiles = publishedIds.Count == 0
            ? []
            : await db.UploadFiles.AsNoTracking()
                .Where(f => publishedIds.Contains(f.Id) && !myIds.Contains(f.Id))
                .ToListAsync(ct);

        var all = myFiles.Concat(sharedFiles)
            .OrderByDescending(f => f.DateCreated);

        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(all));
    }
}
