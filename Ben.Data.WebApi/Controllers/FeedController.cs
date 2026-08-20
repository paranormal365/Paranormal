using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The public feed: short posts by any signed-in person, with follows, mentions and tags.
/// </summary>
/// <remarks>
/// <para><b>Every route here 404s wholesale when <c>features.public-feed</c> is off</b>, which it
/// is by default. Not 403 — a disabled feature should not be discoverable by the shape of its
/// refusal, and "this does not exist here" is the truthful answer for a site whose administrator
/// has not turned the feed on.</para>
///
/// <para><b>Posts are by any signed-in person</b>, which was Ben's decision and is what makes
/// moderation part of the feature rather than an optional extra. Reports never hide anything by
/// themselves: hiding is an administrator's act, so a group who dislike a post cannot remove it
/// between them.</para>
///
/// <para><b>Storage reuses <c>OrgMessage</c></b> with <c>ChannelType.PublicFeed</c>. That table was
/// built with a nullable OrganizationId and parent-based threading, which is exactly a feed post
/// and its replies. A second near-identical table would have meant two places to fix every time
/// the way a message is written changes.</para>
/// </remarks>
[ApiController]
[Route("api/feed")]
[Authorize]
public sealed class FeedController : BenControllerBase
{
    /// <summary>Posts per page. Also the cap a caller may ask for.</summary>
    private const int PageSize = 25;

    /// <summary>Longest post. Short-form is the point; a wall of text belongs in a publication.</summary>
    public const int MaxBodyLength = 1000;

    private readonly IDbContextFactory<BenDataContext> _db;

    public FeedController(IDbContextFactory<BenDataContext> db) => _db = db;

    // ── Reading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A page of the feed, newest first.
    /// </summary>
    /// <param name="mode">
    /// <c>all</c> — everybody's posts. <c>following</c> — only people the reader follows, plus
    /// their own. Anything else reads as <c>all</c>.
    /// </param>
    /// <param name="hashtag">Narrow to one tag. Combines with <paramref name="mode"/>.</param>
    /// <param name="cursor">From a previous page's <c>NextCursor</c>. Opaque.</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet]
    public async Task<ActionResult<FeedPageRecord>> GetFeed(
        [FromQuery] string? mode, [FromQuery] string? hashtag, [FromQuery] string? cursor,
        CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var query = VisiblePosts(db);

        if (string.Equals(mode, "following", StringComparison.OrdinalIgnoreCase))
        {
            // Own posts included. A feed of people you follow that does not contain the thing you
            // just wrote reads as a bug every single time.
            var followed = db.UserFollows.AsNoTracking()
                .Where(f => f.FollowerAppUserId == userId)
                .Select(f => f.FollowedAppUserId);

            query = query.Where(m => m.AuthorAppUserId == userId || followed.Contains(m.AuthorAppUserId));
        }

        if (!string.IsNullOrWhiteSpace(hashtag))
        {
            var tag = hashtag.TrimStart('#').ToLowerInvariant();
            query = query.Where(m => m.Hashtags.Any(h => h.Tag == tag));
        }

        // Top-level only. Replies are read with the post they answer.
        query = query.Where(m => m.ParentMessageId == null);

        if (TryReadCursor(cursor, out var beforeUtc, out var beforeId))
        {
            // Composite so a page boundary in the middle of two posts sharing a timestamp neither
            // repeats one nor skips one — the failure a plain "older than this date" cursor has.
            query = query.Where(m => m.DateCreated < beforeUtc
                                  || (m.DateCreated == beforeUtc && m.Id.CompareTo(beforeId) < 0));
        }

        var page = await query
            .OrderByDescending(m => m.DateCreated).ThenByDescending(m => m.Id)
            .Take(PageSize + 1)          // one extra, purely to know whether there is a next page
            .ToListAsync(ct);

        var hasMore = page.Count > PageSize;
        if (hasMore) page.RemoveAt(page.Count - 1);

        var posts = await ToRecordsAsync(db, page, userId, ct);
        var next = hasMore && page.Count > 0 ? WriteCursor(page[^1].DateCreated, page[^1].Id) : null;

        return Ok(new FeedPageRecord(posts, next));
    }

