using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Investigations that belong to an organization rather than to a case.
/// </summary>
/// <remarks>
/// <para>Two rules carry the weight here. First, an investigation with no case must say where it
/// happened — the <c>CaseId is not null || PlaceId is not null</c> invariant, enforced in the
/// controller because the InMemory provider ignores check constraints and a rule only the database
/// knows is one the tests cannot see.</para>
///
/// <para>Second, a case supplied to this endpoint still has to belong to the organization in the
/// route. That is the "broken ID chain" shape this codebase has been bitten by before: pass your
/// own orgId to satisfy the membership check, plus someone else's caseId, and read their data.</para>
/// </remarks>
public class OrgInvestigationsControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<InvestigationRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is Investigation i
            ? new InvestigationRecord
            {
                Id = i.Id, CaseId = i.CaseId, OrganizationId = i.OrganizationId, PlaceId = i.PlaceId,
                Latitude = i.Latitude, Longitude = i.Longitude, GeocodeNote = i.GeocodeNote,
                Title = i.Title, ScheduledDateTime = i.ScheduledDateTime,
                Status = i.Status, CreatedByAppUserId = i.CreatedByAppUserId,
            }
            : new InvestigationRecord { Title = "", ScheduledDateTime = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<InvestigationRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<Investigation> list
            ? list.Select(i => new InvestigationRecord
            {
                Id = i.Id, CaseId = i.CaseId, OrganizationId = i.OrganizationId, PlaceId = i.PlaceId,
                Title = i.Title, ScheduledDateTime = i.ScheduledDateTime,
                Status = i.Status, CreatedByAppUserId = i.CreatedByAppUserId,
            }).ToList()
            : new List<InvestigationRecord>());
        return m.Object;
    }

    private static OrgInvestigationsController Build(
        IDbContextFactory<BenDataContext> factory, Guid? asUser = null)
        => new(factory, Mapper(), new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, (asUser ?? MemberId).ToString())], "Bearer"))
                }
            }
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[] { (OrgId, "BenCo"), (OtherOrgId, "Rivals") })
        {
            db.Organizations.Add(new Organization
            { Id = id, Name = name, UrlName = name.ToLowerInvariant(), DateCreated = DateTime.UtcNow });
        }
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<Guid> AddCaseAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, int number)
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = id, OrganizationId = orgId, Title = $"Case {number}", CaseYear = 2026,
            OrgCaseNumber = number, StreetAddress1 = "1 Somewhere Rd", City = "Nashville",
            State = "TN", ZipCode = "37201", DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static CreateOrgInvestigationRequest Request(
        Guid? caseId = null, Guid? placeId = null, NewPlaceRequest? newPlace = null)
        => new("Night visit", DateTime.UtcNow.AddDays(7),
               CaseId: caseId, PlaceId: placeId, NewPlace: newPlace);

    private static T Value<T>(ActionResult<T> result)
        => (T)Assert.IsType<CreatedAtActionResult>(result.Result).Value!;

    // ── The invariant ─────────────────────────────────────────────────────────

    [Fact]
    public async Task An_investigation_with_no_case_and_no_place_is_refused()
    {
        var factory = await SeedAsync();

        var result = await Build(factory).Create(OrgId, Request(), default);

        // Refused, not silently accepted as an unplaceable row. A visit that says neither what it
        // was about nor where it happened cannot appear on any map or any case.
        Assert.IsType<BadRequestObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Investigations.ToListAsync());
    }

    [Fact]
    public async Task A_case_less_investigation_with_a_new_place_is_created_and_placed()
    {
        var factory = await SeedAsync();

        var created = Value(await Build(factory).Create(OrgId, Request(
            newPlace: new NewPlaceRequest(
                "The Bell Witch Cave", null, null, "Adams", "TN", null, "US",
                Latitude: 36.5893m, Longitude: -87.0625m, Kind: PlaceKind.PublicLocation)), default));

        Assert.Null(created.CaseId);
        Assert.Equal(OrgId, created.OrganizationId);
        Assert.NotNull(created.PlaceId);
        // The coordinate columns have existed since AddInvestigationCoordinates and nothing had
        // ever written them. This is the assertion that they are finally populated.
        Assert.Equal(36.5893m, created.Latitude);
        Assert.Equal(-87.0625m, created.Longitude);

        await using var db = await factory.CreateDbContextAsync();
        var place = await db.Places.SingleAsync();
        Assert.Equal("The Bell Witch Cave", place.Name);
        Assert.Equal(PlaceKind.PublicLocation, place.Kind);
    }

    [Fact]
    public async Task A_case_less_investigation_can_reuse_an_existing_place()
    {
        var factory = await SeedAsync();
        var placeId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Places.Add(new Place
            {
                Id = placeId, Name = "Shelby Bridge", Latitude = 36.1043m, Longitude = -86.7714m,
                Kind = PlaceKind.PublicLocation, DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            await db.SaveChangesAsync();
        }

        var created = Value(await Build(factory).Create(OrgId, Request(placeId: placeId), default));

        // Reused, not duplicated — accumulating visits against one place is the entire point.
        Assert.Equal(placeId, created.PlaceId);
        Assert.Equal(36.1043m, created.Latitude);

        await using var check = await factory.CreateDbContextAsync();
        Assert.Single(await check.Places.ToListAsync());
    }

    [Fact]
    public async Task An_unknown_place_id_is_refused()
    {
        var factory = await SeedAsync();

        var result = await Build(factory).Create(OrgId, Request(placeId: Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── The org/case chain ────────────────────────────────────────────────────

    [Fact]
    public async Task A_case_belonging_to_another_org_reads_as_not_found()
    {
        var factory = await SeedAsync();
        var foreignCaseId = await AddCaseAsync(factory, OtherOrgId, 1);

        // The caller is a genuine member of OrgId, so the membership check passes. The case is not.
        var result = await Build(factory).Create(OrgId, Request(caseId: foreignCaseId), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Investigations.ToListAsync());
    }

    [Fact]
    public async Task A_case_in_the_route_org_is_accepted_and_carries_its_place()
    {
        var factory = await SeedAsync();
        var caseId = await AddCaseAsync(factory, OrgId, 2);

        var placeId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Places.Add(new Place
            {
                Id = placeId, StreetAddress1 = "1 Somewhere Rd", City = "Nashville", State = "TN",
                ZipCode = "37201", Latitude = 36.16m, Longitude = -86.78m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MemberId,
            });
            var c = await db.Cases.FirstAsync(x => x.Id == caseId);
            c.PlaceId = placeId;
            await db.SaveChangesAsync();
        }

        var created = Value(await Build(factory).Create(OrgId, Request(caseId: caseId), default));

        // The case's place is inherited rather than re-entered, so the two cannot drift apart.
        Assert.Equal(caseId, created.CaseId);
        Assert.Equal(placeId, created.PlaceId);
        Assert.Equal(36.16m, created.Latitude);
    }

    [Fact]
    public async Task A_non_member_cannot_create_or_list()
    {
        var factory = await SeedAsync();
        var stranger = Guid.NewGuid();

        var create = await Build(factory, stranger).Create(
            OrgId, Request(newPlace: new NewPlaceRequest("Anywhere", null, null, null, null, null, null)), default);
        var list = await Build(factory, stranger).GetAll(OrgId, default);

        Assert.IsType<ForbidResult>(create.Result);
        Assert.IsType<ForbidResult>(list.Result);
    }

    // ── Listing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_includes_case_less_investigations()
    {
        var factory = await SeedAsync();
        var caseId = await AddCaseAsync(factory, OrgId, 3);

        await Build(factory).Create(OrgId, Request(caseId: caseId), default);
        await Build(factory).Create(OrgId, Request(
            newPlace: new NewPlaceRequest("A landmark", null, null, "Adams", "TN", null, "US")), default);

        var listed = Assert.IsAssignableFrom<IEnumerable<InvestigationRecord>>(
            Assert.IsType<OkObjectResult>((await Build(factory).GetAll(OrgId, default)).Result).Value).ToList();

        // The whole reason OrganizationId exists on the investigation: a list that joined through
        // the case would return one row here and give no sign the other was missing.
        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, i => i.CaseId is null);
        Assert.Contains(listed, i => i.CaseId == caseId);
    }

    [Fact]
    public async Task Another_organizations_investigation_is_not_listed_or_readable()
    {
        var factory = await SeedAsync();

        // Created directly, as the other org would have.
        var foreignId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Investigations.Add(new Investigation
            {
                Id = foreignId, OrganizationId = OtherOrgId, Title = "Not yours",
                ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var listed = Assert.IsAssignableFrom<IEnumerable<InvestigationRecord>>(
            Assert.IsType<OkObjectResult>((await Build(factory).GetAll(OrgId, default)).Result).Value).ToList();
        var byId = await Build(factory).GetById(OrgId, foreignId, default);

        Assert.Empty(listed);
        // NotFound rather than Forbid: a 403 confirms the row exists to somebody who should not
        // know that.
        Assert.IsType<NotFoundResult>(byId.Result);
    }

    [Fact]
    public async Task A_created_investigation_gets_a_calendar_event()
    {
        var factory = await SeedAsync();

        await Build(factory).Create(OrgId, Request(
            newPlace: new NewPlaceRequest("A landmark", null, null, "Adams", "TN", null, "US")), default);

        await using var db = await factory.CreateDbContextAsync();
        var calEvent = await db.OrgCalendarEvents.SingleAsync();

        // Case-less visits still belong on the group's calendar — the event's CaseId is simply null.
        Assert.Equal(OrgId, calEvent.OrganizationId);
        Assert.Null(calEvent.CaseId);
        Assert.Equal(calEvent.Id, (await db.Investigations.SingleAsync()).OrgCalendarEventId);
    }
}
