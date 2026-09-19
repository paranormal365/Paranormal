using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The group purge actually running, against a real relational database with foreign keys
/// enforced.
/// </summary>
/// <remarks>
/// <para><b>The failure this exists for is not hypothetical.</b> On 2026-09-03 deleting a group on
/// production was refused: <i>The DELETE statement conflicted with the REFERENCE constraint
/// FK_InvestigationDutyAssignments_InvestigationAttendees_InvestigationAttendeeId.</i> A table had
/// been added to the schema after the purge was written. <c>OrganizationPurgeCoverageTests</c> was
/// the answer at the time because the InMemory provider cannot run an <c>ExecuteDelete</c> at all;
/// with a real provider available (item 183) the purge can now simply be run, and a wrong order
/// shows up as the database refusing it rather than as a source scan somebody has to trust.</para>
///
/// <para>Both tests stay. The coverage test names the missing table in one line; this one proves
/// the whole sequence actually executes end to end.</para>
/// </remarks>
public sealed class OrganizationPurgeBehaviourTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private const string OrgName = "Music City Spirit Seekers";

    /// <summary>
    /// A group with the shape that broke production: a case, an investigation under it, an
    /// attendee, and a duty assignment hanging off that attendee.
    /// </summary>
    private static async Task SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        db.Users.Add(new AppUser
        {
            Id = AdminId, Email = "admin@example.com", UserName = "admin@example.com",
            DisplayName = "The Admin", DateCreated = DateTime.UtcNow,
        });
        db.Users.Add(new AppUser
        {
            Id = MemberId, Email = "member@example.com", UserName = "member@example.com",
            DisplayName = "A Member", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = OrgName, UrlName = "mcss",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        var caseId = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = OrgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
            Status = CaseStatus.Active,
            StreetAddress1 = "1 Elm", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.CaseNotes.Add(new CaseNote
        {
            Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = MemberId, Body = "A note.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
        });

        var investigationId = Guid.NewGuid();
        db.Investigations.Add(new Investigation
        {
            Id = investigationId, OrganizationId = OrgId, CaseId = caseId, Title = "A visit",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        var attendeeId = Guid.NewGuid();
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = attendeeId, InvestigationId = investigationId, AppUserId = MemberId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        var dutyId = Guid.NewGuid();
        db.InvestigationDuties.Add(new InvestigationDuty
        {
            Id = dutyId, OrganizationId = OrgId, Name = "Camera",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        // A title, and a cell of the duty matrix pointing at it (item 160). The cell's key to the
        // ladder is NoAction, so a purge that removes rungs before cells is refused outright.
        var levelId = Guid.NewGuid();
        db.OrganizationMemberLevels.Add(new OrganizationMemberLevel
        {
            Id = levelId, OrganizationId = OrgId, Name = "Investigator", SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        db.InvestigationDutyEligibilities.Add(new InvestigationDutyEligibility
        {
            Id = Guid.NewGuid(), InvestigationDutyId = dutyId, OrganizationMemberLevelId = levelId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });
        // The exact row production was refused on.
        db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
        {
            Id = Guid.NewGuid(), InvestigationAttendeeId = attendeeId, InvestigationDutyId = dutyId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = AdminId,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_group_with_a_case_an_investigation_and_a_duty_assignment_deletes_cleanly()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var purge = new OrganizationPurge(sqlite.Factory, storage.Object, NullLogger<OrganizationPurge>.Instance);

        var (removed, error) = await purge.PurgeAsync(OrgId, OrgName, AdminId, default);

        // The whole point: an error here is the database refusing the delete order.
        Assert.Null(error);
        Assert.NotNull(removed);

        await using var db = await sqlite.NewContextAsync();
        Assert.Empty(await db.Organizations.ToListAsync());
        Assert.Empty(await db.Cases.ToListAsync());
        Assert.Empty(await db.Investigations.ToListAsync());
        Assert.Empty(await db.InvestigationAttendees.ToListAsync());
        Assert.Empty(await db.InvestigationDutyAssignments.ToListAsync());
        Assert.Empty(await db.InvestigationDutyEligibilities.ToListAsync());
        Assert.Empty(await db.OrganizationMemberLevels.ToListAsync());
        Assert.Empty(await db.OrganizationUserMemberships.ToListAsync());

        // The people are not the group's property and stay.
        Assert.Equal(2, await db.Users.CountAsync());
    }

    /// <summary>
    /// The group's case boards go with its cases; a member's personal board does not.
    /// </summary>
    /// <remarks>
    /// Same rule and same reason as the case purge (canvas plan review R3): the board's key to its
    /// case is SetNull, so leaving it to the database would turn every case board of a deleted
    /// group into a personal board of whichever member made it.
    /// </remarks>
    [Fact]
    public async Task The_groups_case_boards_are_deleted_and_a_members_personal_board_is_not()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var personal = Guid.NewGuid();
        await using (var seed = await sqlite.NewContextAsync())
        {
            var caseId = await seed.Cases.Select(c => c.Id).SingleAsync();
            seed.CanvasDocuments.Add(new CanvasDocument
            {
                Id = Guid.NewGuid(), CaseId = caseId, Name = "Case board", DocumentJson = "{}", Revision = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            seed.CanvasDocuments.Add(new CanvasDocument
            {
                Id = personal, CaseId = null, Name = "Personal board", DocumentJson = "{}", Revision = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            await seed.SaveChangesAsync();
        }
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var purge = new OrganizationPurge(sqlite.Factory, storage.Object, NullLogger<OrganizationPurge>.Instance);

        var (_, error) = await purge.PurgeAsync(OrgId, OrgName, AdminId, default);
        Assert.Null(error);

        await using var db = await sqlite.NewContextAsync();
        var left = await db.CanvasDocuments.Select(d => d.Id).ToListAsync();
        Assert.True(left.SequenceEqual([personal]),
            $"expected only the personal board to survive the group purge; {left.Count} board(s) left");
    }

    [Fact]
    public async Task A_mistyped_name_deletes_nothing()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var purge = new OrganizationPurge(sqlite.Factory, storage.Object, NullLogger<OrganizationPurge>.Instance);

        var (removed, error) = await purge.PurgeAsync(OrgId, "music city spirit seekers", AdminId, default);

        Assert.Null(removed);
        Assert.NotNull(error);

        await using var db = await sqlite.NewContextAsync();
        Assert.Single(await db.Organizations.ToListAsync());
        Assert.Single(await db.Cases.ToListAsync());
    }
}
