namespace Ben.Service.Models.Entities;

/// <summary>
/// The response of <c>GET /api/video-assets/watermark-config</c>.
/// </summary>
/// <remarks>
/// <para><b>A wire contract with the editor.</b> Ben.Video.Editor deserialises this into its own
/// <c>VideoWatermarkConfig</c> (Ben.Video.Core/Models/Assets) with default System.Text.Json
/// settings, so a renamed property arrives as null rather than failing — the same rule as
/// <see cref="VideoAssetCatalogItemRecord"/>.</para>
///
/// <para>The editor's record carries presentation settings too — opacity, position, scale and
/// margins — with sensible defaults. They are omitted here deliberately: nothing administers them,
/// and sending the defaults back would imply somebody had chosen them. When a screen exists for
/// setting them, adding the properties is additive and the editor already reads them.</para>
///
/// <para>Every field is optional except <see cref="Enabled"/>, which is the whole answer when no
/// watermark is configured.</para>
/// </remarks>
public sealed record VideoWatermarkConfigRecord
{
    /// <summary>True when every export must carry the watermark at <see cref="FileUrl"/>.</summary>
    public bool Enabled { get; init; }

    /// <summary>Absolute URL of the watermark image. Null when <see cref="Enabled"/> is false.</summary>
    public string? FileUrl { get; init; }

    /// <summary>
    /// Content hash of the watermark file, so the editor can tell a stale local copy from a
    /// current one without re-downloading it.
    /// </summary>
    public string? Version { get; init; }
}
