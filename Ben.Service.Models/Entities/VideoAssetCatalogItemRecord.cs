using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// One entry in the response of <c>GET /api/video-assets</c>.
/// </summary>
/// <remarks>
/// <para><b>This is a wire contract with a consumer in another repository.</b>
/// Ben.Video.Editor deserialises it into its own <c>VideoAssetCatalogItem</c>
/// (Ben.Video.Core/Models/Assets). Property names must match that record exactly — the editor
/// uses default System.Text.Json settings, so a renamed property silently arrives as null rather
/// than failing loudly.</para>
///
/// <para>Fields the editor defines but this server does not yet populate — <c>ControlPoints</c>
/// and <c>ShapeDefinition</c> — are deliberately omitted rather than sent as null. Both belong to
/// the callout-shape authoring feature, which is not built; the editor treats absent as null, so
/// omitting them costs nothing and adding them later is additive.</para>
///
/// <para>The three local-cache flags (<c>IsLocalAvailable</c>, <c>IsUpdateAvailable</c>,
/// <c>IsServerRemoved</c>) are also omitted: the editor computes them itself after comparing the
/// catalog against its own OPFS cache. Sending them would be the server guessing at browser state.</para>
/// </remarks>
public sealed record VideoAssetCatalogItemRecord
{
    /// <summary>Asset id as a string. The editor parses this as a Guid to build its cache path.</summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Always <see cref="AssetSource.SharedCatalog"/> from this server.</summary>
    public AssetSource Source { get; init; } = AssetSource.SharedCatalog;

    public VideoAssetType Type { get; init; }
    public VideoAssetFormat Format { get; init; }

    /// <summary>Thumbnail URL, served without auth so the browser grid can load it directly.</summary>
    public string ThumbnailUrl { get; init; } = string.Empty;

    /// <summary>SHA-256 of the binary. The editor re-downloads when this changes.</summary>
    public string Version { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; init; }
    public long FileSizeBytes { get; init; }
    public int? NativeWidth { get; init; }
    public int? NativeHeight { get; init; }

    public VideoAssetSettingsRecord Settings { get; init; } = new();
}

/// <summary>
/// What the editor permits a user to do with an asset. Mirrors Ben.Video's
/// <c>VideoAssetSettings</c> — same names, same defaults.
/// </summary>
public sealed record VideoAssetSettingsRecord
{
    public bool AllowRecolor { get; init; }
    public bool AllowResize { get; init; } = true;
    public bool AllowOpacity { get; init; } = true;
    public bool AllowRotation { get; init; } = true;
    public bool AllowEffects { get; init; }
    public bool AllowEasing { get; init; }
    public bool AllowMotion { get; init; } = true;
    public bool AllowControlPoints { get; init; }
    public IReadOnlyList<string>? PresetColors { get; init; }
    public double? MinScale { get; init; }
    public double? MaxScale { get; init; }
    public bool FlattenOnExport { get; init; } = true;
}

// ── Admin-side records ───────────────────────────────────────────────────────

/// <summary>A catalog entry as the SuperAdmin management page sees it, including inactive ones.</summary>
public sealed record VideoAssetAdminRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Tags { get; init; }
    public VideoAssetType Type { get; init; }
    public VideoAssetFormat Format { get; init; }
    public Guid UploadFileId { get; init; }
    public Guid? ThumbnailUploadFileId { get; init; }
    public string? ContentHash { get; init; }
    public long FileSizeBytes { get; init; }
    public int? NativeWidth { get; init; }
    public int? NativeHeight { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
}

/// <param name="UploadFileId">An already-uploaded file to publish as an asset.</param>
public sealed record CreateVideoAssetRequest(
    Guid UploadFileId,
    string Name,
    string? Description,
    string? Category,
    string? Tags,
    VideoAssetType Type,
    Guid? ThumbnailUploadFileId = null,
    int? NativeWidth = null,
    int? NativeHeight = null,
    int SortOrder = 0);

public sealed record UpdateVideoAssetRequest(
    string Name,
    string? Description,
    string? Category,
    string? Tags,
    VideoAssetType Type,
    bool IsActive,
    Guid? ThumbnailUploadFileId = null,
    int? NativeWidth = null,
    int? NativeHeight = null,
    int SortOrder = 0);