    /// <summary>One post and its replies, oldest reply first.</summary>
    [HttpGet("posts/{id:guid}")]
    public async Task<ActionResult<IReadOnlyList<FeedPostRecord>>> GetThread(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var root = await VisiblePosts(db).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (root is null) return NotFound();

        var replies = await VisiblePosts(db)
            .Where(m => m.ParentMessageId == id)
            .OrderBy(m => m.DateCreated).ThenBy(m => m.Id)
            .ToListAsync(ct);

        return Ok(await ToRecordsAsync(db, [root, .. replies], userId, ct));
    }

    /// <summary>Somebody's feed profile — their counts, and whether the reader follows them.</summary>
    [HttpGet("profile/{appUserId:guid}")]
    public async Task<ActionResult<FeedProfileRecord>> GetProfile(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var user = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == appUserId)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null) return NotFound();

        return Ok(new FeedProfileRecord(
            user.Id,
            user.DisplayName ?? user.Email ?? "Unknown",
            await VisiblePosts(db).CountAsync(m => m.AuthorAppUserId == appUserId, ct),
            await db.UserFollows.AsNoTracking().CountAsync(f => f.FollowedAppUserId == appUserId, ct),
            await db.UserFollows.AsNoTracking().CountAsync(f => f.FollowerAppUserId == appUserId, ct),
            await db.UserFollows.AsNoTracking()
                .AnyAsync(f => f.FollowerAppUserId == userId && f.FollowedAppUserId == appUserId, ct),
            appUserId == userId));
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Posts, or replies to a post.
    /// </summary>
    /// <remarks>
    /// The mention and hashtag tables are filled here, from the body, by the server. Doing it
    /// client-side would mean trusting a browser about who gets notified.
    /// </remarks>
    [HttpPost("posts")]
    public async Task<ActionResult<FeedPostRecord>> CreatePost(
        [FromBody] CreateFeedPostRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("A post needs something in it.");
        if (body.Length > MaxBodyLength)
            return BadRequest($"A post can be at most {MaxBodyLength} characters.");

        if (request.ParentMessageId is { } parentId)
        {
            // Replying to something hidden, or to something that is not a feed post at all, is
            // refused — otherwise a hidden post keeps growing a thread nobody can see the top of.
            var parentExists = await VisiblePosts(db).AnyAsync(m => m.Id == parentId, ct);
            if (!parentExists) return NotFound("That post is no longer there.");
        }

        var post = new OrgMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = null,               // a feed post belongs to a person, not a group
            AuthorAppUserId = userId,
            ParentMessageId = request.ParentMessageId,
            ChannelType = OrgMessageChannel.PublicFeed,
            Body = body,
            IsPublic = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.OrgMessages.Add(post);

        foreach (var tag in FeedTextParser.FindHashtags(body))
        {
            db.OrgMessageHashtags.Add(new OrgMessageHashtag
            {
                Id = Guid.NewGuid(), OrgMessageId = post.Id, Tag = tag, DateCreated = post.DateCreated,
            });
        }

        foreach (var mentionedId in await ResolveMentionsAsync(db, body, ct))
        {
            db.OrgMessageMentions.Add(new OrgMessageMention
            {
                Id = Guid.NewGuid(), OrgMessageId = post.Id,
                MentionedAppUserId = mentionedId, DateCreated = post.DateCreated,
            });
        }

        await db.SaveChangesAsync(ct);

        var records = await ToRecordsAsync(db, [post], userId, ct);
        return Ok(records[0]);
    }

    /// <summary>Reports a post to the administrators. Idempotent per person.</summary>
    [HttpPost("posts/{id:guid}/report")]
    public async Task<IActionResult> ReportPost(
        Guid id, [FromBody] ReportFeedPostRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        if (!await VisiblePosts(db).AnyAsync(m => m.Id == id, ct)) return NotFound();

        // Reporting twice is not twice the signal. Answering the same way either way also means a
        // reporter cannot learn whether their first report was already acted on.
        if (await db.OrgMessageReports.AnyAsync(
                r => r.OrgMessageId == id && r.ReportedByAppUserId == userId, ct))
        {
            return NoContent();
        }

        db.OrgMessageReports.Add(new OrgMessageReport
        {
            Id = Guid.NewGuid(),
            OrgMessageId = id,
            ReportedByAppUserId = userId,
            Reason = request.Reason?.Trim(),
            Outcome = FeedReportOutcome.Pending,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Following ────────────────────────────────────────────────────────────

    [HttpPost("follow/{appUserId:guid}")]
    public async Task<IActionResult> Follow(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (appUserId == userId) return BadRequest("You already read your own posts.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        if (!await db.AppUsers.AsNoTracking().AnyAsync(u => u.Id == appUserId, ct)) return NotFound();

        if (await db.UserFollows.AnyAsync(
                f => f.FollowerAppUserId == userId && f.FollowedAppUserId == appUserId, ct))
        {
            return NoContent();   // already following; saying so twice changes nothing
        }

        db.UserFollows.Add(new UserFollow
        {
            Id = Guid.NewGuid(),
            FollowerAppUserId = userId,
            FollowedAppUserId = appUserId,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpDelete("follow/{appUserId:guid}")]
    public async Task<IActionResult> Unfollow(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var existing = await db.UserFollows
            .FirstOrDefaultAsync(f => f.FollowerAppUserId == userId && f.FollowedAppUserId == appUserId, ct);

        if (existing is not null)
        {
            // Deleted rather than flagged: a soft-deleted follow is a record of who once read whom.
            db.UserFollows.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the feed is switched on for this site.
    /// </summary>
    /// <remarks>
    /// Default <b>off</b>. A new feature that starts on is a feature nobody chose to run.
    /// </remarks>
    internal static Task<bool> FeedEnabledAsync(BenDataContext db, CancellationToken ct)
        => SiteSettingsService.GetBoolAsync(db, SiteSettingKeys.FeaturePublicFeed, whenUnset: false, ct);

    /// <summary>
    /// Feed posts a reader may see: the right channel, and not hidden.
    /// </summary>
    /// <remarks>
    /// Every read goes through this. Writing the hidden check at each call site is how one query
    /// eventually forgets it and serves a post an administrator removed.
    /// </remarks>
    private static IQueryable<OrgMessage> VisiblePosts(BenDataContext db)
        => db.OrgMessages.AsNoTracking()
             .Where(m => m.ChannelType == OrgMessageChannel.PublicFeed && m.HiddenUtc == null);

    /// <summary>
    /// The accounts a post's <c>@names</c> refer to.
    /// </summary>
    /// <remarks>
    /// <para><b>Ambiguity is a refusal, not a guess.</b> Display names are not unique in this
    /// product and a mention token cannot contain spaces, so "@sarahmitchell" is matched against
    /// display names with their spaces and punctuation removed. When two accounts normalise alike,
    /// neither is mentioned: notifying the wrong Sarah Mitchell is worse than notifying neither,
    /// and the text stays as plain text so the author can see it did not take.</para>
    ///
    /// <para>Whether this product should have real handles — unique, chosen, part of a profile URL
    /// — is a decision for Ben rather than something to introduce as a side effect of building a
    /// feed. Logged as a backlog item. Until then this is the honest approximation.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<Guid>> ResolveMentionsAsync(
        BenDataContext db, string body, CancellationToken ct)
    {
        var tokens = FeedTextParser.FindMentions(body);
        if (tokens.Count == 0) return [];

        // Every account is loaded because the comparison is over a normalised form the database
        // cannot index. Acceptable at this size and honestly not beyond it either — but if the
        // user table ever gets large, this is the query to revisit, and handles are the fix.
        var candidates = await db.AppUsers.AsNoTracking()
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);

        var byNormalised = candidates
            .Select(u => new { u.Id, Key = FeedTextParser.NormalizeName(u.DisplayName) })
            .Where(u => u.Key.Length > 0)
            .GroupBy(u => u.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Id).ToList(), StringComparer.Ordinal);

        var resolved = new List<Guid>();
        foreach (var token in tokens)
        {
            var key = FeedTextParser.NormalizeName(token);
            if (key.Length == 0) continue;

            // Exactly one, or nobody. See the remarks.
            if (byNormalised.TryGetValue(key, out var ids) && ids.Count == 1 && !resolved.Contains(ids[0]))
                resolved.Add(ids[0]);
        }

        return resolved;
    }

    /// <summary>Turns posts into records, resolving names, counts and the reader's own state.</summary>
    /// <remarks>
    /// One batch of queries for the whole page rather than a few per post — the N+1 this shape
    /// exists to avoid is the one that makes a feed slow exactly as it becomes worth reading.
    /// </remarks>
    private static async Task<IReadOnlyList<FeedPostRecord>> ToRecordsAsync(
        BenDataContext db, IReadOnlyList<OrgMessage> posts, Guid readerId, CancellationToken ct)
    {
        if (posts.Count == 0) return [];

        var ids = posts.Select(p => p.Id).ToList();
        var authorIds = posts.Select(p => p.AuthorAppUserId).Distinct().ToList();

        var names = await db.AppUsers.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Name ?? "Unknown", ct);

        var mentions = await db.OrgMessageMentions.AsNoTracking()
            .Where(m => ids.Contains(m.OrgMessageId))
            .Select(m => new
            {
                m.OrgMessageId,
                m.MentionedAppUserId,
                Name = m.MentionedAppUser.DisplayName ?? m.MentionedAppUser.Email,
            })
            .ToListAsync(ct);

        var hashtags = await db.OrgMessageHashtags.AsNoTracking()
            .Where(h => ids.Contains(h.OrgMessageId))
            .Select(h => new { h.OrgMessageId, h.Tag })
            .ToListAsync(ct);

        var replyCounts = (await db.OrgMessages.AsNoTracking()
            .Where(m => m.ParentMessageId != null
                     && ids.Contains(m.ParentMessageId!.Value)
                     && m.HiddenUtc == null)
            .GroupBy(m => m.ParentMessageId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.ParentId, x => x.Count);

        var followed = await db.UserFollows.AsNoTracking()
            .Where(f => f.FollowerAppUserId == readerId && authorIds.Contains(f.FollowedAppUserId))
            .Select(f => f.FollowedAppUserId)
            .ToListAsync(ct);

        var reported = await db.OrgMessageReports.AsNoTracking()
            .Where(r => ids.Contains(r.OrgMessageId) && r.ReportedByAppUserId == readerId)
            .Select(r => r.OrgMessageId)
            .ToListAsync(ct);

        return posts.Select(p => new FeedPostRecord(
            p.Id,
            p.AuthorAppUserId,
            names.GetValueOrDefault(p.AuthorAppUserId, "Unknown"),
            p.ParentMessageId,
            p.Body,
            p.DateCreated,
            replyCounts.GetValueOrDefault(p.Id),
            mentions.Where(m => m.OrgMessageId == p.Id)
                    .Select(m => new FeedMentionRecord(m.MentionedAppUserId, m.Name ?? "Unknown"))
                    .ToList(),
            hashtags.Where(h => h.OrgMessageId == p.Id).Select(h => h.Tag).ToList(),
            followed.Contains(p.AuthorAppUserId),
            p.AuthorAppUserId == readerId,
            reported.Contains(p.Id))).ToList();
    }

    // ── Cursor ───────────────────────────────────────────────────────────────
    // Timestamp plus id, because two posts can share a timestamp and a date-only cursor either
    // repeats one across the page boundary or skips it. Opaque to callers by contract, so the
    // format can change; not encrypted, because it encodes nothing a reader could not already see.

    private static string WriteCursor(DateTime dateCreated, Guid id)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{dateCreated.Ticks}:{id}"));

    private static bool TryReadCursor(string? cursor, out DateTime beforeUtc, out Guid beforeId)
    {
        beforeUtc = default;
        beforeId = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;

        try
        {
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':', 2);
            if (parts.Length != 2) return false;
            if (!long.TryParse(parts[0], out var ticks)) return false;
            if (!Guid.TryParse(parts[1], out beforeId)) return false;

            beforeUtc = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            // A malformed cursor reads as no cursor — the first page. Better than a 500 for
            // somebody who edited a URL.
            return false;
        }
    }
}
