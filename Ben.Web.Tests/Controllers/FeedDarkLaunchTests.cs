using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The dark-launch facts (item 186 F10): the moderation summary must say, truthfully, whether
/// the feed is on and how much content sits behind it — the reminder banner renders from
/// nothing else, and a reminder computed from stale or guessed facts would either nag past the
/// launch or stay silent before it.
/// </summary>
public sealed class FeedDarkLaunchTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(bool feedOn, int posts)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();

        var userId = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = userId, UserName = "a@t.dev", Email = "a@t.dev", DisplayName = "A", Handle = "a",
        });
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = "features.public-feed",
            Value = feedOn ? "true" : "false", DateCreated = DateTime.UtcNow,
        });
        for (var i = 0; i < posts; i++)
            db.OrgMessages.Add(new OrgMessage
            {
                Id = Guid.NewGuid(), AuthorAppUserId = userId, CreatedByAppUserId = userId,
                Body = $"post {i}", IsPublic = true, ChannelType = OrgMessageChannel.PublicFeed,
                DateCreated = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
        return factory;
    }

    private static ModerationController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, new ManualReviewScreener(),
               new FeedLearningService(
                   TestMedia.StorageOnDisk(Path.Combine(Path.GetTempPath(), "dark-launch-tests")),
                   NullLogger<FeedLearningService>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer")),
                },
            },
        };

    [Fact]
    public async Task Dark_with_content_is_reported_exactly_so()
    {
        var factory = await SeedAsync(feedOn: false, posts: 3);
        var summary = (FeedModerationSummary)Assert.IsType<OkObjectResult>(
            (await Build(factory).GetSummary(default)).Result).Value!;

        Assert.False(summary.FeedIsOn);
        Assert.Equal(3, summary.FeedPostCount);
        Assert.False(summary.ScreeningIsAutomatic); // the manual screener, said honestly
    }

    [Fact]
    public async Task Switched_on_reports_on_so_the_reminder_disappears()
    {
        var factory = await SeedAsync(feedOn: true, posts: 3);
        var summary = (FeedModerationSummary)Assert.IsType<OkObjectResult>(
            (await Build(factory).GetSummary(default)).Result).Value!;
        Assert.True(summary.FeedIsOn);
    }

    [Fact]
    public async Task An_empty_dark_feed_reports_zero_and_nags_nobody()
    {
        var factory = await SeedAsync(feedOn: false, posts: 0);
        var summary = (FeedModerationSummary)Assert.IsType<OkObjectResult>(
            (await Build(factory).GetSummary(default)).Result).Value!;
        Assert.Equal(0, summary.FeedPostCount);
    }
}
