using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The moderation queue, once a report can be about a published case (item 233, 2026-09-11).
/// </summary>
/// <remarks>
/// <para>The queue used to carry one kind of thing, and both halves of deciding a report went
/// straight to <c>OrgMessages</c>. A case report has no message behind it, so the first version of
/// this answered 404 to every decision about one — a button in the queue that could never do
/// anything. These tests are that failure written down, plus the rule that makes the new branch
/// safe: <b>dismissing never publishes a case this queue did not take down.</b></para>
///
/// <para>Every row also has to be somewhere a moderator can go and look, which is why the target
/// address is asserted rather than the id alone.</para>
/// </remarks>
public sealed class AdminFeedReportsTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static readonly Guid Moderator = Guid.NewGuid();

    private static AdminFeedController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Moderator.ToString())], "Bearer")),
                },
            },
        };

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid CaseId, Guid CommentId, Guid ReporterId);

    /// <summary>A published case, a comment on it, and somebody to complain.</summary>
    private static async Task<World> SeedAsync()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using var db = factory.CreateDbContext();

        var reporter = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "reporter@test.com", NormalizedUserName = "REPORTER@TEST.COM",
            Email = "reporter@test.com", NormalizedEmail = "REPORTER@TEST.COM",
            DisplayName = "Reporter", Handle = "reporter", DateCreated = DateTime.UtcNow,
        };
        db.Users.Add(reporter);

        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Nashville Paranormal", UrlName = "nashville-paranormal",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = reporter.Id,
        });

        var caseId = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId,
            Title = "The Printers Alley Knocking", UrlName = "printers-alley-knocking",
            CaseYear = 2026, OrgCaseNumber = 42, City = "Nashville", State = "TN",
            IsPublic = true, Status = CaseStatus.Public,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = reporter.Id,
        });

        var commentId = Guid.NewGuid();
        db.OrgMessages.Add(new OrgMessage
        {
            Id = commentId, AuthorAppUserId = reporter.Id,
            ChannelType = OrgMessageChannel.PublicCaseComment,
            CaseId = caseId, Body = "Not convinced.", IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = reporter.Id,
        });

        await db.SaveChangesAsync();
        return new World(factory, caseId, commentId, reporter.Id);
    }

    private static async Task<Guid> ReportAsync(World world, Guid? caseId = null, Guid? messageId = null)
    {
        await using var db = world.Factory.CreateDbContext();
        var id = Guid.NewGuid();
        db.OrgMessageReports.Add(new OrgMessageReport
        {
            Id = id, CaseId = caseId, OrgMessageId = messageId,
            ReportedByAppUserId = world.ReporterId,
            Reason = "Made up", Outcome = FeedReportOutcome.Pending,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<List<FeedReportRecord>> QueueAsync(
        World world, FeedReportOutcome? outcome = null)
    {
        var result = await Build(world.Factory).GetReports(outcome, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return ((IReadOnlyList<FeedReportRecord>)ok.Value!).ToList();
    }

    [Fact]
    public async Task A_case_report_names_the_case_and_says_where_to_go_and_look()
    {
        var world = await SeedAsync();
        await ReportAsync(world, caseId: world.CaseId);

        var row = Assert.Single(await QueueAsync(world));

        Assert.Equal("Case #2026-042", row.TargetLabel);
        Assert.Equal("/o/nashville-paranormal/cases/printers-alley-knocking", row.TargetUrl);
        Assert.Contains("The Printers Alley Knocking", row.PostBody);
    }

    [Fact]
    public async Task A_reported_comment_points_at_the_case_it_is_on()
    {
        // Not /feed/{id}: the thread page knows feed posts only, so that address was a row in the
        // queue linking to nowhere.
        var world = await SeedAsync();
        await ReportAsync(world, messageId: world.CommentId);

        var row = Assert.Single(await QueueAsync(world));

        Assert.Equal("Case comment", row.TargetLabel);
        Assert.Equal("/o/nashville-paranormal/cases/printers-alley-knocking#case-comments", row.TargetUrl);
    }

    [Fact]
    public async Task Upholding_a_case_report_takes_the_case_off_the_public_site()
    {
        var world = await SeedAsync();
        var reportId = await ReportAsync(world, caseId: world.CaseId);

        var result = await Build(world.Factory)
            .Resolve(reportId, new ResolveFeedReportRequest(FeedReportOutcome.Hidden), default);
        Assert.IsType<NoContentResult>(result);

        await using var db = world.Factory.CreateDbContext();
        Assert.False((await db.Cases.SingleAsync()).IsPublic);
        Assert.Equal(FeedReportOutcome.Hidden, (await db.OrgMessageReports.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task Dismissing_after_a_takedown_puts_the_case_back()
    {
        var world = await SeedAsync();
        var first = await ReportAsync(world, caseId: world.CaseId);
        await Build(world.Factory).Resolve(first, new ResolveFeedReportRequest(FeedReportOutcome.Hidden), default);

        var second = await ReportAsync(world, caseId: world.CaseId);
        await Build(world.Factory).Resolve(second, new ResolveFeedReportRequest(FeedReportOutcome.Dismissed), default);

        await using var db = world.Factory.CreateDbContext();
        Assert.True((await db.Cases.SingleAsync()).IsPublic);
    }

    [Fact]
    public async Task Dismissing_never_publishes_a_case_this_queue_did_not_take_down()
    {
        // A group that unpublished its own case must not find it back on the public site because
        // a moderator dismissed a complaint about it.
        var world = await SeedAsync();
        await using (var db = world.Factory.CreateDbContext())
        {
            (await db.Cases.SingleAsync()).IsPublic = false;
            await db.SaveChangesAsync();
        }

        var reportId = await ReportAsync(world, caseId: world.CaseId);
        await Build(world.Factory).Resolve(reportId, new ResolveFeedReportRequest(FeedReportOutcome.Dismissed), default);

        await using var after = world.Factory.CreateDbContext();
        Assert.False((await after.Cases.SingleAsync()).IsPublic);
    }

    [Fact]
    public async Task One_decision_resolves_every_waiting_report_about_the_same_case()
    {
        var world = await SeedAsync();
        var first = await ReportAsync(world, caseId: world.CaseId);
        await ReportAsync(world, caseId: world.CaseId);
        await ReportAsync(world, caseId: world.CaseId);

        await Build(world.Factory).Resolve(first, new ResolveFeedReportRequest(FeedReportOutcome.Hidden), default);

        Assert.Empty(await QueueAsync(world));
        Assert.Equal(3, (await QueueAsync(world, FeedReportOutcome.Hidden)).Count);
    }

    [Fact]
    public async Task Hiding_a_reported_comment_still_works_the_way_it_always_did()
    {
        var world = await SeedAsync();
        var reportId = await ReportAsync(world, messageId: world.CommentId);

        await Build(world.Factory).Resolve(reportId, new ResolveFeedReportRequest(FeedReportOutcome.Hidden), default);

        await using var db = world.Factory.CreateDbContext();
        Assert.NotNull((await db.OrgMessages.SingleAsync()).HiddenUtc);
        Assert.True((await db.Cases.SingleAsync()).IsPublic);
    }
}
