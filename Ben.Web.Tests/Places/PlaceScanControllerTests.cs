using System.Security.Claims;
using System.Security.Cryptography;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Ben.Web.Tests.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// A place's room scans: one current scan per room, the three doors to replacing it, holds, the
/// private-home rule, who pays, and taking one down (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// <para>On SQLite, not the in-memory provider: "one current scan per room" is a filtered unique
/// index and replacing a scan is two saves in a transaction, and neither exists in memory.</para>
///
/// <para>The people: Sarah and James are in Harpeth Paranormal (Sarah its owner); Dana is in
/// Cumberland Seekers; Wren belongs to no group.</para>
/// </remarks>
public sealed class PlaceScanControllerTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "place-scans-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed record World(
        SqliteTestDb Db, IFileStorageService Storage, Clock Clock,
        Guid PublicPlace, Guid PrivateHome,
        Guid Harpeth, Guid Cumberland,
        Guid Sarah, Guid James, Guid Dana, Guid Wren) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private async Task<World> BuildAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        var storage = new LocalFileStorageService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:RootPath"] = _root })
            .Build());

        Guid sarah = Guid.NewGuid(), james = Guid.NewGuid(), dana = Guid.NewGuid(), wren = Guid.NewGuid();
        Guid harpeth = Guid.NewGuid(), cumberland = Guid.NewGuid();
        Guid publicPlace = Guid.NewGuid(), privateHome = Guid.NewGuid();

        await using var db = await sqlite.NewContextAsync();
        foreach (var (id, name) in new[] { (sarah, "Sarah Whitfield"), (james, "James Thornton"), (dana, "Dana Reyes"), (wren, "Wren Ashby") })
            db.AppUsers.Add(new AppUser { Id = id, DisplayName = name, Email = $"{id:N}@example.com", DateCreated = Start });

        void Group(Guid id, string name, params (Guid Person, OrganizationMemberRole Role)[] members)
        {
            db.Organizations.Add(new Organization
            {
                Id = id, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'),
                DateCreated = Start, CreatedByAppUserId = members[0].Person,
            });
            foreach (var (person, role) in members)
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = id, AppUserId = person, Role = role, IsActive = true,
                    DateCreated = Start, CreatedByAppUserId = person,
                });
        }
        Group(harpeth, "Harpeth Paranormal", (sarah, OrganizationMemberRole.Owner), (james, OrganizationMemberRole.Member));
        Group(cumberland, "Cumberland Seekers", (dana, OrganizationMemberRole.Owner));

        db.Places.Add(new Place
        {
            Id = publicPlace, Name = "The Hermitage Hotel", City = "Nashville", State = "TN",
            Kind = PlaceKind.PublicLocation, DateCreated = Start, CreatedByAppUserId = sarah,
        });
        db.Places.Add(new Place
        {
            Id = privateHome, Name = "The Cole House", City = "Franklin", State = "TN",
            Kind = PlaceKind.PrivateResidence, DateCreated = Start, CreatedByAppUserId = sarah,
        });
        // Harpeth works at the Cole House: it has named a room there, which puts it among the
        // groups that know the place.
        db.PlaceRooms.Add(new PlaceRoom
        {
            Id = Guid.NewGuid(), OrganizationId = harpeth, PlaceId = privateHome, Name = "Nursery",
            DateCreated = Start, CreatedByAppUserId = sarah,
        });
        await db.SaveChangesAsync();

        return new World(sqlite, storage, new Clock(Start), publicPlace, privateHome, harpeth, cumberland, sarah, james, dana, wren);
    }

    private static PlaceScanController Controller(World w, Guid acting)
    {
        var audit = new Mock<IAuditLogService>();
        var services = new ServiceCollection().AddSingleton(audit.Object).BuildServiceProvider();
        return new PlaceScanController(
            w.Db.Factory, w.Storage, TestBundles.Store(w.Storage), new PlatformMessageService(w.Db.Factory),
            NullLogger<PlaceScanController>.Instance, w.Clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, acting.ToString())], "Bearer")),
                },
            },
        };
    }

    private static T Ok<T>(ActionResult<T> result)
        => Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static async Task<ActionResult<PlaceScanUploadResult>> UploadAsync(
        World w, Guid who, Guid placeId, string room = "Bedroom 1", string intent = "baseline",
        DateTime? scannedAt = null, int meshBytes = 64)
        => await Controller(w, who).Upload(
            placeId, TestScans.Upload(TestScans.Members(room, placeId, scannedAt: scannedAt ?? w.Clock.Now, meshBytes: meshBytes)),
            room, "modelAndPictures", holdId: null, intent, default);

    private static async Task<PlaceScan> RowAsync(World w, Guid scanId)
    {
        await using var db = await w.Db.NewContextAsync();
        return await db.PlaceScans.AsNoTracking().SingleAsync(s => s.Id == scanId);
    }

    private static async Task<int> MessagesToAsync(World w, Guid person)
    {
        await using var db = await w.Db.NewContextAsync();
        return await db.UserMessageTos.CountAsync(m => m.ToAppUserId == person);
    }

    private IEnumerable<string> StoredFiles()
        => Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*.scan", SearchOption.AllDirectories) : [];

    // ── the first scan ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_first_scan_of_a_room_becomes_its_scan()
    {
        await using var w = await BuildAsync();

        var sent = TestScans.Members("Bedroom 1", w.PublicPlace, scannedAt: Start);
        var bytes = TestScans.File(sent).ToArray();
        var result = Ok(await Controller(w, w.Sarah).Upload(
            w.PublicPlace, TestScans.Upload(sent), " bedroom  1 ", "modelAndPictures", null, null, default));

        Assert.Equal("current", result.Outcome);
        Assert.Equal(PlaceScanState.Current, result.Scan.State);
        Assert.Equal(1, result.Scan.Version);
        Assert.Equal("bedroom 1", result.Scan.RoomKey);
        // The name as typed in the manifest, not as the form spelled it.
        Assert.Equal("Bedroom 1", result.Scan.RoomName);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Scan.Sha256);
        Assert.Equal(2, result.Scan.KeyframeCount);
        Assert.Equal(18.4, result.Scan.FloorAreaM2);
        Assert.Contains(result.Scan.Entries, e => e is { Path: "mesh.glb", ContentType: "model/gltf-binary" });

        var row = await RowAsync(w, result.Scan.Id);
        Assert.StartsWith($"places/{w.PublicPlace}/scans/", row.StoragePath);
        Assert.Equal(w.Sarah, row.OwnerAppUserId);
        Assert.Null(row.OwnerOrganizationId);

        var list = Ok(await Controller(w, w.James).List(w.PublicPlace, default));
        var room = Assert.Single(list.Rooms);
        Assert.Equal(result.Scan.Id, room.Current!.Id);
        Assert.False(room.CanScan);
        Assert.True(room.CanCompare);
        Assert.Equal("Sarah Whitfield", room.Current.ScannedBy);
    }

    [Fact]
    public async Task The_mesh_and_a_keyframe_are_served_from_inside_the_file()
    {
        await using var w = await BuildAsync();
        var scan = Ok(await UploadAsync(w, w.Sarah, w.PublicPlace)).Scan;

        var mesh = Assert.IsType<FileStreamResult>(await Controller(w, w.Dana).Entry(w.PublicPlace, scan.Id, "mesh.glb", default));
        Assert.Equal("model/gltf-binary", mesh.ContentType);
        Assert.True(mesh.EnableRangeProcessing);
        using (var reader = new MemoryStream())
        {
            await mesh.FileStream.CopyToAsync(reader);
            Assert.Equal(TestScans.Mesh(), reader.ToArray());
        }

        Assert.IsType<FileStreamResult>(await Controller(w, w.Dana).Entry(w.PublicPlace, scan.Id, "keyframes/0002.jpg", default));
        Assert.IsType<NotFoundResult>(await Controller(w, w.Dana).Entry(w.PublicPlace, scan.Id, "keyframes/0009.jpg", default));
        Assert.IsType<NotFoundResult>(await Controller(w, w.Dana).Entry(w.PublicPlace, scan.Id, "../manifest.json", default));

        var file = Assert.IsType<FileStreamResult>(await Controller(w, w.Dana).Download(w.PublicPlace, scan.Id, default));
        Assert.Equal("bedroom-1-v1.scan", file.FileDownloadName);
    }

    // ── somebody else's scan of the same room ────────────────────────────────

    [Fact]
    public async Task A_second_scan_by_a_stranger_is_answered_with_the_three_choices()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        var refused = Assert.IsType<ConflictObjectResult>((await UploadAsync(w, w.Dana, w.PublicPlace)).Result);
        var conflict = Assert.IsType<PlaceScanConflictRecord>(refused.Value);

        Assert.Equal("already scanned", conflict.Reason);
        Assert.Equal(["compare", "offer", "discard"], conflict.Choices);
        Assert.False(conflict.MayReplace);
        Assert.Contains("Sarah Whitfield", conflict.ReplaceReason);
        Assert.Contains("From 10/28/2027, anyone may replace it.", conflict.ReplaceReason);
        // The phone still holds the file; the server keeps nothing it refused.
        Assert.Single(StoredFiles());
    }

    [Fact]
    public async Task An_offered_scan_waits_for_the_custodian_and_nobody_else_sees_it()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        var offered = Ok(await UploadAsync(w, w.Dana, w.PublicPlace, intent: "offer"));
        Assert.Equal("offered", offered.Outcome);
        Assert.Equal(PlaceScanState.Offered, offered.Scan.State);
        Assert.Equal(2, offered.Scan.Version);
        Assert.Contains("offered to Sarah Whitfield", offered.Says);

        // At a public location with no venue, whoever made the room's scan looks after it.
        Assert.Equal(1, await MessagesToAsync(w, w.Sarah));
        Assert.Equal(1, Assert.Single(Ok(await Controller(w, w.Sarah).List(w.PublicPlace, default)).Rooms).OffersWaiting);

        // Nobody else sees it, not even somebody in the custodian's own group.
        Assert.IsType<NotFoundResult>((await Controller(w, w.James).Get(w.PublicPlace, offered.Scan.Id, default)).Result);
        Assert.Equal(0, Assert.Single(Ok(await Controller(w, w.James).List(w.PublicPlace, default)).Rooms).OffersWaiting);
        Assert.DoesNotContain(Ok(await Controller(w, w.James).Versions(w.PublicPlace, "bedroom 1", default)),
            v => v.State == PlaceScanState.Offered);
        // Its scanner still can.
        Assert.IsType<OkObjectResult>((await Controller(w, w.Dana).Get(w.PublicPlace, offered.Scan.Id, default)).Result);
    }

    [Fact]
    public async Task Accepting_an_offer_flips_the_versions()
    {
        await using var w = await BuildAsync();
        var first = Ok(await UploadAsync(w, w.Sarah, w.PublicPlace)).Scan;
        var offered = Ok(await UploadAsync(w, w.Dana, w.PublicPlace, intent: "offer")).Scan;

        // Only the custodian decides.
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>((await Controller(w, w.Dana).Accept(w.PublicPlace, offered.Id, new("mine"), default)).Result).StatusCode);

        var accepted = Ok(await Controller(w, w.Sarah).Accept(w.PublicPlace, offered.Id, new("Better light than mine."), default));

        Assert.Equal(PlaceScanState.Current, accepted.State);
        Assert.Equal(PlaceScanState.Previous, (await RowAsync(w, first.Id)).State);
        Assert.Equal("Better light than mine.", (await RowAsync(w, offered.Id)).DecisionNote);
        Assert.Equal(1, await MessagesToAsync(w, w.Dana));

        var versions = Ok(await Controller(w, w.James).Versions(w.PublicPlace, "Bedroom 1", default));
        Assert.Equal([2, 1], versions.Select(v => v.Version));
        Assert.Equal([PlaceScanState.Current, PlaceScanState.Previous], versions.Select(v => v.State));
    }

    [Fact]
    public async Task A_declined_offer_loses_its_file_and_the_room_keeps_its_scan()
    {
        await using var w = await BuildAsync();
        var first = Ok(await UploadAsync(w, w.Sarah, w.PublicPlace)).Scan;
        var offered = Ok(await UploadAsync(w, w.Dana, w.PublicPlace, intent: "offer")).Scan;
        Assert.Equal(2, StoredFiles().Count());

        var declined = Ok(await Controller(w, w.Sarah).Decline(w.PublicPlace, offered.Id, new("Half the room is missing."), default));

        Assert.Equal(PlaceScanState.Declined, declined.State);
        Assert.Equal(PlaceScanState.Current, (await RowAsync(w, first.Id)).State);
        Assert.Single(StoredFiles());
        Assert.Equal(1, await MessagesToAsync(w, w.Dana));
    }

    // ── a year on ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_scan_older_than_a_year_may_be_replaced_by_anybody_who_may_scan()
    {
        await using var w = await BuildAsync();
        var first = Ok(await UploadAsync(w, w.Sarah, w.PublicPlace)).Scan;

        w.Clock.Now = Start.AddDays(366);
        var second = Ok(await UploadAsync(w, w.Dana, w.PublicPlace));

        Assert.Equal("replaced", second.Outcome);
        Assert.Equal(PlaceScanState.Current, second.Scan.State);
        Assert.Equal(PlaceScanState.Previous, (await RowAsync(w, first.Id)).State);
        Assert.Contains("is kept as version 1", second.Says);
    }

    [Fact]
    public async Task A_scan_under_a_year_old_is_not_replaced_by_a_stranger_and_says_when_it_can_be()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        w.Clock.Now = Start.AddDays(364);
        var refused = Assert.IsType<ConflictObjectResult>((await UploadAsync(w, w.Dana, w.PublicPlace, intent: "replace")).Result);
        var conflict = Assert.IsType<PlaceScanConflictRecord>(refused.Value);

        Assert.Equal(
            "Bedroom 1 was scanned on 10/28/2026 by Sarah Whitfield. Sarah Whitfield decides whether a new scan "
            + "replaces it, so yours can be offered to them or kept as a comparison. From 10/28/2027, anyone may replace it.",
            conflict.ReplaceReason);
    }

    [Fact]
    public async Task Whoever_made_a_scan_may_make_it_again()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Dana, w.PublicPlace));

        var again = Ok(await UploadAsync(w, w.Dana, w.PublicPlace, intent: "replace"));
        Assert.Equal("replaced", again.Outcome);
    }

    // ── holds ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_hold_names_its_holder_to_the_next_person()
    {
        await using var w = await BuildAsync();
        var hold = Ok(await Controller(w, w.Sarah).TakeHold(w.PublicPlace, new("Bedroom 1"), default));
        Assert.True(hold.IsYou);
        Assert.NotNull(hold.HoldId);
        Assert.Equal(Start.AddMinutes(20), hold.Until);

        var refused = Assert.IsType<ConflictObjectResult>((await Controller(w, w.Dana).TakeHold(w.PublicPlace, new(" bedroom 1"), default)).Result);
        var held = Assert.IsType<PlaceScanHeldRecord>(refused.Value);
        Assert.Equal("Sarah Whitfield", held.HeldBy);
        Assert.StartsWith("Sarah Whitfield is scanning Bedroom 1 right now.", held.Says);

        var room = Assert.Single(Ok(await Controller(w, w.Dana).List(w.PublicPlace, default)).Rooms);
        Assert.False(room.CanScan);
        Assert.Equal("Sarah Whitfield", room.HoldBy!.HeldBy);
        Assert.False(room.HoldBy.IsYou);
        Assert.Null(room.HoldBy.HoldId);
    }

    [Fact]
    public async Task An_expired_hold_is_ignored_and_your_own_hold_is_extended()
    {
        await using var w = await BuildAsync();
        Ok(await Controller(w, w.Sarah).TakeHold(w.PublicPlace, new("Bedroom 1"), default));

        w.Clock.Now = Start.AddMinutes(10);
        var extended = Ok(await Controller(w, w.Sarah).TakeHold(w.PublicPlace, new("Bedroom 1"), default));
        Assert.Equal(Start.AddMinutes(30), extended.Until);

        w.Clock.Now = Start.AddMinutes(31);
        Assert.True(Ok(await Controller(w, w.Dana).TakeHold(w.PublicPlace, new("Bedroom 1"), default)).IsYou);
    }

    [Fact]
    public async Task A_room_with_a_scan_is_not_held_for_a_stranger()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        var refused = Assert.IsType<ConflictObjectResult>((await Controller(w, w.Dana).TakeHold(w.PublicPlace, new("Bedroom 1"), default)).Result);
        Assert.Equal("already scanned", Assert.IsType<PlaceScanConflictRecord>(refused.Value).Reason);
    }

    [Fact]
    public async Task Uploading_lets_your_hold_go_and_the_sweep_clears_the_rest()
    {
        await using var w = await BuildAsync();
        Ok(await Controller(w, w.Sarah).TakeHold(w.PublicPlace, new("Bedroom 1"), default));
        Ok(await Controller(w, w.Dana).TakeHold(w.PublicPlace, new("Kitchen"), default));
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        await using (var db = await w.Db.NewContextAsync())
            Assert.Equal("kitchen", Assert.Single(db.PlaceScanHolds).RoomKey);

        w.Clock.Now = Start.AddHours(1);
        await new PlaceScanHoldSweepJob(w.Db.Factory, NullLogger<PlaceScanHoldSweepJob>.Instance, w.Clock).RunAsync(default);

        await using (var db = await w.Db.NewContextAsync())
            Assert.Empty(db.PlaceScanHolds);
    }

    // ── somebody's home ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_private_home_is_closed_to_a_group_not_working_there()
    {
        await using var w = await BuildAsync();

        Assert.IsType<NotFoundResult>((await Controller(w, w.Dana).List(w.PrivateHome, default)).Result);

        // Not even a 403: whether a stranger's home has scans is not for probing.
        Assert.IsType<NotFoundResult>((await UploadAsync(w, w.Dana, w.PrivateHome, room: "Nursery")).Result);
        Assert.IsType<NotFoundResult>((await Controller(w, w.Dana).TakeHold(w.PrivateHome, new("Nursery"), default)).Result);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task A_group_working_at_a_private_home_scans_it_and_looks_after_the_scan()
    {
        await using var w = await BuildAsync();

        var scan = Ok(await UploadAsync(w, w.James, w.PrivateHome, room: "Nursery")).Scan;
        var row = await RowAsync(w, scan.Id);
        Assert.Equal(w.Harpeth, row.ScannedForOrganizationId);

        // The venue's room of the same name is listed once, with the scan on it.
        var room = Assert.Single(Ok(await Controller(w, w.Sarah).List(w.PrivateHome, default)).Rooms);
        Assert.Equal(scan.Id, room.Current!.Id);
        // Sarah answers for Harpeth, the group that made the scan, so she may replace it any time.
        Assert.True(room.CanReplace);
    }

    [Fact]
    public async Task A_viewer_in_the_group_may_look_but_not_scan()
    {
        await using var w = await BuildAsync();
        await using (var db = await w.Db.NewContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = w.Harpeth, AppUserId = w.Wren,
                Role = OrganizationMemberRole.Viewer, IsActive = true, DateCreated = Start, CreatedByAppUserId = w.Sarah,
            });
            await db.SaveChangesAsync();
        }

        var list = Ok(await Controller(w, w.Wren).List(w.PrivateHome, default));
        Assert.False(list.MayScan);
        Assert.Contains("viewer", list.WhyCannotScan);

        var refused = Assert.IsType<ObjectResult>((await UploadAsync(w, w.Wren, w.PrivateHome, room: "Nursery")).Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
    }

    // ── who pays ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_verified_venue_pays_for_every_scan_of_its_building()
    {
        await using var w = await BuildAsync();
        await VerifyVenueAsync(w, w.PublicPlace, w.Harpeth);

        var scan = Ok(await UploadAsync(w, w.Dana, w.PublicPlace)).Scan;
        var row = await RowAsync(w, scan.Id);

        Assert.Null(row.OwnerAppUserId);
        Assert.Equal(w.Harpeth, row.OwnerOrganizationId);
        Assert.Null(Ok(await Controller(w, w.Dana).List(w.PublicPlace, default)).StorageLeftBytes);
        await using var db = await w.Db.NewContextAsync();
        Assert.Equal(0, await AccountStorageGuard.UsedBytesAsync(db, w.Dana, default));
    }

    [Fact]
    public async Task Without_a_venue_the_scanner_pays_and_the_cap_refuses_past_it()
    {
        await using var w = await BuildAsync();
        var scan = Ok(await UploadAsync(w, w.Wren, w.PublicPlace)).Scan;

        await using (var db = await w.Db.NewContextAsync())
        {
            Assert.Equal(scan.FileSize, await AccountStorageGuard.UsedBytesAsync(db, w.Wren, default));
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.FreeAccountStorageMegabytes, Value = "1",
                DateCreated = Start, CreatedByAppUserId = w.Sarah,
            });
            await db.SaveChangesAsync();
        }

        var list = Ok(await Controller(w, w.Wren).List(w.PublicPlace, default));
        Assert.Equal(1024 * 1024 - scan.FileSize, list.StorageLeftBytes);

        var refused = Assert.IsType<ObjectResult>((await UploadAsync(w, w.Wren, w.PublicPlace, room: "Kitchen", meshBytes: 1024 * 1024)).Result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, refused.StatusCode);
        Assert.Contains("a free account can store", (string)refused.Value!);
        Assert.Single(StoredFiles());
    }

    // ── the venue and an administrator's grant ───────────────────────────────

    [Fact]
    public async Task The_venue_looks_after_the_scans_and_its_administrators_decide()
    {
        await using var w = await BuildAsync();
        await VerifyVenueAsync(w, w.PublicPlace, w.Harpeth);
        Ok(await UploadAsync(w, w.Dana, w.PublicPlace));

        // Sarah owns the venue's group: she may replace Dana's scan the same evening.
        Assert.Equal("replaced", Ok(await UploadAsync(w, w.Sarah, w.PublicPlace, intent: "replace")).Outcome);

        // James is an ordinary member of it: his scan becomes an offer to Sarah.
        Assert.IsType<ConflictObjectResult>((await UploadAsync(w, w.James, w.PublicPlace)).Result);
        var offered = Ok(await UploadAsync(w, w.James, w.PublicPlace, intent: "offer"));
        Assert.Contains("offered to Harpeth Paranormal", offered.Says);

        var list = Ok(await Controller(w, w.Sarah).List(w.PublicPlace, default));
        Assert.Equal(new PlaceScanCustodianRecord("venue", "Harpeth Paranormal", true), list.Custodian);
    }

    [Fact]
    public async Task An_administrator_may_hand_a_places_scans_to_a_group_until_a_venue_takes_over()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        var admin = AdminController(w, w.Wren);
        var granted = Ok(await admin.Grant(w.PublicPlace, new GrantScanCustodyRequest(w.Cumberland, "They run the tours."), default));
        Assert.Equal("Cumberland Seekers", granted.OrganizationName);
        Assert.Equal(1, await MessagesToAsync(w, w.Dana));

        // Dana now answers for the scans, so Sarah's scan is hers to replace.
        Assert.Equal("replaced", Ok(await UploadAsync(w, w.Dana, w.PublicPlace, intent: "replace")).Outcome);

        // A venue proved later takes over, and the grant closes itself.
        await VerifyVenueAsync(w, w.PublicPlace, w.Harpeth, throughTheClaim: true);
        await using (var db = await w.Db.NewContextAsync())
        {
            var grant = await db.PlaceScanCustodies.SingleAsync();
            Assert.Equal(PlaceScanCustody.ClosedByVenue, grant.ClosedReason);
        }

        // And while a venue holds custody, no administrator can give it away.
        var refused = Assert.IsType<ConflictObjectResult>((await admin.Grant(w.PublicPlace, new(w.Cumberland, null), default)).Result);
        Assert.Equal("Harpeth Paranormal is this place's confirmed venue, so it holds custody.", refused.Value);
    }

    // ── taking one down ──────────────────────────────────────────────────────

    [Fact]
    public async Task Removing_a_scan_takes_its_file_and_keeps_its_row()
    {
        await using var w = await BuildAsync();
        var scan = Ok(await UploadAsync(w, w.Sarah, w.PublicPlace)).Scan;

        var refused = Assert.IsType<ObjectResult>(await Controller(w, w.Dana).Remove(w.PublicPlace, scan.Id, default));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);

        Assert.IsType<NoContentResult>(await Controller(w, w.Sarah).Remove(w.PublicPlace, scan.Id, default));

        // The row stays, so a comparison made against it still says what it was compared with.
        Assert.Equal(PlaceScanState.Removed, (await RowAsync(w, scan.Id)).State);
        Assert.Empty(StoredFiles());
        Assert.IsType<NotFoundResult>((await Controller(w, w.Sarah).Get(w.PublicPlace, scan.Id, default)).Result);
        // And the room is free to scan again.
        Assert.Empty(Ok(await Controller(w, w.Dana).List(w.PublicPlace, default)).Rooms);
    }

    // ── what the upload must agree with ──────────────────────────────────────

    [Fact]
    public async Task A_scan_sent_as_another_room_is_refused()
    {
        await using var w = await BuildAsync();
        var refused = await Controller(w, w.Sarah).Upload(
            w.PublicPlace, TestScans.Upload(TestScans.Members("Kitchen", w.PublicPlace)), "Bedroom 1",
            "modelAndPictures", null, null, default);

        Assert.Equal("That scan is of Kitchen, but it was sent as Bedroom 1.",
            Assert.IsType<BadRequestObjectResult>(refused.Result).Value);
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task A_scan_made_at_another_place_is_refused()
    {
        await using var w = await BuildAsync();
        var refused = await Controller(w, w.Sarah).Upload(
            w.PublicPlace, TestScans.Upload(TestScans.Members("Bedroom 1", w.PrivateHome)), "Bedroom 1",
            "modelAndPictures", null, null, default);

        Assert.Equal("That scan was made at a different place.",
            Assert.IsType<BadRequestObjectResult>(refused.Result).Value);
    }

    [Fact]
    public async Task The_database_refuses_a_second_current_scan_of_a_room()
    {
        await using var w = await BuildAsync();
        var scan = Ok(await UploadAsync(w, w.Sarah, w.PublicPlace)).Scan;

        await using var db = await w.Db.NewContextAsync();
        var copy = await db.PlaceScans.AsNoTracking().SingleAsync(s => s.Id == scan.Id);
        copy.Id = Guid.NewGuid();
        copy.Version = 2;
        db.PlaceScans.Add(copy);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── the place's census and merge ─────────────────────────────────────────

    [Fact]
    public async Task A_place_with_scans_is_held_by_them()
    {
        await using var w = await BuildAsync();
        Ok(await UploadAsync(w, w.Sarah, w.PublicPlace));

        await using var db = await w.Db.NewContextAsync();
        var usage = await PlaceUsageCensus.ForAsync(db, w.PublicPlace, default);
        Assert.Equal(1, usage.Scans);
        Assert.Equal("1 room scan", usage.WhatHoldsIt());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AdminPlaceScanCustodyController AdminController(World w, Guid acting)
        => new(w.Db.Factory, new Mock<IAuditLogService>().Object, new PlatformMessageService(w.Db.Factory), w.Clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, acting.ToString())], "Bearer")),
                },
            },
        };

    /// <summary>Makes a group the verified venue — directly, or through the claim approval a SuperAdmin or the job runs.</summary>
    private static async Task VerifyVenueAsync(World w, Guid placeId, Guid orgId, bool throughTheClaim = false)
    {
        await using var db = await w.Db.NewContextAsync();
        var owner = await db.OrganizationUserMemberships
            .Where(m => m.OrganizationId == orgId && m.Role == OrganizationMemberRole.Owner)
            .Select(m => m.AppUserId).FirstAsync();

        if (throughTheClaim)
        {
            var claim = new VenuePlaceClaim
            {
                Id = Guid.NewGuid(), PlaceId = placeId, OrganizationId = orgId, ClaimantAppUserId = owner,
                State = VenueClaimState.Proved, DateCreated = w.Clock.Now, CreatedByAppUserId = owner,
            };
            db.VenuePlaceClaims.Add(claim);
            Assert.Null(await VenueClaims.ApproveAsync(db, claim, null, null, w.Clock.Now, default));
        }
        else
        {
            db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, PlaceId = placeId, VerifiedUtc = w.Clock.Now,
                DateCreated = w.Clock.Now, CreatedByAppUserId = owner,
            });
        }
        await db.SaveChangesAsync();
    }
}
