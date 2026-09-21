using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Investigations;
using Ben.Data.WebApi.Services.Media;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Investigations;

/// <summary>
/// A guest's code lets them SEND, and does not let them READ (item 248).
/// </summary>
/// <remarks>
/// <para><b>The rule this whole feature is built around.</b> The obvious implementation is to
/// write an attendee row, or to add the guest door to <c>MayContributeAsync</c> and be done. That
/// method is also the read door: <c>MayReadAsync</c> runs through it, and so does "everything
/// anyone has sent up for this investigation". Either shortcut would have handed a walk-up from
/// the pavement every recording the team made inside somebody's house.</para>
///
/// <para>This test was written against that shortcut first and seen to fail, which is the only
/// reason to believe it can.</para>
/// </remarks>
public sealed class GuestPassReadDoorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();

    private static FieldSessionUploadController As(SqliteTestDb sqlite, Guid userId)
    {
        var storage = new Mock<IFileStorageService>();
        var ingest = new Mock<IMediaIngestService>();
        var bundles = new Mock<IBenBundleStore>();
        var controller = new FieldSessionUploadController(
            sqlite.Factory, storage.Object, ingest.Object,
            new MediaRetentionPolicy(new SubscriptionLimitGuard(sqlite.Factory)),
            bundles.Object, NullLogger<FieldSessionUploadController>.Instance)
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
        return controller;
    }

    [Fact]
    public async Task AGuestPassDoesNotOpenTheReadDoor()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();

        Guid investigationId;
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = OrgId, Name = "Apple-Beta", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
            });
            db.AppUsers.Add(new AppUser { Id = Lead, DisplayName = "The lead", DateCreated = DateTime.UtcNow });
            db.AppUsers.Add(new AppUser { Id = Guest, DisplayName = "A walk-up", DateCreated = DateTime.UtcNow });

            investigationId = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = OrgId, Title = "Hollow Creek Road",
                ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
            });
            await db.SaveChangesAsync();

            var investigation = await db.Investigations.FirstAsync(i => i.Id == investigationId);
            var code = await GuestCodes.IssueAsync(db, investigation, Lead, DateTime.UtcNow.AddHours(8), default);
            await db.SaveChangesAsync();

            var (pass, refused) = await GuestCodes.RedeemAsync(db, code, Guest, "A walk-up", default);
            Assert.Null(refused);
            Assert.NotNull(pass);
            await db.SaveChangesAsync();

            // The credential is real: the write door opens for them.
            Assert.True(await GuestCodes.HoldsALivePassAsync(db, investigationId, Guest, default));
        }

        // …and the list of what everybody sent up still does not.
        var result = await As(sqlite, Guest).GetForInvestigation(investigationId, default);
        Assert.IsType<NotFoundResult>(result.Result);

        // The same call for somebody who is actually on the visit is the control: without it,
        // a NotFound here could mean the seat, the seed or the route was wrong rather than the rule.
        await using (var db = await sqlite.Factory.CreateDbContextAsync())
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = investigationId, AppUserId = Lead,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Lead,
            });
            await db.SaveChangesAsync();
        }

        var asLead = await As(sqlite, Lead).GetForInvestigation(investigationId, default);
        Assert.IsType<OkObjectResult>(asLead.Result);
    }
}
