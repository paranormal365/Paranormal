using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Feed posts written by the seeded accounts — the fixture people the e2e suite signs in as.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Development and production share one database, so a Playwright
/// run leaves its posts on the live feed. On 2026-09-02 the first page of ishaunted.com's feed was
/// 184 lines of "Playback check" and "e2e post", with the four curated posts the App Store
/// screenshots show buried at ranks 196–199. Nothing could take them down: the only route to
/// hiding a post was a member reporting it and a moderator upholding the report, one at a time.</para>
///
/// <para><b>What counts as a test post</b> is a fact about the author, not a guess about the text:
/// the account's email is on a domain only the seeder uses (<c>benco.dev</c>, or <c>example.com</c>,
/// which is reserved and can belong to nobody). Matching on words like "test" would eventually
/// hide a real person's post that happened to say it. The four curated posts are by the same
/// accounts, so they appear on this list too — which is why the list has checkboxes rather than a
/// single "hide them all" button.</para>
///
/// <para><b>Hidden, not deleted.</b> The feed has no delete; hiding is what a moderator does and it
/// is reversible from this same page. A hidden post drops off every feed query at once because
/// they already filter on <c>HiddenUtc</c>.</para>
/// </remarks>
[ApiController]
[Route("api/admin/feed/test-posts")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminTestPostController : BenControllerBase
{
    /// <summary>Upper-cased to match <c>NormalizedEmail</c>, which is what Identity indexes.</summary>
    internal static readonly string[] SeededDomains = ["@BENCO.DEV", "@EXAMPLE.COM"];

    private const int ListCap = 1000;

    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminTestPostController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Every public-feed post by a seeded account, newest first, hidden or not.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TestFeedPostRecord>>> List(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = await SeededPosts(db)
            .OrderByDescending(m => m.DateCreated)
            .Take(ListCap)
            .Select(m => new TestFeedPostRecord(
                m.Id,
                AuthorName(m.AuthorAppUser),
                m.AuthorAppUser.Email ?? "",
                m.DateCreated,
                m.Body,
                m.ParentMessageId != null,
                m.HiddenUtc != null,
                m.Replies.Count(r => r.HiddenUtc == null)))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Hide the chosen posts. A top-level post takes its visible replies with it.</summary>
    [HttpPost("hide")]
    public Task<ActionResult<TestFeedPostHideResult>> Hide(
        [FromBody] TestFeedPostIdsRequest request, CancellationToken ct)
        => Apply(request, hide: true, ct);

    /// <summary>Put the chosen posts back. Only the ids given; replies are their own rows.</summary>
    [HttpPost("unhide")]
    public Task<ActionResult<TestFeedPostHideResult>> Unhide(
        [FromBody] TestFeedPostIdsRequest request, CancellationToken ct)
        => Apply(request, hide: false, ct);

    private async Task<ActionResult<TestFeedPostHideResult>> Apply(
        TestFeedPostIdsRequest request, bool hide, CancellationToken ct)
    {
        if (request.Ids is not { Count: > 0 })
            return BadRequest("Choose at least one post.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var ids = request.Ids.Distinct().ToList();
        var posts = await SeededPosts(db)
            .Where(m => ids.Contains(m.Id))
            .Include(m => m.Replies)
            .ToListAsync(ct);

        // Every id must be a seeded account's post. This door hides posts with no report and no
        // second pair of eyes, so an id that belongs to anybody else refuses the whole batch
        // rather than quietly doing the part it could — the caller's list is stale or wrong,
        // and either way they should look again before anything changes.
        if (posts.Count != ids.Count)
            return Conflict(new TestFeedPostHideResult(0, 0,
                $"{ids.Count - posts.Count} of the chosen posts are not by a seeded account, or no longer exist. "
                + "Nothing was changed — look again and choose from the current list."));

        var now = DateTime.UtcNow;
        var changed = 0;
        var repliesAlso = 0;

        foreach (var post in posts)
        {
            if (hide)
            {
                if (post.HiddenUtc is null) { post.HiddenUtc = now; post.HiddenByAppUserId = userId; changed++; }
                foreach (var reply in post.Replies.Where(r => r.HiddenUtc == null && !ids.Contains(r.Id)))
                {
                    reply.HiddenUtc = now; reply.HiddenByAppUserId = userId; repliesAlso++;
                }
            }
            else if (post.HiddenUtc is not null)
            {
                post.HiddenUtc = null; post.HiddenByAppUserId = null; changed++;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new TestFeedPostHideResult(changed, repliesAlso, null));
    }

    private static IQueryable<OrgMessage> SeededPosts(BenDataContext db)
        => db.OrgMessages
            .Where(m => m.ChannelType == OrgMessageChannel.PublicFeed
                     && m.AuthorAppUser.NormalizedEmail != null
                     && (m.AuthorAppUser.NormalizedEmail.EndsWith(SeededDomains[0])
                      || m.AuthorAppUser.NormalizedEmail.EndsWith(SeededDomains[1])));

    private static string AuthorName(AppUser a)
        => !string.IsNullOrWhiteSpace(a.DisplayName) ? a.DisplayName
         : !string.IsNullOrWhiteSpace(a.FirstName) ? $"{a.FirstName} {a.LastName}".Trim()
         : a.UserName ?? "Unknown";
}

public sealed record TestFeedPostIdsRequest(IReadOnlyList<Guid> Ids);

public sealed record TestFeedPostRecord(
    Guid Id, string AuthorName, string AuthorEmail, DateTime DateCreated, string Body,
    bool IsReply, bool Hidden, int VisibleReplies);

/// <param name="Changed">Posts whose state actually flipped; an already-hidden post is not counted.</param>
/// <param name="RepliesAlso">Replies hidden alongside their parent, beyond the ids given.</param>
public sealed record TestFeedPostHideResult(int Changed, int RepliesAlso, string? Refusal);
