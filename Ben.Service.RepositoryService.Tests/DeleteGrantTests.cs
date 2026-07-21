using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;
using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using MemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.RepositoryService.Tests;

public class DeleteGrantTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static async Task<(Guid orgId, Guid ownerId, Guid memberId)>
        SeedOrgAsync(IDbContextFactory<BenDataContext> factory)
    {
        var orgId    = Guid.NewGuid();
        var ownerId  = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Org", UrlName = "org",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
        });

        foreach (var (uid, role) in new[] { (ownerId, MemberRole.Owner), (memberId, MemberRole.Member) })
        {
            db.AppUsers.Add(new AppUser { Id = uid, UserName = uid.ToString(), Email = $"{uid}@test.com" });
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

    private static async Task AddGrantAsync(IDbContextFactory<BenDataContext> factory,
        Guid orgId, Guid userId, DataTable table, DataAction action)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            TableName = table, Actions = action,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
        });
        await db.SaveChangesAsync();
    }

    private static OrganizationSecurityService Build(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    // ── DeleteGrantAsync — specific table ─────────────────────────────────────

    [Fact]
    public async Task DeleteGrant_SpecificTable_DeletesOnlyThatGrant()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        await AddGrantAsync(factory, orgId, memberId, DataTable.Organization,    DataAction.Read);
        await AddGrantAsync(factory, orgId, memberId, DataTable.OrganizationNote, DataAction.Create);

        var svc    = Build(factory);
        var result = await svc.DeleteGrantAsync(orgId, memberId, DataTable.Organization, ownerId);

        Assert.Equal(1, result);

        await using var db = await factory.CreateDbContextAsync();
        var remaining = await db.OrganizationAccessGrants
            .Where(g => g.OrganizationId == orgId && g.AppUserId == memberId)
            .ToListAsync();

        Assert.Single(remaining);
        Assert.Equal(DataTable.OrganizationNote, remaining[0].TableName);
    }

    // ── DeleteGrantAsync — all grants ─────────────────────────────────────────

    [Fact]
    public async Task DeleteGrant_NullTable_DeletesAllGrantsForUser()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        await AddGrantAsync(factory, orgId, memberId, DataTable.Organization,     DataAction.Read);
        await AddGrantAsync(factory, orgId, memberId, DataTable.OrganizationNote, DataAction.Create);
        await AddGrantAsync(factory, orgId, memberId, DataTable.OrganizationLink, DataAction.Update);

        var svc    = Build(factory);
        var result = await svc.DeleteGrantAsync(orgId, memberId, null, ownerId);

        Assert.Equal(3, result);

        await using var db = await factory.CreateDbContextAsync();
        var remaining = await db.OrganizationAccessGrants
            .CountAsync(g => g.OrganizationId == orgId && g.AppUserId == memberId);

        Assert.Equal(0, remaining);
    }

    // ── DeleteGrantAsync — no matching rows ───────────────────────────────────

    [Fact]
    public async Task DeleteGrant_NoMatchingRows_ReturnsZero()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        var svc    = Build(factory);
        var result = await svc.DeleteGrantAsync(orgId, memberId, DataTable.Organization, ownerId);

        Assert.Equal(0, result);
    }

    // ── DeleteGrantAsync — only deletes grants for the specified user ──────────

    [Fact]
    public async Task DeleteGrant_DoesNotAffectOtherUsersGrants()
    {
        var factory = CreateFactory();
        var (orgId, ownerId, memberId) = await SeedOrgAsync(factory);

        // Add a second member
        var otherId = Guid.NewGuid();
        await using (var db2 = await factory.CreateDbContextAsync())
        {
            db2.AppUsers.Add(new AppUser { Id = otherId, UserName = otherId.ToString(), Email = $"{otherId}@test.com" });
            db2.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = otherId,
                Role = MemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId
            });
            await db2.SaveChangesAsync();
        }

        await AddGrantAsync(factory, orgId, memberId, DataTable.Organization, DataAction.Read);
        await AddGrantAsync(factory, orgId, otherId,  DataTable.Organization, DataAction.Read);

        var svc = Build(factory);
        await svc.DeleteGrantAsync(orgId, memberId, null, ownerId);

        await using var db = await factory.CreateDbContextAsync();
        var otherGrants = await db.OrganizationAccessGrants
            .CountAsync(g => g.OrganizationId == orgId && g.AppUserId == otherId);

        Assert.Equal(1, otherGrants);
    }
}
