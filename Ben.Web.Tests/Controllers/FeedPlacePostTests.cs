using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Posts about a place: ordinary feed posts carrying one, and the wider door they come through.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-17: a public location should be "actually public for adding files, messages
/// etc". Built as one nullable column on a feed post rather than a comment table of its own, so a
/// post about a place inherits the media screener, the day-long upload pause, reporting, hiding,
/// likes, replies, the moderator queues and the phone app's reader. The load-bearing tests here are
/// the two that hold the seam: <b>a residence takes no posts at all</b>, and <b>signing in is enough
/// to post about a place while the feed's front page still asks for more</b> — which is a deliberate
/// difference, not an oversight, and the only way to prove it is deliberate is to assert both
/// directions in one test.</para>
/// </remarks>
public sealed class FeedPlacePostTests
{
    private static readonly string MediaRoot =
        Path.Combine(Path.GetTempPath(), "ben-place-post-tests", Guid.NewGuid().ToString("N"));

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FeedController Feed(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, TestMedia.StorageOnDisk(MediaRoot), TestMedia.IngestToDisk(MediaRoot),
               new Ben.Data.WebApi.Services.Feed.ManualReviewScreener(),
               new Ben.Data.WebApi.Services.Feed.FeedLearningService(
                   TestMedia.StorageOnDisk(MediaRoot),
                   Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.Feed.FeedLearningService>.Instance),
               Microsoft.Extensions.Logging.Abstractions.NullLogger<FeedController>.Instance,
               Ben.Data.WebApi.Services.LinkPreviews.LinkPreviewWarmer.None)
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

