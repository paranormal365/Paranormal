using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// Definition of a single adjustable control point on a server-defined
/// built-in shape callout (<see cref="VideoAssetType.Callout"/>).
///
/// <para>When a callout template is served from the Ben app WebAPI, the admin
/// defines which <see cref="CalloutControlPoints"/> keys users may edit,
/// along with labels, ranges, and defaults for each.</para>
/// </summary>
public sealed record CalloutControlPointDef
{
    /// <summary>
    /// The key matching a constant in <see cref="CalloutControlPoints"/>
    /// (e.g. <c>"midX"</c>, <c>"midY"</c>).
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Human-readable label shown in the editor (e.g. "Curve handle X").</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Minimum allowed value (canvas fraction 0–1 for position keys, 0–1 for radius, ≥0 for pixel values).</summary>
    public double MinValue { get; init; }

    /// <summary>Maximum allowed value.</summary>
    public double MaxValue { get; init; } = 1.0;

    /// <summary>Default value used when the callout is first placed.</summary>
    public double DefaultValue { get; init; }

    /// <summary>
    /// When true, the user can add keyframes to this control point to animate
    /// it over the clip duration. When false, it is a static adjustment only.
    /// </summary>
    public bool AllowKeyframe { get; init; }
}

/// <summary>
/// Describes a server-defined built-in shape template served as a
/// <see cref="VideoAssetType.Callout"/> catalog item.
///
/// <para>This record is returned by the Ben app WebAPI inside
/// <see cref="VideoAssetCatalogItem.ShapeDefinition"/> for callout-type items.
/// Ben.Video.Editor uses it to create a pre-configured <see cref="CalloutClip"/>
/// and display only the control points the admin has permitted.</para>
/// </summary>
public sealed record CalloutShapeDefinition
{
    /// <summary>The built-in shape type to use when this template is added to the timeline.</summary>
    public ShapeType ShapeType { get; init; }

    /// <summary>
    /// Ordered list of control points the user is allowed to adjust.
    /// Empty = no adjustments permitted (shape is fully static).
    /// Null = server did not restrict — show all control points for the shape.
    /// </summary>
    public IReadOnlyList<CalloutControlPointDef>? AdjustableControlPoints { get; init; }

    /// <summary>
    /// Convenience: returns the set of allowed key strings, or null if unrestricted.
    /// </summary>
    public IReadOnlySet<string>? AllowedKeys =>
        AdjustableControlPoints is null ? null
        : (IReadOnlySet<string>)AdjustableControlPoints
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a definition for an arrow where only the curve midpoint can be adjusted.
    /// Typical server default for a "basic curved arrow" template.
    /// </summary>
    public static CalloutShapeDefinition CurvedArrow() => new()
    {
        ShapeType = ShapeType.Arrow,
        AdjustableControlPoints =
        [
            new() { Key = CalloutControlPoints.StartX,  Label = "Start X",   MinValue = 0, MaxValue = 1, DefaultValue = 0.1 },
            new() { Key = CalloutControlPoints.StartY,  Label = "Start Y",   MinValue = 0, MaxValue = 1, DefaultValue = 0.5 },
            new() { Key = CalloutControlPoints.EndX,    Label = "End X",     MinValue = 0, MaxValue = 1, DefaultValue = 0.9, AllowKeyframe = true },
            new() { Key = CalloutControlPoints.EndY,    Label = "End Y",     MinValue = 0, MaxValue = 1, DefaultValue = 0.5, AllowKeyframe = true },
            new() { Key = CalloutControlPoints.MidX,    Label = "Curve X",   MinValue = 0, MaxValue = 1, DefaultValue = 0.5, AllowKeyframe = true },
            new() { Key = CalloutControlPoints.MidY,    Label = "Curve Y",   MinValue = 0, MaxValue = 1, DefaultValue = 0.3, AllowKeyframe = true },
        ],
    };
}
