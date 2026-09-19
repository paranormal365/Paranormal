using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Which parts of the app apply to this person at all.
/// </summary>
/// <param name="HasGroups">They belong to at least one group.</param>
/// <param name="AdministersAGroup">Owner or Administrator somewhere — the settings-shaped screens.</param>
/// <param name="HasCases">
/// They can reach at least one case: as the client who asked for it, as somebody granted access to
/// it, or through a group. Not "may create one" — a screen listing nothing is the thing being
/// avoided.
/// </param>
/// <param name="HasInvestigations">They are on a group's investigation, or their group has one.</param>
/// <param name="AttendsPublicEvents">
/// They have a confirmed attendance at a public event — the ghost-walk guest. Past ones count:
/// evidence and photographs outlive the walk, and the night after is exactly when somebody opens
/// the app.
/// </param>
/// <param name="HasOwnFieldSessions">They have recorded something on their own account.</param>
public sealed record MeSurfaces(
    bool HasGroups,
    bool AdministersAGroup,
    bool HasCases,
    bool HasInvestigations,
    bool AttendsPublicEvents,
    bool HasOwnFieldSessions);

/// <summary>
/// What to offer this person, decided once on the server.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (Ben, 2026-08-31): "if they are alone and investigating alone,
/// they should not see things that should be available to people who log in and are members of
/// groups". The app's tabs were fixed for everybody, so a solo investigator carried a My Cases tab
/// that could never hold anything and an Investigations tab belonging to groups they had not
/// joined. Empty screens are not neutral — they read as a broken app, or as a feature the person
/// is failing to find.</para>
///
/// <para><b>Answers "is there anything here?", not "may you do this?".</b> Permission is already
/// decided at each endpoint and must stay there; this is about whether a door leads anywhere
/// today. The distinction matters because the answers differ: somebody may be perfectly entitled
/// to create a case and still have none, and showing them an empty list is the wrong way to say
/// so.</para>
///
/// <para><b>Beside <c>HelpAudienceController</c>, not instead of it.</b> That returns a single
/// ceiling on a ladder — Everyone through AppAdministrator — which is right for documentation and
/// cannot express what is asked here: attending a ghost walk is not a rung above being a group
/// member, it is a different axis entirely. A person can be a solo investigator, somebody's
/// client, and a tour guest at once.</para>
///
/// <para><b>Counts, never contents.</b> Every field is a boolean derived from an existence check.
/// Shipping the lists would hand a navigation question far more than it asked, and every one of
/// those lists already has its own gated endpoint.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/surfaces")]
public sealed class MeSurfacesController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MeSurfacesController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<MeSurfaces>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var memberships = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync(ct);

        var orgIds = memberships.Select(m => m.OrganizationId).ToList();

        // An app-wide administrator is shown everything: their job is the parts of the product
        // other people cannot see, and hiding a section because their own account happens to be
        // empty would hide the thing they came to look at.
        var appAdmin = User.IsInRole(RoleNames.SuperAdmin) || User.IsInRole(RoleNames.Admin);

        var hasCases = appAdmin
            || await db.Cases.AsNoTracking().AnyAsync(c => orgIds.Contains(c.OrganizationId), ct)
            // The client's own two routes in, which owe nothing to membership: they asked for the
            // investigation, or somebody granted them access to the case.
            || await db.Cases.AsNoTracking()
                .AnyAsync(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct)
            || await db.CaseClientAccesses.AsNoTracking().AnyAsync(a => a.AppUserId == userId, ct);

        var hasInvestigations = appAdmin
            || await db.Investigations.AsNoTracking()
                .AnyAsync(i => orgIds.Contains(i.OrganizationId), ct)
            || await db.InvestigationAttendees.AsNoTracking()
                .AnyAsync(a => a.AppUserId == userId, ct);

        return Ok(new MeSurfaces(
            HasGroups: memberships.Count > 0,
            AdministersAGroup: appAdmin || memberships.Any(m =>
                m.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Administrator),
            HasCases: hasCases,
            HasInvestigations: hasInvestigations,
            AttendsPublicEvents: await db.EventAttendanceInvites.AsNoTracking()
                .AnyAsync(i => i.ConfirmedByAppUserId == userId, ct),
            HasOwnFieldSessions: await db.FieldSessionUploads.AsNoTracking()
                .AnyAsync(s => s.SubmittedByAppUserId == userId, ct)));
    }
}
