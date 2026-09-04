using System.Reflection;
using System.Security.Claims;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Sharing a session with somebody who has no account (item 207).
/// </summary>
/// <remarks>
/// <para>Every test here is about a door that is deliberately open to strangers. The share
/// endpoints take no bearer token by design, so the token, the expiry, the revocation stamp and
/// the file scope are the only things standing between a URL in an inbox and somebody's
/// recordings. Each is broken on purpose in the discrimination tests at the bottom.</para>
/// </remarks>
public sealed class FieldSessionShareControllerTests
{
    private static readonly Guid OrgId           = Guid.NewGuid();
    private static readonly Guid InvestigationId = Guid.NewGuid();
    private static readonly Guid OwnerId         = Guid.NewGuid();
    private static readonly Guid MemberId        = Guid.NewGuid();
    private static readonly Guid AttendeeId      = Guid.NewGuid();
    private static readonly Guid StrangerId      = Guid.NewGuid();
    private static readonly Guid SessionId       = Guid.NewGuid();
    private static readonly Guid DocumentFileId  = Guid.NewGuid();
    private static readonly Guid AudioId         = Guid.NewGuid();
    private static readonly Guid PhotoId         = Guid.NewGuid();

    private const string DocumentPath = "users/owner/session.json";
    private const string AudioPath    = "users/owner/audio-001.m4a";
    private const string PhotoPath    = "users/owner/photo-001.jpg";

    private const string Document = """
    {
      "format_version": "1.0.0",
      "device": { "manufacturer": "Apple", "model": "iPhone17,1" },
      "session": { "started_at": "2026-09-01T22:00:00Z", "location_label": "back bedroom",
                   "trigger": { "mode": "interval", "interval_seconds": 1 } },
      "readings": [
        { "at": "2026-09-01T22:00:01Z",
          "position": { "latitude": 36.1627, "longitude": -86.7816, "accuracy_meters": 32 },
          "measurements": { "emf": { "value": 4.1, "unit": "uT" } } }
      ]
    }
    """;

