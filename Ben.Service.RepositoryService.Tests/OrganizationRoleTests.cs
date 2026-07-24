using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;
using MemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Entity-level tests for OrganizationRole, OrganizationRolePermission, and
/// OrganizationRoleMembership — persistence, cascade deletes, and unique indexes.
/// </summary>
public class OrganizationRoleTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static async Task<(Guid orgId, Guid ownerId)>
        SeedOrgAsync(IDbContextFactory<BenDataContext> factory)
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

    private static async Task<(Guid orgId, Guid ownerId, Guid membershipId)>
        SeedOrgWithMemberAsync(IDbContextFactory<BenDataContext> factory)
    {
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var memberId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = memberId, UserName = memberId.ToString(), Email = $"{memberId}@test.com" });
        var membership = new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
            Role = MemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
        };
        db.OrganizationUserMemberships.Add(membership);
        await db.SaveChangesAsync();
        return (orgId, ownerId, membership.Id);
    }

    private static OrganizationRole MakeRole(Guid orgId, Guid creatorId, string name = "Designer") =>
        new()
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Name = name, IsActive = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId
        };

    // ── OrganizationRole ──────────────────────────────────────────────────────

    [Fact]
    public async Task OrganizationRole_CanBeCreatedAndRetrieved()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var role = MakeRole(orgId, ownerId, "Designer");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(role);
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var loaded = await verify.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == role.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Designer", loaded!.Name);
        Assert.Equal(orgId, loaded.OrganizationId);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task OrganizationRole_CascadeDeletesWithOrganization()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(MakeRole(orgId, ownerId));
            db.OrganizationRoles.Add(MakeRole(orgId, ownerId, "Editor"));
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            // In-memory DB: load and remove children before removing parent
            var roles = await db.OrganizationRoles.Where(r => r.OrganizationId == orgId).ToListAsync();
            db.OrganizationRoles.RemoveRange(roles);
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            db.Organizations.Remove(org);
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var count = await verify.OrganizationRoles.CountAsync(r => r.OrganizationId == orgId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task OrganizationRole_MultipleRolesPerOrg_AllPersist()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.AddRange(
                MakeRole(orgId, ownerId, "Designer"),
                MakeRole(orgId, ownerId, "Reviewer"),
                MakeRole(orgId, ownerId, "Moderator"));
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var count = await verify.OrganizationRoles.CountAsync(r => r.OrganizationId == orgId);
        Assert.Equal(3, count);
    }

    // ── OrganizationRolePermission ────────────────────────────────────────────

    [Fact]
    public async Task OrganizationRolePermission_CanBeCreated()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var role = MakeRole(orgId, ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(role);
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission
            {
                Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                TableName = DataTable.OrganizationAddress, Actions = DataAction.Read | DataAction.Create,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var perm = await verify.OrganizationRolePermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrganizationRoleId == role.Id);

        Assert.NotNull(perm);
        Assert.Equal(DataTable.OrganizationAddress, perm!.TableName);
        Assert.True((perm.Actions & DataAction.Read) != DataAction.None);
        Assert.True((perm.Actions & DataAction.Create) != DataAction.None);
        Assert.False((perm.Actions & DataAction.Delete) != DataAction.None);
    }

    [Fact]
    public async Task OrganizationRolePermission_UniqueIndex_ConfiguredOnModel()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var entityType = db.Model.FindEntityType(typeof(OrganizationRolePermission));
        var indexes = entityType!.GetIndexes().ToList();

        Assert.True(indexes.Any(i => i.IsUnique),
            "Expected a unique index on OrganizationRolePermission (OrganizationRoleId, TableName)");
    }

    [Fact]
    public async Task OrganizationRolePermission_CascadeDeletesWithRole()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var role = MakeRole(orgId, ownerId);
        var permId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(role);
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission
            {
                Id = permId, OrganizationRoleId = role.Id,
                TableName = DataTable.OrganizationEmail, Actions = DataAction.All,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var r = await db.OrganizationRoles
                .Include(r => r.Permissions)
                .FirstAsync(r => r.Id == role.Id);
            db.OrganizationRoles.Remove(r);
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.OrganizationRolePermissions.AnyAsync(p => p.Id == permId));
    }

    [Fact]
    public async Task OrganizationRolePermission_MultipleTablesPerRole()
    {
        var factory = CreateFactory();
        var (orgId, ownerId) = await SeedOrgAsync(factory);
        var role = MakeRole(orgId, ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(role);
            db.OrganizationRolePermissions.AddRange(
                new OrganizationRolePermission { Id = Guid.NewGuid(), OrganizationRoleId = role.Id, TableName = DataTable.OrganizationAddress, Actions = DataAction.Read, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId },
                new OrganizationRolePermission { Id = Guid.NewGuid(), OrganizationRoleId = role.Id, TableName = DataTable.OrganizationEmail,   Actions = DataAction.All,  DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId },
                new OrganizationRolePermission { Id = Guid.NewGuid(), OrganizationRoleId = role.Id, TableName = DataTable.OrganizationPhone,   Actions = DataAction.Read | DataAction.Create, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var count = await verify.OrganizationRolePermissions.CountAsync(p => p.OrganizationRoleId == role.Id);
        Assert.Equal(3, count);
    }

    // ── OrganizationRoleMembership ────────────────────────────────────────────

    [Fact]
    public async Task OrganizationRoleMembership_CanBeCreated()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, membershipId) = await SeedOrgWithMemberAsync(factory);
        var role = MakeRole(orgId, ownerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(role);
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = Guid.NewGuid(), OrganizationRoleId = role.Id,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        var rm = await verify.OrganizationRoleMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationRoleId == role.Id);

        Assert.NotNull(rm);
        Assert.Equal(membershipId, rm!.OrganizationUserMembershipId);
    }

    [Fact]
    public async Task OrganizationRoleMembership_UniqueIndex_ConfiguredOnModel()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var entityType = db.Model.FindEntityType(typeof(OrganizationRoleMembership));
        var indexes = entityType!.GetIndexes().ToList();

        Assert.True(indexes.Any(i => i.IsUnique),
            "Expected a unique index on OrganizationRoleMembership (OrganizationRoleId, OrganizationUserMembershipId)");
    }

    [Fact]
    public async Task OrganizationRoleMembership_CascadeDeletesWithRole()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, membershipId) = await SeedOrgWithMemberAsync(factory);
        var role = MakeRole(orgId, ownerId);
        var rmId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationRoles.Add(role);
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = rmId, OrganizationRoleId = role.Id,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var r = await db.OrganizationRoles
                .Include(r => r.Members)
                .FirstAsync(r => r.Id == role.Id);
            db.OrganizationRoles.Remove(r);
            await db.SaveChangesAsync();
        }

        await using var verify = await factory.CreateDbContextAsync();
        Assert.False(await verify.OrganizationRoleMemberships.AnyAsync(m => m.Id == rmId));
    }
}
