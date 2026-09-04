using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>A member's desk says exactly what the screens it links to say (item 204).</summary>
public class MyDeskControllerTests
{
    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MyDeskController Build(IDbContextFactory<BenDataContext> f, Guid userId) => new(f)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            }
        }
    };

    [Fact]
    public async Task A_member_sees_their_next_investigation_open_cases_unread_and_gear()
    {
        var f = Factory();
        var me = Guid.NewGuid(); var other = Guid.NewGuid();
        var org = new Organization { Id = Guid.NewGuid(), Name = "Desk Group", UrlName = "desk-group" };
        var now = DateTime.UtcNow;
        await using (var db = await f.CreateDbContextAsync())
        {
            db.AppUsers.AddRange(
                new AppUser { Id = me, UserName = "me@benco.dev", Email = "me@benco.dev", DisplayName = "Me" },
                new AppUser { Id = other, UserName = "o@benco.dev", Email = "o@benco.dev", DisplayName = "Other" });
            db.Organizations.Add(org);
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = me, IsActive = true,
                Role = OrganizationMemberRole.Member, CreatedByAppUserId = me,
            });

            var soon = new Investigation { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Tonight at the mill", UrlName = "tonight-at-the-mill", ScheduledDateTime = now.AddHours(6), Location = "The mill", CreatedByAppUserId = me };
            var later = new Investigation { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Next month", ScheduledDateTime = now.AddDays(30), CreatedByAppUserId = me };
            var past = new Investigation { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Last week", ScheduledDateTime = now.AddDays(-7), EndDateTime = now.AddDays(-7).AddHours(4), CreatedByAppUserId = me };
            db.Investigations.AddRange(soon, later, past);
            db.InvestigationAttendees.AddRange(
                new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = soon.Id, AppUserId = me, IsLead = true, Rsvp = RsvpStatus.Accepted, CreatedByAppUserId = me },
                new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = soon.Id, AppUserId = other, Rsvp = RsvpStatus.Accepted, CreatedByAppUserId = me },
                new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = later.Id, AppUserId = me, Rsvp = RsvpStatus.Invited, CreatedByAppUserId = me },
                new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = past.Id, AppUserId = me, Rsvp = RsvpStatus.Accepted, CreatedByAppUserId = me });

            var mine = new Case { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "The Belmont house", UrlName = "belmont", CaseYear = 2026, OrgCaseNumber = 3, Status = CaseStatus.Active, DateCaseOpened = now.AddDays(-2), City = "Nashville" };
            var open = new Case { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "The old depot", CaseYear = 2026, OrgCaseNumber = 4, Status = CaseStatus.Proposed, DateCaseOpened = now.AddDays(-1), City = "Nashville" };
            var closed = new Case { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Done", CaseYear = 2025, OrgCaseNumber = 9, Status = CaseStatus.Closed, DateCaseOpened = now.AddDays(-90), City = "Nashville" };
            db.Cases.AddRange(mine, open, closed);
            db.CaseContacts.Add(new CaseContact { Id = Guid.NewGuid(), CaseId = mine.Id, AppUserId = me, CreatedByAppUserId = me });

            var msg = new OrgMessage { Id = Guid.NewGuid(), OrganizationId = org.Id, AuthorAppUserId = other, CreatedByAppUserId = other, Body = "hello", DateCreated = now };
            db.OrgMessages.Add(msg);
            db.OrgMessageRecipients.AddRange(
                new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = msg.Id, RecipientAppUserId = me },
                new OrgMessageRecipient { Id = Guid.NewGuid(), OrgMessageId = msg.Id, RecipientAppUserId = other });

            var item = new EquipmentItem { Id = Guid.NewGuid(), DisplayName = "K2 meter", OwningOrganizationId = org.Id, CreatedByAppUserId = me };
            db.EquipmentItems.Add(item);
            db.EquipmentCheckouts.AddRange(
                new EquipmentCheckout { Id = Guid.NewGuid(), EquipmentItemId = item.Id, BorrowerAppUserId = me, Status = EquipmentCheckoutStatus.CheckedOut, DateCheckedOut = now.AddDays(-10), DateDue = now.AddDays(-1), CreatedByAppUserId = me },
                new EquipmentCheckout { Id = Guid.NewGuid(), EquipmentItemId = item.Id, BorrowerAppUserId = me, Status = EquipmentCheckoutStatus.Returned, CreatedByAppUserId = me });
            await db.SaveChangesAsync();
        }

        var result = await Build(f, me).GetDesk(default);
        var desk = Assert.IsType<MemberDeskResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, desk.GroupCount);
        Assert.Equal("Tonight at the mill", desk.NextInvestigation!.Title);
        Assert.True(desk.NextInvestigation.IsLead);
        Assert.Equal(2, desk.NextInvestigation.AttendeeCount);
        Assert.Equal(2, desk.UpcomingInvestigationCount);        // not the one last week
        Assert.Equal(2, desk.OpenCaseCount);                     // not the closed one
        Assert.Equal("The Belmont house", desk.OpenCases[0].Title); // the one I am a contact on comes first
        Assert.True(desk.OpenCases[0].IsContact);
        Assert.Equal(1, desk.UnreadMessageCount);
        Assert.Equal(1, desk.GearCheckedOutCount);
        Assert.Equal(1, desk.OverdueGearCount);
        Assert.True(desk.GearCheckedOut[0].IsOverdue);
    }

    [Fact]
    public async Task Somebody_in_no_group_has_an_empty_desk_and_says_so()
    {
        var f = Factory();
        var me = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = me, UserName = "solo@benco.dev", Email = "solo@benco.dev" });
            await db.SaveChangesAsync();
        }
        var desk = Assert.IsType<MemberDeskResponse>(Assert.IsType<OkObjectResult>((await Build(f, me).GetDesk(default)).Result).Value);
        Assert.Equal(0, desk.GroupCount);
        Assert.Null(desk.NextInvestigation);
        Assert.Empty(desk.OpenCases);
    }
}
