using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.FieldSessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Public field sessions: found near a point or by a place's name, and handed over as a sanitized
/// public <c>.ben</c> (Ben, 2026-09-27).
/// </summary>
/// <remarks>
/// The promise under test is the archive's: a visitor never gets the recorder's exact position.
/// The original bundle here carries a walked path at a precise point, and the tests look for that
/// point's digits anywhere in what comes out.
/// </remarks>
public sealed class PublicFieldSessionTests
{
    // Somewhere precise, with digits that cannot occur by accident in anything else.
    private const double TrueLatitude = 36.1627413;
    private const double TrueLongitude = -86.7815972;

    private static readonly Guid Owner = Guid.NewGuid();

    private static byte[] Document() => Encoding.UTF8.GetBytes($$"""
        {
          "format": "ishaunted.device-data", "version": 1,
          "session": { "location_label": "North wall" },
          "readings": [
            { "t": "2026-09-27T01:00:00Z", "position": { "latitude": {{TrueLatitude}}, "longitude": {{TrueLongitude}}, "accuracy_meters": 8 } },
            { "t": "2026-09-27T01:00:10Z", "position": { "latitude": {{TrueLatitude + 0.0001}}, "longitude": {{TrueLongitude}}, "accuracy_meters": 9 },
              "measurements": { "emf": { "value": 48.2 } } }
          ],
          "markers": [ { "kind": "manual", "latitude": {{TrueLatitude}}, "longitude": {{TrueLongitude}} } ]
        }
        """);

    private static readonly byte[] Photo = Encoding.ASCII.GetBytes("JPEG-BYTES-STRIPPED-BY-THE-PHONE");
    private static readonly byte[] Sound = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

    private static MemoryStream Original() => Services.BenBundleTests.StoredZip.Write(
        ("data.json", Document()),
        ("media/photo-001.jpg", Photo),
        ("media/audio-001.m4a", Sound),
        ("seal.json", Encoding.UTF8.GetBytes("""{"version":1,"device_id":"PHONE-VENDOR-ID"}""")));

    private static async Task<(BenBundle Bundle, MemoryStream Bytes)> PublicCopyAsync(
        bool includeRecordings, double? latitude = 36.15, double? longitude = -86.78)
    {
        var output = new MemoryStream();
        await PublicSessionBundle.WriteAsync(Original(), output, latitude, longitude, includeRecordings,
            recordedByAccountId: Owner, sessionId: Guid.Parse("7a3b1c52-0d1e-4f2a-9b8c-1234567890ab"),
            sealedAtUtc: new DateTime(2026, 9, 27, 3, 0, 0, DateTimeKind.Utc));
        output.Position = 0;
        return (await BenBundle.ReadAsync(output), output);
    }

    private static byte[] Member(MemoryStream file, BenBundleEntry entry)
    {
        var bytes = new byte[entry.Length];
        file.Position = entry.Offset;
        file.ReadExactly(bytes);
        return bytes;
    }

    // ── The public copy ──────────────────────────────────────────────────────

    [Fact]
    public async Task No_exact_position_survives_anywhere_in_the_public_copy()
    {
        var (_, bytes) = await PublicCopyAsync(includeRecordings: true);
        var text = Encoding.UTF8.GetString(bytes.ToArray());

        Assert.DoesNotContain("36.16274", text);
        Assert.DoesNotContain("86.78159", text);
        Assert.DoesNotContain("PHONE-VENDOR-ID", text);
    }

    [Fact]
    public async Task Every_position_becomes_the_public_point_with_an_honest_accuracy()
    {
        var (bundle, bytes) = await PublicCopyAsync(includeRecordings: false);
        var doc = JsonNode.Parse(Member(bytes, bundle.Document!))!;

        foreach (var reading in doc["readings"]!.AsArray())
        {
            Assert.Equal(36.15, reading!["position"]!["latitude"]!.GetValue<double>());
            Assert.Equal(-86.78, reading["position"]!["longitude"]!.GetValue<double>());
            Assert.Equal(PublicSessionBundle.PublicAccuracyMeters,
                         reading["position"]!["accuracy_meters"]!.GetValue<double>());
        }
        Assert.Equal(36.15, doc["markers"]![0]!["latitude"]!.GetValue<double>());
        // The evidence itself is untouched.
        Assert.Equal(48.2, doc["readings"]![1]!["measurements"]!["emf"]!["value"]!.GetValue<double>());
        Assert.Equal("North wall", doc["session"]!["location_label"]!.GetValue<string>());
    }

