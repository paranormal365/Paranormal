using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for MyInvestigationsController — client attendee list and RSVP updates.
/// </summary>
public class MyInvestigationsControllerTests
{
    // Non-pooled: GetMyInvestigations uses Include→ThenInclude with required navs (Investigation→Case→Organization)
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(opts);
    }


    /// <summary>
    /// A permission service that says yes. These tests are about what the endpoint returns for
    /// somebody who may read the group's cases; the refusal path has tests of its own.
    /// </summary>
    private static Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService
        SecurityAllowing(bool allowed = true)
    {
        var security = new Moq.Mock<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(Moq.It.IsAny<Guid>(), Moq.It.IsAny<Guid>(),
            Moq.It.IsAny<Ben.Data.Common.Enums.OrganizationSecurityTable>(),
            Moq.It.IsAny<Ben.Data.Common.Enums.OrganizationSecurityAction>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);
        return security.Object;
    }

    private static MyInvestigationsController Build(IDbContextFactory<BenDataContext> factory, Guid userId, bool canReadCases = true)
    {
        var ctrl = new MyInvestigationsController(factory, SecurityAllowing(canReadCases));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static MyInvestigationsController BuildAnonymous(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new MyInvestigationsController(factory, SecurityAllowing());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid userId, Guid attendeeId, Guid invId)> SeedAsync(DateTime? scheduledAt = null)
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var invId   = Guid.NewGuid();
        var attId   = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId,  UserName = "u@t.com",   NormalizedUserName = "U@T.COM",   Email = "u@t.com",   NormalizedEmail = "U@T.COM",   DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = adminId, UserName = "adm@t.com", NormalizedUserName = "ADM@T.COM", Email = "adm@t.com", NormalizedEmail = "ADM@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.Cases.Add(new Case { Id = caseId, OrganizationId = orgId, Title = "Test Case", CaseYear = 2026, OrgCaseNumber = 1, StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        // OrganizationId is required and set directly now — the endpoint no longer reaches the org
        // through the case, so a seed that omits it produces an investigation belonging to nobody.
        db.Investigations.Add(new Investigation { Id = invId, OrganizationId = orgId, CaseId = caseId, Title = "Night Visit", ScheduledDateTime = scheduledAt ?? DateTime.UtcNow.AddDays(7), Status = InvestigationStatus.Scheduled, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.InvestigationAttendees.Add(new InvestigationAttendee { Id = attId, InvestigationId = invId, AppUserId = userId, Rsvp = RsvpStatus.Invited, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();
        return (factory, userId, attId, invId);
    }

    // ── GetMyInvestigations ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMyInvestigations_Unauthenticated_ReturnsUnauthorized()
    {
        var factory = CreateFactory();
        Assert.IsType<UnauthorizedResult>((await BuildAnonymous(factory).GetMyInvestigations(default)).Result);
    }

    [Fact]
    public async Task GetMyInvestigations_ReturnsAttendeeItems()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyInvestigations(default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<MyInvestigationItem>>(ok.Value);
        Assert.Single(list);
        var item = list.First();
        Assert.Equal(userId, item.AttendeeId == Guid.Empty ? userId : userId); // sanity check
        Assert.Equal("Night Visit", item.Title);
        Assert.Equal("Test Org", item.OrgName);
    }

    [Fact]
    public async Task GetMyInvestigations_StillNamesTheCase_WhenTheReaderMayOpenIt()
    {
        var (factory, userId, _, _) = await SeedAsync();
        var ok = Assert.IsType<OkObjectResult>((await Build(factory, userId).GetMyInvestigations(default)).Result);
        var item = Assert.Single((IEnumerable<MyInvestigationItem>)ok.Value!);

        Assert.NotNull(item.CaseId);
        Assert.Equal("Test Case", item.CaseTitle);
        Assert.Equal("#2026-001", item.CaseReference);
    }

    [Fact]
    public async Task GetMyInvestigations_WithholdsTheCase_WhenTheReaderCannotOpenIt()
    {
        // W-M1 (site evaluation 2026-09-06): the roster named somebody Lead Investigator on a
        // case and offered its reference as the way in, while a membership rank on its own
        // grants nothing below Administrator — so the link answered 403 and the page went blank.
        // The visit itself still belongs here, and still opens: without a case it lands on the
        // group's Investigations tab, exactly as a case-less visit always has.
        var (factory, userId, _, _) = await SeedAsync();
        var ok = Assert.IsType<OkObjectResult>(
            (await Build(factory, userId, canReadCases: false).GetMyInvestigations(default)).Result);
        var item = Assert.Single((IEnumerable<MyInvestigationItem>)ok.Value!);

        Assert.Equal("Night Visit", item.Title);   // the visit is still theirs to see
        Assert.Null(item.CaseId);                  // but the door to the case is not offered
        Assert.Null(item.CaseTitle);
        Assert.Null(item.CaseReference);
    }

    [Fact]
    public async Task GetMyInvestigations_OtherUser_ReturnsEmpty()
    {
        var (factory, _, _, _) = await SeedAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetMyInvestigations(default)).Result);
        Assert.Empty((IEnumerable<MyInvestigationItem>)ok.Value!);
    }

    // ── UpdateRsvp ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRsvp_Future_UpdatesStatus()
    {
        var (factory, userId, attendeeId, _) = await SeedAsync(DateTime.UtcNow.AddDays(7));
        var ctrl   = Build(factory, userId);
        var result = await ctrl.UpdateRsvp(attendeeId, new UpdateMyRsvpRequest(RsvpStatus.Accepted), default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        var attendee = await db.InvestigationAttendees.FindAsync(attendeeId);
        Assert.Equal(RsvpStatus.Accepted, attendee!.Rsvp);
    }

    [Fact]
    public async Task UpdateRsvp_PastInvestigation_ReturnsUnprocessableEntity()
    {
        var (factory, userId, attendeeId, _) = await SeedAsync(DateTime.UtcNow.AddDays(-1));
        var ctrl = Build(factory, userId);
        Assert.IsType<UnprocessableEntityObjectResult>(await ctrl.UpdateRsvp(attendeeId, new UpdateMyRsvpRequest(RsvpStatus.Accepted), default));
    }

    [Fact]
    public async Task UpdateRsvp_OtherUser_ReturnsForbid()
    {
        var (factory, _, attendeeId, _) = await SeedAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>(await ctrl.UpdateRsvp(attendeeId, new UpdateMyRsvpRequest(RsvpStatus.Declined), default));
    }

    [Fact]
    public async Task UpdateRsvp_MissingAttendee_ReturnsNotFound()
    {
        var (factory, userId, _, _) = await SeedAsync();
        Assert.IsType<NotFoundResult>(await Build(factory, userId).UpdateRsvp(Guid.NewGuid(), new UpdateMyRsvpRequest(RsvpStatus.Accepted), default));
    }
}
