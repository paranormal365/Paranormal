using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Promotions;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A group's own place in the founding groups promotion, and the group that referred it (11/02/2026).
/// </summary>
/// <remarks>
/// <para><b>Who sees what.</b> Any member sees the group's standing, who counts and why not, and their own score;
/// whoever may change the group's settings claims the spot, names the referrer, and sees every member's score
/// (Ben: "the group admin can see their members' score and the group score").</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}")]
public sealed class FoundingGroupController : OrgCmsControllerBase
{
    public FoundingGroupController(IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    private async Task<bool> MayManageAsync(Guid userId, Guid organizationId, CancellationToken ct)
        => await IsCmsAuthorizedAsync(userId, organizationId, OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private static Task<bool> IsMemberAsync(BenDataContext db, Guid userId, Guid organizationId, CancellationToken ct)
        => db.OrganizationUserMemberships.AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive, ct);

    [HttpGet("founding-group")]
    public async Task<ActionResult<FoundingGroupStatusRecord>> Get(Guid organizationId, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var manage = await MayManageAsync(userId, organizationId, ct);
        if (!manage && !await IsMemberAsync(db, userId, organizationId, ct)) return Forbid();
        return Ok(await DescribeAsync(db, organizationId, userId, manage, ct));
    }

    /// <summary>Takes a spot and starts the group's week.</summary>
    [HttpPost("founding-group/claim")]
    public async Task<ActionResult<FoundingGroupStatusRecord>> Claim(Guid organizationId, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();
        if (!await MayManageAsync(userId, organizationId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var p = await FoundingGroups.GetAsync(db, ct);
        var now = DateTime.UtcNow;
        if (await FoundingGroups.WhyCantClaimAsync(db, p, organizationId, userId, now, ct) is { } why) return BadRequest(why);

        var entry = FoundingGroups.Claim(db, p, organizationId, userId, now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return BadRequest("This group has already taken part."); }

        // Members already in the group count straight away, rather than at the job's next pass.
        await FoundingGroups.CountMembersAsync(db, entry, now, ct);
        return Ok(await DescribeAsync(db, organizationId, userId, true, ct));
    }

    private static async Task<FoundingGroupStatusRecord> DescribeAsync(
        BenDataContext db, Guid organizationId, Guid userId, bool manage, CancellationToken ct)
    {
        var p = await FoundingGroups.GetAsync(db, ct);
        var now = DateTime.UtcNow;
        var name = await db.Organizations.AsNoTracking().Where(o => o.Id == organizationId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "";
        var entry = await db.PromotionEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.PromotionId == p.Id && e.OrganizationId == organizationId, ct);
        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId)
            .Select(s => new { s.CurrentPeriodEnd, s.PriceAtPeriodStart, s.CurrentPeriodStart, Plan = s.SubscriptionTier != null ? s.SubscriptionTier.Name : null })
            .FirstOrDefaultAsync(ct);

        var members = entry is null
            ? []
            : (await FoundingGroups.MembersAsync(db, entry, ct)).Select(m => new FoundingGroupMemberRecord(m.AppUserId, m.Name, m.Counts, m.WhyNot)).ToList();

        string? wouldEarn = null;
        if (entry is { State: PromotionEntryState.Gathering })
        {
            var counted = members.Count(m => m.Counts || m.WhyNot is null);
            if (counted >= p.MinimumMembers) wouldEarn = (await FoundingGroups.TierForAsync(db, organizationId, counted, ct))?.Name;
        }

        FoundingGroupScoreRecord? score = null;
        if (entry is not null && FoundingGroupsScore.Window(entry, now) is { } window)
        {
            var s = await FoundingGroupsScore.ScoreAsync(db, p, organizationId, window.From, window.To, ct);
            score = new FoundingGroupScoreRecord(s.Members, s.Total, s.PerMember,
                FoundingGroupsScore.PerMemberPerMonth(s, window.From, window.To), s.Events, s.Referrals, s.GroupPoints,
                [.. s.People.Where(x => manage || x.AppUserId == userId)
                    .Select(x => new FoundingGroupPersonScoreRecord(x.AppUserId, x.Name, x.Sessions, x.Investigations, x.Evidence, x.Points))]);
        }

        var freeUntil = entry?.State == PromotionEntryState.Qualified && sub is { PriceAtPeriodStart: 0m } && sub.CurrentPeriodStart == entry.QualifiedUtc
            ? sub.CurrentPeriodEnd : null;

        return new FoundingGroupStatusRecord(
            organizationId, name, manage, entry?.State, entry?.GatherEndsUtc, entry?.QualifiedUtc, entry?.FreeYearEndsUtc,
            freeUntil, entry?.State == PromotionEntryState.Qualified ? sub?.Plan : null, wouldEarn, p.MinimumMembers,
            entry is null ? await FoundingGroups.WhyCantClaimAsync(db, p, organizationId, userId, now, ct) : null,
            entry?.DisqualifiedReason, entry?.FinalRank, members, score);
    }

    // ── referrals ─────────────────────────────────────────────────────────────

    [HttpGet("referral")]
    public async Task<ActionResult<GroupReferralRecord>> GetReferral(Guid organizationId, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId, organizationId, OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Read, ct))
            return Forbid();
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        return Ok(await ReferralAsync(db, organizationId, await MayManageAsync(userId, organizationId, ct), ct));
    }

