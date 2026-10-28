using System.Security.Claims;
using System.Text;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Media;
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
/// A session carrying the person map's figures and shapes: its joints files and pictures are taken
/// with the video they belong to, the timeline lists each sighting with the files a player draws
/// it from, and the public copy leaves them out (LiDAR plan, phase 4).
/// </summary>
public sealed class PersonMapSessionTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "person-map-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private const string Document = """
        { "format_version": "1.2.0",
          "device": { "manufacturer": "Apple", "model": "iPhone16,1" },
          "session": { "started_at": "2026-10-28T21:00:00Z", "ended_at": "2026-10-28T21:30:00Z",
                       "trigger": { "mode": "event", "event_description": "a person did it" } },
          "readings": [
            { "at": "2026-10-28T21:01:01Z", "triggered_by": "event", "note": "In view for 4 seconds.",
              "measurements": { "marker": { "value": "figure_seen" },
                                "figure": { "value": "figures/figure-001.json", "from": 1.2, "to": 4.8,
                                            "video": "media/video-001.mov", "peak_confidence": 0.94,
                                            "picture": "figures/figure-001.jpg" } } },
            { "at": "2026-10-28T21:01:07.5Z", "triggered_by": "event", "note": "45 × 150 × 5 cm, 2.6 m away, not in the room's scan.",
              "measurements": { "marker": { "value": "unexplained_shape" },
                                "shape": { "value": "45 × 150 × 5 cm", "size_cm": [45, 150, 5], "distance_m": 2.6,
                                           "where": [0.7, 0.85, -0.2], "from": 7.5, "to": 11,
                                           "video": "media/video-001.mov", "picture": "figures/shape-001.jpg" } } },
            { "at": "2026-10-28T21:05:00Z", "triggered_by": "manual", "note": "video: media/video-001.mov",
              "measurements": { "marker": { "value": "video" } } }
          ] }
        """;

    /// A QuickTime header: a box size, then "ftyp".
    private static readonly byte[] Movie = [0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'q', (byte)'t', (byte)' ', (byte)' ', 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Joints = """[{"t":1.200,"joints":[[0.512,0.301,2.41,0.96],null]}]"""u8.ToArray();

    [Theory]
    [InlineData("figures/figure-001.json", "[{\"t\":1", true)]
    [InlineData("figures/figure-001.json", "\n  [", true)]
    [InlineData("figures/figure-001.json", "{\"t\":1}", false)]
    [InlineData("media/notes.json", "[{\"t\":1", false)]
    [InlineData("data2.json", "[1]", false)]
    public void A_joints_file_is_taken_only_where_the_person_map_keeps_them(string path, string start, bool taken)
    {
        var header = Encoding.UTF8.GetBytes(start);
        var refusal = FieldSessionFileGuard.Refusal(path, contentType: null, header.Length, header, filesAlready: 0);
        Assert.Equal(taken, refusal is null);
        if (taken) Assert.Equal("application/json", FieldSessionFileGuard.ContentTypeFor(path));
    }

    [Fact]
    public async Task A_session_carrying_figures_is_taken_and_its_timeline_lists_them_with_their_files()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var storage = new LocalFileStorageService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = _root }).Build());

        var person = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = person, DisplayName = "Dana Reyes", DateCreated = Start });
            db.UploadFileTypes.Add(new UploadFileType
            {
                Id = new Guid("20000000-0000-0000-0000-000000000001"), Name = "Evidence",
                DateCreated = Start, CreatedByAppUserId = person,
            });
            await db.SaveChangesAsync();
        }

        var file = BenBundleTests.StoredZip.Write(
            ("data.json", Encoding.UTF8.GetBytes(Document)),
            ("media/video-001.mov", Movie),
            ("figures/figure-001.json", Joints),
            ("figures/figure-001.jpg", TestScans.Jpeg),
            ("figures/shape-001.jpg", TestScans.Jpeg));
        var controller = new FieldSessionUploadController(
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

        var sent = await controller.SubmitBundle(new FormFile(file, 0, file.Length, "file", "session.ben"),
                                                 Guid.NewGuid(), null, null, null, default);
        Assert.True(sent.Result is OkObjectResult, (sent.Result as ObjectResult)?.Value?.ToString());
        var session = Assert.IsType<FieldSessionRecord>(Assert.IsType<OkObjectResult>(sent.Result).Value);

        List<TimelineFile> files;
        await using (var db = await sqlite.NewContextAsync())
        {
            var rows = await db.FieldSessionUploadFiles.AsNoTracking()
                .Where(f => f.FieldSessionUploadId == session.Id).ToListAsync();
            Assert.Contains(rows, r => r.RelativePath == "figures/figure-001.json" && r.ContentType == "application/json");
            files = rows.Select(r => new TimelineFile(r.Id, r.RelativePath, null)).ToList();
        }

        var timeline = FieldSessionTimelineBuilder.Build(Document, files,
            Ben.Service.Models.FieldSessions.TimelinePositionPolicy.Withheld);

        var figure = Assert.Single(timeline.Figures!);
        Assert.Equal(61_000, figure.T);
        Assert.Equal("figures/figure-001.json", figure.Joints);
        Assert.Equal(files.Single(f => f.RelativePath == "figures/figure-001.json").FileId, figure.JointsFileId);
        Assert.Equal(files.Single(f => f.RelativePath == "media/video-001.mov").FileId, figure.VideoFileId);
        Assert.Equal(files.Single(f => f.RelativePath == "figures/figure-001.jpg").FileId, figure.PictureFileId);
        Assert.Equal((1.2, 4.8), (figure.From, figure.To));
        Assert.Equal(0.94, figure.PeakConfidence);
        Assert.Equal("In view for 4 seconds.", figure.Note);

        var shape = Assert.Single(timeline.Shapes!);
        Assert.Equal(67_500, shape.T);
        Assert.Equal("45 × 150 × 5 cm", shape.Words);
        Assert.Equal([45, 150, 5], Assert.IsType<double[]>(shape.SizeCm));
        Assert.Equal(2.6, shape.DistanceM);
        Assert.Equal([0.7, 0.85, -0.2], Assert.IsType<double[]>(shape.Where));
        Assert.Equal(files.Single(f => f.RelativePath == "figures/shape-001.jpg").FileId, shape.PictureFileId);

        // The pictures are the person map's, not photographs: neither is on the photo strip.
        Assert.DoesNotContain(timeline.Photos, p => p.Path.StartsWith("figures/", StringComparison.Ordinal));
        // And both are marks a player draws without knowing either channel.
        Assert.Contains(timeline.Markers, m => m.Kind == "figure_seen");
        Assert.Contains(timeline.Markers, m => m.Kind == "unexplained_shape");
    }

    [Fact]
    public async Task The_public_copy_of_a_session_leaves_the_person_map_out()
    {
        var source = BenBundleTests.StoredZip.Write(
            ("data.json", Encoding.UTF8.GetBytes(Document)),
            ("media/video-001.mov", Movie),
            ("figures/figure-001.json", Joints),
            ("figures/figure-001.jpg", TestScans.Jpeg));
        using var destination = new MemoryStream();

        await PublicSessionBundle.WriteAsync(source, destination, 36.1, -86.7, includeRecordings: true,
                                             recordedByAccountId: null, sessionId: Guid.NewGuid(), sealedAtUtc: Start);

        destination.Position = 0;
        var copy = await BenBundle.ReadAsync(destination);
        Assert.NotNull(copy.Find("media/video-001.mov"));
        Assert.Null(copy.Find("figures/figure-001.json"));
        Assert.Null(copy.Find("figures/figure-001.jpg"));
    }
}
