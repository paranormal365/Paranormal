namespace Ben.Video.Editor.Models;

/// <summary>
/// What a media-bin card says about one source: how long it is, where it is used, and whether its
/// bytes are actually here.
/// </summary>
/// <remarks>
/// <para><b>V-4 of the 2026-09-06 evaluation.</b> The Video bin marked two clips "on timeline"
/// while Live playback said "2 clips are missing their media, so they play as black here". Both
/// were reading the same project. The bin simply never asked the question — its label was built
/// from the duration and the placement count, and <see cref="TrackItem.IsMediaMissing"/> was not
/// part of it.</para>
///
/// <para>That is the worse half of a disagreement: the bin is where somebody looks to see what
/// the project HAS, so a card reading "on timeline" is a promise that the thing will render. The
/// warning existed on the clip itself and on the preview; the one screen whose job is inventory
/// was the one that did not carry it.</para>
///
/// <para>A plain function of three values, so it can be tested without a project, a store or a
/// browser — the bins are raw <c>RenderTreeBuilder</c> code and nothing about them is reachable
/// from a test.</para>
/// </remarks>
public static class BinCardState
{
    /// <summary>The line under a bin card's name.</summary>
    /// <param name="duration">The clip's own duration, already formatted.</param>
    /// <param name="timesOnTimeline">How many times this source is placed.</param>
    /// <param name="mediaMissing">Whether the bytes this card stands for are absent.</param>
    /// <remarks>
    /// <para>Missing comes FIRST and replaces the placement count rather than joining it. "on
    /// timeline · media missing" reads as two facts of equal weight; the truth is that the
    /// placement does not matter while there is nothing to play, and the reader's next action is
    /// to find the file, not to look at the timeline.</para>
    ///
    /// <para>The duration is kept in every case. It is what the card was opened for, and a clip
    /// whose media is missing still knows how long it was.</para>
    /// </remarks>
    public static string Meta(string duration, int timesOnTimeline, bool mediaMissing)
    {
        if (mediaMissing) return $"{duration} · media missing";

        return timesOnTimeline switch
        {
            0 => duration,
            1 => $"{duration} · on timeline",
            _ => $"{duration} · on timeline ×{timesOnTimeline}",
        };
    }

    /// <summary>Whether the card should be marked as a warning rather than as ordinary status.</summary>
    /// <remarks>
    /// Separate from <see cref="Meta"/> because the two have different jobs: one is the words, the
    /// other is whether they are coloured. A caller that shows the words and forgets the colour is
    /// still telling the truth.
    /// </remarks>
    public static bool IsWarning(bool mediaMissing) => mediaMissing;
}
