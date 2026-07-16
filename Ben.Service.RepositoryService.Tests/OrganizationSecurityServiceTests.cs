using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Security.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

using SecurityAction    = Ben.Data.Common.Enums.OrganizationSecurityAction;
using SecurityTable     = Ben.Service.Security.Enums.OrganizationSecurityTable;
using DataCommonTable   = Ben.Data.Common.Enums.OrganizationSecurityTable;
using MemberRole        = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.RepositoryService.Tests;

public class OrganizationSecurityServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static async Task<BenDataContext> GetDbAsync(IDbContextFactory<BenDataContext> factory)
        => await factory.CreateDbContextAsync();

    /// <summary>Creates a membership record. isAdmin=true maps to Owner role.</summary>
    private static OrganizationUserMembership Membership(Guid orgId, Guid userId, bool isAdmin = false) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        AppUserId = userId,
        Role = isAdmin ? MemberRole.Owner : MemberRole.Member,
        IsActive = true,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = userId
    };

    private static OrganizationAccessGrant Grant(Guid orgId, Guid userId,
        SecurityTable secTable, SecurityAction secAction) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        AppUserId = userId,
        TableName = (OrganizationSecurityTable)secTable,
        Actions = (OrganizationSecurityAction)secAction,
        DateCreated = DateTime.UtcNow,
        CreatedByAppUserId = userId
    };

    // ── IsMemberAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IsMember_WhenMembershipExists_ReturnsTrue()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.IsMemberAsync(userId, orgId);

        Assert.True(result);
    }

    [Fact]
    public async Task IsMember_WhenNoMembership_ReturnsFalse()
    {
        var factory = CreateFactory();
        var svc     = new OrganizationSecurityService(factory);

        var result = await svc.IsMemberAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }

    // ── IsOwnerAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IsOwner_WhenRoleIsOwner_ReturnsTrue()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: true));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.IsOwnerAsync(userId, orgId);

        Assert.True(result);
    }

    [Fact]
    public async Task IsOwner_WhenRegularMember_ReturnsFalse()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: false));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.IsOwnerAsync(userId, orgId);

        Assert.False(result);
    }

    // ── HasPermissionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task HasPermission_WhenUserIsOwner_ReturnsTrue_WithoutGrant()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            // Owner — no explicit grant seeded
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: true));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.HasPermissionAsync(
            userId, orgId,
            SecurityTable.Organization,
            SecurityAction.Delete);

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermission_WhenUserHasMatchingGrant_ReturnsTrue()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: false));
            db.OrganizationAccessGrants.Add(Grant(orgId, userId,
                SecurityTable.Organization,
                SecurityAction.Read));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.HasPermissionAsync(
            userId, orgId,
            SecurityTable.Organization,
            SecurityAction.Read);

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermission_WhenNoGrant_ReturnsFalse()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: false));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.HasPermissionAsync(
            userId, orgId,
            SecurityTable.Organization,
            SecurityAction.Delete);

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermission_WhenGrantForDifferentAction_ReturnsFalse()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: false));
            // Grant only Read — not Delete
            db.OrganizationAccessGrants.Add(Grant(orgId, userId,
                SecurityTable.Organization,
                SecurityAction.Read));
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.HasPermissionAsync(
            userId, orgId,
            SecurityTable.Organization,
            SecurityAction.Delete);

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermission_WhenGrantHasMultipleFlags_EachFlagCheckedIndividually()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: false));
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                TableName = DataCommonTable.Organization,
                Actions = SecurityAction.Read | SecurityAction.Delete,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);

        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.Organization, SecurityAction.Read));
        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.Organization, SecurityAction.Delete));
        Assert.False(await svc.HasPermissionAsync(userId, orgId, SecurityTable.Organization, SecurityAction.Create));
    }

    [Fact]
    public async Task HasPermission_WhenGrantActionsIsNone_ReturnsFalse()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId, isAdmin: false));
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                TableName = DataCommonTable.Organization,
                Actions = SecurityAction.None,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);
        Assert.False(await svc.HasPermissionAsync(userId, orgId, SecurityTable.Organization, SecurityAction.Read));
    }

    // ── GetUserOrganizationsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetUserOrganizations_ReturnsOnlyActiveOrgIds()
    {
        var factory   = CreateFactory();
        var userId    = Guid.NewGuid();
        var activeOrg = Guid.NewGuid();
        var inactOrg  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(activeOrg, userId, isAdmin: false));
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = inactOrg, AppUserId = userId,
                IsActive = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var svc    = new OrganizationSecurityService(factory);
        var result = await svc.GetUserOrganizationsAsync(userId);

        Assert.Single(result);
        Assert.Contains(activeOrg, result);
        Assert.DoesNotContain(inactOrg, result);
    }

    // ── GrantAccessAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GrantAccessAsync_CreatesGrant_AllowsPermission()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);
        await svc.GrantAccessAsync(orgId, userId,
            SecurityTable.OrganizationNote, SecurityAction.Read, userId);

        var hasPermission = await svc.HasPermissionAsync(
            userId, orgId, SecurityTable.OrganizationNote, SecurityAction.Read);

        Assert.True(hasPermission);
    }

    [Fact]
    public async Task GrantAccessAsync_WhenGrantPreviouslyRevoked_ReEnablesPermission()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        // Seed a revoked grant
        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            db.OrganizationAccessGrants.Add(new Data.Source.Entities.OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                TableName = DataCommonTable.OrganizationNote,
                Actions = OrganizationSecurityAction.None,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);

        // Permission should be denied before re-grant
        Assert.False(await svc.HasPermissionAsync(
            userId, orgId, SecurityTable.OrganizationNote, SecurityAction.Read));

        await svc.GrantAccessAsync(orgId, userId,
            SecurityTable.OrganizationNote, SecurityAction.Read, userId);

        Assert.True(await svc.HasPermissionAsync(
            userId, orgId, SecurityTable.OrganizationNote, SecurityAction.Read));
    }

    [Fact]
    public async Task GrantAccessAsync_WhenCalledTwice_AccumulatesBothFlagSets()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);

        await svc.GrantAccessAsync(orgId, userId, SecurityTable.OrganizationNote, SecurityAction.Read, userId);
        await svc.GrantAccessAsync(orgId, userId, SecurityTable.OrganizationNote, SecurityAction.Create, userId);

        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.OrganizationNote, SecurityAction.Read));
        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.OrganizationNote, SecurityAction.Create));
        Assert.False(await svc.HasPermissionAsync(userId, orgId, SecurityTable.OrganizationNote, SecurityAction.Delete));
    }

    // ── RevokeAccessAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAccessAsync_AfterGrant_DeniesPermission()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            db.OrganizationAccessGrants.Add(Grant(orgId, userId,
                SecurityTable.UserNote, SecurityAction.Create));
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);

        Assert.True(await svc.HasPermissionAsync(
            userId, orgId, SecurityTable.UserNote, SecurityAction.Create));

        await svc.RevokeAccessAsync(orgId, userId, SecurityTable.UserNote);

        Assert.False(await svc.HasPermissionAsync(
            userId, orgId, SecurityTable.UserNote, SecurityAction.Create));
    }
    [Fact]
    public async Task RevokeAccessAsync_ClearsAllGrantedActions()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                // Cast via SecurityTable so the value matches what HasPermissionAsync expects
                TableName = (OrganizationSecurityTable)SecurityTable.UserNote,
                Actions = SecurityAction.Read | SecurityAction.Create | SecurityAction.Update,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);

        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.UserNote, SecurityAction.Read));
        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.UserNote, SecurityAction.Create));
        Assert.True(await svc.HasPermissionAsync(userId, orgId, SecurityTable.UserNote, SecurityAction.Update));

        await svc.RevokeAccessAsync(orgId, userId, SecurityTable.UserNote);

        Assert.False(await svc.HasPermissionAsync(userId, orgId, SecurityTable.UserNote, SecurityAction.Read));
        Assert.False(await svc.HasPermissionAsync(userId, orgId, SecurityTable.UserNote, SecurityAction.Create));
        Assert.False(await svc.HasPermissionAsync(userId, orgId, SecurityTable.UserNote, SecurityAction.Update));
    }
    // ── AddMemberAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMemberAsync_CreatesMembership_UserBecomesActiveMember()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var svc = new OrganizationSecurityService(factory);

        Assert.False(await svc.IsMemberAsync(userId, orgId));

        await svc.AddMemberAsync(orgId, userId, MemberRole.Member);

        Assert.True(await svc.IsMemberAsync(userId, orgId));
    }

    [Fact]
    public async Task AddMemberAsync_OwnerRole_IsRecognizedAsOwner()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var svc = new OrganizationSecurityService(factory);
        await svc.AddMemberAsync(orgId, userId, MemberRole.Owner);

        Assert.True(await svc.IsOwnerAsync(userId, orgId));
    }

    // ── RemoveMemberAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMemberAsync_SetsIsActiveFalse_UserNoLongerActiveMember()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, userId));
            await db.SaveChangesAsync();
        }

        var svc = new OrganizationSecurityService(factory);

        Assert.True(await svc.IsMemberAsync(userId, orgId));

        await svc.RemoveMemberAsync(orgId, userId);

        // Record exists but IsActive = false — IsMemberAsync checks existence only,
        // so we verify via GetUserOrganizationsAsync which filters by IsActive
        var orgs = await svc.GetUserOrganizationsAsync(userId);
        Assert.DoesNotContain(orgId, orgs);
    }

    // ── GetOrganizationMembersAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetOrganizationMembers_ReturnsActiveMembersWithCorrectRoles()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var ownerId  = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await using (var db = await GetDbAsync(factory))
        {
            db.OrganizationUserMemberships.Add(Membership(orgId, ownerId, isAdmin: true));
            db.OrganizationUserMemberships.Add(Membership(orgId, memberId, isAdmin: false));
            // Inactive — should not appear
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = Guid.NewGuid(),
                Role = MemberRole.Member, IsActive = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var svc     = new OrganizationSecurityService(factory);
        var members = await svc.GetOrganizationMembersAsync(orgId);

        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.UserId == ownerId  && m.Role == MemberRole.Owner);
        Assert.Contains(members, m => m.UserId == memberId && m.Role == MemberRole.Member);
    }
}
