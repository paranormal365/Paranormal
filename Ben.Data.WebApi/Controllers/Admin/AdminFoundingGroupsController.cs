using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Promotions;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The founding groups promotion for a SuperAdmin (11/02/2026): its numbers, opening it, every entry, and free
/// months given or taken back.
/// </summary>
/// <remarks>
/// Opening it the first time starts the claim window; switching it off hides it everywhere without forgetting
/// anything. A rule changed after groups have entered applies from then on, as the rules page says.
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/founding-groups")]
public sealed class AdminFoundingGroupsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public AdminFoundingGroupsController(IDbContextFactory<BenDataContext> dbFactory) { _dbFactory = dbFactory; }

    [HttpGet]
    public async Task<ActionResult<AdminFoundingGroupsRecord>> Get(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await DescribeAsync(db, ct));
    }

    [HttpPut]
    public async Task<ActionResult<AdminFoundingGroupsRecord>> Save([FromBody] FoundingGroupsSettingsRecord s, CancellationToken ct)
    {
        if (new[] { s.ClaimWindowDays, s.Spots, s.GatherDays, s.MinimumMembers, s.FreeMonths }.Any(n => n < 1))
            return BadRequest("Days, spots, members and free months must each be at least 1.");
        if (new[] { s.Winners, s.WinnerMonths, s.GroupOfTheMonthMonths, s.ReachedScoreMonths, s.ReachableScorePerMember,
                    s.PointsPerSession, s.PointsPerInvestigation, s.PointsPerEvent, s.PointsPerEvidence, s.PointsPerReferral,
                    s.MinimumReadingsPerSession }.Any(n => n < 0))
            return BadRequest("None of the numbers can be below zero.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var p = await FoundingGroups.GetAsync(db, ct);
        var userId = GetCurrentUserId();
        p.IsOpen = s.IsOpen;
        if (s.IsOpen && p.OpenedUtc is null) p.OpenedUtc = DateTime.UtcNow;
        p.ClaimWindowDays = s.ClaimWindowDays; p.Spots = s.Spots; p.GatherDays = s.GatherDays; p.MinimumMembers = s.MinimumMembers;
        p.FreeMonths = s.FreeMonths; p.Winners = s.Winners; p.WinnerMonths = s.WinnerMonths;
        p.GroupOfTheMonthMonths = s.GroupOfTheMonthMonths; p.ReachedScoreMonths = s.ReachedScoreMonths;
        p.ReachableScorePerMember = s.ReachableScorePerMember;
        p.PointsPerSession = s.PointsPerSession; p.PointsPerInvestigation = s.PointsPerInvestigation;
        p.PointsPerEvent = s.PointsPerEvent; p.PointsPerEvidence = s.PointsPerEvidence; p.PointsPerReferral = s.PointsPerReferral;
        p.MinimumReadingsPerSession = s.MinimumReadingsPerSession;
        p.DateUpdated = DateTime.UtcNow;
        p.UpdatedByAppUserId = userId;
        await SaveAndAuditAsync(db, p, p.Id, userId, ct);
        return Ok(await DescribeAsync(db, ct));
    }

    /// <summary>Takes a group out of the promotion with a reason it is told. Its free plan runs to its current end.</summary>
    [HttpPost("entries/{entryId:guid}/disqualify")]
    public async Task<ActionResult<AdminFoundingGroupsRecord>> Disqualify(
        Guid entryId, [FromBody] DisqualifyFoundingGroupRequest request,
        [FromServices] Services.PlatformMessageService messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest("Say why, so the group can be told.");
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entry = await db.PromotionEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct);
        if (entry is null) return NotFound();
        if (entry.State is PromotionEntryState.Disqualified or PromotionEntryState.Released)
            return BadRequest("This group isn't in the promotion any more.");

        entry.State = PromotionEntryState.Disqualified;
        entry.DisqualifiedReason = request.Reason.Trim();
        entry.DisqualifiedUtc = DateTime.UtcNow;
        entry.DateUpdated = entry.DisqualifiedUtc;
        entry.UpdatedByAppUserId = GetCurrentUserId();
        // Its members are free to be counted for another group.
        db.PromotionMembers.RemoveRange(await db.PromotionMembers.Where(m => m.PromotionEntryId == entryId).ToListAsync(ct));
        await SaveAndAuditAsync(db, entry, entry.Id, GetCurrentUserId(), ct);

        var to = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == entry.OrganizationId && m.IsActive
                     && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator))
            .Select(m => m.AppUserId).ToListAsync(ct);
        if (to.Count > 0)
            await messages.SendAsync("Your group has been taken out of the founding groups promotion",
                $"Your group is no longer one of the founding groups. The reason: {entry.DisqualifiedReason} "
                + "Its plan stays as it is until its current period ends. If you think this is a mistake, reply to this message.",
                to, GetCurrentUserId(), ct, alsoPost: Ben.Data.Common.Mail.MailKinds.FoundingGroups);
        return Ok(await DescribeAsync(db, ct));
    }

    /// <summary>Gives a group free months by hand, with a note it sees.</summary>
    [HttpPost("free-months")]
    public async Task<ActionResult<AdminFoundingGroupsRecord>> Give([FromBody] GiveFreeMonthsRequest request, CancellationToken ct)
    {
        if (request.Months is < 1 or > 24) return BadRequest("Give between 1 and 24 months.");
        if (string.IsNullOrWhiteSpace(request.Note)) return BadRequest("Add a note, so the group knows why.");
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == request.OrganizationId, ct)) return NotFound();

        var now = DateTime.UtcNow;
        await FreeMonths.GiveAsync(db, request.OrganizationId, FreeMonthReason.Given, request.Months,
            $"given:{Guid.NewGuid():N}", now, GetCurrentUserId(), ct, note: request.Note.Trim());
        await db.SaveChangesAsync(ct);
        return Ok(await DescribeAsync(db, ct));
    }

    /// <summary>Takes back months not yet added to a plan.</summary>
    [HttpDelete("free-months/{id:guid}")]
    public async Task<ActionResult<AdminFoundingGroupsRecord>> Revoke(Guid id, [FromQuery] string? reason, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var months = await db.OrganizationFreeMonths.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (months is null) return NotFound();
        if (months.AppliedUtc is not null) return BadRequest("Those months are already on the group's plan, so they can't be taken back here.");
        if (months.RevokedUtc is not null) return BadRequest("Those months were already taken back.");

        months.RevokedUtc = DateTime.UtcNow;
        months.RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        months.DateUpdated = months.RevokedUtc;
        months.UpdatedByAppUserId = GetCurrentUserId();
        await SaveAndAuditAsync(db, months, months.Id, GetCurrentUserId(), ct);
        return Ok(await DescribeAsync(db, ct));
    }

    private static async Task<AdminFoundingGroupsRecord> DescribeAsync(BenDataContext db, CancellationToken ct)
    {
        var p = await FoundingGroups.GetAsync(db, ct);
        var now = DateTime.UtcNow;
        var entries = await db.PromotionEntries.AsNoTracking().Where(e => e.PromotionId == p.Id)
            .OrderBy(e => e.ClaimedUtc)
            .Select(e => new
            {
                Entry = e, e.Organization.Name,
                Counted = e.Members.Count,
                Plan = db.OrganizationSubscriptions.Where(s => s.OrganizationId == e.OrganizationId).Select(s => s.SubscriptionTier!.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var rows = new List<AdminFoundingGroupEntryRecord>();
        foreach (var x in entries)
        {
            decimal? soFar = null;
            if (FoundingGroupsScore.Window(x.Entry, now) is { } w)
                soFar = (await FoundingGroupsScore.ScoreAsync(db, p, x.Entry.OrganizationId, w.From, w.To, ct)).PerMember;
            rows.Add(new AdminFoundingGroupEntryRecord(x.Entry.Id, x.Entry.OrganizationId, x.Name, x.Entry.State, x.Entry.ClaimedUtc,
                x.Entry.GatherEndsUtc, x.Entry.QualifiedUtc, x.Entry.FreeYearEndsUtc, x.Counted,
                x.Entry.State == PromotionEntryState.Qualified ? x.Plan : null,
                soFar, x.Entry.FinalScorePerMember, x.Entry.FinalRank, x.Entry.DisqualifiedReason));
        }

        var months = await db.OrganizationFreeMonths.AsNoTracking()
            .OrderByDescending(m => m.EarnedUtc).Take(200)
            .Select(m => new AdminFreeMonthsRecord(m.Id, m.OrganizationId, m.Organization.Name, m.Reason, m.Months, m.EarnedUtc,
                m.AppliedUtc, m.RevokedUtc, m.SourceOrganization != null ? m.SourceOrganization.Name : null, m.Note))
            .ToListAsync(ct);

        return new AdminFoundingGroupsRecord(
            new FoundingGroupsSettingsRecord(p.IsOpen, p.ClaimWindowDays, p.Spots, p.GatherDays, p.MinimumMembers, p.FreeMonths,
                p.Winners, p.WinnerMonths, p.GroupOfTheMonthMonths, p.ReachedScoreMonths, p.ReachableScorePerMember,
                p.PointsPerSession, p.PointsPerInvestigation, p.PointsPerEvent, p.PointsPerEvidence, p.PointsPerReferral,
                p.MinimumReadingsPerSession),
            p.OpenedUtc, FoundingGroups.ClaimsCloseUtc(p), p.DecidedUtc,
            await FoundingGroups.SpotsTakenAsync(db, p, ct), rows, months);
    }
}