    [Fact]
    public async Task With_no_public_point_positions_are_removed_rather_than_guessed()
    {
        var (bundle, bytes) = await PublicCopyAsync(includeRecordings: false, latitude: null, longitude: null);
        var doc = JsonNode.Parse(Member(bytes, bundle.Document!))!;
        var position = doc["readings"]![0]!["position"]!.AsObject();
        Assert.False(position.ContainsKey("latitude"));
        Assert.False(position.ContainsKey("longitude"));
        Assert.False(position.ContainsKey("accuracy_meters"));
    }

    [Fact]
    public async Task Held_media_is_left_out_and_approved_media_is_copied_byte_for_byte()
    {
        var (held, _) = await PublicCopyAsync(includeRecordings: false);
        Assert.Equal(["data.json", "seal.json"], held.Entries.Keys.Order().ToArray());

        var (approved, bytes) = await PublicCopyAsync(includeRecordings: true);
        Assert.Equal(Photo, Member(bytes, approved.Find("media/photo-001.jpg")!));
        Assert.Equal(Sound, Member(bytes, approved.Find("media/audio-001.m4a")!));
    }

    [Fact]
    public async Task The_new_seal_verifies_the_way_the_phone_verifies_it()
    {
        var (bundle, bytes) = await PublicCopyAsync(includeRecordings: true);
        var seal = JsonNode.Parse(Member(bytes, bundle.Seal!))!;

        Assert.True(seal["public_archive_copy"]!.GetValue<bool>());
        Assert.Null(seal["device_id"]);
        Assert.Equal(Owner.ToString().ToUpperInvariant(), seal["recorded_by_account_id"]!.GetValue<string>());
        Assert.Equal("7A3B1C52-0D1E-4F2A-9B8C-1234567890AB", seal["session_id"]!.GetValue<string>());

        // Recompute every entry from the bytes actually in the file.
        var listed = seal["entries"]!.AsArray().Select(e => e!["path"]!.GetValue<string>()).Order().ToArray();
        var members = bundle.Entries.Keys.Where(k => k != "seal.json").Order().ToArray();
        Assert.Equal(members, listed);
        var recomputed = members.Select(path =>
        {
            var content = Member(bytes, bundle.Entries[path]);
            return (path, Convert.ToHexStringLower(SHA256.HashData(content)), (long)content.Length);
        }).ToList();
        Assert.Equal(seal["digest"]!.GetValue<string>(), PublicSessionBundle.Digest(recomputed));

        // And the digest is the phone's algorithm, not merely self-consistent: one line per entry.
        var lines = string.Concat(recomputed.OrderBy(r => r.path, StringComparer.Ordinal)
            .Select(r => $"{r.path}\n{r.Item2}\n{r.Item3}\n"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines))),
                     seal["digest"]!.GetValue<string>());
    }

    // ── The lookups and the door ─────────────────────────────────────────────

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
                                 Guid Published, Guid Unpublished, Guid AtAHome, Guid FarAway, Guid Held);

    private static async Task<Seeded> SeedAsync()
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new Storage();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = Owner, UserName = "o@t", Email = "o@t", DisplayName = "Ada Recorder" });

        Guid Place(string name, string city, PlaceKind kind, decimal lat, decimal lon)
        {
            var id = Guid.NewGuid();
            db.Places.Add(new Place { Id = id, Name = name, City = city, State = "TN", Kind = kind,
                Latitude = lat, Longitude = lon, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Owner });
            return id;
        }
        var mill = Place("The Old Mill", "Nashville", PlaceKind.PublicLocation, 36.1627413m, -86.7815972m);
        var home = Place("The Smith House", "Nashville", PlaceKind.PrivateResidence, 36.1630m, -86.7810m);
        var far = Place("Waverly Hills", "Louisville", PlaceKind.PublicLocation, 38.1300m, -85.8400m);

        Guid Session(Guid placeId, bool published, FeedMediaReviewState media, DateTime started)
        {
            var path = $"orgs/x/{Guid.NewGuid():N}.ben";
            storage.Files[path] = Original().ToArray();
            var file = new UploadFile { Id = Guid.NewGuid(), FileName = "s.ben",
                ContentType = BenBundle.ContentType, StoragePath = path, DateCreated = DateTime.UtcNow };
            var id = Guid.NewGuid();
            db.UploadFiles.Add(file);
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = id, DeviceSessionId = Guid.NewGuid(), DeviceModel = "iPhone17,1", IsBundle = true,
                StartedAt = started, SubmittedByAppUserId = Owner, DocumentUploadFileId = file.Id,
                DocumentUploadFile = file, PlaceId = placeId, ReadingCount = 2, MarkerCount = 1,
                PublishedAtUtc = published ? DateTime.UtcNow : null, MediaReviewState = media,
                DateCreated = DateTime.UtcNow,
            });
            return id;
        }
        var published = Session(mill, true, FeedMediaReviewState.Approved, DateTime.UtcNow.AddDays(-1));
        var unpublished = Session(mill, false, FeedMediaReviewState.Approved, DateTime.UtcNow);
        var atAHome = Session(home, true, FeedMediaReviewState.Approved, DateTime.UtcNow);
        var farAway = Session(far, true, FeedMediaReviewState.Approved, DateTime.UtcNow);
        var held = Session(mill, true, FeedMediaReviewState.Held, DateTime.UtcNow.AddDays(-2));
        await db.SaveChangesAsync();
        return new Seeded(factory, storage, published, unpublished, atAHome, farAway, held);
    }

    private static PublicFieldSessionController Build(Seeded s)
        => new(s.Factory, s.Storage, TestBundles.Store(s.Storage));

    private static IReadOnlyList<PublicArchiveSessionRow> Rows(ActionResult<IReadOnlyList<PublicArchiveSessionRow>> r)
        => Assert.IsAssignableFrom<IReadOnlyList<PublicArchiveSessionRow>>(Assert.IsType<OkObjectResult>(r.Result).Value);

    [Fact]
    public async Task Nearby_lists_only_what_the_archive_would_show_nearest_first()
    {
        var seeded = await SeedAsync();
        var rows = Rows(await Build(seeded).Nearby(36.16, -86.78, 10));

        Assert.Equal([seeded.Published, seeded.Held], rows.Select(r => r.Id).ToArray());
        Assert.All(rows, r => Assert.Equal("The Old Mill", r.PlaceName));
        // The held session is listed — its readings are public — but offers no media.
        Assert.Equal(0, rows.Single(r => r.Id == seeded.Held).MediaCount);
        // The point handed out is the public one, never the stored one.
        Assert.All(rows, r => Assert.NotEqual(36.1627413, r.PublicLatitude));
    }

    [Fact]
    public async Task A_tiny_radius_cannot_narrow_down_where_a_place_really_is()
    {
        var seeded = await SeedAsync();
        // Standing on the true point with a hundred-yard circle: the lookup still answers for the
        // whole public cell, not the exact spot.
        var rows = Rows(await Build(seeded).Nearby(36.1627413, -86.7815972, 0.05));
        Assert.Contains(rows, r => r.Id == seeded.Published);
    }

    [Fact]
    public async Task Search_finds_places_by_name_or_town_and_never_a_private_home()
    {
        var seeded = await SeedAsync();
        var controller = Build(seeded);

        Assert.Equal([seeded.FarAway], Rows(await controller.Search("louisville")).Select(r => r.Id).ToArray());
        Assert.Equal([seeded.FarAway], Rows(await controller.Search("waverly hills")).Select(r => r.Id).ToArray());
        var nashville = Rows(await controller.Search("nashville")).Select(r => r.Id).ToHashSet();
        Assert.Contains(seeded.Published, nashville);
        Assert.DoesNotContain(seeded.AtAHome, nashville);
        Assert.DoesNotContain(seeded.Unpublished, nashville);
        Assert.IsType<BadRequestObjectResult>((await controller.Search("n")).Result);
    }

    [Fact]
    public async Task The_door_hands_out_the_public_copy_and_nothing_else()
    {
        var seeded = await SeedAsync();
        var controller = Build(seeded);

        Assert.IsType<NotFoundResult>(await controller.Bundle(seeded.Unpublished));
        Assert.IsType<NotFoundResult>(await controller.Bundle(seeded.AtAHome));
        Assert.IsType<NotFoundResult>(await controller.Bundle(Guid.NewGuid()));

        var ok = Assert.IsType<FileStreamResult>(await controller.Bundle(seeded.Published));
        using var copy = new MemoryStream();
        await ok.FileStream.CopyToAsync(copy);
        Assert.DoesNotContain("36.16274", Encoding.UTF8.GetString(copy.ToArray()));
        copy.Position = 0;
        var bundle = await BenBundle.ReadAsync(copy);
        Assert.NotNull(bundle.Find("media/photo-001.jpg"));

        // Held media: the readings go, the recordings do not.
        var held = Assert.IsType<FileStreamResult>(await controller.Bundle(seeded.Held));
        using var heldCopy = new MemoryStream();
        await held.FileStream.CopyToAsync(heldCopy);
        heldCopy.Position = 0;
        Assert.Null((await BenBundle.ReadAsync(heldCopy)).Find("media/photo-001.jpg"));
    }

    [Fact]
    public async Task The_public_copy_is_built_once_per_state_of_the_session()
    {
        var seeded = await SeedAsync();
        var controller = Build(seeded);
        await controller.Bundle(seeded.Published);
        await controller.Bundle(seeded.Published);
        Assert.Single(seeded.Storage.Files.Keys, k => k.StartsWith("public-bundles/", StringComparison.Ordinal)
                                                     && k.Contains(seeded.Published.ToString("N")));
    }
}
