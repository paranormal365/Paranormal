using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The public feed: anyone may read it, people who belong here may write in it.
/// </summary>
/// <remarks>
/// <para><b>Every route here 404s wholesale when <c>features.public-feed</c> is off</b>, which it
/// is by default. Not 403 — a disabled feature should not be discoverable by the shape of its
/// refusal, and "this does not exist here" is the truthful answer for a site whose administrator
/// has not turned the feed on.</para>
///
/// <para><b>Reading is anonymous; writing is not</b> (item 186). The open scroll is the front
/// door — a visitor who has to sign in before seeing anything has nothing to sign up for — so the
/// three GETs are <c>[AllowAnonymous]</c> and every write carries <c>[Authorize]</c> of its own.
/// The read path is otherwise IDENTICAL for both: an anonymous reader is <c>Guid.Empty</c>, whose
/// follows, reports and authorship simply match nothing, so the per-reader flags come back false
/// without a second code path to keep in step.</para>
///
/// <para>Reports never hide anything by themselves: hiding is an administrator's act, so a group
/// who dislike a post cannot remove it between them.</para>
///
/// <para><b>Storage reuses <c>OrgMessage</c></b> with <c>ChannelType.PublicFeed</c>. That table was
/// built with a nullable OrganizationId and parent-based threading, which is exactly a feed post
/// and its replies. A second near-identical table would have meant two places to fix every time
/// the way a message is written changes.</para>
/// </remarks>
[ApiController]
[Route("api/feed")]
public sealed class FeedController : BenControllerBase
{
    /// <summary>Posts per page. Also the cap a caller may ask for.</summary>
    private const int PageSize = 25;

    /// <summary>Longest post. Short-form is the point; a wall of text belongs in a publication.</summary>
    public const int MaxBodyLength = 1000;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaIngestService _mediaIngest;

    public FeedController(
        IDbContextFactory<BenDataContext> db,
        IFileStorageService fileStorage,
        IMediaIngestService mediaIngest)
    {
        _db = db;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
    }

    /// <summary>What a feed post may carry, by content type.</summary>
    private static bool IsAllowedMedia(string? contentType)
        => contentType is not null
        && (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
         || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        // An SVG is a document that can carry script, and this is the one upload surface open to
        // everybody who belongs. Refused by type as well as by extension.
        && !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    // ── Reading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A page of the feed, newest first.
    /// </summary>
    /// <param name="mode">
    /// <c>foryou</c> — ranked by <see cref="FeedRanking"/>. <c>all</c> — everybody's posts,
    /// newest first. <c>following</c> — only people the reader follows, plus their own. Anything
    /// else reads as <c>all</c>, which keeps every link and client written before ranking existed.
    /// </param>
    /// <param name="hashtag">Narrow to one tag. Combines with <paramref name="mode"/>.</param>
    /// <param name="author">
    /// One person's posts. Overrides <paramref name="mode"/> — "their posts, followed or not" is
    /// the only question a profile page asks.
    /// </param>
    /// <param name="cursor">From a previous page's <c>NextCursor</c>. Opaque.</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<FeedPageRecord>> GetFeed(
        [FromQuery] string? mode, [FromQuery] string? hashtag, [FromQuery] string? cursor,
        CancellationToken ct, [FromQuery] Guid? author = null)
    {
        // Guid.Empty for a visitor: they follow nobody and wrote nothing, so every per-reader flag
        // resolves false through the same queries a signed-in reader uses.
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var query = VisiblePosts(db);

        if (author is { } authorId)
        {
            query = query.Where(m => m.AuthorAppUserId == authorId);
        }
        else if (string.Equals(mode, "following", StringComparison.OrdinalIgnoreCase))
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

        // ── For You: score a bounded window, then page by offset ─────────────
        // A keyset cursor cannot work here: the sort key is a score that changes as people like
        // things, so "everything after post X" has no stable meaning. An offset over a freshly
        // ranked window does, and the page de-dupes what it has already shown.
        if (author is null && string.Equals(mode, "foryou", StringComparison.OrdinalIgnoreCase))
            return Ok(await RankedPageAsync(db, query, userId, cursor, ct));

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

        // Asked once per page rather than per post: the answer is about the reader, not the row.
        var canPost = userId != Guid.Empty
                   && await FeedParticipation.RefusalAsync(db, userId, ct) is null;

        return Ok(new FeedPageRecord(posts, next, canPost));
    }

