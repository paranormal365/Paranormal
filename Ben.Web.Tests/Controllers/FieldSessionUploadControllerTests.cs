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
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Field sessions arriving from a phone, hours after they were recorded.
/// </summary>
/// <remarks>
/// <para>The two things worth guarding are WHO may send one and what happens when the same
/// session is sent twice. Retries are not an edge case here: somebody uploads a night of
/// recordings over whatever connection they got home to, and half of them fail the first time.
/// Two copies of one night is worse than none, because nobody can tell which is which.</para>
/// </remarks>
public sealed class FieldSessionUploadControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid InvestigationId = Guid.NewGuid();
    private static readonly Guid AttendeeId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    /// Storage paths carry a fresh GUID per write, so one store across the suite cannot collide.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Stored = new();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 { (AttendeeId, "An Attendee"), (MemberId, "A Member"), (StrangerId, "A Stranger") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t",
                                       DisplayName = name });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = MemberId,
            Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        db.Investigations.Add(new Investigation
        {
            Id = InvestigationId, OrganizationId = OrgId, Title = "The Old Mill",
            ScheduledDateTime = DateTime.UtcNow.AddDays(-1), DateCreated = DateTime.UtcNow,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = InvestigationId, AppUserId = AttendeeId,
            DateCreated = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static FieldSessionUploadController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.OrgFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((org, name) => $"orgs/{org}/{name}");
        // Personal sessions live under the PERSON, not under a group they may not belong to.
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((user, name) => $"users/{user}/{name}");
        // Writes are remembered and reads hand them back, so a test that reads a session gets
        // the bytes that were written rather than a stub that pretends they vanished. Shared
        // across controllers because a real upload and a later read are different requests —
        // giving each its own store would make every read miss.
        var written = Stored;
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(),
                                        It.IsAny<CancellationToken>()))
               .Returns<string, Stream, CancellationToken>((path, stream, _) =>
               {
                   using var buffer = new MemoryStream();
                   stream.CopyTo(buffer);
                   written[path] = buffer.ToArray();
                   return Task.CompletedTask;
               });
        _ = written;
        storage.Setup(s => s.Exists(It.IsAny<string>()))
               .Returns<string>(path => written.ContainsKey(path));
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns<string, CancellationToken>((path, _) => Task.FromResult<Stream>(
                   written.TryGetValue(path, out var bytes)
                       ? new MemoryStream(bytes) : Stream.Null));

        // Stubbed to behave like the real thing: it writes the bytes through storage and hands
        // back what was served. The existing path-traversal tests never reach it, which is why
        // it had gone unstubbed — and why a test that DID reach it fell over.
        var ingest = new Mock<Ben.Data.WebApi.Services.IMediaIngestService>();
        ingest.Setup(m => m.IngestAsync(It.IsAny<IFormFile>(), It.IsAny<string>(),
                                        It.IsAny<Guid>(), It.IsAny<CancellationToken>(),
                                        It.IsAny<bool>()))
              .Returns<IFormFile, string, Guid, CancellationToken, bool>(
                  async (file, path, uploadFileId, _, _) =>
                  {
                      using var buffer = new MemoryStream();
                      await file.CopyToAsync(buffer);
                      written[path] = buffer.ToArray();
                      return new Ben.Data.WebApi.Services.IngestedMedia(
                          new UploadFileMetadata
                          {
                              Id = Guid.NewGuid(), UploadFileId = uploadFileId,
                              MediaKind = "Audio", ExtractedAtUtc = DateTime.UtcNow,
                          },
                          buffer.Length, file.ContentType ?? "application/octet-stream", false);
                  });

        var controller = new FieldSessionUploadController(
            factory, storage.Object, ingest.Object,
            NullLogger<FieldSessionUploadController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            }
        };
        return controller;
    }

    /// <summary>
    /// A form file with real headers.
    /// </summary>
    /// <remarks>
    /// FormFile throws on <c>ContentType</c> when it was built without a header dictionary, and
    /// the failure surfaces deep inside the controller rather than at construction — so every
    /// file in these tests is built here.
    /// </remarks>
    private static IFormFile Upload(byte[] bytes, string name, string contentType)
        => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary { ["Content-Type"] = contentType },
        };

    /// <summary>
    /// Bytes that begin like an M4A — the door checks the first bytes now — followed by a few of
    /// whatever, so a test about serving is not refused for not being a recording.
    /// </summary>
    private static byte[] M4a(int trailing)
        => new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ', 0, 0, 0, 0 }
           .Concat(Enumerable.Range(1, trailing).Select(i => (byte)i)).ToArray();

    private static IFormFile Document(string json)
    {
        return Upload(Encoding.UTF8.GetBytes(json), "data.json", "application/json");
    }

    private static string ValidDocument(string startedAt = "2026-08-24T22:05:07.000Z",
                                        int readings = 2) =>
        $$"""
        {
          "format_version": "1.0.0",
          "device": { "manufacturer": "Apple", "model": "iPhone17,1" },
          "session": {
            "started_at": "{{startedAt}}",
            "ended_at": "2026-08-25T03:11:00.000Z",
            "location_label": "Back bedroom, north wall",
            "trigger": { "mode": "hybrid", "interval_seconds": 2 }
          },
          "readings": [
            { "at": "{{startedAt}}", "triggered_by": "interval",
              "measurements": { "emf": { "value": 48.2, "unit": "uT" } } }
            {{(readings > 1 ? """
            , { "at": "2026-08-24T22:06:07.000Z", "triggered_by": "event",
                "measurements": { "marker": { "value": "sentry_emf" } } }
            """ : "")}}
          ]
        }
        """;

    // ── Who may send one ──────────────────────────────────────────────────────

    [Fact]
    public async Task Somebody_who_was_on_the_investigation_can_send_a_session()
    {
        var factory = await SeedAsync();
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);

        var record = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("iPhone17,1", record.DeviceModel);
        Assert.Equal("Back bedroom, north wall", record.LocationLabel);
        Assert.Equal(2, record.ReadingCount);
        // One of the two readings carries a marker channel.
        Assert.Equal(1, record.MarkerCount);
    }

    [Fact]
    public async Task A_member_of_the_group_can_send_one_too()
    {
        var factory = await SeedAsync();
        var result = await Build(factory, MemberId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_stranger_is_told_the_investigation_is_not_there()
    {
        // Not "forbidden": whether somebody else's investigation exists is not a thing to let
        // an outsider probe for.
        var factory = await SeedAsync();
        var result = await Build(factory, StrangerId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Retries ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sending_the_same_session_twice_updates_it_rather_than_duplicating_it()
    {
        // The case this exists for: an upload that failed halfway and was tried again. Two
        // copies of one night is worse than none, because nobody can tell which is which.
        var factory = await SeedAsync();
        var deviceSessionId = Guid.NewGuid();

        var first = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), deviceSessionId, InvestigationId, AttendeeId, "An Attendee", default);
        var firstRecord = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(first.Result).Value);

        var second = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), deviceSessionId, InvestigationId, AttendeeId, "An Attendee", default);
        var secondRecord = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(second.Result).Value);

        Assert.Equal(firstRecord.Id, secondRecord.Id);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.FieldSessionUploads.CountAsync());
    }

    [Fact]
    public async Task Two_different_sessions_are_two_records()
    {
        var factory = await SeedAsync();
        await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);
        await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.FieldSessionUploads.CountAsync());
    }

    // ── What it refuses ───────────────────────────────────────────────────────

    [Fact]
    public async Task Something_that_is_not_a_session_document_is_refused_with_a_reason()
    {
        var factory = await SeedAsync();
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document("""{"hello":"world"}"""), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        // A sentence somebody can act on, not a status code.
        Assert.Contains("format_version", refusal.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task A_document_from_a_future_format_is_refused_rather_than_half_read()
    {
        // Reading a version 2 document with version 1 assumptions is how silently wrong records
        // get created. Better to say plainly that this server cannot read it.
        var factory = await SeedAsync();
        var future = ValidDocument().Replace("\"1.0.0\"", "\"2.0.0\"");
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document(future), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("version 1", refusal.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task An_empty_document_is_refused()
    {
        var factory = await SeedAsync();
        var empty = Upload([], "data.json", "application/json");
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(empty, Guid.NewGuid(), InvestigationId, AttendeeId, null, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_session_with_no_identifier_of_its_own_is_refused()
    {
        // Without it, a retry cannot find its own row and every attempt makes another copy.
        var factory = await SeedAsync();
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.Empty, InvestigationId, AttendeeId, "An Attendee", default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../../secrets.json")]
    [InlineData("media/../../out.m4a")]
    [InlineData("media\\audio.m4a")]
    [InlineData("")]
    public async Task A_file_path_that_could_escape_the_bundle_is_refused(string path)
    {
        // The format's path rules are a security boundary: an importer expanding a bundle must
        // never be steered outside its own directory.
        var factory = await SeedAsync();
        var submitted = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        var result = await Build(factory, AttendeeId).SubmitFile(
            session.Id, Upload(M4a(4), "clip.m4a", "audio/mp4"), path, null, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Listing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sessions_are_listed_newest_first_for_people_entitled_to_see_them()
    {
        var factory = await SeedAsync();
        await Build(factory, AttendeeId).SubmitDocument(Document(ValidDocument("2026-08-20T20:00:00.000Z")), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);
        await Build(factory, AttendeeId).SubmitDocument(Document(ValidDocument("2026-08-24T22:05:07.000Z")), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);

        var listed = await Build(factory, MemberId).GetForInvestigation(InvestigationId, default);
        var sessions = Assert.IsAssignableFrom<IEnumerable<FieldSessionRecord>>(
            Assert.IsType<OkObjectResult>(listed.Result).Value).ToList();

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].StartedAt > sessions[1].StartedAt);

        var refused = await Build(factory, StrangerId).GetForInvestigation(InvestigationId, default);
        Assert.IsType<NotFoundResult>(refused.Result);
    }

    // ── Sessions that belong to nobody's investigation ────────────────────────

    [Fact]
    public async Task A_session_can_belong_to_the_account_with_no_investigation_at_all()
    {
        // Somebody scouting a building, or a tour guide walking a route. There is no case and no
        // investigation yet — and refusing to take the recording until there is one would mean
        // losing the night that prompted it.
        var factory = await SeedAsync();
        var result = await Build(factory, StrangerId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(),
            investigationId: null, StrangerId, "A Stranger", default);

        var record = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Null(record.InvestigationId);

        // And it is theirs — someone with no group at all still has their own sessions.
        var mine = await Build(factory, StrangerId).GetMine(default);
        var sessions = Assert.IsAssignableFrom<IEnumerable<FieldSessionRecord>>(
            Assert.IsType<OkObjectResult>(mine.Result).Value).ToList();
        Assert.Single(sessions);
    }

    [Fact]
    public async Task A_personal_session_is_not_visible_to_anybody_else()
    {
        var factory = await SeedAsync();
        await Build(factory, StrangerId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(),
            investigationId: null, StrangerId, "A Stranger", default);

        var others = await Build(factory, MemberId).GetMine(default);
        var sessions = Assert.IsAssignableFrom<IEnumerable<FieldSessionRecord>>(
            Assert.IsType<OkObjectResult>(others.Result).Value);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task A_personal_session_can_be_attached_to_an_investigation_afterwards()
    {
        // The order this really happens in: record first, decide where it belongs later, when
        // there is signal and something to attach it to.
        var factory = await SeedAsync();
        var deviceSessionId = Guid.NewGuid();

        var personal = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), deviceSessionId,
            investigationId: null, AttendeeId, "An Attendee", default);
        var first = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(personal.Result).Value);
        Assert.Null(first.InvestigationId);

        var attached = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), deviceSessionId,
            InvestigationId, AttendeeId, "An Attendee", default);
        var second = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(attached.Result).Value);

        Assert.Equal(first.Id, second.Id);           // the same session, not a second copy
        Assert.Equal(InvestigationId, second.InvestigationId);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.FieldSessionUploads.CountAsync());
    }

    [Fact]
    public async Task Two_people_may_each_hold_their_own_copy_of_the_same_exported_session()
    {
        // Exports get handed around. Each person's upload is their own record rather than one
        // of them silently overwriting the other's.
        var factory = await SeedAsync();
        var shared = Guid.NewGuid();

        await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), shared, InvestigationId, AttendeeId, "An Attendee", default);
        await Build(factory, MemberId).SubmitDocument(
            Document(ValidDocument()), shared, InvestigationId, AttendeeId, "An Attendee", default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.FieldSessionUploads.CountAsync());
    }

    // ── Reading one back ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_document_comes_back_verbatim_for_playing_back()
    {
        // Returned as it was written, not reshaped: it is the only copy that is definitely what
        // the device recorded, and a page reading anything else shows a story about the
        // readings rather than the readings.
        var factory = await SeedAsync();
        var submitted = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            AttendeeId, "An Attendee", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        var result = await Build(factory, AttendeeId).GetSession(session.Id, default);
        var detail = Assert.IsType<FieldSessionDetail>(
            Assert.IsType<OkObjectResult>(result).Value);

        Assert.Contains("\"format_version\"", detail.Document);
        Assert.Contains("sentry_emf", detail.Document);
        Assert.Equal(session.Id, detail.Session.Id);
    }

    [Fact]
    public async Task A_personal_session_is_readable_only_by_the_person_who_sent_it()
    {
        var factory = await SeedAsync();
        var submitted = await Build(factory, StrangerId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(),
            investigationId: null, StrangerId, "A Stranger", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        Assert.IsType<OkObjectResult>(
            await Build(factory, StrangerId).GetSession(session.Id, default));
        // Nobody else, not even a member of some group — it was never theirs.
        Assert.IsType<NotFoundResult>(
            await Build(factory, MemberId).GetSession(session.Id, default));
    }

    [Fact]
    public async Task An_investigation_session_is_readable_by_the_group_working_it()
    {
        var factory = await SeedAsync();
        var submitted = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            AttendeeId, "An Attendee", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        // A member who was not on the night can still review what came back from it.
        Assert.IsType<OkObjectResult>(
            await Build(factory, MemberId).GetSession(session.Id, default));
        Assert.IsType<NotFoundResult>(
            await Build(factory, StrangerId).GetSession(session.Id, default));
    }

    // ── Getting a recording back ──────────────────────────────────────────────

    [Fact]
    public async Task A_recording_comes_back_to_whoever_may_read_the_session()
    {
        var factory = await SeedAsync();
        var submitted = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            AttendeeId, "An Attendee", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        var attached = await Build(factory, AttendeeId).SubmitFile(
            session.Id, Upload(M4a(5), "clip.m4a", "audio/mp4"),
            "media/audio-001.m4a", null, default);
        var file = Assert.IsType<FieldSessionFileRecord>(
            Assert.IsType<OkObjectResult>(attached.Result).Value);

        var streamed = await Build(factory, MemberId).GetFile(session.Id, file.Id, default);
        var result = Assert.IsType<FileStreamResult>(streamed);
        // Range processing matters: a two-hour recording that must be fetched whole before it
        // plays is a recording nobody reviews.
        Assert.True(result.EnableRangeProcessing);

        Assert.IsType<NotFoundResult>(
            await Build(factory, StrangerId).GetFile(session.Id, file.Id, default));
    }

    [Fact]
    public async Task A_recording_whose_bytes_are_gone_says_so_rather_than_streaming_nothing()
    {
        // The row can outlive the file. An empty stream would play as silence, which somebody
        // would hear as a recording of a quiet room rather than a missing file.
        var factory = await SeedAsync();
        var submitted = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            AttendeeId, "An Attendee", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        var attached = await Build(factory, AttendeeId).SubmitFile(
            session.Id, Upload(M4a(3), "clip.m4a", "audio/mp4"),
            "media/audio-002.m4a", null, default);
        var file = Assert.IsType<FieldSessionFileRecord>(
            Assert.IsType<OkObjectResult>(attached.Result).Value);

        Stored.Clear();   // the bytes went away; the row did not

        var streamed = await Build(factory, AttendeeId).GetFile(session.Id, file.Id, default);
        var refusal = Assert.IsType<NotFoundObjectResult>(streamed);
        Assert.Contains("no longer on the server", refusal.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task A_session_whose_document_is_gone_says_so_rather_than_looking_empty()
    {
        var factory = await SeedAsync();
        var submitted = await Build(factory, AttendeeId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            AttendeeId, "An Attendee", default);
        var session = Assert.IsType<FieldSessionRecord>(
            Assert.IsType<OkObjectResult>(submitted.Result).Value);

        Stored.Clear();

        var result = await Build(factory, AttendeeId).GetSession(session.Id, default);
        var refusal = Assert.IsType<NotFoundObjectResult>(result);
        // A night where nothing happened and a night nobody can read are different facts.
        Assert.Contains("no longer on the server", refusal.Value?.ToString() ?? "");
    }

    // ── Who may contribute to a PUBLIC investigation ──────────────────────────

    [Fact]
    public async Task Anybody_may_add_a_recording_to_a_public_investigation()
    {
        // An open investigation is an invitation, and thirty strangers with phones is the whole
        // value of one — the same bargain the public-event evidence door already makes.
        var factory = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var investigation = await db.Investigations.SingleAsync(i => i.Id == InvestigationId);
            investigation.Visibility = InvestigationVisibility.Public;
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, StrangerId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            StrangerId, "A Stranger", default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_public_case_opens_its_investigations_to_recordings_too()
    {
        var factory = await SeedAsync();
        var caseId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = OrgId, Title = "An open case",
                City = "Nashville", State = "TN", IsPublic = true,
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            });
            var investigation = await db.Investigations.SingleAsync(i => i.Id == InvestigationId);
            investigation.CaseId = caseId;
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, StrangerId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            StrangerId, "A Stranger", default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_group_only_investigation_still_turns_a_stranger_away()
    {
        // The widening must stay tied to the flags. A private residence's investigation is the
        // whole reason the default is closed.
        var factory = await SeedAsync();
        var result = await Build(factory, StrangerId).SubmitDocument(
            Document(ValidDocument()), Guid.NewGuid(), InvestigationId,
            StrangerId, "A Stranger", default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    /// <summary>
    /// A session that recorded nothing is refused at the door rather than stored as a row, a
    /// file and a "Play back" button for a page with nothing on it.
    /// </summary>
    [Fact]
    public async Task A_document_with_no_readings_is_refused_with_a_sentence()
    {
        var factory = await SeedAsync();
        var empty = ValidDocument().Replace("\"readings\": [", "\"readings\": [ ] , \"_was\": [");

        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document(empty), Guid.NewGuid(), InvestigationId, AttendeeId, "An Attendee", default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("no readings", refusal.Value?.ToString());

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.FieldSessionUploads.CountAsync());
    }

    /// <summary>
    /// Not saying who recorded it means the signed-in sender did — which is what the app sends
    /// anyway. Before this a hand-built upload played back as "nobody signed in when recorded".
    /// </summary>
    [Fact]
    public async Task Leaving_out_who_recorded_it_attributes_it_to_the_sender()
    {
        var factory = await SeedAsync();
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, null, null, default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.FieldSessionUploads.SingleAsync();
        Assert.Equal(AttendeeId, row.RecordedByAppUserId);
    }

    /// <summary>The empty id is the client's deliberate "nobody", and is kept as such.</summary>
    [Fact]
    public async Task Saying_nobody_recorded_it_is_respected()
    {
        var factory = await SeedAsync();
        var result = await Build(factory, AttendeeId)
            .SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, Guid.Empty, null, default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.FieldSessionUploads.SingleAsync()).RecordedByAppUserId);
    }

    /// <summary>
    /// The list said every recording was 0 bytes while the detail said 2,048: the list loaded the
    /// file rows without the upload rows their sizes live on. Found by the guard work, when the
    /// app's placeholder "shrank" to nothing in the list and not in the detail.
    /// </summary>
    [Fact]
    public async Task The_list_reports_each_recordings_real_size()
    {
        var factory = await SeedAsync();
        var ctrl = Build(factory, AttendeeId);
        var created = await ctrl.SubmitDocument(Document(ValidDocument()), Guid.NewGuid(), InvestigationId, AttendeeId, null, default);
        var session = Assert.IsType<FieldSessionRecord>(Assert.IsType<OkObjectResult>(created.Result).Value);
        var bytes = M4a(5);
        Assert.IsType<OkObjectResult>((await ctrl.SubmitFile(session.Id, Upload(bytes, "clip.m4a", "audio/mp4"), "media/clip.m4a", null, default)).Result);

        var listed = await Build(factory, AttendeeId).GetMine(default);
        var rows = Assert.IsAssignableFrom<IEnumerable<FieldSessionRecord>>(Assert.IsType<OkObjectResult>(listed.Result).Value);
        var file = Assert.Single(rows.Single(r => r.Id == session.Id).Files);
        Assert.Equal(bytes.Length, file.FileSize);
    }
}