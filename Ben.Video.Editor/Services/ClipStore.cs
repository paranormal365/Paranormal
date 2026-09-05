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
    /// <summary>
    /// Mutes or unmutes a track.
    /// </summary>
    /// <remarks>
    /// <see cref="TimelineTrack.IsMuted"/> is documented as "audio suppressed during playback and
    /// export", and nothing read it: the menu item flipped the flag straight on the model and a
    /// muted track was mixed into the render exactly like any other. It was not undoable either
    /// (2026-09-05 audit, audio-5 and timeline-11).
    /// </remarks>
    public void MuteTrack(Guid trackId, bool muted)
    {
        var track = RequireTrack(trackId);
        if (track.IsMuted == muted) return;
        PushCommand(new MuteTrackCommand(track, muted));
        track.IsMuted = muted;
        Notify();
    }

    /// <summary>
    /// Whether this item's sound should be heard at all — its own mute, or its track's.
    /// </summary>
    /// <remarks>
    /// The one place to ask, so preview and export cannot disagree. A muted video track silences
    /// its clips' own audio; a muted audio track drops out of the mix entirely.
    /// </remarks>
    public bool IsAudible(TrackItem item)
    {
        var track = FindTrackOf(item.Id);
        if (track is { IsMuted: true }) return false;

        return item is not VideoClip { MuteAudio: true };
    }

    /// <summary>Audio clips that should actually be mixed, in the order they play.</summary>
    public IEnumerable<AudioClip> AudibleAudioClips =>
        AudioTracks.Where(t => !t.IsMuted)
                   .SelectMany(t => t.AudioClips)
                   .OrderBy(a => a.TimelinePosition);

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
            ItemRemoved?.Invoke(clipId);
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
            ItemRemoved?.Invoke(clipId);
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

    /// <summary>The item with this id, on whichever track it sits.</summary>
    public TrackItem? FindItem(Guid id)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == id);
            if (item is not null) return item;
        }
        return null;
    }

    /// <summary>Add any TrackItem to the specified track.</summary>
    /// <summary>
    /// Adds an item to a track, after whatever is already there.
    /// </summary>
    /// <remarks>
    /// <para>It never set a position. Every clip added this way therefore sat at zero, so a second
    /// import landed exactly on top of the first — the model had them stacked while the lane drew
    /// them politely side by side (2026-09-05 audit, F5 and media-panel-4). The method has always
    /// been documented as "append"; this makes it true in time as well as in list order.</para>
    ///
    /// <para>A position the caller has already chosen is respected: the Server tab places at the
    /// playhead, and restoring a project sets every position from the file. Only the default of
    /// zero is treated as "wherever it fits".</para>
    /// </remarks>
    public void AddClipToTrack(Guid trackId, TrackItem item)
    {
        var track = RequireTrack(trackId);
        if (track.IsLocked) return;

        if (item.TimelinePosition <= TrackLayout.Tolerance && TrackLayout.IsSequential(item))
            item.TimelinePosition = TrackLayout.EndOf(track);

        var index = track.Items.Count;
        item.Order = index;
        track.Items.Add(item);
        PushCommand(new AddClipCommand(track, item, index));
        ResortSequential(track);
        AssertLaidOut(track);
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

            // A ripple move is a lift and an insert, and it used to be neither: every item after
            // the lower of the two positions was shifted by the DRAG DISTANCE. Dragging a clip
            // eighteen seconds earlier therefore moved the clips behind it eighteen seconds
            // earlier too — straight through zero and into negative time (found by the new
            // no-overlap assertion, 2026-09-05).
            //
            // Lift: the clips that were after this one close up by its own length, not by how far
            // it travelled.
            var length = item.EffectiveLength;
            var shifted = TrackLayout.SequentialItems(track)
                .Where(i => i.Id != itemId
                         && i.TimelinePosition >= originalPosition - TrackLayout.Tolerance)
                .ToList();

            // Reset to original so Execute() can apply cleanly
            item.TimelinePosition = originalPosition;

            var cmd = new RippleCommitDraggedCommand(item, originalPosition, finalPos, shifted, -length);
            PushCommand(cmd);
            cmd.Execute();

            // Insert: whatever is where it landed moves on to make room. Ripple already means
            // "close the gaps"; this is the other half of the same idea, and without it a backwards
            // drag simply left the two clips overlapping (2026-09-05 audit, F5).
            MakeRoomFor(track, item);

            ReconcileTransitions(track);
            ResortSequential(track);
            AssertLaidOut(track);
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
            ItemRemoved?.Invoke(itemId);
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

                // Overlays could not be duplicated at all, so making three matching callouts meant
                // building each from scratch and matching every colour, size and font by hand —
                // the thing Camtasia's Ctrl+D exists for (2026-09-05 audit, callouts-15).
                //
                // Every dictionary and list is copied rather than shared: two callouts made from
                // one is the whole point, and a shared control-point dictionary would make editing
                // either of them edit both.
                CalloutClip cc => cc with
                {
                    Id                 = Guid.NewGuid(),
                    TimelinePosition   = cc.TimelinePosition + cc.Duration + 0.1,
                    ControlPointValues = new Dictionary<string, double>(cc.ControlPointValues),
                    Runs               = cc.Runs is null ? null : [.. cc.Runs],
                },

                ClipArtClip ar => ar with
                {
                    Id                 = Guid.NewGuid(),
                    TimelinePosition   = ar.TimelinePosition + ar.Duration + 0.1,
                    ControlPointValues = new Dictionary<string, double>(ar.ControlPointValues),
                    ControlPointColors = new Dictionary<string, string>(ar.ControlPointColors),
                },

                TextOverlay to => to with
                {
                    Id               = Guid.NewGuid(),
                    TimelinePosition = to.TimelinePosition + to.Duration + 0.1,
                    Runs             = to.Runs is null ? null : [.. to.Runs],
                },

                _ => null,
            };

            if (copy is null) return;

            // An overlay's row is decided by its layer, and a copy belongs above what it was
            // copied from rather than sharing its row.
            if (copy is CalloutClip or ClipArtClip or TextOverlay)
                copy.LayerIndex = NextLayerIndex();

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
    /// <param name="opfsExt">
    /// The extension the replacement was stored under, when it was persisted.
    /// </param>
    /// <param name="sourceFileId">
    /// The server file the replacement is, when it is one — null when it is a file off this
    /// person's own machine, or when it is not the file the clip previously recorded.
    /// </param>
    /// <param name="sourceFileSize">The replacement's size.</param>
    /// <param name="sourceContentHash">Its hash, when one was taken.</param>
    /// <remarks>
    /// <para>Re-linking used to write the browser's session filesystem and nothing else, so the
    /// replacement lasted exactly as long as the tab: reopening the project showed the clip as
    /// missing again. The one repair the editor offered did not survive being used
    /// (2026-09-05 audit, F14).</para>
    ///
    /// <para>An image clip was silently not handled at all — its <c>MemFsName</c> was left alone
    /// while the clip was marked present, so re-linking a picture produced a clip that claimed to
    /// have media and pointed at nothing.</para>
    /// </remarks>
    public void RelinkClip(
        Guid itemId, string newMemFsName, string? opfsExt = null,
        Guid? sourceFileId = null, long? sourceFileSize = null, string? sourceContentHash = null)
    {
        foreach (var track in _tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null) continue;

            string? oldMemFs = item is VideoClip vc ? vc.MemFsName
                             : item is AudioClip ac ? ac.MemFsName
                             : item is ImageClip ic ? ic.MemFsName
                             : null;

            // Built before the edit is applied: it captures what is being replaced.
            var command = new RelinkClipCommand(
                item, oldMemFs, newMemFsName,
                opfsExt, sourceFileId, sourceFileSize, sourceContentHash);

            command.Execute();
            PushCommand(command);
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
            // Where the pointer left it, then out of anything it landed on. A drop onto an
            // occupied spot used to be written verbatim, so two clips sat on top of each other in
            // the model while the lane drew them politely side by side (2026-09-05 audit, F5).
            var finalPos = ResolveDropPosition(track, item, Math.Max(0, item.TimelinePosition));
            item.TimelinePosition = finalPos;

            if (Math.Abs(finalPos - originalPosition) >= 0.001)
                PushCommand(new SetClipPositionCommand(item, originalPosition, finalPos));

            AnnounceShift(item, originalPosition, finalPos);
            ReconcileTransitions(track);
            ResortSequential(track);
            AssertLaidOut(track);
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

        var finalPos = ResolveDropPosition(toTrack, item, Math.Max(0, item.TimelinePosition));
        item.TimelinePosition = finalPos;

        if (toTrack.Id == fromTrack.Id)
        {
            if (Math.Abs(finalPos - originalPosition) >= 0.001)
                PushCommand(new SetClipPositionCommand(item, originalPosition, finalPos));

            AnnounceShift(item, originalPosition, finalPos);
            ResortSequential(toTrack);
            AssertLaidOut(toTrack);
            Notify();
            return;
        }

        fromTrack.Items.Remove(item);
        toTrack.Items.Add(item);
        PushCommand(new MoveClipToTrackCommand(fromTrack, toTrack, item, originalPosition, finalPos));
        AnnounceShift(item, originalPosition, finalPos);
        ResortSequential(fromTrack);
        ResortSequential(toTrack);
        AssertLaidOut(fromTrack);
        AssertLaidOut(toTrack);
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
    /// <summary>
    /// Splits the item under an <b>absolute timeline position</b>, in seconds from the start of the
    /// project.
    /// </summary>
    /// <remarks>
    /// <para><see cref="SplitClip"/> takes a position measured from the clip's own start, and every
    /// caller in the editor handed it the playhead instead — an absolute time. For the first clip
    /// on a track the two happen to be equal, which is why it looked right; for anything after it
    /// the cut landed early by exactly the clip's start position, and for a clip whose start is
    /// past the playhead it threw and was swallowed (2026-09-05 audit, timeline-1 and audio-9).</para>
    ///
    /// <para>Returns false rather than throwing when the playhead is not inside the item, because
    /// that is an ordinary thing for a person to do — press the key with the playhead somewhere
    /// else — and the caller wants to say so, not to catch.</para>
    /// </remarks>
    public bool SplitClipAtTimelineTime(Guid itemId, double timelineSeconds)
    {
        var item = _tracks.SelectMany(t => t.Items).FirstOrDefault(i => i.Id == itemId);
        if (item is null) return false;

        var offset = timelineSeconds - item.TimelinePosition;

        // Strictly inside: a cut exactly on either edge produces a zero-length piece.
        if (offset <= 0 || offset >= item.EffectiveLength) return false;

        SplitClip(itemId, offset);
        return true;
    }

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
            ReconcileTransitions(track);
            ResortSequential(track);
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

    // ── Media bin ─────────────────────────────────────────────────────────────

    private readonly List<TrackItem> _mediaBin = [];

    /// <summary>
    /// The media you have brought in, whether or not any of it is on the timeline.
    /// </summary>
    /// <remarks>
    /// <para>There was no such thing. The Media panel's three tabs listed the timeline's own items,
    /// so "your media" and "your edit" were one list: declining the insert prompt left the clip
    /// nowhere, removing it from the timeline meant importing the file again, and using one source
    /// twice was only possible by finding a copy of it already placed (2026-09-05 audit,
    /// media-panel-3 and F8).</para>
    ///
    /// <para>Entries are ordinary clips that happen to live outside any track. Placing one puts a
    /// copy on the timeline, so the same source can be used as often as you like and trimming one
    /// placement leaves the others alone.</para>
    /// </remarks>
    public IReadOnlyList<TrackItem> MediaBin => _mediaBin;

    /// <summary>Adds a source to the bin. Undoable.</summary>
    public void AddToBin(TrackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_mediaBin.Any(i => i.Id == item.Id)) return;

        PushCommand(new AddToBinCommand(_mediaBin, item, _mediaBin.Count));
        _mediaBin.Add(item);
        Notify();
    }

    /// <summary>
    /// Takes a source out of the bin.
    /// </summary>
    /// <remarks>
    /// Clips already placed from it stay exactly as they are: each holds its own copy of the
    /// arrangement and points at the same file, so removing the card removes a card rather than
    /// pulling footage out from under an edit.
    /// </remarks>
    public void RemoveFromBin(Guid binItemId)
    {
        var index = _mediaBin.FindIndex(i => i.Id == binItemId);
        if (index < 0) return;

        var item = _mediaBin[index];
        PushCommand(new RemoveFromBinCommand(_mediaBin, item, index));
        _mediaBin.RemoveAt(index);
        Notify();
    }

    /// <summary>How many times this source has been placed on the timeline.</summary>
    public int TimesOnTimeline(Guid binItemId) =>
        _tracks.SelectMany(t => t.Items).Count(i => i.SourceBinId == binItemId);

    /// <summary>The bin's video sources, in the order they were brought in.</summary>
    public IEnumerable<VideoClip> BinVideoClips => _mediaBin.OfType<VideoClip>();

    /// <summary>The bin's audio sources.</summary>
    public IEnumerable<AudioClip> BinAudioClips => _mediaBin.OfType<AudioClip>();

    /// <summary>The bin's pictures.</summary>
    public IEnumerable<ImageClip> BinImageClips => _mediaBin.OfType<ImageClip>();

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

        // Never longer than the clips it joins can spare — a two-second crossfade between
        // one-second clips renders as something ffmpeg has to invent (2026-09-05 audit,
        // transitions-7). The 1.0s the callers hard-code is a request, not a promise.
        durationSeconds = TransitionDurationClamp.Clamp(
            durationSeconds, from.EffectiveLength, to.EffectiveLength);

        var fromEndSeconds = from.TimelinePosition + from.EffectiveLength;
        var transition = new Transition
        {
            Name             = $"{style}",
            Style            = style,
            FromClipId       = fromClipId,
            ToClipId         = toClipId,
            TimelinePosition = fromEndSeconds - durationSeconds,
            Duration         = durationSeconds,
        };

        // The two clips genuinely play at once for the length of the crossfade, so the second one
        // moves back to meet the first and everything after it follows. Without this the timeline
        // claimed a length the render never produced: ffmpeg's xfade output is A + B − d, so every
        // marker, overlay and audio clip after the junction drifted later than what it lined up
        // with on screen (2026-09-05 audit, transitions-3).
        ShiftFrom(track, to, -durationSeconds, transition.Id);
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
    /// Cross-track crossfades are not offered any more.
    /// </summary>
    /// <remarks>
    /// <para><c>AddCrossTrackTransition</c> stood here. What it produced could not be rendered
    /// correctly: the export replaced the first clip's segment with a merged one whose length is
    /// fromDur + toDur − overlap, while every later offset was still measured against the original
    /// length, so everything after the junction drifted — and the preview never showed it at all
    /// (2026-09-05 audit, transitions-9).</para>
    ///
    /// <para>Fading a clip up from black or down to it is the honest version of the same idea, and
    /// it already existed: <c>ClipEffects.FadeInSeconds</c> and <c>FadeOutSeconds</c>, which the
    /// export renders and a project file saves. The clip's right-click menu offers both.</para>
    /// </remarks>


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

        var owningTrack = _tracks.First(t => t.Items.Contains(transition));
        var toItem      = owningTrack.Items.FirstOrDefault(i => i.Id == transition.ToClipId);

        if (fromItem is not null && toItem is not null)
            durationSeconds = TransitionDurationClamp.Clamp(
                durationSeconds, fromItem.EffectiveLength, toItem.EffectiveLength);

        // Lengthening the crossfade pulls the second clip further back, shortening it lets it out
        // again — the overlap and the duration are the same number seen twice.
        var delta = durationSeconds - transition.Duration;

        var command = BuildTransitionStyleUpdate(transition, fromItem, style, durationSeconds);
        PushCommand(command);

        if (toItem is not null && !owningTrack.IsLocked)
            ShiftFrom(owningTrack, toItem, -delta, transition.Id);

        ResortSequential(owningTrack);
        AssertLaidOut(owningTrack);
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
    /// Commits a transition's edge-drag resize, given what its duration was before the drag.
    /// </summary>
    /// <remarks>
    /// The drag mutates the transition live for a smooth preview, and the commit then called
    /// <see cref="UpdateTransition"/> with the duration it had just finished writing — so the undo
    /// step recorded "from 2s to 2s" and undoing did nothing at all (2026-09-05 audit,
    /// transitions-6). Passing the original in is the whole fix.
    /// </remarks>
    public void CommitTransitionResize(Guid transitionId, double originalDuration)
    {
        var track      = _tracks.FirstOrDefault(t => t.Items.Any(i => i.Id == transitionId));
        var transition = track?.Items.OfType<Transition>().FirstOrDefault(t => t.Id == transitionId);
        if (track is null || transition is null) return;

        var requested = transition.Duration;
        if (Math.Abs(requested - originalDuration) < TrackLayout.Tolerance) return;

        // Put it back so UpdateTransition sees the real before-value and can record it.
        transition.Duration = originalDuration;
        var toItem = track.Items.FirstOrDefault(i => i.Id == transition.ToClipId);
        if (toItem is not null)
            transition.TimelinePosition += originalDuration - requested;

        UpdateTransition(transitionId, transition.Style, requested);
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
            // The transition covers the stretch where both clips play, which starts where the
            // first clip ends less the crossfade — not centred on the junction. See AddTransition.
            var fromEndSeconds = fromItem.TimelinePosition + fromItem.EffectiveLength;
            transition.TimelinePosition = fromEndSeconds - durationSeconds;
        }

        return new UpdateTransitionCommand(
            transition,
            oldStyle, oldDuration, oldName, oldPosition,
            transition.Style, transition.Duration, transition.Name, transition.TimelinePosition);
    }

    /// <summary>Remove a transition by id.</summary>
    /// <summary>
    /// Removes a transition and gives back the time it was borrowing.
    /// </summary>
    /// <remarks>
    /// Adding one pulled the second clip back to overlap the first for its duration; removing it
    /// has to push that clip and everything after it forward again, or the two stay overlapping
    /// with nothing to justify it (2026-09-05 audit, transitions-3).
    /// </remarks>
    public void RemoveTransition(Guid transitionId)
    {
        var track = _tracks.FirstOrDefault(t => t.Items.Any(i => i.Id == transitionId));
        var transition = track?.Items.OfType<Transition>().FirstOrDefault(t => t.Id == transitionId);

        if (track is null || transition is null) { RemoveClip(transitionId); return; }

        var to = track.Items.FirstOrDefault(i => i.Id == transition.ToClipId);
        var duration = transition.Duration;

        using (BeginBatch())
        {
            RemoveClip(transitionId);
            if (to is not null && !track.IsLocked) ShiftFrom(track, to, duration);
        }

        ResortSequential(track);
        AssertLaidOut(track);
        Notify();
    }

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

    /// <summary>
    /// Applies one change to a title, undoably.
    /// </summary>
    /// <remarks>
    /// <para>The same shape as <see cref="CommitCalloutUpdate"/>, and added for the same reason it
    /// exists there. Titles were the one thing on the timeline whose edits were not undoable at
    /// all: <see cref="UpdateTextOverlay"/> wrote straight into the model and pushed nothing, so
    /// Ctrl+Z after changing a title undid whatever had happened before it (2026-09-05 audit,
    /// titles-4).</para>
    ///
    /// <para>Per property rather than per panel, so undo steps back through the edits somebody
    /// actually made instead of one opaque "the title changed".</para>
    /// </remarks>
    public void CommitTextOverlayUpdate(
        Guid overlayId, string propertyPath,
        Action<TextOverlay> apply, Action<TextOverlay> revert)
    {
        foreach (var track in _tracks)
        {
            var overlay = track.Items.OfType<TextOverlay>().FirstOrDefault(o => o.Id == overlayId);
            if (overlay is null) continue;
            if (track.IsLocked) return;

            apply(overlay);
            PushCommand(new CommitTextOverlayPropertyCommand(overlay, propertyPath, apply, revert));
            Notify();
            return;
        }
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

    /// <summary>
    /// Whether there is anything worth saving.
    /// </summary>
    /// <remarks>
    /// Deliberately wider than <see cref="HasExportableContent"/>. A project can be worth keeping
    /// without a video clip in it — a set of markers, a title, an audio track, media sitting in the
    /// bin — and gating Save on video content meant Save was disabled over work that was plainly
    /// there (2026-09-05 audit, persistence-4).
    /// </remarks>
    public bool HasSaveableContent =>
        _tracks.Any(t => t.Items.Count > 0) || _markers.Count > 0 || _mediaBin.Count > 0;

    /// <summary>Whether there is anything for an export to render.</summary>
    /// <remarks>
    /// The toolbar asked one question and the export dialog asked another: the dialog counted only
    /// the primary track's video clips, so an image-only timeline opened a dialog whose Export
    /// button was permanently disabled (2026-09-05 audit, export-20). One answer for both, and for
    /// the pipeline's own "nothing to export" check.
    /// </remarks>
    public bool HasExportableContent => AllVideoClips.Any() || AllImageClips.Any();

    /// <summary>How many clips an export would render, for the dialog's summary line.</summary>
    public int ExportableItemCount => AllVideoClips.Count() + AllImageClips.Count();

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
    /// <summary>
    /// Puts a track's sequential items back in playing order and renumbers <c>Order</c> to match.
    /// </summary>
    /// <remarks>
    /// A drag changed only <see cref="TrackItem.TimelinePosition"/>. <c>Order</c> was left as it
    /// was, and export sequences by <c>Order</c> — so dragging the second clip in front of the
    /// first left a timeline that showed one arrangement and rendered another (2026-09-05 audit,
    /// timeline-10). Overlays and transitions keep their own relative order: <c>LayerIndex</c>, not
    /// position, is what decides which of them is drawn on top.
    /// </remarks>
    /// <summary>
    /// The nearest position at or after <paramref name="preferred"/> where this item fits without
    /// landing on anything.
    /// </summary>
    /// <remarks>
    /// <para>The rule a person expects from a timeline: a clip dropped onto an occupied spot goes
    /// after what is there rather than into it. It is deliberately the last line of defence — the
    /// timeline offers Insert or Overwrite before it gets here, and this is what happens when
    /// nobody chose (a drop with ripple off, an import, a restored project).</para>
    ///
    /// <para>Overlays and transitions are not resolved: they are supposed to sit over the picture.</para>
    /// </remarks>
    private static double ResolveDropPosition(TimelineTrack track, TrackItem item, double preferred)
    {
        if (!TrackLayout.IsSequential(item)) return preferred;

        var length = item.EffectiveLength;
        if (length <= 0) return preferred;

        var position = preferred;

        // Walk forward past whatever is in the way. Each step lands on the far edge of the blocker,
        // so this terminates: there are finitely many items and each is passed at most once.
        while (TrackLayout.FirstOverlapping(track, position, length, item.Id) is { } blocker)
            position = blocker.TimelinePosition + blocker.EffectiveLength;

        return position;
    }

    /// <summary>
    /// Moves an item to <paramref name="position"/> and pushes whatever is there out of the way.
    /// </summary>
    /// <remarks>
    /// The "Insert" answer to a drop onto an occupied spot. One undo step covers the move and the
    /// room made for it, so undoing puts the whole track back.
    /// </remarks>
    public void MoveWithInsert(Guid itemId, Guid trackId, double position, double originalPosition)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == trackId);
        var item  = track?.Items.FirstOrDefault(i => i.Id == itemId)
                 ?? _tracks.SelectMany(t => t.Items).FirstOrDefault(i => i.Id == itemId);
        if (track is null || item is null || track.IsLocked) return;

        var finalPos = Math.Max(0, position);

        var steps = new List<IEditorCommand>
        {
            new SetClipPositionCommand(item, originalPosition, finalPos),
        };

        item.TimelinePosition = finalPos;

        var length  = item.EffectiveLength;
        var blocker = TrackLayout.FirstOverlapping(track, finalPos, length, item.Id);
        if (blocker is not null)
        {
            var shiftBy = finalPos + length - blocker.TimelinePosition;
            steps.AddRange(TrackLayout.SequentialItems(track)
                .Where(i => i.Id != item.Id
                         && i.TimelinePosition >= blocker.TimelinePosition - TrackLayout.Tolerance)
                .Select(i => (IEditorCommand)new SetClipPositionCommand(
                    i, i.TimelinePosition, i.TimelinePosition + shiftBy)));
        }

        item.TimelinePosition = originalPosition;   // so Execute applies cleanly

        var command = new CompositeCommand("Insert clip", steps);
        PushCommand(command);
        command.Execute();

        ResortSequential(track);
        AssertLaidOut(track);
        Notify();
    }

    /// <summary>
    /// Moves an item to <paramref name="position"/>, replacing whatever it lands on.
    /// </summary>
    /// <remarks>
    /// The "Overwrite" answer. What is underneath is trimmed back, split around, or removed —
    /// <see cref="OverwriteInsert"/> already does exactly that for a newly added clip, so this
    /// lifts the item out of the track first and then re-adds it through that path, which keeps one
    /// implementation of the hard part.
    /// </remarks>
    public void MoveWithOverwrite(Guid itemId, Guid trackId, double position, double originalPosition)
    {
        var track = _tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || track.IsLocked) return;

        var sourceTrack = _tracks.FirstOrDefault(t => t.Items.Any(i => i.Id == itemId));
        var item = sourceTrack?.Items.FirstOrDefault(i => i.Id == itemId);
        if (sourceTrack is null || item is null || sourceTrack.IsLocked) return;

        // Overwrite is only defined for video clips today (see OverwriteInsert). Anything else
        // falls back to insert, which is lossless — better than an edit nobody can undo into.
        if (item is not VideoClip clip)
        {
            MoveWithInsert(itemId, trackId, position, originalPosition);
            return;
        }

        // One notification for the pair. They are still two undo steps — lifting the clip out and
        // putting it back over what was there — which reads correctly when undone one at a time.
        using (BeginBatch())
        {
            RemoveClip(itemId);
            clip.TimelinePosition = Math.Max(0, position);
            OverwriteInsert(trackId, clip, Math.Max(0, position));
        }

        ResortSequential(track);
        AssertLaidOut(track);
        Notify();
    }

    /// <summary>
    /// Pushes whatever <paramref name="item"/> has landed on later, so it fits exactly where it was
    /// dropped.
    /// </summary>
    /// <remarks>
    /// The insert half of an insert-or-overwrite drop. Everything from the first blocker onwards
    /// moves by the same amount, so their spacing is preserved rather than collapsed, and the shift
    /// is pushed as its own undo step beside the move — undoing the drop puts the whole track back.
    /// </remarks>
    private void MakeRoomFor(TimelineTrack track, TrackItem item)
    {
        if (!TrackLayout.IsSequential(item)) return;

        var length = item.EffectiveLength;
        if (length <= 0) return;

        var blocker = TrackLayout.FirstOverlapping(track, item.TimelinePosition, length, item.Id);
        if (blocker is null) return;

        var neededAt = item.TimelinePosition + length;
        var shiftBy  = neededAt - blocker.TimelinePosition;
        if (shiftBy <= 0) return;

        var toShift = TrackLayout.SequentialItems(track)
            .Where(i => i.Id != item.Id && i.TimelinePosition >= blocker.TimelinePosition - TrackLayout.Tolerance)
            .ToList();

        var shifts = toShift
            .Select(i => (IEditorCommand)new SetClipPositionCommand(
                i, i.TimelinePosition, i.TimelinePosition + shiftBy))
            .ToList();

        if (shifts.Count == 0) return;

        var command = new CompositeCommand("Make room", shifts);
        PushCommand(command);
        command.Execute();

        foreach (var moved in toShift)
            ItemTimeShifted?.Invoke(moved.Id, shiftBy);
    }

    /// <summary>
    /// Moves <paramref name="anchor"/> and everything after it on the track by
    /// <paramref name="seconds"/>.
    /// </summary>
    /// <remarks>
    /// Used to open and close the overlap a transition needs. The shift is pushed as its own undo
    /// step so removing a transition can put the clips back exactly.
    /// </remarks>
    private void ShiftFrom(TimelineTrack track, TrackItem anchor, double seconds, Guid? exceptId = null)
    {
        if (Math.Abs(seconds) < TrackLayout.Tolerance) return;

        var affected = TrackLayout.SequentialItems(track)
            .Where(i => i.Id != exceptId
                     && i.TimelinePosition >= anchor.TimelinePosition - TrackLayout.Tolerance)
            .ToList();

        if (affected.Count == 0) return;

        var steps = affected
            .Select(i => (IEditorCommand)new SetClipPositionCommand(
                i, i.TimelinePosition, Math.Max(0, i.TimelinePosition + seconds)))
            .ToList();

        var command = new CompositeCommand(seconds < 0 ? "Close for transition" : "Reopen after transition", steps);
        PushCommand(command);
        command.Execute();

        foreach (var moved in affected)
            ItemTimeShifted?.Invoke(moved.Id, seconds);
    }

    /// <summary>
    /// Drops transitions whose clips are no longer where they were.
    /// </summary>
    /// <remarks>
    /// <para>A transition names two clips and sits on the junction between them. Nothing checked
    /// that the junction still existed: removing, splitting, trimming or moving either clip left
    /// the transition behind, pointing at a clip that was gone or no longer adjacent, and the
    /// export then matched transitions to junctions by position and applied it to whichever pair
    /// happened to be there (2026-09-05 audit, transitions-5).</para>
    ///
    /// <para>Called after every edit that can move a clip. Silent by design — a transition whose
    /// junction a person has just deleted is not news.</para>
    /// </remarks>
    /// <summary>
    /// Pushes apart any clips that overlap without a transition to justify it.
    /// </summary>
    /// <remarks>
    /// The repair half of the invariant. Every edit that can create an overlap resolves it up
    /// front, so this is normally a no-op; what it exists for is the case where the justification
    /// disappears rather than the overlap appearing — a transition dropped because its junction is
    /// gone leaves the two clips still sitting on top of each other.
    /// </remarks>
    private void CloseUnjustifiedOverlaps(TimelineTrack track)
    {
        if (track.IsLocked) return;

        // Front to back, so closing one overlap cannot leave a later one measured against a
        // position that is about to change.
        for (var guard = 0; guard < 100; guard++)
        {
            var items = TrackLayout.SequentialItems(track);
            var moved = false;

            for (var i = 1; i < items.Count; i++)
            {
                var previous = items[i - 1];
                var item     = items[i];

                var allowed = TrackLayout.AllowedOverlap(track, previous, item);
                var overlap = previous.TimelinePosition + previous.EffectiveLength
                            - item.TimelinePosition - allowed;

                if (overlap <= TrackLayout.Tolerance) continue;

                ShiftFrom(track, item, overlap);
                moved = true;
                break;
            }

            if (!moved) return;
        }
    }

    private void ReconcileTransitions(TimelineTrack track)
    {
        var stale = track.Items.OfType<Transition>().Where(t =>
        {
            var from = track.Items.FirstOrDefault(i => i.Id == t.FromClipId);
            var to   = track.Items.FirstOrDefault(i => i.Id == t.ToClipId);

            if (from is null || to is null) return true;

            // Still adjacent? The junction is where the first one ends, less the overlap the
            // transition itself opened.
            var junction = from.TimelinePosition + from.EffectiveLength - t.Duration;
            return Math.Abs(to.TimelinePosition - junction) > 0.05;
        }).ToList();

        if (stale.Count == 0) return;

        foreach (var transition in stale)
            track.Items.Remove(transition);

        RenumberItems(track);

        // The overlap only existed because the transition did. Close whatever is left over — by
        // position, not by clip id, because the clip a transition pointed at may well have been
        // replaced by the very edit that stranded it (splitting one produces two new items).
        CloseUnjustifiedOverlaps(track);
    }

    private static void ResortSequential(TimelineTrack track)
    {
        var sequential = track.Items.Where(TrackLayout.IsSequential)
                                    .OrderBy(i => i.TimelinePosition)
                                    .ToList();
        if (sequential.Count == 0) return;

        var others = track.Items.Where(i => !TrackLayout.IsSequential(i)).ToList();

        track.Items.Clear();
        track.Items.AddRange(sequential);
        track.Items.AddRange(others);

        RenumberItems(track);
    }

    /// <summary>
    /// Asserts the no-overlap invariant in debug builds.
    /// </summary>
    /// <remarks>
    /// Debug only, and deliberately loud there: an overlap is a bug in whichever edit produced it,
    /// and the failing assertion names the track and the two clips. Release builds carry on — a
    /// person mid-edit is not helped by a crash.
    /// </remarks>
    [System.Diagnostics.Conditional("DEBUG")]
    private static void AssertLaidOut(TimelineTrack track)
    {
        var problem = TrackLayout.Validate(track);
        System.Diagnostics.Debug.Assert(problem is null, $"Track '{track.Label}': {problem}");
    }

    /// <summary>
    /// Checks every track. Public so tests can hold the whole store to the invariant after an edit.
    /// </summary>
    /// <returns>The first problem found, or null when every track is laid out properly.</returns>
    public string? ValidateAll() =>
        _tracks.Select(t => TrackLayout.Validate(t) is { } p ? $"Track '{t.Label}': {p}" : null)
               .FirstOrDefault(p => p is not null);

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

    /// <summary>
    /// Raised when an item's position on the timeline changes, with how far it moved.
    /// </summary>
    /// <remarks>
    /// Motion keyframes are stored in project seconds, so a layer that moves has to take its
    /// animation with it. The store does not know about the keyframe service — the editor
    /// subscribes and forwards (2026-09-05 audit, motion-3).
    /// </remarks>
    public event Action<Guid, double>? ItemTimeShifted;

    /// <summary>Raised when an item is removed, so anything hanging off it can be cleaned up.</summary>
    public event Action<Guid>? ItemRemoved;

    private void AnnounceShift(TrackItem item, double from, double to)
    {
        if (Math.Abs(to - from) < TrackLayout.Tolerance) return;
        ItemTimeShifted?.Invoke(item.Id, to - from);
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
        _mediaBin.Clear();
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
        _mediaBin.Clear();
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

        RestoreBin(project);

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

    /// <summary>
    /// Fills the media bin from a project file.
    /// </summary>
    /// <remarks>
    /// <para>A file written before the bin existed has no <c>Bin</c> section, and opening it to an
    /// empty Media panel would read as having lost the footage. So for those, the bin is seeded
    /// from what is on the timeline — one entry per distinct source, which is what the panel used
    /// to show anyway.</para>
    ///
    /// <para>Seeded entries get their own ids and are linked to the clips they were derived from,
    /// so the "on timeline" count is right immediately and re-saving writes a real bin.</para>
    /// </remarks>
    private void RestoreBin(ProjectFile project)
    {
        if (!project.Bin.IsEmpty)
        {
            foreach (var pv in project.Bin.VideoClips) _mediaBin.Add(RestoreVideoClip(pv));
            foreach (var pa in project.Bin.AudioClips) _mediaBin.Add(RestoreAudioClip(pa));
            foreach (var pi in project.Bin.ImageClips) _mediaBin.Add(RestoreImageClip(pi));
            return;
        }

        // An older project: one bin entry per source already on the timeline.
        foreach (var group in _tracks.SelectMany(t => t.Items)
                                     .Where(TrackLayout.IsSequential)
                                     .GroupBy(SourceKeyOf))
        {
            var first = group.First();
            var entry = CloneForBin(first);
            if (entry is null) continue;

            _mediaBin.Add(entry);

            foreach (var placed in group)
                placed.SourceBinId = entry.Id;
        }
    }

    /// <summary>
    /// What makes two placed clips the same source: the file they came from, or failing that their
    /// name.
    /// </summary>
    private static string SourceKeyOf(TrackItem item) =>
        item.OriginalFileName
        ?? (item as VideoClip)?.MemFsName
        ?? (item as AudioClip)?.BlobUrl
        ?? (item as ImageClip)?.MemFsName
        ?? item.Name;

    /// <summary>A bin entry made from a placed clip: same media, its own identity, untrimmed.</summary>
    private static TrackItem? CloneForBin(TrackItem item) => item switch
    {
        VideoClip v => v with
        {
            Id = Guid.NewGuid(), SourceBinId = null, TimelinePosition = 0, Order = 0,
            ThumbnailUrls = [.. v.ThumbnailUrls],
            VolumeAutomation = [.. v.VolumeAutomation],
            AppliedEffects = [.. v.AppliedEffects],
        },
        AudioClip a => a with
        {
            Id = Guid.NewGuid(), SourceBinId = null, TimelinePosition = 0, Order = 0,
            VolumeAutomation = [.. a.VolumeAutomation],
        },
        ImageClip i => i with { Id = Guid.NewGuid(), SourceBinId = null, TimelinePosition = 0, Order = 0 },
        _ => null,
    };

    private static VideoClip RestoreVideoClip(ProjectVideoClip p) => new()
    {
        Id               = p.Id,
        SourceBinId      = p.SourceBinId,
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
        MuteAudio        = p.MuteAudio,
        HasAudio         = p.HasAudio,
        LinkedClipId     = p.LinkedClipId,
        Effects          = p.Effects,
        AppliedEffects   = p.AppliedEffects.Select(e => new AppliedEffect
        {
            EffectId   = e.EffectId,
            Parameters = new Dictionary<string, double>(e.Parameters),
        }).ToList(),
        IsMediaMissing   = true,
        OriginalFileName = p.OriginalFileName,
        OpfsExt          = p.OpfsExt,
        SourceFileId      = p.SourceFileId,
        SourceFileSize    = p.SourceFileSize,
        SourceContentHash = p.SourceContentHash,
    };

    private static AudioClip RestoreAudioClip(ProjectAudioClip p) => new()
    {
        Id               = p.Id,
        SourceBinId      = p.SourceBinId,
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
        LinkedClipId     = p.LinkedClipId,
        IsMediaMissing   = true,
        OriginalFileName = p.OriginalFileName,
        OpfsExt          = p.OpfsExt,
        SourceFileId      = p.SourceFileId,
        SourceFileSize    = p.SourceFileSize,
        SourceContentHash = p.SourceContentHash,
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
        MaxWidth         = p.MaxWidth,
            ShadowColor      = p.ShadowColor,
            ShadowOffsetX    = p.ShadowOffsetX,
            ShadowOffsetY    = p.ShadowOffsetY,
            ShadowBlur       = p.ShadowBlur,
        };
    }

    private static ImageClip RestoreImageClip(ProjectImageClip p) => new()
    {
        Id               = p.Id,
        SourceBinId      = p.SourceBinId,
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
        SourceFileId      = p.SourceFileId,
        SourceFileSize    = p.SourceFileSize,
        SourceContentHash = p.SourceContentHash,
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
        TextAlign         = p.TextAlign,
        TextVerticalAlign = p.TextVerticalAlign,
        TextWrap          = p.TextWrap,
        TextShadow        = p.TextShadow,
        TextPadding       = p.TextPadding,
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
        NativeWidth      = p.NativeWidth,
        NativeHeight     = p.NativeHeight,
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


