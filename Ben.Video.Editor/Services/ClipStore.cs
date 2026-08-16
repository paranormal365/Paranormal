using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Microsoft.Extensions.Options;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that owns the timeline state: all tracks and their items.
/// Raises <see cref="OnChange"/> whenever tracks or items are mutated so
/// components can call StateHasChanged().
///
/// Single-track mode (default): one Video track pre-created; audio on the clip itself.
/// Multi-track mode (MultiTrack = true): multiple Video + Audio tracks supported.
/// </summary>
public sealed class ClipStore
{
    private readonly VideoEditorOptions _options;
    private readonly List<TimelineTrack>   _tracks  = [];
    private readonly List<TimelineMarker>  _markers = [];

    // Cycling colour palette for new markers (DAW-style)
    private static readonly string[] MarkerColors =
    [
        "#f59e0b", // amber
        "#3b82f6", // blue
        "#10b981", // emerald
        "#ef4444", // red
        "#a855f7", // purple
        "#f97316", // orange
    ];

    // â”€â”€ Undo / Redo stacks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const int MaxHistoryDepth = 50;
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();

    public IReadOnlyList<TimelineTrack>  Tracks  => _tracks;

    /// <summary>Next <see cref="TrackItem.LayerIndex"/> to assign to a newly-created overlay item
    /// (CalloutClip/TextOverlay/ClipArtClip) — one higher than every existing overlay item's
    /// LayerIndex, across every track, so a freshly-added layer always renders on top regardless
    /// of where on the timeline it starts. Computed on demand (not a separate counter field) so it
    /// stays correct after a project load without needing its own persisted/restored state.</summary>
    private int NextLayerIndex() =>
        _tracks.SelectMany(t => t.Items)
               .Where(i => i is CalloutClip or TextOverlay or ClipArtClip)
               .Select(i => i.LayerIndex)
               .DefaultIfEmpty(-1)
               .Max() + 1;

    /// <summary>All timeline markers sorted by <see cref="TimelineMarker.TimeSeconds"/> ascending.</summary>
    public IReadOnlyList<TimelineMarker> Markers => _markers.OrderBy(m => m.TimeSeconds).ToList();

    public event Action? OnChange;

    public ClipStore(IOptions<VideoEditorOptions> options)
    {
        _options = options.Value;
        InitializeTracks();
    }

    // â”€â”€ Initialisation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void InitializeTracks()
    {
        _tracks.Add(new TimelineTrack
        {
            Label = "Video 1",
            Type  = TrackType.Video,
            Order = 0
        });

        if (_options.AudioTracks)
        {
            _tracks.Add(new TimelineTrack
            {
                Label = "Audio 1",
                Type  = TrackType.Audio,
                Order = 1
            });
        }
    }

    // â”€â”€ Track management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Add a new video track. Requires MultiTrack feature flag.</summary>
    public TimelineTrack AddVideoTrack()
    {
        var count = _tracks.Count(t => t.Type == TrackType.Video);
        if (count >= _options.MaxVideoTracks)
            throw new InvalidOperationException($"Maximum of {_options.MaxVideoTracks} video tracks reached.");

        var track = new TimelineTrack
        {
            Label = $"Video {count + 1}",
            Type  = TrackType.Video,
            Order = _tracks.Count
        };
        PushCommand(new AddTrackCommand(_tracks, track));
        _tracks.Add(track);
        Notify();
        return track;
    }

    /// <summary>Add a new audio track. Requires AudioTracks feature flag.</summary>
    public TimelineTrack AddAudioTrack()
    {
        var count = _tracks.Count(t => t.Type == TrackType.Audio);
        if (count >= _options.MaxAudioTracks)
            throw new InvalidOperationException($"Maximum of {_options.MaxAudioTracks} audio tracks reached.");

        var track = new TimelineTrack
        {
            Label = $"Audio {count + 1}",
            Type  = TrackType.Audio,
            Order = _tracks.Count
        };
        PushCommand(new AddTrackCommand(_tracks, track));
        _tracks.Add(track);
        Notify();
        return track;
    }

    /// <summary>
    /// Set the locked state of a track. When locked, clip mutations (add, remove, move, trim,
    /// reorder, split, duplicate) are silently ignored. Pushes a <see cref="LockTrackCommand"/>
    /// for undo/redo support.
    /// </summary>
    public void LockTrack(Guid trackId, bool locked)
    {
        var track = RequireTrack(trackId);
        if (track.IsLocked == locked) return;
        PushCommand(new LockTrackCommand(track, locked));
        track.IsLocked = locked;
        Notify();
    }

    /// <summary>Remove an empty track. The primary Video 1 track cannot be removed.</summary>
    public void RemoveTrack(Guid trackId)
    {
        var track = RequireTrack(trackId);
        if (track.Order == 0 && track.Type == TrackType.Video)
            throw new InvalidOperationException("The primary video track cannot be removed.");
        var index = _tracks.IndexOf(track);
        PushCommand(new RemoveTrackCommand(_tracks, track, index, RenumberTracks));
        _tracks.Remove(track);
        RenumberTracks();
        Notify();
    }

