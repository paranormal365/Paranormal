using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A case's points of contact (item 158): who the client talks to, besides the case manager.
/// </summary>
/// <remarks>
/// The read always answers with at least one person when it can: explicit contacts first, and
/// when there are none, the case manager IS the contact — so the client-facing "who do I talk
/// to" surface can never render empty. Writes use the case-edit gate (case manager, org admin,
/// SuperAdmin), same as editing the case itself.
/// </remarks>
[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/contacts")]
[Authorize]
public sealed class CaseContactController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    public CaseContactController(
        IDbContextFactory<BenDataContext> db,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _security = security; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseContactRecord>>> GetAll(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await MayReadCasesAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await ResolveAsync(db, orgId, caseId, ct));
    }

    /// <summary>Replaces the contact list. An empty list means "fall back to the case manager".</summary>
    [HttpPut]
    public async Task<ActionResult<IEnumerable<CaseContactRecord>>> SetAll(
        Guid orgId, Guid caseId, [FromBody] SetCaseContactsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = await db.Cases.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        var isCaseManager = entity.CaseManagerAppUserId == userId;
        if (!isCaseManager && !await IsOrgAdmin(orgId, ct)) return Forbid();

        // Contacts must be active members of this group — a contact the client cannot actually
        // reach through the platform is a name, not a contact.
        var memberIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.IsActive)
            .Select(m => m.AppUserId).ToListAsync(ct);
        var wanted = request.AppUserIds.Distinct().ToList();
        if (wanted.Any(idm => !memberIds.Contains(idm)))
            return BadRequest("Every contact must be an active member of this group.");

        var current = await db.CaseContacts.Where(c => c.CaseId == caseId).ToListAsync(ct);
        db.CaseContacts.RemoveRange(current.Where(c => !wanted.Contains(c.AppUserId)));
        var now = DateTime.UtcNow;
        for (var i = 0; i < wanted.Count; i++)
        {
            var existing = current.FirstOrDefault(c => c.AppUserId == wanted[i]);
            if (existing is not null) { existing.SortOrder = i; continue; }
            db.CaseContacts.Add(new CaseContact
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = wanted[i], SortOrder = i,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(await ResolveAsync(db, orgId, caseId, ct));
    }

    /// <summary>Explicit contacts, or the case manager as the standing fallback.</summary>
    internal static async Task<List<CaseContactRecord>> ResolveAsync(
        BenDataContext db, Guid orgId, Guid caseId, CancellationToken ct)
    {
        var contacts = await db.CaseContacts.AsNoTracking()
            .Where(c => c.CaseId == caseId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CaseContactRecord(
                c.AppUserId,
                c.AppUser.DisplayName ?? "A member of the team",
                false))
            .ToListAsync(ct);
        if (contacts.Count > 0) return contacts;

        return await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId && c.OrganizationId == orgId && c.CaseManagerAppUserId != null)
            .Select(c => new CaseContactRecord(
                c.CaseManagerAppUser!.Id,
                c.CaseManagerAppUser.DisplayName ?? "The case manager",
                true))
            .ToListAsync(ct);
    }

    /// <summary>Whether the caller may read this group's cases.</summary>
    /// <remarks>
    /// Was bare active membership. Case contacts name the people a client is told to call, which
    /// is case content, so it follows the case grant like every other tab. The write half
    /// (<see cref="SetAll"/>) is left to the case manager or an administrator — a stricter rule
    /// than any grant, and it stays as it is.
    /// </remarks>
    private Task<bool> MayReadCasesAsync(Guid orgId, CancellationToken ct)
        => User.IsInRole(RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId,
                  Ben.Data.Common.Enums.OrganizationPermissionArea.Cases,
                  Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);

    /// <summary>Owner or administrator of this group, or a site administrator.</summary>
    /// <remarks>The fourth hand-written copy of Role &lt;= Administrator; asked of the service now.</remarks>
    private Task<bool> IsOrgAdmin(Guid orgId, CancellationToken ct)
        => User.IsInRole(RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.IsOwnerOrAdminAsync(GetCurrentUserId(), orgId, ct);
}

/// <summary>One person the client can talk to. <c>IsFallback</c> marks the case manager standing
/// in because no explicit contact is set.</summary>
public sealed record CaseContactRecord(Guid AppUserId, string DisplayName, bool IsFallback);

public sealed record SetCaseContactsRequest(IReadOnlyList<Guid> AppUserIds);
