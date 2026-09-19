using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>One clip that cannot be rendered, and why.</summary>
/// <param name="ClipId">The clip.</param>
/// <param name="ClipName">What it is called on the timeline, so somebody can find it.</param>
public readonly record struct ExportBlocker(Guid ClipId, string ClipName);

/// <summary>
/// Whether the timeline can actually be rendered, and what to say when it cannot.
/// </summary>
/// <param name="Blockers">Clips whose media the editor does not have.</param>
public sealed record ExportReadiness(IReadOnlyList<ExportBlocker> Blockers)
{
    /// <summary>Everything the render needs is here.</summary>
    public bool CanExport => Blockers.Count == 0;

    /// <summary>Nothing is wrong.</summary>
    public static ExportReadiness Ready { get; } = new([]);

    /// <summary>
    /// What to tell somebody, or null when there is nothing to tell them.
    /// </summary>
    /// <remarks>
    /// Names the clips rather than counting them: "one clip is missing its media" sends somebody
    /// hunting along a timeline, and the point of saying anything is that they can go and fix it.
    /// Long lists are trimmed, because a wall of names is its own kind of unhelpful.
    /// </remarks>
    public string? Explanation
    {
        get
        {
            if (CanExport) return null;

            const int shown = 3;

            var names = string.Join(", ", Blockers.Take(shown).Select(b => $"\"{b.ClipName}\""));

            var subject = Blockers.Count switch
            {
                1 => $"{names} is missing its media",
                <= shown => $"{names} are missing their media",
                _ => $"{names} and {Blockers.Count - shown} more are missing their media",
            };

            return $"{subject}. The editor cannot render a clip whose file it does not have, so the "
                 + "export would produce a video with a hole in it. Reconnect the media — right-click "
                 + "the clip and choose Replace Media — or remove the clip, then export again.";
        }
    }

    /// <summary>
    /// Checks whether every clip on the timeline still has the media it needs.
    /// </summary>
    /// <remarks>
    /// <para><b>Why up front.</b> The render used to start and then meet the missing clip partway
    /// through: it stopped at that clip's percentage and stayed there, with no message and nothing
    /// to act on, while the person watched a progress bar that had already stopped meaning anything.
    /// A render that cannot succeed is better refused before it starts, with the reason
    /// (2026-09-06 large-media walk).</para>
    ///
    /// <para><b>What counts as missing.</b> Either the editor has already noticed
    /// (<see cref="TrackItem.IsMediaMissing"/> — a project reopened on a machine whose browser
    /// storage no longer holds the file), or the clip has no mounted source for ffmpeg to read.
    /// Both end the same way at render time.</para>
    ///
    /// <para>Audio counts too. A missing audio clip was skipped silently, so an export finished
    /// looking complete and was missing its narration — which is worse than being told.</para>
    /// </remarks>
    public static ExportReadiness Check(IReadOnlyList<TimelineTrack>? tracks)
    {
        if (tracks is null || tracks.Count == 0) return Ready;

        List<ExportBlocker>? blockers = null;

        foreach (var track in tracks)
            foreach (var item in track.Items)
            {
                if (!NeedsMedia(item) || HasMedia(item)) continue;

                blockers ??= [];
                blockers.Add(new ExportBlocker(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.Name) ? "Untitled clip" : item.Name));
            }

        return blockers is null ? Ready : new ExportReadiness(blockers);
    }

    /// <summary>
    /// The kinds of item that are rendered from a file on disk.
    /// </summary>
    /// <remarks>
    /// Titles, callouts and transitions are drawn rather than read, so they have nothing to be
    /// missing. Clip art is drawn from the asset catalogue and is deliberately left out here: it
    /// re-downloads on demand rather than living in browser storage the way footage does.
    /// </remarks>
    private static bool NeedsMedia(TrackItem item) => item is VideoClip or ImageClip or AudioClip;

    private static bool HasMedia(TrackItem item) =>
        !item.IsMediaMissing && !string.IsNullOrEmpty(MemFsNameOf(item));

    private static string? MemFsNameOf(TrackItem item) => item switch
    {
        VideoClip v => v.MemFsName,
        ImageClip i => i.MemFsName,
        AudioClip a => a.MemFsName,
        _           => null,
    };
}
