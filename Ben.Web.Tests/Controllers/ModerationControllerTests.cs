using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Authorization;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The moderator's desk (item 186 F5): who may open it, and what deciding actually does.
/// </summary>
/// <remarks>
/// The claim these tests exist to protect is Ben's: nothing a member posts reaches the public feed
/// until somebody or something has looked at it. Everything else here — who holds the role, what a
/// decision records — is in service of that one.
/// </remarks>
public sealed class ModerationControllerTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ModerationController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new ModerationController(factory, new ManualReviewScreener());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid PostId, Guid AuthorId)>
        SeedPostWithMediaAsync(FeedMediaReviewState state = FeedMediaReviewState.Pending)
    {
        var factory = CreateFactory();
        Guid authorId = Guid.NewGuid(), postId = Guid.NewGuid(), fileId = Guid.NewGuid();

        await using var db = factory.CreateDbContext();
        db.Users.Add(new AppUser
        {
            Id = authorId, UserName = "a@t.com", NormalizedUserName = "A@T.COM",
            Email = "a@t.com", NormalizedEmail = "A@T.COM", DisplayName = "Sarah Mitchell",
            DateCreated = DateTime.UtcNow,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = authorId,
            FileName = "porch.jpg", StoredFileName = "porch.jpg", ContentType = "image/jpeg",
            FileSize = 10, StoragePath = "/tmp/porch.jpg", IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.OrgMessages.Add(new OrgMessage
        {
            Id = postId, AuthorAppUserId = authorId, ChannelType = OrgMessageChannel.PublicFeed,
            Body = "The landing at 3am.", IsPublic = true,
            MediaUploadFileId = fileId, MediaReviewState = state,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        await db.SaveChangesAsync();

        return (factory, postId, authorId);
    }

    // ── Who may moderate ──────────────────────────────────────────────────────

    /// <summary>
    /// The moderation surface is behind the moderator policy, not the administrator one.
    /// </summary>
    /// <remarks>
    /// Structural, because the failure it guards against is silent: an endpoint added here without
    /// an attribute is open to every signed-in account, and nothing else in the suite would notice.
    /// </remarks>
    [Fact]
    public void Every_moderation_endpoint_is_behind_the_moderator_policy()
    {
        var classPolicy = typeof(ModerationController)
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .ToList();

        Assert.Contains(AuthPolicyNames.Moderator, classPolicy);

        var unguarded = typeof(ModerationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            .Select(m => m.Name)
            .ToList();

        Assert.True(unguarded.Count == 0,
            "These moderation endpoints are anonymous, which would publish unscreened media to "
            + "anybody who guessed the URL: " + string.Join(", ", unguarded));
    }

    /// <summary>
    /// A SuperAdmin moderates without holding a second role.
    /// </summary>
    /// <remarks>
    /// Pinned in both places at once — the server handler and the token parser the browser uses —
    /// because two answers to "may this person moderate" would drift, and the visible symptom
    /// would be a menu item that leads to a 403.
    /// </remarks>
    [Fact]
    public void A_super_admin_satisfies_the_moderator_rule_implicitly()
    {
        Assert.Contains(RoleNames.SuperAdmin, RoleNames.Moderators);
        Assert.Contains(RoleNames.Moderator, RoleNames.Moderators);

        // Admin deliberately does NOT moderate: that role grants almost nothing by design, and
        // widening it here would be the unreviewed privilege expansion its own remarks warn about.
        Assert.DoesNotContain(RoleNames.Admin, RoleNames.Moderators);
    }

    [Fact]
    public async Task The_moderator_handler_accepts_both_roles_and_refuses_everybody_else()
    {
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.Moderator })
        {
            var context = Context(role);
            await new ModeratorHandler(UserManagerStub()).HandleAsync(context);
            Assert.True(context.HasSucceeded, $"{role} should satisfy the moderator requirement");
        }

        var ordinary = Context(RoleNames.Admin);
        await new ModeratorHandler(UserManagerStub()).HandleAsync(ordinary);
        Assert.False(ordinary.HasSucceeded, "Admin must not moderate by virtue of being Admin");

        // Never consulted for the role paths above: the handler answers from the principal's own
        // claims first, and only falls back to the database for Entra callers, who have none.
        static UserManager<AppUser> UserManagerStub()
            => new Mock<UserManager<AppUser>>(
                   Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!).Object;

        static AuthorizationHandlerContext Context(string role)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, role),
                 new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));
            return new AuthorizationHandlerContext([new ModeratorRequirement()], principal, null);
        }
    }

    // ── The queue ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_queue_shows_what_is_waiting_with_the_words_it_was_posted_under()
    {
        var (factory, postId, _) = await SeedPostWithMediaAsync();

        var result = await Build(factory, Guid.NewGuid())
            .GetFeedMedia(null, CancellationToken.None);
        var items = (IReadOnlyList<FeedMediaReviewItem>)Assert.IsType<OkObjectResult>(result.Result).Value!;

        var item = Assert.Single(items);
        Assert.Equal(postId, item.PostId);
        Assert.Equal("Sarah Mitchell", item.AuthorDisplayName);
        Assert.Equal("The landing at 3am.", item.Body);
        Assert.Equal(FeedMediaKind.Image, item.Kind);
    }

    [Fact]
    public async Task Approving_publishes_it_and_records_who_decided()
    {
        var (factory, postId, _) = await SeedPostWithMediaAsync();
        var moderatorId = Guid.NewGuid();

        var result = await Build(factory, moderatorId).ReviewFeedMedia(
            postId, new ReviewFeedMediaRequest(Approve: true, "Looks like a hallway."),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        await using var db = factory.CreateDbContext();
        var post = await db.OrgMessages.SingleAsync(m => m.Id == postId);
        Assert.Equal(FeedMediaReviewState.Approved, post.MediaReviewState);
        Assert.Equal(moderatorId, post.MediaReviewedByAppUserId);
        Assert.NotNull(post.MediaReviewedUtc);
        Assert.Equal("Looks like a hallway.", post.MediaReviewNote);
    }

    /// <summary>Holding keeps the file. Moderating is not deleting.</summary>
    [Fact]
    public async Task Holding_keeps_the_file_and_can_be_undone()
    {
        var (factory, postId, _) = await SeedPostWithMediaAsync();
        var moderatorId = Guid.NewGuid();

        await Build(factory, moderatorId).ReviewFeedMedia(
            postId, new ReviewFeedMediaRequest(Approve: false), CancellationToken.None);

        await using (var db = factory.CreateDbContext())
        {
            var post = await db.OrgMessages.SingleAsync(m => m.Id == postId);
            Assert.Equal(FeedMediaReviewState.Held, post.MediaReviewState);
            // The file is still there: a decision can be revisited, and a mistake undone.
            Assert.NotNull(post.MediaUploadFileId);
            Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == post.MediaUploadFileId));
        }

        await Build(factory, moderatorId).ReviewFeedMedia(
            postId, new ReviewFeedMediaRequest(Approve: true), CancellationToken.None);

        await using (var check = factory.CreateDbContext())
            Assert.Equal(FeedMediaReviewState.Approved,
                (await check.OrgMessages.SingleAsync(m => m.Id == postId)).MediaReviewState);
    }

    [Fact]
    public async Task The_summary_counts_each_pile_and_reports_that_screening_is_manual()
    {
        var (factory, _, _) = await SeedPostWithMediaAsync();

        var result = await Build(factory, Guid.NewGuid()).GetSummary(CancellationToken.None);
        var summary = (FeedModerationSummary)Assert.IsType<OkObjectResult>(result.Result).Value!;

        Assert.Equal(1, summary.MediaAwaitingReview);
        Assert.Equal(0, summary.MediaHeld);
        // The honest answer while no classifier is configured — the queue depends on a person.
        Assert.False(summary.ScreeningIsAutomatic);
    }

    // ── The screener contract ─────────────────────────────────────────────────

    /// <summary>
    /// The shipped screener approves NOTHING by itself.
    /// </summary>
    /// <remarks>
    /// The obvious placeholder — wave everything through until a classifier arrives — is the one
    /// implementation that could put the site in the state Ben asked to avoid, and it would do it
    /// silently. This test is what stops somebody writing it.
    /// </remarks>
    [Fact]
    public async Task The_shipped_screener_never_approves_anything_on_its_own()
    {
        var screener = new ManualReviewScreener();

        var verdict = await screener.ScreenAsync("/tmp/anything.jpg", "image/jpeg", CancellationToken.None);

        Assert.NotEqual(FeedMediaReviewState.Approved, verdict.State);
        Assert.Equal(FeedMediaReviewState.Pending, verdict.State);
        Assert.False(screener.IsAutomatic);
    }
}
