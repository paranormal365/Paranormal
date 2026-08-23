using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;
using MemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.RepositoryService.Tests;

public class OrganizationSecurityServiceRepositoryTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static OrganizationSecurityService CreateService(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    /// <summary>Seeds an org with an Owner and one Member. Returns (orgId, ownerId, memberId).</summary>
    private static async Task<(Guid orgId, Guid ownerId, Guid memberId)>
        SeedOrgAsync(IDbContextFactory<BenDataContext> factory)
    {
        var orgId    = Guid.NewGuid();
        var ownerId  = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = "test-org",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
        });

        foreach (var (uid, role, display) in new[]
        {
            (ownerId,  MemberRole.Owner,  "Owner User"),
            (memberId, MemberRole.Member, "Member User")
        })
        {
            db.AppUsers.Add(new AppUser
            {
                Id = uid, UserName = uid.ToString(),
                Email = $"{uid}@test.com", DisplayName = display
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = uid,
                Role = role, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
        }

        await db.SaveChangesAsync();
        return (orgId, ownerId, memberId);
    }

    /// <summary>Seeds a user with the SuperAdmin Identity role and returns their UserId.</summary>
    private static async Task<Guid> SeedSuperAdminAsync(IDbContextFactory<BenDataContext> factory)
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = userId.ToString(),
            Email = $"{userId}@test.com", DisplayName = "SuperAdmin User"
        });
        db.Set<IdentityRole<Guid>>().Add(new IdentityRole<Guid>
        {
            Id = roleId, Name = RoleNames.SuperAdmin,
            NormalizedName = RoleNames.SuperAdmin.ToUpperInvariant()
        });
        db.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid>
        {
            UserId = userId, RoleId = roleId
        });

        await db.SaveChangesAsync();
        return userId;
    }

    // ── SearchUsersAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchUsers_SuperAdmin_SeesAllUsers()
    {
        var factory    = CreateFactory();
        var superAdmin = await SeedSuperAdminAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 3; i++)
            {
                var uid = Guid.NewGuid();
                db.AppUsers.Add(new AppUser
                {
                    Id = uid, UserName = uid.ToString(),
                    Email = $"other{i}@test.com", DisplayName = $"Other User {i}"
                });
            }
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.SearchUsersAsync(superAdmin, null, take: 100);

        // superAdmin + 3 others = 4
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public async Task SearchUsers_RegularUser_SeesOnlySharedOrgMembers()
    {
        var factory = CreateFactory();
        var (_, ownerId, memberId) = await SeedOrgAsync(factory);

        // Seed a user in a completely separate org — should never appear
        var outsiderId = Guid.NewGuid();
        var org2Id     = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = outsiderId, UserName = outsiderId.ToString(), Email = "outsider@test.com" });
            db.Organizations.Add(new Organization
            {
                Id = org2Id, Name = "Other Org", UrlName = "other-org",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = outsiderId
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org2Id, AppUserId = outsiderId,
                Role = MemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = outsiderId
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.SearchUsersAsync(memberId, null, take: 100);

        var ids = result.Select(u => u.Id).ToHashSet();
        Assert.Contains(ownerId,  ids);
        Assert.Contains(memberId, ids);
        Assert.DoesNotContain(outsiderId, ids);
    }

    [Fact]
    public async Task SearchUsers_WithQuery_FiltersOnDisplayName()
    {
        var factory = CreateFactory();
        var (_, ownerId, _) = await SeedOrgAsync(factory);

        // Both seeded users have DisplayName starting with "Owner User" / "Member User"
        var svc    = CreateService(factory);
        var result = await svc.SearchUsersAsync(ownerId, "Owner", take: 100);

        Assert.Single(result);
        Assert.Contains("Owner", result[0].DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchUsers_Pagination_SkipAndTakeWork()
    {
        var factory    = CreateFactory();
        var superAdmin = await SeedSuperAdminAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 5; i++)
            {
                var uid = Guid.NewGuid();
                db.AppUsers.Add(new AppUser
                {
                    Id = uid, UserName = uid.ToString(),
                    Email = $"page{i}@test.com", DisplayName = $"Page User {i:D2}"
                });
            }
            await db.SaveChangesAsync();
        }

        var svc  = CreateService(factory);
        var page = await svc.SearchUsersAsync(superAdmin, null, skip: 2, take: 2);

        Assert.Equal(2, page.Count);
    }

    // ── HasAccessAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task HasAccess_SuperAdmin_AlwaysReturnsTrue()
    {
        var factory    = CreateFactory();
        var superAdmin = await SeedSuperAdminAsync(factory);
        var orgId      = Guid.NewGuid(); // org the superAdmin is not a member of

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(superAdmin, orgId, DataTable.Organization, DataAction.Delete);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_Owner_AlwaysReturnsTrueWithoutGrant()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, _) = await SeedOrgAsync(factory);

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(ownerId, orgId, DataTable.Organization, DataAction.Delete);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_Administrator_AlwaysReturnsTrueWithoutGrant()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, _) = await SeedOrgAsync(factory);

        var adminId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = adminId, UserName = adminId.ToString(), Email = "admin@test.com" });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId,
                Role = MemberRole.Administrator, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(adminId, orgId, DataTable.OrganizationAddress, DataAction.Update);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_MemberWithMatchingGrant_ReturnsTrue()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId,
                TableName = DataTable.Organization, Actions = DataAction.Read,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(memberId, orgId, DataTable.Organization, DataAction.Read);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_MemberWithNoGrant_ReturnsFalse()
    {
        var factory = CreateFactory();
        var (orgId, _, memberId) = await SeedOrgAsync(factory);

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(memberId, orgId, DataTable.Organization, DataAction.Delete);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccess_NonMember_ReturnsFalse()
    {
        var factory    = CreateFactory();
        var (orgId, _, _) = await SeedOrgAsync(factory);
        var strangerId = Guid.NewGuid();

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(strangerId, orgId, DataTable.Organization, DataAction.Read);

        Assert.False(result);
    }

    // ── GetOrganizationsForUserAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetOrganizationsForUser_SuperAdmin_ReturnsAllOrgs()
    {
        var factory    = CreateFactory();
        var superAdmin = await SeedSuperAdminAsync(factory);
        await SeedOrgAsync(factory); // 1 org — superAdmin is not a member

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Second Org", UrlName = "second-org",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = superAdmin
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.GetOrganizationsForUserAsync(superAdmin);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetOrganizationsForUser_RegularUser_ReturnsOnlyMemberOrgs()
    {
        var factory = CreateFactory();
        var (_, _, memberId) = await SeedOrgAsync(factory);

        // Org the member is NOT in
        await using (var db = await factory.CreateDbContextAsync())
        {
            var other = Guid.NewGuid();
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Unrelated Org", UrlName = "unrelated-org",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = other
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.GetOrganizationsForUserAsync(memberId);

        Assert.Single(result);
        Assert.Equal("Test Org", result[0].Name);
    }

    // ── GetMembershipOrganizationsAsync (item 159) ────────────────────────────

    [Fact]
    public async Task MembershipOrganizations_ForSuperAdmin_AreOnlyTheirOwnMemberships()
    {
        // The sidebar's list answers "your groups". GetOrganizationsForUserAsync expands to every
        // organization for a SuperAdmin — right for an admin screen, and exactly the wrong thing
        // to render under Home. This method must never inherit that expansion.
        var factory = CreateFactory();
        var (orgId, _, _) = await SeedOrgAsync(factory);
        var superAdmin = await SeedSuperAdminAsync(factory);

        // A second org the SuperAdmin does NOT belong to.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Elsewhere", UrlName = "elsewhere",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = superAdmin
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = superAdmin,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = superAdmin
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(factory);

        var mine = await svc.GetMembershipOrganizationsAsync(superAdmin);
        Assert.Single(mine);
        Assert.Equal(orgId, mine[0].Id);

        // The admin expansion still exists and still differs — the two answers are different on purpose.
        var all = await svc.GetOrganizationsForUserAsync(superAdmin);
        Assert.True(all.Count > mine.Count);
    }

    [Fact]
    public async Task MembershipOrganizations_ExcludeInactiveMemberships()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var m = await db.OrganizationUserMemberships.FirstAsync(x => x.AppUserId == memberId);
            m.IsActive = false;
            await db.SaveChangesAsync();
        }

        var svc = CreateService(factory);
        Assert.Empty(await svc.GetMembershipOrganizationsAsync(memberId));
        Assert.Single(await svc.GetMembershipOrganizationsAsync(ownerId));
    }

    // ── RegisterOrganizationAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RegisterOrganization_CreatesOrgAndOwnerMembership()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = userId, UserName = userId.ToString(), Email = "owner@test.com" });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(factory);
        var org = await svc.RegisterOrganizationAsync(userId, "New Org", "new-org");

        Assert.NotEqual(Guid.Empty, org.Id);
        Assert.Equal("New Org", org.Name);
        Assert.Equal("new-org", org.UrlName);

        await using var verify = await factory.CreateDbContextAsync();
        var membership = await verify.OrganizationUserMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == org.Id && m.AppUserId == userId);

        Assert.NotNull(membership);
        Assert.Equal(MemberRole.Owner, membership.Role);
        Assert.True(membership.IsActive);

        // A new group's calendar must be usable from the first moment: registration stamps the
        // default event types (OrgCalendarDefaults), same as both SuperAdmin create doors.
        var eventTypes = await verify.OrgCalendarEventTypes
            .Where(t => t.OrganizationId == org.Id)
            .OrderBy(t => t.SortOrder)
            .Select(t => t.Name)
            .ToListAsync();

        Assert.Equal(["Investigation", "Public Event", "Meeting", "Training", "Fundraiser"], eventTypes);

        // …and the member-title ladder (item 157), for the same reason: a founder's group must
        // be usable from the first moment, and an empty ladder is a feature they cannot find.
        var ladder = await verify.OrganizationMemberLevels
            .Where(l => l.OrganizationId == org.Id)
            .OrderBy(l => l.SortOrder)
            .Select(l => l.Name)
            .ToListAsync();

        Assert.Equal(
            ["Probationary", "Junior Investigator", "Investigator", "Senior Investigator", "Lead Investigator"],
            ladder);

        // …and the duty list (item 158) — same reasoning again.
        var duties = await verify.InvestigationDuties
            .Where(d => d.OrganizationId == org.Id)
            .OrderBy(d => d.SortOrder)
            .Select(d => d.Name)
            .ToListAsync();

        Assert.Equal(["Lead Investigator", "Equipment", "Evidence Collection", "Documentation"], duties);
    }

    [Fact]
    public async Task RegisterOrganization_TrimsWhitespace_BeforeValidation()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = userId, UserName = userId.ToString(), Email = "u@test.com" });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(factory);
        var org = await svc.RegisterOrganizationAsync(userId, "  Trimmed Org  ", "  trimmed-url  ");

        Assert.Equal("Trimmed Org", org.Name);
        Assert.Equal("trimmed-url", org.UrlName);
    }

    [Fact]
    public async Task RegisterOrganization_ThrowsWhenNameIsBlank()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = userId, UserName = userId.ToString(), Email = "u@test.com" });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterOrganizationAsync(userId, "   ", "valid-url"));
    }

    [Fact]
    public async Task RegisterOrganization_ThrowsWhenUserNotFound()
    {
        var factory   = CreateFactory();
        var missingId = Guid.NewGuid();

        var svc = CreateService(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterOrganizationAsync(missingId, "Good Name", "good-url"));
    }

    [Fact]
    public async Task RegisterOrganization_ThrowsWhenUrlNameTaken()
    {
        var factory = CreateFactory();
        var (_, ownerId, _) = await SeedOrgAsync(factory); // "test-org" is taken

        var svc = CreateService(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterOrganizationAsync(ownerId, "Duplicate", "test-org"));
    }

    // ── GetOrganizationUsersAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetOrganizationUsers_Owner_ReturnsMembersOrderedByRole()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, _) = await SeedOrgAsync(factory);

        var svc    = CreateService(factory);
        var result = await svc.GetOrganizationUsersAsync(orgId, ownerId);

        Assert.Equal(2, result.Count);
        Assert.Equal(MemberRole.Owner, result[0].Role); // Owner (1) before Member (4)
    }

    [Fact]
    public async Task GetOrganizationUsers_SuperAdmin_CanAccessAnyOrg()
    {
        var factory    = CreateFactory();
        var (orgId, _, _) = await SeedOrgAsync(factory);
        var superAdmin = await SeedSuperAdminAsync(factory);

        var svc    = CreateService(factory);
        var result = await svc.GetOrganizationUsersAsync(orgId, superAdmin);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetOrganizationUsers_RegularMember_ThrowsUnauthorized()
    {
        var factory = CreateFactory();
        var (orgId, _, memberId) = await SeedOrgAsync(factory);

        var svc = CreateService(factory);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.GetOrganizationUsersAsync(orgId, memberId));
    }

    // ── UpsertMembershipAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertMembership_NewUser_CreatesMembership()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, _) = await SeedOrgAsync(factory);

        var newUserId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = newUserId, UserName = newUserId.ToString(), Email = "new@test.com" });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.UpsertMembershipAsync(orgId, newUserId, MemberRole.Viewer, true, ownerId);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(MemberRole.Viewer, result.Role);
        Assert.True(result.IsActive);
        Assert.Equal(orgId,     result.OrganizationId);
        Assert.Equal(newUserId, result.AppUserId);
    }

    [Fact]
    public async Task UpsertMembership_ExistingUser_UpdatesRoleAndIsActive()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var svc    = CreateService(factory);
        var result = await svc.UpsertMembershipAsync(orgId, memberId, MemberRole.Administrator, false, ownerId);

        Assert.Equal(MemberRole.Administrator, result.Role);
        Assert.False(result.IsActive);
        Assert.NotNull(result.DateUpdated);
    }

    [Fact]
    public async Task UpsertMembership_RegularMember_ThrowsUnauthorized()
    {
        var factory = CreateFactory();
        var (orgId, _, memberId) = await SeedOrgAsync(factory);

        var svc = CreateService(factory);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.UpsertMembershipAsync(orgId, memberId, MemberRole.Administrator, true, memberId));
    }

    // ── SetAccessGrantAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SetAccessGrant_NewGrant_CreatesEntry()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var svc    = CreateService(factory);
        var result = await svc.SetAccessGrantAsync(orgId, memberId, DataTable.Organization, DataAction.Read, ownerId);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(DataAction.Read,         result.Actions);
        Assert.Equal(DataTable.Organization,  result.TableName);
        Assert.Equal(orgId,                   result.OrganizationId);
        Assert.Equal(memberId,                result.AppUserId);
    }

    [Fact]
    public async Task SetAccessGrant_ExistingGrant_UpdatesActions()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var svc = CreateService(factory);
        await svc.SetAccessGrantAsync(orgId, memberId, DataTable.Organization, DataAction.Read, ownerId);

        var updated = await svc.SetAccessGrantAsync(
            orgId, memberId, DataTable.Organization, DataAction.Read | DataAction.Update, ownerId);

        Assert.Equal(DataAction.Read | DataAction.Update, updated.Actions);
        Assert.NotNull(updated.DateUpdated);
    }

    [Fact]
    public async Task SetAccessGrant_TargetNotMember_ThrowsInvalidOperation()
    {
        var factory    = CreateFactory();
        var (orgId, ownerId, _) = await SeedOrgAsync(factory);
        var outsiderId = Guid.NewGuid();

        var svc = CreateService(factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SetAccessGrantAsync(orgId, outsiderId, DataTable.Organization, DataAction.Read, ownerId));
    }

    // ── HasAccessAsync — named role permissions ───────────────────────────────

    [Fact]
    public async Task HasAccess_MemberWithNamedRoleGrantingPermission_ReturnsTrue()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        // Create a named role with Read permission on OrganizationAddress
        var roleId       = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var membership = new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            };
            db.OrganizationUserMemberships.Add(membership);
            db.OrganizationRoles.Add(new OrganizationRole
            {
                Id = roleId, OrganizationId = orgId, Name = "Viewer",
                IsActive = true, SortOrder = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission
            {
                Id = Guid.NewGuid(), OrganizationRoleId = roleId,
                TableName = DataTable.OrganizationAddress, Actions = DataAction.Read,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = Guid.NewGuid(), OrganizationRoleId = roleId,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(memberId, orgId, DataTable.OrganizationAddress, DataAction.Read);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAccess_MemberWithNamedRole_WrongTable_ReturnsFalse()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var roleId       = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var membership = new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            };
            db.OrganizationUserMemberships.Add(membership);
            db.OrganizationRoles.Add(new OrganizationRole
            {
                Id = roleId, OrganizationId = orgId, Name = "AddressOnly",
                IsActive = true, SortOrder = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission
            {
                Id = Guid.NewGuid(), OrganizationRoleId = roleId,
                TableName = DataTable.OrganizationAddress, Actions = DataAction.All,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = Guid.NewGuid(), OrganizationRoleId = roleId,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        // Role only grants Address permission — asking for Email should return false
        var result = await svc.HasAccessAsync(memberId, orgId, DataTable.OrganizationEmail, DataAction.Read);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccess_MemberWithInactiveRole_ReturnsFalse()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var roleId       = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var membership = new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            };
            db.OrganizationUserMemberships.Add(membership);
            db.OrganizationRoles.Add(new OrganizationRole
            {
                Id = roleId, OrganizationId = orgId, Name = "Inactive",
                IsActive = false,  // ← inactive role should not grant permission
                SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission
            {
                Id = Guid.NewGuid(), OrganizationRoleId = roleId,
                TableName = DataTable.OrganizationAddress, Actions = DataAction.All,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
            {
                Id = Guid.NewGuid(), OrganizationRoleId = roleId,
                OrganizationUserMembershipId = membershipId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db.SaveChangesAsync();
        }

        var svc    = CreateService(factory);
        var result = await svc.HasAccessAsync(memberId, orgId, DataTable.OrganizationAddress, DataAction.Read);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAccess_MultipleRoles_OrLogic_ReturnsTrueWhenAnyRoleGrants()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var role1Id      = Guid.NewGuid();
        var role2Id      = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var membership = new OrganizationUserMembership
            {
                Id = membershipId, OrganizationId = orgId, AppUserId = memberId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            };
            db.OrganizationUserMemberships.Add(membership);

            // Role 1: grants only Address Read
            db.OrganizationRoles.Add(new OrganizationRole { Id = role1Id, OrganizationId = orgId, Name = "R1", IsActive = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission { Id = Guid.NewGuid(), OrganizationRoleId = role1Id, TableName = DataTable.OrganizationAddress, Actions = DataAction.Read, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });

            // Role 2: grants Email Create
            db.OrganizationRoles.Add(new OrganizationRole { Id = role2Id, OrganizationId = orgId, Name = "R2", IsActive = true, SortOrder = 2, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationRolePermissions.Add(new OrganizationRolePermission { Id = Guid.NewGuid(), OrganizationRoleId = role2Id, TableName = DataTable.OrganizationEmail, Actions = DataAction.Create, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });

            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership { Id = Guid.NewGuid(), OrganizationRoleId = role1Id, OrganizationUserMembershipId = membershipId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership { Id = Guid.NewGuid(), OrganizationRoleId = role2Id, OrganizationUserMembershipId = membershipId, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            await db.SaveChangesAsync();
        }

        var svc = CreateService(factory);

        // Both role permissions should grant access via OR logic
        Assert.True(await svc.HasAccessAsync(memberId, orgId, DataTable.OrganizationAddress, DataAction.Read));
        Assert.True(await svc.HasAccessAsync(memberId, orgId, DataTable.OrganizationEmail,   DataAction.Create));

        // Neither role grants Delete on Address → false
        Assert.False(await svc.HasAccessAsync(memberId, orgId, DataTable.OrganizationAddress, DataAction.Delete));
    }
}
