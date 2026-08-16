namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service holding how much the editor's own Preview render should be scaled down from the
/// real export resolution (<see cref="ExportResolutionService"/>) — never the export resolution
/// itself, and never read by the real export pipeline. Default 100% keeps Preview byte-for-byte
/// identical to today's behavior; lower values trade preview quality for a faster/cheaper re-encode.
/// </summary>
public sealed class PreviewQualityService
{
    public int ScalePercent { get; private set; } = 75;

    public event Action? OnChanged;

    public void SetScalePercent(int percent)
    {
        ScalePercent = percent;
        OnChanged?.Invoke();
    }
}