    /// <summary>Set the display name of a track.</summary>
    public void RenameTrack(Guid trackId, string name)
    {
        var track = RequireTrack(trackId);
        var trimmed = name.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == track.Label) return;
        track.Label = trimmed;
        Notify();
    }

    /// <summary>
    /// Swap the <see cref="TimelineTrack.Order"/> of <paramref name="trackId"/> with the
    /// track currently occupying <paramref name="newOrder"/>. This changes the compositing
    /// z-order: lower Order value = higher layer = renders on top.
    /// The operation is wrapped in a <see cref="ReorderTrackCommand"/> for undo/redo.
    /// </summary>
    /// <exception cref="ArgumentException">Track not found.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="newOrder"/> is out of range.</exception>
    public void ReorderTrack(Guid trackId, int newOrder)
    {
        var track = RequireTrack(trackId);
        if (newOrder < 0 || newOrder >= _tracks.Count)
            throw new ArgumentOutOfRangeException(nameof(newOrder),
                $"newOrder {newOrder} is outside the valid range [0, {_tracks.Count - 1}].");

        if (track.Order == newOrder) return;

        // Apply the swap directly (same pattern as other ClipStore mutations)
        var displaced = _tracks.FirstOrDefault(t => t.Order == newOrder && t.Id != trackId);
        var oldOrder  = track.Order;
        if (displaced is not null)
            displaced.Order = oldOrder;
        track.Order = newOrder;

        PushCommand(new ReorderTrackCommand(_tracks, trackId, oldOrder, newOrder));
        Notify();
    }


    /// <summary>
    /// Applies the "Separate Audio" mutation synchronously: sets <see cref="VideoClip.MuteAudio"/>
    /// to <c>true</c> on the source clip and adds <paramref name="audioClip"/> to the audio track
    /// identified by <paramref name="audioTrackId"/>. Pushes a <see cref="DetachAudioCommand"/> onto the undo stack.
    /// </summary>
    public void DetachAudio(Guid videoClipId, AudioClip audioClip, Guid audioTrackId)
    {
        var sourceTrack = _tracks.FirstOrDefault(t => t.Items.Any(i => i.Id == videoClipId))
            ?? throw new ArgumentException($"VideoClip {videoClipId} not found.", nameof(videoClipId));
        var sourceClip  = sourceTrack.Items.OfType<VideoClip>().FirstOrDefault(c => c.Id == videoClipId)
            ?? throw new ArgumentException($"Item {videoClipId} is not a VideoClip.", nameof(videoClipId));
        var audioTrack  = RequireTrack(audioTrackId);

        if (sourceTrack.IsLocked) return;

        sourceClip.MuteAudio = true;
        audioClip.Order      = audioTrack.Items.Count;
        audioTrack.Items.Add(audioClip);

        PushCommand(new DetachAudioCommand(sourceClip, audioClip, audioTrack));
        Notify();
    }
    // â”€â”€ Clip management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Add a VideoClip to the primary video track (backward-compatible single-track API).
    /// </summary>
    public void AddClip(VideoClip clip) => AddClipToTrack(PrimaryVideoTrack.Id, clip);

    /// <summary>
    /// Add an <see cref="ImageClip"/> to the primary video track.
    /// Requires the <c>ImageClips</c> feature flag to be enabled.
    /// </summary>
    public void AddImageClip(ImageClip clip)
    {
        if (!_options.ImageClips)
            throw new InvalidOperationException("ImageClips feature flag is disabled.");
        var track = PrimaryVideoTrack;
        var index = track.Items.Count;
        clip.Order = index;
        track.Items.Add(clip);
        PushCommand(new AddImageClipCommand(track, clip, index));
        Notify();
    }

    // ── Callout clips ─────────────────────────────────────────────────────────

    /// <summary>Add a callout clip to the primary video track with full undo support.</summary>
    public void AddCallout(CalloutClip clip)
    {
        var track = PrimaryVideoTrack;
        if (track.IsLocked) return;
        // Always ensure geometry-based defaults are set (StartX/Y from clip.X/Y etc.)
        // Then overlay any server-provided values already in ControlPointValues
        var server = new Dictionary<string, double>(clip.ControlPointValues);
        Models.CalloutShapeRenderer.SetDefaults(clip);   // sets ALL keys from geometry
        foreach (var kv in server)                       // restore server-provided values
            clip.ControlPointValues[kv.Key] = kv.Value;
        // Insert by chronological position (not appended at Count) — same reasoning as
        // AddTransition/AddTextOverlay: the timeline's gap-rendering walks Items by Order,
        // so an out-of-order Order renders the chip at the wrong spot regardless of a
        // correctly-computed TimelinePosition.
        var index = track.Items.Count(i => i.TimelinePosition <= clip.TimelinePosition);
        clip.Order = index;
        clip.LayerIndex = NextLayerIndex();
        track.Items.Insert(index, clip);
        RenumberItems(track);
        PushCommand(new AddClipCommand(track, clip, index));
        Notify();
    }

    /// <summary>Remove a callout clip by id.</summary>
    public void RemoveCallout(Guid clipId)
    {
        foreach (var track in _tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == clipId && i is CalloutClip);
            if (idx < 0) continue;
            if (track.IsLocked) return;
            var clip = track.Items[idx];
            track.Items.RemoveAt(idx);
            RenumberItems(track);
            PushCommand(new RemoveClipCommand(track, clip, idx));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Apply a mutation to a <see cref="CalloutClip"/> in place and notify.
    /// The update is direct (no undo command). Use for interactive drag operations;
    /// commit a discrete undo entry separately if needed.
    /// </summary>
    public void UpdateCallout(Guid clipId, Action<CalloutClip> update)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<CalloutClip>().FirstOrDefault(c => c.Id == clipId);
            if (clip is null) continue;
            if (track.IsLocked) return;
            update(clip);
            Notify();
            return;
        }
    }

    /// <summary>
    /// Apply a mutation to a <see cref="CalloutClip"/> AND push an undo entry.
    /// Use this from editor panels (sliders, pickers) rather than during live drag.
    /// </summary>
    public void CommitCalloutUpdate(Guid clipId, string propertyPath, Action<CalloutClip> apply, Action<CalloutClip> revert)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<CalloutClip>().FirstOrDefault(c => c.Id == clipId);
            if (clip is null) continue;
            if (track.IsLocked) return;
            apply(clip);
            PushCommand(new CommitCalloutPropertyCommand(clip, propertyPath, apply, revert));
            Notify();
            return;
        }
    }

    // ── Clipart clips ─────────────────────────────────────────────────────────

    /// <summary>Add a clipart/catalog asset overlay clip to the primary video track with undo support.</summary>
    public void AddClipArtClip(ClipArtClip clip)
    {
        var track = PrimaryVideoTrack;
        if (track.IsLocked) return;
        var index = track.Items.Count;
        clip.Order = index;
        clip.LayerIndex = NextLayerIndex();
        track.Items.Add(clip);
        PushCommand(new AddClipCommand(track, clip, index));
        Notify();
    }

    /// <summary>Remove a <see cref="ClipArtClip"/> by id.</summary>
    public void RemoveClipArtClip(Guid clipId)
    {
        foreach (var track in _tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == clipId && i is ClipArtClip);
            if (idx < 0) continue;
            if (track.IsLocked) return;
            var clip = track.Items[idx];
            track.Items.RemoveAt(idx);
            RenumberItems(track);
            PushCommand(new RemoveClipCommand(track, clip, idx));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Apply a mutation to a <see cref="ClipArtClip"/> in place and notify.
    /// The update is direct (no undo command). Use for interactive drag operations;
    /// commit a discrete undo entry separately if needed.
    /// </summary>
    public void UpdateClipArtClip(Guid clipId, Action<ClipArtClip> update)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<ClipArtClip>().FirstOrDefault(c => c.Id == clipId);
            if (clip is null) continue;
            if (track.IsLocked) return;
            update(clip);
            Notify();
            return;
        }
    }

    /// <summary>
    /// Apply a mutation to a <see cref="ClipArtClip"/> AND push an undo entry.
    /// Use this from editor panels (sliders, pickers) or a drag gesture's final commit rather than
    /// during live drag itself — mirrors <see cref="CommitCalloutUpdate"/>.
    /// </summary>
    public void CommitClipArtUpdate(Guid clipId, string propertyPath, Action<ClipArtClip> apply, Action<ClipArtClip> revert)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<ClipArtClip>().FirstOrDefault(c => c.Id == clipId);
            if (clip is null) continue;
            if (track.IsLocked) return;
            apply(clip);
            PushCommand(new CommitClipArtPropertyCommand(clip, propertyPath, apply, revert));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Pushes a single undo/redo entry for a mutation that already happened in a different
    /// scoped service (item #63 — <c>MotionKeyframeService</c>'s keyframe-branch canvas edits:
    /// body-drag, resize/position HUD type-in, and arrow-key nudge). <paramref name="apply"/> has
    /// already been called once by the caller before pushing (matching every other Commit* method
    /// on this class); this only registers <paramref name="apply"/>/<paramref name="revert"/> for
    /// future redo/undo. Doesn't call <see cref="Notify"/> — the caller's own mutation already
    /// raised whatever change event is appropriate for the service that actually owns the data.
    /// </summary>
    public void CommitMotionKeyframeEdit(string description, Action apply, Action revert)
        => PushCommand(new CommitMotionKeyframeCommand(description, apply, revert));

    /// <summary>Remove an <see cref="ImageClip"/> by id from whichever track contains it.</summary>
    public void RemoveImageClip(Guid clipId)
    {
        foreach (var track in _tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == clipId && i is ImageClip);
            if (idx < 0) continue;
            if (track.IsLocked) return;
            var clip = (ImageClip)track.Items[idx];
            track.Items.RemoveAt(idx);
            RenumberItems(track);
            PushCommand(new RemoveImageClipCommand(track, clip, idx));
            Notify();
            return;
        }
    }

    /// <summary>Update the display duration of an <see cref="ImageClip"/> (in seconds).</summary>
    public void UpdateImageDuration(Guid clipId, double newDuration)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<ImageClip>().FirstOrDefault(c => c.Id == clipId);
            if (clip is null) continue;
            if (track.IsLocked) return;
            clip.Duration = Math.Max(0.1, newDuration);
            Notify();
            return;
        }
    }

    /// <summary>
    /// Commit a duration change to an <see cref="ImageClip"/> with undo support.
    /// Use from editor panels; the live-drag path uses <see cref="UpdateImageDuration"/> directly.
    /// </summary>
    public void CommitImageDuration(Guid clipId, double newDuration)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<ImageClip>().FirstOrDefault(c => c.Id == clipId);
            if (clip is null) continue;
            if (track.IsLocked) return;
            var oldDuration = clip.Duration;
            clip.Duration = Math.Max(0.1, newDuration);
            PushCommand(new CommitImageDurationCommand(clip, oldDuration, Math.Max(0.1, newDuration)));
            Notify();
            return;
        }
    }

    // ── Applied effects (Phase 29) ────────────────────────────────────────────────

    /// <summary>
    /// Add an effect to a clip (VideoClip or ImageClip) with full undo support.
    /// The <paramref name="effect"/> instance is created by <c>IClipEffect.CreateDefault()</c>
    /// and passed in ready to apply.
    /// </summary>
    public void AddEffect(Guid itemId, AppliedEffect effect)
    {
        var item = FindItem(itemId)
            ?? throw new InvalidOperationException($"Item '{itemId}' not found on any track.");

        var cmd = new AddEffectCommand(item, effect);
        cmd.Execute();
        PushCommand(cmd);
        Notify();
    }

    /// <summary>Remove an effect from a clip by object reference with full undo support.</summary>
    public void RemoveEffect(Guid itemId, AppliedEffect effect)
    {
        var item = FindItem(itemId)
            ?? throw new InvalidOperationException($"Item '{itemId}' not found on any track.");

        var cmd = new RemoveEffectCommand(item, effect);
        cmd.Execute();
        PushCommand(cmd);
        Notify();
    }

    /// <summary>
    /// Update a single parameter value on an applied effect with full undo support.
    /// </summary>
    public void UpdateEffectParameter(AppliedEffect effect, string key, double newValue)
    {
        var cmd = new UpdateEffectParameterCommand(effect, key, newValue);
        cmd.Execute();
        PushCommand(cmd);
        Notify();
    }

    private TrackItem? FindItem(Guid id)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == id);
            if (item is not null) return item;
        }
        return null;
    }

    /// <summary>Add any TrackItem to the specified track.</summary>
    public void AddClipToTrack(Guid trackId, TrackItem item)
    {
        var track = RequireTrack(trackId);
        if (track.IsLocked) return;
        var index = track.Items.Count;
        item.Order = index;
        track.Items.Add(item);
        PushCommand(new AddClipCommand(track, item, index));
        Notify();
    }

    /// <summary>Remove a TrackItem by id from whichever track contains it.</summary>
    /// <summary>
    /// Remove a clip and shift every subsequent clip on the same track left by the
    /// removed clip's duration, closing the gap.  Fully undoable.
    /// No-op when locked or not found.
    /// </summary>
    public void RippleDeleteClip(Guid itemId)
    {
        foreach (var track in _tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == itemId);
            if (idx < 0) continue;
            if (track.IsLocked) return;

            var item     = track.Items[idx];
            var shiftBy  = item is VideoClip vc ? vc.TrimmedDuration : item.Duration;
            var removeAt = item.TimelinePosition;

            // All items whose timeline start > removeAt are shifted left
            var shifted = track.Items
                .Where(i => i.Id != itemId && i.TimelinePosition > removeAt)
                .ToList();

            var cmd = new RippleDeleteCommand(track, item, idx, shifted, shiftBy);
            PushCommand(cmd);
            cmd.Execute();
            RenumberItems(track);
            Notify();
            return;
        }
    }

    /// <summary>
    /// Commits a pointer-drag move (position already set on <paramref name="itemId"/>)
    /// and ripples all clips that appear after the clip's new position by the same delta.
    /// If the track is locked, the position is reverted to <paramref name="originalPosition"/>.
    /// Fully undoable.
    /// </summary>
    public void RippleCommitDraggedPosition(Guid itemId, double originalPosition)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) continue;
            if (track.IsLocked)
            {
                item.TimelinePosition = originalPosition;
                Notify();
                return;
            }

            var finalPos = Math.Max(0, item.TimelinePosition);
            item.TimelinePosition = finalPos;
            var delta = finalPos - originalPosition;

            if (Math.Abs(delta) < 0.001) { Notify(); return; }

            // Ripple: shift items that are at or after the clip's new position
            var boundary = Math.Min(originalPosition, finalPos);
            var shifted  = track.Items
                .Where(i => i.Id != itemId && i.TimelinePosition >= boundary)
                .ToList();

            // Reset to original so Execute() can apply cleanly
            item.TimelinePosition = originalPosition;

            var cmd = new RippleCommitDraggedCommand(item, originalPosition, finalPos, shifted, delta);
            PushCommand(cmd);
            cmd.Execute();
            Notify();
            return;
        }
    }

    /// <summary>
    /// Adds a new clip at <paramref name="position"/>, shifting every clip on the track that
    /// starts at or after that position later by the new clip's duration — opening up room for
    /// it instead of overlapping (item #25's "make room" confirmation flow). Fully undoable.
    /// No-op when the track is locked or not found.
    /// </summary>
    public void InsertClipWithRipple(Guid trackId, TrackItem clip, double position)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || track.IsLocked) return;

        var duration = clip is VideoClip vc ? vc.TrimmedDuration : clip.Duration;
        var shifted  = track.Items.Where(i => i.TimelinePosition >= position).ToList();

        clip.TimelinePosition = position;

        var cmd = new InsertClipRippleCommand(track, clip, shifted, duration);
        PushCommand(cmd);
        cmd.Execute();
        RenumberItems(track);
        Notify();
    }

    /// <summary>
    /// Links two <see cref="TrackItem"/>s together (item #52 — J-cuts/L-cuts) — typically a
    /// <see cref="VideoClip"/> and an <see cref="AudioClip"/> from the same source take. Once
    /// linked, their relative timeline offset can be shown in the UI, and each side can still be
    /// trimmed/moved fully independently to produce a J-cut (linked partner's edit point leads)
    /// or L-cut (trails) — <b>linking never moves either clip, and later moving/trimming one side
    /// does not automatically move the other</b>. Keeping them in sync during a drag would need
    /// the same kind of cross-@foreach-loop DOM relocation that broke pointer capture for item #25
    /// (see phase 104's fix) — deliberately out of scope here; linking is purely a labeled
    /// relationship plus an offset readout. Replaces any existing link on either side. Fully
    /// undoable. No-op if either item isn't found or they're on a locked track.
    /// </summary>
    public void LinkClips(Guid itemAId, Guid itemBId)
    {
        var a = FindItem(itemAId);
        var b = FindItem(itemBId);
        if (a is null || b is null || a.Id == b.Id) return;
        if (_tracks.Any(t => t.IsLocked && t.Items.Any(i => i.Id == a.Id || i.Id == b.Id))) return;

        // Unlinking any previous partners first keeps links exclusively pairwise.
        var oldAPartner = a.LinkedClipId.HasValue ? FindItem(a.LinkedClipId.Value) : null;
        var oldBPartner = b.LinkedClipId.HasValue ? FindItem(b.LinkedClipId.Value) : null;
        if (oldAPartner is not null) oldAPartner.LinkedClipId = null;
        if (oldBPartner is not null) oldBPartner.LinkedClipId = null;

        var cmd = new LinkClipsCommand(a, b, a.LinkedClipId, b.LinkedClipId, b.Id, a.Id);
        PushCommand(cmd);
        cmd.Execute();
        Notify();
    }

    /// <summary>
    /// Removes <paramref name="itemId"/>'s link (item #52), if it has one. No-op otherwise or if
    /// the item isn't found.
    /// </summary>
    public void UnlinkClip(Guid itemId)
    {
        var item = FindItem(itemId);
        if (item?.LinkedClipId is not { } partnerId) return;
        var partner = FindItem(partnerId);
        if (partner is null) return;

        var cmd = new LinkClipsCommand(item, partner, item.LinkedClipId, partner.LinkedClipId, null, null);
        PushCommand(cmd);
        cmd.Execute();
        Notify();
    }

    /// <summary>
    /// Finds the closest not-already-linked <see cref="AudioClip"/> to <paramref name="videoClip"/>
    /// on any audio track, within <paramref name="thresholdSeconds"/> of either of its edges —
    /// the auto-detect-by-proximity pattern used elsewhere for adjacent-clip discovery (item #50's
    /// <c>RollEdit</c>/<c>SlideClip</c>), applied here to suggest a link candidate instead of
    /// requiring the user to select two clips of different types simultaneously.
    /// </summary>
    public AudioClip? FindNearbyLinkCandidate(VideoClip videoClip, double thresholdSeconds = 1.0)
    {
        var videoEnd = videoClip.TimelinePosition + videoClip.TrimmedDuration;
        return AllAudioClips
            .Where(a => a.LinkedClipId is null)
            .Select(a => (Clip: a, Distance: Math.Min(
                Math.Abs(a.TimelinePosition - videoClip.TimelinePosition),
                Math.Abs(a.TimelinePosition - videoEnd))))
            .Where(x => x.Distance <= thresholdSeconds)
            .OrderBy(x => x.Distance)
            .Select(x => x.Clip)
            .FirstOrDefault();
    }

    /// <summary>
    /// Adds a new video clip at <paramref name="position"/>, trimming, splitting, or removing
    /// whatever existing clips on the track it overlaps instead of shifting subsequent clips
    /// later (contrast <see cref="InsertClipWithRipple"/>) — standard NLE "Overwrite" edit mode
    /// (item #49). Fully undoable. No-op when the track is locked or not found.
    /// </summary>
    public void OverwriteInsert(Guid trackId, VideoClip clip, double position)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || track.IsLocked) return;

        var duration = clip.TrimmedDuration;
        var changes = new List<(VideoClip Original, List<VideoClip> Replacements)>();

        foreach (var existing in track.Items.OfType<VideoClip>().Where(c => c.Id != clip.Id))
        {
            var existingEnd = existing.EndTrim > existing.StartTrim ? existing.EndTrim : existing.Duration;
            var segment = new TrimmedSegment(
                existing.TimelinePosition, existing.TrimmedDuration, existing.StartTrim, existingEnd);

            var resolved = OverwriteEditCalculator.Resolve(position, duration, segment);
            if (resolved.Count == 1 &&
                Math.Abs(resolved[0].Start - segment.Start) < 0.0001 &&
                Math.Abs(resolved[0].Duration - segment.Duration) < 0.0001)
                continue; // untouched

            var replacements = resolved.Count switch
            {
                0 => [],
                1 => new List<VideoClip>
                {
                    existing with
                    {
                        TimelinePosition = resolved[0].Start,
                        StartTrim        = resolved[0].SourceStart,
                        EndTrim          = resolved[0].SourceEnd,
                    },
                },
                _ => new List<VideoClip>
                {
                    existing with
                    {
                        Id               = Guid.NewGuid(),
                        Name             = existing.Name + " A",
                        TimelinePosition = resolved[0].Start,
                        StartTrim        = resolved[0].SourceStart,
                        EndTrim          = resolved[0].SourceEnd,
                    },
                    existing with
                    {
                        Id               = Guid.NewGuid(),
                        Name             = existing.Name + " B",
                        TimelinePosition = resolved[1].Start,
                        StartTrim        = resolved[1].SourceStart,
                        EndTrim          = resolved[1].SourceEnd,
                    },
                },
            };

            changes.Add((existing, replacements));
        }

        var cmd = new OverwriteInsertCommand(track, clip, position, changes);
        PushCommand(cmd);
        cmd.Execute();
        Notify();
    }

    public void RemoveClip(Guid itemId)
    {
        foreach (var track in _tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == itemId);
            if (idx < 0) continue;
            if (track.IsLocked) return;
            var item = track.Items[idx];
            track.Items.RemoveAt(idx);
            RenumberItems(track);
            PushCommand(new RemoveClipCommand(track, item, idx));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Duplicate a VideoClip or AudioClip, placing the copy immediately after the original
    /// on the same track with a 0.1 s gap.  Transitions and TextOverlays are not duplicated.
    /// No-op when the id is not found or the item is not a media clip.
    /// </summary>
    public void DuplicateClip(Guid itemId)
    {
        foreach (var track in _tracks)
        {
            var original = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (original is null) continue;
            if (track.IsLocked) return;

            TrackItem? copy = original switch
            {
                VideoClip vc => vc with
                {
                    Id               = Guid.NewGuid(),
                    TimelinePosition = vc.TimelinePosition + vc.EffectiveDuration + 0.1,
                    ThumbnailUrls    = new List<string>(vc.ThumbnailUrls),
                    VolumeAutomation = new List<VolumeKeyframe>(vc.VolumeAutomation),
                    Effects          = vc.Effects with { },
                },
                AudioClip ac => ac with
                {
                    Id               = Guid.NewGuid(),
                    TimelinePosition = ac.TimelinePosition + ac.Duration + 0.1,
                    VolumeAutomation = new List<VolumeKeyframe>(ac.VolumeAutomation),
                    // BlobUrl and WaveformPeaks intentionally shared (read-only data)
                },
                _ => null,
            };

            if (copy is null) return;

            copy.Order = track.Items.Count;
            track.Items.Add(copy);
            RenumberItems(track);
            PushCommand(new AddClipCommand(track, copy, copy.Order));
            Notify();
            return;
        }
    }

    // â”€â”€ Undo / Redo public API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>True when there is at least one command on the undo stack.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>True when there is at least one command on the redo stack.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Description of the next command that will be undone, or null.</summary>
    public string? UndoDescription  => CanUndo ? _undoStack.Peek().Description : null;

    /// <summary>Description of the next command that will be re-done, or null.</summary>
    public string? RedoDescription  => CanRedo ? _redoStack.Peek().Description : null;

    /// <summary>
    /// Undo the most recently pushed command.
    /// No-op when <see cref="CanUndo"/> is false.
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        Notify();
    }

    /// <summary>
    /// Re-apply the most recently undone command.
    /// No-op when <see cref="CanRedo"/> is false.
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        Notify();
    }

    /// <summary>
    /// Backward-compatible shim â€” delegates to <see cref="Undo"/>.
    /// </summary>
    public void UndoLastRemove() => Undo();

    // â”€â”€ Internal helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void PushCommand(IEditorCommand cmd)
    {
        _undoStack.Push(cmd);
        _redoStack.Clear();
        // Trim oldest entries when the stack exceeds the depth limit
        if (_undoStack.Count > MaxHistoryDepth)
        {
            var trimmed = _undoStack.ToArray().Take(MaxHistoryDepth).Reverse();
            _undoStack.Clear();
            foreach (var c in trimmed) _undoStack.Push(c);
        }
    }

    /// <summary>Update the in/out trim points of a <see cref="VideoClip"/>.
    /// <paramref name="start"/> and <paramref name="end"/> are seconds within the source file.
    /// Clamps both values to [0, clip.Duration] and ensures start &lt; end.
    /// </summary>
    public void UpdateTrim(Guid itemId, double start, double end)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not VideoClip clip) continue;
            if (track.IsLocked) return;

            var dur = clip.Duration;
                start = Math.Clamp(start, 0, dur);
                end   = Math.Clamp(end,   0, dur);
                if (start >= end) return; // invalid range â€” ignore

                var oldStart = clip.StartTrim;
                var oldEnd   = clip.EndTrim;
                clip.StartTrim = start;
                clip.EndTrim   = end;
                PushCommand(new UpdateTrimCommand(clip, oldStart, oldEnd, start, end));
                Notify();
                return;
        }
    }

    /// <summary>
    /// <b>Slip</b> edit (item #50): shifts a clip's source-trim window by <paramref name="delta"/>
    /// seconds without moving it on the timeline or changing its on-timeline duration — changes
    /// what portion of the source media it shows. Clamped to the source media's bounds via
    /// <see cref="TrimEditCalculator.ClampSlipDelta"/>. Fully undoable. No-op when the track is
    /// locked, the clip isn't found, or there's no room to slip in the requested direction.
    /// </summary>
    public void SlipClip(Guid itemId, double delta)
    {
        foreach (var track in _tracks)
        {
            var clip = track.Items.OfType<VideoClip>().FirstOrDefault(i => i.Id == itemId);
            if (clip is null) continue;
            if (track.IsLocked) return;

            var clamped = TrimEditCalculator.ClampSlipDelta(delta, clip.StartTrim, clip.EndTrim, clip.Duration);
            if (Math.Abs(clamped) < 0.0001) return;

            var cmd = new SlipClipCommand(clip, clamped);
            PushCommand(cmd);
            cmd.Execute();
            Notify();
            return;
        }
    }

    /// <summary>
    /// <b>Roll</b> edit (item #50): moves the shared edit point between <paramref name="itemId"/>
    /// and its immediately-following (touching) clip on the same track by <paramref name="delta"/>
    /// seconds — the left clip's out-trim and the right clip's in-trim/position shift oppositely,
    /// leaving their combined span unchanged. Clamped via
    /// <see cref="TrimEditCalculator.ClampBoundaryShift"/>. Fully undoable. No-op when the track
    /// is locked, the clip isn't found, it has no immediately-following clip, or there's no room
    /// to roll in the requested direction.
    /// </summary>
    public void RollEdit(Guid itemId, double delta)
    {
        foreach (var track in _tracks)
        {
            var left = track.Items.OfType<VideoClip>().FirstOrDefault(i => i.Id == itemId);
            if (left is null) continue;
            if (track.IsLocked) return;

            var leftEnd = left.TimelinePosition + left.TrimmedDuration;
            var right = track.Items.OfType<VideoClip>()
                .FirstOrDefault(c => c.Id != left.Id && Math.Abs(c.TimelinePosition - leftEnd) < 0.001);
            if (right is null) return;

            var leftSourceDuration = left.Duration;
            var clamped = TrimEditCalculator.ClampBoundaryShift(
                delta, left.EndTrim, leftSourceDuration, left.TrimmedDuration,
                right.StartTrim, right.TrimmedDuration);
            if (Math.Abs(clamped) < 0.0001) return;

            var cmd = new RollEditCommand(left, right, clamped);
            PushCommand(cmd);
            cmd.Execute();
            Notify();
            return;
        }
    }

    /// <summary>
    /// <b>Slide</b> edit (item #50): moves <paramref name="itemId"/> along the timeline by
    /// <paramref name="delta"/> seconds without changing its own trim points, while its immediate
    /// (touching) neighbors on either side absorb the move — the previous clip's out-trim and the
    /// next clip's in-trim/position shift to compensate. Requires both neighbors to exist and be
    /// touching. Clamped via <see cref="TrimEditCalculator.ClampBoundaryShift"/>. Fully undoable.
    /// No-op when the track is locked, the clip isn't found, either neighbor is missing, or
    /// there's no room to slide in the requested direction.
    /// </summary>
    public void SlideClip(Guid itemId, double delta)
    {
        foreach (var track in _tracks)
        {
            var mid = track.Items.OfType<VideoClip>().FirstOrDefault(i => i.Id == itemId);
            if (mid is null) continue;
            if (track.IsLocked) return;

            var midEnd = mid.TimelinePosition + mid.TrimmedDuration;
            var prev = track.Items.OfType<VideoClip>()
                .FirstOrDefault(c => c.Id != mid.Id && Math.Abs(c.TimelinePosition + c.TrimmedDuration - mid.TimelinePosition) < 0.001);
            var next = track.Items.OfType<VideoClip>()
                .FirstOrDefault(c => c.Id != mid.Id && Math.Abs(c.TimelinePosition - midEnd) < 0.001);
            if (prev is null || next is null) return;

            var clamped = TrimEditCalculator.ClampBoundaryShift(
                delta, prev.EndTrim, prev.Duration, prev.TrimmedDuration,
                next.StartTrim, next.TrimmedDuration);
            if (Math.Abs(clamped) < 0.0001) return;

            var cmd = new SlideClipCommand(prev, mid, next, clamped);
            PushCommand(cmd);
            cmd.Execute();
            Notify();
            return;
        }
    }

    /// <summary>
    /// Set the trim in/out points of an <see cref="AudioClip"/>.
    /// Values are clamped to [0, Duration]; start must be less than end.
    /// </summary>
    public void UpdateAudioTrim(Guid itemId, double start, double end)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not AudioClip clip) continue;
            if (track.IsLocked) return;

            var dur = clip.Duration;
            start = Math.Clamp(start, 0, dur);
            end   = Math.Clamp(end,   0, dur);
            if (start >= end) return;

            var oldStart = clip.StartTrim;
            var oldEnd   = clip.EndTrim;
            clip.StartTrim = start;
            clip.EndTrim   = end;
            PushCommand(new UpdateAudioTrimCommand(clip, oldStart, oldEnd, start, end));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Set the fade-in and fade-out durations of an <see cref="AudioClip"/>.
    /// Values are clamped to [0, Duration/2] so fades cannot exceed half the clip length.
    /// </summary>
    public void UpdateAudioFade(Guid itemId, double fadeInSeconds, double fadeOutSeconds)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not AudioClip clip) continue;

            var maxFade = clip.Duration / 2.0;
            fadeInSeconds  = Math.Clamp(fadeInSeconds,  0, maxFade);
            fadeOutSeconds = Math.Clamp(fadeOutSeconds, 0, maxFade);

            var oldFadeIn  = clip.FadeInSeconds;
            var oldFadeOut = clip.FadeOutSeconds;
            clip.FadeInSeconds  = fadeInSeconds;
            clip.FadeOutSeconds = fadeOutSeconds;
            PushCommand(new UpdateAudioFadeCommand(clip, oldFadeIn, oldFadeOut, fadeInSeconds, fadeOutSeconds));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Re-link a clip to a new MEMFS source after project restore.
    /// Clears <see cref="TrackItem.IsMediaMissing"/> and pushes an undo command.
    /// </summary>
    public void RelinkClip(Guid itemId, string newMemFsName)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) continue;

            string? oldMemFs = item is VideoClip vc ? vc.MemFsName
                             : item is AudioClip ac ? ac.MemFsName
                             : null;

            if (item is VideoClip vc2) vc2.MemFsName = newMemFsName;
            else if (item is AudioClip ac2) ac2.MemFsName = newMemFsName;
            item.IsMediaMissing = false;

            PushCommand(new RelinkClipCommand(item, oldMemFs, newMemFsName));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Set the playback speed multiplier of a <see cref="VideoClip"/>.
    /// <paramref name="speed"/> is clamped to [0.25, 4.0].
    /// Values outside that range are silently clamped; zero or negative values are ignored.
    /// </summary>
    public void UpdateClipSpeed(Guid itemId, double speed)
    {
        if (speed <= 0) return;
        speed = Math.Clamp(speed, 0.25, 4.0);

        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not VideoClip clip) continue;

            var oldSpeed = clip.Speed;
            clip.Speed = speed;
            PushCommand(new UpdateSpeedCommand(clip, oldSpeed, speed));
            Notify();
            return;
        }
    }

    // â”€â”€ Volume automation mutations

    /// <summary>
    /// Apply per-clip visual effects (colour grading + fade in/out) to a <see cref="VideoClip"/>.
    /// Pushes an undo command so the change is reversible.
    /// </summary>
    public void UpdateClipEffects(Guid itemId, ClipEffects newEffects)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not VideoClip clip) continue;

            var oldEffects = clip.Effects;
            clip.Effects   = newEffects;
            PushCommand(new UpdateClipEffectsCommand(clip, oldEffects, newEffects));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Fade the video clip covering <paramref name="timelineSeconds"/> to black, starting at
    /// that position and finishing exactly at the clip's end. Reuses the existing
    /// <see cref="ClipEffects.FadeOutSeconds"/> mechanism (and its export rendering/undo
    /// support) rather than introducing a new transition concept — a marker is just a
    /// convenient anchor point for picking the fade-out duration.
    /// No-op when no video clip (on any video track) covers that position.
    /// </summary>
    public void ApplyFadeToBlackAt(double timelineSeconds)
    {
        var clip = VideoTracks
            .SelectMany(t => t.VideoClips)
            .FirstOrDefault(c => timelineSeconds >= c.TimelinePosition
                               && timelineSeconds <  c.TimelinePosition + c.EffectiveDuration);
        if (clip is null) return;

        var clipEnd     = clip.TimelinePosition + clip.EffectiveDuration;
        var fadeSeconds = Math.Clamp(clipEnd - timelineSeconds, 0, clip.EffectiveDuration);
        UpdateClipEffects(clip.Id, clip.Effects with { FadeOutSeconds = fadeSeconds });
    }

    /// <summary>
    /// Set the scalar volume of any clip that implements <see cref="IHasVolumeAutomation"/>.
    /// <paramref name="volume"/> is clamped to [0.0, 2.0].
    /// </summary>
    public void SetClipVolume(Guid itemId, double volume)
    {
        volume = Math.Clamp(volume, 0.0, 2.0);

        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not IHasVolumeAutomation clip) continue;

            var oldVol = clip.Volume;
            clip.Volume = volume;
            PushCommand(new UpdateVolumeCommand(clip, oldVol, volume));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Set an <see cref="AudioClip"/>'s per-channel volume balance (backlog #10) — a multiplier
    /// on top of <see cref="AudioClip.Volume"/>/automation, applied independently to the left and
    /// right channels at export. Each clamped to [0, 2] (silence to +6 dB), matching
    /// <see cref="SetClipVolume"/>.
    /// </summary>
    public void SetClipChannelVolume(Guid itemId, double left, double right)
    {
        left  = Math.Clamp(left,  0.0, 2.0);
        right = Math.Clamp(right, 0.0, 2.0);

        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not AudioClip clip) continue;

            var oldLeft  = clip.LeftVolume;
            var oldRight = clip.RightVolume;
            clip.LeftVolume  = left;
            clip.RightVolume = right;
            PushCommand(new UpdateChannelVolumeCommand(clip, oldLeft, oldRight, left, right));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Add (or replace if same position) a volume keyframe
    /// <paramref name="position"/> is clamped to [0,1]; <paramref name="volume"/> to [0,2].
    /// The keyframe list is kept sorted by Position after insertion.
    /// </summary>
    public void AddVolumeKeyframe(Guid itemId, double position, double volume)
    {
        position = Math.Clamp(position, 0.0, 1.0);
        volume   = Math.Clamp(volume,   0.0, 2.0);

        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not IHasVolumeAutomation clip) continue;

            // Replace any existing keyframe at the same position
            var existing = clip.VolumeAutomation.FirstOrDefault(k => Math.Abs(k.Position - position) < 1e-6);
            if (existing is not null)
            {
                var cmd = new UpdateVolumeKeyframeCommand(existing, existing.Position, existing.Volume, position, volume);
                PushCommand(cmd);
                cmd.Execute();
            }
            else
            {
                var kf = new VolumeKeyframe { Position = position, Volume = volume };
                var cmd = new AddVolumeKeyframeCommand(clip, kf);
                PushCommand(cmd);
                cmd.Execute();
                clip.VolumeAutomation.Sort((a, b) => a.Position.CompareTo(b.Position));
            }

            Notify();
            return;
        }
    }

    /// <summary>
    /// Update an existing volume keyframe's position and/or volume.
    /// Re-sorts the keyframe list after the position change.
    /// </summary>
    public void UpdateVolumeKeyframe(Guid itemId, Guid keyframeId, double position, double volume)
    {
        position = Math.Clamp(position, 0.0, 1.0);
        volume   = Math.Clamp(volume,   0.0, 2.0);

        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not IHasVolumeAutomation clip) continue;

            var kf = clip.VolumeAutomation.FirstOrDefault(k => k.Id == keyframeId);
            if (kf is null) return;

            PushCommand(new UpdateVolumeKeyframeCommand(kf, kf.Position, kf.Volume, position, volume));
            kf.Position = position;
            kf.Volume   = volume;
            clip.VolumeAutomation.Sort((a, b) => a.Position.CompareTo(b.Position));

            Notify();
            return;
        }
    }

    /// <summary>
    /// Remove a volume keyframe by id. No-op if the keyframe or clip is not found.
    /// </summary>
    public void RemoveVolumeKeyframe(Guid itemId, Guid keyframeId)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not IHasVolumeAutomation clip) continue;

            var kf = clip.VolumeAutomation.FirstOrDefault(k => k.Id == keyframeId);
            if (kf is not null)
            {
                PushCommand(new RemoveVolumeKeyframeCommand(clip, kf));
                clip.VolumeAutomation.RemoveAll(k => k.Id == keyframeId);
                Notify();
            }
            return;
        }
    }

    /// <summary>
    /// Remove all volume keyframes from a clip, reverting to the scalar <see cref="IHasVolumeAutomation.Volume"/>.
    /// </summary>
    public void ClearVolumeAutomation(Guid itemId)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not IHasVolumeAutomation clip) continue;

            if (clip.VolumeAutomation.Count > 0)
            {
                PushCommand(new ClearVolumeAutomationCommand(clip));
                clip.VolumeAutomation.Clear();
                Notify();
            }
            return;
        }
    }

    /// <summary>
    /// Nudge a clip's <see cref="TrackItem.TimelinePosition"/> by <paramref name="deltaSeconds"/>.
    /// Negative values move the clip earlier; clamped to 0. Undo/redo aware.
    /// </summary>
    public void MoveClip(Guid itemId, double deltaSeconds)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) continue;
            if (track.IsLocked) return;

            var cmd = new MoveClipCommand(item, deltaSeconds);
            PushCommand(cmd);
            cmd.Execute();
            Notify();
            return;
        }
    }

    /// <summary>
    /// Commits a pointer-drag move where live preview already set
    /// <see cref="TrackItem.TimelinePosition"/> during the drag.
    /// Registers a <see cref="SetClipPositionCommand"/> for undo/redo
    /// without executing it (position is already correct).
    /// If the track is locked the position is reset to <paramref name="originalPosition"/>.
    /// </summary>
    public void CommitDraggedPosition(Guid itemId, double originalPosition)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) continue;
            if (track.IsLocked)
            {
                item.TimelinePosition = originalPosition;
                Notify();
                return;
            }
            var finalPos = Math.Max(0, item.TimelinePosition);
            item.TimelinePosition = finalPos;
            if (Math.Abs(finalPos - originalPosition) >= 0.001)
                PushCommand(new SetClipPositionCommand(item, originalPosition, finalPos));
            Notify();
            return;
        }
    }

    /// <summary>
    /// Commits a pointer-drag move, optionally also moving the clip to a different track
    /// (item #25 — cross-track drag). Unlike horizontal position, the actual track-membership
    /// change happens entirely here at commit time, not during the live-preview drag: relocating
    /// a chip's underlying <see cref="TrackItem"/> into a different track's <c>Items</c> list
    /// mid-drag would move it into a different track's <c>@foreach</c> in the markup, which
    /// destroys and recreates its DOM element — breaking the pointer capture the drag depends on
    /// for the rest of the gesture (the same class of "stuck drag" bug fixed for #27, just
    /// triggered by cross-track relocation instead of native-drag hijacking). The caller only
    /// tracks which row is hovered for visual feedback during the drag; the real move happens
    /// once, here, after the pointer has already been released.
    /// </summary>
    /// <param name="itemId">The dragged item.</param>
    /// <param name="originalTrackId">The track the item was on before this drag started.</param>
    /// <param name="originalPosition">The item's position before this drag started.</param>
    /// <param name="targetTrackId">
    /// The track to move the item to, if different from <paramref name="originalTrackId"/>.
    /// Pass <c>null</c> or <paramref name="originalTrackId"/> itself for a same-track move.
    /// </param>
    public void CommitDraggedPositionAndTrack(
        Guid itemId, Guid originalTrackId, double originalPosition, Guid? targetTrackId = null)
    {
        var fromTrack = _tracks.FirstOrDefault(t => t.Id == originalTrackId);
        var item      = fromTrack?.Items.FirstOrDefault(i => i.Id == itemId);
        if (fromTrack is null || item is null) return;

        var toTrack = targetTrackId.HasValue && targetTrackId.Value != originalTrackId
            ? _tracks.FirstOrDefault(t => t.Id == targetTrackId.Value) ?? fromTrack
            : fromTrack;

        if (toTrack.IsLocked || fromTrack.IsLocked)
        {
            item.TimelinePosition = originalPosition;
            Notify();
            return;
        }

        var finalPos = Math.Max(0, item.TimelinePosition);
        item.TimelinePosition = finalPos;

        if (toTrack.Id == fromTrack.Id)
        {
            if (Math.Abs(finalPos - originalPosition) >= 0.001)
                PushCommand(new SetClipPositionCommand(item, originalPosition, finalPos));
            Notify();
            return;
        }

        fromTrack.Items.Remove(item);
        toTrack.Items.Add(item);
        PushCommand(new MoveClipToTrackCommand(fromTrack, toTrack, item, originalPosition, finalPos));
        Notify();
    }

    /// <summary>
    /// Split a <see cref="VideoClip"/>, <see cref="AudioClip"/>, or <see cref="ImageClip"/>
    /// at <paramref name="splitAt"/> seconds (relative to the clip's
    /// <see cref="TrackItem.TimelinePosition"/>). Replaces the original item with two
    /// adjacent items preserving trims/volume automation/fades as appropriate for the type.
    /// Throws <see cref="ArgumentException"/> if the item is not found, is not a splittable
    /// type, or the split point is outside its range. Undoable.
    /// </summary>
    public void SplitClip(Guid itemId, double splitAt)
    {
        foreach (var track in _tracks)
        {
            var idx = track.Items.FindIndex(i => i.Id == itemId);
            if (idx < 0) continue;
            if (track.IsLocked) return;

            var original = track.Items[idx];
            var (first, second) = original switch
            {
                VideoClip vc => SplitVideoClip(vc, idx, splitAt),
                AudioClip ac => SplitAudioClip(ac, idx, splitAt),
                ImageClip ic => SplitImageClip(ic, idx, splitAt),
                _ => throw new ArgumentException($"Clips of type {original.GetType().Name} cannot be split.", nameof(itemId)),
            };

            track.Items.RemoveAt(idx);
            track.Items.Insert(idx,     first);
            track.Items.Insert(idx + 1, second);
            RenumberItems(track);
            PushCommand(new SplitClipCommand(track, original, first, second, idx));
            Notify();
            return;
        }

        throw new ArgumentException($"Clip {itemId} not found.", nameof(itemId));
    }

    private static (TrackItem first, TrackItem second) SplitVideoClip(VideoClip original, int idx, double splitAt)
    {
        var effectiveStart = original.StartTrim;
        var effectiveEnd   = original.EndTrim > original.StartTrim ? original.EndTrim : original.Duration;
        var sourceOffset   = effectiveStart + splitAt;

        if (sourceOffset <= effectiveStart || sourceOffset >= effectiveEnd)
            throw new ArgumentOutOfRangeException(nameof(splitAt),
                "Split point must be within the trimmed region of the clip.");

        var firstDuration = sourceOffset - effectiveStart;
        var totalDuration = effectiveEnd - effectiveStart;

        var first = original with
        {
            Id               = Guid.NewGuid(),
            Name             = original.Name + " A",
            StartTrim        = effectiveStart,
            EndTrim          = sourceOffset,
            Duration         = original.Duration,
            TimelinePosition = original.TimelinePosition,
            Order            = idx,
            VolumeAutomation = RedistributeKeyframes(original.VolumeAutomation, totalDuration, firstDuration, firstHalf: true),
        };

        var second = original with
        {
            Id               = Guid.NewGuid(),
            Name             = original.Name + " B",
            StartTrim        = sourceOffset,
            EndTrim          = effectiveEnd,
            Duration         = original.Duration,
            TimelinePosition = original.TimelinePosition + firstDuration,
            Order            = idx + 1,
            VolumeAutomation = RedistributeKeyframes(original.VolumeAutomation, totalDuration, firstDuration, firstHalf: false),
        };

        return (first, second);
    }

    private static (TrackItem first, TrackItem second) SplitAudioClip(AudioClip original, int idx, double splitAt)
    {
        var effectiveStart = original.StartTrim;
        var effectiveEnd   = original.EndTrim > original.StartTrim ? original.EndTrim : original.Duration;
        var sourceOffset   = effectiveStart + splitAt;

        if (sourceOffset <= effectiveStart || sourceOffset >= effectiveEnd)
            throw new ArgumentOutOfRangeException(nameof(splitAt),
                "Split point must be within the trimmed region of the clip.");

        var firstDuration = sourceOffset - effectiveStart;
        var totalDuration = effectiveEnd - effectiveStart;

        var first = original with
        {
            Id               = Guid.NewGuid(),
            Name             = original.Name + " A",
            StartTrim        = effectiveStart,
            EndTrim          = sourceOffset,
            Duration         = original.Duration,
            TimelinePosition = original.TimelinePosition,
            Order            = idx,
            VolumeAutomation = RedistributeKeyframes(original.VolumeAutomation, totalDuration, firstDuration, firstHalf: true),
            // Fades only apply at the true edges of the source media — the new internal
            // cut point should be a hard edge, not a fade, on either side of the split.
            FadeOutSeconds   = 0,
        };

        var second = original with
        {
            Id               = Guid.NewGuid(),
            Name             = original.Name + " B",
            StartTrim        = sourceOffset,
            EndTrim          = effectiveEnd,
            Duration         = original.Duration,
            TimelinePosition = original.TimelinePosition + firstDuration,
            Order            = idx + 1,
            VolumeAutomation = RedistributeKeyframes(original.VolumeAutomation, totalDuration, firstDuration, firstHalf: false),
            FadeInSeconds    = 0,
        };

        return (first, second);
    }

    private static (TrackItem first, TrackItem second) SplitImageClip(ImageClip original, int idx, double splitAt)
    {
        if (splitAt <= 0 || splitAt >= original.Duration)
            throw new ArgumentOutOfRangeException(nameof(splitAt),
                "Split point must be within the clip's duration.");

        var first = original with
        {
            Id               = Guid.NewGuid(),
            Name             = original.Name + " A",
            Duration         = splitAt,
            TimelinePosition = original.TimelinePosition,
            Order            = idx,
        };

        var second = original with
        {
            Id               = Guid.NewGuid(),
            Name             = original.Name + " B",
            Duration         = original.Duration - splitAt,
            TimelinePosition = original.TimelinePosition + splitAt,
            Order            = idx + 1,
        };

        return (first, second);
    }

    /// <summary>
    /// Redistributes volume keyframes (normalised [0,1] within the original clip's
    /// <paramref name="totalDuration"/>) into one half of a split, renormalised to
    /// [0,1] within that half's own duration.
    /// </summary>
    private static List<VolumeKeyframe> RedistributeKeyframes(
        List<VolumeKeyframe> source, double totalDuration, double splitOffset, bool firstHalf)
    {
        if (totalDuration <= 0) return [];
        var span = firstHalf ? splitOffset : totalDuration - splitOffset;
        if (span <= 0) return [];

        var result = new List<VolumeKeyframe>();
        foreach (var kf in source)
        {
            var absPos = kf.Position * totalDuration;
            var belongsToThisHalf = firstHalf ? absPos <= splitOffset : absPos > splitOffset;
            if (!belongsToThisHalf) continue;

            var newPos = firstHalf ? absPos / span : (absPos - splitOffset) / span;
            result.Add(new VolumeKeyframe { Position = Math.Clamp(newPos, 0, 1), Volume = kf.Volume });
        }
        return result;
    }

    /// <summary>Replace the ordered items on a track (used by drag-to-reorder).</summary>
    public void ReorderTrackItems(Guid trackId, IEnumerable<TrackItem> ordered)
    {
        var track = RequireTrack(trackId);
        if (track.IsLocked) return;
        track.Items.Clear();
        track.Items.AddRange(ordered);
        RenumberItems(track);
        Notify();
    }

    /// <summary>
    /// Backward-compatible reorder for single-track mode â€” reorders the primary video track.
    /// </summary>
    public void ReorderClips(IEnumerable<VideoClip> ordered) =>
        ReorderTrackItems(PrimaryVideoTrack.Id, ordered);

    // â”€â”€ Transition management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Insert a transition between two adjacent clips on the same track.
    /// Requires Transitions feature flag.
    /// </summary>
    public void AddTransition(Guid trackId, Guid fromClipId, Guid toClipId,
                               TransitionStyle style, double durationSeconds)
    {
        var track = RequireTrack(trackId);
        var from  = track.Items.FirstOrDefault(i => i.Id == fromClipId)
                    ?? throw new ArgumentException("fromClipId not found on track.");
        var to    = track.Items.FirstOrDefault(i => i.Id == toClipId)
                    ?? throw new ArgumentException("toClipId not found on track.");

        // Use TrimmedDuration (not the raw, untrimmed source Duration) for VideoClip —
        // otherwise a transition placed after a split/trimmed piece lands far past
        // where that piece actually ends on the timeline (same pitfall as
        // TimelineTrack.TotalDuration).
        var fromEndSeconds = from.TimelinePosition + (from is VideoClip fromVc ? fromVc.TrimmedDuration : from.Duration);
        var transition = new Transition
        {
            Name             = $"{style}",
            Style            = style,
            FromClipId       = fromClipId,
            ToClipId         = toClipId,
            TimelinePosition = fromEndSeconds - (durationSeconds / 2),
            Duration         = durationSeconds,
        };
        // Insert right before `to` (not appended last) — Order must reflect chronological
        // position, not insertion order: the timeline UI walks Items sorted by Order to
        // compute each chip's rendered gap from the previous chip's end, so an
        // Order=Count transition centred between two earlier clips would render after
        // everything else instead of at its true (and correctly-computed) TimelinePosition.
        var index = track.Items.IndexOf(to);
        transition.Order = index;
        track.Items.Insert(index, transition);
        RenumberItems(track);
        PushCommand(new AddClipCommand(track, transition, index));
        Notify();
    }

    /// <summary>
    /// Insert a crossfade transition between two <see cref="VideoClip"/>s that live on
    /// <em>different</em> video tracks and overlap in time (e.g. a clip on a higher track
    /// dropped on top of part of a clip on a lower track). The overlap window itself becomes
    /// the transition's duration and position — unlike <see cref="AddTransition"/>, the caller
    /// does not choose a duration. "From"/"to" is determined by track <see cref="TimelineTrack.Order"/>
    /// (lower Order = from, higher Order = to), not by argument order, so it always reads as
    /// "the lower track's clip transitions into the higher track's clip" regardless of which
    /// clip id is passed first.
    /// Requires the Transitions feature flag.
    /// </summary>
    public void AddCrossTrackTransition(Guid clipAId, Guid clipBId, TransitionStyle style = TransitionStyle.Fade)
    {
        var trackA = FindTrackOf(clipAId) ?? throw new ArgumentException("clipAId not found on any track.");
        var trackB = FindTrackOf(clipBId) ?? throw new ArgumentException("clipBId not found on any track.");

        if (trackA.Id == trackB.Id)
            throw new InvalidOperationException(
                "Both clips are on the same track — use AddTransition for same-track transitions.");

        var clipA = trackA.Items.First(i => i.Id == clipAId) as VideoClip
                    ?? throw new ArgumentException("clipAId must be a VideoClip.");
        var clipB = trackB.Items.First(i => i.Id == clipBId) as VideoClip
                    ?? throw new ArgumentException("clipBId must be a VideoClip.");

        // Sort by track Order so "from"/"to" reflects "lower track to higher track" regardless
        // of which clip the caller happened to pass first.
        var (fromTrack, fromClip, toTrack, toClip) = trackA.Order < trackB.Order
            ? (trackA, clipA, trackB, clipB)
            : (trackB, clipB, trackA, clipA);

        var fromEnd = fromClip.TimelinePosition + fromClip.TrimmedDuration;
        var toEnd   = toClip.TimelinePosition + toClip.TrimmedDuration;
        var overlapStart = Math.Max(fromClip.TimelinePosition, toClip.TimelinePosition);
        var overlapEnd   = Math.Min(fromEnd, toEnd);
        if (overlapEnd <= overlapStart)
            throw new ArgumentException("Clips do not overlap in time.");

        var transition = new Transition
        {
            Name             = $"{style}",
            Style            = style,
            FromClipId       = fromClip.Id,
            ToClipId         = toClip.Id,
            TimelinePosition = overlapStart,
            Duration         = overlapEnd - overlapStart,
        };
        // Same chronological-insertion-index pattern as AddTransition/AddTextOverlay/AddCallout —
        // lives on the "to" (higher) track since that's the track it visually hands off to.
        var index = toTrack.Items.Count(i => i.TimelinePosition <= transition.TimelinePosition);
        transition.Order = index;
        toTrack.Items.Insert(index, transition);
        RenumberItems(toTrack);
        PushCommand(new AddClipCommand(toTrack, transition, index));
        Notify();
    }

    /// <summary>
    /// Update the style and/or duration of an existing transition.
    /// Recalculates <see cref="Transition.TimelinePosition"/> to keep it centred between the two clips.
    /// </summary>
    public void UpdateTransition(Guid transitionId, TransitionStyle style, double durationSeconds)
    {
        if (durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be positive.");

        Transition? transition = null;
        TrackItem?  fromItem   = null;

        foreach (var track in Tracks)
        {
            transition = track.Items.OfType<Transition>().FirstOrDefault(t => t.Id == transitionId);
            if (transition is not null)
            {
                fromItem = track.Items.FirstOrDefault(i => i.Id == transition.FromClipId);
                break;
            }
        }

        if (transition is null)
            throw new ArgumentException("Transition not found.", nameof(transitionId));

        var command = BuildTransitionStyleUpdate(transition, fromItem, style, durationSeconds);
        PushCommand(command);
        Notify();
    }

    /// <summary>
    /// Item #57 T4 — applies one style to every transition on the timeline (same-track and
    /// cross-track), keeping each transition's own current duration unchanged. A single undo
    /// entry reverts the whole batch, not one click per junction touched.
    /// </summary>
    public void ApplyStyleToAllTransitions(TransitionStyle style)
    {
        var commands = new List<IEditorCommand>();
        foreach (var transition in AllTransitions.ToList())
        {
            var fromTrack = FindTrackOf(transition.FromClipId);
            var fromItem  = fromTrack?.Items.FirstOrDefault(i => i.Id == transition.FromClipId);
            commands.Add(BuildTransitionStyleUpdate(transition, fromItem, style, transition.Duration));
        }
        if (commands.Count == 0) return;

        PushCommand(new CompositeCommand($"Apply {style} to all transitions", commands));
        Notify();
    }

    /// <summary>
    /// Shared mutation behind <see cref="UpdateTransition"/> and
    /// <see cref="ApplyStyleToAllTransitions"/> — applies the new style/duration to
    /// <paramref name="transition"/>, recentres it on <paramref name="fromItem"/>'s end (if
    /// resolvable), and returns the built (not yet pushed) undo command so callers can either
    /// push it singly or bundle several into one <see cref="CompositeCommand"/>.
    /// </summary>
    private static UpdateTransitionCommand BuildTransitionStyleUpdate(
        Transition transition, TrackItem? fromItem, TransitionStyle style, double durationSeconds)
    {
        var oldStyle    = transition.Style;
        var oldDuration = transition.Duration;
        var oldName     = transition.Name;
        var oldPosition = transition.TimelinePosition;

        transition.Style    = style;
        transition.Duration = durationSeconds;
        transition.Name     = $"{style}";

        if (fromItem is not null)
        {
            var fromEndSeconds = fromItem.TimelinePosition + (fromItem is VideoClip fromVc ? fromVc.TrimmedDuration : fromItem.Duration);
            transition.TimelinePosition = fromEndSeconds - (durationSeconds / 2);
        }

        return new UpdateTransitionCommand(
            transition,
            oldStyle, oldDuration, oldName, oldPosition,
            transition.Style, transition.Duration, transition.Name, transition.TimelinePosition);
    }

    /// <summary>Remove a transition by id.</summary>
    public void RemoveTransition(Guid transitionId) => RemoveClip(transitionId);

    // â”€â”€ Text overlay management â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Add a text overlay that spans a time range across the whole composition.
    /// Requires TextOverlays feature flag.
    /// Overlays are stored on the primary video track by convention.
    /// </summary>
    /// <remarks>
    /// Doesn't go through the generic append-only <see cref="AddClipToTrack"/> —
    /// overlays are typically added with a TimelinePosition earlier than existing
    /// video clips (e.g. 0, to sit over the start of the video), and Order must
    /// reflect chronological position for the timeline UI's gap rendering to place
    /// the chip correctly (see the same fix in <see cref="AddTransition"/>).
    /// </remarks>
    public void AddTextOverlay(TextOverlay overlay)
    {
        var track = PrimaryVideoTrack;
        if (track.IsLocked) return;
        var index = track.Items.Count(i => i.TimelinePosition <= overlay.TimelinePosition);
        overlay.Order = index;
        overlay.LayerIndex = NextLayerIndex();
        track.Items.Insert(index, overlay);
        RenumberItems(track);
        PushCommand(new AddClipCommand(track, overlay, index));
        Notify();
    }

    /// <summary>
    /// Update all editable properties of an existing text overlay and notify subscribers.
    /// </summary>
    public void UpdateTextOverlay(TextOverlay updated)
    {
        var existing = _tracks
            .SelectMany(t => t.Items)
            .OfType<TextOverlay>()
            .FirstOrDefault(o => o.Id == updated.Id)
            ?? throw new ArgumentException("TextOverlay not found.", nameof(updated));

        existing.Text             = updated.Text;
        existing.FontFamily       = updated.FontFamily;
        existing.FontSize         = updated.FontSize;
        existing.FontColor        = updated.FontColor;
        existing.FontBold         = updated.FontBold;
        existing.FontUnderline    = updated.FontUnderline;
        existing.Runs             = updated.Runs;
        existing.BoxColor         = updated.BoxColor;
        existing.HorizontalAlign  = updated.HorizontalAlign;
        existing.VerticalAlign    = updated.VerticalAlign;
        existing.OffsetX          = updated.OffsetX;
        existing.OffsetY          = updated.OffsetY;
        existing.OverrideX        = updated.OverrideX;
        existing.OverrideY        = updated.OverrideY;
        existing.FadeInSeconds    = updated.FadeInSeconds;
        existing.FadeOutSeconds   = updated.FadeOutSeconds;
        existing.Opacity          = updated.Opacity;
        existing.ShadowColor      = updated.ShadowColor;
        existing.ShadowOffsetX    = updated.ShadowOffsetX;
        existing.ShadowOffsetY    = updated.ShadowOffsetY;
        existing.ShadowBlur       = updated.ShadowBlur;
        existing.TimelinePosition = updated.TimelinePosition;
        existing.Duration         = updated.Duration;
        existing.Name             = updated.Name;

        Notify();
    }

    /// <summary>Remove a text overlay by id.</summary>
    public void RemoveTextOverlay(Guid overlayId) => RemoveClip(overlayId);

    // â”€â”€ Convenience accessors â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>The first (primary) video track â€” always present.</summary>
    public TimelineTrack PrimaryVideoTrack =>
        _tracks.First(t => t.Type == TrackType.Video);

    /// <summary>All video clips on the primary track (single-track backward compat).</summary>
    public IReadOnlyList<VideoClip> Clips =>
        PrimaryVideoTrack.VideoClips.ToList();

    /// <summary>All video clips across every video track, ordered by track then clip.</summary>
    public IEnumerable<VideoClip> AllVideoClips =>
        VideoTracks.SelectMany(t => t.VideoClips);

    /// <summary>All audio clips across every audio track, ordered by track then clip.</summary>
    public IEnumerable<AudioClip> AllAudioClips =>
        AudioTracks.SelectMany(t => t.AudioClips);

    /// <summary>All image clips across every video track, ordered by track then clip.</summary>
    public IEnumerable<ImageClip> AllImageClips =>
        VideoTracks.SelectMany(t => t.ImageClips);

    /// <summary>All callout clips across every video track, ordered by track then clip.</summary>
    public IEnumerable<CalloutClip> AllCalloutClips =>
        VideoTracks.SelectMany(t => t.CalloutClips);

    /// <summary>All clipart/catalog asset overlay clips across every video track.</summary>
    public IEnumerable<ClipArtClip> AllClipArtClips =>
        VideoTracks.SelectMany(t => t.ClipArtClips);

    /// <summary>All video tracks ordered by track.Order.</summary>
    public IEnumerable<TimelineTrack> VideoTracks =>
        _tracks.Where(t => t.Type == TrackType.Video).OrderBy(t => t.Order);

    /// <summary>All audio tracks ordered by track.Order.</summary>
    public IEnumerable<TimelineTrack> AudioTracks =>
        _tracks.Where(t => t.Type == TrackType.Audio).OrderBy(t => t.Order);

    /// <summary>All text overlays across all tracks.</summary>
    public IEnumerable<TextOverlay> AllTextOverlays =>
        _tracks.SelectMany(t => t.TextOverlays);

    /// <summary>All transitions across all tracks.</summary>
    public IEnumerable<Transition> AllTransitions =>
        _tracks.SelectMany(t => t.Transitions);

    /// <summary>Maximum total duration across all tracks in seconds.</summary>
    public double TotalDuration =>
        _tracks.Count == 0 ? 0 : _tracks.Max(t => t.TotalDuration);

    /// <summary>The track that contains the given item id, or null if not found on any track.</summary>
    public TimelineTrack? FindTrackOf(Guid itemId) =>
        _tracks.FirstOrDefault(t => t.Items.Any(i => i.Id == itemId));

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private TimelineTrack RequireTrack(Guid trackId) =>
        _tracks.FirstOrDefault(t => t.Id == trackId)
        ?? throw new ArgumentException($"Track {trackId} not found.", nameof(trackId));

    /// <summary>
    /// Re-derives every item's <see cref="TrackItem.Order"/> from its index in
    /// <see cref="TimelineTrack.Items"/>.
    ///
    /// <para><b>Mutates in place</b> — deliberately, and this matters (item #59). It previously
    /// did <c>track.Items[i] = track.Items[i] with { Order = i }</c>, which replaces every entry
    /// with a *copy*, silently orphaning any reference held elsewhere to the original instance.
    /// Every <see cref="IEditorCommand"/> holds exactly such references (the item it added, the
    /// list of items it shifted) to undo itself with, so any command followed by a renumber had
    /// its undo quietly detached from the track: <c>InsertClipRippleCommand.Undo</c> un-shifted
    /// orphaned objects while the clips actually on the track kept their shifted positions
    /// forever. Records give <see cref="TrackItem"/> value equality, so even
    /// <c>Items.Remove(item)</c> kept working by value — which is why this hid for so long, and
    /// why the pre-existing undo test passed (it asserted on the caller's own now-orphaned
    /// reference rather than through the track).</para>
    /// </summary>
    private static void RenumberItems(TimelineTrack track)
    {
        for (var i = 0; i < track.Items.Count; i++)
            track.Items[i].Order = i;
    }

    private void RenumberTracks()
    {
        for (var i = 0; i < _tracks.Count; i++)
            _tracks[i].Order = i;
    }

    private bool _suppressNotify;
    private bool _pendingNotify;

    private void Notify()
    {
        if (_suppressNotify) { _pendingNotify = true; return; }
        OnChange?.Invoke();
    }

    /// <summary>Explicitly raise OnChange â€” for external callers that mutate track state directly (e.g. IsMuted toggle).</summary>
    public void NotifyChanged() => Notify();

    /// <summary>
    /// Item #59-#65 flakiness investigation, phase 145 — coalesces every OnChange raised while the
    /// returned scope is alive into at most one, fired when it's disposed. A multi-file import loop
    /// (ClipBrowser.OnFileInputChangeAsync) used to add one clip per iteration, each firing
    /// OnChange — which VideoEditor's auto-preview debounce absorbed correctly, but still meant N
    /// redundant reschedules for an N-file batch instead of exactly one refresh after the whole
    /// batch lands. Nest-safe: only the outermost scope actually suppresses/flushes; an inner
    /// using block is a no-op both ways.
    /// </summary>
    public IDisposable BeginBatch()
    {
        if (_suppressNotify) return NoOpBatchScope.Instance; // already inside an outer batch
        _suppressNotify = true;
        return new BatchScope(this);
    }

    private void EndBatch()
    {
        _suppressNotify = false;
        if (_pendingNotify)
        {
            _pendingNotify = false;
            Notify();
        }
    }

    private sealed class BatchScope(ClipStore store) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            store.EndBatch();
        }
    }

    private sealed class NoOpBatchScope : IDisposable
    {
        public static readonly NoOpBatchScope Instance = new();
        public void Dispose() { }
    }

    // â”€â”€ Marker mutations â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Add a new marker at <paramref name="timeSeconds"/>.
    /// The colour is chosen by cycling through the preset palette.
    /// <paramref name="label"/> defaults to the HH:MM:SS.f timecode when empty.
    /// Does nothing if the Markers feature flag is disabled.
    /// </summary>
    public TimelineMarker? AddMarker(double timeSeconds, string? label = null)
    {
        if (!_options.Markers) return null;
        timeSeconds = Math.Max(0, timeSeconds);

        var color = MarkerColors[_markers.Count % MarkerColors.Length];
        var ts    = TimeSpan.FromSeconds(timeSeconds);
        var defaultLabel = ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss\.f")
            : ts.ToString(@"m\:ss\.f");

        var marker = new TimelineMarker
        {
            Label       = string.IsNullOrWhiteSpace(label) ? defaultLabel : label,
            TimeSeconds = timeSeconds,
            Color       = color
        };

        _markers.Add(marker);
        PushCommand(new AddMarkerCommand(_markers, marker));
        Notify();
        return marker;
    }

    /// <summary>
    /// Update the <paramref name="label"/> and/or <paramref name="timeSeconds"/> of an existing marker.
    /// No-op if the marker id is not found.
    /// </summary>
    public void UpdateMarker(Guid markerId, string label, double timeSeconds)
    {
        var marker = _markers.FirstOrDefault(m => m.Id == markerId);
        if (marker is null) return;

        var fromLabel = marker.Label;
        var fromTime  = marker.TimeSeconds;
        var toLabel   = string.IsNullOrWhiteSpace(label) ? fromLabel : label.Trim();
        var toTime    = Math.Max(0, timeSeconds);
        if (fromLabel == toLabel && Math.Abs(fromTime - toTime) < 0.001) return;

        marker.Label       = toLabel;
        marker.TimeSeconds = toTime;
        PushCommand(new UpdateMarkerCommand(marker, fromLabel, fromTime, toLabel, toTime));
        Notify();
    }

    /// <summary>
    /// Commits a marker's position after a ruler drag. <paramref name="originalTime"/> is the
    /// position captured at drag-start; the live drag itself mutates <see cref="TimelineMarker.TimeSeconds"/>
    /// directly (for immediate visual feedback), so this only needs to clamp the final value and
    /// push a single undo entry — same pattern as <see cref="CommitDraggedPosition"/> for clips.
    /// No-op (no undo entry) if the position didn't actually change.
    /// </summary>
    public void CommitMarkerPosition(Guid markerId, double originalTime)
    {
        var marker = _markers.FirstOrDefault(m => m.Id == markerId);
        if (marker is null) return;

        var finalTime = Math.Max(0, marker.TimeSeconds);
        marker.TimeSeconds = finalTime;
        if (Math.Abs(finalTime - originalTime) >= 0.001)
            PushCommand(new UpdateMarkerCommand(marker, marker.Label, originalTime, marker.Label, finalTime));
        Notify();
    }

    /// <summary>
    /// Remove a marker by id. No-op if the id is not found.
    /// </summary>
    public void RemoveMarker(Guid markerId)
    {
        var marker = _markers.FirstOrDefault(m => m.Id == markerId);
        if (marker is null) return;
        _markers.Remove(marker);
        PushCommand(new RemoveMarkerCommand(_markers, marker));
        Notify();
    }

    // â”€â”€ Project restore â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Replace all timeline state with the content of a deserialized project file.
    /// All clips are marked <see cref="TrackItem.IsMediaMissing"/> = <c>true</c> because
    /// media files cannot be embedded in the project file and must be re-linked by the user.
    /// The undo/redo stacks are cleared so the restored state is the new baseline.
    /// </summary>
    /// <summary>
    /// Clear all tracks, clips, markers, and the undo/redo stacks.
    /// Used when starting a new project (File → New).
    /// </summary>
    public void Reset()
    {
        _tracks.Clear();
        _markers.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        // Re-add the mandatory primary video track
        _tracks.Add(new TimelineTrack
        {
            Label = "Video 1",
            Type  = TrackType.Video,
            Order = 0
        });
        if (_options.AudioTracks)
            _tracks.Add(new TimelineTrack
            {
                Label = "Audio 1",
                Type  = TrackType.Audio,
                Order = 1
            });
        Notify();
    }

    public void ReplaceFromProject(ProjectFile project)
    {
        _tracks.Clear();
        _markers.Clear();
        _undoStack.Clear();
        _redoStack.Clear();

        foreach (var pt in project.Tracks.OrderBy(t => t.Order))
        {
            var track = new TimelineTrack
            {
                Id       = pt.Id,
                Label    = pt.Label,
                Type     = pt.Type,
                Order    = pt.Order,
                IsMuted  = pt.IsMuted,
                IsLocked = pt.IsLocked,
            };

            foreach (var pv in pt.VideoClips.OrderBy(c => c.Order))
                track.Items.Add(RestoreVideoClip(pv));

            foreach (var pa in pt.AudioClips.OrderBy(c => c.Order))
                track.Items.Add(RestoreAudioClip(pa));

            foreach (var px in pt.Transitions.OrderBy(t => t.TimelinePosition))
                track.Items.Add(RestoreTransition(px));

            foreach (var po in pt.TextOverlays.OrderBy(o => o.TimelinePosition))
                track.Items.Add(RestoreTextOverlay(po));

            foreach (var pi in pt.ImageClips.OrderBy(c => c.Order))
                track.Items.Add(RestoreImageClip(pi));

            foreach (var pc in pt.CalloutClips.OrderBy(c => c.Order))
                track.Items.Add(RestoreCalloutClip(pc));

            foreach (var pa in pt.ClipArtClips.OrderBy(c => c.Order))
                track.Items.Add(RestoreClipArtClip(pa));

            _tracks.Add(track);
        }

        foreach (var m in project.Markers)
            _markers.Add(m);

        NormalizeLayerIndices();
        Notify();
    }

    /// <summary>
    /// Renumbers every overlay item's <see cref="TrackItem.LayerIndex"/> to a dense 0..N-1
    /// sequence (stable — ties broken by <see cref="TrackItem.Order"/>), across all tracks.
    /// Called after loading a project: older saved projects have no <c>LayerIndex</c> at all
    /// (JSON deserializes it to the default 0 for every item), which would collapse every
    /// overlay onto the same stack row instead of each getting its own. Safe to run on
    /// already-correct data too — a strictly-increasing sequence renumbers to itself.
    /// </summary>
    private void NormalizeLayerIndices()
    {
        var overlays = _tracks
            .SelectMany(t => t.Items)
            .Where(i => i is CalloutClip or TextOverlay or ClipArtClip)
            .OrderBy(i => i.LayerIndex)
            .ThenBy(i => i.Order)
            .ToList();

        for (var i = 0; i < overlays.Count; i++)
            overlays[i].LayerIndex = i;
    }

    private static VideoClip RestoreVideoClip(ProjectVideoClip p) => new()
    {
        Id               = p.Id,
        Name             = p.Name,
        TimelinePosition = p.TimelinePosition,
        Duration         = p.Duration,
        Order            = p.Order,
        StartTrim        = p.StartTrim,
        EndTrim          = p.EndTrim,
        Speed            = p.Speed,
        Width            = p.Width,
        Height           = p.Height,
        Volume           = p.Volume,
        VolumeAutomation = p.VolumeAutomation,
        Effects          = p.Effects,
        AppliedEffects   = p.AppliedEffects.Select(e => new AppliedEffect
        {
            EffectId   = e.EffectId,
            Parameters = new Dictionary<string, double>(e.Parameters),
        }).ToList(),
        IsMediaMissing   = true,
        OriginalFileName = p.OriginalFileName,
        OpfsExt          = p.OpfsExt,
    };

    private static AudioClip RestoreAudioClip(ProjectAudioClip p) => new()
    {
        Id               = p.Id,
        Name             = p.Name,
        TimelinePosition = p.TimelinePosition,
        Duration         = p.Duration,
        Order            = p.Order,
        StartTrim        = p.StartTrim,
        EndTrim          = p.EndTrim,
        Volume           = p.Volume,
        FadeInSeconds    = p.FadeInSeconds,
        FadeOutSeconds   = p.FadeOutSeconds,
        VolumeAutomation = p.VolumeAutomation,
        LeftVolume       = p.LeftVolume,
        RightVolume      = p.RightVolume,
        IsMediaMissing   = true,
        OriginalFileName = p.OriginalFileName,
        OpfsExt          = p.OpfsExt,
    };

    private static Transition RestoreTransition(ProjectTransition p) => new()
    {
        Id               = p.Id,
        Name             = p.Name,
        TimelinePosition = p.TimelinePosition,
        Duration         = p.Duration,
        Order            = p.Order,
        Style            = p.Style,
        FromClipId       = p.FromClipId,
        ToClipId         = p.ToClipId,
    };

    private static TextOverlay RestoreTextOverlay(ProjectTextOverlay p)
    {
        var boxColor = p.HasBackground
            ? $"{p.BackgroundColor}@{p.BackgroundOpacity:F2}"
            : null;

        var hAlign = Enum.TryParse<TextHorizontalAlign>(p.HorizontalAlign, true, out var h)
            ? h : TextHorizontalAlign.Center;
        var vAlign = Enum.TryParse<TextVerticalAlign>(p.VerticalAlign, true, out var v)
            ? v : TextVerticalAlign.Bottom;

        return new TextOverlay
        {
            Id               = p.Id,
            Name             = p.Name,
            TimelinePosition = p.TimelinePosition,
            Duration         = p.Duration,
            Order            = p.Order,
            LayerIndex       = p.LayerIndex,
            Text             = p.Text,
            FontFamily       = p.FontFamily,
            FontSize         = p.FontSize,
            FontColor        = p.FontColor,
            FontBold         = p.FontBold,
            FontUnderline    = p.FontUnderline,
            Runs             = p.Runs?.Select(RestoreTextRun).ToList(),
            BoxColor         = boxColor,
            HorizontalAlign  = hAlign,
            VerticalAlign    = vAlign,
            OffsetX          = p.OffsetX,
            OffsetY          = p.OffsetY,
            OverrideX        = p.OverrideX,
            OverrideY        = p.OverrideY,
            FadeInSeconds    = p.FadeInSeconds,
            FadeOutSeconds   = p.FadeOutSeconds,
            Opacity          = p.Opacity,
            ShadowColor      = p.ShadowColor,
            ShadowOffsetX    = p.ShadowOffsetX,
            ShadowOffsetY    = p.ShadowOffsetY,
            ShadowBlur       = p.ShadowBlur,
        };
    }

    private static ImageClip RestoreImageClip(ProjectImageClip p) => new()
    {
        Id               = p.Id,
        Name             = p.Name,
        TimelinePosition = p.TimelinePosition,
        Duration         = p.Duration,
        Order            = p.Order,
        Width            = p.Width,
        Height           = p.Height,
        Effects          = p.Effects,
        AppliedEffects   = p.AppliedEffects.Select(e => new AppliedEffect
        {
            EffectId   = e.EffectId,
            Parameters = new Dictionary<string, double>(e.Parameters),
        }).ToList(),
        IsMediaMissing   = true,
        OriginalFileName = p.OriginalFileName,
        OpfsExt          = p.OpfsExt,
    };

    private static CalloutClip RestoreCalloutClip(ProjectCalloutClip p) => new()
    {
        Id               = p.Id,
        Name             = p.Name,
        TimelinePosition = p.TimelinePosition,
        Duration         = p.Duration,
        Order            = p.Order,
        LayerIndex       = p.LayerIndex,
        Shape            = p.Shape,
        X                = p.X,
        Y                = p.Y,
        Width            = p.Width,
        Height           = p.Height,
        Rotation         = p.Rotation,
        FillColor        = p.FillColor,
        StrokeColor      = p.StrokeColor,
        StrokeWidth      = p.StrokeWidth,
        Opacity          = p.Opacity,
        ShadowColor      = p.ShadowColor,
        ShadowOffsetX    = p.ShadowOffsetX,
        ShadowOffsetY    = p.ShadowOffsetY,
        ShadowBlur       = p.ShadowBlur,
        Text             = p.Text,
        FontFamily       = p.FontFamily,
        FontSize         = p.FontSize,
        FontColor        = p.FontColor,
        FontBold         = p.FontBold,
        FontUnderline    = p.FontUnderline,
        Runs             = p.Runs?.Select(RestoreTextRun).ToList(),
        FadeInSeconds    = p.FadeInSeconds,
        FadeOutSeconds   = p.FadeOutSeconds,
        OpfsAssetName    = p.OpfsAssetName,
        AssetMissing     = p.AssetMissing,
        OpfsExt          = p.OpfsExt,
        ControlPointValues = new Dictionary<string, double>(p.ControlPointValues),
    };

    private static TextRun RestoreTextRun(ProjectTextRun p) => new()
    {
        Text        = p.Text,
        Bold        = p.Bold,
        Underline   = p.Underline,
        Subscript   = p.Subscript,
        Superscript = p.Superscript,
        Color       = p.Color,
    };

    private static ClipArtClip RestoreClipArtClip(ProjectClipArtClip p) => new()
    {
        Id               = p.Id,
        Name             = p.Name,
        TimelinePosition = p.TimelinePosition,
        Duration         = p.Duration,
        Order            = p.Order,
        LayerIndex       = p.LayerIndex,
        AssetId          = p.AssetId,
        AssetSource      = p.AssetSource,
        AssetFormat      = p.AssetFormat,
        X                = p.X,
        Y                = p.Y,
        Width            = p.Width,
        Height           = p.Height,
        Rotation         = p.Rotation,
        Opacity          = p.Opacity,
        TintColor        = p.TintColor,
        ControlPointValues = new Dictionary<string, double>(p.ControlPointValues),
        ControlPointColors = new Dictionary<string, string>(p.ControlPointColors),
        Settings = new Models.Assets.VideoAssetSettings
        {
            AllowRecolor       = p.SettingsAllowRecolor,
            AllowResize        = p.SettingsAllowResize,
            AllowOpacity       = p.SettingsAllowOpacity,
            AllowRotation      = p.SettingsAllowRotation,
            AllowEffects       = p.SettingsAllowEffects,
            AllowEasing        = p.SettingsAllowEasing,
            AllowMotion        = p.SettingsAllowMotion,
            AllowControlPoints = p.SettingsAllowControlPoints,
        },
    };
}


