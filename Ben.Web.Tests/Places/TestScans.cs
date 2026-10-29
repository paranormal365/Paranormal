using System.Text;
using System.Text.Json.Nodes;
using Ben.Web.Tests.Services;
using Microsoft.AspNetCore.Http;

namespace Ben.Web.Tests.Places;

/// <summary>
/// Room scans for the tests, built the way the phone's <c>ScanPackage</c> writes them: a stored
/// ZIP with a manifest, a glTF mesh, and a picture and a depth map per keyframe.
/// </summary>
/// <remarks>
/// Written with <see cref="BenBundleTests.StoredZip"/>, the re-implementation of the phone's
/// <c>ZipWriter</c>, rather than .NET's ZipArchive — the server should be tested against the file
/// the phone makes.
/// </remarks>
internal static class TestScans
{
    /// <summary>A glTF binary header and a few bytes of body — enough to be a glTF file by its first bytes.</summary>
    public static byte[] Mesh(int bytes = 64)
    {
        var mesh = new byte[Math.Max(bytes, 12)];
        "glTF"u8.CopyTo(mesh);
        mesh[4] = 2;
        return mesh;
    }

    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 0xFF, 0xD9];
    public static readonly byte[] Usdz = [(byte)'P', (byte)'K', 3, 4, 0, 0, 0, 0];

    /// <summary>A manifest as the phone writes one, for a test to change before it is packed.</summary>
    public static JsonObject Manifest(string room, Guid? placeId, int keyframes, string retention, DateTime scannedAt)
        => new()
        {
            ["format"] = "ishaunted-scan/1",
            ["kind"] = "baseline",
            ["room"] = room,
            ["placeId"] = placeId?.ToString(),
            ["scannedAt"] = scannedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["device"] = "iPhone16,1",
            ["scannerVersion"] = 1,
            ["retention"] = retention,
            ["frame"] = new JsonObject { ["origin"] = new JsonArray(0, 0, 0), ["up"] = new JsonArray(0, 1, 0) },
            ["stats"] = new JsonObject { ["floorAreaM2"] = 18.4, ["triangles"] = 1234 },
            ["entries"] = new JsonObject
            {
                ["roomplan"] = "room.usdz",
                ["roomModel"] = "room.json",
                ["mesh"] = "mesh.glb",
                ["worldmap"] = "worldmap.arworldmap",
                ["preview"] = "preview.jpg",
                ["keyframes"] = keyframes,
            },
        };

    /// <summary>
    /// The members of a scan. <paramref name="change"/> edits the manifest; <paramref name="members"/>
    /// edits the list before it is packed — to drop the mesh, add a stray file, and so on.
    /// </summary>
    public static List<(string Path, byte[] Bytes)> Members(
        string room = "Bedroom 1", Guid? placeId = null, int keyframes = 2,
        string retention = "modelAndPictures", DateTime? scannedAt = null,
        Action<JsonObject>? change = null, int meshBytes = 64)
    {
        var manifest = Manifest(room, placeId, keyframes, retention, scannedAt ?? new DateTime(2026, 10, 28, 21, 0, 0, DateTimeKind.Utc));
        change?.Invoke(manifest);

        var members = new List<(string Path, byte[] Bytes)>
        {
            ("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())),
            ("room.usdz", Usdz),
            ("room.json", """{"objects":[]}"""u8.ToArray()),
            ("mesh.glb", Mesh(meshBytes)),
            ("worldmap.arworldmap", new byte[32]),
            ("preview.jpg", Jpeg),
        };
        if (keyframes > 0)
        {
            members.Add(("keyframes/index.json", "[]"u8.ToArray()));
            for (var i = 1; i <= keyframes; i++)
            {
                members.Add(($"keyframes/{i:0000}.jpg", Jpeg));
                members.Add(($"keyframes/{i:0000}.depth", new byte[16]));
            }
        }
        return members;
    }

    /// <summary>A whole scan file.</summary>
    public static MemoryStream File(IEnumerable<(string Path, byte[] Bytes)> members, bool deflateFirst = false)
        => BenBundleTests.StoredZip.Write(deflateFirst, members.ToArray());

    /// <summary>A scan as an uploaded form file.</summary>
    public static IFormFile Upload(IEnumerable<(string Path, byte[] Bytes)> members)
    {
        var stream = File(members);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "room.scan");
    }
}
