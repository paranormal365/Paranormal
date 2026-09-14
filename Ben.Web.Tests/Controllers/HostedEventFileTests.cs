using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// An event's files reach exactly who they are for (item 235 phase 11).
/// </summary>
/// <remarks>
/// The list and the download ask the same question, and both are tested: a list that hid a file
/// behind a download that served it would be a link in a group chat away from a stranger reading
/// the stewards' briefing.
/// </remarks>
public sealed class HostedEventFileTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();
    private static readonly Guid Steward = Guid.NewGuid();
    private static readonly Guid Confirmed = Guid.NewGuid();
    private static readonly Guid Waiting = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static readonly Guid BriefingId = Guid.NewGuid();
    private static readonly Guid GuestPackId = Guid.NewGuid();
    private static readonly Guid PosterId = Guid.NewGuid();

    private static PublicHostedEventFileController As(SqliteTestDb sqlite, Guid? who)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(Host, OrgId, It.IsAny<OrganizationSecurityTable>(),
            It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>())).Returns(true);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream("the bytes"u8.ToArray()));

        var ingest = new Mock<IMediaIngestService>();
        ingest.Setup(i => i.ServingPathFor(It.IsAny<string>())).Returns<string>(p => p);

        var controller = new PublicHostedEventFileController(sqlite.Factory, new HostedEventAccess(security.Object), storage.Object, ingest.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = who is Guid id
                    ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
                    : new ClaimsPrincipal(new ClaimsIdentity()),
            },
        };
        return controller;
    }

    private static async Task<SqliteTestDb> SeedAsync(HostedEventLifecycleState state = HostedEventLifecycleState.Published)
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;

        foreach (var id in new[] { Host, Steward, Confirmed, Waiting, Stranger })
            db.Users.Add(new AppUser { Id = id, Email = $"{id:N}@example.test", UserName = $"{id:N}", DateCreated = now });

        db.Organizations.Add(new Organization { Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = Host });
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place { Id = placeId, Name = "The Thomas House Hotel", DateCreated = now, CreatedByAppUserId = Host });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = placeId, Name = "Lock-In", UrlName = "lock-in",
            StartsOn = now.Date.AddDays(10), EndsOn = now.Date.AddDays(11), LifecycleState = state,
            DateCreated = now, CreatedByAppUserId = Host,
        });
        db.HostedEventStaff.Add(new HostedEventStaff
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, AppUserId = Steward, RunsTheDoor = true,
            DateConfirmed = now, DateCreated = now, CreatedByAppUserId = Host,
        });
        foreach (var (lead, status) in new[] { (Confirmed, HostedEventBookingStatus.Confirmed), (Waiting, HostedEventBookingStatus.Requested) })
            db.HostedEventBookings.Add(new HostedEventBooking
            {
                Id = Guid.NewGuid(), HostedEventId = EventId, LeadAppUserId = lead, PartySize = 1,
                Kind = HostedEventBookingKind.DayPass, Status = status, DateCreated = now, CreatedByAppUserId = lead,
            });

        var fileType = new UploadFileType { Id = Guid.NewGuid(), Name = "Event file", AllowAllExtensions = true, IsActive = true, DateCreated = now, CreatedByAppUserId = Host };
        db.UploadFileTypes.Add(fileType);

        foreach (var (id, name, audience) in new[]
                 {
                     (BriefingId, "stewards-briefing.pdf", EventFileAudience.Staff),
                     (GuestPackId, "guest-pack.pdf", EventFileAudience.Attendees),
                     (PosterId, "poster.pdf", EventFileAudience.Public),
                 })
        {
            var uploadId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = uploadId, UploadFileTypeId = fileType.Id, OwnerOrganizationId = OrgId, FileName = name,
                StoredFileName = $"{uploadId:N}.pdf", ContentType = "application/pdf", FileSize = 9,
                StoragePath = $"orgs/{OrgId}/events/{uploadId:N}.pdf", DateCreated = now, CreatedByAppUserId = Host,
            });
            db.HostedEventFiles.Add(new HostedEventFile
            {
                Id = id, HostedEventId = EventId, UploadFileId = uploadId, Audience = audience,
                DateCreated = now, CreatedByAppUserId = Host,
            });
        }

        await db.SaveChangesAsync();
        return sqlite;
    }

    private static async Task<string[]> ListFor(SqliteTestDb sqlite, Guid? who)
    {
        var result = await As(sqlite, who).Get(EventId, default);
        return result.Result is OkObjectResult ok
            ? [.. Assert.IsAssignableFrom<IReadOnlyList<HostedEventFileRecord>>(ok.Value).Select(f => f.FileName).Order()]
            : [];
    }

    [Fact]
    public async Task A_stranger_sees_only_what_is_public()
    {
        await using var sqlite = await SeedAsync();
        Assert.Equal(["poster.pdf"], await ListFor(sqlite, null));
        Assert.Equal(["poster.pdf"], await ListFor(sqlite, Stranger));
    }

    [Fact]
    public async Task A_confirmed_guest_sees_the_guest_pack_and_not_the_stewards_briefing()
    {
        await using var sqlite = await SeedAsync();
        Assert.Equal(["guest-pack.pdf", "poster.pdf"], await ListFor(sqlite, Confirmed));
    }

    [Fact]
    public async Task A_guest_still_waiting_on_the_venue_is_a_stranger_to_the_guest_pack()
    {
        await using var sqlite = await SeedAsync();
        Assert.Equal(["poster.pdf"], await ListFor(sqlite, Waiting));
    }

    [Fact]
    public async Task The_events_own_people_see_everything()
    {
        await using var sqlite = await SeedAsync();
        Assert.Equal(["guest-pack.pdf", "poster.pdf", "stewards-briefing.pdf"], await ListFor(sqlite, Steward));
        Assert.Equal(["guest-pack.pdf", "poster.pdf", "stewards-briefing.pdf"], await ListFor(sqlite, Host));
    }

    [Fact]
    public async Task A_download_asks_the_same_question_as_the_list()
    {
        // The copied link: a confirmed guest's download URL for the stewards' briefing, and a
        // stranger's for the guest pack.
        await using var sqlite = await SeedAsync();

        Assert.IsType<ForbidResult>(await As(sqlite, Confirmed).Download(EventId, BriefingId, default));
        Assert.IsType<UnauthorizedResult>(await As(sqlite, null).Download(EventId, GuestPackId, default));

        var served = Assert.IsType<FileStreamResult>(await As(sqlite, Confirmed).Download(EventId, GuestPackId, default));
        Assert.Equal("guest-pack.pdf", served.FileDownloadName);
        Assert.Equal("application/octet-stream", served.ContentType);   // never inline
    }

    [Fact]
    public async Task A_draft_events_public_files_are_nobodys_yet()
    {
        await using var sqlite = await SeedAsync(HostedEventLifecycleState.Draft);
        Assert.Empty(await ListFor(sqlite, null));
        Assert.Equal(["guest-pack.pdf", "poster.pdf", "stewards-briefing.pdf"], await ListFor(sqlite, Host));
    }
}
