namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service tracking whether the current Preview render still matches the timeline's edited
/// state — a whole-timeline signal, not per-clip/per-region. True per-region tracking (Premiere-style)
/// isn't buildable on the current pipeline: <see cref="ClipStore"/>'s change notification carries no
/// information about what changed, and the preview doesn't even reflect every edit type yet (video-clip
/// trims aren't applied to the concatenated preview today). This is the honest subset: "has anything
/// changed since the last successful render," driven by <see cref="ClipStore.OnChange"/> (marks stale)
/// and the render pipeline's own completion point (marks fresh).
/// </summary>
public sealed class PreviewFreshnessService
{
    /// <summary>False until the first successful Preview render, and after every edit since.</summary>
    public bool IsFresh { get; private set; }

    public event Action? OnChanged;

    public void MarkStale()
    {
        if (!IsFresh) return;
        IsFresh = false;
        OnChanged?.Invoke();
    }

    public void MarkFresh()
    {
        if (IsFresh) return;
        IsFresh = true;
        OnChanged?.Invoke();
    }
}
