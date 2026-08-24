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
/// The public feed: who can see what, what a mention resolves to, and what a report does.
/// </summary>
/// <remarks>
/// <para>Three properties here are the ones that would matter if this were got wrong, and each has
/// its own test rather than being implied by another: <b>the feed 404s wholesale when switched
/// off</b>, <b>a hidden post disappears from every read path</b>, and <b>a report never hides
/// anything by itself</b>.</para>
///
/// <para>The parser's own behaviour lives in <c>FeedTextParserTests</c>; this covers what the
/// controller does with what the parser found — which accounts a mention actually resolves to, and
/// what happens when it resolves to nobody.</para>
/// </remarks>
public sealed class FeedControllerTests
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

    private static FeedController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
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

    /// <summary>A controller with no signed-in user at all — a visitor (item 186).</summary>
    private static FeedController BuildAnonymous(IDbContextFactory<BenDataContext> factory)
        => new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
            },
        };

    private static AppUser MakeUser(string handle, string? displayName = null) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"{handle}@test.com", NormalizedUserName = $"{handle}@TEST.COM",
        Email = $"{handle}@test.com", NormalizedEmail = $"{handle}@TEST.COM",
        DisplayName = displayName ?? handle, Handle = handle, DateCreated = DateTime.UtcNow,
    };

    /// <summary>
    /// A database with the feed switched on, the given people in it, and — unless told otherwise —
    /// every one of them a member of a group so they may post (item 186 F2).
    /// </summary>
    /// <remarks>
    /// Belonging is the default here because almost every test in this class is about what the
    /// feed DOES, not about who may write in it. The gate's own tests opt out with
    /// <paramref name="everybodyBelongs"/> false and grant standing deliberately.
    /// </remarks>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        bool feedOn = true, bool everybodyBelongs = true, params AppUser[] users)
    {
        var factory = CreateFactory();
        await using var db = factory.CreateDbContext();

        db.Users.AddRange(users);

        if (everybodyBelongs && users.Length > 0)
        {
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
        }

        // The flag defaults to OFF when no row exists, so switching it on is an explicit row —
        // which is also what production looks like once a SuperAdmin has turned it on.
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(),
            Key = SiteSettingKeys.FeaturePublicFeed,
            Value = feedOn ? "true" : "false",
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = users.FirstOrDefault()?.Id ?? Guid.NewGuid(),
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<Guid> PostAsync(FeedController controller, string body, Guid? parent = null)
    {
        var result = await controller.CreatePost(new CreateFeedPostRequest(body, parent), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return ((FeedPostRecord)ok.Value!).Id;
    }

    private static async Task<List<FeedPostRecord>> ReadFeedAsync(
        FeedController controller, string? mode = null, string? hashtag = null, Guid? author = null)
    {
        var result = await controller.GetFeed(mode, hashtag, null, CancellationToken.None, author);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return ((FeedPageRecord)ok.Value!).Posts.ToList();
    }

    // ── The switch ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every route is 404 when the feed is off — not 403.
    /// </summary>
    /// <remarks>
    /// A disabled feature should not be discoverable by the shape of its refusal. "This does not
    /// exist here" is the truthful answer for a site whose administrator has not turned the feed
    /// on, and 403 would confirm it exists and is merely barred.
    /// </remarks>
    [Fact]
    public async Task With_the_feed_switched_off_every_route_is_not_found()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(feedOn: false, users: sarah);
        var controller = Build(factory, sarah.Id);

        Assert.IsType<NotFoundResult>((await controller.GetFeed(null, null, null, default)).Result);
        Assert.IsType<NotFoundResult>((await controller.GetThread(Guid.NewGuid(), default)).Result);
        Assert.IsType<NotFoundResult>((await controller.GetProfile(sarah.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await controller.CreatePost(new CreateFeedPostRequest("hello"), default)).Result);
        Assert.IsType<NotFoundResult>(await controller.Follow(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task With_no_setting_row_at_all_the_feed_is_off()
    {
        // The default matters: a feature nobody has switched on should not be running.
        var sarah = MakeUser("sarahmitchell");
        var factory = CreateFactory();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(sarah);
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(factory, sarah.Id).GetFeed(null, null, null, default)).Result);
    }

    // ── Posting, mentions and tags ────────────────────────────────────────────

    [Fact]
    public async Task A_post_records_its_tags_and_the_accounts_it_mentions()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);

        var id = await PostAsync(Build(factory, sarah.Id), "clear #EVP with @jamesthornton at the #bellwitch cave");

        await using var db = factory.CreateDbContext();

        var tags = await db.OrgMessageHashtags.Where(h => h.OrgMessageId == id).Select(h => h.Tag).ToListAsync();
        Assert.Equal(["evp", "bellwitch"], tags);

        var mentioned = await db.OrgMessageMentions.Where(m => m.OrgMessageId == id)
            .Select(m => m.MentionedAppUserId).ToListAsync();
        Assert.Equal([james.Id], mentioned);
    }

    /// <summary>
    /// An <c>@name</c> nobody answers to mentions nobody, and does not fail the post.
    /// </summary>
    /// <remarks>
    /// A typo is a typo. Refusing the post would be a strange way to report one, and inventing a
    /// recipient would be worse.
    /// </remarks>
    [Fact]
    public async Task A_mention_of_nobody_is_left_as_text()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);

        var id = await PostAsync(Build(factory, sarah.Id), "thanks @nobodyhere");

        await using var db = factory.CreateDbContext();
        Assert.Empty(await db.OrgMessageMentions.Where(m => m.OrgMessageId == id).ToListAsync());
    }

    [Fact]
    public async Task An_empty_post_is_refused()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);

        var result = await Build(factory, sarah.Id).CreatePost(new CreateFeedPostRequest("   "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_post_longer_than_the_limit_is_refused()
    {
        // Short-form is the point. A wall of text belongs in a publication.
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);

        var result = await Build(factory, sarah.Id)
            .CreatePost(new CreateFeedPostRequest(new string('a', FeedController.MaxBodyLength + 1)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Following ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_following_feed_shows_followed_people_and_yourself()
    {
        // Own posts included: a feed of people you follow that does not contain the thing you just
        // wrote reads as a bug every single time.
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var emma = MakeUser("emmarodriguez");
        var factory = await SeedAsync(users: [sarah, james, emma]);

        await PostAsync(Build(factory, james.Id), "james posts");
        await PostAsync(Build(factory, emma.Id), "emma posts");
        await PostAsync(Build(factory, sarah.Id), "sarah posts");

        await Build(factory, sarah.Id).Follow(james.Id, default);

        var bodies = (await ReadFeedAsync(Build(factory, sarah.Id), mode: "following"))
            .Select(p => p.Body).ToList();

        Assert.Contains("james posts", bodies);
        Assert.Contains("sarah posts", bodies);
        Assert.DoesNotContain("emma posts", bodies);
    }

    [Fact]
    public async Task Following_twice_follows_once_and_unfollowing_is_forgiving()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);
        var controller = Build(factory, sarah.Id);

        await controller.Follow(james.Id, default);
        await controller.Follow(james.Id, default);

        await using (var db = factory.CreateDbContext())
            Assert.Equal(1, await db.UserFollows.CountAsync());

        await controller.Unfollow(james.Id, default);
        await controller.Unfollow(james.Id, default);   // already gone; must not throw

        await using (var db = factory.CreateDbContext())
            Assert.Equal(0, await db.UserFollows.CountAsync());
    }

    [Fact]
    public async Task You_cannot_follow_yourself()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);

        Assert.IsType<BadRequestObjectResult>(await Build(factory, sarah.Id).Follow(sarah.Id, default));
    }

    // ── Reporting and hiding ──────────────────────────────────────────────────

    /// <summary>
    /// Reports do not hide anything, however many of them there are.
    /// </summary>
    /// <remarks>
    /// The property this feature rests on. A threshold would moderate whoever is least popular
    /// rather than whatever breaks the rules.
    /// </remarks>
    [Fact]
    public async Task Reporting_a_post_does_not_hide_it()
    {
        var author = MakeUser("author");
        var a = MakeUser("reportera");
        var b = MakeUser("reporterb");
        var c = MakeUser("reporterc");
        var factory = await SeedAsync(users: [author, a, b, c]);

        var id = await PostAsync(Build(factory, author.Id), "something people dislike");

        foreach (var reporter in new[] { a, b, c })
            await Build(factory, reporter.Id).ReportPost(id, new ReportFeedPostRequest("no"), default);

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(3, await db.OrgMessageReports.CountAsync());
            Assert.Null((await db.OrgMessages.FirstAsync(m => m.Id == id)).HiddenUtc);
        }

        Assert.Single(await ReadFeedAsync(Build(factory, a.Id)));
    }

    [Fact]
    public async Task One_person_reporting_twice_is_one_report()
    {
        // Otherwise a single objector could make a post look like a pile-on.
        var author = MakeUser("author");
        var reporter = MakeUser("reporter");
        var factory = await SeedAsync(users: [author, reporter]);

        var id = await PostAsync(Build(factory, author.Id), "a post");
        var controller = Build(factory, reporter.Id);

        await controller.ReportPost(id, new ReportFeedPostRequest("first"), default);
        await controller.ReportPost(id, new ReportFeedPostRequest("second"), default);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.OrgMessageReports.CountAsync());
    }

    /// <summary>
    /// A hidden post is gone from every read path, not just the main feed.
    /// </summary>
    /// <remarks>
    /// The one that would be got wrong: hiding the post from the feed while its thread, its
    /// author's profile count, or the reply endpoint still served it would make "hidden" a
    /// half-measure that leaks through whichever route was forgotten.
    /// </remarks>
    [Fact]
    public async Task A_hidden_post_disappears_from_every_read_path()
    {
        var author = MakeUser("author");
        var reader = MakeUser("reader");
        var factory = await SeedAsync(users: [author, reader]);

        var id = await PostAsync(Build(factory, author.Id), "to be hidden");

        await using (var db = factory.CreateDbContext())
        {
            var post = await db.OrgMessages.FirstAsync(m => m.Id == id);
            post.HiddenUtc = DateTime.UtcNow;
            post.HiddenByAppUserId = reader.Id;
            await db.SaveChangesAsync();
        }

        var controller = Build(factory, reader.Id);

        Assert.Empty(await ReadFeedAsync(controller));
        Assert.IsType<NotFoundResult>((await controller.GetThread(id, default)).Result);

        var profile = Assert.IsType<OkObjectResult>((await controller.GetProfile(author.Id, default)).Result);
        Assert.Equal(0, ((FeedProfileRecord)profile.Value!).PostCount);

        // And it cannot grow a thread nobody can see the top of.
        Assert.IsType<NotFoundObjectResult>(
            (await controller.CreatePost(new CreateFeedPostRequest("reply", id), default)).Result);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_feed_lists_top_level_posts_only()
    {
        // Replies are read with the post they answer; a feed that interleaved them would show
        // half a conversation in date order.
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        var controller = Build(factory, sarah.Id);

        var root = await PostAsync(controller, "the post");
        await PostAsync(controller, "the reply", root);

        var feed = await ReadFeedAsync(controller);

        Assert.Single(feed);
        Assert.Equal("the post", feed[0].Body);
        Assert.Equal(1, feed[0].ReplyCount);
    }

    [Fact]
    public async Task A_tag_filter_narrows_to_that_tag_however_it_was_typed()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        var controller = Build(factory, sarah.Id);

        await PostAsync(controller, "an #EVP night");
        await PostAsync(controller, "some #orbs");

        Assert.Single(await ReadFeedAsync(controller, hashtag: "evp"));
        Assert.Single(await ReadFeedAsync(controller, hashtag: "#EVP"));
        Assert.Empty(await ReadFeedAsync(controller, hashtag: "nothing"));
    }

    [Fact]
    public async Task Opening_a_thread_marks_the_post_seen_so_a_mention_stops_nagging()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);

        var id = await PostAsync(Build(factory, sarah.Id), "over to you @jamesthornton");

        await Build(factory, james.Id).GetThread(id, default);

        await using var db = factory.CreateDbContext();
        Assert.True(await db.OrgMessageViews.AnyAsync(v => v.OrgMessageId == id && v.ViewerAppUserId == james.Id));
    }

    [Fact]
    public async Task A_reader_sees_whether_they_already_reported_a_post()
    {
        // Drives whether the report control is offered, and stops somebody wondering if their
        // first report registered.
        var author = MakeUser("author");
        var reader = MakeUser("reader");
        var factory = await SeedAsync(users: [author, reader]);

        var id = await PostAsync(Build(factory, author.Id), "a post");

        Assert.False((await ReadFeedAsync(Build(factory, reader.Id)))[0].ReportedByCurrentUser);

        await Build(factory, reader.Id).ReportPost(id, new ReportFeedPostRequest(null), default);

        Assert.True((await ReadFeedAsync(Build(factory, reader.Id)))[0].ReportedByCurrentUser);
    }

    // ── item 186 F1: anyone reads ─────────────────────────────────────────────

    /// <summary>
    /// A visitor with no account reads the same posts a member does.
    /// </summary>
    /// <remarks>
    /// The front door. A feed that demands sign-in before showing anything gives a visitor nothing
    /// to sign up FOR, which is the whole reason the arc exists.
    /// </remarks>
    [Fact]
    public async Task A_visitor_reads_the_feed()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        await PostAsync(Build(factory, sarah.Id), "Knocking in the upstairs hall #EVP");

        var posts = await ReadFeedAsync(BuildAnonymous(factory));

        Assert.Single(posts);
        Assert.Equal("Knocking in the upstairs hall #EVP", posts[0].Body);
        Assert.Contains("evp", posts[0].Hashtags);
    }

    /// <summary>Every per-reader flag is false for somebody who is not a reader we know.</summary>
    [Fact]
    public async Task A_visitors_per_reader_flags_are_all_false()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);

        var postId = await PostAsync(Build(factory, sarah.Id), "Something happened here.");
        await Build(factory, james.Id).Follow(sarah.Id, CancellationToken.None);
        await Build(factory, james.Id).ReportPost(
            postId, new ReportFeedPostRequest(null), CancellationToken.None);

        var posts = await ReadFeedAsync(BuildAnonymous(factory));

        Assert.False(posts[0].IsOwnPost);
        Assert.False(posts[0].AuthorIsFollowedByCurrentUser);
        Assert.False(posts[0].ReportedByCurrentUser);
    }

    /// <summary>A shared link to a thread opens for the person it was shared with.</summary>
    [Fact]
    public async Task A_visitor_reads_a_thread_and_a_profile()
    {
        var sarah = MakeUser("sarahmitchell", "Sarah Mitchell");
        var factory = await SeedAsync(users: sarah);
        var rootId = await PostAsync(Build(factory, sarah.Id), "The recorder caught something.");
        await PostAsync(Build(factory, sarah.Id), "Uploading it now.", rootId);

        var thread = await BuildAnonymous(factory).GetThread(rootId, CancellationToken.None);
        var threadPosts = (IReadOnlyList<FeedPostRecord>)Assert.IsType<OkObjectResult>(thread.Result).Value!;
        Assert.Equal(2, threadPosts.Count);

        var profile = await BuildAnonymous(factory).GetProfile(sarah.Id, CancellationToken.None);
        var record = (FeedProfileRecord)Assert.IsType<OkObjectResult>(profile.Result).Value!;
        Assert.Equal("Sarah Mitchell", record.DisplayName);
        Assert.Equal(2, record.PostCount);
        Assert.False(record.IsSelf);
        Assert.False(record.IsFollowedByCurrentUser);
    }

    /// <summary>
    /// Opening a thread as a visitor records no view.
    /// </summary>
    /// <remarks>
    /// OrgMessageView is keyed by viewer, and Guid.Empty is not a person — writing one would put a
    /// row against a user that does not exist and, worse, could clear a real reader's bell if the
    /// key ever collided.
    /// </remarks>
    [Fact]
    public async Task A_visitor_opening_a_thread_marks_nothing_seen()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        var rootId = await PostAsync(Build(factory, sarah.Id), "Anyone else hear that?");

        await BuildAnonymous(factory).GetThread(rootId, CancellationToken.None);

        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.OrgMessageViews.CountAsync());
    }

    /// <summary>The feed is still 404 for a visitor when it is switched off.</summary>
    [Fact]
    public async Task A_visitor_gets_nothing_when_the_feed_is_off()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(feedOn: false, users: sarah);
        var anonymous = BuildAnonymous(factory);

        Assert.IsType<NotFoundResult>(
            (await anonymous.GetFeed(null, null, null, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            (await anonymous.GetThread(Guid.NewGuid(), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(
            (await anonymous.GetProfile(sarah.Id, CancellationToken.None)).Result);
    }

    // ── item 186 F1: one person's posts ───────────────────────────────────────

    [Fact]
    public async Task The_author_filter_returns_only_that_persons_posts_and_ignores_mode()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);

        await PostAsync(Build(factory, sarah.Id), "Sarah's first.");
        await PostAsync(Build(factory, james.Id), "James's only.");
        await PostAsync(Build(factory, sarah.Id), "Sarah's second.");

        // "following" would normally mean James's own posts plus those he follows — but an author
        // filter is a question about one person, so the mode must not narrow it further.
        var posts = await ReadFeedAsync(Build(factory, james.Id), mode: "following", author: sarah.Id);

        Assert.Equal(2, posts.Count);
        Assert.All(posts, p => Assert.Equal(sarah.Id, p.AuthorAppUserId));
    }

    [Fact]
    public async Task The_author_filter_leaves_out_replies_and_hidden_posts()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        var controller = Build(factory, sarah.Id);

        var rootId = await PostAsync(controller, "Top level.");
        await PostAsync(controller, "A reply of mine.", rootId);
        var hiddenId = await PostAsync(controller, "This one gets hidden.");

        await using (var db = factory.CreateDbContext())
        {
            var hidden = await db.OrgMessages.SingleAsync(m => m.Id == hiddenId);
            hidden.HiddenUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var posts = await ReadFeedAsync(BuildAnonymous(factory), author: sarah.Id);

        Assert.Single(posts);
        Assert.Equal(rootId, posts[0].Id);
    }

    // ── item 186 F2: who may write ────────────────────────────────────────────

    /// <summary>Ben's rule: a member of any group may post, whatever their role.</summary>
    [Fact]
    public async Task A_member_of_any_group_may_post()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);          // seeded as a Member

        var result = await Build(factory, sarah.Id).CreatePost(
            new CreateFeedPostRequest("Members write here.", null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>
    /// A client may post — Ben's decision. Both routes to being one are honoured.
    /// </summary>
    [Theory]
    [InlineData(true)]     // the person whose request became the case
    [InlineData(false)]    // somebody the case was later shared with
    public async Task A_client_may_post(bool viaOriginalRequest)
    {
        var client = MakeUser("danielpark");
        var factory = await SeedAsync(everybodyBelongs: false, users: client);

        await using (var db = factory.CreateDbContext())
        {
            var caseId = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            db.ClientRequests.Add(new ClientRequest
            {
                Id = requestId,
                AppUserId = viaOriginalRequest ? client.Id : Guid.NewGuid(),
                Status = ClientRequestStatus.Assigned,
                StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = client.Id,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = Guid.NewGuid(), ClientRequestId = requestId,
                Title = "Their case", CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Active,
                StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = client.Id,
            });
            if (!viaOriginalRequest)
            {
                db.CaseClientAccesses.Add(new CaseClientAccess
                {
                    Id = Guid.NewGuid(), CaseId = caseId, AppUserId = client.Id,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = client.Id,
                });
            }
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, client.Id).CreatePost(
            new CreateFeedPostRequest("The knocking started again last night.", null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>
    /// A signed-in stranger — no group, no case — is refused, and told both doors.
    /// </summary>
    [Fact]
    public async Task Somebody_who_belongs_to_nothing_is_refused_with_both_doors()
    {
        var stranger = MakeUser("passerby");
        var factory = await SeedAsync(everybodyBelongs: false, users: stranger);

        var result = await Build(factory, stranger.Id).CreatePost(
            new CreateFeedPostRequest("Hello?", null), CancellationToken.None);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        var text = refusal.Value!.ToString()!;
        Assert.Contains("belong here", text);
        Assert.Contains("Join a group", text);
        Assert.Contains("request an investigation", text);

        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.OrgMessages.CountAsync());
    }

    /// <summary>Following builds an audience, so it is participation too.</summary>
    [Fact]
    public async Task Somebody_who_belongs_to_nothing_cannot_follow()
    {
        var stranger = MakeUser("passerby");
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(everybodyBelongs: false, users: [stranger, sarah]);

        var result = await Build(factory, stranger.Id).Follow(sarah.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.UserFollows.CountAsync());
    }

    /// <summary>
    /// Reporting is NOT gated: safety must not require belonging.
    /// </summary>
    /// <remarks>
    /// If a signed-in stranger is the first to see something that should not be on the site, we
    /// want to hear about it — refusing the report because they have not joined a group would be
    /// choosing the funnel over the thing the funnel is for.
    /// </remarks>
    [Fact]
    public async Task Somebody_who_belongs_to_nothing_may_still_report()
    {
        var sarah = MakeUser("sarahmitchell");
        var stranger = MakeUser("passerby");
        var factory = await SeedAsync(users: [sarah, stranger]);   // sarah is a member
        var postId = await PostAsync(Build(factory, sarah.Id), "Something worth reporting.");

        // Strip the stranger's membership so only Sarah belongs.
        await using (var db = factory.CreateDbContext())
        {
            var m = await db.OrganizationUserMemberships.SingleAsync(x => x.AppUserId == stranger.Id);
            db.OrganizationUserMemberships.Remove(m);
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, stranger.Id).ReportPost(
            postId, new ReportFeedPostRequest("This is not paranormal."), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await using var check = factory.CreateDbContext();
        Assert.Equal(1, await check.OrgMessageReports.CountAsync());
    }

    /// <summary>
    /// The page tells the reader whether they may write, from the same rule the create endpoint
    /// enforces — so the composer is never offered to somebody whose post would be refused.
    /// </summary>
    [Fact]
    public async Task The_page_reports_whether_this_reader_may_post()
    {
        var member = MakeUser("sarahmitchell");
        var stranger = MakeUser("passerby");
        var factory = await SeedAsync(users: [member, stranger]);
        await using (var db = factory.CreateDbContext())
        {
            var m = await db.OrganizationUserMemberships.SingleAsync(x => x.AppUserId == stranger.Id);
            db.OrganizationUserMemberships.Remove(m);
            await db.SaveChangesAsync();
        }

        async Task<bool> CanPostAsync(FeedController controller)
        {
            var result = await controller.GetFeed(null, null, null, CancellationToken.None);
            return ((FeedPageRecord)Assert.IsType<OkObjectResult>(result.Result).Value!).CanPost;
        }

        Assert.True(await CanPostAsync(Build(factory, member.Id)));
        Assert.False(await CanPostAsync(Build(factory, stranger.Id)));
        Assert.False(await CanPostAsync(BuildAnonymous(factory)));
    }

    // ── item 186 F3: likes ────────────────────────────────────────────────────

    [Fact]
    public async Task Liking_is_idempotent_and_counted_once()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);
        var postId = await PostAsync(Build(factory, sarah.Id), "Worth a look.");

        Assert.IsType<NoContentResult>(await Build(factory, james.Id).LikePost(postId, CancellationToken.None));
        Assert.IsType<NoContentResult>(await Build(factory, james.Id).LikePost(postId, CancellationToken.None));

        var asJames = (await ReadFeedAsync(Build(factory, james.Id))).Single();
        Assert.Equal(1, asJames.LikeCount);
        Assert.True(asJames.LikedByCurrentUser);

        // The count is everybody's; the flag is the reader's own.
        var asSarah = (await ReadFeedAsync(Build(factory, sarah.Id))).Single();
        Assert.Equal(1, asSarah.LikeCount);
        Assert.False(asSarah.LikedByCurrentUser);
    }

    [Fact]
    public async Task Unliking_removes_it_and_forgives_a_like_that_was_never_there()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);
        var postId = await PostAsync(Build(factory, sarah.Id), "Worth a look.");

        await Build(factory, james.Id).LikePost(postId, CancellationToken.None);
        Assert.IsType<NoContentResult>(await Build(factory, james.Id).UnlikePost(postId, CancellationToken.None));
        Assert.IsType<NoContentResult>(await Build(factory, james.Id).UnlikePost(postId, CancellationToken.None));

        Assert.Equal(0, (await ReadFeedAsync(Build(factory, james.Id))).Single().LikeCount);
    }

    [Fact]
    public async Task A_visitor_sees_the_count_but_never_the_flag()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);
        var postId = await PostAsync(Build(factory, sarah.Id), "Worth a look.");
        await Build(factory, james.Id).LikePost(postId, CancellationToken.None);

        var asVisitor = (await ReadFeedAsync(BuildAnonymous(factory))).Single();
        Assert.Equal(1, asVisitor.LikeCount);
        Assert.False(asVisitor.LikedByCurrentUser);
    }

    [Fact]
    public async Task Somebody_who_belongs_to_nothing_cannot_like_but_can_still_unlike()
    {
        var sarah = MakeUser("sarahmitchell");
        var stranger = MakeUser("passerby");
        var factory = await SeedAsync(users: [sarah, stranger]);
        var postId = await PostAsync(Build(factory, sarah.Id), "Worth a look.");

        // Liked while they still belonged...
        await Build(factory, stranger.Id).LikePost(postId, CancellationToken.None);

        // ...then their membership went away.
        await using (var db = factory.CreateDbContext())
        {
            var m = await db.OrganizationUserMemberships.SingleAsync(x => x.AppUserId == stranger.Id);
            db.OrganizationUserMemberships.Remove(m);
            await db.SaveChangesAsync();
        }

        Assert.IsType<BadRequestObjectResult>(
            await Build(factory, stranger.Id).LikePost(postId, CancellationToken.None));

        // Taking back what you already did is not participation, and must never be trapped.
        Assert.IsType<NoContentResult>(
            await Build(factory, stranger.Id).UnlikePost(postId, CancellationToken.None));
        await using var check = factory.CreateDbContext();
        Assert.Equal(0, await check.OrgMessageLikes.CountAsync());
    }

    [Fact]
    public async Task A_hidden_post_cannot_be_liked()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var factory = await SeedAsync(users: [sarah, james]);
        var postId = await PostAsync(Build(factory, sarah.Id), "This gets hidden.");

        await using (var db = factory.CreateDbContext())
        {
            var post = await db.OrgMessages.SingleAsync(m => m.Id == postId);
            post.HiddenUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>(
            await Build(factory, james.Id).LikePost(postId, CancellationToken.None));
    }

    // ── item 186 F3: the ranked feed ──────────────────────────────────────────

    [Fact]
    public async Task For_you_puts_the_engaging_post_above_the_newer_silent_one()
    {
        var sarah = MakeUser("sarahmitchell");
        var james = MakeUser("jamesthornton");
        var emma = MakeUser("emmablake");
        var factory = await SeedAsync(users: [sarah, james, emma]);

        var older = await PostAsync(Build(factory, sarah.Id), "Older, but people cared.");
        var newer = await PostAsync(Build(factory, sarah.Id), "Newest, and nobody looked.");

        // Four hours old with two likes. Deliberately modest numbers: an earlier draft of this
        // test used one like against a ten-hour gap and failed, which is the ranking working —
        // a single like is not supposed to outweigh most of a day. Two likes at four hours is
        // the everyday case the tab exists to surface.
        await using (var db = factory.CreateDbContext())
        {
            var post = await db.OrgMessages.SingleAsync(m => m.Id == older);
            post.DateCreated = DateTime.UtcNow.AddHours(-4);
            await db.SaveChangesAsync();
        }
        await Build(factory, james.Id).LikePost(older, CancellationToken.None);
        await Build(factory, emma.Id).LikePost(older, CancellationToken.None);

        var ranked = await ReadFeedAsync(Build(factory, james.Id), mode: "foryou");
        Assert.Equal(older, ranked[0].Id);

        // Latest is untouched by ranking — that is what it is for.
        var latest = await ReadFeedAsync(Build(factory, james.Id), mode: "all");
        Assert.Equal(newer, latest[0].Id);
    }

    [Fact]
    public async Task For_you_leaves_out_hidden_posts_and_replies()
    {
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        var controller = Build(factory, sarah.Id);

        var rootId = await PostAsync(controller, "Top level.");
        await PostAsync(controller, "A reply.", rootId);
        var hiddenId = await PostAsync(controller, "Hidden one.");

        await using (var db = factory.CreateDbContext())
        {
            var hidden = await db.OrgMessages.SingleAsync(m => m.Id == hiddenId);
            hidden.HiddenUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var ranked = await ReadFeedAsync(controller, mode: "foryou");
        Assert.Single(ranked);
        Assert.Equal(rootId, ranked[0].Id);
    }

    [Fact]
    public async Task An_unknown_mode_still_reads_as_latest()
    {
        // Every link and client written before ranking existed must keep working.
        var sarah = MakeUser("sarahmitchell");
        var factory = await SeedAsync(users: sarah);
        await PostAsync(Build(factory, sarah.Id), "Still here.");

        Assert.Single(await ReadFeedAsync(Build(factory, sarah.Id), mode: "whatever"));
    }
}
