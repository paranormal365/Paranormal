using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Verifies that a <c>caseId</c> route parameter actually belongs to the <c>orgId</c> route
/// parameter it's nested under.
/// </summary>
/// <remarks>
/// A recurring bug shape across this app: an action checks <c>IsOrgMemberAsync(routeOrgId)</c>
/// (is the caller a member of the org named in the route), then queries the target resource
/// filtered only by <c>caseId</c> — without ever confirming that <c>caseId</c>'s real
/// <c>Case.OrganizationId</c> actually equals <c>routeOrgId</c>. A legitimate member of their own
/// org can then supply their own <c>orgId</c> (to pass the membership check) alongside any other
/// org's <c>caseId</c> they know or can guess, and reach that org's data. <c>CaseReportController.Create</c>
/// shows the correct check already in use for one action; this helper is that same check, shared
/// so every affected action can call it identically.
/// </remarks>
public static class CaseOrgAccess
{
    /// <summary>True if <paramref name="caseId"/> exists and its <c>OrganizationId</c> equals
    /// <paramref name="organizationId"/>. Callers should treat a <c>false</c> result as
    /// <c>NotFound()</c> — the same response a case in a different (and thus invisible) org would
    /// otherwise deserve — rather than <c>Forbid()</c>, so this doesn't confirm to a prober
    /// whether the guessed caseId exists at all.</summary>
    public static async Task<bool> CaseBelongsToOrgAsync(
        BenDataContext db, Guid caseId, Guid organizationId, CancellationToken ct)
    {
        return await db.Cases.AsNoTracking()
            .AnyAsync(c => c.Id == caseId && c.OrganizationId == organizationId, ct);
    }
}
