using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// What people said about a published case (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// <para>A comment is an <see cref="OrgMessage"/> on the <see cref="OrgMessageChannel.PublicCaseComment"/>
/// channel carrying the case it belongs to. That is not a shortcut: hiding, reporting, the author
/// trail and the audit columns are already on that entity and already work, so a comment arrives
/// moderatable rather than arriving and then needing a moderation story written for it.</para>
///
/// <para>Reading is anonymous, because the case is. Writing needs an account, because a name has
/// to attach to it.</para>
/// </remarks>
[ApiController]
[Route("api/public/cases/{caseId:guid}/comments")]
public sealed class PublicCaseCommentController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IConfiguration _configuration;

    public PublicCaseCommentController(IDbContextFactory<BenDataContext> db, IConfiguration configuration)
    { _db = db; _configuration = configuration; }

    /// <summary>How long a comment may be. Longer than a feed post: this is a considered reply.</summary>
    public const int MaxBodyLength = 2_000;

    /// <summary>The comments on a case, oldest first, hidden ones left out.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicCaseComment>>> Get(
        Guid caseId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsPublicAsync(db, caseId, ct)) return NotFound();

        // Guid.Empty for an anonymous reader, which is the base class's convention — and a value
        // no author row can carry, so "is this mine" is simply false for them.
        var me = GetCurrentUserId();

        var rows = await db.OrgMessages.AsNoTracking()
            .Where(m => m.CaseId == caseId
                     && m.ChannelType == OrgMessageChannel.PublicCaseComment
                     && m.HiddenUtc == null)
            .OrderBy(m => m.DateCreated)
            .Select(m => new PublicCaseComment(
                m.Id,
                m.AuthorAppUser.DisplayName ?? m.AuthorAppUser.UserName ?? "Somebody",
                m.AuthorAppUser.Handle,
                m.Body,
                m.DateCreated,
                me != Guid.Empty && m.AuthorAppUserId == me,
                false))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Leaves a comment.</summary>
    /// <remarks>
    /// The body is stored as the plain text it was typed as. Nothing here renders it as markup and
    /// nothing should: a comment box that accepts HTML on a page anybody can post to is the oldest
    /// way there is to put a script in front of a stranger.
    /// </remarks>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<PublicCaseComment>> Post(
        Guid caseId, [FromBody] PostCaseCommentRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsPublicAsync(db, caseId, ct)) return NotFound();

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body)) return BadRequest("A comment needs something in it.");
        if (body.Length > MaxBodyLength)
            return BadRequest($"A comment can be at most {MaxBodyLength:N0} characters.");

        var now = DateTime.UtcNow;
        var comment = new OrgMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = null,        // it belongs to the case and its author, not to a group
            AuthorAppUserId = userId,
            ChannelType = OrgMessageChannel.PublicCaseComment,
            CaseId = caseId,
            Body = body,
            IsPublic = true,
            DateCreated = now,
            CreatedByAppUserId = userId,
        };

        db.OrgMessages.Add(comment);
        await db.SaveChangesAsync(ct);

        var author = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.UserName, u.Handle })
            .FirstAsync(ct);

        return Ok(new PublicCaseComment(
            comment.Id, author.DisplayName ?? author.UserName ?? "Somebody", author.Handle,
            comment.Body, comment.DateCreated, IsMine: true, IsHidden: false));
    }

    /// <summary>
    /// Takes a comment down.
    /// </summary>
    /// <remarks>
    /// Only its author, and only their own. A group cannot delete what somebody said about its
    /// case — that is what reporting is for, and letting the subject of a comment remove it would
    /// make the comments a place where nothing critical survives.
    /// </remarks>
    [HttpDelete("{commentId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid caseId, Guid commentId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var comment = await db.OrgMessages
            .FirstOrDefaultAsync(m => m.Id == commentId
                                   && m.CaseId == caseId
                                   && m.ChannelType == OrgMessageChannel.PublicCaseComment, ct);
        if (comment is null) return NotFound();
        if (comment.AuthorAppUserId != userId) return Forbid();

        db.OrgMessages.Remove(comment);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Reports a case, or a comment on it, to the site's moderators.
    /// </summary>
    /// <remarks>
    /// <para>The same answer whether or not it was already reported, and whether or not a
    /// moderator has acted: a reporter must not be able to learn what happened to their first
    /// report by sending a second, and nothing here is a channel back to them.</para>
    ///
    /// <para>A case and a comment land in the same queue, which is the one an administrator
    /// already watches — a second queue is a queue somebody forgets to open.</para>
    /// </remarks>
    [HttpPost("~/api/public/cases/{caseId:guid}/report")]
    [Authorize]
    public async Task<IActionResult> ReportCase(
        Guid caseId, [FromBody] ReportContentRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsPublicAsync(db, caseId, ct)) return NotFound();

        if (await db.OrgMessageReports.AnyAsync(
                r => r.CaseId == caseId && r.ReportedByAppUserId == userId, ct))
        {
            return NoContent();
        }

        db.OrgMessageReports.Add(new OrgMessageReport
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ReportedByAppUserId = userId,
            Reason = Trimmed(request.Reason),
            Outcome = FeedReportOutcome.Pending,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Puts a link to this case on the feed, as a post by the person pressing the button.
    /// </summary>
    /// <remarks>
    /// <para><b>As themselves, never as a group.</b> Ben's rule, 2026-09-11: posting on behalf of
    /// an organization takes permission, and anybody may repost a published case. So this is one
    /// person saying "look at this", and the post says so — the case keeps its own attribution on
    /// its own page, where the group's name already is.</para>
    ///
    /// <para>The case travels in <c>CaseId</c>, which is the same column the editor's
    /// "post from a case" uses, so anything that already reads a post's case reads this one too.
    /// Unlike that path there is no media and no consent question: a published case is public
    /// already, and a link to it discloses nothing its own page does not.</para>
    ///
    /// <para>Reposting the same case twice is refused with the first post's id rather than an
    /// error — the reader wanted a link to exist, and one does.</para>
    /// </remarks>
    [HttpPost("~/api/public/cases/{caseId:guid}/repost")]
    [Authorize]
    public async Task<ActionResult<Guid>> Repost(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var theCase = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId && c.IsPublic
                     && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted))
            .Select(c => new
            {
                c.Title,
                c.UrlName,
                c.CaseYear,
                c.OrgCaseNumber,
                OrgUrl = c.Organization.UrlName,
            })
            .FirstOrDefaultAsync(ct);
        if (theCase is null) return NotFound();

        if (await FeedParticipation.RefusalAsync(db, userId, ct) is { } refusal)
            return BadRequest(refusal);

        var already = await db.OrgMessages.AsNoTracking()
            .Where(m => m.CaseId == caseId
                     && m.AuthorAppUserId == userId
                     && m.ChannelType == OrgMessageChannel.PublicFeed)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (already is { } existing) return Ok(existing);

        var now = DateTime.UtcNow;
        var post = new OrgMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = null,          // a feed post belongs to a person, not a group
            AuthorAppUserId = userId,
            ChannelType = OrgMessageChannel.PublicFeed,
            CaseId = caseId,
            // "The repost is a link to the case" (Ben, 2026-09-11) — so the body is the link and
            // nothing else. The card under it already says the title, the group and the place,
            // read live from our own records; repeating the title in the body put it on the
            // screen twice. And no sentence is invented on the poster's behalf: words they did
            // not say should not appear over their name.
            //
            // ABSOLUTE, for two reasons the first version got wrong: a root-relative path is not
            // a link anywhere a body is rendered (the segmenter wants a scheme, on purpose, so
            // that ordinary full stops do not become links), and it is not a link at all once
            // somebody copies the post somewhere else.
            Body = PublicCaseUrl(theCase.OrgUrl, theCase.UrlName, theCase.CaseYear, theCase.OrgCaseNumber),
            IsPublic = true,
            AttributionState = OrgAttributionState.Unclaimed,
            DateCreated = now,
            CreatedByAppUserId = userId,
        };

        db.OrgMessages.Add(post);
        await db.SaveChangesAsync(ct);
        return Ok(post.Id);
    }

    /// <summary>Reports one comment. See <see cref="ReportCase"/> for why the answer never varies.</summary>
    [HttpPost("{commentId:guid}/report")]
    [Authorize]
    public async Task<IActionResult> ReportComment(
        Guid caseId, Guid commentId, [FromBody] ReportContentRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var exists = await db.OrgMessages.AsNoTracking().AnyAsync(
            m => m.Id == commentId && m.CaseId == caseId
              && m.ChannelType == OrgMessageChannel.PublicCaseComment
              && m.HiddenUtc == null, ct);
        if (!exists) return NotFound();

        if (await db.OrgMessageReports.AnyAsync(
                r => r.OrgMessageId == commentId && r.ReportedByAppUserId == userId, ct))
        {
            return NoContent();
        }

        db.OrgMessageReports.Add(new OrgMessageReport
        {
            Id = Guid.NewGuid(),
            OrgMessageId = commentId,
            ReportedByAppUserId = userId,
            Reason = Trimmed(request.Reason),
            Outcome = FeedReportOutcome.Pending,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The public address of a case, absolute.
    /// </summary>
    /// <remarks>
    /// <paramref name="urlName"/> is null on every case published before slugs existed, and the
    /// case route accepts the old "2026-042" reference for exactly that reason — so the fallback
    /// here is not a nicety. The first version of this built ".../cases/" with nothing on the end
    /// and put it on the feed.
    /// </remarks>
    private string PublicCaseUrl(string orgUrl, string? urlName, int year, int number)
    {
        var slug = string.IsNullOrWhiteSpace(urlName) ? $"{year}-{number:D3}" : urlName;
        var root = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        return $"{root}/o/{orgUrl}/cases/{slug}";
    }

    /// <summary>A case anybody may read: published, and its group meant it to be.</summary>
    private static Task<bool> IsPublicAsync(BenDataContext db, Guid caseId, CancellationToken ct)
        => db.Cases.AsNoTracking().AnyAsync(
            c => c.Id == caseId && c.IsPublic
              && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted), ct);
}
