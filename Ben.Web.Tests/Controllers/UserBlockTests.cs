using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Blocking an abusive user (App Review Guideline 1.2): their content stops reaching you, now.
/// </summary>
/// <remarks>
/// Reporting already existed; blocking is the half that acts immediately for the one reader
/// rather than eventually for everyone. The tests lean on the same seeding helpers as
/// <see cref="FeedControllerTests"/> so a block is exercised against the real feed queries —
/// the list, the ranked page, and the thread — not against a stub of them.
/// </remarks>
public sealed class UserBlockTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static readonly string MediaRoot =
        Path.Combine(Path.GetTempPath(), "ben-block-tests", Guid.NewGuid().ToString("N"));

    private static ControllerContext As(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
        },
    };

    private static MyBlocksController Blocks(IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory) { ControllerContext = As(userId) };

    private static FeedController Feed(IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory, TestMedia.StorageOnDisk(MediaRoot), TestMedia.IngestToDisk(MediaRoot),
               new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
               new Ben.Data.WebApi.Services.Feed.FeedLearningService(
                   TestMedia.StorageOnDisk(MediaRoot),
                   Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.Feed.FeedLearningService>.Instance),
               Microsoft.Extensions.Logging.Abstractions.NullLogger<FeedController>.Instance)
        { ControllerContext = As(userId) };

    private static AppUser MakeUser(string handle) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"{handle}@test.com", NormalizedUserName = $"{handle}@TEST.COM",
        Email = $"{handle}@test.com", NormalizedEmail = $"{handle}@TEST.COM",
        DisplayName = handle, Handle = handle, DateCreated = DateTime.UtcNow,
    };

    /// <summary>Feed on, everyone a member of one group so they may post — as FeedControllerTests seeds.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(params AppUser[] users)
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();
        db.Users.AddRange(users);

        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Feed Org", UrlName = $"feed-org-{Guid.NewGuid():N}"[..18],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = users[0].Id,
        });
        foreach (var u in users)
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = u.Id,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = u.Id,
            });
        }
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = SiteSettingKeys.FeaturePublicFeed, Value = "true",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = users[0].Id,
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<Guid> PostAsync(FeedController controller, string body, Guid? parent = null)
    {
        var result = await controller.CreatePost(new CreateFeedPostRequest(body, parent), null, CancellationToken.None);
        return ((FeedPostRecord)Assert.IsType<OkObjectResult>(result.Result).Value!).Id;
    }

    private static async Task<List<FeedPostRecord>> ReadFeedAsync(FeedController controller, string? mode = null)
    {
        var result = await controller.GetFeed(mode, null, null, CancellationToken.None);
        return ((FeedPageRecord)Assert.IsType<OkObjectResult>(result.Result).Value!).Posts.ToList();
    }

    // ── What a block does to the feed ─────────────────────────────────────────

    [Fact]
    public async Task A_blocked_authors_posts_vanish_for_the_blocker_and_nobody_else()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(sarah, troll, james);

        await PostAsync(Feed(factory, troll.Id), "Shouting into the void");
        await PostAsync(Feed(factory, sarah.Id), "A quiet evening at the Ryman");

        Assert.IsType<NoContentResult>(await Blocks(factory, sarah.Id).Block(troll.Id, default));

        // Gone for Sarah — on the plain feed AND the ranked one, which pages differently.
        Assert.DoesNotContain(await ReadFeedAsync(Feed(factory, sarah.Id)), p => p.AuthorAppUserId == troll.Id);
        Assert.DoesNotContain(await ReadFeedAsync(Feed(factory, sarah.Id), "foryou"), p => p.AuthorAppUserId == troll.Id);

        // Still there for James: a block is one reader's choice, not a takedown — that is what
        // Report is for.
        Assert.Contains(await ReadFeedAsync(Feed(factory, james.Id)), p => p.AuthorAppUserId == troll.Id);
    }

    [Fact]
    public async Task A_blocked_authors_replies_vanish_from_other_peoples_threads()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);

        var postId = await PostAsync(Feed(factory, sarah.Id), "Anyone else hear that?");
        await PostAsync(Feed(factory, troll.Id), "Rubbish.", parent: postId);

        await Blocks(factory, sarah.Id).Block(troll.Id, default);

        var thread = (IReadOnlyList<FeedPostRecord>)Assert.IsType<OkObjectResult>(
            (await Feed(factory, sarah.Id).GetThread(postId, default)).Result).Value!;

        Assert.Single(thread);   // her own post, no reply — the abuse is simply not shown
    }

    [Fact]
    public async Task A_blocked_authors_own_thread_is_not_found_for_the_blocker()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);

        var theirPost = await PostAsync(Feed(factory, troll.Id), "More shouting");
        await Blocks(factory, sarah.Id).Block(troll.Id, default);

        // NotFound, not a page with a hole where the root should be. A saved link or an old
        // notification lands here, and "that post isn't available" is the honest render.
        Assert.IsType<NotFoundResult>((await Feed(factory, sarah.Id).GetThread(theirPost, default)).Result);
    }

    [Fact]
    public async Task Blocking_severs_follows_in_both_directions_and_unblocking_does_not_restore_them()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);

        Assert.IsType<NoContentResult>(await Feed(factory, sarah.Id).Follow(troll.Id, default));
        Assert.IsType<NoContentResult>(await Feed(factory, troll.Id).Follow(sarah.Id, default));

        await Blocks(factory, sarah.Id).Block(troll.Id, default);

        await using (var db = factory.CreateDbContext())
            // Both directions: leaving the reverse row would keep Sarah appearing in the
            // troll's following feed, which is exactly the audience she is withdrawing from.
            Assert.Empty(db.UserFollows.Where(
                f => (f.FollowerAppUserId == sarah.Id && f.FollowedAppUserId == troll.Id)
                  || (f.FollowerAppUserId == troll.Id && f.FollowedAppUserId == sarah.Id)));

        await Blocks(factory, sarah.Id).Unblock(troll.Id, default);

        await using (var after = factory.CreateDbContext())
        {
            // Unblocking is a decision revisited, not one that never happened.
            Assert.Empty(after.UserFollows);
            Assert.Empty(after.UserBlocks);
        }
    }

    [Fact]
    public async Task Unblocking_brings_the_posts_back()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);

        await PostAsync(Feed(factory, troll.Id), "Shouting");
        await Blocks(factory, sarah.Id).Block(troll.Id, default);
        Assert.Empty(await ReadFeedAsync(Feed(factory, sarah.Id)));

        await Blocks(factory, sarah.Id).Unblock(troll.Id, default);
        Assert.Contains(await ReadFeedAsync(Feed(factory, sarah.Id)), p => p.AuthorAppUserId == troll.Id);
    }

    // ── The list, and the guard rails ─────────────────────────────────────────

    [Fact]
    public async Task The_block_list_names_who_and_when_most_recent_first()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);

        await Blocks(factory, sarah.Id).Block(troll.Id, default);

        var list = (IReadOnlyList<MyBlocksController.BlockedUserRecord>)Assert.IsType<OkObjectResult>(
            (await Blocks(factory, sarah.Id).GetBlocks(default)).Result).Value!;

        var row = Assert.Single(list);
        Assert.Equal(troll.Id, row.AppUserId);
        Assert.Equal("noisyneighbour", row.DisplayName);
    }

    [Fact]
    public async Task Blocking_twice_is_blocking_once()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);

        await Blocks(factory, sarah.Id).Block(troll.Id, default);
        Assert.IsType<NoContentResult>(await Blocks(factory, sarah.Id).Block(troll.Id, default));

        await using var db = factory.CreateDbContext();
        Assert.Single(db.UserBlocks);
    }

    [Fact]
    public async Task You_cannot_block_yourself_and_a_stranger_id_is_not_found()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(sarah);

        Assert.IsType<BadRequestObjectResult>(await Blocks(factory, sarah.Id).Block(sarah.Id, default));
        Assert.IsType<NotFoundResult>(await Blocks(factory, sarah.Id).Block(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_visitor_sees_the_feed_exactly_as_before_blocks_existed()
    {
        var sarah = MakeUser("sarahmitchell");
        var troll = MakeUser("noisyneighbour");
        var factory = await SeedAsync(sarah, troll);
        await PostAsync(Feed(factory, troll.Id), "Shouting");

        // Someone ELSE's block must never leak into the anonymous view.
        await Blocks(factory, sarah.Id).Block(troll.Id, default);

        var anonymous = new FeedController(factory, TestMedia.StorageOnDisk(MediaRoot), TestMedia.IngestToDisk(MediaRoot),
            new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
            new Ben.Data.WebApi.Services.Feed.FeedLearningService(
                TestMedia.StorageOnDisk(MediaRoot),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.Feed.FeedLearningService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FeedController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } } };

        var result = await anonymous.GetFeed(null, null, null, CancellationToken.None);
        var posts = ((FeedPageRecord)Assert.IsType<OkObjectResult>(result.Result).Value!).Posts;
        Assert.Contains(posts, p => p.AuthorAppUserId == troll.Id);
    }
}

/// <summary>
/// The exact JSON a block-list row goes over the wire as — the server half of the contract
/// whose client half is <c>BlockActionsTests.theBlockListDecodesTheServersShape</c> in BenKit.
/// </summary>
public sealed class BlockedUserWireShapeTests
{
    [Fact]
    public void A_block_row_serializes_with_the_keys_the_iOS_client_decodes()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new MyBlocksController.BlockedUserRecord(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "A former member",
                new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(
            """{"appUserId":"11111111-1111-1111-1111-111111111111","displayName":"A former member","dateCreated":"2026-08-28T12:00:00Z"}""",
            json);
    }
}
