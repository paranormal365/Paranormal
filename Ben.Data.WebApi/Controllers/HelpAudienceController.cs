using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// How much of the help documentation the caller may see.
/// </summary>
/// <remarks>
/// <para>Computed here rather than in the browser because this is the only place that knows both
/// the app-wide roles and the caller's organization memberships — and because
/// <c>OrganizationSummaryResponse</c>, the shape the UI can already fetch, carries no role at all,
/// so the Owner/Administrator distinction is not derivable client-side.</para>
///
/// <para>Returns a single ceiling rather than the membership list it was derived from. The UI only
/// needs to know how far up the ladder the reader stands; shipping the memberships would be
/// handing over more than the question requires.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/help-audience")]
public sealed class HelpAudienceController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public HelpAudienceController(IDbContextFactory<BenDataContext> dbContextFactory)
        => _dbContextFactory = dbContextFactory;

    [HttpGet]
    public async Task<ActionResult<HelpAudience>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Ok(HelpAudience.Everyone);

        // Either app-wide role sees everything. Admin grants nothing else anywhere in the app —
        // see RoleNames.Admin — but the administration documents are precisely what it is for.
        if (User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.Admin))
            return Ok(HelpAudience.AppAdministrator);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var roles = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.Role)
            .ToListAsync(ct);

        if (roles.Count == 0) return Ok(HelpAudience.SignedIn);

        // "Create/own/administer" is Owner and Administrator. A Manager runs cases day to day but
        // does not configure the organization, so they get the member documents.
        var administers = roles.Any(r => r is OrganizationMemberRole.Owner
                                            or OrganizationMemberRole.Administrator);

        return Ok(administers
            ? HelpAudience.OrganizationAdministrator
            : HelpAudience.OrganizationMember);
    }
}
