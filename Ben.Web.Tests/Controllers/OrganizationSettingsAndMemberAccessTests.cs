using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="OrganizationSettingsController"/> — GET and PUT org-level settings.
/// Tests for address member-access endpoints (GET/POST/DELETE) on
/// <see cref="OrganizationAddressCrudController"/>.
/// </summary>
public class OrganizationSettingsControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>Builds a SuperAdmin-authenticated OrganizationSettingsController.</summary>
    private static OrganizationSettingsController BuildSettings(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var security = new Mock<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ctrl = new OrganizationSettingsController(
            factory, new Mock<IMapper>().Object, security.Object,
            new Mock<IAuditLogService>().Object);

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                     new Claim(ClaimTypes.Role, "SuperAdmin")], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(Guid orgId, Guid ownerId)> SeedOrgAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        var orgId   = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = ownerId, UserName = ownerId.ToString(), Email = $"{ownerId}@t.com" });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
        await db.SaveChangesAsync();
        return (orgId, ownerId);
    }

    // ── OrganizationSettingsController ────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsDefaultSettings()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = BuildSettings(factory, ownerId);
        var result = await ctrl.Get(orgId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var s  = Assert.IsType<OrgSettingsResponse>(ok.Value);
        Assert.False(s.ShowAddressMap);
        Assert.False(s.ShowAddressDirections);
    }

    [Fact]
    public async Task Update_ShowAddressMap_PersistsToDatabase()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = BuildSettings(factory, ownerId);
        var result = await ctrl.Update(orgId,
            new OrgSettingsRequest(ShowAddressMap: true, ShowAddressDirections: false),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var verify = await factory.CreateDbContextAsync();
        var org = await verify.Organizations.FirstAsync(o => o.Id == orgId);
        Assert.True(org.ShowAddressMap);
        Assert.False(org.ShowAddressDirections);
    }

    [Fact]
    public async Task Update_BothFlags_PersistCorrectly()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = BuildSettings(factory, ownerId);
        await ctrl.Update(orgId,
            new OrgSettingsRequest(ShowAddressMap: true, ShowAddressDirections: true),
            CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var org = await verify.Organizations.FirstAsync(o => o.Id == orgId);
        Assert.True(org.ShowAddressMap);
        Assert.True(org.ShowAddressDirections);
    }

    [Fact]
    public async Task Get_AfterUpdate_ReturnsUpdatedValues()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl = BuildSettings(factory, ownerId);
        await ctrl.Update(orgId, new OrgSettingsRequest(true, true), CancellationToken.None);

        var result = await ctrl.Get(orgId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var s  = Assert.IsType<OrgSettingsResponse>(ok.Value);
        Assert.True(s.ShowAddressMap);
        Assert.True(s.ShowAddressDirections);
    }
}

// ── Address member-access endpoint tests ─────────────────────────────────────

public class OrganizationAddressMemberAccessTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static OrganizationAddressCrudController BuildCrud(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var security = new Mock<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ctrl = new OrganizationAddressCrudController(
            factory, new Mock<IMapper>().Object, security.Object,
            new Mock<IAuditLogService>().Object);

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                     new Claim(ClaimTypes.Role, "SuperAdmin")], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(Guid orgId, Guid ownerId, Guid addressId, Guid membershipId)>
        SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        var orgId       = Guid.NewGuid();
        var ownerId     = Guid.NewGuid();
        var memberId    = Guid.NewGuid();
        var addrId      = Guid.NewGuid();
        var typeId      = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.AddRange(
            new AppUser { Id = ownerId, UserName = ownerId.ToString(), Email = $"{ownerId}@t.com" },
            new AppUser { Id = memberId, UserName = memberId.ToString(), Email = $"{memberId}@t.com" });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
        });
        db.OrganizationAddressTypes.Add(new OrganizationAddressType { Id = typeId, Name = "Main", IsActive = true, IsPublic = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addrId, OrganizationId = orgId, OrganizationAddressTypeId = typeId,
            StreetAddress1 = "123 Main", City = "Nashville", State = "TN", ZipCode = "37000", Country = "US",
            Visibility = OrganizationAddressVisibility.SpecificMembers,
            PublicDisplayMode = OrganizationAddressDisplayMode.Hidden,
            MemberDisplayMode = OrganizationAddressDisplayMode.FullAddressAndMap,
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
        });
        await db.SaveChangesAsync();
        return (orgId, ownerId, addrId, membershipId);
    }

    [Fact]
    public async Task GetMemberAccess_WhenNoEntries_ReturnsEmpty()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, addrId, _) = await SeedAsync(factory);

        var ctrl   = BuildCrud(factory, ownerId);
        var result = await ctrl.GetMemberAccess(orgId, addrId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<Ben.Service.Models.Entities.OrganizationAddressMemberAccessRecord>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task AddMemberAccess_ValidRequest_CreatesEntry()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, addrId, membershipId) = await SeedAsync(factory);

        var ctrl   = BuildCrud(factory, ownerId);
        var result = await ctrl.AddMemberAccess(orgId, addrId,
            new AddAddressMemberAccessRequest(membershipId), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.True(await verify.OrganizationAddressMemberAccesses
            .AnyAsync(x => x.OrganizationAddressId == addrId && x.OrganizationUserMembershipId == membershipId));
    }

    [Fact]
    public async Task AddMemberAccess_Duplicate_ReturnsConflict()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, addrId, membershipId) = await SeedAsync(factory);

        var ctrl = BuildCrud(factory, ownerId);
        await ctrl.AddMemberAccess(orgId, addrId, new AddAddressMemberAccessRequest(membershipId), CancellationToken.None);

        var result2 = await ctrl.AddMemberAccess(orgId, addrId,
            new AddAddressMemberAccessRequest(membershipId), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result2.Result);
    }

    [Fact]
    public async Task RemoveMemberAccess_ExistingEntry_ReturnsNoContent()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, addrId, membershipId) = await SeedAsync(factory);

        var ctrl = BuildCrud(factory, ownerId);
        await ctrl.AddMemberAccess(orgId, addrId, new AddAddressMemberAccessRequest(membershipId), CancellationToken.None);

        // Read the created entry's ID directly from the DB
        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.OrganizationAddressMemberAccesses.FirstAsync(x => x.OrganizationAddressId == addrId);

        var del = await ctrl.RemoveMemberAccess(orgId, addrId, entry.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(del);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.OrganizationAddressMemberAccesses.AnyAsync(x => x.Id == entry.Id));
    }

    [Fact]
    public async Task RemoveMemberAccess_NotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, addrId, _) = await SeedAsync(factory);

        var ctrl   = BuildCrud(factory, ownerId);
        var result = await ctrl.RemoveMemberAccess(orgId, addrId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
