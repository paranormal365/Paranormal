using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The edit verdict that travels with a case-bound investigation.
/// </summary>
/// <remarks>
/// <para>P3's rule is that the server decides who may edit and the screen renders the answer. The
/// org-wide grid did that from the start; the case panel did not, because
/// <c>InvestigationRecord</c> carried no verdict — so it passed <c>CanManage="true"</c> and showed
/// the attendance and lead controls to every member of the group, each of whom got a 403 on
/// using them.</para>
///
/// <para>These tests are about the flag being a genuine answer rather than a constant: the same
/// investigation, read by two people, must come back with two different values.</para>
/// </remarks>
public class InvestigationEditVerdictTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CaseId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<InvestigationRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is Investigation inv
                ? new InvestigationRecord
                {
                    Id = inv.Id, CaseId = inv.CaseId, Title = inv.Title,
                    ScheduledDateTime = inv.ScheduledDateTime, CreatedByAppUserId = inv.CreatedByAppUserId,
                }
                : new InvestigationRecord
                { Title = "", ScheduledDateTime = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });

        m.Setup(x => x.Map<IEnumerable<InvestigationRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<Investigation> list
                ? list.Select(inv => new InvestigationRecord
                {
                    Id = inv.Id, CaseId = inv.CaseId, Title = inv.Title,
                    ScheduledDateTime = inv.ScheduledDateTime, CreatedByAppUserId = inv.CreatedByAppUserId,
                })
                : []);
        return m.Object;
    }

    private static InvestigationController Build(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, Mapper(), new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(f))
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

    /// <summary>One case, one visit, and two plain members — its creator and somebody else.</summary>
    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid InvestigationId)> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var invId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });

        foreach (var id in new[] { CreatorId, PlainMemberId })
        {
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t" });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = id,
                // Members, not managers: the point is that membership alone is not enough.
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        db.Cases.Add(new Case
        {
            Id = CaseId, OrganizationId = OrgId, Title = "A case",
            CaseYear = 2026, OrgCaseNumber = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, CaseId = CaseId, Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.AddDays(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        await db.SaveChangesAsync();
        return (factory, invId);
    }

    private static async Task<InvestigationRecord> ListedForAsync(
        IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var result = await Build(f, userId).GetAll(OrgId, CaseId, default);
        var list = Assert.IsAssignableFrom<IEnumerable<InvestigationRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        return Assert.Single(list);
    }

    [Fact]
    public async Task The_person_who_scheduled_it_is_told_they_may_edit()
    {
        var w = await SeedAsync();

        Assert.True((await ListedForAsync(w.Factory, CreatorId)).CanEditRecord);
    }

    [Fact]
    public async Task Another_member_of_the_same_group_is_not()
    {
        var w = await SeedAsync();

        // The case that was wrong on screen: a member could read this visit and was shown every
        // control for changing it.
        Assert.False((await ListedForAsync(w.Factory, PlainMemberId)).CanEditRecord);
    }

    [Fact]
    public async Task Leading_the_visit_flips_the_verdict()
    {
        var w = await SeedAsync();
        Assert.False((await ListedForAsync(w.Factory, PlainMemberId)).CanEditRecord);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = w.InvestigationId, AppUserId = PlainMemberId,
                IsLead = true, Rsvp = RsvpStatus.Accepted,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            await db.SaveChangesAsync();
        }

        // Same person, same investigation, different answer — which is what makes it a verdict
        // rather than a constant.
        Assert.True((await ListedForAsync(w.Factory, PlainMemberId)).CanEditRecord);
    }

    [Fact]
    public async Task Reading_one_investigation_gives_the_same_answer_as_the_list()
    {
        var w = await SeedAsync();

        var byId = await Build(w.Factory, PlainMemberId).GetById(OrgId, CaseId, w.InvestigationId, default);
        var single = Assert.IsType<InvestigationRecord>(
            Assert.IsType<OkObjectResult>(byId.Result).Value);

        // Two endpoints, one question. A page that opened the detail view and got a different
        // answer from the list it clicked would be the drift this flag exists to prevent.
        Assert.Equal((await ListedForAsync(w.Factory, PlainMemberId)).CanEditRecord, single.CanEditRecord);
        Assert.False(single.CanEditRecord);
    }

    [Fact]
    public async Task A_member_can_still_read_what_they_cannot_edit()
    {
        var w = await SeedAsync();

        var record = await ListedForAsync(w.Factory, PlainMemberId);

        // The tightening narrowed editing, not reading. If this ever fails, the group has lost
        // sight of its own work.
        Assert.Equal("Night visit", record.Title);
    }
}
