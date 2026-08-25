using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Editor → feed (item 186 F7): case lineage, the private-engagement consent, and the group's
/// say over its own name.
/// </summary>
/// <remarks>
/// Two claims under protection. First, item 184's: private-engagement footage goes public only
/// through an explicit, RECORDED agreement — no tick, no post. Second, the attribution default:
/// a group's name appears on nothing until the group says so, and a reader can never tell an
/// unclaimed post from an unattributed one.
/// </remarks>
public sealed class FeedAttributionTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static readonly string MediaRoot =
        Path.Combine(Path.GetTempPath(), "ben-feed-attr-tests", Guid.NewGuid().ToString("N"));

    private static AppUser MakeUser(string handle) => new()
    {
        Id = Guid.NewGuid(),
        UserName = $"{handle}@test.com", NormalizedUserName = $"{handle}@TEST.COM",
        Email = $"{handle}@test.com", NormalizedEmail = $"{handle}@TEST.COM",
        DisplayName = handle, Handle = handle, DateCreated = DateTime.UtcNow,
    };

    /// <summary>Feed on, one org, the member in it, one case (optionally private-engagement).</summary>
    private static async Task<(IDbContextFactory<BenDataContext> Factory, AppUser Member, Guid OrgId, Guid CaseId)>
        SeedAsync(bool privateEngagement)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var member = MakeUser("caseworker");
        Guid orgId = Guid.NewGuid(), caseId = Guid.NewGuid();

        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();
        db.Users.Add(member);
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = "features.public-feed", Value = "true",
            DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Field Org", UrlName = $"field-{Guid.NewGuid():N}"[..16],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = member.Id,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = member.Id,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = member.Id,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "The house on Elm",
            CaseYear = 2026, OrgCaseNumber = 7, Status = CaseStatus.Active,
            StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1", Country = "US",
            IsPrivateEngagement = privateEngagement,
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = member.Id,
        });
        await db.SaveChangesAsync();
        return (factory, member, orgId, caseId);
    }

    private static FeedController BuildFeed(IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory, TestMedia.StorageOnDisk(MediaRoot), TestMedia.IngestToDisk(MediaRoot),
               new ManualReviewScreener(),
               new FeedLearningService(TestMedia.StorageOnDisk(MediaRoot),
                   NullLogger<FeedLearningService>.Instance),
               NullLogger<FeedController>.Instance)
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

    private static OrgFeedAttributionController BuildAttribution(
        IDbContextFactory<BenDataContext> factory, Guid userId, bool isOrgAdmin)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(isOrgAdmin);

        return new OrgFeedAttributionController(factory, security.Object,
            new FeedLearningService(TestMedia.StorageOnDisk(MediaRoot),
                NullLogger<FeedLearningService>.Instance))
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
    }

    private static IFormFile Render() =>
        new FormFile(new MemoryStream(TestImages.Jpeg()), 0, TestImages.Jpeg().Length, "media", "render.jpg")
        { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

    // ── The consent gate ────────────────────────────────────────────────────

    [Fact]
    public async Task Private_engagement_render_without_the_tick_is_refused_in_those_words()
    {
        var (factory, member, _, caseId) = await SeedAsync(privateEngagement: true);
        var result = await BuildFeed(factory, member.Id).CreatePost(
            new CreateFeedPostRequest("night footage", SourceCaseId: caseId), Render(), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("private engagement", refusal.Value!.ToString());

        // And nothing landed: no post, no consent row.
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.OrgMessages.CountAsync());
        Assert.Equal(0, await db.FeedPostConsents.CountAsync());
    }

    [Fact]
    public async Task Private_engagement_render_with_the_tick_posts_and_records_who_agreed()
    {
        var (factory, member, orgId, caseId) = await SeedAsync(privateEngagement: true);
        var result = await BuildFeed(factory, member.Id).CreatePost(
            new CreateFeedPostRequest("night footage", SourceCaseId: caseId,
                ConsentToPublishPrivateEngagement: true), Render(), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var consent = await db.FeedPostConsents.SingleAsync();
        Assert.Equal(caseId, consent.CaseId);
        Assert.Equal(member.Id, consent.AgreedByAppUserId);
        Assert.Equal(1, consent.WordingVersion);

        var post = await db.OrgMessages.SingleAsync();
        Assert.Equal(orgId, post.AttributedOrganizationId);
        Assert.Equal(OrgAttributionState.Unclaimed, post.AttributionState);
        Assert.Equal(caseId, post.CaseId);
    }

    [Fact]
    public async Task Ordinary_case_render_needs_no_tick_and_writes_no_consent_row()
    {
        var (factory, member, orgId, caseId) = await SeedAsync(privateEngagement: false);
        var result = await BuildFeed(factory, member.Id).CreatePost(
            new CreateFeedPostRequest("footage", SourceCaseId: caseId), Render(), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.FeedPostConsents.CountAsync());
        Assert.Equal(orgId, (await db.OrgMessages.SingleAsync()).AttributedOrganizationId);
    }

    [Fact]
    public async Task Case_lineage_without_media_is_refused()
    {
        var (factory, member, _, caseId) = await SeedAsync(privateEngagement: false);
        var result = await BuildFeed(factory, member.Id).CreatePost(
            new CreateFeedPostRequest("just words", SourceCaseId: caseId), media: null, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_stranger_to_the_case_cannot_post_from_it_and_learns_nothing()
    {
        var (factory, _, _, caseId) = await SeedAsync(privateEngagement: false);
        // A member of SOME group (so the participation gate passes) — but not of this case's.
        var outsider = MakeUser("outsider");
        await using (var db = await factory.CreateDbContextAsync())
        {
            var otherOrg = Guid.NewGuid();
            db.Users.Add(outsider);
            db.Organizations.Add(new Organization
            {
                Id = otherOrg, Name = "Other", UrlName = $"other-{Guid.NewGuid():N}"[..14],
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = outsider.Id,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = otherOrg, AppUserId = outsider.Id,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = outsider.Id,
            });
            await db.SaveChangesAsync();
        }

        var result = await BuildFeed(factory, outsider.Id).CreatePost(
            new CreateFeedPostRequest("stolen?", SourceCaseId: caseId), Render(), default);
        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        // One answer for missing and refused alike — the door confirms nothing.
        Assert.Contains("isn't available", refusal.Value!.ToString());
    }

    // ── The attribution decision ────────────────────────────────────────────

    private static async Task<Guid> PostFromCaseAsync(
        IDbContextFactory<BenDataContext> factory, Guid memberId, Guid caseId)
    {
        var result = await BuildFeed(factory, memberId).CreatePost(
            new CreateFeedPostRequest("footage", SourceCaseId: caseId), Render(), default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return ((FeedPostRecord)ok.Value!).Id;
    }

    [Fact]
    public async Task Unclaimed_attribution_emits_nothing_to_any_reader()
    {
        var (factory, member, _, caseId) = await SeedAsync(privateEngagement: false);
        var postId = await PostFromCaseAsync(factory, member.Id, caseId);

        // The author's own read — the most-entitled reader there is — still sees no org name:
        // the group has not agreed to be named, and absence is structural.
        var thread = await BuildFeed(factory, member.Id).GetThread(postId, default);
        var record = Assert.IsType<OkObjectResult>(thread.Result).Value as IReadOnlyList<FeedPostRecord>;
        Assert.Null(record![0].AttributedOrgName);
        Assert.Null(record[0].AttributedOrgUrlName);
        Assert.False(record[0].GroupVerified);
    }

    [Fact]
    public async Task Claiming_names_the_group_verifies_the_post_and_writes_the_example()
    {
        var (factory, member, orgId, caseId) = await SeedAsync(privateEngagement: false);

        // Give the post a category so the claim has something to confirm.
        Guid typeId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var categoryId = Guid.NewGuid();
            typeId = Guid.NewGuid();
            db.ExperienceCategories.Add(new ExperienceCategory
            {
                Id = categoryId, Name = "Visual", SortOrder = 1, IsActive = true, IsApproved = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = member.Id,
            });
            db.ExperienceTypes.Add(new ExperienceType
            {
                Id = typeId, ExperienceCategoryId = categoryId, Name = "Apparition",
                SortOrder = 1, IsActive = true, IsApproved = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = member.Id,
            });
            await db.SaveChangesAsync();
        }

        var create = await BuildFeed(factory, member.Id).CreatePost(
            new CreateFeedPostRequest("figure on the landing", SourceCaseId: caseId,
                ExperienceTypeId: typeId), Render(), default);
        var postId = ((FeedPostRecord)Assert.IsType<OkObjectResult>(create.Result).Value!).Id;

        var admin = BuildAttribution(factory, member.Id, isOrgAdmin: true);
        Assert.IsType<NoContentResult>(await admin.Claim(orgId, postId, default));

        await using var check = await factory.CreateDbContextAsync();
        var post = await check.OrgMessages.SingleAsync(m => m.Id == postId);
        Assert.Equal(OrgAttributionState.Claimed, post.AttributionState);
        Assert.Equal(member.Id, post.AttributionDecidedByAppUserId);

        var example = await check.FeedLabelledExamples.SingleAsync();
        Assert.Equal(FeedLabel.Confirmed, example.Label);
        Assert.Equal(FeedLabelSource.GroupClaim, example.Source);
        Assert.Equal(typeId, example.ExperienceTypeId);

        // And the reader now sees the name and the badge.
        var thread = await BuildFeed(factory, member.Id).GetThread(postId, default);
        var record = (IReadOnlyList<FeedPostRecord>)Assert.IsType<OkObjectResult>(thread.Result).Value!;
        Assert.Equal("Field Org", record[0].AttributedOrgName);
        Assert.True(record[0].GroupVerified);
    }

    [Fact]
    public async Task Declining_leaves_the_post_and_shows_nothing()
    {
        var (factory, member, orgId, caseId) = await SeedAsync(privateEngagement: false);
        var postId = await PostFromCaseAsync(factory, member.Id, caseId);

        var admin = BuildAttribution(factory, member.Id, isOrgAdmin: true);
        Assert.IsType<NoContentResult>(await admin.Decline(orgId, postId, default));

        await using var db = await factory.CreateDbContextAsync();
        var post = await db.OrgMessages.SingleAsync(m => m.Id == postId);
        Assert.Equal(OrgAttributionState.Declined, post.AttributionState);
        Assert.Null(post.HiddenUtc); // the post itself is untouched

        var thread = await BuildFeed(factory, member.Id).GetThread(postId, default);
        var record = (IReadOnlyList<FeedPostRecord>)Assert.IsType<OkObjectResult>(thread.Result).Value!;
        Assert.Null(record[0].AttributedOrgName);
        Assert.False(record[0].GroupVerified);
    }

    [Fact]
    public async Task A_plain_member_cannot_open_the_queue_or_decide()
    {
        var (factory, member, orgId, caseId) = await SeedAsync(privateEngagement: false);
        var postId = await PostFromCaseAsync(factory, member.Id, caseId);

        var notAdmin = BuildAttribution(factory, member.Id, isOrgAdmin: false);
        Assert.IsType<NotFoundResult>((await notAdmin.GetQueue(orgId, default)).Result);
        Assert.IsType<NotFoundResult>(await notAdmin.Claim(orgId, postId, default));
    }

    [Fact]
    public async Task Reclaiming_does_not_write_a_second_example()
    {
        var (factory, member, orgId, caseId) = await SeedAsync(privateEngagement: false);
        var postId = await PostFromCaseAsync(factory, member.Id, caseId);

        var admin = BuildAttribution(factory, member.Id, isOrgAdmin: true);
        await admin.Claim(orgId, postId, default);
        await admin.Claim(orgId, postId, default); // idempotent re-click

        await using var db = await factory.CreateDbContextAsync();
        // No category on this post, so zero examples — and crucially not two.
        Assert.Equal(0, await db.FeedLabelledExamples.CountAsync());
    }
}
