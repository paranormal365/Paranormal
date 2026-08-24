using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The moderation queue: reported feed posts, and what an administrator decided about them.
/// </summary>
/// <remarks>
/// <para><b>Reports do not hide anything, and no number of them does.</b> Hiding is an act with a
/// name attached to it, taken here. A pile-on threshold would moderate whoever is least popular
/// rather than whatever breaks the rules, which is the failure mode of every automatic system of
/// this shape.</para>
///
/// <para><b>Hidden, not deleted.</b> A deleted post takes its replies, its reports and the record
/// of the decision with it, so the next administrator asking "what happened here" finds nothing.
/// Hiding removes it from every feed query and keeps all of that.</para>
///
/// <para>Resolved reports are kept too. A dismissed report is the evidence that somebody looked,
/// and the pattern across reports — one author reported repeatedly, or one reporter reporting
/// everybody — is exactly what an administrator needs and what deleting them would destroy.</para>
///
/// <para><b>Not gated on <c>features.public-feed</c>,</b> unlike every reader-facing feed route.
/// Switching the feed off does not un-report anything: a site that turns it off may still have
/// complaints nobody got to, and stranding them behind the switch would leave the only record of
/// them unreachable. The flag governs the feature; this is the record of decisions about it.</para>
/// </remarks>
[ApiController]
[Route("api/admin/feed")]
// Item 186 F5: widened from SuperAdmin to the moderation policy, which a SuperAdmin satisfies
// implicitly. Deciding a report IS the moderator's job — leaving this behind the administrator
// role would have meant the new role could see the media queue and not the complaints.
[Authorize(Policy = AuthPolicyNames.Moderator)]
public sealed class AdminFeedController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminFeedController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>
    /// The queue, oldest first.
    /// </summary>
    /// <param name="outcome">
    /// Which decisions to show. Omit for everything still <see cref="FeedReportOutcome.Pending"/>,
    /// which is the queue as such.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// Oldest first on purpose: a moderation queue worked newest-first leaves the oldest complaint
    /// unanswered for ever, and that is the one somebody is waiting on.
    /// </remarks>
    [HttpGet("reports")]
    public async Task<ActionResult<IReadOnlyList<FeedReportRecord>>> GetReports(
        [FromQuery] FeedReportOutcome? outcome, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var wanted = outcome ?? FeedReportOutcome.Pending;

        // Counted once for the whole page rather than per row: "how many people reported this" is
        // context an administrator wants on every line, and a per-row query is the N+1 that makes a
        // queue slow exactly as it gets long enough to matter.
        var reports = await db.OrgMessageReports.AsNoTracking()
            .Where(r => r.Outcome == wanted)
            .OrderBy(r => r.DateCreated)
            .Select(r => new
            {
                r.Id,
                r.OrgMessageId,
                PostBody = r.OrgMessage.Body,
                PostAuthorId = r.OrgMessage.AuthorAppUserId,
                PostAuthorName = r.OrgMessage.AuthorAppUser.DisplayName ?? r.OrgMessage.AuthorAppUser.Email,
                PostDateCreated = r.OrgMessage.DateCreated,
                PostHidden = r.OrgMessage.HiddenUtc != null,
                r.ReportedByAppUserId,
                ReportedByName = r.ReportedByAppUser.DisplayName ?? r.ReportedByAppUser.Email,
                r.Reason,
                r.Outcome,
                r.DateCreated,
                r.ResolvedUtc,
                ResolvedByName = r.ResolvedByAppUser != null
                    ? (r.ResolvedByAppUser.DisplayName ?? r.ResolvedByAppUser.Email)
                    : null,
            })
            .ToListAsync(ct);

        var postIds = reports.Select(r => r.OrgMessageId).Distinct().ToList();
        var countsPerPost = (await db.OrgMessageReports.AsNoTracking()
            .Where(r => postIds.Contains(r.OrgMessageId))
            .GroupBy(r => r.OrgMessageId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.PostId, x => x.Count);

        return Ok(reports.Select(r => new FeedReportRecord(
            r.Id, r.OrgMessageId, r.PostBody, r.PostAuthorId, r.PostAuthorName ?? "Unknown",
            r.PostDateCreated, r.PostHidden, r.ReportedByAppUserId, r.ReportedByName ?? "Unknown",
            r.Reason, r.Outcome, r.DateCreated, r.ResolvedUtc, r.ResolvedByName,
            countsPerPost.GetValueOrDefault(r.OrgMessageId))).ToList());
    }

    /// <summary>
    /// Decides a report: dismiss it, or hide the post.
    /// </summary>
    /// <remarks>
    /// <para><b>Every pending report against the same post is resolved together.</b> Five people
    /// reporting one post is one decision, not five, and leaving the other four pending would put
    /// the same post back in the queue for a colleague to judge again — with no way of telling it
    /// had already been dealt with.</para>
    ///
    /// <para>Hiding is idempotent and records who did it. Un-hiding happens by dismissing a report
    /// against a hidden post, which is the same act read the other way round.</para>
    /// </remarks>
    [HttpPost("reports/{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(
        Guid id, [FromBody] ResolveFeedReportRequest request, CancellationToken ct)
    {
        if (request.Outcome is not (FeedReportOutcome.Dismissed or FeedReportOutcome.Hidden))
            return BadRequest("A report is either dismissed or upheld by hiding the post.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var report = await db.OrgMessageReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null) return NotFound();

        var post = await db.OrgMessages.FirstOrDefaultAsync(m => m.Id == report.OrgMessageId, ct);
        if (post is null) return NotFound();

        var now = DateTime.UtcNow;

        // The post first, then every pending report against it — including this one.
        if (request.Outcome == FeedReportOutcome.Hidden)
        {
            post.HiddenUtc ??= now;
            post.HiddenByAppUserId ??= userId;
        }
        else
        {
            // Dismissing a report against a hidden post is how a post comes back.
            post.HiddenUtc = null;
            post.HiddenByAppUserId = null;
        }

        var siblings = await db.OrgMessageReports
            .Where(r => r.OrgMessageId == report.OrgMessageId && r.Outcome == FeedReportOutcome.Pending)
            .ToListAsync(ct);

        foreach (var sibling in siblings)
        {
            sibling.Outcome = request.Outcome;
            sibling.ResolvedUtc = now;
            sibling.ResolvedByAppUserId = userId;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
