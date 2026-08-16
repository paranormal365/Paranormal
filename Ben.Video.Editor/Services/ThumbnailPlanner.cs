namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 145 (symptom S3) — how many thumbnails a video clip
/// gets, split into an upfront batch (generated synchronously during import — small on purpose, so
/// import stays fast) and the full target count (filled in lazily afterward, through the same
/// worker lock, once the import's own critical path is done). Per the user's explicit decision:
/// accept a smaller initial thumbnail strip in exchange for faster imports.
///
/// <see cref="FullCount"/> preserves the exact clamp every import call site already used before
/// this phase (<c>Math.Clamp((int)(duration / 2.0), 1, 8)</c>) — centralized here instead of
/// duplicated at three call sites, and now unit-testable in isolation.
/// </summary>
public static class ThumbnailPlanner
{
    /// <summary>Upfront budget — generated synchronously, blocking the import.</summary>
    public const int UpfrontBudget = 3;

    /// <summary>The full number of thumbnails a clip of this duration should eventually have.</summary>
    public static int FullCount(double durationSeconds) => Math.Clamp((int)(durationSeconds / 2.0), 1, 8);

    /// <summary>
    /// How many to generate upfront — never more than <see cref="FullCount"/> itself (a 3s clip
    /// that only wants 1 thumbnail total shouldn't "lazily fill" zero more just because the
    /// upfront budget is 3).
    /// </summary>
    public static int UpfrontCount(double durationSeconds) => Math.Min(UpfrontBudget, FullCount(durationSeconds));
}
