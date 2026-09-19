using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Controllers.Publications;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Publications;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Publications: what a group may write, and — the part that matters — what a stranger may read.
/// </summary>
/// <remarks>
/// <para>Four properties get their own test each, because each is a distinct way this feature
/// could go wrong and none of them implies the others:</para>
///
/// <list type="bullet">
/// <item><b>A draft is invisible to the public.</b> The single worst failure available here: a
/// group's unfinished work in front of the world.</item>
/// <item><b>A private publication is invisible even when its posts are published.</b> Two gates,
/// both required.</item>
/// <item><b>A tiered body is withheld by the server</b>, not sent for a page to hide.</item>
/// <item><b>Everything 404s when the feature is off</b> — not 403, which would confirm it
/// exists.</item>
/// </list>
///
/// <para>The public controller is exercised with <b>no principal at all</b>. Signing a test in and
/// then reading a public page is how a feature passes its tests and is broken for every real
/// visitor: the author always sees what the visitor cannot.</para>
/// </remarks>
public sealed class PublicationControllerTests
{
    // ── Harness ──────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<IOrganizationSecurityService> GrantAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(true);
        return s;
    }

    private static Mock<IOrganizationSecurityService> DenyAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(false);
        return s;
    }

    private static OrgPublicationController BuildAuthoring(
        IDbContextFactory<BenDataContext> factory, Guid userId,
        Mock<IOrganizationSecurityService>? security = null)
    {
        // The real sanitizer. A mock that passed markup through would leave the sanitization test
        // proving nothing at all.
        var controller = new OrgPublicationController(
            factory, new Mock<IMapper>().Object, (security ?? GrantAll()).Object,
            new CmsMarkupSanitizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

        return controller;
    }

    /// <summary>The same authoring controller, with the SuperAdmin role on the principal.</summary>
    /// <remarks>
    /// The role goes on the claims rather than into the security service mock, because that is
    /// where the controller reads it — <c>IsCmsAuthorizedAsync</c> short-circuits on
    /// <c>User.IsInRole</c> before the service is consulted at all.
    /// </remarks>
    private static OrgPublicationController BuildSuperAdmin(
        IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory, new Mock<IMapper>().Object, GrantAll().Object, new CmsMarkupSanitizer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };

    /// <summary>The public controller, holding no identity whatsoever.</summary>
    private static PublicPublicationController BuildVisitor(IDbContextFactory<BenDataContext> factory)
        => new(factory)
        {
            // An empty ClaimsIdentity — not signed in, no name, no roles. This is the whole point
            // of the class: everything it answers, it answers to a stranger.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
            },
        };

    private static MySubscriptionController BuildSubscriber(
        IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    private static AppUser MakeUser(string handle) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"{handle}@test.com", NormalizedUserName = $"{handle}@TEST.COM",
        Email = $"{handle}@test.com", NormalizedEmail = $"{handle}@TEST.COM",
        DisplayName = handle, Handle = handle, DateCreated = DateTime.UtcNow,
    };

    /// <summary>A site with one group, one user, and publications switched on or off.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId)>
        SeedAsync(bool publicationsOn = true)
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();

        var user = MakeUser("author");
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "BenCo Paranormal", UrlName = "benco",
            DateCreated = DateTime.UtcNow,
        };

        db.Users.Add(user);
        db.Organizations.Add(org);

        // The flag defaults to OFF with no row, so an explicit row is what "a SuperAdmin turned it
        // on" actually looks like in production.
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(),
            Key = SiteSettingKeys.FeaturePublications,
            Value = publicationsOn ? "true" : "false",
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        });

        await db.SaveChangesAsync();
        return (factory, org.Id, user.Id);
    }

    private static async Task<PublicationRecord> CreatePublicationAsync(
        OrgPublicationController controller, Guid orgId, string title, bool isPublic)
    {
        var result = await controller.Create(
            orgId, new SavePublicationRequest(title, "Notes from the field", isPublic),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return (PublicationRecord)ok.Value!;
    }

    private static async Task<PublicationPostRecord> CreatePostAsync(
        OrgPublicationController controller, Guid orgId, Guid publicationId,
        string title, string body = "<p>Body.</p>", string? excerpt = "An excerpt.")
    {
        var result = await controller.CreatePost(
            orgId, publicationId, new SavePublicationPostRequest(title, excerpt, body),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return (PublicationPostRecord)ok.Value!;
    }

    private static async Task PublishAsync(
        OrgPublicationController controller, Guid orgId, Guid publicationId, Guid postId)
    {
        var result = await controller.SetPublished(
            orgId, publicationId, postId, published: true, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    // ── The gate that matters: drafts ────────────────────────────────────────

    /// <summary>
    /// A draft never reaches a visitor, by any of the three public routes.
    /// </summary>
    /// <remarks>
    /// All three routes are asserted rather than one, because they are three separate queries.
    /// Getting the filter right in the listing and wrong in the by-address read would serve the
    /// draft to anybody who guessed — or was told — its URL, which is exactly how a draft leaks.
    /// </remarks>
    [Fact]
    public async Task A_draft_is_invisible_to_a_visitor_on_every_public_route()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var draft = await CreatePostAsync(authoring, orgId, publication.Id, "Unfinished thoughts");

        Assert.Null(draft.PublishedUtc);   // creating never publishes

        // 1. The directory leaves out a publication whose only post is a draft.
        var directory = Assert.IsType<OkObjectResult>(
            (await visitor.GetAll(CancellationToken.None)).Result);
        Assert.Empty((IReadOnlyList<PublicPublicationRecord>)directory.Value!);

        // 2. The publication's own page lists no posts.
        var page = Assert.IsType<OkObjectResult>(
            (await visitor.Get(publication.UrlName, CancellationToken.None)).Result);
        Assert.Empty(((PublicPublicationDetail)page.Value!).Posts);

        // 3. The draft's own address does not answer.
        Assert.IsType<NotFoundResult>(
            (await visitor.GetPost(publication.UrlName, draft.UrlName, CancellationToken.None)).Result);
    }

    /// <summary>Publishing makes exactly that post visible, and nothing else changes.</summary>
    [Fact]
    public async Task Publishing_a_post_makes_it_readable_by_a_visitor()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var published = await CreatePostAsync(authoring, orgId, publication.Id, "The Ridgeway house");
        var stillDraft = await CreatePostAsync(authoring, orgId, publication.Id, "Not ready");

        await PublishAsync(authoring, orgId, publication.Id, published.Id);

        var page = Assert.IsType<OkObjectResult>(
            (await visitor.Get(publication.UrlName, CancellationToken.None)).Result);
        var posts = ((PublicPublicationDetail)page.Value!).Posts;

        var only = Assert.Single(posts);
        Assert.Equal("The Ridgeway house", only.Title);

        Assert.IsType<NotFoundResult>(
            (await visitor.GetPost(publication.UrlName, stillDraft.UrlName, CancellationToken.None)).Result);
    }

    /// <summary>
    /// A private publication stays private even when its posts are published.
    /// </summary>
    /// <remarks>
    /// The second gate, and an independent one: a group drafting in the open should be able to
    /// publish posts internally and decide separately when the publication itself goes live.
    /// </remarks>
    [Fact]
    public async Task A_published_post_in_a_private_publication_is_still_invisible()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Internal", isPublic: false);
        var post = await CreatePostAsync(authoring, orgId, publication.Id, "Ready to go");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        Assert.IsType<NotFoundResult>(
            (await visitor.Get(publication.UrlName, CancellationToken.None)).Result);

        Assert.IsType<NotFoundResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);

        var directory = Assert.IsType<OkObjectResult>(
            (await visitor.GetAll(CancellationToken.None)).Result);
        Assert.Empty((IReadOnlyList<PublicPublicationRecord>)directory.Value!);
    }

    /// <summary>The author, meanwhile, sees their drafts — with the counts kept apart.</summary>
    [Fact]
    public async Task The_author_sees_drafts_counted_separately_from_published_posts()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var first = await CreatePostAsync(authoring, orgId, publication.Id, "One");
        await CreatePostAsync(authoring, orgId, publication.Id, "Two");
        await PublishAsync(authoring, orgId, publication.Id, first.Id);

        var listing = Assert.IsType<OkObjectResult>(
            (await authoring.GetAll(orgId, CancellationToken.None)).Result);

        var record = Assert.Single((IReadOnlyList<PublicationRecord>)listing.Value!);
        Assert.Equal(1, record.PublishedPostCount);
        Assert.Equal(1, record.DraftPostCount);
    }

    // ── The paywall that does not exist yet ──────────────────────────────────

    /// <summary>
    /// A tiered post's body is withheld by the server; the excerpt is all a visitor gets.
    /// </summary>
    /// <remarks>
    /// <para>Nothing writes a tier today, so the tier is set directly on the row here. That is the
    /// reason to have the test at all: the withholding path has no other way of being exercised
    /// until billing exists, and an untested path that will one day be the only thing between a
    /// paid article and the public is not a path worth having.</para>
    ///
    /// <para>The assertion is that <c>BodyHtml</c> is <b>null</b>, not that some flag is set. A
    /// body that arrives and is hidden has arrived.</para>
    /// </remarks>
    [Fact]
    public async Task A_tiered_post_arrives_without_its_body()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(
            authoring, orgId, publication.Id, "For subscribers",
            body: "<p>The paid part.</p>", excerpt: "Only this much is free.");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        await using (var db = factory.CreateDbContext())
        {
            var row = await db.PublicationPosts.FirstAsync(p => p.Id == post.Id);
            row.RequiredTier = 1;
            await db.SaveChangesAsync();
        }

        var result = Assert.IsType<OkObjectResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);
        var record = (PublicPublicationPostRecord)result.Value!;

        Assert.Null(record.BodyHtml);
        Assert.True(record.RequiresSubscription);
        Assert.Equal("Only this much is free.", record.Excerpt);
    }

    /// <summary>A free post — every post today — arrives with its body.</summary>
    /// <remarks>
    /// Without this the withholding test would pass just as well against a server that withheld
    /// every body, which would be a broken product with a green suite.
    /// </remarks>
    [Fact]
    public async Task A_free_post_arrives_with_its_body()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(
            authoring, orgId, publication.Id, "Open to all", body: "<p>The whole thing.</p>");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        var result = Assert.IsType<OkObjectResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);
        var record = (PublicPublicationPostRecord)result.Value!;

        Assert.False(record.RequiresSubscription);
        Assert.Contains("The whole thing.", record.BodyHtml);
    }

    /// <summary>A listing never carries bodies, free or not.</summary>
    [Fact]
    public async Task The_publication_page_lists_posts_without_their_bodies()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(
            authoring, orgId, publication.Id, "Open to all", body: "<p>The whole thing.</p>");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        var page = Assert.IsType<OkObjectResult>(
            (await visitor.Get(publication.UrlName, CancellationToken.None)).Result);

        var listed = Assert.Single(((PublicPublicationDetail)page.Value!).Posts);
        Assert.Null(listed.BodyHtml);
    }

    // ── Sanitising, and when it happens ──────────────────────────────────────

    /// <summary>
    /// A script in a submitted body is gone from what is stored, not merely from what is rendered.
    /// </summary>
    /// <remarks>
    /// The assertion reads the database row rather than the response, because sanitising at save
    /// time is the claim: the stored markup is the safe markup, so no future read path can
    /// resurrect what the author sent.
    /// </remarks>
    [Fact]
    public async Task A_script_in_a_body_never_reaches_the_database()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(
            authoring, orgId, publication.Id, "Nasty",
            body: "<p>Hello</p><script>alert('x')</script>");

        await using var db = factory.CreateDbContext();
        var stored = await db.PublicationPosts.FirstAsync(p => p.Id == post.Id);

        Assert.DoesNotContain("<script", stored.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", stored.BodyHtml);
    }

    // ── Addresses ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renaming a publication does not move its address.
    /// </summary>
    /// <remarks>
    /// Item 89's lesson, applied before it could be repeated: an address that regenerates on
    /// rename silently breaks every link anybody has shared.
    /// </remarks>
    [Fact]
    public async Task Renaming_a_publication_leaves_its_address_alone()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        Assert.Equal("field-notes", publication.UrlName);

        var updated = await authoring.Update(
            orgId, publication.Id,
            new SavePublicationRequest("Completely Different Name", null, true),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(updated);

        var listing = Assert.IsType<OkObjectResult>(
            (await authoring.GetAll(orgId, CancellationToken.None)).Result);
        var record = Assert.Single((IReadOnlyList<PublicationRecord>)listing.Value!);

        Assert.Equal("Completely Different Name", record.Title);
        Assert.Equal("field-notes", record.UrlName);
    }

    /// <summary>Two publications cannot claim the same address, even across different groups.</summary>
    [Fact]
    public async Task A_second_publication_with_the_same_title_gets_its_own_address()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var first = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var second = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);

        Assert.NotEqual(first.UrlName, second.UrlName);
        Assert.StartsWith("field-notes", second.UrlName);
    }

    // ── Subscribing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribing twice is one subscription, and re-subscribing after cancelling reuses the row.
    /// </summary>
    /// <remarks>
    /// <para>Asserted against the table rather than the API's answer. A second row would satisfy
    /// any reasonable-looking response while breaking the unique index in production — and the
    /// in-memory provider does not enforce unique indexes, so the count is the only thing standing
    /// in for the constraint here.</para>
    /// </remarks>
    [Fact]
    public async Task Subscribing_twice_leaves_one_subscription()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);

        var reader = MakeUser("reader");
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(reader);
            await db.SaveChangesAsync();
        }

        var subscriber = BuildSubscriber(factory, reader.Id);

        Assert.IsType<NoContentResult>(await subscriber.Subscribe(publication.UrlName, CancellationToken.None));
        Assert.IsType<NoContentResult>(await subscriber.Subscribe(publication.UrlName, CancellationToken.None));

        await using (var db = factory.CreateDbContext())
            Assert.Equal(1, await db.PublicationSubscriptions.CountAsync());

        // Cancelling marks rather than deletes — a cancelled subscription is what a payment would
        // have attached to, so it stays answerable for what it covered.
        Assert.IsType<NoContentResult>(await subscriber.Unsubscribe(publication.UrlName, CancellationToken.None));

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, await db.PublicationSubscriptions.CountAsync());
            Assert.NotNull((await db.PublicationSubscriptions.FirstAsync()).CancelledUtc);
        }

        // And re-subscribing revives that row rather than adding a second.
        Assert.IsType<NoContentResult>(await subscriber.Subscribe(publication.UrlName, CancellationToken.None));

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, await db.PublicationSubscriptions.CountAsync());
            Assert.Null((await db.PublicationSubscriptions.FirstAsync()).CancelledUtc);
        }
    }

    /// <summary>A cancelled subscription drops out of the reader's own list.</summary>
    [Fact]
    public async Task An_unsubscribed_publication_is_not_in_my_subscriptions()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);

        var reader = MakeUser("reader");
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(reader);
            await db.SaveChangesAsync();
        }

        var subscriber = BuildSubscriber(factory, reader.Id);
        await subscriber.Subscribe(publication.UrlName, CancellationToken.None);

        var before = Assert.IsType<OkObjectResult>(
            (await subscriber.GetMine(CancellationToken.None)).Result);
        Assert.Single((IReadOnlyList<MySubscriptionRecord>)before.Value!);

        await subscriber.Unsubscribe(publication.UrlName, CancellationToken.None);

        var after = Assert.IsType<OkObjectResult>(
            (await subscriber.GetMine(CancellationToken.None)).Result);
        Assert.Empty((IReadOnlyList<MySubscriptionRecord>)after.Value!);
    }

    /// <summary>
    /// A private publication cannot be subscribed to, even by exact address.
    /// </summary>
    /// <remarks>
    /// Otherwise a guessed or leaked URL name would let somebody attach themselves to something the
    /// group has never shown anyone — and then be notified about it.
    /// </remarks>
    [Fact]
    public async Task A_private_publication_cannot_be_subscribed_to()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var publication = await CreatePublicationAsync(authoring, orgId, "Internal", isPublic: false);

        var reader = MakeUser("reader");
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(reader);
            await db.SaveChangesAsync();
        }

        var subscriber = BuildSubscriber(factory, reader.Id);

        Assert.IsType<NotFoundResult>(
            await subscriber.Subscribe(publication.UrlName, CancellationToken.None));

        await using (var db = factory.CreateDbContext())
            Assert.Equal(0, await db.PublicationSubscriptions.CountAsync());
    }

    // ── Deleting ─────────────────────────────────────────────────────────────

    /// <summary>An empty publication — the "wrong title, start again" case — deletes cleanly.</summary>
    [Fact]
    public async Task An_empty_publication_can_be_deleted_by_the_group()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var publication = await CreatePublicationAsync(authoring, orgId, "Typo Notes", isPublic: false);

        Assert.IsType<NoContentResult>(
            await authoring.Delete(orgId, publication.Id, CancellationToken.None));

        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.Publications.CountAsync());
    }

    /// <summary>
    /// A publication with posts is refused, and the refusal says so.
    /// </summary>
    /// <remarks>
    /// <para>The message is asserted, not just the status. A guard whose reason the UI cannot show
    /// leaves somebody clicking the same button — "it still has one post in it" is the difference
    /// between a rule and a wall.</para>
    ///
    /// <para>The row surviving is asserted separately: a refusal that returned 409 after already
    /// deleting the thing would satisfy a status-only test.</para>
    /// </remarks>
    [Fact]
    public async Task A_publication_with_posts_is_refused_with_a_reason()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        await CreatePostAsync(authoring, orgId, publication.Id, "Something written");

        var refusal = Assert.IsType<ConflictObjectResult>(
            await authoring.Delete(orgId, publication.Id, CancellationToken.None));

        Assert.Contains("one post", (string)refusal.Value!);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.Publications.CountAsync());
    }

    /// <summary>
    /// A cancelled subscription still blocks a group's delete.
    /// </summary>
    /// <remarks>
    /// The empty rule means "nothing ever happened here", and somebody having subscribed and left
    /// is something having happened. It is also the case the UI cannot see — the group's listing
    /// counts only live subscribers — which is exactly why the server has to be the one deciding.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_subscription_still_blocks_the_group_from_deleting()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);

        var reader = MakeUser("reader");
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(reader);
            await db.SaveChangesAsync();
        }

        var subscriber = BuildSubscriber(factory, reader.Id);
        await subscriber.Subscribe(publication.UrlName, CancellationToken.None);
        await subscriber.Unsubscribe(publication.UrlName, CancellationToken.None);

        var refusal = Assert.IsType<ConflictObjectResult>(
            await authoring.Delete(orgId, publication.Id, CancellationToken.None));

        Assert.Contains("subscribed to it", (string)refusal.Value!);

        // The advice has to match the blocker: "delete the posts first" sends somebody to look at
        // an empty list, and there is nothing they could do about a subscriber in any case.
        Assert.DoesNotContain("Delete the posts first", (string)refusal.Value!);
    }

    /// <summary>
    /// A SuperAdmin deletes anything, and the posts and subscriptions go with it.
    /// </summary>
    /// <remarks>
    /// The cascade is asserted rather than assumed. Removing the publication and leaving its posts
    /// behind would leave rows pointing at nothing — worse than either refusing or deleting.
    /// </remarks>
    [Fact]
    public async Task A_super_admin_deletes_a_full_publication_and_everything_in_it()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(authoring, orgId, publication.Id, "Published piece");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        var reader = MakeUser("reader");
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(reader);
            await db.SaveChangesAsync();
        }
        await BuildSubscriber(factory, reader.Id).Subscribe(publication.UrlName, CancellationToken.None);

        // The group itself is refused first, so what follows is the role and not the rule
        // quietly having stopped applying.
        Assert.IsType<ConflictObjectResult>(
            await authoring.Delete(orgId, publication.Id, CancellationToken.None));

        var superAdmin = BuildSuperAdmin(factory, userId);
        Assert.IsType<NoContentResult>(
            await superAdmin.Delete(orgId, publication.Id, CancellationToken.None));

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(0, await db.Publications.CountAsync());
            Assert.Equal(0, await db.PublicationPosts.CountAsync());
            Assert.Equal(0, await db.PublicationSubscriptions.CountAsync());
        }
    }

    /// <summary>A deleted publication's posts stop answering to a visitor.</summary>
    [Fact]
    public async Task A_deleted_publication_is_gone_from_the_public_side()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var visitor = BuildVisitor(factory);

        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(authoring, orgId, publication.Id, "Published piece");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        Assert.IsType<OkObjectResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);

        await BuildSuperAdmin(factory, userId).Delete(orgId, publication.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(
            (await visitor.Get(publication.UrlName, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);
    }

    /// <summary>Delete is 404 with the feature off, like everything else.</summary>
    [Fact]
    public async Task Deleting_is_404_when_publications_are_switched_off()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var authoring = BuildAuthoring(factory, userId);
        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: false);

        await using (var db = factory.CreateDbContext())
        {
            var setting = await db.SiteSettings.FirstAsync(s => s.Key == SiteSettingKeys.FeaturePublications);
            setting.Value = "false";
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>(
            await authoring.Delete(orgId, publication.Id, CancellationToken.None));
    }

    // ── Permissions ──────────────────────────────────────────────────────────

    /// <summary>Somebody without permission on the group cannot write in its name.</summary>
    [Fact]
    public async Task Without_permission_on_the_group_authoring_is_refused()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var denied = BuildAuthoring(factory, userId, DenyAll());

        Assert.IsType<ForbidResult>(
            (await denied.GetAll(orgId, CancellationToken.None)).Result);

        Assert.IsType<ForbidResult>(
            (await denied.Create(orgId, new SavePublicationRequest("Mine now", null, true),
                CancellationToken.None)).Result);
    }

    // ── The switch ───────────────────────────────────────────────────────────

    /// <summary>
    /// With the feature off, every route answers 404 — the authoring ones and the public ones.
    /// </summary>
    /// <remarks>
    /// 404 rather than 403 on purpose: a section a SuperAdmin has not switched on should look
    /// exactly like a section that was never built. 403 would confirm it exists and is merely
    /// barred, which is more than a disabled feature should say.
    /// </remarks>
    [Fact]
    public async Task Everything_is_404_when_publications_are_switched_off()
    {
        // Built with the flag on, so there is real content to hide — a test against an empty
        // database would pass against a server that simply had nothing to show.
        var (factory, orgId, userId) = await SeedAsync(publicationsOn: true);
        var authoring = BuildAuthoring(factory, userId);
        var publication = await CreatePublicationAsync(authoring, orgId, "Field Notes", isPublic: true);
        var post = await CreatePostAsync(authoring, orgId, publication.Id, "Live");
        await PublishAsync(authoring, orgId, publication.Id, post.Id);

        // Confirm it is genuinely readable first, so the assertions below mean the switch and not
        // some other failure.
        var visitor = BuildVisitor(factory);
        Assert.IsType<OkObjectResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);

        await using (var db = factory.CreateDbContext())
        {
            var setting = await db.SiteSettings.FirstAsync(s => s.Key == SiteSettingKeys.FeaturePublications);
            setting.Value = "false";
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await visitor.GetAll(CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await visitor.Get(publication.UrlName, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            (await visitor.GetPost(publication.UrlName, post.UrlName, CancellationToken.None)).Result);

        Assert.IsType<NotFoundResult>((await authoring.GetAll(orgId, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            (await authoring.GetPosts(orgId, publication.Id, CancellationToken.None)).Result);

        var subscriber = BuildSubscriber(factory, userId);
        Assert.IsType<NotFoundResult>((await subscriber.GetMine(CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await subscriber.Subscribe(publication.UrlName, CancellationToken.None));
    }

    /// <summary>
    /// A site that has never touched the setting has publications off.
    /// </summary>
    /// <remarks>
    /// The default lives at the read site, and no row is written at deploy — so "off unless
    /// switched on" is a property of the code, not of a seeded row somebody could forget.
    /// </remarks>
    [Fact]
    public async Task With_no_setting_row_at_all_publications_are_off()
    {
        var factory = CreateFactory();
        var visitor = BuildVisitor(factory);

        Assert.IsType<NotFoundResult>((await visitor.GetAll(CancellationToken.None)).Result);
    }
}