    private static PublicPlaceController PlacePage(IDbContextFactory<BenDataContext> f, Guid readerId)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = readerId == Guid.Empty
                    ? new DefaultHttpContext()
                    : new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, readerId.ToString())], "Bearer")),
                    },
            },
        };

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid LandmarkId, Guid HomeId,
        Guid MemberId, Guid StrangerId);

    /// <summary>
    /// A landmark, a home, somebody in a group and somebody in none.
    /// </summary>
    /// <remarks>
    /// The stranger is the point of the fixture: no membership, no case, no client access. Under
    /// the feed's front-page rule they may not write a word, and under the place rule they may.
    /// </remarks>
    private static async Task<World> SeedAsync(bool feedOn = true)
    {
        var f = CreateFactory();
        var landmarkId = Guid.NewGuid();
        var homeId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = f.CreateDbContext();

        db.Users.Add(new AppUser
        {
            Id = memberId, UserName = "member@t.com", NormalizedUserName = "MEMBER@T.COM",
            Email = "member@t.com", NormalizedEmail = "MEMBER@T.COM",
            DisplayName = "A Member", Handle = "member", DateCreated = now,
        });
        db.Users.Add(new AppUser
        {
            Id = strangerId, UserName = "stranger@t.com", NormalizedUserName = "STRANGER@T.COM",
            Email = "stranger@t.com", NormalizedEmail = "STRANGER@T.COM",
            DisplayName = "A Visitor", Handle = "visitor", DateCreated = now,
        });

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Paranormal365", UrlName = "paranormal365",
            DateCreated = now, CreatedByAppUserId = memberId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = now, CreatedByAppUserId = memberId,
        });

        db.Places.Add(new Place
        {
            Id = landmarkId, Name = "Cragfont", City = "Castalian Springs", State = "TN",
            Latitude = 36.3839m, Longitude = -86.3281m, Kind = PlaceKind.PublicLocation,
            DateCreated = now, CreatedByAppUserId = memberId,
        });
        db.Places.Add(new Place
        {
            Id = homeId, StreetAddress1 = "9 Quiet Lane", City = "Nashville", State = "TN",
            Kind = PlaceKind.PrivateResidence,
            DateCreated = now, CreatedByAppUserId = memberId,
        });

        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = SiteSettingKeys.FeaturePublicFeed,
            Value = feedOn ? "true" : "false",
            DateCreated = now, CreatedByAppUserId = memberId,
        });

        await db.SaveChangesAsync();
        return new World(f, landmarkId, homeId, memberId, strangerId);
    }

    private static Task<ActionResult<FeedPostRecord>> PostAsync(
        FeedController feed, string body, Guid? placeId = null, Guid? parent = null)
        => feed.CreatePost(
            new CreateFeedPostRequest(body, parent, PlaceId: placeId), null, CancellationToken.None);

    private static FeedPostRecord Posted(ActionResult<FeedPostRecord> result)
        => (FeedPostRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;

    private static string Refused(ActionResult<FeedPostRecord> result)
        => (string)Assert.IsType<BadRequestObjectResult>(result.Result).Value!;

    private static async Task<PublicPlaceResponse> PlaceAsync(World w, Guid placeId, Guid readerId)
    {
        var result = await PlacePage(w.Factory, readerId).GetById(placeId, default);
        return (PublicPlaceResponse)Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    /// <summary>
    /// The signed-in place controller, which is where "may you post" is actually answered.
    /// </summary>
    /// <remarks>
    /// The anonymous endpoint reports <c>CanPost: false</c> unconditionally, because the website
    /// calls it without a token and it therefore cannot know the reader. Asking it as somebody was
    /// a mistake this fixture made first: the browser showed a composer nobody could post with.
    /// </remarks>
    private static Ben.Data.WebApi.Controllers.Entities.PlaceController MyPlace(
        IDbContextFactory<BenDataContext> f, Guid readerId)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, readerId.ToString())], "Bearer")),
                },
            },
        };

    private static async Task<Ben.Data.WebApi.Controllers.Entities.PlacePostsRecord> MyPostsAsync(
        World w, Guid placeId, Guid readerId)
    {
        var result = await MyPlace(w.Factory, readerId).GetPosts(placeId, default);
        return (Ben.Data.WebApi.Controllers.Entities.PlacePostsRecord)
            Assert.IsType<OkObjectResult>(result.Result).Value!;
    }

    // ── The write door ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_post_about_a_place_carries_it_and_says_what_it_is_called()
    {
        var w = await SeedAsync();

        var post = Posted(await PostAsync(Feed(w.Factory, w.MemberId), "Cold spot on the stair", w.LandmarkId));

        Assert.Equal(w.LandmarkId, post.PlaceId);
        // Resolved at read, so renaming the place renames it everywhere at once.
        Assert.Equal("Cragfont", post.AboutPlaceName);
    }

    /// <summary>
    /// Publishing what happens inside somebody's home is theirs to agree to, and there is still no
    /// mechanism for asking — the same reason an investigation at a residence cannot be made public.
    /// </summary>
    [Fact]
    public async Task A_post_about_somebodys_home_is_refused()
    {
        var w = await SeedAsync();

        var said = Refused(await PostAsync(Feed(w.Factory, w.MemberId), "Anything", w.HomeId));

        Assert.Contains("somebody's home", said);
    }

    [Fact]
    public async Task A_post_about_a_place_that_is_not_there_is_not_found()
    {
        var w = await SeedAsync();

        var result = await PostAsync(Feed(w.Factory, w.MemberId), "Anything", Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    /// <summary>
    /// The seam, asserted in both directions at once. Signing in is enough to write about a public
    /// location; the feed's front page still asks for a group or a case. Either half alone would
    /// read as a bug in the other.
    /// </summary>
    [Fact]
    public async Task Signing_in_is_enough_for_a_place_but_not_for_the_front_page()
    {
        var w = await SeedAsync();
        var stranger = Feed(w.Factory, w.StrangerId);

        var aboutThePlace = Posted(await PostAsync(stranger, "I went on the evening tour", w.LandmarkId));
        Assert.Equal(w.LandmarkId, aboutThePlace.PlaceId);

        var onTheFrontPage = Refused(await PostAsync(stranger, "Hello everybody"));
        Assert.Contains("people who belong here", onTheFrontPage);
    }

    /// <summary>
    /// A reply is judged by the thread it joins, not by what its caller claims. So it inherits the
    /// parent's place — which is also what lets somebody with no group answer a question asked on a
    /// place's page.
    /// </summary>
    [Fact]
    public async Task A_reply_inherits_the_place_of_the_post_it_answers()
    {
        var w = await SeedAsync();
        var parent = Posted(await PostAsync(Feed(w.Factory, w.MemberId), "Cold spot on the stair", w.LandmarkId));

        // Sends no place of its own, and is from somebody the front page would refuse.
        var reply = Posted(await PostAsync(Feed(w.Factory, w.StrangerId), "We felt that too", parent: parent.Id));

        Assert.Equal(w.LandmarkId, reply.PlaceId);
    }

    /// <summary>A reply cannot smuggle itself onto a different place than its thread.</summary>
    [Fact]
    public async Task A_reply_cannot_claim_a_different_place()
    {
        var w = await SeedAsync();
        var parent = Posted(await PostAsync(Feed(w.Factory, w.MemberId), "On the stair", w.LandmarkId));

        var reply = Posted(await PostAsync(
            Feed(w.Factory, w.MemberId), "Nothing to do with it", placeId: w.HomeId, parent: parent.Id));

        Assert.Equal(w.LandmarkId, reply.PlaceId);
    }

    // ── The read paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_feed_can_be_narrowed_to_one_place()
    {
        var w = await SeedAsync();
        var member = Feed(w.Factory, w.MemberId);
        await PostAsync(member, "About the landmark", w.LandmarkId);
        await PostAsync(member, "About nothing in particular");

        var result = await member.GetFeed(null, null, null, CancellationToken.None, place: w.LandmarkId);
        var page = (FeedPageRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;

        Assert.Equal("About the landmark", Assert.Single(page.Posts).Body);
    }

    /// <summary>An id nobody knows is an empty page, not an error: places get merged away.</summary>
    [Fact]
    public async Task An_unknown_place_filter_is_an_empty_page()
    {
        var w = await SeedAsync();
        await PostAsync(Feed(w.Factory, w.MemberId), "About the landmark", w.LandmarkId);

        var result = await Feed(w.Factory, w.MemberId)
            .GetFeed(null, null, null, CancellationToken.None, place: Guid.NewGuid());

        Assert.Empty(((FeedPageRecord)Assert.IsType<OkObjectResult>(result.Result).Value!).Posts);
    }

    /// <summary>
    /// A visitor reads a place's posts. Whether they may ADD one is a question the anonymous
    /// endpoint cannot answer, so it says no and the signed-in one answers properly.
    /// </summary>
    [Fact]
    public async Task The_places_page_carries_its_posts_for_anybody_reading()
    {
        var w = await SeedAsync();
        await PostAsync(Feed(w.Factory, w.MemberId), "Cold spot on the stair", w.LandmarkId);

        var visitor = await PlaceAsync(w, w.LandmarkId, Guid.Empty);
        Assert.Equal("Cold spot on the stair", Assert.Single(visitor.Posts!).Body);

        // Never true here, whoever asks: the website calls this without a token on purpose, so an
        // answer of "yes" could only ever be wrong for somebody.
        Assert.False(visitor.CanPost);
        Assert.False((await PlaceAsync(w, w.LandmarkId, w.StrangerId)).CanPost);
    }

    [Fact]
    public async Task Whether_you_may_post_is_answered_as_somebody()
    {
        var w = await SeedAsync();
        await PostAsync(Feed(w.Factory, w.MemberId), "Cold spot on the stair", w.LandmarkId);

        // Somebody in no group at all, which is the whole point of the wider door.
        var mine = await MyPostsAsync(w, w.LandmarkId, w.StrangerId);
        Assert.True(mine.CanPost);
        Assert.Single(mine.Posts);
    }

    [Fact]
    public async Task A_home_offers_nobody_the_box()
    {
        var w = await SeedAsync();

        Assert.False((await MyPostsAsync(w, w.HomeId, w.MemberId)).CanPost);
    }

    /// <summary>
    /// With the feed switched off there is no box and no posts — place posts are feed posts and
    /// follow its switch. Offering the box anyway is what the browser did until this was asked:
    /// a control that looks live, is clicked, and is refused by an endpoint that 404s.
    /// </summary>
    [Fact]
    public async Task With_the_feed_switched_off_a_place_takes_no_posts()
    {
        var w = await SeedAsync(feedOn: false);

        var mine = await MyPostsAsync(w, w.LandmarkId, w.MemberId);
        Assert.False(mine.CanPost);
        Assert.Empty(mine.Posts);

        Assert.Empty((await PlaceAsync(w, w.LandmarkId, Guid.Empty)).Posts!);
    }

    /// <summary>
    /// Hiding a post has to hide it here too. The place page reads through the feed's own
    /// visibility predicate rather than a query of its own, and this is what says so.
    /// </summary>
    [Fact]
    public async Task A_hidden_post_disappears_from_the_places_page()
    {
        var w = await SeedAsync();
        var post = Posted(await PostAsync(Feed(w.Factory, w.MemberId), "Hide me", w.LandmarkId));

        await using (var db = w.Factory.CreateDbContext())
        {
            var row = await db.OrgMessages.FirstAsync(m => m.Id == post.Id);
            row.HiddenUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.Empty((await PlaceAsync(w, w.LandmarkId, w.MemberId)).Posts!);
        Assert.Empty((await MyPostsAsync(w, w.LandmarkId, w.MemberId)).Posts);
    }

    /// <summary>
    /// A reply is read with the post it answers, in the feed and here alike — so the place page
    /// counts threads, not messages.
    /// </summary>
    [Fact]
    public async Task The_places_page_lists_threads_rather_than_every_reply()
    {
        var w = await SeedAsync();
        var member = Feed(w.Factory, w.MemberId);
        var parent = Posted(await PostAsync(member, "On the stair", w.LandmarkId));
        await PostAsync(member, "We felt that too", parent: parent.Id);

        var post = Assert.Single((await MyPostsAsync(w, w.LandmarkId, w.MemberId)).Posts);
        Assert.Equal("On the stair", post.Body);
        Assert.Equal(1, post.ReplyCount);
    }
}
