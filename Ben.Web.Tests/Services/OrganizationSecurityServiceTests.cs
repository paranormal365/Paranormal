using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for <see cref="OrganizationSecurityService.UpsertMembershipAsync"/> — Phase-A fix for a
/// privilege-escalation gap. Before the fix, <c>EnsureCanManageOrganizationAsync</c> treated
/// <c>Owner</c> and <c>Administrator</c> as equally authorized to manage membership, with no
/// further check on what a non-Owner caller could actually set: a non-Owner <c>Administrator</c>
/// could self-promote to <c>Owner</c>, or demote/deactivate the real <c>Owner</c>.
/// </summary>
public class OrganizationSecurityServiceTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static async Task<Guid> SeedUserAsync(IDbContextFactory<BenDataContext> factory, string label)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = id, UserName = $"{label}@test.com", NormalizedUserName = $"{label}@TEST.COM".ToUpperInvariant(),
            Email = $"{label}@test.com", NormalizedEmail = $"{label}@TEST.COM".ToUpperInvariant(),
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedOrgAsync(IDbContextFactory<BenDataContext> factory, Guid ownerId)
    {
        var orgId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = $"test-org-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return orgId;
    }

    private static async Task AddMembershipAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId,
        OrganizationMemberRole role, bool isActive = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = role, IsActive = isActive, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    private static async Task MakeSuperAdminAsync(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new IdentityRole<Guid> { Id = roleId, Name = RoleNames.SuperAdmin, NormalizedName = RoleNames.SuperAdmin.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    // ── The actual attack: Administrator self-promotes to Owner ────────────────

    [Fact]
    public async Task Administrator_CannotSelfPromoteToOwner()
    {
        var factory = CreateFactory();
        var ownerId = await SeedUserAsync(factory, "owner");
        var orgId   = await SeedOrgAsync(factory, ownerId);
        var adminId = await SeedUserAsync(factory, "admin");
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);

        var service = new OrganizationSecurityService(factory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpsertMembershipAsync(orgId, adminId, OrganizationMemberRole.Owner, true, adminId));
    }

    [Fact]
    public async Task Administrator_CannotPromoteSomeoneElseToOwner()
    {
        var factory = CreateFactory();
        var ownerId  = await SeedUserAsync(factory, "owner");
        var orgId    = await SeedOrgAsync(factory, ownerId);
        var adminId  = await SeedUserAsync(factory, "admin");
        var targetId = await SeedUserAsync(factory, "target");
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);

        var service = new OrganizationSecurityService(factory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpsertMembershipAsync(orgId, targetId, OrganizationMemberRole.Owner, true, adminId));
    }

    [Fact]
    public async Task Administrator_CannotDemoteOrDeactivateTheRealOwner()
    {
        var factory = CreateFactory();
        var ownerId = await SeedUserAsync(factory, "owner");
        var orgId   = await SeedOrgAsync(factory, ownerId);
        var adminId = await SeedUserAsync(factory, "admin");
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);

        var service = new OrganizationSecurityService(factory);

        // Try to demote the Owner to Member.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpsertMembershipAsync(orgId, ownerId, OrganizationMemberRole.Member, true, adminId));

        // Try to deactivate the Owner outright.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpsertMembershipAsync(orgId, ownerId, OrganizationMemberRole.Owner, false, adminId));
    }

    // ── Hierarchy: Administrator can only manage roles strictly below Administrator ───

    [Fact]
    public async Task Administrator_CannotGrantAdministratorToAnotherUser()
    {
        var factory  = CreateFactory();
        var ownerId  = await SeedUserAsync(factory, "owner");
        var orgId    = await SeedOrgAsync(factory, ownerId);
        var adminId  = await SeedUserAsync(factory, "admin");
        var targetId = await SeedUserAsync(factory, "target");
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);

        var service = new OrganizationSecurityService(factory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpsertMembershipAsync(orgId, targetId, OrganizationMemberRole.Administrator, true, adminId));
    }

    [Fact]
    public async Task Administrator_CanGrantManagerRole_ToNewMember()
    {
        var factory  = CreateFactory();
        var ownerId  = await SeedUserAsync(factory, "owner");
        var orgId    = await SeedOrgAsync(factory, ownerId);
        var adminId  = await SeedUserAsync(factory, "admin");
        var targetId = await SeedUserAsync(factory, "target");
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);

        var service = new OrganizationSecurityService(factory);

        var result = await service.UpsertMembershipAsync(orgId, targetId, OrganizationMemberRole.Manager, true, adminId);

        Assert.Equal(OrganizationMemberRole.Manager, result.Role);
    }

    [Fact]
    public async Task Administrator_CannotModifyAnotherAdministrators_Membership()
    {
        var factory   = CreateFactory();
        var ownerId   = await SeedUserAsync(factory, "owner");
        var orgId     = await SeedOrgAsync(factory, ownerId);
        var adminId   = await SeedUserAsync(factory, "admin");
        var peerAdminId = await SeedUserAsync(factory, "peer-admin");
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);
        await AddMembershipAsync(factory, orgId, peerAdminId, OrganizationMemberRole.Administrator);

        var service = new OrganizationSecurityService(factory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpsertMembershipAsync(orgId, peerAdminId, OrganizationMemberRole.Member, false, adminId));
    }

    // ── Owner retains full control ──────────────────────────────────────────────

    [Fact]
    public async Task Owner_CanPromoteMemberToAdministrator()
    {
        var factory  = CreateFactory();
        var ownerId  = await SeedUserAsync(factory, "owner");
        var orgId    = await SeedOrgAsync(factory, ownerId);
        var targetId = await SeedUserAsync(factory, "target");
        await AddMembershipAsync(factory, orgId, targetId, OrganizationMemberRole.Member);

        var service = new OrganizationSecurityService(factory);

        var result = await service.UpsertMembershipAsync(orgId, targetId, OrganizationMemberRole.Administrator, true, ownerId);

        Assert.Equal(OrganizationMemberRole.Administrator, result.Role);
    }

    // ── Last-Owner guard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_CannotDeactivateSelf_AsTheLastActiveOwner()
    {
        var factory = CreateFactory();
        var ownerId = await SeedUserAsync(factory, "owner");
        var orgId   = await SeedOrgAsync(factory, ownerId);

        var service = new OrganizationSecurityService(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpsertMembershipAsync(orgId, ownerId, OrganizationMemberRole.Owner, false, ownerId));
    }

    [Fact]
    public async Task SuperAdmin_CanDeactivateTheLastOwner_IfASecondActiveOwnerExists()
    {
        var factory   = CreateFactory();
        var ownerId   = await SeedUserAsync(factory, "owner");
        var orgId     = await SeedOrgAsync(factory, ownerId);
        var secondOwnerId = await SeedUserAsync(factory, "second-owner");
        await MakeSuperAdminAsync(factory, secondOwnerId);
        // SuperAdmin path grants Owner directly (bypasses the non-SuperAdmin "never assign Owner" rule).
        var service = new OrganizationSecurityService(factory);
        await service.UpsertMembershipAsync(orgId, secondOwnerId, OrganizationMemberRole.Owner, true, secondOwnerId);

        var result = await service.UpsertMembershipAsync(orgId, ownerId, OrganizationMemberRole.Owner, false, secondOwnerId);

        Assert.False(result.IsActive);
    }

    // ── SuperAdmin retains full control ─────────────────────────────────────────

    [Fact]
    public async Task SuperAdmin_CanGrantOwnerRole()
    {
        var factory  = CreateFactory();
        var ownerId  = await SeedUserAsync(factory, "owner");
        var orgId    = await SeedOrgAsync(factory, ownerId);
        var adminId  = await SeedUserAsync(factory, "super");
        await MakeSuperAdminAsync(factory, adminId);

        var service = new OrganizationSecurityService(factory);

        var result = await service.UpsertMembershipAsync(orgId, adminId, OrganizationMemberRole.Owner, true, adminId);

        Assert.Equal(OrganizationMemberRole.Owner, result.Role);
    }
}
