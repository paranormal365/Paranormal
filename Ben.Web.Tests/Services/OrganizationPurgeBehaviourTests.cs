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
        Assert.Empty(await db.OrganizationUserMemberships.ToListAsync());

        // The people are not the group's property and stay.
        Assert.Equal(2, await db.Users.CountAsync());
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
