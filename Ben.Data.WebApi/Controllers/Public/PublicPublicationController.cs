using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Publications;
using Ben.Service.Models.Publications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Reading publications, without an account.
/// </summary>
/// <remarks>
/// <para><b>Anonymous by design, and that is the product.</b> A publication nobody can read without
/// signing up is a newsletter with no readers — the whole point is that a stranger finds a piece,
/// reads it, and then decides to subscribe. Subscribing needs an account; reading does not.</para>
///
/// <para><b>Two gates, both required.</b> A post is visible only when its publication is public
/// <i>and</i> the post itself has been published. Drafts are invisible here whatever the
/// publication's state, and an entire publication can be withheld whatever its posts' states.</para>
///
/// <para><b>This controller never sees a draft.</b> The authoring controller is a separate class
/// rather than the same queries with a flag: one forgotten argument on a shared path is how a draft
/// reaches the public, and there is no way to forget an argument that does not exist.</para>
///
/// <para>404s wholesale when <c>features.publications</c> is off.</para>
/// </remarks>
[ApiController]
[Route("api/public/publications")]
[AllowAnonymous]
public sealed class PublicPublicationController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicPublicationController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Every public publication with something in it, most recently active first.</summary>
    /// <remarks>
    /// Publications with no published post are left out. A directory listing a title that leads to
    /// an empty page wastes the one click somebody was willing to spend.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicPublicationRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        var publications = await db.Publications.AsNoTracking()
            .Where(p => p.IsPublic && p.Posts.Any(post => post.PublishedUtc != null))
            .Select(p => new PublicPublicationRecord(
                p.UrlName,
                p.Title,
                p.Description,
                p.Organization.Name,
                p.Organization.UrlName,
                p.Posts.Count(post => post.PublishedUtc != null),
                p.Posts.Where(post => post.PublishedUtc != null).Max(post => post.PublishedUtc)))
            .ToListAsync(ct);

        return Ok(publications.OrderByDescending(p => p.LatestPostUtc).ToList());
    }

    /// <summary>One publication, and its published posts newest first.</summary>
    [HttpGet("{urlName}")]
    public async Task<ActionResult<PublicPublicationDetail>> Get(string urlName, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        var publication = await db.Publications.AsNoTracking()
            .Where(p => p.IsPublic && p.UrlName == urlName)
            .Select(p => new { p.Id, p.UrlName, p.Title, p.Description, OrgName = p.Organization.Name, OrgUrl = p.Organization.UrlName })
            .FirstOrDefaultAsync(ct);

        if (publication is null) return NotFound();

        // Ordered on the entity before the projection: ordering a projected record is not
        // translatable and fails at runtime rather than at compile time.
        var posts = await db.PublicationPosts.AsNoTracking()
            .Where(p => p.PublicationId == publication.Id && p.PublishedUtc != null)
            .OrderByDescending(p => p.PublishedUtc)
            .Select(p => new { p.UrlName, p.Title, p.Excerpt, p.PublishedUtc, p.RequiredTier })
            .ToListAsync(ct);

        // A listing never carries bodies — free or not. There is no reason to send a page of
        // articles to render a page of titles, and it keeps the tier decision in exactly one place.
        var summaries = posts
            .Select(p => new PublicPublicationPostRecord(
                p.UrlName, p.Title, p.Excerpt, BodyHtml: null, p.PublishedUtc!.Value,
                RequiresSubscription: p.RequiredTier is not null,
                publication.Title, publication.UrlName))
            .ToList();

        return Ok(new PublicPublicationDetail(
            new PublicPublicationRecord(
                publication.UrlName, publication.Title, publication.Description,
                publication.OrgName, publication.OrgUrl, summaries.Count,
                summaries.Count > 0 ? summaries[0].PublishedUtc : null),
            summaries));
    }

    /// <summary>
    /// One post, with its body when the reader may have it.
    /// </summary>
    /// <remarks>
    /// <para>The tier check is the one piece of monetisation that exists today, and it exists
    /// <i>because</i> nothing writes a tier yet: writing the withholding path now, against a column
    /// that is always null, costs nothing — retrofitting it later means changing what is already
    /// published and already being read.</para>
    ///
    /// <para><b>The body is withheld rather than sent and hidden.</b> Markup delivered to a browser
    /// has been delivered, whatever the page then does with it — a paywall implemented in CSS is
    /// not a paywall.</para>
    /// </remarks>
    [HttpGet("{urlName}/{postUrlName}")]
    public async Task<ActionResult<PublicPublicationPostRecord>> GetPost(
        string urlName, string postUrlName, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        var post = await db.PublicationPosts.AsNoTracking()
            .Where(p => p.UrlName == postUrlName
                     && p.PublishedUtc != null
                     && p.Publication.IsPublic
                     && p.Publication.UrlName == urlName)
            .Select(p => new
            {
                p.UrlName, p.Title, p.Excerpt, p.BodyHtml, p.PublishedUtc, p.RequiredTier,
                PublicationTitle = p.Publication.Title,
                PublicationUrlName = p.Publication.UrlName,
            })
            .FirstOrDefaultAsync(ct);

        if (post is null) return NotFound();

        // Null tier means free, which is every post today.
        var withheld = post.RequiredTier is not null;

        return Ok(new PublicPublicationPostRecord(
            post.UrlName, post.Title, post.Excerpt,
            withheld ? null : post.BodyHtml,
            post.PublishedUtc!.Value,
            withheld,
            post.PublicationTitle, post.PublicationUrlName));
    }
}

/// <summary>A publication and its posts, for the publication's own page.</summary>
public sealed record PublicPublicationDetail(
    PublicPublicationRecord Publication,
    IReadOnlyList<PublicPublicationPostRecord> Posts);
