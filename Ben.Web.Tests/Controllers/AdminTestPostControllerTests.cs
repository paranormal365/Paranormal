using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The door that takes an e2e run's posts off the live feed must open only for posts by the seeded
/// accounts, and must never hide a real person's post even when handed its id.
/// </summary>
public class AdminTestPostControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static AdminTestPostController Build(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new AdminTestPostController(factory);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                     new Claim(ClaimTypes.Role, "SuperAdmin")], "Bearer"))
            }
        };
        return ctrl;
    }

    private static AppUser Person(string email) => new()
    {
        Id = Guid.NewGuid(), UserName = email, Email = email,
        NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0],
    };

    private static OrgMessage Post(AppUser by, string body, Guid? parent = null, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), AuthorAppUserId = by.Id, CreatedByAppUserId = by.Id,
        ChannelType = OrgMessageChannel.PublicFeed, Body = body, ParentMessageId = parent,
        DateCreated = DateTime.UtcNow, HiddenUtc = hidden ? DateTime.UtcNow : null,
    };

    /// <summary>A seeded author's posts are listed; a real person's are not, whatever they say.</summary>
    [Fact]
    public async Task Lists_only_posts_by_seeded_accounts()
    {
        var factory = CreateFactory();
        var seeded = Person("sarah.mitchell@benco.dev");
        var real   = Person("someone@gmail.com");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.AddRange(seeded, real);
            db.OrgMessages.AddRange(
                Post(seeded, "Playback check"),
                Post(real,   "Playback check — a real person who happened to say it"),
                new OrgMessage { Id = Guid.NewGuid(), AuthorAppUserId = seeded.Id, CreatedByAppUserId = seeded.Id,
                                 ChannelType = OrgMessageChannel.OrgBroadcast, Body = "not on the feed", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).List(CancellationToken.None);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<TestFeedPostRecord>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        var row = Assert.Single(rows);
        Assert.Equal("Playback check", row.Body);
        Assert.Equal("sarah.mitchell@benco.dev", row.AuthorEmail);
    }

    [Fact]
    public async Task Hiding_a_post_takes_its_visible_replies_with_it_and_unhiding_puts_only_it_back()
    {
        var factory = CreateFactory();
        var seeded = Person("james.thornton@example.com");
        var parent = Post(seeded, "e2e post");
        var reply  = Post(seeded, "e2e reply", parent.Id);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(seeded);
            db.OrgMessages.AddRange(parent, reply);
            await db.SaveChangesAsync();
        }

        var hide = await Build(factory).Hide(new TestFeedPostIdsRequest([parent.Id]), CancellationToken.None);
        var hidden = Assert.IsType<TestFeedPostHideResult>(Assert.IsType<OkObjectResult>(hide.Result).Value);
        Assert.Equal(1, hidden.Changed);
        Assert.Equal(1, hidden.RepliesAlso);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.NotNull((await db.OrgMessages.SingleAsync(m => m.Id == parent.Id)).HiddenUtc);
            Assert.NotNull((await db.OrgMessages.SingleAsync(m => m.Id == reply.Id)).HiddenUtc);
        }

        // Hiding again changes nothing — the count is of state that flipped, not of ids sent.
        var again = await Build(factory).Hide(new TestFeedPostIdsRequest([parent.Id]), CancellationToken.None);
        Assert.Equal(0, Assert.IsType<TestFeedPostHideResult>(Assert.IsType<OkObjectResult>(again.Result).Value).Changed);

        var unhide = await Build(factory).Unhide(new TestFeedPostIdsRequest([parent.Id]), CancellationToken.None);
        Assert.Equal(1, Assert.IsType<TestFeedPostHideResult>(Assert.IsType<OkObjectResult>(unhide.Result).Value).Changed);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null((await db.OrgMessages.SingleAsync(m => m.Id == parent.Id)).HiddenUtc);
            Assert.NotNull((await db.OrgMessages.SingleAsync(m => m.Id == reply.Id)).HiddenUtc);
        }
    }

    /// <summary>
    /// Handed a real person's post id alongside a seeded one, the batch is refused whole and the
    /// seeded post is left as it was. This is the test that matters: the page never sends such an
    /// id, but the endpoint is the thing that must not trust that.
    /// </summary>
    [Fact]
    public async Task A_real_persons_post_id_refuses_the_whole_batch()
    {
        var factory = CreateFactory();
        var seeded = Person("victor.reyes@benco.dev");
        var real   = Person("member@outlook.com");
        var seededPost = Post(seeded, "e2e post");
        var realPost   = Post(real,   "my first night walk");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.AddRange(seeded, real);
            db.OrgMessages.AddRange(seededPost, realPost);
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).Hide(new TestFeedPostIdsRequest([seededPost.Id, realPost.Id]), CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.NotNull(Assert.IsType<TestFeedPostHideResult>(conflict.Value).Refusal);

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null((await db.OrgMessages.SingleAsync(m => m.Id == seededPost.Id)).HiddenUtc);
            Assert.Null((await db.OrgMessages.SingleAsync(m => m.Id == realPost.Id)).HiddenUtc);
        }
    }

    [Fact]
    public async Task An_empty_choice_is_refused_with_a_sentence()
    {
        var result = await Build(CreateFactory()).Hide(new TestFeedPostIdsRequest([]), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
