using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;
using MemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="OrganizationRoleController"/> — verifies CRUD for roles,
/// permission replacement, member assignment, and validation behaviour.
/// </summary>
public class OrganizationRoleControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrganizationRoleRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not OrganizationRole e) return new OrganizationRoleRecord { Name = "" };
             return new OrganizationRoleRecord
             {
                 Id = e.Id, OrganizationId = e.OrganizationId,
                 Name = e.Name, Description = e.Description,
                 IsActive = e.IsActive, SortOrder = e.SortOrder,
                 DateCreated = e.DateCreated, CreatedByAppUserId = e.CreatedByAppUserId,
             };
         });
        m.Setup(x => x.Map<IEnumerable<OrganizationRoleRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<OrganizationRole> list) return [];
             return list.Select(e => new OrganizationRoleRecord
             {
                 Id = e.Id, OrganizationId = e.OrganizationId,
                 Name = e.Name, IsActive = e.IsActive, SortOrder = e.SortOrder,
                 DateCreated = e.DateCreated, CreatedByAppUserId = e.CreatedByAppUserId,
             });
         });
        m.Setup(x => x.Map<IEnumerable<OrganizationRolePermissionRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<OrganizationRolePermission> list) return [];
             return list.Select(e => new OrganizationRolePermissionRecord
             {
                 Id = e.Id, OrganizationRoleId = e.OrganizationRoleId,
                 TableName = e.TableName, Actions = e.Actions,
                 DateCreated = e.DateCreated, CreatedByAppUserId = e.CreatedByAppUserId,
             });
         });
        m.Setup(x => x.Map<IEnumerable<OrganizationRoleMembershipRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<OrganizationRoleMembership> list) return [];
             return list.Select(e => new OrganizationRoleMembershipRecord
             {
                 Id = e.Id, OrganizationRoleId = e.OrganizationRoleId,
                 OrganizationUserMembershipId = e.OrganizationUserMembershipId,
                 DateCreated = e.DateCreated, CreatedByAppUserId = e.CreatedByAppUserId,
             });
         });
        m.Setup(x => x.Map<OrganizationRoleMembershipRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not OrganizationRoleMembership e) return new OrganizationRoleMembershipRecord();
             return new OrganizationRoleMembershipRecord
             {
                 Id = e.Id, OrganizationRoleId = e.OrganizationRoleId,
                 OrganizationUserMembershipId = e.OrganizationUserMembershipId,
                 DateCreated = e.DateCreated, CreatedByAppUserId = e.CreatedByAppUserId,
             };
         });
        return m.Object;
    }

    /// <summary>
    /// Builds the controller with a SuperAdmin user so IsCmsAuthorizedAsync always passes,
    /// isolating controller logic from security service behaviour.
    /// </summary>
    private static OrganizationRoleController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DataTable>(), It.IsAny<DataAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctrl = new OrganizationRoleController(
            factory, CreateMapper(), securityMock.Object, new Mock<IAuditLogService>().Object);

        // SuperAdmin role claim so IsCmsAuthorizedAsync short-circuits via IsInRole check
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "SuperAdmin")
        ], "Bearer");

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return ctrl;
    }

    private static async Task<(Guid orgId, Guid ownerId)> SeedOrgAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        var orgId   = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = ownerId, UserName = ownerId.ToString(), Email = $"{ownerId}@test.com" });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = "test-org",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
        });
        await db.SaveChangesAsync();
        return (orgId, ownerId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsRolesForOrg()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.AddRange(
                new OrganizationRole { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Designer", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId },
                new OrganizationRole { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Reviewer",  IsActive = true, SortOrder = 2, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.GetAll(orgId, CancellationToken.None);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IEnumerable<OrganizationRoleRecord>>(ok.Value);
        Assert.Equal(2, roles.Count());
    }

    [Fact]
    public async Task GetAll_DifferentOrg_DoesNotReturnOtherOrgRoles()
    {
        var factory  = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var otherOrgId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = otherOrgId, Name = "Other", UrlName = "other", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationRoles.Add(new OrganizationRole { Id = Guid.NewGuid(), OrganizationId = otherOrgId, Name = "OtherRole", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.GetAll(orgId, CancellationToken.None);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IEnumerable<OrganizationRoleRecord>>(ok.Value);
        Assert.Empty(roles);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.Create(orgId,
            new CreateOrgRoleRequest("Content Team", "Manages content", true, 1),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<OrganizationRoleRecord>(created.Value);
        Assert.Equal("Content Team", record.Name);
        Assert.Equal(orgId, record.OrganizationId);
    }

    [Fact]
    public async Task Create_BlankName_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.Create(orgId,
            new CreateOrgRoleRequest("  ", null, true, 1),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ValidRequest_UpdatesAndReturnsOk()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var roleId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(new OrganizationRole { Id = roleId, OrganizationId = orgId, Name = "Old Name", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.Update(orgId, roleId,
            new UpdateOrgRoleRequest("New Name", "Updated desc", false, 5),
            CancellationToken.None);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<OrganizationRoleRecord>(ok.Value);
        Assert.Equal("New Name", record.Name);
        Assert.False(record.IsActive);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.Update(orgId, Guid.NewGuid(),
            new UpdateOrgRoleRequest("X", null, true, 1),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingRole_ReturnsNoContent()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var roleId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(new OrganizationRole { Id = roleId, OrganizationId = orgId, Name = "ToDelete", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.Delete(orgId, roleId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.OrganizationRoles.AnyAsync(r => r.Id == roleId));
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.Delete(orgId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── SetPermissions ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetPermissions_ReplacesExistingPermissions()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var roleId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(new OrganizationRole { Id = roleId, OrganizationId = orgId, Name = "Role", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission { Id = Guid.NewGuid(), OrganizationRoleId = roleId, TableName = DataTable.OrganizationAddress, Actions = DataAction.Read, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.SetPermissions(orgId, roleId, [
            new SetRolePermissionRequest(DataTable.OrganizationEmail, DataAction.All),
            new SetRolePermissionRequest(DataTable.OrganizationPhone, DataAction.Read)
        ], CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var verify = await factory.CreateDbContextAsync();
        var perms = await verify.OrganizationRolePermissions
            .Where(p => p.OrganizationRoleId == roleId).ToListAsync();

        Assert.Equal(2, perms.Count);
        Assert.DoesNotContain(perms, p => p.TableName == DataTable.OrganizationAddress);
        Assert.Contains(perms, p => p.TableName == DataTable.OrganizationEmail);
        Assert.Contains(perms, p => p.TableName == DataTable.OrganizationPhone);
    }

    [Fact]
    public async Task SetPermissions_NoneActionsAreExcluded()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var roleId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(new OrganizationRole { Id = roleId, OrganizationId = orgId, Name = "Role", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        await ctrl.SetPermissions(orgId, roleId, [
            new SetRolePermissionRequest(DataTable.OrganizationEmail, DataAction.All),
            new SetRolePermissionRequest(DataTable.OrganizationPhone, DataAction.None) // None should be filtered out
        ], CancellationToken.None);

        await using var verify = await factory.CreateDbContextAsync();
        var perms = await verify.OrganizationRolePermissions.Where(p => p.OrganizationRoleId == roleId).ToListAsync();

        Assert.Single(perms);
        Assert.Equal(DataTable.OrganizationEmail, perms[0].TableName);
    }

    // ── Member management ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_ValidRequest_ReturnsCreated()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var roleId       = Guid.NewGuid();
        var memberId     = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = memberId, UserName = memberId.ToString(), Email = $"{memberId}@test.com" });
            db.OrganizationRoles.Add(new OrganizationRole { Id = roleId, OrganizationId = orgId, Name = "Role", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.AddMember(orgId, roleId,
            new AddRoleMemberRequest(membershipId), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.True(await verify.OrganizationRoleMemberships
            .AnyAsync(m => m.OrganizationRoleId == roleId && m.OrganizationUserMembershipId == membershipId));
    }

    [Fact]
    public async Task RemoveMember_ExistingMembership_ReturnsNoContent()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var roleId       = Guid.NewGuid();
        var memberId     = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var rmId         = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = memberId, UserName = memberId.ToString(), Email = $"{memberId}@test.com" });
            db.OrganizationRoles.Add(new OrganizationRole { Id = roleId, OrganizationId = orgId, Name = "Role", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = rmId, OrganizationRoleId = roleId,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = Build(factory, ownerId);
        var result = await ctrl.RemoveMember(orgId, roleId, rmId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.OrganizationRoleMemberships.AnyAsync(m => m.Id == rmId));
    }
}
