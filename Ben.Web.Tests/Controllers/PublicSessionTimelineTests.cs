using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.FieldSessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A place's archived session, played back by a visitor (10/14/2026): where they stood is the
/// place's public point, recordings only once approved, and nothing the archive would not list.
/// </summary>
public sealed class PublicSessionTimelineTests
{
    // Somewhere precise, with digits that cannot occur by accident in anything else.
    private const string TrueLatitude = "36.1627413";
    private const string TrueLongitude = "-86.7815972";
    private static readonly Guid Owner = Guid.NewGuid();

    private static readonly string Document = $$$"""
        {"format_version":"1.0.0","device":{"manufacturer":"Apple","model":"iPhone17,1"},
         "session":{"started_at":"2026-09-27T01:00:00Z","ended_at":"2026-09-27T01:00:20Z","location_label":"North wall"},
         "readings":[
           {"at":"2026-09-27T01:00:00Z","measurements":{"emf":{"value":48}},
            "position":{"latitude":{{{TrueLatitude}}},"longitude":{{{TrueLongitude}}},"accuracy_meters":8},
            "audio_ref":{"filename":"media/audio-001.m4a","start_offset_seconds":0}},
           {"at":"2026-09-27T01:00:10Z","measurements":{"emf":{"value":52}},
            "position":{"latitude":36.1628413,"longitude":{{{TrueLongitude}}},"accuracy_meters":9}}
         ]}
        """;

