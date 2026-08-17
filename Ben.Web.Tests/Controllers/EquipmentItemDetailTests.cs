using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
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
/// The item page's audience matrix (item #55, phase 6b) — one endpoint serving owners, group
/// members, passers-by and administrators.
/// </summary>
/// <remarks>
/// <para>The thing worth testing is not that each audience sees something, but that the parts they
/// must not see are <b>absent from the payload</b> rather than merely unrendered. Each nullable
/// sub-record either arrives or does not; a client that ignored every flag would still learn
/// nothing extra.</para>
/// </remarks>
public class EquipmentItemDetailTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid PersonalItemId, Guid OrgItemId, Guid PrivateItemId);

    private static EquipmentItemDetailController Build(
        IDbContextFactory<BenDataContext> f, Guid? userId, Guid? equipmentPermissionHolder = null)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                    It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid u, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _)
                    => equipmentPermissionHolder is not null && u == equipmentPermissionHolder);

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer");

        return new EquipmentItemDetailController(f, security.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 { (OwnerId, "The Owner"), (MemberId, "A Member"), (AdminId, "The Admin"), (StrangerId, "Stranger") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        foreach (var (userId, role) in new[]
                 {
                     (OwnerId, OrganizationMemberRole.Member),
                     (MemberId, OrganizationMemberRole.Member),
                     (AdminId, OrganizationMemberRole.Administrator),
                 })
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
                Role = role, IsActive = true, DateCreated = DateTime.UtcNow,
            });

        var categoryId = Guid.NewGuid(); var brandId = Guid.NewGuid(); var modelId = Guid.NewGuid();
        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Zoom", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "H1n", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var personalId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = personalId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "My H1n", SerialNumber = "SN-PERSONAL", IncludeInGlobalCatalog = true,
            ViewCount = 7, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var orgItemId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = orgItemId, OwningOrganizationId = OrgId, EquipmentModelId = modelId,
            DisplayName = "Group recorder", SerialNumber = "SN-GROUP",
            CurrentHolderAppUserId = MemberId, ViewCount = 12, LinkClickCount = 4,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        var privateId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = privateId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "Kept private", SerialNumber = "SN-PRIVATE",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, personalId, orgItemId, privateId);
    }

    private static async Task<EquipmentItemDetailRecord> GetAsync(World w, Guid itemId, Guid? viewer, Guid? permHolder = null)
    {
        var result = await Build(w.Factory, viewer, permHolder).GetItem(itemId, default);
        return Assert.IsType<EquipmentItemDetailRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ── Who sees what ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAnonymousVisitorSeesAPubliclyListedPieceWithoutItsOwnerOrSerial()
    {
        var w = await SeedAsync();
        var detail = await GetAsync(w, w.PersonalItemId, viewer: null);

        Assert.Equal("My H1n", detail.DisplayName);
        // The sections they are not entitled to are absent, not nulled field by field.
        Assert.Null(detail.Ownership);
        Assert.Null(detail.Management);
        Assert.Null(detail.Counters);
        Assert.False(detail.Flags.CanEdit);
    }

    [Fact]
    public async Task AnUnlistedPieceIsNotFoundForAStranger()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, StrangerId).GetItem(w.PrivateItemId, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TheOwnerSeesEverythingAboutTheirOwn_ExceptTheirCounters()
    {
        var w = await SeedAsync();
        var detail = await GetAsync(w, w.PrivateItemId, OwnerId);

        Assert.Equal("SN-PRIVATE", detail.Management!.SerialNumber);
        Assert.Equal(OwnerId, detail.Ownership!.OwnerAppUserId);
        Assert.True(detail.Flags.IsOwner);
        Assert.True(detail.Flags.CanRetire);
        // Interest numbers are for administrators — an owner does not see their own.
        Assert.Null(detail.Counters);
    }

    [Fact]
    public async Task AGroupMemberSeesTheGroupsGearAndItsHolder_ButNotItsSerial()
    {
        var w = await SeedAsync();
        var detail = await GetAsync(w, w.OrgItemId, MemberId, permHolder: AdminId);

        Assert.Equal(OrgId, detail.Ownership!.OwningOrganizationId);
        Assert.Equal("Ghost Squad", detail.Ownership.OwningOrganizationName);
        Assert.Null(detail.Management);      // serial and condition are the custodians' business
        Assert.Null(detail.Counters);
        Assert.False(detail.Flags.CanEdit);
    }

    [Fact]
    public async Task SomeoneWithTheEquipmentPermissionSeesTheSerialAndCanEdit()
    {
        var w = await SeedAsync();
        var detail = await GetAsync(w, w.OrgItemId, MemberId, permHolder: MemberId);

        Assert.Equal("SN-GROUP", detail.Management!.SerialNumber);
        Assert.Equal("A Member", detail.Management.CurrentHolderDisplayName);
        Assert.True(detail.Flags.CanEdit);
    }

    /// <summary>
    /// Counters follow the membership role, not the equipment permission — Ben's audience is
    /// administrators, and a group may give its equipment role to somebody who is not one.
    /// </summary>
    [Fact]
    public async Task OnlyAnAdministratorSeesTheInterestNumbers()
    {
        var w = await SeedAsync();

        var asPermissionHolder = await GetAsync(w, w.OrgItemId, MemberId, permHolder: MemberId);
        Assert.Null(asPermissionHolder.Counters);

        var asAdministrator = await GetAsync(w, w.OrgItemId, AdminId, permHolder: MemberId);
        Assert.Equal(12, asAdministrator.Counters!.ViewCount);
        Assert.Equal(4, asAdministrator.Counters.LinkClickCount);
    }

    [Fact]
    public async Task RetiringAPublicPieceClosesThePublicRoute()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.PersonalItemId);
            item.IsRetired = true;
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(w.Factory, userId: null).GetItem(w.PersonalItemId, default)).Result);
        // Its owner can still reach it — retiring is not losing it.
        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId).GetItem(w.PersonalItemId, default)).Result);
    }

    [Fact]
    public async Task AMemberOfAGroupTheItemIsSharedWithCanSeeIt()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentItemShares.Add(new EquipmentItemShare
            {
                Id = Guid.NewGuid(), EquipmentItemId = w.PrivateItemId, OrganizationId = OrgId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var detail = await GetAsync(w, w.PrivateItemId, MemberId);
        Assert.Equal("The Owner", detail.Ownership!.OwnerDisplayName);   // knowing whose it is, is the point
        Assert.Null(detail.Management);                                   // the serial still is not
    }
}