    /// <summary>Names the group that told this one about IsHaunted, or clears it. Fixed once this group has paid.</summary>
    [HttpPut("referral")]
    public async Task<ActionResult<GroupReferralRecord>> SetReferral(
        Guid organizationId, [FromBody] SetGroupReferrerRequest request, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();
        if (!await MayManageAsync(userId, organizationId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return NotFound();

        if (request.ReferredByOrganizationId is { } referrer)
        {
            if (await FreeMonths.WhyCantNameReferrerAsync(db, organizationId, referrer, ct) is { } why) return BadRequest(why);
        }
        else if (await FreeMonths.HasPaidAsync(db, organizationId, ct))
            return BadRequest("This group has already paid for its plan, so the group that referred it can't be changed.");

        org.ReferredByOrganizationId = request.ReferredByOrganizationId;
        org.ReferredAtUtc = request.ReferredByOrganizationId is null ? null : DateTime.UtcNow;
        await SaveAndAuditAsync(db, org, org.Id, userId, ct);
        return Ok(await ReferralAsync(db, organizationId, true, ct));
    }

    /// <summary>Groups whose name matches, to pick a referrer from (never this group).</summary>
    [HttpGet("referral/groups")]
    public async Task<ActionResult<IEnumerable<GroupPickRecord>>> FindGroups(Guid organizationId, [FromQuery] string? q, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();
        if (!await MayManageAsync(userId, organizationId, ct)) return Forbid();
        var text = (q ?? "").Trim();
        if (text.Length < 2) return Ok(Array.Empty<GroupPickRecord>());

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        return Ok(await db.Organizations.AsNoTracking()
            .Where(o => o.Id != organizationId && o.Name.Contains(text))
            .OrderBy(o => o.Name).Take(10)
            .Select(o => new GroupPickRecord(o.Id, o.Name, o.UrlName))
            .ToListAsync(ct));
    }

    private static async Task<GroupReferralRecord> ReferralAsync(BenDataContext db, Guid organizationId, bool manage, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new
            {
                o.ReferredByOrganizationId,
                ReferrerName = db.Organizations.Where(r => r.Id == o.ReferredByOrganizationId).Select(r => r.Name).FirstOrDefault(),
            })
            .FirstAsync(ct);
        var months = await db.OrganizationFreeMonths.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderByDescending(m => m.EarnedUtc)
            .Select(m => new FreeMonthsRecord(m.Id, m.Reason, m.Months, m.EarnedUtc, m.AppliedUtc, m.AppliedToPeriodEndUtc, m.RevokedUtc,
                m.SourceOrganization != null ? m.SourceOrganization.Name : null, m.Note))
            .ToListAsync(ct);
        return new GroupReferralRecord(org.ReferredByOrganizationId, org.ReferrerName,
            await FreeMonths.HasPaidAsync(db, organizationId, ct), manage, months);
    }
}
