using System.Security.Claims;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Media;
using Ben.Data.WebApi.Services.Places;
using Ben.Web.Tests.Controllers;
using Ben.Web.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// A session carrying a comparison with a place's room scan: its changes become the place's rows,
/// its pictures are served from inside the session's file, and its public copy leaves them out
/// (LiDAR plan, phase 2).
/// </summary>
public sealed class PlaceScanChangeTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scan-changes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private static string Document(Guid placeScanId, params (string What, string Object, double? Distance)[] changes)
    {
        var readings = new StringBuilder();
        readings.Append($$"""
            { "at": "2026-10-28T21:10:00Z", "triggered_by": "manual",
              "measurements": { "marker": { "value": "room_compared" },
                                "scan": { "value": "scans/compare-001.scan", "room": "Bedroom 1",
                                          "place_scan_id": "{{placeScanId}}", "place_scan_version": 1, "changes": {{changes.Length}} } } }
            """);
        var index = 0;
        foreach (var (what, obj, distance) in changes)
        {
            index++;
            readings.Append($$"""
                , { "at": "2026-10-28T21:10:00Z", "triggered_by": "event", "note": "The {{obj}} {{what}}.",
                    "measurements": { "marker": { "value": "scan_change" },
                                      "scan_change": { "value": "{{what}}", "object": "{{obj}}",
                                                       {{(distance is { } d ? $"\"distance_cm\": {d}," : "")}}
                                                       "size_cm": [45, 90, 45], "where": [1.5, 0.45, -2.25],
                                                       "picture": "scans/compare-001.scan#changes/{{index:00}}.jpg",
                                                       "place_scan_id": "{{placeScanId}}" } } }
                """);
        }
        return $$"""
            { "format_version": "1.2.0",
              "device": { "manufacturer": "Apple", "model": "iPhone16,1" },
              "session": { "started_at": "2026-10-28T21:00:00Z", "ended_at": "2026-10-28T21:30:00Z",
                           "trigger": { "mode": "event", "event_description": "a person did it" } },
              "readings": [ {{readings}} ] }
            """;
    }

    [Fact]
    public void Each_change_reading_is_a_row_against_the_scan_it_names()
    {
        var scanId = Guid.NewGuid();
        var rows = PlaceScanChangeRows.Read(Document(scanId, ("moved", "chair", 42), ("missing", "table", null)));

        Assert.Equal(2, rows.Count);
        var moved = rows[0];
        Assert.Equal(PlaceScanChangeKind.Moved, moved.Kind);
        Assert.Equal("chair", moved.ObjectCategory);
        Assert.Equal(42, moved.DistanceCm);
        Assert.Equal((1.5, 0.45, -2.25), (moved.WhereX, moved.WhereY, moved.WhereZ));
        Assert.Equal("scans/compare-001.scan#changes/01.jpg", moved.PictureEntryPath);
        Assert.Equal(scanId, moved.PlaceScanId);
        Assert.Equal(new DateTime(2026, 10, 28, 21, 10, 0, DateTimeKind.Utc), moved.ComparedUtc);
        Assert.Equal(PlaceScanChangeKind.Missing, rows[1].Kind);
        Assert.Equal(45, rows[1].SizeXCm);
    }

    [Fact]
    public async Task A_session_sent_with_a_comparison_writes_its_rows_serves_its_pictures_and_replaces_them_when_sent_again()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var storage = new LocalFileStorageService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = _root }).Build());

        var person = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var scanId = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = person, DisplayName = "Dana Reyes", DateCreated = Start });
            db.UploadFileTypes.Add(new UploadFileType
            {
                Id = new Guid("20000000-0000-0000-0000-000000000001"), Name = "Evidence",
                DateCreated = Start, CreatedByAppUserId = person,
            });
            db.Places.Add(new Place
            {
                Id = placeId, Name = "The Hermitage Hotel", Kind = PlaceKind.PublicLocation,
                DateCreated = Start, CreatedByAppUserId = person,
            });
            db.PlaceScans.Add(new PlaceScan
            {
                Id = scanId, PlaceId = placeId, RoomKey = "bedroom 1", RoomName = "Bedroom 1", Version = 1,
                State = PlaceScanState.Current, Retention = PlaceScanRetention.ModelOnly, ScannedUtc = Start,
                ScannedByAppUserId = person, StoragePath = "places/x/room.scan", FileSize = 1,
                Sha256 = new string('0', 64), ScannerVersion = 1, DateCreated = Start, CreatedByAppUserId = person,
            });
            await db.SaveChangesAsync();
        }

        var picture = TestScans.Jpeg;
        var comparison = BenBundleTests.StoredZip.Write(
            ("manifest.json", """{"format":"ishaunted-scan/1","kind":"comparison"}"""u8.ToArray()),
            ("changes.json", "[]"u8.ToArray()),
            ("changes/01.jpg", picture)).ToArray();
        var deviceSessionId = Guid.NewGuid();

        IFormFile Bundle(string document)
        {
            var file = BenBundleTests.StoredZip.Write(
                ("data.json", Encoding.UTF8.GetBytes(document)),
                ("scans/compare-001.scan", comparison));
            return new FormFile(file, 0, file.Length, "file", "session.ben");
        }

        FieldSessionUploadController Controller() => new(
            sqlite.Factory, storage, new Mock<IMediaIngestService>().Object,
            new MediaRetentionPolicy(new SubscriptionLimitGuard(sqlite.Factory)),
            TestBundles.Store(storage), NullLogger<FieldSessionUploadController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, person.ToString())], "Bearer")),
                },
            },
        };

        var sent = await Controller().SubmitBundle(
            Bundle(Document(scanId, ("moved", "chair", 42), ("new", "unclassified", null))),
            deviceSessionId, null, null, null, default);
        var session = Assert.IsType<FieldSessionRecord>(Assert.IsType<OkObjectResult>(sent.Result).Value);

        await using (var db = await sqlite.NewContextAsync())
        {
            var rows = await db.PlaceScanChanges.AsNoTracking().OrderBy(c => c.Kind).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(session.Id, r.FieldSessionUploadId));
            Assert.Equal([PlaceScanChangeKind.Moved, PlaceScanChangeKind.New], rows.Select(r => r.Kind));
        }

        // The picture of the chair, out of the comparison inside the session's one file.
        var served = Assert.IsType<FileStreamResult>(
            await Controller().GetScanMember(session.Id, "compare-001.scan", "changes/01.jpg", default));
        Assert.Equal("image/jpeg", served.ContentType);
        using (var bytes = new MemoryStream())
        {
            await served.FileStream.CopyToAsync(bytes);
            Assert.Equal(picture, bytes.ToArray());
        }
        Assert.IsType<FileStreamResult>(await Controller().GetScanMember(session.Id, "compare-001.scan", null, default));
        Assert.IsType<NotFoundResult>(await Controller().GetScanMember(session.Id, "compare-001.scan", "changes/09.jpg", default));
        Assert.IsType<NotFoundResult>(await Controller().GetScanMember(session.Id, "..%2Fdata.json", null, default));

        // Sent again with one change: the rows follow the document.
        await Controller().SubmitBundle(Bundle(Document(scanId, ("missing", "table", null))), deviceSessionId, null, null, null, default);
        await using (var db = await sqlite.NewContextAsync())
        {
            var row = Assert.Single(db.PlaceScanChanges);
            Assert.Equal(PlaceScanChangeKind.Missing, row.Kind);
        }

        // A change naming no scan this server knows is dropped quietly.
        await Controller().SubmitBundle(Bundle(Document(Guid.NewGuid(), ("moved", "chair", 10))), deviceSessionId, null, null, null, default);
        await using (var db = await sqlite.NewContextAsync())
            Assert.Empty(db.PlaceScanChanges);
    }

    [Fact]
    public async Task The_public_copy_of_a_session_leaves_its_comparisons_out()
    {
        var source = BenBundleTests.StoredZip.Write(
            ("data.json", Encoding.UTF8.GetBytes(Document(Guid.NewGuid(), ("moved", "chair", 42)))),
            ("media/photo-001.jpg", TestScans.Jpeg),
            ("scans/compare-001.scan", BenBundleTests.StoredZip.Write(("manifest.json", "{}"u8.ToArray())).ToArray()));
        using var destination = new MemoryStream();

        await PublicSessionBundle.WriteAsync(source, destination, 36.1, -86.7, includeRecordings: true,
                                             recordedByAccountId: null, sessionId: Guid.NewGuid(), sealedAtUtc: Start);

        destination.Position = 0;
        var copy = await BenBundle.ReadAsync(destination);
        Assert.NotNull(copy.Find("media/photo-001.jpg"));
        Assert.Null(copy.Find("scans/compare-001.scan"));
    }

    [Fact]
    public void The_timeline_lists_the_comparison_and_what_it_found()
    {
        var scanId = Guid.NewGuid();
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(scanId, ("moved", "chair", 42)), [], Ben.Service.Models.FieldSessions.TimelinePositionPolicy.Withheld);

        var compared = Assert.Single(timeline.RoomScans!);
        Assert.Equal("compared", compared.Kind);
        Assert.Equal("Bedroom 1", compared.Room);
        Assert.Equal("scans/compare-001.scan", compared.Path);
        Assert.Equal(scanId, compared.PlaceScanId);
        Assert.Equal(1, compared.Changes);

        var change = Assert.Single(timeline.ScanChanges!);
        Assert.Equal("moved", change.What);
        Assert.Equal("chair", change.Object);
        Assert.Equal(42, change.DistanceCm);
        Assert.Equal([1.5, 0.45, -2.25], Assert.IsType<double[]>(change.Where));
        Assert.Equal(600_000, change.T);
        Assert.Equal("The chair moved.", change.Note);
    }
}
