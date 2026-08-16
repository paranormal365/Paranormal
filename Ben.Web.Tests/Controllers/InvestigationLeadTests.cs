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
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Handing one person the lead of a visit.
/// </summary>
/// <remarks>
/// <para>Leading is delegated per visit and expires with it — deliberately not a standing rank.
/// It is also one of the five ways <c>InvestigationAccess.CanManageAsync</c> grants the right to
/// edit an investigation, which makes this endpoint one that hands out an edit right. So it is
/// gated on already holding one, and the last test here is the one that matters: a lead can
/// actually edit afterwards. Without it, the flag would be decorative.</para>
///
/// <para>The whole reason this endpoint exists: <c>IsLead</c> shipped as a column, a permission
/// branch and a roster badge, and <b>nothing in the app could set it</b> — only the seeder did.
/// A permission nobody can grant is the same as one that does not exist.</para>
/// </remarks>
public class InvestigationLeadTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid FirstLeadId = Guid.NewGuid();
    private static readonly Guid SecondLeadId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid InvestigationId,
        Guid FirstRow,
        Guid SecondRow,
        Guid PlainRow);

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

    /// <summary>One case-less investigation, its creator, and three plain members on the team.</summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var invId = Guid.NewGuid();
        var rows = new Dictionary<Guid, Guid>();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });

        foreach (var (id, name) in new[]
                 {
                     (CreatorId, "The Creator"),
                     (FirstLeadId, "First Lead"),
                     (SecondLeadId, "Second Lead"),
                     (PlainMemberId, "Plain Member"),
                 })
        {
            db.Users.Add(new AppUser
            { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = id,
                // Plain members throughout: nobody here is an owner or administrator, so the only
                // way anyone earns manage rights is by creating the visit or leading it.
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.AddDays(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        foreach (var userId in new[] { FirstLeadId, SecondLeadId, PlainMemberId })
        {
            var rowId = Guid.NewGuid();
            rows[userId] = rowId;
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = rowId, InvestigationId = invId, AppUserId = userId,
                Rsvp = RsvpStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
        }

        await db.SaveChangesAsync();
        return new World(factory, invId, rows[FirstLeadId], rows[SecondLeadId], rows[PlainMemberId]);
    }

    private static async Task<List<InvestigationAttendee>> RowsAsync(World w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await db.InvestigationAttendees
            .Where(a => a.InvestigationId == w.InvestigationId).ToListAsync();
    }

    private static Task<ActionResult<IEnumerable<InvestigationRosterEntry>>> SetLeadAsync(
        World w, Guid asUser, Guid attendeeRow, bool isLead = true)
        => Build(w.Factory, asUser).SetLead(
            OrgId, w.InvestigationId, attendeeRow, new SetLeadRequest(isLead), default);

    // ── Who may hand out the lead ─────────────────────────────────────────────

    [Fact]
    public async Task The_person_who_scheduled_it_can_name_a_lead()
    {
        var w = await SeedAsync();

        var result = await SetLeadAsync(w, CreatorId, w.FirstRow);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((await RowsAsync(w)).Single(a => a.Id == w.FirstRow).IsLead);
    }

    [Fact]
    public async Task An_ordinary_attendee_cannot_make_themselves_the_lead()
    {
        var w = await SeedAsync();

        var result = await SetLeadAsync(w, PlainMemberId, w.PlainRow);

        // The obvious way to escalate: the lead flag is an edit right, so handing it to yourself
        // must need one already.
        Assert.IsType<ForbidResult>(result.Result);
        Assert.All(await RowsAsync(w), a => Assert.False(a.IsLead));
    }

    [Fact]
    public async Task Somebody_outside_the_group_gets_nothing()
    {
        var w = await SeedAsync();

        var result = await SetLeadAsync(w, Guid.NewGuid(), w.FirstRow);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task An_attendee_of_another_investigation_is_not_found()
    {
        var w = await SeedAsync();

        var result = await SetLeadAsync(w, CreatorId, Guid.NewGuid());

        // Not a 403: the caller may manage this investigation, the row simply is not on it.
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── One lead at a time ────────────────────────────────────────────────────

    [Fact]
    public async Task Naming_a_lead_takes_it_from_the_previous_one()
    {
        var w = await SeedAsync();
        await SetLeadAsync(w, CreatorId, w.FirstRow);

        await SetLeadAsync(w, CreatorId, w.SecondRow);

        var rows = await RowsAsync(w);
        // Every screen says "the lead" of a visit. Two people quietly holding an edit right while
        // the page shows one would be the worst reading of both.
        Assert.False(rows.Single(a => a.Id == w.FirstRow).IsLead);
        Assert.True(rows.Single(a => a.Id == w.SecondRow).IsLead);
    }

    [Fact]
    public async Task A_visit_can_have_no_lead_at_all()
    {
        var w = await SeedAsync();
        await SetLeadAsync(w, CreatorId, w.FirstRow);

        await SetLeadAsync(w, CreatorId, w.FirstRow, isLead: false);

        Assert.All(await RowsAsync(w), a => Assert.False(a.IsLead));
    }

    [Fact]
    public async Task The_whole_roster_comes_back_so_the_old_badge_clears()
    {
        var w = await SeedAsync();
        await SetLeadAsync(w, CreatorId, w.FirstRow);

        var result = await SetLeadAsync(w, CreatorId, w.SecondRow);

        var roster = Assert.IsAssignableFrom<IEnumerable<InvestigationRosterEntry>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        // Returning only the clicked row would leave the previous lead's badge on screen until
        // the next poll — visibly two leads, for as long as that takes.
        Assert.Equal(3, roster.Count);
        Assert.Single(roster, r => r.IsLead);
        Assert.Equal(w.SecondRow, roster.First(r => r.IsLead).AttendeeId);
    }

    [Fact]
    public async Task The_lead_is_listed_first()
    {
        var w = await SeedAsync();

        var result = await SetLeadAsync(w, CreatorId, w.SecondRow);
        var roster = Assert.IsAssignableFrom<IEnumerable<InvestigationRosterEntry>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.True(roster[0].IsLead);
    }

    // ── The point of the flag ─────────────────────────────────────────────────

    [Fact]
    public async Task Being_made_lead_actually_confers_the_right_to_edit()
    {
        var w = await SeedAsync();
        await using var db = await w.Factory.CreateDbContextAsync();

        // Before: a plain attendee, and no.
        Assert.False(await InvestigationAccess.CanManageAsync(
            db, w.InvestigationId, FirstLeadId, isSuperAdmin: false, default));

        await SetLeadAsync(w, CreatorId, w.FirstRow);

        await using var after = await w.Factory.CreateDbContextAsync();
        // After: yes — which is the entire reason the flag is worth setting. If this ever fails,
        // the lead badge has become decoration.
        Assert.True(await InvestigationAccess.CanManageAsync(
            after, w.InvestigationId, FirstLeadId, isSuperAdmin: false, default));
    }

    [Fact]
    public async Task A_lead_can_hand_the_lead_on()
    {
        var w = await SeedAsync();
        await SetLeadAsync(w, CreatorId, w.FirstRow);

        var result = await SetLeadAsync(w, FirstLeadId, w.SecondRow);

        // Follows from the right they now hold, and is the ordinary case: whoever is running
        // tonight hands over when they leave.
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True((await RowsAsync(w)).Single(a => a.Id == w.SecondRow).IsLead);
    }
}
