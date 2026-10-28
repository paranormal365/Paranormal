using System.Text.Json;
using System.Text.Json.Nodes;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Places;
using Ben.Web.Tests.Services;
using Xunit;

namespace Ben.Web.Tests.Places;

/// <summary>
/// What a room's <c>.scan</c> must carry, and the sentence for each way it can fail to (LiDAR
/// plan, phase 1). Each refusal reaches somebody who has just waited for an upload.
/// </summary>
public sealed class PlaceScanManifestTests
{
    private static async Task<PlaceScanManifest> ReadAsync(IEnumerable<(string Path, byte[] Bytes)> members, bool deflateFirst = false)
    {
        var file = TestScans.File(members, deflateFirst);
        var bundle = await BenBundle.ReadAsync(file, BenBundleRules.Scan);
        return await PlaceScanManifest.ReadAsync(bundle, (path, _) =>
        {
            var entry = bundle.Find(path);
            Stream? member = entry is null ? null
                : new MemoryStream(file.ToArray(), (int)entry.Offset, (int)entry.Length, writable: false);
            return Task.FromResult(member);
        }, default);
    }

    private static async Task<string> RefusalAsync(IEnumerable<(string Path, byte[] Bytes)> members, bool deflateFirst = false)
        => (await Assert.ThrowsAsync<BenBundleFormatException>(() => ReadAsync(members, deflateFirst))).Message;

    private static List<(string Path, byte[] Bytes)> Without(string path, List<(string Path, byte[] Bytes)> members)
        => members.Where(m => m.Path != path).ToList();

    [Fact]
    public async Task A_scan_the_phone_wrote_reads_back_as_what_it_says()
    {
        var placeId = Guid.NewGuid();
        var manifest = await ReadAsync(TestScans.Members("  Room   217 ", placeId, keyframes: 3));

        Assert.Equal("Room 217", manifest.Room);
        Assert.Equal(placeId, manifest.PlaceId);
        Assert.Equal(new DateTime(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc), manifest.ScannedAt);
        Assert.Equal(DateTimeKind.Utc, manifest.ScannedAt.Kind);
        Assert.Equal("iPhone16,1", manifest.Device);
        Assert.Equal(1, manifest.ScannerVersion);
        Assert.Equal(PlaceScanRetention.ModelAndPictures, manifest.Retention);
        Assert.Equal(3, manifest.KeyframeCount);
        Assert.Equal(18.4, manifest.FloorAreaM2);
        Assert.Equal(1234, manifest.TriangleCount);
    }

    [Fact]
    public async Task A_model_only_scan_carries_no_pictures()
    {
        var manifest = await ReadAsync(TestScans.Members(keyframes: 0, retention: "modelOnly"));
        Assert.Equal(PlaceScanRetention.ModelOnly, manifest.Retention);
        Assert.Equal(0, manifest.KeyframeCount);

        var members = TestScans.Members(keyframes: 0, retention: "modelOnly");
        members.Add(("keyframes/0001.jpg", TestScans.Jpeg));
        Assert.Equal("That scan was kept as the model only, but it carries pictures.", await RefusalAsync(members));
    }

    [Fact]
    public async Task A_manifest_that_is_not_json_is_refused()
    {
        var members = TestScans.Members();
        members[0] = ("manifest.json", "{ not json"u8.ToArray());
        Assert.Equal("That scan's manifest isn't readable JSON.", await RefusalAsync(members));
    }

    [Fact]
    public async Task A_scan_without_its_manifest_is_refused()
        => Assert.Equal("That scan has no manifest.json, so there is nothing to say which room it is.",
            await RefusalAsync(Without("manifest.json", TestScans.Members())));

    [Fact]
    public async Task A_scan_without_a_mesh_is_refused()
    {
        Assert.Equal("That scan lists mesh.glb but doesn't carry it.",
            await RefusalAsync(Without("mesh.glb", TestScans.Members())));

        Assert.Equal("That scan has no mesh.glb, so there is no model of the room in it.",
            await RefusalAsync(TestScans.Members(change: m => m["entries"]!.AsObject().Remove("mesh"))));
    }

    [Fact]
    public async Task A_mesh_that_is_not_gltf_is_refused()
    {
        var members = TestScans.Members();
        var mesh = members.FindIndex(m => m.Path == "mesh.glb");
        members[mesh] = ("mesh.glb", new byte[64]);
        Assert.Equal("That scan's mesh.glb is not a glTF model.", await RefusalAsync(members));
    }

