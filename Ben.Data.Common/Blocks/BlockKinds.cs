namespace Ben.Data.Common.Blocks;

/// <summary>The kinds of block a block document can hold.</summary>
/// <remarks>
/// Strings rather than an enum because they are stored in JSON that outlives any one build: a value written by a newer
/// build is recognised as unknown and refused with a sentence, never silently turned into a different kind. A table
/// block is the next kind asked for (Ben, 2026-09-14).
/// </remarks>
public static class BlockKinds
{
    public const string Text = "text";
    public const string Image = "image";
    public const string File = "file";
    public const string Link = "link";
    public const string Map = "map";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>([Text, Image, File, Link, Map], StringComparer.Ordinal);
}

/// <summary>How a map block joins its stops.</summary>
/// <remarks>
/// Chosen by the author per map (Ben, 2026-09-14): none, straight lines, or a walking or driving route from the map
/// provider. Only the choice is stored; distances and times are worked out when the map is shown.
/// </remarks>
public static class BlockMapRoutes
{
    public const string None = "none";
    public const string Straight = "straight";
    public const string Walking = "walking";
    public const string Driving = "driving";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>([None, Straight, Walking, Driving], StringComparer.Ordinal);
}

/// <summary>Limits a map block keeps within — the same number on the editor and on the server.</summary>
public static class BlockMapLimits
{
    /// <summary>Each walking or driving leg is one directions request every time the map is shown.</summary>
    public const int MaxStops = 10;
}
