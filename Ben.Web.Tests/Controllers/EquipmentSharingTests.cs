using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Ben.Data.WebApi.Services;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Sharing personal equipment with a group (backlog item #55, phase 2).
/// </summary>
/// <remarks>
/// <para>The rules these tests exist to hold: you can only share into a group you actually belong
/// to; a share is <b>visibility only</b> and never carries the serial number; and a share stops
/// meaning anything the moment either party leaves the group, because membership is checked live
/// rather than inferred from the row still existing.</para>
///
/// <para>Sharing is deliberately gated on plain membership rather than a permission bit — the
/// owner's consent is what a share <i>is</i>. The group's Equipment permission governs the group's
/// own property, which arrives in phase 3.</para>
/// </remarks>
public class EquipmentSharingTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FellowMemberId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid ModelId, Guid ItemId);

    private static MyEquipmentController BuildMine(IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        return new MyEquipmentController(f, storage.Object, new Mock<IAuditLogService>().Object, BuildIngest(storage))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };
    }

    private static OrganizationEquipmentController BuildOrg(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, new Mock<IOrganizationSecurityService>().Object, new Mock<IFileStorageService>().Object, new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };

    /// <summary>
    /// One owner and one fellow member in OrgId; an outsider in neither; a second group the owner
    /// belongs to but the fellow member does not.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 {
                     (OwnerId, "The Owner"),
                     (FellowMemberId, "Fellow Member"),
                     (OutsiderId, "Outsider"),
                 })
        {
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
        }

        db.Organizations.Add(new Organization { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = OtherOrgId, Name = "Second Group", UrlName = "second-group", DateCreated = DateTime.UtcNow });

        foreach (var (orgId, userId) in new[]
                 {
                     (OrgId, OwnerId),
                     (OrgId, FellowMemberId),
                     (OtherOrgId, OwnerId),
                 })
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        var categoryId = Guid.NewGuid();
        var brandId    = Guid.NewGuid();
        var modelId    = Guid.NewGuid();
        var itemId     = Guid.NewGuid();

        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Zoom", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "H1n", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = itemId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "My H1n", SerialNumber = "SN-PRIVATE",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, modelId, itemId);
    }

    private static async Task ShareWithAsync(World w, Guid orgId)
    {
        var result = await BuildMine(w.Factory, OwnerId).SetShares(w.ItemId, new SetEquipmentSharesRequest([orgId]), default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Setting shares ────────────────────────────────────────────────────────

    /// <summary>
    /// A real ingest service over the mocked storage — tests exercise the actual sanitize/extract
    /// path rather than mocking past the thing phase 6a exists to guarantee.
    /// </summary>
    private static IMediaIngestService BuildIngest(Mock<IFileStorageService>? storage = null)
        => new MediaIngestService(
            (storage ?? new Mock<IFileStorageService>()).Object,
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            NullLogger<MediaIngestService>.Instance);

    [Fact]
    public async Task GetShares_ListsTheOwnersGroups_WithSharedFlags()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OrgId);

        var result = await BuildMine(w.Factory, OwnerId).GetShares(w.ItemId, default);
        var options = Assert.IsAssignableFrom<IEnumerable<EquipmentShareOptionRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Equal(2, options.Count);   // both groups the owner belongs to
        Assert.True(options.Single(o => o.OrganizationId == OrgId).IsShared);
        Assert.False(options.Single(o => o.OrganizationId == OtherOrgId).IsShared);
    }

    [Fact]
    public async Task SetShares_WithAGroupTheOwnerDoesNotBelongTo_IsRejected()
    {
        var w = await SeedAsync();
        var strangerOrgId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = strangerOrgId, Name = "Not Mine", UrlName = "not-mine", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var result = await BuildMine(w.Factory, OwnerId)
            .SetShares(w.ItemId, new SetEquipmentSharesRequest([strangerOrgId]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.Empty(await check.EquipmentItemShares.ToListAsync());
    }

    [Fact]
    public async Task SetShares_ForSomeoneElsesItem_ReturnsNotFound()
    {
        var w = await SeedAsync();
        var result = await BuildMine(w.Factory, FellowMemberId)
            .SetShares(w.ItemId, new SetEquipmentSharesRequest([OrgId]), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SetShares_ReplacesTheWholeSet_UnsharingOmittedGroups()
    {
        var w = await SeedAsync();
        await BuildMine(w.Factory, OwnerId).SetShares(w.ItemId, new SetEquipmentSharesRequest([OrgId, OtherOrgId]), default);

        await BuildMine(w.Factory, OwnerId).SetShares(w.ItemId, new SetEquipmentSharesRequest([OtherOrgId]), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var shares = await db.EquipmentItemShares.Where(s => s.EquipmentItemId == w.ItemId).ToListAsync();
        Assert.Single(shares);
        Assert.Equal(OtherOrgId, shares[0].OrganizationId);
    }

    [Fact]
    public async Task SetShares_TwiceWithTheSameGroup_DoesNotDuplicateTheRow()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OrgId);
        await ShareWithAsync(w, OrgId);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.EquipmentItemShares.CountAsync(s => s.EquipmentItemId == w.ItemId));
    }

    // ── Bulk sharing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BulkShare_SharesEveryNonRetiredItem_AndReportsWhatItDid()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = Guid.NewGuid(), OwnerAppUserId = OwnerId, EquipmentModelId = w.ModelId,
                DisplayName = "Second piece", DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = Guid.NewGuid(), OwnerAppUserId = OwnerId, EquipmentModelId = w.ModelId,
                DisplayName = "Retired piece", IsRetired = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await BuildMine(w.Factory, OwnerId)
            .BulkShare(new BulkEquipmentShareRequest(OrgId, true), default);
        var summary = Assert.IsType<BulkEquipmentShareResult>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, summary.ItemsAffected);   // the retired one is left out
        Assert.Equal(2, summary.TotalItems);
    }

    [Fact]
    public async Task BulkShare_IntoAGroupTheOwnerDoesNotBelongTo_IsRejected()
    {
        var w = await SeedAsync();
        var result = await BuildMine(w.Factory, OwnerId)
            .BulkShare(new BulkEquipmentShareRequest(Guid.NewGuid(), true), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task BulkUnshare_RemovesEveryShareWithThatGroup()
    {
        var w = await SeedAsync();
        await BuildMine(w.Factory, OwnerId).BulkShare(new BulkEquipmentShareRequest(OrgId, true), default);

        await BuildMine(w.Factory, OwnerId).BulkShare(new BulkEquipmentShareRequest(OrgId, false), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Empty(await db.EquipmentItemShares.Where(s => s.OrganizationId == OrgId).ToListAsync());
    }

    // ── What the group sees ───────────────────────────────────────────────────

    [Fact]
    public async Task SharedList_ShowsTheItemToAFellowMember_WithTheOwnersName()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OrgId);

        var result = await BuildOrg(w.Factory, FellowMemberId).GetSharedWithOrg(OrgId, default);
        var items = Assert.IsAssignableFrom<IEnumerable<SharedEquipmentItemRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        var item = Assert.Single(items);
        Assert.Equal("My H1n", item.DisplayName);
        Assert.Equal("The Owner", item.OwnerDisplayName);   // knowing whose gear it is, is the point
    }

    /// <summary>
    /// The serial stays with the owner even inside a group they shared the item with — asserted
    /// against the type, so a later projection change cannot quietly start carrying it.
    /// </summary>
    [Fact]
    public void SharedEquipmentItemRecord_HasNoSerialProperty()
    {
        var props = typeof(SharedEquipmentItemRecord).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(props, n => n.Contains("Serial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SharedList_IsNotFoundForSomeoneOutsideTheGroup()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OrgId);

        var result = await BuildOrg(w.Factory, OutsiderId).GetSharedWithOrg(OrgId, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SharedList_DoesNotShowItemsSharedWithADifferentGroup()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OtherOrgId);   // owner's other group; the fellow member isn't in it

        var result = await BuildOrg(w.Factory, FellowMemberId).GetSharedWithOrg(OrgId, default);
        var items = Assert.IsAssignableFrom<IEnumerable<SharedEquipmentItemRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }

    /// <summary>
    /// A share left behind after its owner leaves the group must stop granting visibility — the
    /// row alone is not the permission, live membership is.
    /// </summary>
    [Fact]
    public async Task SharedList_StopsShowingGearOnceTheOwnerLeavesTheGroup()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OrgId);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var membership = await db.OrganizationUserMemberships
                .SingleAsync(m => m.OrganizationId == OrgId && m.AppUserId == OwnerId);
            membership.IsActive = false;
            await db.SaveChangesAsync();
        }

        var result = await BuildOrg(w.Factory, FellowMemberId).GetSharedWithOrg(OrgId, default);
        var items = Assert.IsAssignableFrom<IEnumerable<SharedEquipmentItemRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);

        // The row itself is deliberately left alone — leaving a group is not a reason to destroy
        // the owner's own sharing choices, which should hold if they rejoin.
        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.Single(await check.EquipmentItemShares.Where(s => s.EquipmentItemId == w.ItemId).ToListAsync());
    }

    [Fact]
    public async Task SharedList_ExcludesRetiredItems()
    {
        var w = await SeedAsync();
        await ShareWithAsync(w, OrgId);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.ItemId);
            item.IsRetired = true;
            await db.SaveChangesAsync();
        }

        var result = await BuildOrg(w.Factory, FellowMemberId).GetSharedWithOrg(OrgId, default);
        var items = Assert.IsAssignableFrom<IEnumerable<SharedEquipmentItemRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(items);
    }

    // ── Photo bytes follow the same rule as the listing ──────────────────────

    private static EquipmentPhotoContentController BuildPhotos(IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        return new EquipmentPhotoContentController(f, storage.Object, BuildIngest(storage))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };
    }

    private static async Task<Guid> AddPhotoAsync(World w)
    {
        var photoId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();
        await using var db = await w.Factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = OwnerId,
            FileName = "gear.jpg", StoredFileName = "gear.jpg", ContentType = "image/jpeg",
            FileSize = 3, StoragePath = "fake/path.jpg",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.EquipmentItemPhotos.Add(new EquipmentItemPhoto
        {
            Id = photoId, EquipmentItemId = w.ItemId, UploadFileId = fileId, IsPrimary = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        await db.SaveChangesAsync();
        return photoId;
    }

    [Fact]
    public async Task PhotoBytes_AreServedToAFellowMemberOnceShared()
    {
        var w = await SeedAsync();
        var photoId = await AddPhotoAsync(w);

        // Before sharing, a fellow member is just another stranger to this item.
        Assert.IsType<NotFoundResult>(await BuildPhotos(w.Factory, FellowMemberId).GetContent(photoId, default));

        await ShareWithAsync(w, OrgId);

        Assert.IsType<FileStreamResult>(await BuildPhotos(w.Factory, FellowMemberId).GetContent(photoId, default));
    }

    [Fact]
    public async Task PhotoBytes_AreNotServedToSomeoneOutsideEveryGroupItIsSharedWith()
    {
        var w = await SeedAsync();
        var photoId = await AddPhotoAsync(w);
        await ShareWithAsync(w, OrgId);

        Assert.IsType<NotFoundResult>(await BuildPhotos(w.Factory, OutsiderId).GetContent(photoId, default));
    }

    /// <summary>
    /// Group gear has no OwnerAppUserId, so before phase 6a nothing matched IsOwner and an org
    /// item's photos were reachable by nobody but SuperAdmin — including the members whose group
    /// owns the thing.
    /// </summary>
    [Fact]
    public async Task PhotoBytes_OfGroupOwnedGear_AreServedToTheOwningGroupsMembers()
    {
        var w = await SeedAsync();
        var orgItemId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var modelId = await db.EquipmentModels.Select(m => m.Id).FirstAsync();
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = orgItemId, OwningOrganizationId = OrgId, OwnerAppUserId = null,
                EquipmentModelId = modelId, DisplayName = "Group thermal camera",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = OwnerId,
                FileName = "kit.jpg", StoredFileName = "kit.jpg", ContentType = "image/jpeg",
                FileSize = 3, StoragePath = "fake/path.jpg",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            db.EquipmentItemPhotos.Add(new EquipmentItemPhoto
            {
                Id = photoId, EquipmentItemId = orgItemId, UploadFileId = fileId, IsPrimary = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        // A member of the owning group can see its kit.
        Assert.IsType<FileStreamResult>(await BuildPhotos(w.Factory, FellowMemberId).GetContent(photoId, default));
        // Somebody outside the group still cannot.
        Assert.IsType<NotFoundResult>(await BuildPhotos(w.Factory, OutsiderId).GetContent(photoId, default));
    }

    [Fact]
    public async Task SharedList_CarriesTheLoanAudience_SoMembersSeeWhatIsActuallyBorrowable()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.ItemId);
            item.LoanAudience = EquipmentLoanAudience.SharedGroups;
            await db.SaveChangesAsync();
        }
        await ShareWithAsync(w, OrgId);

        var result = await BuildOrg(w.Factory, FellowMemberId).GetSharedWithOrg(OrgId, default);
        var items = Assert.IsAssignableFrom<IEnumerable<SharedEquipmentItemRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Equal(EquipmentLoanAudience.SharedGroups, items.Single().LoanAudience);
    }
}