    [Fact]
    public async Task Five_hundred_and_one_keyframes_are_refused()
        => Assert.Equal("That scan claims 501 keyframes; a room scan keeps at most 500.",
            await RefusalAsync(TestScans.Members(keyframes: 2, change: m => m["entries"]!["keyframes"] = 501)));

    [Fact]
    public async Task Five_hundred_keyframes_are_a_scan()
        => Assert.Equal(500, (await ReadAsync(TestScans.Members(keyframes: 500))).KeyframeCount);

    [Fact]
    public async Task A_keyframe_count_that_does_not_match_the_pictures_is_refused()
        => Assert.Equal("That scan says it has 3 keyframes and carries 2.",
            await RefusalAsync(TestScans.Members(keyframes: 2, change: m => m["entries"]!["keyframes"] = 3)));

    [Fact]
    public async Task A_compressed_member_is_refused()
        => Assert.Equal("That scan file is compressed, and a scan is stored uncompressed so it can be viewed from inside.",
            await RefusalAsync(TestScans.Members(), deflateFirst: true));

    [Fact]
    public async Task A_member_the_phone_never_writes_is_refused_by_name()
    {
        var members = TestScans.Members();
        members.Add(("index.html", "<script>"u8.ToArray()));
        Assert.Equal("That scan carries \"index.html\", which a room scan does not have.", await RefusalAsync(members));
    }

    [Fact]
    public async Task A_comparison_belongs_in_its_session_not_to_the_place()
        => Assert.Equal("That is a comparison scan. A comparison belongs in the session it was made in, not to the place.",
            await RefusalAsync(TestScans.Members(change: m => m["kind"] = "comparison")));

    [Fact]
    public async Task Another_format_is_refused_with_what_to_do()
        => Assert.Equal("That scan is in format \"ishaunted-scan/2\", and this site reads ishaunted-scan/1. Update the app and scan again.",
            await RefusalAsync(TestScans.Members(change: m => m["format"] = "ishaunted-scan/2")));

    [Fact]
    public async Task A_scan_that_does_not_say_which_room_is_refused()
        => Assert.Equal("That scan doesn't say which room it is.",
            await RefusalAsync(TestScans.Members(change: m => m["room"] = "   ")));

    [Fact]
    public async Task A_depth_map_without_its_picture_is_refused()
    {
        var members = TestScans.Members(keyframes: 2);
        members.Add(("keyframes/0003.depth", new byte[16]));
        Assert.Equal("That scan carries keyframes/0003.depth without the picture it belongs to.", await RefusalAsync(members));
    }

    [Fact]
    public async Task A_scan_with_more_members_than_any_scan_has_is_refused_before_a_name_is_read()
    {
        var members = TestScans.Members(keyframes: 0);
        for (var i = 0; i < PlaceScanManifest.MaxEntries; i++) members.Add(($"keyframes/{i:0000}.depth", new byte[1]));
        Assert.StartsWith("That scan claims", await RefusalAsync(members));
    }
}

/// <summary>
/// A room's name and its key, held to the vectors the phone's <c>RoomKey</c> is held to as well.
/// </summary>
/// <remarks>
/// The vectors live with the phone's fixtures (<c>Fixtures/place-scans/room-key-vectors.json</c>)
/// and are linked into this project, so changing a rule on one side without the other fails here or
/// there — and "Bedroom 1" never becomes two rooms with two scans.
/// </remarks>
public sealed class PlaceScanRoomKeyTests
{
    public static TheoryData<string, string?, string> Vectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "place-scans", "room-key-vectors.json");
        var data = new TheoryData<string, string?, string>();
        foreach (var vector in JsonNode.Parse(File.ReadAllText(path))!["vectors"]!.AsArray())
            data.Add((string)vector!["input"]!, (string?)vector["name"], (string)vector["key"]!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void A_room_name_becomes_the_same_key_on_the_server_as_on_the_phone(string input, string? name, string key)
    {
        Assert.Equal(name, PlaceScans.CleanRoomName(input));
        Assert.Equal(key, PlaceScans.NormalizeRoomKey(input));
    }

    [Fact]
    public void The_vectors_are_there_to_be_read()
        => Assert.True(Vectors().Count >= 10, "The room-key vectors were not found beside the test binary.");
}