    // ── The world these tests run in ──────────────────────────────────────────

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 { (OwnerId, "The Owner"), (MemberId, "A Member"),
                   (AttendeeId, "An Attendee"), (StrangerId, "A Stranger") })
            db.Users.Add(new AppUser
            { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        // Public, so the wide read door is open — which is exactly the door the SHARE rule must
        // not inherit. Without this the narrowing test below would pass for the wrong reason.
        db.Investigations.Add(new Investigation
        {
            Id = InvestigationId, OrganizationId = OrgId, Title = "The Old Mill",
            Visibility = InvestigationVisibility.Public,
            ScheduledDateTime = DateTime.UtcNow.AddDays(-1), DateCreated = DateTime.UtcNow,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = InvestigationId, AppUserId = AttendeeId,
            DateCreated = DateTime.UtcNow,
        });

        db.UploadFiles.Add(new UploadFile
        {
            Id = DocumentFileId, FileName = "data.json", StoragePath = DocumentPath,
            ContentType = "application/json", FileSize = Document.Length,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = AudioId, FileName = "audio-001.m4a", StoragePath = AudioPath,
            ContentType = "audio/mp4", FileSize = 4,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.UploadFiles.Add(new UploadFile
        {
            Id = PhotoId, FileName = "photo-001.jpg", StoragePath = PhotoPath,
            ContentType = "image/jpeg", FileSize = 4,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = SessionId, InvestigationId = InvestigationId,
            SubmittedByAppUserId = OwnerId, CreatedByAppUserId = OwnerId,
            DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = DocumentFileId,
            DeviceModel = "iPhone17,1", LocationLabel = "back bedroom",
            StartedAt = DateTime.UtcNow.AddHours(-3), EndedAt = DateTime.UtcNow.AddHours(-1),
            ReadingCount = 1, MarkerCount = 0, DateCreated = DateTime.UtcNow,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = SessionId, UploadFileId = AudioId,
            RelativePath = "media/audio-001.m4a", DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = OwnerId,
        });
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = SessionId, UploadFileId = PhotoId,
            RelativePath = "media/photo-001.jpg", DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static FieldSessionShareController Build(
        IDbContextFactory<BenDataContext> factory, Guid? userId)
    {
        var bytes = new Dictionary<string, byte[]>
        {
            [DocumentPath] = Encoding.UTF8.GetBytes(Document),
            [AudioPath]    = [1, 2, 3, 4],
            [PhotoPath]    = [5, 6, 7, 8],
        };

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>()))
               .Returns<string>(bytes.ContainsKey);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns<string, CancellationToken>((path, _) => Task.FromResult<Stream>(
                   bytes.TryGetValue(path, out var b) ? new MemoryStream(b) : Stream.Null));

        var controller = new FieldSessionShareController(
            factory, storage.Object, NullLogger<FieldSessionShareController>.Instance);

        // No identity at all for the anonymous side: a share endpoint that only works because a
        // signed-in principal happened to be lying around would pass every test here and fail for
        // the one person the feature exists for.
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        if (userId is Guid id)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"));

        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static async Task<FieldSessionShareRecord> MakeLinkAsync(
        IDbContextFactory<BenDataContext> factory, Guid asUser,
        Guid? fileId = null, int days = 14, bool includePositions = false)
    {
        var result = await Build(factory, asUser).CreateShare(
            SessionId,
            new FieldSessionShareController.CreateShareRequest(fileId, days, "for the client", includePositions),
            default);
        return Assert.IsType<FieldSessionShareRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private static async Task<Guid> AudioRowIdAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.FieldSessionUploadFiles
            .Where(f => f.UploadFileId == AudioId).Select(f => f.Id).FirstAsync();
    }

    // ── Who may hand a session to an outsider ─────────────────────────────────

    [Fact]
    public async Task The_person_who_sent_the_session_up_may_share_it()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        Assert.True(link.IsLive);
        Assert.False(string.IsNullOrWhiteSpace(link.Token));
    }

    [Fact]
    public async Task An_active_member_of_the_group_running_it_may_share_it()
    {
        // The case the feature is actually for: an investigator sending the client the night that
        // was recorded in their house, where the session was uploaded by a colleague.
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, MemberId);

        Assert.True(link.IsLive);
    }

    [Fact]
    public async Task Reading_a_public_investigation_does_not_carry_the_right_to_share_it()
    {
        // The investigation is Public, so MayContributeAsync would let this stranger READ the
        // session. Minting a link that outlives their visit and reaches people the group has never
        // heard of is a different act, and this is the line between them.
        var factory = await SeedAsync();

        var result = await Build(factory, StrangerId).CreateShare(
            SessionId, new FieldSessionShareController.CreateShareRequest(null, 14, null, false), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Being_on_the_investigation_is_not_by_itself_the_right_to_share()
    {
        // An attendee who is not in the group and did not upload this session. They were there;
        // they are still not the person who decides who else sees somebody's recordings.
        var factory = await SeedAsync();

        var result = await Build(factory, AttendeeId).CreateShare(
            SessionId, new FieldSessionShareController.CreateShareRequest(null, 14, null, false), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task A_link_must_have_an_end_date_inside_the_allowed_range()
    {
        var factory = await SeedAsync();
        var controller = Build(factory, OwnerId);

        foreach (var days in new[] { 0, -1, 31, 3650 })
        {
            var result = await controller.CreateShare(
                SessionId, new FieldSessionShareController.CreateShareRequest(null, days, null, false), default);
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }

    [Fact]
    public async Task A_file_from_another_session_cannot_be_named()
    {
        var factory = await SeedAsync();

        var result = await Build(factory, OwnerId).CreateShare(
            SessionId,
            new FieldSessionShareController.CreateShareRequest(Guid.NewGuid(), 14, null, false),
            default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── What the recipient gets ───────────────────────────────────────────────

    [Fact]
    public async Task A_live_link_opens_the_session_for_somebody_with_no_account()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        var result = await Build(factory, userId: null).GetShared(link.Token, default);
        var shared = Assert.IsType<SharedFieldSessionDetail>(Assert.IsType<OkObjectResult>(result).Value);

        // The positive path, proven, not merely the refusals: a feature whose tests only show what
        // it forbids can ship forbidding everything.
        Assert.Equal("iPhone17,1", shared.DeviceModel);
        Assert.Equal("back bedroom", shared.LocationLabel);
        Assert.Contains("\"emf\"", shared.Document);
        Assert.Equal(2, shared.Files.Count);
    }

    [Fact]
    public async Task Coordinates_are_withheld_unless_the_sharer_said_otherwise()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        var result = await Build(factory, null).GetShared(link.Token, default);
        var shared = Assert.IsType<SharedFieldSessionDetail>(Assert.IsType<OkObjectResult>(result).Value);

        Assert.True(shared.PositionsWithheld);
        Assert.DoesNotContain("36.1627", shared.Document);
        Assert.DoesNotContain("-86.7816", shared.Document);
    }

    [Fact]
    public async Task Coordinates_travel_when_the_sharer_deliberately_included_them()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId, includePositions: true);

        var result = await Build(factory, null).GetShared(link.Token, default);
        var shared = Assert.IsType<SharedFieldSessionDetail>(Assert.IsType<OkObjectResult>(result).Value);

        Assert.False(shared.PositionsWithheld);
        Assert.Contains("36.1627", shared.Document);
    }

    [Fact]
    public async Task A_link_to_one_recording_lists_and_serves_only_that_recording()
    {
        var factory = await SeedAsync();
        var audioRowId = await AudioRowIdAsync(factory);
        var link = await MakeLinkAsync(factory, OwnerId, fileId: audioRowId);

        var result = await Build(factory, null).GetShared(link.Token, default);
        var shared = Assert.IsType<SharedFieldSessionDetail>(Assert.IsType<OkObjectResult>(result).Value);

        // A list of everything else recorded that night would tell the recipient precisely what
        // they were not given, which is its own disclosure.
        Assert.True(shared.SingleFileOnly);
        Assert.Single(shared.Files);
        Assert.Equal(audioRowId, shared.Files[0].Id);

        await using var db = await factory.CreateDbContextAsync();
        var photoRowId = await db.FieldSessionUploadFiles
            .Where(f => f.UploadFileId == PhotoId).Select(f => f.Id).FirstAsync();

        var refused = await Build(factory, null).GetSharedFile(link.Token, photoRowId, default);
        Assert.IsType<NotFoundResult>(refused);

        var served = await Build(factory, null).GetSharedFile(link.Token, audioRowId, default);
        Assert.IsType<FileStreamResult>(served);
    }

    [Fact]
    public async Task A_whole_session_link_serves_every_recording_in_it()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);
        var audioRowId = await AudioRowIdAsync(factory);

        var served = await Build(factory, null).GetSharedFile(link.Token, audioRowId, default);
        Assert.IsType<FileStreamResult>(served);
    }

    // ── Expiry and revocation ─────────────────────────────────────────────────

    [Fact]
    public async Task A_withdrawn_link_stops_working_immediately()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);
        var audioRowId = await AudioRowIdAsync(factory);

        Assert.IsType<OkObjectResult>(await Build(factory, null).GetShared(link.Token, default));

        var revoked = await Build(factory, OwnerId).RevokeShare(SessionId, link.Id, default);
        Assert.IsType<NoContentResult>(revoked);

        // Both doors, not just the page: a recipient who kept the media URL must not still reach
        // the bytes after the link that gave it to them was pulled.
        Assert.IsType<NotFoundResult>(await Build(factory, null).GetShared(link.Token, default));
        Assert.IsType<NotFoundResult>(await Build(factory, null).GetSharedFile(link.Token, audioRowId, default));
    }

    [Fact]
    public async Task An_expired_link_stops_working_without_anybody_doing_anything()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.FieldSessionShareLinks.FirstAsync(l => l.Id == link.Id);
            row.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>(await Build(factory, null).GetShared(link.Token, default));
    }

    [Fact]
    public async Task Withdrawing_twice_is_not_an_error()
    {
        // Somebody unsure the first click worked will click again. Refusing them there would read
        // as "the link is still live", which is the opposite of what happened.
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        Assert.IsType<NoContentResult>(await Build(factory, OwnerId).RevokeShare(SessionId, link.Id, default));
        Assert.IsType<NoContentResult>(await Build(factory, OwnerId).RevokeShare(SessionId, link.Id, default));
    }

    [Fact]
    public async Task A_stranger_cannot_withdraw_somebody_elses_link()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        var result = await Build(factory, StrangerId).RevokeShare(SessionId, link.Id, default);
        Assert.IsType<NotFoundResult>(result);

        Assert.IsType<OkObjectResult>(await Build(factory, null).GetShared(link.Token, default));
    }

    [Fact]
    public async Task An_unknown_token_is_a_plain_not_found()
    {
        var factory = await SeedAsync();

        // Unknown, expired and revoked all answer the same way. A distinct "expired" would confirm
        // to somebody guessing tokens that they had found a real one.
        Assert.IsType<NotFoundResult>(await Build(factory, null).GetShared("not-a-real-token", default));
        Assert.IsType<NotFoundResult>(await Build(factory, null).GetShared("", default));
    }

    // ── The owner's view of what they have handed out ─────────────────────────

    [Fact]
    public async Task Opening_a_link_is_counted_and_logged()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        await Build(factory, null).GetShared(link.Token, default);
        await Build(factory, null).GetShared(link.Token, default);

        var listed = await Build(factory, OwnerId).GetShares(SessionId, default);
        var rows = Assert.IsAssignableFrom<IEnumerable<FieldSessionShareRecord>>(
            Assert.IsType<OkObjectResult>(listed.Result).Value);
        Assert.Equal(2, rows.Single().ViewCount);

        var viewsResult = await Build(factory, OwnerId).GetShareViews(SessionId, link.Id, default);
        var views = Assert.IsAssignableFrom<IEnumerable<FieldSessionShareViewRecord>>(
            Assert.IsType<OkObjectResult>(viewsResult.Result).Value).ToList();
        Assert.Equal(2, views.Count);
    }

    [Fact]
    public async Task The_view_log_never_carries_the_visitor_address()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);

        await Build(factory, null).GetShared(link.Token, default);

        await using var db = await factory.CreateDbContextAsync();
        var view = await db.FieldSessionShareLinkViews.FirstAsync();

        // These are people with no account who agreed to nothing. A hash separates two visitors,
        // which is the only question the log is ever asked.
        Assert.NotNull(view.ViewerHash);
        Assert.DoesNotContain("203.0.113.7", view.ViewerHash);
    }

    [Fact]
    public async Task The_same_visitor_hashes_differently_on_two_different_links()
    {
        var factory = await SeedAsync();
        var one = await MakeLinkAsync(factory, OwnerId);
        var two = await MakeLinkAsync(factory, OwnerId);

        await Build(factory, null).GetShared(one.Token, default);
        await Build(factory, null).GetShared(two.Token, default);

        await using var db = await factory.CreateDbContextAsync();
        var hashes = await db.FieldSessionShareLinkViews.Select(v => v.ViewerHash).ToListAsync();

        // Salted per link. Otherwise an owner comparing digests across their links could tell that
        // the same person opened both — a fact about a stranger that nobody asked for.
        Assert.Equal(2, hashes.Count);
        Assert.NotEqual(hashes[0], hashes[1]);
    }

    [Fact]
    public async Task Withdrawn_links_stay_in_the_owners_list()
    {
        var factory = await SeedAsync();
        var link = await MakeLinkAsync(factory, OwnerId);
        await Build(factory, OwnerId).RevokeShare(SessionId, link.Id, default);

        var listed = await Build(factory, OwnerId).GetShares(SessionId, default);
        var rows = Assert.IsAssignableFrom<IEnumerable<FieldSessionShareRecord>>(
            Assert.IsType<OkObjectResult>(listed.Result).Value).ToList();

        // "Was this ever shared, and when did that stop" is a question somebody eventually has to
        // answer — most sharply when evidence turns up where it should not have.
        Assert.Single(rows);
        Assert.False(rows[0].IsLive);
        Assert.NotNull(rows[0].RevokedUtc);
    }

    [Fact]
    public async Task A_stranger_cannot_see_what_has_been_shared()
    {
        var factory = await SeedAsync();
        await MakeLinkAsync(factory, OwnerId);

        var listed = await Build(factory, StrangerId).GetShares(SessionId, default);
        Assert.IsType<NotFoundResult>(listed.Result);
    }

    [Fact]
    public async Task Two_links_never_share_a_token()
    {
        var factory = await SeedAsync();
        var tokens = new HashSet<string>();
        for (var i = 0; i < 25; i++)
            Assert.True(tokens.Add((await MakeLinkAsync(factory, OwnerId)).Token));
    }

    // ── Structural absence ────────────────────────────────────────────────────

    [Fact]
    public void The_shared_shape_has_nowhere_to_put_who_or_where()
    {
        var names = typeof(SharedFieldSessionDetail)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        // Asserted against the SHAPE, not against one response. A condition that withholds these
        // is a line somebody can delete; a record with no field for them cannot start carrying
        // them because a query was widened three months from now.
        foreach (var forbidden in new[]
                 { "InvestigationId", "PlaceId", "CaseId", "OrganizationId",
                   "SubmittedByAppUserId", "RecordedByAppUserId", "RecordedByName",
                   "CreatedByAppUserId", "DocumentUploadFileId", "DeviceSessionId",
                   "PublishedAtUtc", "StoragePath", "MediaReviewState" })
            Assert.DoesNotContain(forbidden, names);
    }

    [Fact]
    public void The_shared_file_shape_carries_no_uploader_and_no_upload_file_id()
    {
        var names = typeof(SharedFieldSessionFile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();

        foreach (var forbidden in new[]
                 { "UploadFileId", "CreatedByAppUserId", "Sha256", "StoragePath",
                   "FieldSessionUploadId" })
            Assert.DoesNotContain(forbidden, names);
    }
}