    private sealed class Storage : IFileStorageService
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, ct);
            Files[relativePath] = buffer.ToArray();
        }
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Files[relativePath], writable: false));
        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        { Files.TryRemove(relativePath, out _); return Task.CompletedTask; }
        public Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default) => Task.CompletedTask;
        public bool Exists(string relativePath) => Files.ContainsKey(relativePath);
        public IReadOnlyList<string> ListFiles(string relativeDirectory) => [];
        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    private sealed record Seeded(IDbContextFactory<BenDataContext> Factory, Storage Storage,
                                 Guid Approved, Guid Held, Guid Unpublished, Guid AtAHome, Guid ApprovedSound);

    private static async Task<Seeded> SeedAsync()
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new Storage();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = Owner, UserName = "o@t", Email = "o@t", DisplayName = "Ada Recorder" });

        Guid Place(PlaceKind kind)
        {
            var id = Guid.NewGuid();
            db.Places.Add(new Place { Id = id, Name = "The Old Mill", City = "Nashville", State = "TN", Kind = kind,
                Latitude = 36.1627413m, Longitude = -86.7815972m, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner });
            return id;
        }
        var mill = Place(PlaceKind.PublicLocation);
        var home = Place(PlaceKind.PrivateResidence);
        var soundIds = new Dictionary<Guid, Guid>();

        Guid Session(Guid placeId, bool published, FeedMediaReviewState media)
        {
            var docPath = $"orgs/x/{Guid.NewGuid():N}.json";
            storage.Files[docPath] = Encoding.UTF8.GetBytes(Document);
            var doc = new UploadFile { Id = Guid.NewGuid(), FileName = "data.json", ContentType = "application/json",
                StoragePath = docPath, DateCreated = DateTime.UtcNow };
            var soundPath = $"orgs/x/{Guid.NewGuid():N}.m4a";
            storage.Files[soundPath] = new byte[64];
            var sound = new UploadFile { Id = Guid.NewGuid(), FileName = "audio-001.m4a", ContentType = "audio/mp4",
                StoragePath = soundPath, DateCreated = DateTime.UtcNow };
            db.UploadFiles.AddRange(doc, sound);

            var id = Guid.NewGuid();
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = id, DeviceSessionId = Guid.NewGuid(), DeviceModel = "iPhone17,1", IsBundle = false,
                StartedAt = DateTime.UtcNow, SubmittedByAppUserId = Owner, DocumentUploadFileId = doc.Id,
                DocumentUploadFile = doc, PlaceId = placeId, ReadingCount = 2, MarkerCount = 0,
                PublishedAtUtc = published ? DateTime.UtcNow : null, MediaReviewState = media,
                LocationLabel = "North wall", DateCreated = DateTime.UtcNow,
            });
            db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
            {
                Id = Guid.NewGuid(), FieldSessionUploadId = id, UploadFileId = sound.Id,
                RelativePath = "media/audio-001.m4a", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner,
            });
            soundIds[id] = sound.Id;
            return id;
        }

        var approved = Session(mill, true, FeedMediaReviewState.Approved);
        var held = Session(mill, true, FeedMediaReviewState.Held);
        var unpublished = Session(mill, false, FeedMediaReviewState.Approved);
        var atAHome = Session(home, true, FeedMediaReviewState.Approved);
        await db.SaveChangesAsync();
        return new Seeded(factory, storage, approved, held, unpublished, atAHome, soundIds[approved]);
    }

    private static PublicFieldSessionController Build(Seeded s)
        => new(s.Factory, s.Storage, TestBundles.Store(s.Storage))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static FieldSessionTimelineService Timelines(Seeded s)
        => new(s.Storage, TestBundles.Store(s.Storage), NullLogger<FieldSessionTimelineService>.Instance);

    private static SessionTimeline Read(IActionResult result)
        => JsonSerializer.Deserialize<SessionTimeline>(Assert.IsType<FileContentResult>(result).FileContents,
                                                      new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public async Task A_visitor_gets_the_places_public_point_never_where_the_phone_was()
    {
        var seeded = await SeedAsync();
        var result = await Build(seeded).Timeline(seeded.Approved, Timelines(seeded));

        var json = Encoding.UTF8.GetString(Assert.IsType<FileContentResult>(result).FileContents);
        Assert.DoesNotContain("36.16274", json);
        Assert.DoesNotContain("36.16284", json);
        Assert.DoesNotContain("86.78159", json);

        var timeline = Read(result);
        Assert.Equal(TimelinePositionPolicy.PublicPoint, timeline.Positions);
        Assert.Empty(timeline.Track);
        Assert.Equal(2, timeline.Readings.T.Length);
    }

    [Fact]
    public async Task Approved_recordings_are_on_the_timeline_by_the_id_the_archive_serves()
    {
        var seeded = await SeedAsync();
        var timeline = Read(await Build(seeded).Timeline(seeded.Approved, Timelines(seeded)));
        var clip = Assert.Single(timeline.Clips);
        Assert.Equal(seeded.ApprovedSound, clip.FileId);
        Assert.Equal(0, clip.StartMs);
    }

    [Fact]
    public async Task While_media_is_held_the_readings_play_and_the_recordings_do_not()
    {
        var seeded = await SeedAsync();
        var timeline = Read(await Build(seeded).Timeline(seeded.Held, Timelines(seeded)));
        Assert.Empty(timeline.Clips);
        Assert.Equal(2, timeline.Readings.T.Length);

        var summary = Assert.IsType<PublicSessionSummary>(
            Assert.IsType<OkObjectResult>((await Build(seeded).Summary(seeded.Held)).Result).Value);
        Assert.Empty(summary.Files);
    }

    [Fact]
    public async Task Nothing_the_archive_would_not_list_can_be_played()
    {
        var seeded = await SeedAsync();
        var controller = Build(seeded);
        var timelines = Timelines(seeded);
        Assert.IsType<NotFoundResult>(await controller.Timeline(seeded.Unpublished, timelines));
        Assert.IsType<NotFoundResult>(await controller.Timeline(seeded.AtAHome, timelines));
        Assert.IsType<NotFoundResult>(await controller.Timeline(Guid.NewGuid(), timelines));
        Assert.IsType<NotFoundResult>((await controller.Summary(seeded.Unpublished)).Result);
        Assert.IsType<NotFoundResult>((await controller.Summary(seeded.AtAHome)).Result);
    }

    [Fact]
    public async Task The_summary_names_the_place_and_who_recorded_it()
    {
        var seeded = await SeedAsync();
        var summary = Assert.IsType<PublicSessionSummary>(
            Assert.IsType<OkObjectResult>((await Build(seeded).Summary(seeded.Approved)).Result).Value);
        Assert.Equal("The Old Mill", summary.PlaceName);
        Assert.Equal("Ada Recorder", summary.RecordedBy);
        Assert.Equal([seeded.ApprovedSound], summary.Files.Select(f => f.Id));
    }
}
