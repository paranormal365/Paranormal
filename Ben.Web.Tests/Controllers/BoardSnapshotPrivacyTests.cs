using System.Security.Claims;
using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A published board snapshot stays inside the case (canvas plan review R22).
/// </summary>
/// <remarks>
/// <para>The snapshot is a picture of the team's working board: witness names, a client's home
/// address, pinned places. A case file reaches a visitor by exactly two routes — a timeline entry
/// made Public on a public case, and the public-page media rule built on that — so both refuse a
/// Board Snapshot upload, and the refusal says why in a sentence an investigator can act on.</para>
/// </remarks>
public sealed class BoardSnapshotPrivacyTests
{
    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId, Guid AuthorId, Guid EntryId, Guid SnapshotId, Guid PhotoId);

    private static async Task<World> SeedAsync(bool withSnapshot = true)
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = authorId, UserName = "a@t.com", Email = "a@t.com", DisplayName = "Author", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Night Watch", UrlName = "nw", DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = authorId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Henderson", IsPublic = true, Status = CaseStatus.Public,
            StreetAddress1 = "1 Elm", City = "Franklin", State = "TN", ZipCode = "37064",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = entryId, CaseId = caseId, AuthorAppUserId = authorId, Title = "The board so far",
            EntryType = CaseTimelineEntryType.InvestigatorNote, Visibility = CaseTimelineVisibility.OrgOnly,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = photoId, UploadFileTypeId = Guid.NewGuid(), AppUserId = authorId, FileName = "hallway.jpg",
            StoredFileName = "h.jpg", ContentType = "image/jpeg", FileSize = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.CaseTimelineEntryFiles.Add(new CaseTimelineEntryFile
        {
            Id = Guid.NewGuid(), CaseTimelineEntryId = entryId, UploadFileId = photoId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        if (withSnapshot)
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = snapshotId, UploadFileTypeId = UploadFileTypeSeeder.BoardSnapshotFileTypeId, AppUserId = authorId,
                FileName = "Henderson board.png", StoredFileName = "b.png", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
            });
            db.CaseTimelineEntryFiles.Add(new CaseTimelineEntryFile
            {
                Id = Guid.NewGuid(), CaseTimelineEntryId = entryId, UploadFileId = snapshotId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
            });
        }
        await db.SaveChangesAsync();
        return new World(factory, orgId, caseId, authorId, entryId, snapshotId, photoId);
    }

    private static CaseController BuildCaseController(World w)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<CaseTimelineEntryRecord>(It.IsAny<object>()))
            .Returns<object>(o => new CaseTimelineEntryRecord { Id = ((CaseTimelineEntry)o).Id, Title = ((CaseTimelineEntry)o).Title });
        var ctrl = new CaseController(w.Factory, mapper.Object,
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(w.Factory),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory),
            new RequestReviewNotifier(w.Factory, new PlatformMessageService(w.Factory)),
            TestMailer.Quiet(),
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, w.AuthorId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static UpsertTimelineEntryRequest Request(CaseTimelineVisibility visibility)
        => new(CaseTimelineEntryType.InvestigatorNote, null, "The board so far", null, visibility, []);

    [Fact]
    public async Task A_timeline_entry_holding_a_board_snapshot_cannot_be_made_public()
    {
        var w = await SeedAsync();

        var result = await BuildCaseController(w).UpdateTimelineEntry(w.OrgId, w.CaseId, w.EntryId, Request(CaseTimelineVisibility.Public), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(BoardSnapshots.StaysInsideTheCase, refused.Value);
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(CaseTimelineVisibility.OrgOnly, (await db.CaseTimelineEntries.SingleAsync()).Visibility);
    }

    [Theory]
    [InlineData(CaseTimelineVisibility.OrgOnly)]
    [InlineData(CaseTimelineVisibility.Client)]
    public async Task The_same_entry_may_still_be_shared_inside_the_case(CaseTimelineVisibility visibility)
    {
        var w = await SeedAsync();

        var result = await BuildCaseController(w).UpdateTimelineEntry(w.OrgId, w.CaseId, w.EntryId, Request(visibility), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task An_entry_without_a_snapshot_is_made_public_as_before()
    {
        var w = await SeedAsync(withSnapshot: false);

        var result = await BuildCaseController(w).UpdateTimelineEntry(w.OrgId, w.CaseId, w.EntryId, Request(CaseTimelineVisibility.Public), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_public_page_is_never_offered_a_board_snapshot_even_on_a_public_entry()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            // However it got there — an older row, a direct edit — the publication rule still refuses.
            (await db.CaseTimelineEntries.SingleAsync()).Visibility = CaseTimelineVisibility.Public;
            await db.SaveChangesAsync();
        }

        await using var read = await w.Factory.CreateDbContextAsync();
        var offered = await CaseMediaPublication.PublishableAsync(read, w.CaseId, default);

        Assert.Contains(offered, f => f.UploadFileId == w.PhotoId);
        Assert.DoesNotContain(offered, f => f.UploadFileId == w.SnapshotId);
        Assert.False(await CaseMediaPublication.MayPublishAsync(read, w.CaseId, w.SnapshotId, default));
        Assert.True(await CaseMediaPublication.MayPublishAsync(read, w.CaseId, w.PhotoId, default));
    }
}
