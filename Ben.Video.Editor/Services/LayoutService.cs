namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that owns the editor layout state:
/// panel visibility toggles and drag-resized panel sizes.
/// Components subscribe to <see cref="OnChanged"/> to re-render when layout mutates.
/// </summary>
public sealed class LayoutService
{
    // ── visibility ────────────────────────────────────────────────────────────
    public bool ShowClipBrowser  { get; private set; } = true;
    public bool ShowPreview      { get; private set; } = true;
    public bool ShowTimeline     { get; private set; } = true;

    // ── sizes (CSS-custom-property values written onto .bv-editor) ───────────
    /// <summary>Width of the Clip Browser panel in pixels.</summary>
    public int BrowserWidth      { get; private set; } = 240;

    /// <summary>Height of the Timeline section in pixels.</summary>
    public int TimelineHeight    { get; private set; } = 220;

    /// <summary>Height of the Preview row in pixels.</summary>
    public int PreviewHeight     { get; private set; } = 180;

    // ── constraints ──────────────────────────────────────────────────────────
    public const int BrowserMinWidth   = 160;
    public const int BrowserMaxWidth   = 480;
    public const int TimelineMinHeight = 120;
    public const int TimelineMaxHeight = 600;
    public const int PreviewMinHeight  = 90;
    public const int PreviewMaxHeight  = 400;

    // ── change notification ───────────────────────────────────────────────────
    public event Action? OnChanged;

    // ── visibility toggles ────────────────────────────────────────────────────
    public void ToggleClipBrowser()
    {
        ShowClipBrowser = !ShowClipBrowser;
        Notify();
    }

    public void TogglePreview()
    {
        ShowPreview = !ShowPreview;
        Notify();
    }

    public void ToggleTimeline()
    {
        ShowTimeline = !ShowTimeline;
        Notify();
    }

    // ── resize ────────────────────────────────────────────────────────────────
    public void SetBrowserWidth(int px)
    {
        BrowserWidth = Math.Clamp(px, BrowserMinWidth, BrowserMaxWidth);
        Notify();
    }

    public void SetTimelineHeight(int px)
    {
        TimelineHeight = Math.Clamp(px, TimelineMinHeight, TimelineMaxHeight);
        Notify();
    }

    public void SetPreviewHeight(int px)
    {
        PreviewHeight = Math.Clamp(px, PreviewMinHeight, PreviewMaxHeight);
        Notify();
    }

    private void Notify() => OnChanged?.Invoke();
}
