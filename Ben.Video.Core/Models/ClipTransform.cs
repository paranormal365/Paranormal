namespace Ben.Video.Editor.Models;

/// <summary>
/// Where a clip's picture sits in the frame, and how much of it is used.
/// </summary>
/// <remarks>
/// <para>A clip could only ever fill the frame. <see cref="VideoClip"/> and <see cref="ImageClip"/>
/// carried a width and a height and nothing else — no position, no scale, no crop — so a clip on a
/// second track could replace the picture underneath and never sit beside it or in a corner of it.
/// Two cameras side by side, a corner inset over the wide shot, portrait phone footage turned
/// upright, the black bars off a DVR export: none of it was possible (2026-09-05 audit, the
/// completeness critic's picture-in-picture and crop/rotate items).</para>
///
/// <para>Fractions throughout, of the frame for the placement and of the source for the crop, so a
/// layout survives a change of export resolution.</para>
///
/// <para>Null on a clip means "fill the frame", which is what every clip did before this existed
/// and what most clips should go on doing.</para>
/// </remarks>
public sealed record ClipTransform
{
    /// <summary>Left edge of the placed picture, as a fraction of the frame width.</summary>
    public double X { get; set; }

    /// <summary>Top edge of the placed picture, as a fraction of the frame height.</summary>
    public double Y { get; set; }

    /// <summary>Width of the placed picture, as a fraction of the frame width.</summary>
    public double Width { get; set; } = 1.0;

    /// <summary>Height of the placed picture, as a fraction of the frame height.</summary>
    public double Height { get; set; } = 1.0;

    /// <summary>Rotation in degrees, clockwise.</summary>
    /// <remarks>
    /// What turns portrait phone footage upright. Applied to the source before it is placed, so
    /// the box the picture lands in is the box that was drawn.
    /// </remarks>
    public double Rotation { get; set; }

    /// <summary>How much of the source to cut off each edge, as a fraction of the source.</summary>
    /// <remarks>
    /// The DVR bars, the timestamp burned into the corner, the neighbour's window at the edge of
    /// the shot. Cutting rather than covering, so nothing of it reaches the output at all.
    /// </remarks>
    public double CropLeft   { get; set; }

    /// <inheritdoc cref="CropLeft"/>
    public double CropTop    { get; set; }

    /// <inheritdoc cref="CropLeft"/>
    public double CropRight  { get; set; }

    /// <inheritdoc cref="CropLeft"/>
    public double CropBottom { get; set; }

    /// <summary>How opaque the placed picture is.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Whether this transform asks for anything at all.</summary>
    /// <remarks>
    /// The export skips its whole extra pass when nothing is asked for, so a project that never
    /// touches any of this renders exactly as it did before.
    /// </remarks>
    public bool IsIdentity =>
        X == 0 && Y == 0 && Width == 1.0 && Height == 1.0
        && Rotation == 0 && Opacity >= 1.0
        && CropLeft == 0 && CropTop == 0 && CropRight == 0 && CropBottom == 0;
}
