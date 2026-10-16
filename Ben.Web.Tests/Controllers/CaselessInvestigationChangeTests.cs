using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Changing, canceling and deleting a visit that has no case (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Found while writing the e2e tests for that day's time-zone fix: the only edit, cancel and
/// delete were nested under a case, so a visit booked from the group's Investigations page could
/// never be changed. The visits saved an hour late before the fix stayed an hour late.</para>
///
/// <para>The rules are the case-bound door's: the caller must manage the visit, a Viewer changes
/// nothing, the end comes after the start, and each change is audited. The visit's own calendar
/// entry follows it. A delete that would lose recordings, written accounts or attendance is
/// refused in words that say to cancel instead.</para>
/// </remarks>
public class CaselessInvestigationChangeTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();
    private static readonly Guid ViewerId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid OtherPlaceId = Guid.NewGuid();
    private static readonly Guid CaseId = Guid.NewGuid();

    /// <summary>10/30/2026 at 6:00 AM Central, the visit as it should have been saved.</summary>
    private static readonly DateTime RightStartUtc = new(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Mock<IAuditLogService> Audit,
        Guid VisitId,
        Guid CalendarEntryId,
        Guid CaseVisitId,
        Guid OtherOrgVisitId,
        Guid PlainMemberRow);

    private static ControllerContext As(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
        }
    };

    private static OrgInvestigationsController Build(World w, Guid userId)
        => new(w.Factory, new Mock<IMapper>().Object, w.Audit.Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory))
        { ControllerContext = As(userId) };

    private static InvestigationController BuildCaseDoor(World w, Guid userId)
        => new(w.Factory,
               new MapperConfiguration(cfg => cfg.AddProfile<global::InvestigationProfile>(), NullLoggerFactory.Instance).CreateMapper(),
               new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(w.Factory),
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory),
               TestMailer.Quiet(), new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), w.Audit.Object)
        { ControllerContext = As(userId) };

    /// <summary>
    /// One case-less visit at a public place, booked by a plain member an hour late (the bug), with
    /// the calendar entry the booking made; one case-bound visit; another group's visit.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var visitId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var caseVisitId = Guid.NewGuid();
        var otherVisitId = Guid.NewGuid();
        var plainRow = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        { Id = OtherOrgId, Name = "Elsewhere", UrlName = "elsewhere", DateCreated = DateTime.UtcNow });

        void Person(Guid id, string name)
            => db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
        void Member(Guid id, Guid orgId, OrganizationMemberRole role)
            => db.OrganizationUserMemberships.Add(new OrganizationUserMembership
               {
                   Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = id,
                   Role = role, IsActive = true, DateCreated = DateTime.UtcNow,
               });

        Person(CreatorId, "The Creator");
        Person(PlainMemberId, "Plain Member");
        Person(ViewerId, "A Viewer");
        Person(OutsiderId, "An Outsider");
        // Plain members throughout, so the only manage right in the room is the creator's.
        Member(CreatorId, OrgId, OrganizationMemberRole.Member);
        Member(PlainMemberId, OrgId, OrganizationMemberRole.Member);
        Member(ViewerId, OrgId, OrganizationMemberRole.Viewer);
        Member(OutsiderId, OtherOrgId, OrganizationMemberRole.Member);

        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "Bell Witch Cave", City = "Adams", State = "TN",
            Kind = PlaceKind.PublicLocation, DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.Places.Add(new Place
        {
            Id = OtherPlaceId, Name = "Old Gray Cemetery", City = "Knoxville", State = "TN",
            Kind = PlaceKind.PublicLocation, DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = entryId, OrganizationId = OrgId, Title = "Investigation: Landmark night",
            StartDateTime = RightStartUtc.AddHours(1), EndDateTime = RightStartUtc.AddHours(3),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.Investigations.Add(new Investigation
        {
            Id = visitId, OrganizationId = OrgId, Title = "Landmark night", PlaceId = PlaceId,
            // An hour late: saved through the server's clock before 10/16/2026.
            ScheduledDateTime = RightStartUtc.AddHours(1),
            Visibility = InvestigationVisibility.Public, OrgCalendarEventId = entryId,
            Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = plainRow, InvestigationId = visitId, AppUserId = PlainMemberId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        db.Cases.Add(new Case
        {
            Id = CaseId, OrganizationId = OrgId, Title = "The house on the hill",
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Hill Rd", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.Investigations.Add(new Investigation
        {
            Id = caseVisitId, OrganizationId = OrgId, CaseId = CaseId, Title = "First night",
            ScheduledDateTime = RightStartUtc, DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.Investigations.Add(new Investigation
        {
            Id = otherVisitId, OrganizationId = OtherOrgId, Title = "Their night", PlaceId = PlaceId,
            ScheduledDateTime = RightStartUtc, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OutsiderId,
        });

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, OrgId);
        await TestSeeds.BridgeAsync(factory, OtherOrgId);
        return new World(factory, new Mock<IAuditLogService>(), visitId, entryId, caseVisitId, otherVisitId, plainRow);
    }

    private static UpdateOrgInvestigationRequest Corrected(string title = "Landmark night") => new(
        Title: title,
        ScheduledDateTime: RightStartUtc,
        EndDateTime: RightStartUtc.AddHours(17),
        Description: "Corrected to the time we meant.",
        PlaceId: PlaceId,
        Visibility: InvestigationVisibility.Public);

    private static async Task<Investigation?> VisitAsync(World w, Guid? id = null)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await db.Investigations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == (id ?? w.VisitId));
    }

    private static async Task<OrgCalendarEvent?> EntryAsync(World w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await db.OrgCalendarEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == w.CalendarEntryId);
    }

    private static string Sentence(IActionResult result)
        => Assert.IsType<string>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    // ── Changing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Whoever_manages_a_visit_with_no_case_can_put_its_time_right()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).Update(OrgId, w.VisitId, Corrected("Bell Witch night"), default);

        Assert.IsType<NoContentResult>(result);
        var visit = await VisitAsync(w);
        Assert.Equal(RightStartUtc, visit!.ScheduledDateTime);
        Assert.Equal(RightStartUtc.AddHours(17), visit.EndDateTime);
        Assert.Equal("Bell Witch night", visit.Title);
        Assert.Equal("Corrected to the time we meant.", visit.Description);
        Assert.Equal(CreatorId, visit.UpdatedByAppUserId);
    }

    [Fact]
    public async Task Its_calendar_entry_moves_with_it()
    {
        var w = await SeedAsync();

        await Build(w, CreatorId).Update(OrgId, w.VisitId, Corrected("Bell Witch night"), default);

        var entry = await EntryAsync(w);
        Assert.Equal(RightStartUtc, entry!.StartDateTime);
        Assert.Equal(RightStartUtc.AddHours(17), entry.EndDateTime);
        Assert.Equal("Investigation: Bell Witch night", entry.Title);
    }

    [Fact]
    public async Task A_change_is_audited_with_before_and_after()
    {
        var w = await SeedAsync();

        await Build(w, CreatorId).Update(OrgId, w.VisitId, Corrected(), default);

        w.Audit.Verify(a => a.LogUpdateAsync(
            nameof(Investigation), w.VisitId,
            It.Is<Investigation>(b => b.ScheduledDateTime == RightStartUtc.AddHours(1)),
            It.Is<Investigation>(n => n.ScheduledDateTime == RightStartUtc),
            CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task The_place_can_be_changed_and_is_kept_when_not_named()
    {
        var w = await SeedAsync();

        await Build(w, CreatorId).Update(OrgId, w.VisitId, Corrected() with { PlaceId = OtherPlaceId }, default);
        Assert.Equal(OtherPlaceId, (await VisitAsync(w))!.PlaceId);

        await Build(w, CreatorId).Update(OrgId, w.VisitId, Corrected() with { PlaceId = null }, default);
        Assert.Equal(OtherPlaceId, (await VisitAsync(w))!.PlaceId);
    }

    [Fact]
    public async Task An_end_before_the_start_is_refused_in_words()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).Update(
            OrgId, w.VisitId, Corrected() with { EndDateTime = RightStartUtc.AddHours(-1) }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("The end has to come after the start.", Sentence(result));
        Assert.Equal(RightStartUtc.AddHours(1), (await VisitAsync(w))!.ScheduledDateTime);
    }

    [Fact]
    public async Task A_narrower_scope_than_the_plan_allows_is_refused_in_words()
    {
        var w = await SeedAsync();

        // An account that pays nothing shares a public location's findings with everyone.
        var result = await Build(w, CreatorId).Update(
            OrgId, w.VisitId, Corrected() with { Visibility = InvestigationVisibility.GroupOnly }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(string.IsNullOrWhiteSpace(Sentence(result)));
        Assert.Equal(InvestigationVisibility.Public, (await VisitAsync(w))!.Visibility);
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_change_it()
    {
        var w = await SeedAsync();

        var result = await Build(w, PlainMemberId).Update(OrgId, w.VisitId, Corrected(), default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(RightStartUtc.AddHours(1), (await VisitAsync(w))!.ScheduledDateTime);
    }

    [Fact]
    public async Task A_viewer_cannot_change_it()
    {
        var w = await SeedAsync();

        var result = await Build(w, ViewerId).Update(OrgId, w.VisitId, Corrected(), default);

        Assert.IsNotType<NoContentResult>(result);
        Assert.Equal(RightStartUtc.AddHours(1), (await VisitAsync(w))!.ScheduledDateTime);
    }

    [Fact]
    public async Task Another_groups_visit_is_not_found_through_this_groups_address()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).Update(OrgId, w.OtherOrgVisitId, Corrected(), default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(RightStartUtc, (await VisitAsync(w, w.OtherOrgVisitId))!.ScheduledDateTime);
    }

    [Fact]
    public async Task A_visit_with_a_case_is_sent_to_its_case()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).Update(OrgId, w.CaseVisitId, Corrected(), default);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("case's Investigations tab", Sentence(result));
    }

    // ── Canceling ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Canceling_marks_it_canceled_says_so_on_the_calendar_and_is_audited()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).Cancel(OrgId, w.VisitId, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(InvestigationStatus.Cancelled, (await VisitAsync(w))!.Status);
        Assert.Equal("Canceled: Investigation: Landmark night", (await EntryAsync(w))!.Title);
        w.Audit.Verify(a => a.LogUpdateAsync(
            nameof(Investigation), w.VisitId,
            It.Is<Investigation>(b => b.Status == InvestigationStatus.Scheduled),
            It.Is<Investigation>(n => n.Status == InvestigationStatus.Cancelled),
            CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task A_visit_already_canceled_or_over_cannot_be_canceled_again()
    {
        var w = await SeedAsync();
        await Build(w, CreatorId).Cancel(OrgId, w.VisitId, default);

        var again = await Build(w, CreatorId).Cancel(OrgId, w.VisitId, default);

        Assert.IsType<ConflictObjectResult>(again);
        Assert.Equal("This visit is already canceled.", Sentence(again));
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_cancel_it()
    {
        var w = await SeedAsync();

        Assert.IsType<ForbidResult>(await Build(w, PlainMemberId).Cancel(OrgId, w.VisitId, default));
        Assert.Equal(InvestigationStatus.Scheduled, (await VisitAsync(w))!.Status);
    }

    // ── Deleting ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_visit_with_nothing_recorded_is_deleted_with_its_calendar_entry_and_duties()
    {
        var w = await SeedAsync();
        var dutyId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationDuties.Add(new InvestigationDuty
            {
                Id = dutyId, OrganizationId = OrgId, Name = "Audio", IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
            {
                Id = Guid.NewGuid(), InvestigationAttendeeId = w.PlainMemberRow, InvestigationDutyId = dutyId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(w, CreatorId).Delete(OrgId, w.VisitId, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await VisitAsync(w));
        Assert.Null(await EntryAsync(w));
        await using var check = await w.Factory.CreateDbContextAsync();
        // The NoAction key that made SQL Server refuse the delete for anybody holding a duty.
        Assert.False(await check.InvestigationDutyAssignments.AnyAsync(x => x.InvestigationAttendeeId == w.PlainMemberRow));
        w.Audit.Verify(a => a.LogDeleteAsync(
            nameof(Investigation), w.VisitId, It.IsAny<Investigation>(), CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task A_visit_with_recordings_findings_and_attendance_is_kept_and_the_refusal_says_to_cancel()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = Guid.NewGuid(), InvestigationId = w.VisitId,
                SubmittedByAppUserId = PlainMemberId, CreatedByAppUserId = PlainMemberId,
                DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = Guid.NewGuid(), DeviceModel = "iPhone",
                StartedAt = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            });
            db.InvestigationFindings.Add(new InvestigationFinding
            {
                Id = Guid.NewGuid(), InvestigationId = w.VisitId, AppUserId = PlainMemberId,
                Narrative = "Footsteps on the landing.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainMemberId,
            });
            var row = await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainMemberRow);
            row.DidAttend = true;
            await db.SaveChangesAsync();
        }

        var result = await Build(w, CreatorId).Delete(OrgId, w.VisitId, default);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(
            "This visit has 1 recording, 1 written account and 1 person marked as there, and deleting "
            + "it would lose them. Cancel it instead. A canceled visit keeps them on record.",
            Sentence(result));
        Assert.NotNull(await VisitAsync(w));
        Assert.NotNull(await EntryAsync(w));
    }

    [Fact]
    public async Task Attendance_alone_is_enough_to_keep_it()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            (await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainMemberRow)).DidAttend = true;
            await db.SaveChangesAsync();
        }

        var result = await Build(w, CreatorId).Delete(OrgId, w.VisitId, default);

        Assert.Contains("1 person marked as there", Sentence(result));
        Assert.NotNull(await VisitAsync(w));
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_delete_it()
    {
        var w = await SeedAsync();

        Assert.IsType<ForbidResult>(await Build(w, PlainMemberId).Delete(OrgId, w.VisitId, default));
        Assert.NotNull(await VisitAsync(w));
    }

    [Fact]
    public async Task A_visit_with_a_case_is_not_deleted_through_this_door()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).Delete(OrgId, w.CaseVisitId, default);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(await VisitAsync(w, w.CaseVisitId));
    }

    // ── The case page's door, found along the way ─────────────────────────────

    [Fact]
    public async Task Editing_on_the_case_page_keeps_the_calendar_entry_and_moves_it()
    {
        var w = await SeedAsync();
        var entryId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = entryId, OrganizationId = OrgId, CaseId = CaseId, Title = "Investigation: First night",
                StartDateTime = RightStartUtc, EndDateTime = RightStartUtc.AddHours(2),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            (await db.Investigations.FirstAsync(i => i.Id == w.CaseVisitId)).OrgCalendarEventId = entryId;
            await db.SaveChangesAsync();
        }

        // The case page always sends no calendar id; every edit used to unlink the entry.
        var result = await BuildCaseDoor(w, CreatorId).Update(OrgId, CaseId, w.CaseVisitId,
            new UpsertInvestigationRequest("First night", null, null, RightStartUtc.AddDays(1),
                RightStartUtc.AddDays(1).AddHours(4), InvestigationStatus.Scheduled, null, null), default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(entryId, (await VisitAsync(w, w.CaseVisitId))!.OrgCalendarEventId);
        await using var check = await w.Factory.CreateDbContextAsync();
        var entry = await check.OrgCalendarEvents.AsNoTracking().FirstAsync(e => e.Id == entryId);
        Assert.Equal(RightStartUtc.AddDays(1), entry.StartDateTime);
    }

    [Fact]
    public async Task Deleting_on_the_case_page_clears_duties_first()
    {
        var w = await SeedAsync();
        var rowId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var dutyId = Guid.NewGuid();
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = rowId, InvestigationId = w.CaseVisitId, AppUserId = PlainMemberId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            db.InvestigationDuties.Add(new InvestigationDuty
            {
                Id = dutyId, OrganizationId = OrgId, Name = "Audio", IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
            {
                Id = Guid.NewGuid(), InvestigationAttendeeId = rowId, InvestigationDutyId = dutyId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            await db.SaveChangesAsync();
        }

        var result = await BuildCaseDoor(w, CreatorId).Delete(OrgId, CaseId, w.CaseVisitId, default);

        Assert.IsType<NoContentResult>(result);
        await using var check = await w.Factory.CreateDbContextAsync();
        Assert.False(await check.InvestigationDutyAssignments.AnyAsync(x => x.InvestigationAttendeeId == rowId));
    }
}
