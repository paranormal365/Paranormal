using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// People, groups, cases and investigations have clocks (Ben, 2026-09-28): what is stored, what is
/// refused, and which clock a thing actually reads on.
/// </summary>
public sealed class TimeZoneTests
{
    // ── Zones ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("America/Chicago", "America/Chicago")]
    [InlineData("  America/Denver ", "America/Denver")]
    [InlineData("Central Standard Time", "America/Chicago")]   // a Windows id is stored as IANA
    [InlineData("Mars/Olympus_Mons", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void An_id_is_stored_as_IANA_or_not_at_all(string? given, string? stored) =>
        Assert.Equal(stored, Zones.Normalize(given));

    [Fact]
    public void The_first_clock_that_speaks_wins_and_Chicago_answers_when_none_does()
    {
        Assert.Equal("America/Denver", Zones.Effective("America/Denver", "America/New_York"));
        Assert.Equal("America/New_York", Zones.Effective(null, "", "America/New_York"));
        Assert.Equal("America/New_York", Zones.Effective("Mars/Olympus_Mons", "America/New_York"));
        Assert.Equal(HouseClock.ZoneId, Zones.Effective(null, null));
    }

    [Fact]
    public void A_zone_is_named_by_the_letters_on_its_clock_that_day()
    {
        var chicago = Zones.Find("America/Chicago");
        Assert.Equal("CDT", Zones.Abbreviation(chicago, new DateTime(2026, 7, 4, 1, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("CST", Zones.Abbreviation(chicago, new DateTime(2026, 12, 25, 1, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("MST", Zones.Abbreviation(Zones.Find("America/Phoenix"), new DateTime(2026, 7, 4, 1, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("UTC+5:30", Zones.Abbreviation(Zones.Find("Asia/Kolkata"), new DateTime(2026, 7, 4, 1, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Seven_in_the_evening_in_Chicago_and_the_hour_that_never_happened()
    {
        var chicago = Zones.Find("America/Chicago");
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0), Zones.ToUtc(new DateTime(2026, 10, 30, 19, 0, 0), chicago));
        Assert.Equal(new DateTime(2026, 10, 30, 19, 0, 0), Zones.ToZone(new DateTime(2026, 10, 31, 0, 0, 0), chicago));
        // 2:30 AM on 03/08/2026 does not exist in Chicago; it is read as the first minute that does.
        Assert.Equal(new DateTime(2026, 3, 8, 8, 0, 0), Zones.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0), chicago));
    }

    // ── ZoneChain ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> Factory() =>
        new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Guid OrgId, Guid CaseId, Guid UserId)> SeedGroupAndCaseAsync(
        IDbContextFactory<BenDataContext> factory, string groupZone, string? caseZone)
    {
        var orgId = Guid.NewGuid(); var caseId = Guid.NewGuid(); var userId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", Email = "u@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Group", UrlName = "group", TimeZoneId = groupZone,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Case", StreetAddress1 = "1 Main", City = "Nashville",
            State = "TN", ZipCode = "37201", Country = "US", TimeZoneId = caseZone,
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (orgId, caseId, userId);
    }

    private static InvestigationRecord Visit(Guid orgId, Guid? caseId, string? zone) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId, Title = "Visit", TimeZoneId = zone,
        ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
    };

    [Fact]
    public async Task A_visit_reads_on_its_own_clock_then_its_cases_then_its_groups()
    {
        var factory = Factory();
        var (orgId, caseId, _) = await SeedGroupAndCaseAsync(factory, "America/New_York", "America/Denver");
        var (_, plainCaseId, _) = await SeedGroupAndCaseAsync(factory, "America/Los_Angeles", null);
        await using var db = await factory.CreateDbContextAsync();
        var plainOrg = (await db.Cases.SingleAsync(c => c.Id == plainCaseId)).OrganizationId;

        var filled = await ZoneChain.FillAsync(db, new[]
        {
            Visit(orgId, caseId, "Pacific/Honolulu"),   // its own
            Visit(orgId, caseId, null),                 // its case's
            Visit(orgId, null, null),                   // no case: the group's
            Visit(plainOrg, plainCaseId, null),         // a case that names none: the group's
        }, default);

        Assert.Equal(["Pacific/Honolulu", "America/Denver", "America/New_York", "America/Los_Angeles"],
            filled.Select(v => v.EffectiveTimeZoneId));
        Assert.Equal("America/Denver", filled[1].CaseTimeZoneId);
    }

    [Fact]
    public async Task Something_new_starts_on_its_cases_clock_else_its_groups()
    {
        var factory = Factory();
        var (orgId, caseId, _) = await SeedGroupAndCaseAsync(factory, "America/New_York", "America/Denver");
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("America/Denver", await ZoneChain.ForNewAsync(db, orgId, caseId, default));
        Assert.Equal("America/New_York", await ZoneChain.ForNewAsync(db, orgId, null, default));
        Assert.Equal(HouseClock.ZoneId, await ZoneChain.ForNewAsync(db, Guid.NewGuid(), null, default));
    }

    // ── A person's clock ────────────────────────────────────────────────────────

    private static MyProfileController Profile(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<List<AppUserPhotoRecord>>(It.IsAny<object>())).Returns(new List<AppUserPhotoRecord>());
        mapper.Setup(m => m.Map<IEnumerable<AppUserPhotoRecord>>(It.IsAny<object>())).Returns(new List<AppUserPhotoRecord>());
        var ctrl = new MyProfileController(factory, mapper.Object, Mock.Of<IAuditLogService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };
        return ctrl;
    }

    [Fact]
    public async Task A_person_chooses_a_clock_keeps_it_through_other_edits_and_can_give_it_back()
    {
        var factory = Factory();
        var (_, _, userId) = await SeedGroupAndCaseAsync(factory, HouseClock.ZoneId, null);
        var profile = Profile(factory, userId);

        var chosen = (MyProfileRecord)((OkObjectResult)(await profile.UpdateProfile(
            new UpdateMyProfileRequest(null, TimeZoneId: "America/Phoenix"), default)).Result!).Value!;
        Assert.Equal("America/Phoenix", chosen.TimeZoneId);

        // An edit that says nothing about the clock leaves it.
        var renamed = (MyProfileRecord)((OkObjectResult)(await profile.UpdateProfile(
            new UpdateMyProfileRequest("New name"), default)).Result!).Value!;
        Assert.Equal("America/Phoenix", renamed.TimeZoneId);

        Assert.IsType<BadRequestObjectResult>((await profile.UpdateProfile(
            new UpdateMyProfileRequest(null, TimeZoneId: "Mars/Olympus_Mons"), default)).Result);

        // Empty means "use my device's clock" — stored as no choice at all.
        var cleared = (MyProfileRecord)((OkObjectResult)(await profile.UpdateProfile(
            new UpdateMyProfileRequest(null, TimeZoneId: ""), default)).Result!).Value!;
        Assert.Null(cleared.TimeZoneId);
    }

    // ── A group's clock ─────────────────────────────────────────────────────────

    private static OrganizationSettingsController Settings(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var security = new Mock<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new OrganizationSettingsController(factory, Mock.Of<IMapper>(), security.Object,
            Mock.Of<IAuditLogService>(), TestMedia.Stripper())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)], "Bearer")),
                },
            },
        };
    }

    [Fact]
    public async Task A_group_starts_on_Chicago_names_its_own_and_a_client_that_leaves_it_out_changes_nothing()
    {
        var factory = Factory();
        var (orgId, _, userId) = await SeedGroupAndCaseAsync(factory, HouseClock.ZoneId, null);
        var settings = Settings(factory, userId);

        var before = (OrgSettingsResponse)((OkObjectResult)(await settings.Get(orgId, default)).Result!).Value!;
        Assert.Equal(HouseClock.ZoneId, before.TimeZoneId);

        var saved = (OrgSettingsResponse)((OkObjectResult)(await settings.Update(orgId,
            new OrgSettingsRequest(false, false, TimeZoneId: "America/Denver"), default)).Result!).Value!;
        Assert.Equal("America/Denver", saved.TimeZoneId);
        // The save answers with the same "may choose" the read does — it used to drop it.
        Assert.Equal(before.StripMediaMetadataCanChoose, saved.StripMediaMetadataCanChoose);

        var older = (OrgSettingsResponse)((OkObjectResult)(await settings.Update(orgId,
            new OrgSettingsRequest(false, false), default)).Result!).Value!;
        Assert.Equal("America/Denver", older.TimeZoneId);

        Assert.IsType<BadRequestObjectResult>((await settings.Update(orgId,
            new OrgSettingsRequest(false, false, TimeZoneId: "Mars/Olympus_Mons"), default)).Result);
    }
}
