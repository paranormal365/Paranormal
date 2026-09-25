using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The link a group shares to bring its own people in.
/// </summary>
/// <remarks>
/// <para><b>The gap this closes.</b> A membership was only ever created four ways on this site —
/// founding a group, an accepted application, the solo plan, and the seeders — and applications
/// can only be opened on a paid plan. So somebody who had just founded a group stood on a Members
/// screen with one row, no applications, and nothing at all that added a person, while the group
/// hub's own guided tour told them the tab was for inviting people. The first-run walk found it
/// on 2026-09-20 by trying to do the thing the product is for.</para>
///
/// <para><b>Issuing is not admitting.</b> Making a link changes nobody's membership; walking
/// through it does, and that is where the plan is checked — see
/// <see cref="Public.PublicOrganizationJoinController"/>. A link made today and used after the
/// group subscribes must work, so refusing to ISSUE one on a free plan would be the wrong rule in
/// the wrong place. What this endpoint does instead is hand back the plan's own sentence alongside
/// the link, so the screen can say what will happen before anybody shares it — item 193's rule
/// that a knowable refusal is said beside the control rather than after the attempt.</para>
///
/// <para><b>One live link per group.</b> Issuing again replaces the old secret, which is also how
/// it is revoked: there is never a second live link to forget about. It expires in a fortnight,
/// the same life the event-staff invitation has, so the site has one answer to how long an
/// invitation lasts.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/join-link")]
[Authorize]
public sealed class OrganizationJoinLinkController : ControllerBase
{
    /// <summary>A fortnight, matching <c>HostedEventStaffInvite</c>.</summary>
    private static readonly TimeSpan Life = TimeSpan.FromDays(14);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IOrganizationSecurityService _security;

    public OrganizationJoinLinkController(
        IDbContextFactory<BenDataContext> dbFactory,
        IOrganizationSecurityService security)
    { _dbFactory = dbFactory; _security = security; }

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst("app_user_id")?.Value;
        if (claim is not null && Guid.TryParse(claim, out var id)) return id;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return sub is not null && Guid.TryParse(sub, out var id2) ? id2 : null;
    }

    /// <summary>
    /// Whether this person may hand out the keys to the group.
    /// </summary>
    /// <remarks>
    /// The same permission that decides an application — a link is an acceptance made in advance,
    /// so anybody who could let somebody in by pressing Accept can let them in by sharing this,
    /// and nobody else can.
    /// </remarks>
    private async Task<bool> MayInviteAsync(Guid userId, Guid orgId, CancellationToken ct)
        => User.IsInRole(RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(userId, orgId,
               OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Update, ct);

    /// <summary>The live link, if there is one, and what the plan will say when somebody uses it.</summary>
    [HttpGet]
    public async Task<ActionResult<OrganizationJoinLinkRecord>> Get(Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayInviteAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound("Organization not found.");

        return Ok(await DescribeAsync(db, org.Id, org.JoinToken, org.JoinTokenExpiresUtc, ct));
    }

    /// <summary>Makes a link, replacing any that came before it.</summary>
    [HttpPost]
    public async Task<ActionResult<OrganizationJoinLinkRecord>> Issue(Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayInviteAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound("Organization not found.");

        // 32 bytes of randomness, url-safe. Long enough that the link is the secret and guessing is
        // not a strategy; short enough to paste into a message without wrapping.
        org.JoinToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        org.JoinTokenExpiresUtc = DateTime.UtcNow.Add(Life);
        org.JoinTokenCreatedByAppUserId = userId.Value;
        org.DateUpdated = DateTime.UtcNow;
        org.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        return Ok(await DescribeAsync(db, org.Id, org.JoinToken, org.JoinTokenExpiresUtc, ct));
    }

    /// <summary>Stops the link working, for everybody who has it.</summary>
    [HttpDelete]
    public async Task<ActionResult<OrganizationJoinLinkRecord>> Revoke(Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayInviteAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound("Organization not found.");

        org.JoinToken = null;
        org.JoinTokenExpiresUtc = null;
        org.JoinTokenCreatedByAppUserId = null;
        org.DateUpdated = DateTime.UtcNow;
        org.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        return Ok(await DescribeAsync(db, org.Id, null, null, ct));
    }

    private static async Task<OrganizationJoinLinkRecord> DescribeAsync(
        BenDataContext db, Guid orgId, string? token, DateTime? expires, CancellationToken ct)
        => new()
        {
            OrganizationId = orgId,
            Token          = token,
            ExpiresUtc     = expires,
            // Said here so the screen can warn BEFORE the link is shared, rather than letting
            // somebody paste it into a group chat and discover the price when a friend clicks it.
            PlanRefusal    = await Services.Billing.PaidPlan.WhyCannotAddMemberAsync(db, orgId, ct),
        };
}
