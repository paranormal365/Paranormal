using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Creating a group and the role it starts members on, against a database that orders its inserts.
/// </summary>
/// <remarks>
/// <para><b>Why SQLite and not the in-memory provider.</b> An organization points at one of its own
/// roles (<c>DefaultMemberRoleId</c>) and every role points back at its organization. That is a
/// cycle, and a relational provider has to topologically sort its inserts, so putting both in one
/// <c>SaveChanges</c> makes EF refuse the entire batch. The in-memory provider does no such sort:
/// it accepted the cycle happily, every unit test passed, and registering a group failed on the
/// real database with a sort exception the moment the code reached a stack.</para>
///
/// <para>So this test exists at the level where the bug lives. A test using the in-memory provider
/// here would pass against the broken code and prove nothing.</para>
/// </remarks>
public sealed class NewOrganizationDefaultsTests
{
    [Fact]
    public async Task A_group_and_the_role_it_starts_members_on_can_actually_be_saved()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();

        var ownerId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = ownerId, UserName = "owner@t.com", NormalizedUserName = "OWNER@T.COM",
            Email = "owner@t.com", NormalizedEmail = "OWNER@T.COM", DateCreated = DateTime.UtcNow,
        });

        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Cycle Test", UrlName = "cycle-test",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        };
        db.Organizations.Add(org);

        // The whole point: this must not throw. It saves twice on purpose — see AddAllAsync.
        await NewOrganizationDefaults.AddAllAsync(db, org, ownerId);

        await using var check = await sqlite.NewContextAsync();
        var saved = await check.Organizations.SingleAsync(o => o.Id == org.Id);
        Assert.NotNull(saved.DefaultMemberRoleId);

        var role = await check.OrganizationRoles.SingleAsync(r => r.Id == saved.DefaultMemberRoleId);
        Assert.Equal(OrgRoleDefaults.DefaultMemberRoleName, role.Name);
        Assert.Equal(org.Id, role.OrganizationId);
    }
}
