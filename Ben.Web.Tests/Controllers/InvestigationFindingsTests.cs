using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// What each person who was there says happened.
/// </summary>
/// <remarks>
/// <para>The rule these tests exist to hold: <b>you write your own account and nobody else's.</b>
/// There is no manager override, deliberately unlike arrival — whether somebody turned up is an
/// observable fact another person can attest to, and what they experienced is not. An account
/// carrying somebody's name that they did not write would be worse than no account.</para>
///
/// <para>Being on the team is the whole qualification. RSVP and the attendance flag are not
/// checked, because these get written the morning after, often before anybody has recorded who
/// arrived.</para>
/// </remarks>
public class InvestigationFindingsTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid AttendeeId = Guid.NewGuid();
    private static readonly Guid OtherAttendeeId = Guid.NewGuid();
    private static readonly Guid StayedHomeId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid InvestigationId);

    private static OrgInvestigationsController Build(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, new Mock<IMapper>().Object, new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };

    /// <summary>
    /// One visit, two people on the team, and a member who stayed at home — who is nonetheless the
    /// creator, so he can manage it. That combination is the point: managing is not attending.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var invId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });

        foreach (var (id, name) in new[]
                 {
                     (CreatorId, "The Creator"),
                     (AttendeeId, "Was There"),
                     (OtherAttendeeId, "Also There"),
                     (StayedHomeId, "Stayed Home"),
                 })
        {
            db.Users.Add(new AppUser
            { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = id,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.AddDays(-1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        foreach (var userId in new[] { AttendeeId, OtherAttendeeId })
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = userId,
                Rsvp = RsvpStatus.Accepted,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });

        await db.SaveChangesAsync();
        return new World(factory, invId);
    }

    private static Task<ActionResult<InvestigationFindingRecord>> FileAsync(
        World w, Guid asUser, string narrative)
        => Build(w.Factory, asUser).UpsertMyFinding(
            OrgId, w.InvestigationId, new UpsertFindingRequest(narrative), default);

    private static async Task<List<InvestigationFindingRecord>> ReadAsync(World w, Guid asUser)
    {
        var result = await Build(w.Factory, asUser).GetFindings(OrgId, w.InvestigationId, default);
        return Assert.IsAssignableFrom<IEnumerable<InvestigationFindingRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
    }

    // ── Who may write ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Somebody_who_was_there_can_write_up_what_they_saw()
    {
        var w = await SeedAsync();

        var result = await FileAsync(w, AttendeeId, "Cold spot on the landing at about 2am.");

        var filed = Assert.IsType<InvestigationFindingRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Cold spot on the landing at about 2am.", filed.Narrative);
        Assert.Null(filed.DateUpdated);
    }

    [Fact]
    public async Task A_member_who_stayed_at_home_has_nothing_to_file()
    {
        var w = await SeedAsync();

        // He created the visit, so he can edit it, cancel it, and record who turned up. None of
        // that gives him an experience of a building he was not in.
        var result = await FileAsync(w, StayedHomeId, "I heard about a cold spot.");

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(await ReadAsync(w, CreatorId));
    }

    [Fact]
    public async Task Somebody_outside_the_group_gets_nothing()
    {
        var w = await SeedAsync();

        var result = await FileAsync(w, Guid.NewGuid(), "I was passing.");

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task An_empty_account_is_refused_rather_than_stored()
    {
        var w = await SeedAsync();

        var result = await FileAsync(w, AttendeeId, "   ");

        // Silently storing blank would leave a row claiming somebody filed something.
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await ReadAsync(w, AttendeeId));
    }

    // ── One each, revisable ───────────────────────────────────────────────────

    [Fact]
    public async Task Filing_twice_revises_the_same_account_rather_than_adding_a_second()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "First impression.");

        await FileAsync(w, AttendeeId, "On reflection, the floorboard explains it.");

        var findings = await ReadAsync(w, AttendeeId);
        var mine = Assert.Single(findings);
        Assert.Equal("On reflection, the floorboard explains it.", mine.Narrative);
        // A first account and a revision a week later are different claims, and a reader comparing
        // accounts needs to know which they have.
        Assert.NotNull(mine.DateUpdated);
    }

    [Fact]
    public async Task Two_people_each_get_their_own_account()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "Cold spot on the landing.");
        await FileAsync(w, OtherAttendeeId, "Nothing at all, and I was on the landing too.");

        var findings = await ReadAsync(w, CreatorId);

        // The disagreement is the point. One shared write-up would have kept only whichever was
        // typed last.
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.AppUserId == AttendeeId);
        Assert.Contains(findings, f => f.AppUserId == OtherAttendeeId);
    }

    [Fact]
    public async Task Accounts_carry_the_name_of_whoever_wrote_them()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "Cold spot.");

        var finding = Assert.Single(await ReadAsync(w, CreatorId));

        Assert.Equal("Was There", finding.DisplayName);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Any_member_of_the_group_can_read_them_all()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "Cold spot.");

        // Including somebody who was not there: comparing accounts is why they are kept.
        Assert.Single(await ReadAsync(w, StayedHomeId));
    }

    [Fact]
    public async Task Somebody_outside_the_group_cannot_read_them()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "Cold spot.");

        var result = await Build(w.Factory, Guid.NewGuid())
            .GetFindings(OrgId, w.InvestigationId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── Withdrawing ───────────────────────────────────────────────────────────

    [Fact]
    public async Task You_can_withdraw_your_own_account()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "I think I saw something.");

        var result = await Build(w.Factory, AttendeeId)
            .DeleteMyFinding(OrgId, w.InvestigationId, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await ReadAsync(w, AttendeeId));
    }

    [Fact]
    public async Task Nobody_can_withdraw_somebody_elses()
    {
        var w = await SeedAsync();
        await FileAsync(w, AttendeeId, "Cold spot on the landing.");

        // The creator manages this investigation and still cannot reach into another person's
        // account — not to edit it, and not to remove it.
        var result = await Build(w.Factory, CreatorId)
            .DeleteMyFinding(OrgId, w.InvestigationId, default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Single(await ReadAsync(w, AttendeeId));
    }

    [Fact]
    public async Task An_investigation_in_another_group_is_not_found()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, AttendeeId)
            .GetFindings(Guid.NewGuid(), w.InvestigationId, default);

        // Not a member of that organization at all.
        Assert.IsType<ForbidResult>(result.Result);
    }
}
