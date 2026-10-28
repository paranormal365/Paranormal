using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.FieldSessions;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// What a room's <c>.scan</c> file says about itself, once it has been checked (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// <para><b>The file.</b> One stored ZIP, like a session's <c>.ben</c>, read in place by
/// <see cref="BenBundle"/> and served member by member:</para>
/// <code>
/// manifest.json          what is in it — format, room, place, when, device, retention, counts
/// room.usdz              the RoomPlan model (walls, doors, furniture), for viewers
/// room.json              the same model as JSON — what a comparison diffs
/// mesh.glb               the ARKit mesh, glTF binary
/// worldmap.arworldmap    the ARKit world map a later phone relocalizes against
/// preview.jpg            one picture for lists
/// keyframes/index.json   pose, intrinsics and time of every keyframe
/// keyframes/0001.jpg     a keyframe picture, and its depth beside it (0001.depth)
/// </code>
///
/// <para><b>Nothing else may be in one.</b> A scan's members are served from this site by name,
/// so the list of names is closed: an extra member would be a file anybody could host here. A
/// member the phone did not write is refused with its name.</para>
///
/// <para><b>Every refusal is a sentence.</b> It reaches a person who has just waited for an upload,
/// usually standing in the room, and "invalid manifest" tells them nothing they can do.</para>
/// </remarks>
/// <param name="Room">The room's name, cleaned the way a stored name is.</param>
/// <param name="PlaceId">The place the phone was at, when the manifest names one.</param>
public sealed record PlaceScanManifest(
    string Room, Guid? PlaceId, DateTime ScannedAt, string? Device, int ScannerVersion,
    PlaceScanRetention Retention, int KeyframeCount, double? FloorAreaM2, int TriangleCount)
{
    /// <summary>The format a baseline scan declares. A new major format is a new string.</summary>
    public const string FormatName = "ishaunted-scan/1";

    public const string ManifestEntry = "manifest.json";
    public const string MeshEntry = "mesh.glb";
    public const string RoomPlanEntry = "room.usdz";
    public const string RoomModelEntry = "room.json";
    public const string WorldMapEntry = "worldmap.arworldmap";
    public const string PreviewEntry = "preview.jpg";
    public const string KeyframeIndexEntry = "keyframes/index.json";

    /// <summary>
    /// More keyframes than a careful scan of a large room takes — one every half metre or fifteen
    /// degrees comes to about 150 in an ordinary room.
    /// </summary>
    public const int MaxKeyframes = 500;

    /// <summary>A picture and a depth map per keyframe, plus the fixed members and a little room.</summary>
    public const int MaxEntries = MaxKeyframes * 2 + 16;

    /// <summary>Largest scan accepted: a large room with every picture is a few hundred megabytes.</summary>
    public const long MaxScanBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>A manifest is a few hundred bytes; past this it is not a manifest.</summary>
    public const long MaxManifestBytes = 64 * 1024;

    /// <summary>The keyframe index lists at most <see cref="MaxKeyframes"/> small records.</summary>
    public const long MaxKeyframeIndexBytes = 4L * 1024 * 1024;

    private static readonly Regex KeyframeMember = new(@"^keyframes/(\d{4})\.(jpg|depth)$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> FixedMembers = new(StringComparer.Ordinal)
    {
        ManifestEntry, MeshEntry, RoomPlanEntry, RoomModelEntry, WorldMapEntry, PreviewEntry, KeyframeIndexEntry,
    };

    /// <summary>What to serve a member as, from its name.</summary>
    public static string ContentTypeFor(string entryPath) => Path.GetExtension(entryPath).ToLowerInvariant() switch
    {
        ".glb" => "model/gltf-binary",
        ".usdz" => "model/vnd.usdz+zip",
        ".jpg" => "image/jpeg",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Reads and checks a scan's manifest against its members. Throws
    /// <see cref="BenBundleFormatException"/> with a sentence when the file is not a room scan the
    /// app could have written.
    /// </summary>
    /// <param name="bundle">The file's member list, already indexed under <see cref="BenBundleRules.Scan"/>.</param>
    /// <param name="open">Opens one member's bytes.</param>
    public static async Task<PlaceScanManifest> ReadAsync(
        BenBundle bundle, Func<string, CancellationToken, Task<Stream?>> open, CancellationToken ct)
    {
        foreach (var path in bundle.Entries.Keys)
        {
            if (!FixedMembers.Contains(path) && !KeyframeMember.IsMatch(path))
                throw new BenBundleFormatException($"That scan carries \"{path}\", which a room scan does not have.");
        }

        if (bundle.Find(ManifestEntry) is not { } manifestEntry)
            throw new BenBundleFormatException("That scan has no manifest.json, so there is nothing to say which room it is.");
        if (manifestEntry.Length > MaxManifestBytes)
            throw new BenBundleFormatException("That scan's manifest is far larger than a manifest can be.");

        JsonElement root;
        await using (var stream = await open(ManifestEntry, ct))
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(stream!, cancellationToken: ct);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new BenBundleFormatException("That scan's manifest isn't readable JSON.");
            }
        }
        if (root.ValueKind != JsonValueKind.Object)
            throw new BenBundleFormatException("That scan's manifest isn't readable JSON.");

        var format = Text(root, "format");
        if (format != FormatName)
        {
            throw new BenBundleFormatException(format is null
                ? "That scan's manifest doesn't say what format it is."
                : $"That scan is in format \"{format}\", and this site reads {FormatName}. Update the app and scan again.");
        }

        if (Text(root, "kind") is { } kind && kind != "baseline")
        {
            throw new BenBundleFormatException(kind == "comparison"
                ? "That is a comparison scan. A comparison belongs in the session it was made in, not to the place."
                : $"That scan says it is a \"{kind}\", which isn't a kind of room scan.");
        }

        var room = PlaceScans.CleanRoomName(Text(root, "room"));
        if (room is null)
            throw new BenBundleFormatException("That scan doesn't say which room it is.");
        if (room.Length > PlaceScans.MaxRoomNameLength)
            throw new BenBundleFormatException($"A room's name can be at most {PlaceScans.MaxRoomNameLength} characters.");

        if (Text(root, "scannedAt") is not { } scannedText
            || !DateTime.TryParse(scannedText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var scannedAt))
        {
            throw new BenBundleFormatException("That scan doesn't say when it was made.");
        }

        var scannerVersion = root.TryGetProperty("scannerVersion", out var versionElement)
                             && versionElement.ValueKind == JsonValueKind.Number
                             && versionElement.TryGetInt32(out var v) ? v : 0;
        if (scannerVersion < 1)
            throw new BenBundleFormatException("That scan doesn't say which version of the scanner made it.");

        var retention = Text(root, "retention") switch
        {
            "modelAndPictures" => PlaceScanRetention.ModelAndPictures,
            "modelOnly" => PlaceScanRetention.ModelOnly,
            null => throw new BenBundleFormatException("That scan doesn't say whether it keeps its pictures."),
            var other => throw new BenBundleFormatException($"That scan keeps \"{other}\", which isn't one of the choices."),
        };

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Object)
            throw new BenBundleFormatException("That scan's manifest doesn't list what it carries.");

        // The mesh is the scan: without it there is nothing to walk through or compare against.
        RequireNamed(entries, "mesh", MeshEntry, bundle, required: true);
        RequireNamed(entries, "roomplan", RoomPlanEntry, bundle, required: false);
        RequireNamed(entries, "roomModel", RoomModelEntry, bundle, required: false);
        RequireNamed(entries, "worldmap", WorldMapEntry, bundle, required: false);
        RequireNamed(entries, "preview", PreviewEntry, bundle, required: false);

        var keyframes = entries.TryGetProperty("keyframes", out var countElement)
                        && countElement.ValueKind == JsonValueKind.Number
                        && countElement.TryGetInt32(out var count) ? count : 0;
        if (keyframes < 0)
            throw new BenBundleFormatException("That scan claims fewer than no keyframes.");
        if (keyframes > MaxKeyframes)
            throw new BenBundleFormatException($"That scan claims {keyframes} keyframes; a room scan keeps at most {MaxKeyframes}.");

        var pictures = bundle.Entries.Keys.Count(p => p.StartsWith("keyframes/", StringComparison.Ordinal) && p.EndsWith(".jpg", StringComparison.Ordinal));
        if (retention == PlaceScanRetention.ModelOnly && (keyframes > 0 || pictures > 0 || bundle.Find(KeyframeIndexEntry) is not null))
            throw new BenBundleFormatException("That scan was kept as the model only, but it carries pictures.");
        if (pictures != keyframes)
            throw new BenBundleFormatException($"That scan says it has {keyframes} keyframes and carries {pictures}.");
        if (keyframes > 0 && bundle.Find(KeyframeIndexEntry) is null)
            throw new BenBundleFormatException("That scan's keyframes have no index, so nothing says where each was taken.");
        if (bundle.Find(KeyframeIndexEntry) is { Length: > MaxKeyframeIndexBytes })
            throw new BenBundleFormatException("That scan's keyframe index is far larger than one can be.");

        foreach (var depth in bundle.Entries.Keys.Where(p => p.EndsWith(".depth", StringComparison.Ordinal)))
        {
            if (bundle.Find(depth[..^".depth".Length] + ".jpg") is null)
                throw new BenBundleFormatException($"That scan carries {depth} without the picture it belongs to.");
        }

        // The bytes, not the names: a member called mesh.glb that is not a glTF file is refused here
        // rather than handed to a viewer that fails on it.
        await RequireHeaderAsync(MeshEntry, "a glTF model", h => h.Length >= 4 && h[0] == (byte)'g' && h[1] == (byte)'l' && h[2] == (byte)'T' && h[3] == (byte)'F');
        if (bundle.Find(RoomPlanEntry) is not null)
            await RequireHeaderAsync(RoomPlanEntry, "a USDZ model", h => h.Length >= 4 && h[0] == (byte)'P' && h[1] == (byte)'K' && h[2] == 3 && h[3] == 4);
        foreach (var picture in bundle.Entries.Keys.Where(p => p.EndsWith(".jpg", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal))
            await RequireHeaderAsync(picture, "a JPEG picture", h => h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF);

        double? floorArea = null;
        var triangles = 0;
        if (root.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            if (stats.TryGetProperty("floorAreaM2", out var area) && area.ValueKind == JsonValueKind.Number
                && area.TryGetDouble(out var a) && a > 0 && a < 100_000) floorArea = Math.Round(a, 2);
            if (stats.TryGetProperty("triangles", out var tri) && tri.ValueKind == JsonValueKind.Number
                && tri.TryGetInt32(out var t) && t > 0) triangles = t;
        }

        var device = Text(root, "device");
        if (device is { Length: > 60 }) device = device[..60];

        var placeId = Text(root, "placeId") is { } placeText && Guid.TryParse(placeText, out var id) ? id : (Guid?)null;

        return new PlaceScanManifest(room, placeId, scannedAt, device, scannerVersion, retention, keyframes, floorArea, triangles);

        async Task RequireHeaderAsync(string path, string what, Func<byte[], bool> matches)
        {
            var header = new byte[8];
            var read = 0;
            await using (var member = await open(path, ct))
            {
                if (member is not null) read = await member.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            }
            if (!matches(header[..read]))
                throw new BenBundleFormatException($"That scan's {path} is not {what}.");
        }
    }

    private static void RequireNamed(JsonElement entries, string key, string canonical, BenBundle bundle, bool required)
    {
        var named = entries.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

        if (named is null)
        {
            if (required)
                throw new BenBundleFormatException($"That scan has no {canonical}, so there is no model of the room in it.");
            return;
        }
        if (named != canonical)
            throw new BenBundleFormatException($"That scan names its {key} \"{named}\"; a room scan's is {canonical}.");
        if (bundle.Find(canonical) is null)
            throw new BenBundleFormatException($"That scan lists {canonical} but doesn't carry it.");
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
