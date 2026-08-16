using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Who may edit an investigation, now that being a member of the group is no longer enough.
/// </summary>
/// <remarks>
/// <para>This is a deliberate tightening, so the test that matters most is the negative one: a
/// perfectly ordinary member of the organization is refused. Everything else here exists to show
/// that the five ways to earn the right actually work, because a rule that refuses everybody is
/// just as broken as one that permits everybody, and only the pair of them together says the
/// change is right.</para>
///
/// <para>Worth recording why these are new files rather than edits to
/// <c>InvestigationControllerTests</c>: every seed in that class sets
/// <c>CreatedByAppUserId = userId</c>, so its caller is always the creator and passes the new gate
/// for free. Those tests went green under the tightening without asserting anything about it.</para>
/// </remarks>
public class InvestigationAccessTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid InvestigationId,
        Guid CaseId,
        Guid CreatorId,
        Guid PlainMemberId);

    /// <summary>
    /// One organization, one case, one investigation created by someone else, and a plain member
    /// who is active in the group and nothing more.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var creator = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var invId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });

        foreach (var (userId, role) in new[]
                 {
                     (creator, OrganizationMemberRole.Member),
                     (plain,   OrganizationMemberRole.Member),
                 })
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
                Role = role, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = OrgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Somewhere Rd", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, CaseId = caseId, Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.AddDays(3),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });

        await db.SaveChangesAsync();
        return new World(factory, invId, caseId, creator, plain);
    }

    private static async Task<bool> CanManageAsync(World w, Guid userId, bool superAdmin = false)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await InvestigationAccess.CanManageAsync(db, w.InvestigationId, userId, superAdmin, default);
    }

    // ── The tightening ────────────────────────────────────────────────────────

    [Fact]
    public async Task An_ordinary_member_of_the_group_cannot_edit_it()
    {
        var w = await SeedAsync();

        // The whole point of P3. Before this change, this returned true — a group of forty had
        // forty people who could move Tuesday's visit.
        Assert.False(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task Someone_outside_the_group_cannot_edit_it()
    {
        var w = await SeedAsync();

        Assert.False(await CanManageAsync(w, Guid.NewGuid()));
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_edit_it()
    {
        var w = await SeedAsync();

        // Guid.Empty is what GetCurrentUserId returns with no usable claim. It must not fall
        // through to a membership lookup that could match a row with an empty AppUserId.
        Assert.False(await CanManageAsync(w, Guid.Empty));
    }

    // ── The five ways to earn it ──────────────────────────────────────────────

    [Fact]
    public async Task The_person_who_scheduled_it_can_edit_it()
    {
        var w = await SeedAsync();

        Assert.True(await CanManageAsync(w, w.CreatorId));
    }

    [Fact]
    public async Task The_lead_of_that_visit_can_edit_it()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = w.InvestigationId, AppUserId = w.PlainMemberId,
                IsLead = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.CreatorId,
            });
            await db.SaveChangesAsync();
        }

        Assert.True(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task Merely_attending_is_not_leading()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = w.InvestigationId, AppUserId = w.PlainMemberId,
                IsLead = false, AssignedRole = "Audio Technician",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.CreatorId,
            });
            await db.SaveChangesAsync();
        }

        // AssignedRole is a description of the job, not a permission — "Lead Investigator" typed
        // into that box must grant nothing.
        Assert.False(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task The_case_manager_can_edit_it()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var c = await db.Cases.FirstAsync(x => x.Id == w.CaseId);
            c.CaseManagerAppUserId = w.PlainMemberId;
            await db.SaveChangesAsync();
        }

        Assert.True(await CanManageAsync(w, w.PlainMemberId));
    }

    [Theory]
    [InlineData(OrganizationMemberRole.Owner)]
    [InlineData(OrganizationMemberRole.Administrator)]
    public async Task An_owner_or_administrator_can_edit_it(OrganizationMemberRole role)
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var m = await db.OrganizationUserMemberships.FirstAsync(x => x.AppUserId == w.PlainMemberId);
            m.Role = role;
            await db.SaveChangesAsync();
        }

        Assert.True(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task A_grant_of_update_on_the_investigation_table_can_edit_it()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = w.PlainMemberId,
                TableName = OrganizationSecurityTable.Investigation,
                Actions = OrganizationSecurityAction.Update,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.CreatorId,
            });
            await db.SaveChangesAsync();
        }

        // The delegable route: an organization hands scheduling to whoever does that job.
        Assert.True(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task A_grant_of_read_only_cannot_edit_it()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = w.PlainMemberId,
                TableName = OrganizationSecurityTable.Investigation,
                Actions = OrganizationSecurityAction.Read,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.CreatorId,
            });
            await db.SaveChangesAsync();
        }

        // Guards the bitmask test rather than a mere "a grant exists" check.
        Assert.False(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task A_grant_on_a_different_table_cannot_edit_it()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = w.PlainMemberId,
                TableName = OrganizationSecurityTable.OrganizationFiles,
                Actions = OrganizationSecurityAction.Update,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.CreatorId,
            });
            await db.SaveChangesAsync();
        }

        // The sibling of the enum-parity problem: permission on one table must not answer for
        // another, whichever direction the confusion comes from.
        Assert.False(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task An_inactive_membership_earns_nothing()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var m = await db.OrganizationUserMemberships.FirstAsync(x => x.AppUserId == w.PlainMemberId);
            m.Role = OrganizationMemberRole.Owner;
            m.IsActive = false;
            await db.SaveChangesAsync();
        }

        // A former owner is not an owner.
        Assert.False(await CanManageAsync(w, w.PlainMemberId));
    }

    [Fact]
    public async Task A_super_admin_can_edit_it()
    {
        var w = await SeedAsync();

        Assert.True(await CanManageAsync(w, Guid.NewGuid(), superAdmin: true));
    }

    // ── Batched flags ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_batched_flags_agree_with_the_single_check()
    {
        var w = await SeedAsync();

        await using var db = await w.Factory.CreateDbContextAsync();
        var flags = await InvestigationAccess.ComputeFlagsAsync(
            db, OrgId, [w.InvestigationId], w.PlainMemberId, false, default);

        // Two code paths deciding the same question is exactly how a list ends up offering an
        // Edit button that the endpoint then refuses.
        Assert.False(flags[w.InvestigationId].CanEditRecord);
        Assert.Equal(await CanManageAsync(w, w.PlainMemberId), flags[w.InvestigationId].CanEditRecord);

        var creatorFlags = await InvestigationAccess.ComputeFlagsAsync(
            db, OrgId, [w.InvestigationId], w.CreatorId, false, default);
        Assert.True(creatorFlags[w.InvestigationId].CanEditRecord);
    }

    [Fact]
    public async Task Recording_your_own_findings_follows_attendance_not_authority()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = w.InvestigationId, AppUserId = w.PlainMemberId,
                IsLead = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.CreatorId,
            });
            await db.SaveChangesAsync();
        }

        await using var check = await w.Factory.CreateDbContextAsync();
        var attendee = await InvestigationAccess.ComputeFlagsAsync(
            check, OrgId, [w.InvestigationId], w.PlainMemberId, false, default);
        var creator = await InvestigationAccess.ComputeFlagsAsync(
            check, OrgId, [w.InvestigationId], w.CreatorId, false, default);

        // Someone who was there has something to say about it whether or not they run anything —
        // and the person who scheduled it but stayed home does not.
        Assert.True(attendee[w.InvestigationId].CanCompleteMyFindings);
        Assert.False(attendee[w.InvestigationId].CanEditRecord);
        Assert.False(creator[w.InvestigationId].CanCompleteMyFindings);
        Assert.True(creator[w.InvestigationId].CanEditRecord);
    }

    [Fact]
    public async Task An_empty_list_asks_the_database_nothing()
    {
        var w = await SeedAsync();

        await using var db = await w.Factory.CreateDbContextAsync();
        var flags = await InvestigationAccess.ComputeFlagsAsync(
            db, OrgId, [], w.PlainMemberId, false, default);

        Assert.Empty(flags);
    }
}
