namespace Ben.Video.Editor.Services;

/// <summary>
/// An existing timeline clip's position/duration plus the trim window into its source media
/// that produces that duration. <see cref="SourceEnd"/> - <see cref="SourceStart"/> should equal
/// <see cref="Duration"/> (both in on-timeline seconds — this deliberately ignores playback
/// <c>Speed</c>, matching the same simplification <c>ClipStore.SplitClip</c> already makes).
/// </summary>
public readonly record struct TrimmedSegment(double Start, double Duration, double SourceStart, double SourceEnd);

/// <summary>
/// Pure static helper that resolves what happens to existing timeline clips when a new/moved
/// clip is placed on top of them in "Overwrite" mode (item #49) — as opposed to "Insert"/ripple
/// mode (item #25 phase 106), which shifts everything after the insertion point later instead of
/// touching what's underneath.
/// Isolated from Blazor/ClipStore so it can be unit-tested without a browser.
/// </summary>
public static class OverwriteEditCalculator
{
    /// <summary>
    /// Resolves how a single existing segment is affected by a new clip occupying
    /// [<paramref name="insertStart"/>, <paramref name="insertStart"/> + <paramref name="insertDuration"/>).
    /// Returns the existing segment unchanged if there's no overlap; an empty list if it's
    /// entirely covered (removed); one shortened segment if only its start or end is trimmed
    /// away; or two segments if the new clip lands entirely inside it (split, with the covered
    /// middle portion dropped).
    /// </summary>
    public static IReadOnlyList<TrimmedSegment> Resolve(
        double insertStart,
        double insertDuration,
        TrimmedSegment existing)
    {
        var insertEnd = insertStart + insertDuration;
        var existingEnd = existing.Start + existing.Duration;

        if (existingEnd <= insertStart || existing.Start >= insertEnd)
            return [existing];

        var leftOutside  = existing.Start < insertStart;
        var rightOutside = existingEnd > insertEnd;

        if (!leftOutside && !rightOutside)
            return []; // fully covered by the new clip — removed

        if (leftOutside && !rightOutside)
        {
            // Existing clip starts before the new clip and ends within/at it — trim its end back.
            var newDuration = insertStart - existing.Start;
            return [existing with { Duration = newDuration, SourceEnd = existing.SourceStart + newDuration }];
        }

        if (!leftOutside && rightOutside)
        {
            // Existing clip starts within/at the new clip and ends after it — trim its start forward.
            var trimAmount = insertEnd - existing.Start;
            return [existing with
            {
                Start       = insertEnd,
                Duration    = existingEnd - insertEnd,
                SourceStart = existing.SourceStart + trimAmount,
            }];
        }

        // Both sides outside — the new clip lands entirely inside the existing one: split it,
        // keeping the front and back remainders, dropping the covered middle.
        var frontDuration     = insertStart - existing.Start;
        var backSourceStart   = existing.SourceStart + (insertEnd - existing.Start);
        var front = existing with { Duration = frontDuration, SourceEnd = existing.SourceStart + frontDuration };
        var back  = existing with { Start = insertEnd, Duration = existingEnd - insertEnd, SourceStart = backSourceStart };
        return [front, back];
    }
}
