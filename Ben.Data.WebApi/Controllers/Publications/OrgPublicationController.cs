using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Publications;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Publications;

/// <summary>
/// Authoring a group's publication: the publication itself, its posts, and publishing them.
/// </summary>
/// <remarks>
/// <para>Permission-gated the same way CMS authoring is, through the organisation security
/// service — a publication is the group's public voice, so who may write in its name is the same
/// question as who may edit its pages.</para>
///
/// <para><b>404s wholesale when <c>features.publications</c> is off</b>, which it is by default.
/// Not 403: a disabled feature should not be discoverable by the shape of its refusal.</para>
///
/// <para>Reading here shows drafts. The anonymous controller shows only what has been published,
/// and the two are kept apart deliberately rather than sharing a query with a flag — one forgotten
/// argument on a shared path is how a draft reaches the public.</para>
/// </remarks>
[Route("api/organizations/{organizationId:guid}/publications")]
public sealed class OrgPublicationController : OrgCmsControllerBase
{
    private readonly ICmsMarkupSanitizer _sanitizer;

    public OrgPublicationController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        ICmsMarkupSanitizer sanitizer)
        : base(dbFactory, mapper, security) => _sanitizer = sanitizer;

    /// <summary>Whether publications are switched on for this site. Default off.</summary>
    internal static Task<bool> PublicationsEnabledAsync(BenDataContext db, CancellationToken ct)
        => SiteSettingsService.GetBoolAsync(db, SiteSettingKeys.FeaturePublications, whenUnset: false, ct);

    // ── The publication ──────────────────────────────────────────────────────

    /// <summary>The group's publications, with their counts.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicationRecord>>> GetAll(
        Guid organizationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        var publications = await db.Publications.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .OrderBy(p => p.Title)
            .Select(p => new PublicationRecord(
                p.Id, p.OrganizationId, p.Title, p.UrlName, p.Description, p.IsPublic,
                p.Posts.Count(post => post.PublishedUtc != null),
                p.Posts.Count(post => post.PublishedUtc == null),
                p.Subscriptions.Count(s => s.CancelledUtc == null),
                p.DateCreated))
            .ToListAsync(ct);

        return Ok(publications);
    }

    /// <summary>
    /// Creates a publication.
    /// </summary>
    /// <remarks>
    /// The URL name is derived from the title <b>once, here</b>, and de-duplicated against every
    /// other publication on the site. It is never recomputed on rename: item 89 established what
    /// happens otherwise — a renamed thing silently breaks every link anybody shared.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<PublicationRecord>> Create(
        Guid organizationId, [FromBody] SavePublicationRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Create, ct))
            return Forbid();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("A publication needs a name.");

        var slug = UrlSlug.From(title);
        if (slug is null) return BadRequest("That name has no letters or digits to make an address from.");

        slug = await UniquePublicationSlugAsync(db, slug, ct);

        var publication = new Publication
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Title = title,
            UrlName = slug,
            Description = request.Description?.Trim(),
            IsPublic = request.IsPublic,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };

        db.Publications.Add(publication);
        await db.SaveChangesAsync(ct);

        return Ok(new PublicationRecord(
            publication.Id, organizationId, publication.Title, publication.UrlName,
            publication.Description, publication.IsPublic, 0, 0, 0, publication.DateCreated));
    }

    /// <summary>Renames a publication, or changes whether it is public. The address does not move.</summary>
    [HttpPut("{publicationId:guid}")]
    public async Task<IActionResult> Update(
        Guid organizationId, Guid publicationId,
        [FromBody] SavePublicationRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        var publication = await db.Publications
            .FirstOrDefaultAsync(p => p.Id == publicationId && p.OrganizationId == organizationId, ct);
        if (publication is null) return NotFound();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("A publication needs a name.");

        publication.Title = title;
        publication.Description = request.Description?.Trim();
        publication.IsPublic = request.IsPublic;
        publication.DateUpdated = DateTime.UtcNow;
        publication.UpdatedByAppUserId = userId.Value;
        // UrlName is deliberately untouched. See Create.

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Deletes a publication — an empty one for a group administrator, any one for a SuperAdmin.
    /// </summary>
    /// <remarks>
    /// <para><b>Two tiers, because deleting a publication is two different acts.</b> Removing one
    /// that was created by mistake — wrong title, and the address never moves once set — costs
    /// nothing and nobody: no posts, nobody subscribed, nothing published. Removing one that
    /// people have written in and subscribed to destroys work and breaks links somebody shared,
    /// which is not a thing a group should be able to do with one click.</para>
    ///
    /// <para>So a group administrator may delete a publication that is <b>completely empty</b>,
    /// and a SuperAdmin may delete any of them — the escape hatch for the cases the first rule
    /// deliberately will not cover.</para>
    ///
    /// <para><b>The refusal names what is in the way</b> rather than answering 403. "It still has
    /// two posts in it" is something the person can act on; a bare refusal leaves them clicking
    /// the same button again. Subscriptions count even when cancelled: a cancelled subscription is
    /// still a record of somebody having read this, and the empty case is meant to mean nothing
    /// ever happened here.</para>
    ///
    /// <para>A SuperAdmin's delete takes the posts and subscriptions with it. That is what
    /// deleting a publication means, and leaving orphaned posts pointing at nothing would be
    /// worse than either outcome.</para>
    /// </remarks>
    [HttpDelete("{publicationId:guid}")]
    public async Task<IActionResult> Delete(
        Guid organizationId, Guid publicationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        var publication = await db.Publications
            .FirstOrDefaultAsync(p => p.Id == publicationId && p.OrganizationId == organizationId, ct);
        if (publication is null) return NotFound();

        var posts = await db.PublicationPosts
            .Where(p => p.PublicationId == publicationId).ToListAsync(ct);

        // Cancelled ones included — see the note above on what "empty" is meant to mean.
        var subscriptions = await db.PublicationSubscriptions
            .Where(s => s.PublicationId == publicationId).ToListAsync(ct);

        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin && (posts.Count > 0 || subscriptions.Count > 0))
            return Conflict(EmptinessRefusal(posts.Count, subscriptions.Count));

        db.PublicationPosts.RemoveRange(posts);
        db.PublicationSubscriptions.RemoveRange(subscriptions);
        db.Publications.Remove(publication);

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Says what is standing in the way, and what to do about it.
    /// </summary>
    /// <remarks>
    /// <para>The advice follows the blocker rather than being one fixed sentence. Telling somebody
    /// to delete the posts first when what stopped them was a subscriber sends them to look at an
    /// empty list and conclude the site is broken — and there is nothing they could do about a
    /// subscriber anyway, since the count includes people who have already unsubscribed.</para>
    ///
    /// <para>That case is not hypothetical: it is the only refusal a group administrator can
    /// actually reach, because the group's own listing counts live subscribers and the rule counts
    /// every subscription ever made. The publication looks empty on screen, the button is offered,
    /// and this sentence is the only explanation they will get.</para>
    /// </remarks>
    private static string EmptinessRefusal(int postCount, int subscriberCount)
    {
        var parts = new List<string>();
        if (postCount > 0)
            parts.Add(postCount == 1 ? "one post" : $"{postCount} posts");
        if (subscriberCount > 0)
            parts.Add(subscriberCount == 1
                ? "one person who has subscribed to it"
                : $"{subscriberCount} people who have subscribed to it");

        var advice = subscriberCount > 0
            // Nothing the group can do — a subscription is somebody else's, and it counts even
            // once cancelled.
            ? "Only a site administrator can remove it now."
            : "Delete the posts first, or ask a site administrator.";

        return $"This publication still has {string.Join(" and ", parts)}, "
             + $"and only a completely empty publication can be deleted here. {advice}";
    }

    // ── Posts ────────────────────────────────────────────────────────────────

    /// <summary>Every post in the publication, drafts included, newest first.</summary>
    [HttpGet("{publicationId:guid}/posts")]
    public async Task<ActionResult<IReadOnlyList<PublicationPostRecord>>> GetPosts(
        Guid organizationId, Guid publicationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        if (!await OwnsAsync(db, organizationId, publicationId, ct)) return NotFound();

        // Ordered on the entity before the projection. Ordering a projected record is not
        // translatable and fails at runtime rather than at compile time — a mistake made more than
        // once in this codebase already.
        var posts = await db.PublicationPosts.AsNoTracking()
            .Where(p => p.PublicationId == publicationId)
            .OrderByDescending(p => p.PublishedUtc ?? p.DateCreated)
            .Select(p => new PublicationPostRecord(
                p.Id, p.PublicationId, p.Title, p.UrlName, p.Excerpt, p.BodyHtml,
                p.PublishedUtc, p.RequiredTier, p.DateCreated, p.DateUpdated))
            .ToListAsync(ct);

        return Ok(posts);
    }

    /// <summary>Writes a new post. It starts as a draft.</summary>
    [HttpPost("{publicationId:guid}/posts")]
    public async Task<ActionResult<PublicationPostRecord>> CreatePost(
        Guid organizationId, Guid publicationId,
        [FromBody] SavePublicationPostRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Create, ct))
            return Forbid();

        if (!await OwnsAsync(db, organizationId, publicationId, ct)) return NotFound();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("A post needs a title.");

        var slug = UrlSlug.From(title);
        if (slug is null) return BadRequest("That title has no letters or digits to make an address from.");

        slug = await UniquePostSlugAsync(db, publicationId, slug, ct);

        var post = new PublicationPost
        {
            Id = Guid.NewGuid(),
            PublicationId = publicationId,
            Title = title,
            UrlName = slug,
            Excerpt = request.Excerpt?.Trim(),
            // Sanitised on the way in. Cleaning on render instead would leave every future reader
            // one forgotten call site away from whatever was submitted.
            BodyHtml = _sanitizer.SanitizeHtml(request.BodyHtml) ?? string.Empty,
            PublishedUtc = null,          // a draft, until somebody publishes it
            RequiredTier = null,          // free; see the entity for why the column exists
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };

        db.PublicationPosts.Add(post);
        await db.SaveChangesAsync(ct);

        return Ok(new PublicationPostRecord(
            post.Id, publicationId, post.Title, post.UrlName, post.Excerpt, post.BodyHtml,
            null, null, post.DateCreated, null));
    }

    /// <summary>Edits a post. Its address does not move, published or not.</summary>
    [HttpPut("{publicationId:guid}/posts/{postId:guid}")]
    public async Task<IActionResult> UpdatePost(
        Guid organizationId, Guid publicationId, Guid postId,
        [FromBody] SavePublicationPostRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        var post = await FindPostAsync(db, organizationId, publicationId, postId, ct);
        if (post is null) return NotFound();

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("A post needs a title.");

        post.Title = title;
        post.Excerpt = request.Excerpt?.Trim();
        post.BodyHtml = _sanitizer.SanitizeHtml(request.BodyHtml) ?? string.Empty;
        post.DateUpdated = DateTime.UtcNow;
        post.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Publishes a draft, or unpublishes a published post.
    /// </summary>
    /// <remarks>
    /// <para>Unpublishing is allowed, and it is worth being honest about what it does: the post
    /// stops being served, but it does not un-send anything. Anyone who already read it still read
    /// it, and a link they shared now leads nowhere. It is a way to withdraw something published in
    /// error, not a way to unpublish history.</para>
    ///
    /// <para>Publishing again after unpublishing sets a <b>new</b> date. The alternative — keeping
    /// the original — would put a post back in the middle of a chronological list where nobody
    /// looking at the top would see it had returned.</para>
    /// </remarks>
    [HttpPost("{publicationId:guid}/posts/{postId:guid}/publish")]
    public async Task<IActionResult> SetPublished(
        Guid organizationId, Guid publicationId, Guid postId,
        [FromQuery] bool published, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        var post = await FindPostAsync(db, organizationId, publicationId, postId, ct);
        if (post is null) return NotFound();

        post.PublishedUtc = published ? DateTime.UtcNow : null;
        post.DateUpdated = DateTime.UtcNow;
        post.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Deletes a post outright.</summary>
    /// <remarks>
    /// Deletion rather than hiding, unlike a feed post: nothing points at a publication post but
    /// its own address, there are no replies hanging off it and no moderation record to preserve.
    /// The group owns what it wrote.
    /// </remarks>
    [HttpDelete("{publicationId:guid}/posts/{postId:guid}")]
    public async Task<IActionResult> DeletePost(
        Guid organizationId, Guid publicationId, Guid postId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await PublicationsEnabledAsync(db, ct)) return NotFound();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.Organization, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        var post = await FindPostAsync(db, organizationId, publicationId, postId, ct);
        if (post is null) return NotFound();

        db.PublicationPosts.Remove(post);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    /// <summary>Whether this publication belongs to this organisation.</summary>
    /// <remarks>
    /// Checked separately from the permission: holding rights at one group must not let somebody
    /// edit another group's publication by naming its id. The "broken ID chain" the security audit
    /// found across nine controllers was exactly this shape.
    /// </remarks>
    private static Task<bool> OwnsAsync(
        BenDataContext db, Guid organizationId, Guid publicationId, CancellationToken ct)
        => db.Publications.AsNoTracking()
             .AnyAsync(p => p.Id == publicationId && p.OrganizationId == organizationId, ct);

    private static async Task<PublicationPost?> FindPostAsync(
        BenDataContext db, Guid organizationId, Guid publicationId, Guid postId, CancellationToken ct)
    {
        if (!await OwnsAsync(db, organizationId, publicationId, ct)) return null;

        return await db.PublicationPosts
            .FirstOrDefaultAsync(p => p.Id == postId && p.PublicationId == publicationId, ct);
    }

    /// <summary>A site-unique publication slug, suffixed if the plain one is taken.</summary>
    private static async Task<string> UniquePublicationSlugAsync(
        BenDataContext db, string slug, CancellationToken ct)
    {
        if (!await db.Publications.AnyAsync(p => p.UrlName == slug, ct)) return slug;

        for (var suffix = 2; suffix <= 500; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!await db.Publications.AnyAsync(p => p.UrlName == candidate, ct)) return candidate;
        }

        // Bounded rather than looping for ever: an unbounded search here is a way to hang the
        // endpoint by creating a few hundred publications with one name.
        return $"{slug}-{Guid.NewGuid():N}"[..Math.Min(120, slug.Length + 9)];
    }

    /// <summary>A slug unique within its publication.</summary>
    private static async Task<string> UniquePostSlugAsync(
        BenDataContext db, Guid publicationId, string slug, CancellationToken ct)
    {
        if (!await db.PublicationPosts.AnyAsync(p => p.PublicationId == publicationId && p.UrlName == slug, ct))
            return slug;

        for (var suffix = 2; suffix <= 500; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!await db.PublicationPosts.AnyAsync(p => p.PublicationId == publicationId && p.UrlName == candidate, ct))
                return candidate;
        }

        return $"{slug}-{Guid.NewGuid():N}"[..Math.Min(160, slug.Length + 9)];
    }
}