    /// <summary>One post and its replies, oldest reply first.</summary>
    [HttpGet("posts/{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<FeedPostRecord>>> GetThread(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var root = await VisiblePosts(db).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (root is null) return NotFound();

        var replies = await VisiblePosts(db)
            .Where(m => m.ParentMessageId == id)
            .OrderBy(m => m.DateCreated).ThenBy(m => m.Id)
            .ToListAsync(ct);

        // Only a signed-in reader has a bell to clear.
        if (userId != Guid.Empty) await MarkSeenAsync(db, id, userId, ct);

        return Ok(await ToRecordsAsync(db, [root, .. replies], userId, ct));
    }

    /// <summary>Somebody's feed profile — their counts, and whether the reader follows them.</summary>
    [HttpGet("profile/{appUserId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<FeedProfileRecord>> GetProfile(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
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
    [Authorize]
    public async Task<ActionResult<FeedPostRecord>> CreatePost(
        [FromForm] CreateFeedPostRequest request, IFormFile? media, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        // Item 186 F2: anyone reads, people who belong here write.
        if (await FeedParticipation.RefusalAsync(db, userId, ct) is { } refusal)
            return BadRequest(refusal);

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

        // ── The photo or video, when there is one (item 186 F4) ──────────────
        // Through MediaIngestService like every other upload door, so the feed cannot become the
        // one surface where location data survives. The review state is left at its default,
        // Pending, which is why nothing rendered here until F5's screening moves it on.
        if (media is { Length: > 0 })
        {
            if (!IsAllowedMedia(media.ContentType))
                return BadRequest("A post can carry a photo or a video. That file is neither.");

            var storedName = $"{Guid.NewGuid():N}{Path.GetExtension(media.FileName)}";
            var storagePath = _fileStorage.UserFilePath(userId, storedName);
            var uploadFileId = Guid.NewGuid();

            IngestedMedia ingested;
            try
            {
                ingested = await _mediaIngest.IngestAsync(media, storagePath, uploadFileId, ct);
            }
            catch (UnreadableImageException ex)
            {
                return BadRequest(ex.Message);
            }

            db.UploadFiles.Add(new UploadFile
            {
                Id = uploadFileId,
                UploadFileTypeId = UploadFileTypeSeeder.FeedMediaFileTypeId,
                AppUserId = userId,
                FileName = media.FileName,
                StoredFileName = storedName,
                ContentType = ingested.ServedContentType,
                FileSize = ingested.ServedFileSize,
                StoragePath = storagePath,
                // False deliberately: the feed's own endpoint decides who may see this, and it
                // refuses anything unscreened. Public here would route around that.
                IsPublic = false,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
            db.UploadFileMetadata.Add(ingested.Metadata);

            post.MediaUploadFileId = uploadFileId;
            post.MediaReviewState = FeedMediaReviewState.Pending;
        }

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
    [Authorize]
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

    /// <summary>
    /// The photo or video on a post, for anybody who may read the post.
    /// </summary>
    /// <remarks>
    /// <para>Three conditions, all re-asked here on every request: the feed is on, the post is
    /// visible (<see cref="VisiblePosts"/> — so hiding a post hides its media with it, at no
    /// extra cost), and the media has been <b>Approved</b>. Pending and Held both 404.</para>
    ///
    /// <para><b>404, not 403.</b> "That exists but you may not see it" is itself a disclosure
    /// about content a moderator has held, and the honest answer to a request for something
    /// nobody may see is that there is nothing there.</para>
    ///
    /// <para>Serves the SANITIZED copy through <c>ServingPathFor</c>, so the location data that
    /// came off at ingest cannot leave by this route either.</para>
    /// </remarks>
    [HttpGet("posts/{id:guid}/media")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPostMedia(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        var post = await VisiblePosts(db)
            .Where(m => m.Id == id
                     && m.MediaUploadFileId != null
                     && m.MediaReviewState == FeedMediaReviewState.Approved)
            .Select(m => new { m.MediaUploadFile!.StoragePath, m.MediaUploadFile.ContentType })
            .FirstOrDefaultAsync(ct);

        // A row with no storage path is a file that never landed — nothing to serve, and the
        // honest answer is the same 404 as a held one.
        if (post?.StoragePath is not { Length: > 0 } storagePath) return NotFound();

        var path = _mediaIngest.ServingPathFor(storagePath);
        if (!System.IO.File.Exists(path)) return NotFound();

        return PhysicalFile(path, post.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    // ── Liking ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Likes a post. Idempotent: liking twice is liking once.
    /// </summary>
    /// <remarks>
    /// Participation-gated like posting — a like is a small act of authorship, it lifts what it
    /// touches in the ranking, and a feed whose scores can be moved by anybody with an email
    /// address is a feed whose scores mean nothing.
    /// </remarks>
    [HttpPost("posts/{id:guid}/like")]
    [Authorize]
    public async Task<IActionResult> LikePost(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        if (await FeedParticipation.RefusalAsync(db, userId, ct) is { } refusal)
            return BadRequest(refusal);

        // A hidden post cannot be liked: VisiblePosts is the one place that knows what "there"
        // means, and a like on something an administrator removed would resurrect it in the
        // ranking the moment it came back.
        if (!await VisiblePosts(db).AnyAsync(m => m.Id == id, ct)) return NotFound();

        if (await db.OrgMessageLikes.AnyAsync(
                l => l.OrgMessageId == id && l.LikerAppUserId == userId, ct))
        {
            return NoContent();
        }

        db.OrgMessageLikes.Add(new OrgMessageLike
        {
            OrgMessageId = id, LikerAppUserId = userId, DateLiked = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two taps, two circuits, one composite key. The post is liked either way.
        }

        return NoContent();
    }

    /// <summary>Takes a like back. Forgiving: unliking what was never liked is a no-op.</summary>
    [HttpDelete("posts/{id:guid}/like")]
    [Authorize]
    public async Task<IActionResult> UnlikePost(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        // Deliberately NOT participation-gated. Taking back something you already did is not
        // participation, and somebody whose standing lapsed must still be able to undo it.
        var like = await db.OrgMessageLikes
            .FirstOrDefaultAsync(l => l.OrgMessageId == id && l.LikerAppUserId == userId, ct);

        if (like is not null)
        {
            db.OrgMessageLikes.Remove(like);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    // ── Following ────────────────────────────────────────────────────────────

    [HttpPost("follow/{appUserId:guid}")]
    [Authorize]
    public async Task<IActionResult> Follow(Guid appUserId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (appUserId == userId) return BadRequest("You already read your own posts.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await FeedEnabledAsync(db, ct)) return NotFound();

        if (!await db.AppUsers.AsNoTracking().AnyAsync(u => u.Id == appUserId, ct)) return NotFound();

        // Following builds somebody's audience, so it is participation (item 186 F2). Reporting
        // is not, and is deliberately left open to any signed-in reader.
        if (await FeedParticipation.RefusalAsync(db, userId, ct) is { } refusal)
            return BadRequest(refusal);

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
    [Authorize]
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
    /// Records that this reader has opened a post, which is what clears a mention from their bell.
    /// </summary>
    /// <remarks>
    /// <para>Reuses <c>OrgMessageView</c>, the marker the rest of the messaging system already
    /// uses. A read flag on the mention itself would be a second record of one fact, and the two
    /// would drift the first time a post was read by some other route.</para>
    ///
    /// <para>Opening a thread is the read signal rather than the post scrolling past in the feed.
    /// A feed post glimpsed on the way down the page has not been read in any sense worth clearing
    /// a notification for, and "you were mentioned" is exactly the notification somebody would be
    /// annoyed to lose without seeing.</para>
    ///
    /// <para>Best-effort: a failure here must not take down the read that succeeded. The mention
    /// simply stays unread and clears next time.</para>
    /// </remarks>
    private static async Task MarkSeenAsync(BenDataContext db, Guid postId, Guid readerId, CancellationToken ct)
    {
        try
        {
            var already = await db.OrgMessageViews
                .AnyAsync(v => v.OrgMessageId == postId && v.ViewerAppUserId == readerId, ct);
            if (already) return;

            db.OrgMessageViews.Add(new OrgMessageView
            {
                OrgMessageId = postId,
                ViewerAppUserId = readerId,
                DateViewed = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two tabs opening the same post at once; the composite key refuses the second. The
            // post is marked seen either way, which is the whole point.
        }
    }

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
    /// <para>Resolved against <c>AppUser.Handle</c>, exactly. A handle is unique and permanent, so
    /// <c>@sarahmitchell</c> means one account and goes on meaning that account after she changes
    /// her display name.</para>
    ///
    /// <para>This used to match a normalised display name, because handles did not exist yet — and
    /// it had to refuse whenever two accounts normalised alike, since notifying the wrong Sarah
    /// Mitchell is worse than notifying neither. Worse, the answer could <i>change</i> as accounts
    /// were added: a mention that resolved today would stop resolving the day a second Sarah signed
    /// up. Handles removed the ambiguity rather than managing it.</para>
    ///
    /// <para>An <c>@name</c> nobody holds resolves to nothing and stays plain text, which is what
    /// the author should see: their typo reached no one.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<Guid>> ResolveMentionsAsync(
        BenDataContext db, string body, CancellationToken ct)
    {
        var tokens = FeedTextParser.FindMentions(body)
            .Select(UserHandle.Normalize)
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tokens.Count == 0) return [];

        // One indexed lookup over the handles actually typed, rather than reading every account
        // and comparing in memory as the display-name version had to.
        return await db.AppUsers.AsNoTracking()
            .Where(u => u.Handle != null && tokens.Contains(u.Handle))
            .Select(u => u.Id)
            .ToListAsync(ct);
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
                m.MentionedAppUser.Handle,
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

        // An anonymous reader (Guid.Empty) follows nobody and has reported nothing. The queries
        // would answer that correctly anyway; skipping them saves two round trips on the page a
        // visitor is most likely to hit, which is the whole front door.
        var followed = readerId == Guid.Empty
            ? []
            : await db.UserFollows.AsNoTracking()
                .Where(f => f.FollowerAppUserId == readerId && authorIds.Contains(f.FollowedAppUserId))
                .Select(f => f.FollowedAppUserId)
                .ToListAsync(ct);

        var reported = readerId == Guid.Empty
            ? []
            : await db.OrgMessageReports.AsNoTracking()
                .Where(r => ids.Contains(r.OrgMessageId) && r.ReportedByAppUserId == readerId)
                .Select(r => r.OrgMessageId)
                .ToListAsync(ct);

        // Counted per page beside the replies, for the reason OrgMessageLike documents: one
        // source of truth. A cached counter drifts the first time an unlike races a like.
        var likeCounts = (await db.OrgMessageLikes.AsNoTracking()
            .Where(l => ids.Contains(l.OrgMessageId))
            .GroupBy(l => l.OrgMessageId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.PostId, x => x.Count);

        // Content types for the page's media, in one lookup. Read from the stored type rather
        // than the file name: the extension is whatever the uploader's phone called it, and the
        // served copy may be a remux with a different one. The nav property is deliberately not
        // Included — an AsNoTracking query would leave it null and every video would render as
        // an image, silently.
        var mediaFileIds = posts.Where(p => p.MediaUploadFileId is not null)
                                .Select(p => p.MediaUploadFileId!.Value)
                                .Distinct()
                                .ToList();
        var mediaTypes = mediaFileIds.Count == 0
            ? []
            : await db.UploadFiles.AsNoTracking()
                .Where(f => mediaFileIds.Contains(f.Id))
                .Select(f => new { f.Id, f.ContentType })
                .ToDictionaryAsync(f => f.Id, f => f.ContentType, ct);

        FeedMediaKind KindOf(OrgMessage post)
        {
            if (post.MediaUploadFileId is not { } fileId) return FeedMediaKind.None;
            var type = mediaTypes.GetValueOrDefault(fileId);
            return type?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
                ? FeedMediaKind.Video
                : FeedMediaKind.Image;
        }

        var liked = readerId == Guid.Empty
            ? []
            : await db.OrgMessageLikes.AsNoTracking()
                .Where(l => ids.Contains(l.OrgMessageId) && l.LikerAppUserId == readerId)
                .Select(l => l.OrgMessageId)
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
                    .Select(m => new FeedMentionRecord(
                        m.MentionedAppUserId, m.Handle ?? string.Empty, m.Name ?? "Unknown"))
                    .ToList(),
            hashtags.Where(h => h.OrgMessageId == p.Id).Select(h => h.Tag).ToList(),
            followed.Contains(p.AuthorAppUserId),
            // Never "own post" for a visitor: Guid.Empty is nobody, and an author id could not be
            // Guid.Empty anyway, but saying so here keeps the intent legible.
            readerId != Guid.Empty && p.AuthorAppUserId == readerId,
            reported.Contains(p.Id),
            likeCounts.GetValueOrDefault(p.Id),
            liked.Contains(p.Id),
            // The id is never emitted — a reader gets the post's own media route or nothing, so
            // there is no file id to try against the general file endpoints.
            p.MediaUploadFileId is not null && p.MediaReviewState == FeedMediaReviewState.Approved,
            // Only the AUTHOR is told their media is waiting. To anybody else the post simply has
            // no media: "somebody uploaded something that has not cleared" is a fact about content
            // nobody may see, and there is no reason for a stranger to learn it.
            p.MediaUploadFileId is not null
                && p.MediaReviewState == FeedMediaReviewState.Pending
                && readerId != Guid.Empty
                && p.AuthorAppUserId == readerId,
            // Likewise the kind: an unapproved post reports None, so not even "there is a video
            // here somewhere" escapes.
            p.MediaReviewState == FeedMediaReviewState.Approved ? KindOf(p) : FeedMediaKind.None))
            .ToList();
    }

    /// <summary>
    /// A page of the ranked feed.
    /// </summary>
    /// <remarks>
    /// <para><b>Bounded window, not the whole table.</b> Only the most recent
    /// <see cref="RankingWindowSize"/> posts from the last <see cref="RankingWindowDays"/> days are
    /// candidates. Scoring is per-row work, so an unbounded window would get slower every day the
    /// site succeeded — and nothing older than the window could win on score anyway, because
    /// gravity has already pushed it below anything from this month.</para>
    ///
    /// <para>Two queries: one for the window's engagement counts, one for the page's bodies. The
    /// counts are grouped in SQL rather than loaded and counted here.</para>
    /// </remarks>
    private static async Task<FeedPageRecord> RankedPageAsync(
        BenDataContext db, IQueryable<OrgMessage> query, Guid userId, string? cursor,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var since = now.AddDays(-RankingWindowDays);

        var window = await query
            .Where(m => m.DateCreated >= since)
            .OrderByDescending(m => m.DateCreated).ThenByDescending(m => m.Id)
            .Take(RankingWindowSize)
            .Select(m => new
            {
                m.Id,
                m.DateCreated,
                Likes = m.Likes.Count,
                Replies = m.Replies.Count(r => r.HiddenUtc == null),
            })
            .ToListAsync(ct);

        var ranked = FeedRanking.Rank(
            window.Select(w => new RankableFeedPost(w.Id, w.DateCreated, w.Likes, w.Replies)), now);

        var offset = ReadOffsetCursor(cursor);
        var pageIds = ranked.Skip(offset).Take(PageSize).Select(p => p.Id).ToList();
        var hasMore = ranked.Count > offset + pageIds.Count;

        // Fetched by id, then put back into ranked order — a WHERE IN says nothing about sequence.
        var posts = await db.OrgMessages.AsNoTracking()
            .Where(m => pageIds.Contains(m.Id))
            .ToListAsync(ct);
        var byId = posts.ToDictionary(p => p.Id);
        var ordered = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        var records = await ToRecordsAsync(db, ordered, userId, ct);
        var canPost = userId != Guid.Empty
                   && await FeedParticipation.RefusalAsync(db, userId, ct) is null;

        return new FeedPageRecord(
            records, hasMore ? WriteOffsetCursor(offset + pageIds.Count) : null, canPost);
    }

    /// <summary>How far back For You looks. Gravity has buried anything older regardless.</summary>
    private const int RankingWindowDays = 30;

    /// <summary>How many candidates are scored. Per-row work, so it is capped.</summary>
    private const int RankingWindowSize = 500;

    // ── Cursor ───────────────────────────────────────────────────────────────
    // Timestamp plus id, because two posts can share a timestamp and a date-only cursor either
    // repeats one across the page boundary or skips it. Opaque to callers by contract, so the
    // format can change; not encrypted, because it encodes nothing a reader could not already see.

    /// <summary>
    /// The For You cursor: an offset into a freshly ranked window, prefixed so it can never be
    /// mistaken for a keyset cursor by whichever branch reads it next.
    /// </summary>
    private static string WriteOffsetCursor(int offset)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"f:{offset}"));

    /// <summary>Reads a For You cursor. Anything unreadable is page one — never an error.</summary>
    private static int ReadOffsetCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;

        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return text.StartsWith("f:", StringComparison.Ordinal)
                && int.TryParse(text[2..], out var offset)
                && offset >= 0
                    ? offset
                    : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

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
