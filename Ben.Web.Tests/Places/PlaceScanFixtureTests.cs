using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Web.Tests.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// The phone's fixtures for a place's room scans, captured from the real controller as the API
/// serializes them (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// <para><b>Never invented.</b> A fixture written from reading a record shipped a broken app here
/// once: the mock agreed with the code because one person wrote both. These are the controller's
/// own answers to a fixed afternoon at one building, serialized with the web defaults MVC uses —
/// camelCase, enums as numbers, the server's own date shapes.</para>
///
/// <para><b>Checked by shape on every run.</b> The ids and times in a capture change each time it
/// is made, so what is compared is every field's path and kind. A field added, renamed, retyped or
/// dropped on the server fails here and names the file; the phone's <c>PlaceScanClientTests</c>
/// decode the same files. To re-capture after a deliberate change:</para>
/// <code>BEN_CAPTURE_FIXTURES=1 dotnet test Ben.Web.Tests --filter PlaceScanFixtureTests</code>
/// </remarks>
public sealed class PlaceScanFixtureTests : IDisposable
{
    private static readonly DateTime Afternoon = new(2026, 10, 28, 15, 30, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "place-scan-fixtures-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private static string FixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Ben.iOS", "BenKit", "Tests", "BenKitTests", "Fixtures", "place-scans");
    }

    [Fact]
    public async Task The_phones_fixtures_are_what_the_api_answers()
    {
        var captured = await CaptureAsync();
        var directory = FixtureDirectory();

        if (Environment.GetEnvironmentVariable("BEN_CAPTURE_FIXTURES") == "1")
        {
            foreach (var (name, json) in captured)
                await File.WriteAllTextAsync(Path.Combine(directory, name), json + "\n");
            return;
        }

        foreach (var (name, json) in captured)
        {
            var path = Path.Combine(directory, name);
            Assert.True(File.Exists(path), $"{name} has not been captured. Run with BEN_CAPTURE_FIXTURES=1.");

            var expected = Shape(JsonNode.Parse(await File.ReadAllTextAsync(path)));
            var actual = Shape(JsonNode.Parse(json));
            Assert.True(expected.SequenceEqual(actual),
                $"The API's answer for {name} no longer has the shape the phone's fixture has.\n"
                + $"Only in the fixture:\n  {string.Join("\n  ", expected.Except(actual))}\n"
                + $"Only in the answer:\n  {string.Join("\n  ", actual.Except(expected))}\n"
                + "If the change is deliberate, re-capture with BEN_CAPTURE_FIXTURES=1 and update the phone's records.");
        }
    }

    /// <summary>Every field's path and kind — what decoding depends on, without the values that change.</summary>
    private static List<string> Shape(JsonNode? node, string path = "$")
    {
        var lines = new List<string>();
        switch (node)
        {
            case null: lines.Add($"{path}: null"); break;
            case JsonObject obj:
                foreach (var (key, child) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    lines.AddRange(Shape(child, $"{path}.{key}"));
                break;
            case JsonArray array:
                if (array.Count == 0) lines.Add($"{path}: []");
                else lines.AddRange(Shape(array[0], $"{path}[]"));
                break;
            default:
                lines.Add($"{path}: {node.GetValueKind()}");
                break;
        }
        return lines;
    }

    /// <summary>One afternoon at the Hermitage Hotel, as each answer the phone reads.</summary>
    private async Task<List<(string Name, string Json)>> CaptureAsync()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var storage = new LocalFileStorageService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = _root }).Build());
        var clock = new Clock(Afternoon);

        Guid sarah = Guid.NewGuid(), dana = Guid.NewGuid(), harpeth = Guid.NewGuid(), place = Guid.NewGuid();
        await using (var db = await sqlite.NewContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = sarah, DisplayName = "Sarah Whitfield", DateCreated = Afternoon });
            db.AppUsers.Add(new AppUser { Id = dana, DisplayName = "Dana Reyes", DateCreated = Afternoon });
            db.Organizations.Add(new Organization
            {
                Id = harpeth, Name = "Hermitage Hotel", UrlName = "hermitage-hotel",
                DateCreated = Afternoon, CreatedByAppUserId = sarah,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = harpeth, AppUserId = sarah, Role = OrganizationMemberRole.Owner,
                IsActive = true, DateCreated = Afternoon, CreatedByAppUserId = sarah,
            });
            db.Places.Add(new Place
            {
                Id = place, Name = "The Hermitage Hotel", City = "Nashville", State = "TN",
                Kind = PlaceKind.PublicLocation, DateCreated = Afternoon, CreatedByAppUserId = sarah,
            });
            db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
            {
                Id = Guid.NewGuid(), OrganizationId = harpeth, PlaceId = place, VerifiedUtc = Afternoon,
                DateCreated = Afternoon, CreatedByAppUserId = sarah,
            });
            // The venue's own rooms: one it has scanned, one it has not.
            foreach (var (name, order) in new[] { ("Room 217", 1), ("The Ballroom", 2) })
                db.PlaceRooms.Add(new PlaceRoom
                {
                    Id = Guid.NewGuid(), OrganizationId = harpeth, PlaceId = place, Name = name, SortOrder = order,
                    IsActive = true, DateCreated = Afternoon, CreatedByAppUserId = sarah,
                });
            await db.SaveChangesAsync();
        }

        PlaceScanController As(Guid who) => new(
            sqlite.Factory, storage, TestBundles.Store(storage), new PlatformMessageService(sqlite.Factory),
            NullLogger<PlaceScanController>.Instance, clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, who.ToString())], "Bearer")),
                },
            },
        };

        string Json(object? value) => JsonSerializer.Serialize(value, Web);
        static object? Body(IConvertToActionResult result) => result.Convert() switch
        {
            ObjectResult o => o.Value,
            var other => throw new InvalidOperationException($"Expected a body, got {other.GetType().Name}."),
        };

        var captured = new List<(string, string)>();

        var hold = Body(await As(sarah).TakeHold(place, new TakeScanHoldRequest("Room 217"), default));
        captured.Add(("place-scan-hold.json", Json(hold)));

        var upload = (PlaceScanUploadResult)Body(await As(sarah).Upload(
            place, TestScans.Upload(TestScans.Members("Room 217", place, keyframes: 2, scannedAt: Afternoon.AddMinutes(-12))),
            "Room 217", "modelAndPictures", ((PlaceScanHoldRecord)hold!).HoldId, "baseline", default))!;
        captured.Add(("place-scan-upload.json", Json(upload)));

        clock.Now = Afternoon.AddMinutes(30);
        var conflict = Body(await As(dana).Upload(
            place, TestScans.Upload(TestScans.Members("Room 217", place, scannedAt: clock.Now)),
            "Room 217", "modelAndPictures", null, "baseline", default));
        captured.Add(("place-scan-conflict.json", Json(conflict)));

        var offered = Body(await As(dana).Upload(
            place, TestScans.Upload(TestScans.Members("Room 217", place, scannedAt: clock.Now)),
            "Room 217", "modelAndPictures", null, "offer", default));
        captured.Add(("place-scan-offered.json", Json(offered)));

        Body(await As(dana).TakeHold(place, new TakeScanHoldRequest("The Ballroom"), default));
        var held = Body(await As(sarah).TakeHold(place, new TakeScanHoldRequest("The Ballroom"), default));
        captured.Add(("place-scan-held.json", Json(held)));

        captured.Add(("place-scan-rooms.json", Json(Body(await As(sarah).List(place, default)))));
        captured.Add(("place-scan-record.json", Json(Body(await As(dana).Get(place, upload.Scan.Id, default)))));
        captured.Add(("place-scan-versions.json", Json(Body(await As(sarah).Versions(place, "room 217", default)))));
        return captured;
    }
}
