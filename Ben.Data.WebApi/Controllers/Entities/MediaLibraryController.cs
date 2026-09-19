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
    //                             [&scope=all|personal|case][&caseId=][&investigationId=]
    /// <summary>
    /// The files this person may see, optionally narrowed to a scope.
    /// </summary>
    /// <remarks>
    /// <para><b>The scope narrows; it never widens.</b> The audience union below is computed first
    /// and in full, exactly as it always was, and a scope is applied as an intersection over the
    /// result. That ordering is the whole safety property: no scope can return a file the caller
    /// was not already entitled to, whatever ids they put in the query string. Somebody who names
    /// a case they cannot reach gets an empty list rather than its contents.</para>
    ///
    /// <para>Why scoping is here rather than in the editor: this endpoint is the only place that
    /// knows what the caller may see. A client-side filter over an unfiltered list would ship every
    /// file's metadata to a browser to hide most of it, and would still send the whole list over
    /// the wire — which is the problem the scope exists to solve. See backlog item 91.</para>
    /// </remarks>
    [HttpGet("files")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetFiles(
        [FromQuery] string? contentTypePrefixes,
        [FromQuery] string? scope,
        [FromQuery] Guid? caseId,
        [FromQuery] Guid? investigationId,
        CancellationToken ct)
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

        // ── The requested scope, as an intersection ──────────────────────────
        // Applied to the finished union above rather than woven into it, so that narrowing cannot
        // accidentally become widening. See the remarks on this method.
        switch (scope?.Trim().ToLowerInvariant())
        {
            case "personal":
                var ownIds = await db.UploadFiles.AsNoTracking()
                    .Where(f => f.AppUserId == userId)
                    .Select(f => f.Id)
                    .ToListAsync(ct);
                idSet.IntersectWith(ownIds);
                break;

            case "case":
                // A case scope with no case named is not "every case" — it is a half-made
                // selection, and returning everything would be the opposite of what was asked.
                if (caseId is null) { idSet.Clear(); break; }
                idSet.IntersectWith(await CaseMediaIdsAsync(db, caseId.Value, investigationId, ct));
                break;

            // "all", null, or anything unrecognised: the full audience union, as before. An
            // unknown scope string reads as no scope rather than as an error, because the failure
            // it would otherwise cause is a blank media tab for a typo.
        }

        // Archived prior versions (item #6 phase 3) are implementation detail, not a real listing —
        // excluded regardless of which scope above happened to surface their Id.
        var query = db.UploadFiles.AsNoTracking()
            .Where(f => idSet.Contains(f.Id) && f.ArchivedFromUploadFileId == null);

        var prefixes = contentTypePrefixes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var listed = await query.OrderByDescending(f => f.DateCreated).ToListAsync(ct);
        if (prefixes is { Length: > 0 })
            listed = listed.Where(f => prefixes.Any(p => f.ContentType.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();

        return Ok(await WithOwnerAndCaseAsync(db, listed, ct));
    }

    /// <summary>
    /// The mapped records, each told who owns it and which case it belongs to.
    /// </summary>
    /// <remarks>
    /// <para>V-3 of the 2026-09-06 evaluation. The video editor's Server tab listed seven
    /// identical <c>test-audio.mp3</c> rows — other people's uploads, reachable through a shared
    /// group — with no owner and no case. A file name is not an identity, and this listing is the
    /// one place that spans several people's files at once.</para>
    ///
    /// <para>Two batched lookups over the page that is actually being returned, not a join in the
    /// query above: the audience union is built from id sets and the result is already in memory,
    /// so this costs two round trips regardless of how large the union was.</para>
    ///
    /// <para>A file handed to a group (item 180 Phase B) has no owning person; the group's name
    /// is the answer then, and it is labelled as a group on the card.</para>
    /// </remarks>
    private async Task<IEnumerable<UploadFileRecord>> WithOwnerAndCaseAsync(
        BenDataContext db, List<Ben.Data.Source.Entities.UploadFile> files, CancellationToken ct)
    {
        // Map<IEnumerable<…>>, matching every other call in this controller — the profile is
        // registered for that shape and nothing else.
        var records = _mapper.Map<IEnumerable<UploadFileRecord>>(files).ToList();
        if (records.Count == 0) return records;

        var fileIds = records.Select(r => r.Id).ToList();

        var userIds = records.Where(r => r.AppUserId.HasValue).Select(r => r.AppUserId!.Value).Distinct().ToList();
        var orgIdsOwning = records.Where(r => r.OwnerOrganizationId.HasValue)
                                  .Select(r => r.OwnerOrganizationId!.Value).Distinct().ToList();

        var owners = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.AppUsers.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email ?? "Someone", ct);

        var owningOrgs = orgIdsOwning.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Organizations.AsNoTracking()
                .Where(o => orgIdsOwning.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        // The three ways a file belongs to a case, in the order the reader would name them.
        var caseOf = new Dictionary<Guid, string>();

        void Remember(Guid fileId, int year, int number)
        {
            if (!caseOf.ContainsKey(fileId)) caseOf[fileId] = $"#{year}-{number:D3}";
        }

        foreach (var row in await db.CaseFiles.AsNoTracking()
                     .Where(cf => fileIds.Contains(cf.UploadFileId))
                     .Select(cf => new { cf.UploadFileId, cf.Case.CaseYear, cf.Case.OrgCaseNumber })
                     .ToListAsync(ct))
            Remember(row.UploadFileId, row.CaseYear, row.OrgCaseNumber);

        foreach (var row in await db.CaseTimelineEntryFiles.AsNoTracking()
                     .Where(ef => fileIds.Contains(ef.UploadFileId))
                     .Select(ef => new { ef.UploadFileId,
                                         ef.CaseTimelineEntry.Case.CaseYear,
                                         ef.CaseTimelineEntry.Case.OrgCaseNumber })
                     .ToListAsync(ct))
            Remember(row.UploadFileId, row.CaseYear, row.OrgCaseNumber);

        foreach (var row in await db.VideoProjects.AsNoTracking()
                     .Where(vp => vp.PublishedUploadFileId.HasValue
                               && fileIds.Contains(vp.PublishedUploadFileId.Value)
                               && vp.Case != null)
                     .Select(vp => new { FileId = vp.PublishedUploadFileId!.Value,
                                         vp.Case!.CaseYear, vp.Case.OrgCaseNumber })
                     .ToListAsync(ct))
            Remember(row.FileId, row.CaseYear, row.OrgCaseNumber);

        return records.Select(r => r with
        {
            OwnerDisplayName = r.OwnerOrganizationId is { } orgId
                ? owningOrgs.TryGetValue(orgId, out var orgName) ? $"{orgName} (group)" : null
                : r.AppUserId is { } ownerId && owners.TryGetValue(ownerId, out var name) ? name : null,
            CaseReference = caseOf.GetValueOrDefault(r.Id),
        }).ToList();
    }

    /// <summary>
    /// Every file attached to one case — or to one investigation within it.
    /// </summary>
    /// <remarks>
    /// <para>"Attached" means the three ways a file reaches a case: a published video project, a
    /// link on the case's Files tab, and evidence on a timeline entry. Narrowing to an
    /// investigation keeps the timeline entries recorded against that visit, and adds files shared
    /// with its team — the only two ways a file belongs to a single visit rather than to the case
    /// as a whole. A published video project belongs to the case, so it drops out of an
    /// investigation scope, which is correct: it is the case's output, not that night's material.</para>
    ///
    /// <para>No access check here on purpose. The caller intersects this with what they may
    /// already see, so an id they have no business with contributes nothing.</para>
    /// </remarks>
    private static async Task<HashSet<Guid>> CaseMediaIdsAsync(
        BenDataContext db, Guid caseId, Guid? investigationId, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();

        if (investigationId is null)
        {
            ids.UnionWith(await db.VideoProjects.AsNoTracking()
                .Where(p => p.CaseId == caseId && p.PublishedUploadFileId.HasValue)
                .Select(p => p.PublishedUploadFileId!.Value)
                .ToListAsync(ct));
            ids.UnionWith(await db.CaseFiles.AsNoTracking()
                .Where(cf => cf.CaseId == caseId)
                .Select(cf => cf.UploadFileId)
                .ToListAsync(ct));
            ids.UnionWith(await db.CaseTimelineEntryFiles.AsNoTracking()
                .Where(ef => ef.CaseTimelineEntry.CaseId == caseId)
                .Select(ef => ef.UploadFileId)
                .ToListAsync(ct));
            return ids;
        }

        // The investigation must belong to the named case, or naming a case would be decorative
        // and an investigation id alone would reach across cases.
        ids.UnionWith(await db.CaseTimelineEntryFiles.AsNoTracking()
            .Where(ef => ef.CaseTimelineEntry.CaseId == caseId
                      && ef.CaseTimelineEntry.InvestigationId == investigationId)
            .Select(ef => ef.UploadFileId)
            .ToListAsync(ct));
        ids.UnionWith(await db.UploadFileShares.AsNoTracking()
            .Where(s => s.IsActive
                     && s.TargetType == ShareTargetType.InvestigationTeam
                     && s.TargetInvestigationId == investigationId
                     && s.TargetInvestigation!.CaseId == caseId)
            .Select(s => s.UploadFileId)
            .ToListAsync(ct));

        return ids;
    }

    // GET /api/media-library/scopes
    /// <summary>
    /// The cases, and the investigations within them, this person can scope the library by.
    /// </summary>
    /// <remarks>
    /// <para>Exists so the editor can offer a scope selector without knowing what a case is. It
    /// receives labels and ids, renders them, and sends an id back — the meaning stays on this
    /// side, which is what keeps a general-purpose editor component from growing a dependency on
    /// this product's domain.</para>
    ///
    /// <para>Only cases at organisations the caller actively belongs to. That is the same set the
    /// file union treats as case-accessible, so every scope offered here can return something and
    /// no scope offered here can return anything the caller could not already reach.</para>
    /// </remarks>
    [HttpGet("scopes")]
    public async Task<ActionResult<IEnumerable<MediaScopeCase>>> GetScopes(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        if (orgIds.Count == 0) return Ok(Array.Empty<MediaScopeCase>());

        var cases = await db.Cases.AsNoTracking()
            .Where(c => orgIds.Contains(c.OrganizationId))
            .OrderByDescending(c => c.DateCreated)
            .Select(c => new { c.Id, c.Title })
            .ToListAsync(ct);

        var caseIds = cases.Select(c => c.Id).ToList();

        // CaseId is nullable — a visit can be scheduled with no case behind it — so a case-less
        // investigation belongs under no case here and is simply not offered as a scope.
        //
        // Ordered on the entity before the projection: ordering a projected record is not
        // translatable and fails at runtime rather than at compile time. That mistake has been
        // made twice in this codebase already.
        var investigations = await db.Investigations.AsNoTracking()
            .Where(i => i.CaseId != null && caseIds.Contains(i.CaseId.Value))
            .OrderByDescending(i => i.ScheduledDateTime)
            .Select(i => new { i.Id, i.CaseId, i.Title, i.ScheduledDateTime })
            .ToListAsync(ct);

        var byCase = investigations.GroupBy(i => i.CaseId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(i =>
                new MediaScopeInvestigation(i.Id, $"{i.ScheduledDateTime:MMM d, yyyy} — {i.Title}")).ToList());

        return Ok(cases.Select(c => new MediaScopeCase(
            c.Id, c.Title, byCase.GetValueOrDefault(c.Id) ?? [])));
    }
}

/// <summary>One case the media library can be scoped to, with its visits.</summary>
public sealed record MediaScopeCase(Guid Id, string Title, IReadOnlyList<MediaScopeInvestigation> Investigations);

/// <summary>One visit within a case. <paramref name="Label"/> is already formatted for display.</summary>
public sealed record MediaScopeInvestigation(Guid Id, string Label);
