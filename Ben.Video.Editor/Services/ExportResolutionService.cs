namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service sharing the currently-configured export output resolution outside of
/// <see cref="ExportDialog"/> (where <c>ExportSettings.Resolution</c> otherwise lives on a
/// private, dialog-scoped field). The preview row and its popout use this to cap their
/// displayed size at the export's real pixel dimensions.
/// </summary>
public sealed class ExportResolutionService
{
    public int Width  { get; private set; } = 1280;
    public int Height { get; private set; } = 720;

    public event Action? OnChanged;

    /// <summary>Accepts the same "WxH" (or empty, meaning source resolution) shape as
    /// <c>ExportSettings.Resolution</c>. Empty/unparseable values fall back to 1920×1080 —
    /// <see cref="ExportService.ParseResolution"/>'s own fallback, deliberately left at Full HD
    /// since it's the "use source/unspecified" sentinel path, not a speed-tuned default. This
    /// service's own field default (1280×720) is the actual initial/faster default, matching
    /// <see cref="ExportSettings.Resolution"/>'s field default — they only diverge for that one
    /// sentinel case.</summary>
    public void SetResolution(string resolution)
    {
        (Width, Height) = ExportService.ParseResolution(resolution);
        OnChanged?.Invoke();
    }
}
