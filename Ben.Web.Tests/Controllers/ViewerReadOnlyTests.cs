using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A Viewer reads a group's work and changes none of it (Ben, 2026-09-14: "make viewers read-only").
/// </summary>
/// <remarks>
/// The calendar was open to every active member, and a Viewer is one: the UI test pass found a Viewer offered New event,
/// the drag and the ✕ on the group's public events, and the server accepted all three. These pin the member-open writes
/// shut for the Viewer rank while leaving them open to a Member.
/// </remarks>
public class ViewerReadOnlyTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid PersonId = Guid.NewGuid();

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(OrganizationMemberRole role)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = OrgId, Name = "Group", UrlName = $"g-{Guid.NewGuid():N}", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = PersonId, Role = role, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = EventId, OrganizationId = OrgId, Title = "Open night", IsPublic = true,
            StartDateTime = DateTime.UtcNow.AddDays(3), EndDateTime = DateTime.UtcNow.AddDays(3).AddHours(2),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = PersonId,
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static OrgCalendarEventController Calendar(IDbContextFactory<BenDataContext> f)
        => new(f, new Mock<IMapper>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f),
            new Mock<Ben.Data.Common.Interfaces.IEmailService>().Object,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity { BaseUrl = "https://example.test" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrgCalendarEventController>.Instance,
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer(),
            Support.SilentTourMail.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, PersonId.ToString())], "Bearer")),
                },
            },
        };

    [Fact]
    public async Task A_viewer_cannot_delete_a_calendar_event_and_is_told_why()
    {
        var factory = await SeedAsync(OrganizationMemberRole.Viewer);

        var result = await Calendar(factory).Delete(OrgId, EventId, default);

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, refused.StatusCode);
        Assert.Contains("viewer", (string)refused.Value!);
        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrgCalendarEvents.AnyAsync(e => e.Id == EventId));
    }

    [Fact]
    public async Task A_member_still_can()
    {
        var factory = await SeedAsync(OrganizationMemberRole.Member);

        var result = await Calendar(factory).Delete(OrgId, EventId, default);

        Assert.IsType<NoContentResult>(result);
    }
}
