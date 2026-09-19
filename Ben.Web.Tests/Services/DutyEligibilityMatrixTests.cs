using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which title may hold which duty, and what holding one lets somebody do on the night
/// (item 160).
/// </summary>
/// <remarks>
/// <para>The two rules are the point: a duty whose matrix has rows is answered by the matrix, and
/// a duty with none falls back to the single minimum title item 158 shipped. Both directions are
/// tested, because a matrix that quietly stopped honouring the old setting would change what every
/// existing group's duties ask for without anybody choosing it.</para>
///
/// <para>And the capability scope: a duty confers its capability <b>on the visit it was assigned
/// for</b>. The test that matters most here is the one where the same person, holding the same
/// duty on a different investigation, is refused.</para>
/// </remarks>
public sealed class DutyEligibilityMatrixTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OrgId   = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> Factory() =>
        new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>A group with the seeded ladder, duties and matrix — what a new group actually gets.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeededGroupAsync()
    {
        var factory = Factory();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        NewOrganizationDefaults.AddAll(db, OrgId, OwnerId);
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<Guid> AddMemberAsync(
        IDbContextFactory<BenDataContext> factory, string titleName)
    {
        var userId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser
        {
            Id = userId, Email = $"{userId:N}@example.com", UserName = $"{userId:N}@example.com",
            DisplayName = titleName, DateCreated = DateTime.UtcNow,
        });
        var level = await db.OrganizationMemberLevels
            .FirstOrDefaultAsync(l => l.OrganizationId == OrgId && l.Name == titleName);
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            MemberLevelId = level?.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<InvestigationDuty> DutyAsync(
        IDbContextFactory<BenDataContext> factory, string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.InvestigationDuties.AsNoTracking()
            .Include(d => d.MinimumMemberLevel)
            .FirstAsync(d => d.OrganizationId == OrgId && d.Name == name);
    }

    // ── what a new group starts with ─────────────────────────────────────────

    [Fact]
    public async Task A_new_group_starts_with_the_ladder_the_duties_and_a_filled_in_matrix()
    {
        var factory = await SeededGroupAsync();
        await using var db = await factory.CreateDbContextAsync();

        var ladder = await db.OrganizationMemberLevels
            .Where(l => l.OrganizationId == OrgId).OrderBy(l => l.SortOrder)
            .Select(l => l.Name).ToListAsync();
        Assert.Equal(
            ["Associate", "Junior Investigator", "Investigator", "Senior Investigator", "Lead Investigator"],
            ladder);

        var duties = await db.InvestigationDuties
            .Where(d => d.OrganizationId == OrgId).Select(d => d.Name).ToListAsync();
        Assert.Contains("Equipment", duties);
        Assert.Contains("Equipment Assist", duties);

        // The grid is filled in, not empty. A group that has to fill one in from scratch never does.
        var dutyIds = await db.InvestigationDuties
            .Where(d => d.OrganizationId == OrgId).Select(d => d.Id).ToListAsync();
        Assert.True(await db.InvestigationDutyEligibilities
            .CountAsync(e => dutyIds.Contains(e.InvestigationDutyId)) > 0);
    }

    [Fact]
    public async Task The_lead_duty_is_the_point_of_contact_and_may_hand_out_the_others()
    {
        var factory = await SeededGroupAsync();

        var lead = await DutyAsync(factory, "Lead Investigator");

        Assert.True(lead.Capabilities.HasFlag(InvestigationDutyCapabilities.PointOfContact));
        Assert.True(lead.Capabilities.HasFlag(InvestigationDutyCapabilities.MayAssignDuties));
    }

    [Fact]
    public async Task An_ordinary_duty_confers_nothing()
    {
        // The other half of the pair: capabilities that were on by default everywhere would make
        // the column meaningless.
        var factory = await SeededGroupAsync();

        Assert.Equal(InvestigationDutyCapabilities.None, (await DutyAsync(factory, "Documentation")).Capabilities);
        Assert.Equal(InvestigationDutyCapabilities.None, (await DutyAsync(factory, "Equipment")).Capabilities);
    }

    // ── the matrix decides ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Associate",           "Documentation",    true)]
    [InlineData("Associate",           "Equipment Assist", true)]
    [InlineData("Associate",           "Equipment",        false)]
    [InlineData("Junior Investigator", "Equipment",        false)]
    [InlineData("Investigator",        "Equipment",        true)]
    [InlineData("Investigator",        "Lead Investigator", false)]
    [InlineData("Senior Investigator", "Lead Investigator", true)]
    public async Task The_seeded_matrix_encodes_the_worked_example(
        string title, string dutyName, bool expected)
    {
        // Ben's example, as data: a junior may ASSIST with equipment, an investigator may RUN it,
        // and leading a visit is open only to the top of the ladder.
        var factory = await SeededGroupAsync();
        var memberId = await AddMemberAsync(factory, title);
        var duty = await DutyAsync(factory, dutyName);

        await using var db = await factory.CreateDbContextAsync();
        var verdict = await DutyEligibility.CheckAsync(db, duty, OrgId, memberId, default);

        Assert.Equal(expected, verdict.Eligible);
        if (!expected) Assert.Contains(dutyName, verdict.Refusal);
    }

    [Fact]
    public async Task A_member_with_no_title_is_refused_and_told_so()
    {
        var factory = await SeededGroupAsync();
        var memberId = await AddMemberAsync(factory, "no such rung");
        var duty = await DutyAsync(factory, "Equipment");

        await using var db = await factory.CreateDbContextAsync();
        var verdict = await DutyEligibility.CheckAsync(db, duty, OrgId, memberId, default);

        Assert.False(verdict.Eligible);
        Assert.Contains("no title yet", verdict.Refusal);
    }

    // ── and falls back when it has nothing to say ────────────────────────────

    [Fact]
    public async Task A_duty_with_no_matrix_row_still_honours_its_minimum_title()
    {
        // The compatibility rule. Every group that was using a minimum before item 160 keeps it,
        // which is why nothing had to be backfilled.
        var factory = await SeededGroupAsync();
        var memberId = await AddMemberAsync(factory, "Associate");

        Guid dutyId;
        await using (var setup = await factory.CreateDbContextAsync())
        {
            var duty = await setup.InvestigationDuties
                .FirstAsync(d => d.OrganizationId == OrgId && d.Name == "Documentation");
            dutyId = duty.Id;
            // Strip its matrix row and give it the old-style single threshold instead.
            setup.InvestigationDutyEligibilities.RemoveRange(
                setup.InvestigationDutyEligibilities.Where(e => e.InvestigationDutyId == dutyId));
            duty.MinimumMemberLevelId = await setup.OrganizationMemberLevels
                .Where(l => l.OrganizationId == OrgId && l.Name == "Investigator")
                .Select(l => l.Id).FirstAsync();
            await setup.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        var reloaded = await db.InvestigationDuties.AsNoTracking()
            .Include(d => d.MinimumMemberLevel).FirstAsync(d => d.Id == dutyId);
        var verdict = await DutyEligibility.CheckAsync(db, reloaded, OrgId, memberId, default);

        Assert.False(verdict.Eligible);
        Assert.Contains("Investigator or above", verdict.Refusal);
    }

    [Fact]
    public async Task A_duty_with_neither_a_matrix_row_nor_a_minimum_is_open_to_anyone()
    {
        var factory = await SeededGroupAsync();
        var memberId = await AddMemberAsync(factory, "Associate");

        Guid dutyId;
        await using (var setup = await factory.CreateDbContextAsync())
        {
            var duty = await setup.InvestigationDuties
                .FirstAsync(d => d.OrganizationId == OrgId && d.Name == "Documentation");
            dutyId = duty.Id;
            setup.InvestigationDutyEligibilities.RemoveRange(
                setup.InvestigationDutyEligibilities.Where(e => e.InvestigationDutyId == dutyId));
            duty.MinimumMemberLevelId = null;
            await setup.SaveChangesAsync();
        }

        await using var db = await factory.CreateDbContextAsync();
        var duty2 = await db.InvestigationDuties.AsNoTracking().FirstAsync(d => d.Id == dutyId);

        Assert.True((await DutyEligibility.CheckAsync(db, duty2, OrgId, memberId, default)).Eligible);
    }

    // ── capabilities are scoped to one visit ─────────────────────────────────

    [Fact]
    public async Task A_capability_applies_to_the_visit_it_was_assigned_on_and_no_other()
    {
        // The whole reason duties can carry capabilities at all: they expire with the visit, the
        // way the lead's manage right does. A capability that leaked to the next investigation
        // would be standing rank by the back door.
        var factory = await SeededGroupAsync();
        var memberId = await AddMemberAsync(factory, "Lead Investigator");

        Guid tonight, another;
        await using (var db = await factory.CreateDbContextAsync())
        {
            tonight = Guid.NewGuid();
            another = Guid.NewGuid();
            foreach (var id in new[] { tonight, another })
            {
                db.Investigations.Add(new Investigation
                {
                    Id = id, OrganizationId = OrgId, Title = "A visit",
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
                });
            }

            var attendeeId = Guid.NewGuid();
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = attendeeId, InvestigationId = tonight, AppUserId = memberId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            // The same person is on the other visit too — just without the duty.
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = another, AppUserId = memberId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });

            var leadDuty = await db.InvestigationDuties
                .FirstAsync(d => d.OrganizationId == OrgId && d.Name == "Lead Investigator");
            db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
            {
                Id = Guid.NewGuid(), InvestigationAttendeeId = attendeeId,
                InvestigationDutyId = leadDuty.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();

        Assert.True(await InvestigationAccess.HasDutyCapabilityAsync(
            read, tonight, memberId, InvestigationDutyCapabilities.MayAssignDuties, default));

        Assert.False(await InvestigationAccess.HasDutyCapabilityAsync(
            read, another, memberId, InvestigationDutyCapabilities.MayAssignDuties, default));
    }

    [Fact]
    public async Task A_duty_that_does_not_confer_a_capability_does_not_grant_it()
    {
        var factory = await SeededGroupAsync();
        var memberId = await AddMemberAsync(factory, "Investigator");

        Guid investigationId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            investigationId = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = OrgId, Title = "A visit",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            var attendeeId = Guid.NewGuid();
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = attendeeId, InvestigationId = investigationId, AppUserId = memberId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            var equipment = await db.InvestigationDuties
                .FirstAsync(d => d.OrganizationId == OrgId && d.Name == "Equipment");
            db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
            {
                Id = Guid.NewGuid(), InvestigationAttendeeId = attendeeId,
                InvestigationDutyId = equipment.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();
        Assert.False(await InvestigationAccess.HasDutyCapabilityAsync(
            read, investigationId, memberId, InvestigationDutyCapabilities.MayAssignDuties, default));
    }
}
