namespace Ben.Video.Editor.Models;

/// <summary>
/// Represents a reversible editor action that can be pushed onto the undo stack.
/// Both <see cref="Execute"/> and <see cref="Undo"/> perform the action directly
/// against the mutable model objects they captured at construction time.
/// </summary>
public interface IEditorCommand
{
    /// <summary>Apply (or re-apply) the action.</summary>
    void Execute();

    /// <summary>Reverse the action.</summary>
    void Undo();

    /// <summary>Human-readable label shown in undo/redo tooltip.</summary>
    string Description { get; }
}

// ── Concrete command records ──────────────────────────────────────────────────
// All are internal; only ClipStore creates them.

/// <summary>Undo/redo for adding a clip (any TrackItem) to a track.</summary>
internal sealed class AddClipCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly TrackItem     _item;
    private readonly int           _index;

    public AddClipCommand(TimelineTrack track, TrackItem item, int index)
    {
        _track = track;
        _item  = item;
        _index = index;
    }

    public string Description => $"Add {_item.Name}";

    public void Execute()
    {
        if (!_track.Items.Any(i => i.Id == _item.Id))
        {
            var at = Math.Min(_index, _track.Items.Count);
            _track.Items.Insert(at, _item);
            Renumber();
        }
    }

    public void Undo()
    {
        var idx = _track.Items.FindIndex(i => i.Id == _item.Id);
        if (idx >= 0)
        {
            _track.Items.RemoveAt(idx);
            Renumber();
        }
    }

    private void Renumber()
    {
        for (var i = 0; i < _track.Items.Count; i++)
            _track.Items[i] = _track.Items[i] with { Order = i };
    }
}

/// <summary>Undo/redo for removing a clip (any TrackItem) from a track.</summary>
internal sealed class RemoveClipCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly TrackItem     _item;
    private readonly int           _index;

    public RemoveClipCommand(TimelineTrack track, TrackItem item, int index)
    {
        _track = track;
        _item  = item;
        _index = index;
    }

    public string Description => $"Remove {_item.Name}";

    public void Execute()
    {
        var idx = _track.Items.FindIndex(i => i.Id == _item.Id);
        if (idx >= 0)
        {
            _track.Items.RemoveAt(idx);
            Renumber();
        }
    }

    public void Undo()
    {
        if (!_track.Items.Any(i => i.Id == _item.Id))
        {
            var at = Math.Min(_index, _track.Items.Count);
            _track.Items.Insert(at, _item);
            Renumber();
        }
    }

    private void Renumber()
    {
        for (var i = 0; i < _track.Items.Count; i++)
            _track.Items[i] = _track.Items[i] with { Order = i };
    }
}

/// <summary>
/// Undo/redo for splitting any <see cref="TrackItem"/> (video, audio, or image clip)
/// into two adjacent items. Execute replaces <c>original</c> with <c>first</c>+<c>second</c>;
/// Undo removes both halves and restores <c>original</c> at the same position.
/// </summary>
internal sealed class SplitClipCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly TrackItem     _original;
    private readonly TrackItem     _first;
    private readonly TrackItem     _second;
    private readonly int           _index;

    public SplitClipCommand(TimelineTrack track, TrackItem original, TrackItem first, TrackItem second, int index)
    {
        _track    = track;
        _original = original;
        _first    = first;
        _second   = second;
        _index    = index;
    }

    public string Description => $"Split {_original.Name}";

    public void Execute()
    {
        var idx = _track.Items.FindIndex(i => i.Id == _original.Id);
        if (idx < 0) return;
        _track.Items.RemoveAt(idx);
        _track.Items.Insert(idx,     _first);
        _track.Items.Insert(idx + 1, _second);
        Renumber();
    }

    public void Undo()
    {
        var idxFirst = _track.Items.FindIndex(i => i.Id == _first.Id);
        if (idxFirst < 0) return;
        _track.Items.RemoveAt(idxFirst);
        var idxSecond = _track.Items.FindIndex(i => i.Id == _second.Id);
        if (idxSecond >= 0) _track.Items.RemoveAt(idxSecond);
        var at = Math.Min(_index, _track.Items.Count);
        _track.Items.Insert(at, _original);
        Renumber();
    }

    private void Renumber()
    {
        for (var i = 0; i < _track.Items.Count; i++)
            _track.Items[i] = _track.Items[i] with { Order = i };
    }
}

/// <summary>Undo/redo for reordering a clip within its track (drag &amp; drop).</summary>
internal sealed class ReorderClipCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly Guid          _itemId;
    private readonly int           _fromIndex;
    private readonly int           _toIndex;

    public string Description => "Move clip";

    public ReorderClipCommand(TimelineTrack track, Guid itemId, int fromIndex, int toIndex)
    {
        _track     = track;
        _itemId    = itemId;
        _fromIndex = fromIndex;
        _toIndex   = toIndex;
    }

    public void Execute() => MoveTo(_fromIndex, _toIndex);
    public void Undo()    => MoveTo(_toIndex,   _fromIndex);

    private void MoveTo(int from, int to)
    {
        var idx = _track.Items.FindIndex(i => i.Id == _itemId);
        if (idx < 0) return;
        var item = _track.Items[idx];
        _track.Items.RemoveAt(idx);
        var dest = Math.Clamp(to, 0, _track.Items.Count);
        _track.Items.Insert(dest, item);
        for (var i = 0; i < _track.Items.Count; i++)
            _track.Items[i] = _track.Items[i] with { Order = i };
    }
}

/// <summary>Undo/redo for updating a video clip's in/out trim points.</summary>
internal sealed class UpdateTrimCommand : IEditorCommand
{
    private readonly VideoClip _clip;
    private readonly double    _oldStart, _oldEnd;
    private readonly double    _newStart, _newEnd;

    public string Description => "Trim clip";

    public UpdateTrimCommand(VideoClip clip, double oldStart, double oldEnd,
                                              double newStart, double newEnd)
    {
        _clip     = clip;
        _oldStart = oldStart; _oldEnd = oldEnd;
        _newStart = newStart; _newEnd = newEnd;
    }

    public void Execute() { _clip.StartTrim = _newStart; _clip.EndTrim = _newEnd; }
    public void Undo()    { _clip.StartTrim = _oldStart; _clip.EndTrim = _oldEnd; }
}

/// <summary>
/// Undo/redo for a <b>slip</b> edit (item #50): shifts a clip's source-trim window by a delta
/// without moving the clip on the timeline or changing its on-timeline duration.
/// </summary>
internal sealed class SlipClipCommand : IEditorCommand
{
    private readonly VideoClip _clip;
    private readonly double    _delta;

    public string Description => $"Slip {_clip.Name}";

    public SlipClipCommand(VideoClip clip, double delta)
    {
        _clip  = clip;
        _delta = delta;
    }

    public void Execute() { _clip.StartTrim += _delta; _clip.EndTrim += _delta; }
    public void Undo()    { _clip.StartTrim -= _delta; _clip.EndTrim -= _delta; }
}

/// <summary>
/// Undo/redo for a <b>roll</b> edit (item #50): moves the shared edit point between two
/// immediately-adjacent clips by a delta — the left clip's out-trim extends/shrinks and the right
/// clip's in-trim (and timeline position, to stay adjacent) shrinks/extends by the same amount,
/// leaving their combined span unchanged.
/// </summary>
internal sealed class RollEditCommand : IEditorCommand
{
    private readonly VideoClip _left;
    private readonly VideoClip _right;
    private readonly double    _delta;

    public string Description => "Roll edit";

    public RollEditCommand(VideoClip left, VideoClip right, double delta)
    {
        _left  = left;
        _right = right;
        _delta = delta;
    }

    public void Execute()
    {
        _left.EndTrim            += _delta;
        _right.StartTrim         += _delta;
        _right.TimelinePosition  += _delta;
    }

    public void Undo()
    {
        _left.EndTrim            -= _delta;
        _right.StartTrim         -= _delta;
        _right.TimelinePosition  -= _delta;
    }
}

/// <summary>
/// Undo/redo for a <b>slide</b> edit (item #50): moves a clip along the timeline without changing
/// its own trim points, while its immediate neighbors absorb the move — the previous clip extends/
/// shrinks its out-trim, the next clip shrinks/extends its in-trim and shifts position to match.
/// </summary>
internal sealed class SlideClipCommand : IEditorCommand
{
    private readonly VideoClip _prev;
    private readonly VideoClip _mid;
    private readonly VideoClip _next;
    private readonly double    _delta;

    public string Description => $"Slide {_mid.Name}";

    public SlideClipCommand(VideoClip prev, VideoClip mid, VideoClip next, double delta)
    {
        _prev  = prev;
        _mid   = mid;
        _next  = next;
        _delta = delta;
    }

    public void Execute()
    {
        _prev.EndTrim           += _delta;
        _mid.TimelinePosition   += _delta;
        _next.StartTrim         += _delta;
        _next.TimelinePosition  += _delta;
    }

    public void Undo()
    {
        _prev.EndTrim           -= _delta;
        _mid.TimelinePosition   -= _delta;
        _next.StartTrim         -= _delta;
        _next.TimelinePosition  -= _delta;
    }
}

/// <summary>Undo/redo for changing a video clip's playback speed.</summary>
internal sealed class UpdateSpeedCommand : IEditorCommand
{
    private readonly VideoClip _clip;
    private readonly double    _oldSpeed, _newSpeed;

    public string Description => "Change speed";

    public UpdateSpeedCommand(VideoClip clip, double oldSpeed, double newSpeed)
    {
        _clip     = clip;
        _oldSpeed = oldSpeed;
        _newSpeed = newSpeed;
    }

    public void Execute() => _clip.Speed = _newSpeed;
    public void Undo()    => _clip.Speed = _oldSpeed;
}

/// <summary>Undo/redo for changing a clip's scalar volume (not automation keyframes).</summary>
internal sealed class UpdateVolumeCommand : IEditorCommand
{
    private readonly IHasVolumeAutomation _clip;
    private readonly double               _oldVolume, _newVolume;

    public string Description => "Change volume";

    public UpdateVolumeCommand(IHasVolumeAutomation clip, double oldVolume, double newVolume)
    {
        _clip      = clip;
        _oldVolume = oldVolume;
        _newVolume = newVolume;
    }

    public void Execute() => _clip.Volume = _newVolume;
    public void Undo()    => _clip.Volume = _oldVolume;
}

/// <summary>Undo/redo for changing an <see cref="AudioClip"/>'s per-channel volume balance (backlog #10).</summary>
internal sealed class UpdateChannelVolumeCommand : IEditorCommand
{
    private readonly AudioClip _clip;
    private readonly double    _oldLeft,  _newLeft;
    private readonly double    _oldRight, _newRight;

    public string Description => "Change channel volume";

    public UpdateChannelVolumeCommand(AudioClip clip,
        double oldLeft, double oldRight, double newLeft, double newRight)
    {
        _clip     = clip;
        _oldLeft  = oldLeft;
        _oldRight = oldRight;
        _newLeft  = newLeft;
        _newRight = newRight;
    }

    public void Execute() { _clip.LeftVolume = _newLeft; _clip.RightVolume = _newRight; }
    public void Undo()    { _clip.LeftVolume = _oldLeft; _clip.RightVolume = _oldRight; }
}

/// <summary>Undo/redo for adding a timeline marker.</summary>
internal sealed class AddMarkerCommand : IEditorCommand
{
    private readonly List<TimelineMarker> _store;
    private readonly TimelineMarker       _marker;

    public string Description => $"Add marker \"{_marker.Label}\"";

    public AddMarkerCommand(List<TimelineMarker> store, TimelineMarker marker)
    {
        _store  = store;
        _marker = marker;
    }

    public void Execute() { if (!_store.Any(m => m.Id == _marker.Id)) _store.Add(_marker); }
    public void Undo()    => _store.RemoveAll(m => m.Id == _marker.Id);
}

/// <summary>Undo/redo for removing a timeline marker.</summary>
internal sealed class RemoveMarkerCommand : IEditorCommand
{
    private readonly List<TimelineMarker> _store;
    private readonly TimelineMarker       _marker;

    public string Description => $"Remove marker \"{_marker.Label}\"";

    public RemoveMarkerCommand(List<TimelineMarker> store, TimelineMarker marker)
    {
        _store  = store;
        _marker = marker;
    }

    public void Execute() => _store.RemoveAll(m => m.Id == _marker.Id);
    public void Undo()    { if (!_store.Any(m => m.Id == _marker.Id)) _store.Add(_marker); }
}

/// <summary>Undo/redo for renaming a marker and/or moving it to a new time (rename edits and ruler drags both go through this).</summary>
internal sealed class UpdateMarkerCommand : IEditorCommand
{
    private readonly TimelineMarker _marker;
    private readonly string         _fromLabel;
    private readonly double         _fromTime;
    private readonly string         _toLabel;
    private readonly double         _toTime;

    public string Description => "Update marker";

    public UpdateMarkerCommand(TimelineMarker marker, string fromLabel, double fromTime, string toLabel, double toTime)
    {
        _marker    = marker;
        _fromLabel = fromLabel;
        _fromTime  = fromTime;
        _toLabel   = toLabel;
        _toTime    = toTime;
    }

    public void Execute() { _marker.Label = _toLabel;   _marker.TimeSeconds = _toTime; }
    public void Undo()    { _marker.Label = _fromLabel; _marker.TimeSeconds = _fromTime; }
}

/// <summary>Undo/redo for changing an AudioClip's trim in/out points.</summary>
internal sealed class UpdateAudioTrimCommand : IEditorCommand
{
    private readonly AudioClip _clip;
    private readonly double    _oldStart, _oldEnd, _newStart, _newEnd;

    public string Description => "Trim audio clip";

    public UpdateAudioTrimCommand(AudioClip clip, double oldStart, double oldEnd, double newStart, double newEnd)
    {
        _clip     = clip;
        _oldStart = oldStart;
        _oldEnd   = oldEnd;
        _newStart = newStart;
        _newEnd   = newEnd;
    }

    public void Execute() { _clip.StartTrim = _newStart; _clip.EndTrim = _newEnd; }
    public void Undo()    { _clip.StartTrim = _oldStart; _clip.EndTrim = _oldEnd; }
}

/// <summary>Undo/redo for changing an AudioClip's fade-in / fade-out durations.</summary>
internal sealed class UpdateAudioFadeCommand : IEditorCommand
{
    private readonly AudioClip _clip;
    private readonly double    _oldFadeIn,  _newFadeIn;
    private readonly double    _oldFadeOut, _newFadeOut;

    public string Description => "Change audio fade";

    public UpdateAudioFadeCommand(AudioClip clip,
        double oldFadeIn, double oldFadeOut,
        double newFadeIn, double newFadeOut)
    {
        _clip       = clip;
        _oldFadeIn  = oldFadeIn;
        _oldFadeOut = oldFadeOut;
        _newFadeIn  = newFadeIn;
        _newFadeOut = newFadeOut;
    }

    public void Execute() { _clip.FadeInSeconds = _newFadeIn; _clip.FadeOutSeconds = _newFadeOut; }
    public void Undo()    { _clip.FadeInSeconds = _oldFadeIn; _clip.FadeOutSeconds = _oldFadeOut; }
}

/// <summary>Undo/redo for re-linking a clip's MEMFS source after project restore.</summary>
internal sealed class RelinkClipCommand : IEditorCommand
{
    private readonly TrackItem _item;
    private readonly string?   _oldMemFs, _newMemFs;

    // Where the media is stored and where it came from, both sides of the edit.
    //
    // Re-linking used to write the browser's session filesystem and nothing else, so a re-linked
    // clip was missing again the moment the project was reopened — the one repair the editor
    // offered did not survive being used (2026-09-05 audit, F14). Persisting it means undo has to
    // put all of it back, not just the session path.
    private readonly string? _oldExt,  _newExt;
    private readonly Guid?   _oldFileId,   _newFileId;
    private readonly long?   _oldFileSize, _newFileSize;
    private readonly string? _oldFileHash, _newFileHash;

    public string Description => $"Re-link \"{_item.Name}\"";

    public RelinkClipCommand(
        TrackItem item, string? oldMemFs, string newMemFs,
        string? newExt = null, Guid? newFileId = null,
        long? newFileSize = null, string? newFileHash = null)
    {
        _item      = item;
        _oldMemFs  = oldMemFs;
        _newMemFs  = newMemFs;

        _oldExt      = item.OpfsExt;
        _oldFileId   = item.SourceFileId;
        _oldFileSize = item.SourceFileSize;
        _oldFileHash = item.SourceContentHash;

        _newExt      = newExt ?? item.OpfsExt;
        _newFileId   = newFileId;
        _newFileSize = newFileSize;
        _newFileHash = newFileHash;
    }

    public void Execute()
    {
        if (_item is VideoClip vc) vc.MemFsName = _newMemFs;
        else if (_item is AudioClip ac) ac.MemFsName = _newMemFs;
        else if (_item is ImageClip ic) ic.MemFsName = _newMemFs;

        _item.OpfsExt           = _newExt;
        _item.SourceFileId      = _newFileId;
        _item.SourceFileSize    = _newFileSize;
        _item.SourceContentHash = _newFileHash;
        _item.IsMediaMissing    = false;
    }

    public void Undo()
    {
        if (_item is VideoClip vc) vc.MemFsName = _oldMemFs;
        else if (_item is AudioClip ac) ac.MemFsName = _oldMemFs;
        else if (_item is ImageClip ic) ic.MemFsName = _oldMemFs;

        _item.OpfsExt           = _oldExt;
        _item.SourceFileId      = _oldFileId;
        _item.SourceFileSize    = _oldFileSize;
        _item.SourceContentHash = _oldFileHash;
        _item.IsMediaMissing    = _oldMemFs is null;
    }
}

/// <summary>Undo/redo for changing per-clip visual effects.</summary>
internal sealed class UpdateClipEffectsCommand : IEditorCommand
{
    private readonly VideoClip    _clip;
    private readonly ClipEffects  _oldEffects, _newEffects;

    public string Description => "Change clip effects";

    public UpdateClipEffectsCommand(VideoClip clip, ClipEffects oldEffects, ClipEffects newEffects)
    {
        _clip       = clip;
        _oldEffects = oldEffects;
        _newEffects = newEffects;
    }

    public void Execute() => _clip.Effects = _newEffects;
    public void Undo()    => _clip.Effects = _oldEffects;
}

/// <summary>Undo/redo for adding a volume automation keyframe.</summary>
internal sealed class AddVolumeKeyframeCommand : IEditorCommand
{
    private readonly IHasVolumeAutomation _clip;
    private readonly VolumeKeyframe       _keyframe;

    public string Description => "Add volume keyframe";

    public AddVolumeKeyframeCommand(IHasVolumeAutomation clip, VolumeKeyframe keyframe)
    {
        _clip     = clip;
        _keyframe = keyframe;
    }

    public void Execute() { if (!_clip.VolumeAutomation.Any(k => k.Id == _keyframe.Id)) _clip.VolumeAutomation.Add(_keyframe); }
    public void Undo()    => _clip.VolumeAutomation.RemoveAll(k => k.Id == _keyframe.Id);
}

/// <summary>Undo/redo for updating a volume automation keyframe's position or volume.</summary>
internal sealed class UpdateVolumeKeyframeCommand : IEditorCommand
{
    private readonly VolumeKeyframe _keyframe;
    private readonly double         _oldPosition, _oldVolume;
    private readonly double         _newPosition, _newVolume;

    public string Description => "Update volume keyframe";

    public UpdateVolumeKeyframeCommand(VolumeKeyframe keyframe,
        double oldPosition, double oldVolume,
        double newPosition, double newVolume)
    {
        _keyframe    = keyframe;
        _oldPosition = oldPosition; _oldVolume = oldVolume;
        _newPosition = newPosition; _newVolume = newVolume;
    }

    public void Execute() { _keyframe.Position = _newPosition; _keyframe.Volume = _newVolume; }
    public void Undo()    { _keyframe.Position = _oldPosition; _keyframe.Volume = _oldVolume; }
}

/// <summary>Undo/redo for removing a single volume automation keyframe.</summary>
internal sealed class RemoveVolumeKeyframeCommand : IEditorCommand
{
    private readonly IHasVolumeAutomation _clip;
    private readonly VolumeKeyframe       _keyframe;

    public string Description => "Remove volume keyframe";

    public RemoveVolumeKeyframeCommand(IHasVolumeAutomation clip, VolumeKeyframe keyframe)
    {
        _clip     = clip;
        _keyframe = keyframe;
    }

    public void Execute() => _clip.VolumeAutomation.RemoveAll(k => k.Id == _keyframe.Id);
    public void Undo()    { if (!_clip.VolumeAutomation.Any(k => k.Id == _keyframe.Id)) _clip.VolumeAutomation.Add(_keyframe); }
}

/// <summary>Undo/redo for clearing all volume automation keyframes from a clip.</summary>
internal sealed class ClearVolumeAutomationCommand : IEditorCommand
{
    private readonly IHasVolumeAutomation  _clip;
    private readonly List<VolumeKeyframe>  _snapshot;

    public string Description => "Clear volume automation";

    public ClearVolumeAutomationCommand(IHasVolumeAutomation clip)
    {
        _clip     = clip;
        _snapshot = clip.VolumeAutomation.ToList();
    }

    public void Execute() => _clip.VolumeAutomation.Clear();

    public void Undo()
    {
        _clip.VolumeAutomation.Clear();
        _clip.VolumeAutomation.AddRange(_snapshot);
    }
}

/// <summary>Undo/redo for nudging a clip's timeline position by a delta (in seconds).</summary>
internal sealed class MoveClipCommand : IEditorCommand
{
    private readonly TrackItem _item;
    private readonly double    _delta;

    public string Description => "Move clip";

    public MoveClipCommand(TrackItem item, double delta)
    {
        _item  = item;
        _delta = delta;
    }

    public void Execute() => _item.TimelinePosition = Math.Max(0, _item.TimelinePosition + _delta);
    public void Undo()    => _item.TimelinePosition = Math.Max(0, _item.TimelinePosition - _delta);
}

/// <summary>
/// Undo/redo for a pointer-drag move where the item is already at the new position.
/// Execute sets the item to <see cref="_to"/>; Undo restores <see cref="_from"/>.
/// This is used instead of <see cref="MoveClipCommand"/> when live preview already
/// mutated TimelinePosition during the drag.
/// </summary>
internal sealed class SetClipPositionCommand : IEditorCommand
{
    private readonly TrackItem _item;
    private readonly double    _from;
    private readonly double    _to;

    public string Description => "Move clip";

    public SetClipPositionCommand(TrackItem item, double from, double to)
    {
        _item = item;
        _from = from;
        _to   = to;
    }

    public void Execute() => _item.TimelinePosition = _to;
    public void Undo()    => _item.TimelinePosition = _from;
}

/// <summary>
/// Commits a pointer-drag move that also changed which track the clip lives on (item #25) — a
/// live-preview drag already relocated <see cref="_item"/> into <see cref="_toTrack"/>'s
/// <c>Items</c> list with its final position before this command is pushed, matching
/// <see cref="SetClipPositionCommand"/>'s "don't re-execute, already applied" convention;
/// <see cref="Execute"/> only re-applies the move for a redo after an undo.
/// </summary>
internal sealed class MoveClipToTrackCommand : IEditorCommand
{
    private readonly TimelineTrack _fromTrack;
    private readonly TimelineTrack _toTrack;
    private readonly TrackItem     _item;
    private readonly double        _fromPosition;
    private readonly double        _toPosition;

    public string Description => "Move clip to another track";

    public MoveClipToTrackCommand(
        TimelineTrack fromTrack, TimelineTrack toTrack, TrackItem item,
        double fromPosition, double toPosition)
    {
        _fromTrack    = fromTrack;
        _toTrack      = toTrack;
        _item         = item;
        _fromPosition = fromPosition;
        _toPosition   = toPosition;
    }

    public void Execute()
    {
        if (!_toTrack.Items.Contains(_item))
        {
            _fromTrack.Items.Remove(_item);
            _toTrack.Items.Add(_item);
        }
        _item.TimelinePosition = _toPosition;
    }

    public void Undo()
    {
        _toTrack.Items.Remove(_item);
        _item.TimelinePosition = _fromPosition;
        _fromTrack.Items.Add(_item);
    }
}

/// <summary>
/// Ripple-delete: removes one item and shifts every subsequent item on the same track
/// left by the removed clip's duration.  Undo re-inserts the item and shifts right again.
/// </summary>
internal sealed class RippleDeleteCommand : IEditorCommand
{
    private readonly TimelineTrack      _track;
    private readonly TrackItem          _item;
    private readonly int                _index;
    private readonly List<TrackItem>    _shifted;  // items that were shifted (sorted by position)
    private readonly double             _shiftBy;  // amount each was shifted (= item.Duration)

    public string Description => "Ripple delete";

    public RippleDeleteCommand(TimelineTrack track, TrackItem item, int index,
        List<TrackItem> shifted, double shiftBy)
    {
        _track   = track;
        _item    = item;
        _index   = index;
        _shifted = shifted;
        _shiftBy = shiftBy;
    }

    public void Execute()
    {
        _track.Items.Remove(_item);
        foreach (var i in _shifted)
            i.TimelinePosition = Math.Max(0, i.TimelinePosition - _shiftBy);
    }

    public void Undo()
    {
        foreach (var i in _shifted)
            i.TimelinePosition += _shiftBy;
        _item.Order = _index;
        _track.Items.Insert(_index, _item);
    }
}

/// <summary>
/// Ripple-move: commits a pointer-drag and shifts all clips after the clip's
/// new position (or old position, whichever is further right) by the same delta.
/// </summary>
internal sealed class RippleCommitDraggedCommand : IEditorCommand
{
    private readonly TrackItem           _item;
    private readonly double              _from;
    private readonly double              _to;
    private readonly List<TrackItem>     _shifted;
    private readonly double              _delta;   // to - from

    public string Description => "Ripple move";

    public RippleCommitDraggedCommand(TrackItem item, double from, double to,
        List<TrackItem> shifted, double delta)
    {
        _item    = item;
        _from    = from;
        _to      = to;
        _shifted = shifted;
        _delta   = delta;
    }

    public void Execute()
    {
        _item.TimelinePosition = _to;
        foreach (var i in _shifted) i.TimelinePosition += _delta;
    }

    public void Undo()
    {
        _item.TimelinePosition = _from;
        foreach (var i in _shifted) i.TimelinePosition -= _delta;
    }
}

/// <summary>
/// Ripple-insert: adds a new clip at a position that overlaps existing clips by shifting
/// every clip at or after that position later by the new clip's duration, opening up room
/// for it instead of the crude "dump at end of track" fallback (item #25).
/// </summary>
internal sealed class InsertClipRippleCommand : IEditorCommand
{
    private readonly TimelineTrack   _track;
    private readonly TrackItem       _item;
    private readonly List<TrackItem> _shifted;
    private readonly double          _shiftBy;

    public string Description => $"Insert {_item.Name} (ripple)";

    public InsertClipRippleCommand(TimelineTrack track, TrackItem item, List<TrackItem> shifted, double shiftBy)
    {
        _track   = track;
        _item    = item;
        _shifted = shifted;
        _shiftBy = shiftBy;
    }

    public void Execute()
    {
        if (!_track.Items.Any(i => i.Id == _item.Id))
        {
            // Item #59 — insert at the chronologically correct index, don't append. Order is
            // derived from list index (ClipStore.RenumberItems) and is NOT cosmetic: the timeline
            // render loop sequences by it, and so does ExportService's concat pipeline (via
            // TimelineTrack.VideoClips). Appending a clip that lands chronologically *first* —
            // exactly what ripple-insert does, since everything at/after its position is shifted
            // later to make room — therefore both drew and EXPORTED the track in the wrong order.
            //
            // The right slot is immediately before the earliest-indexed item being shifted out of
            // the way, which is precisely what "make room here" means. Derived from _shifted
            // rather than by scanning positions so it stays correct even if Items isn't already
            // sorted chronologically. Empty _shifted (nothing after it) => append.
            var insertAt = _track.Items.Count;
            foreach (var s in _shifted)
            {
                var idx = _track.Items.IndexOf(s);
                if (idx >= 0 && idx < insertAt) insertAt = idx;
            }

            _item.Order = insertAt;
            _track.Items.Insert(insertAt, _item);

            // Redo() re-runs Execute() without ClipStore's own post-insert RenumberItems call, so
            // renumber here too — otherwise a redo leaves every later item's Order stale and
            // colliding with the just-inserted one.
            for (var i = 0; i < _track.Items.Count; i++)
                _track.Items[i].Order = i;
        }
        foreach (var i in _shifted) i.TimelinePosition += _shiftBy;
    }

    public void Undo()
    {
        _track.Items.Remove(_item);
        foreach (var i in _shifted) i.TimelinePosition -= _shiftBy;

        // Keep Order contiguous and chronological after the removal, mirroring Execute().
        for (var i = 0; i < _track.Items.Count; i++)
            _track.Items[i].Order = i;
    }
}

/// <summary>
/// Overwrite-insert (item #49): adds a new video clip at a position that overlaps existing
/// clips by trimming/splitting/removing whatever's underneath instead of shifting subsequent
/// clips later (contrast <see cref="InsertClipRippleCommand"/>). <paramref name="changes"/> is
/// the full set of (original clip, its replacement — 0, 1, or 2 clips) pairs already resolved by
/// <see cref="Ben.Video.Editor.Services.OverwriteEditCalculator"/>.
/// </summary>
internal sealed class OverwriteInsertCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly VideoClip _newClip;
    private readonly double _position;
    private readonly List<(VideoClip Original, List<VideoClip> Replacements)> _changes;

    public string Description => $"Overwrite with {_newClip.Name}";

    public OverwriteInsertCommand(
        TimelineTrack track,
        VideoClip newClip,
        double position,
        List<(VideoClip Original, List<VideoClip> Replacements)> changes)
    {
        _track    = track;
        _newClip  = newClip;
        _position = position;
        _changes  = changes;
    }

    public void Execute()
    {
        foreach (var (original, replacements) in _changes)
        {
            var idx = _track.Items.FindIndex(i => i.Id == original.Id);
            if (idx < 0) continue;
            _track.Items.RemoveAt(idx);
            _track.Items.InsertRange(idx, replacements);
        }

        _newClip.TimelinePosition = _position;
        if (!_track.Items.Any(i => i.Id == _newClip.Id))
            _track.Items.Add(_newClip);

        Renumber();
    }

    public void Undo()
    {
        _track.Items.Remove(_newClip);

        foreach (var (original, replacements) in _changes)
        {
            foreach (var r in replacements)
            {
                var idx = _track.Items.FindIndex(i => i.Id == r.Id);
                if (idx >= 0) _track.Items.RemoveAt(idx);
            }
            _track.Items.Add(original);
        }

        Renumber();
    }

    private void Renumber()
    {
        var ordered = _track.Items.OrderBy(i => i.TimelinePosition).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
    }
}

/// <summary>
/// Undo/redo for changing the compositing order (z-order) of a <see cref="TimelineTrack"/>.
/// Higher Order value = lower in the stack = rendered beneath tracks with lower Order.
/// </summary>
internal sealed class ReorderTrackCommand : IEditorCommand
{
    private readonly List<TimelineTrack> _tracks;
    private readonly Guid                _trackId;
    private readonly int                 _oldOrder;
    private readonly int                 _newOrder;

    public string Description => "Reorder track";

    public ReorderTrackCommand(List<TimelineTrack> tracks, Guid trackId, int oldOrder, int newOrder)
    {
        _tracks   = tracks;
        _trackId  = trackId;
        _oldOrder = oldOrder;
        _newOrder = newOrder;
    }

    public void Execute() => ApplyOrder(_newOrder);
    public void Undo()    => ApplyOrder(_oldOrder);

    private void ApplyOrder(int targetOrder)
    {
        var moving = _tracks.FirstOrDefault(t => t.Id == _trackId);
        if (moving is null) return;

        var displaced = _tracks.FirstOrDefault(t => t.Order == targetOrder && t.Id != _trackId);
        if (displaced is not null)
            displaced.Order = moving.Order;   // swap

        moving.Order = targetOrder;
    }
}

/// <summary>
/// Undo/redo for the "Separate Audio" operation.
/// Execute: marks the source <see cref="VideoClip"/> as <c>MuteAudio = true</c> and adds the
/// detached <see cref="AudioClip"/> to the target audio track.
/// Undo: clears <c>MuteAudio</c> and removes the detached clip.
/// </summary>
internal sealed class DetachAudioCommand : IEditorCommand
{
    private readonly VideoClip     _sourceClip;
    private readonly AudioClip     _audioClip;
    private readonly TimelineTrack _audioTrack;

    public string Description => "Separate audio";

    public DetachAudioCommand(VideoClip sourceClip, AudioClip audioClip, TimelineTrack audioTrack)
    {
        _sourceClip = sourceClip;
        _audioClip  = audioClip;
        _audioTrack = audioTrack;
    }

    public void Execute()
    {
        _sourceClip.MuteAudio = true;
        if (!_audioTrack.Items.Contains(_audioClip))
        {
            _audioClip.Order = _audioTrack.Items.Count;
            _audioTrack.Items.Add(_audioClip);
        }
    }

    public void Undo()
    {
        _sourceClip.MuteAudio = false;
        _audioTrack.Items.Remove(_audioClip);
    }
}

/// <summary>Undo/redo for adding an <see cref="ImageClip"/> to a track.</summary>
internal sealed class AddImageClipCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly ImageClip     _clip;
    private readonly int           _index;

    public string Description => "Add image clip";

    public AddImageClipCommand(TimelineTrack track, ImageClip clip, int index)
    {
        _track = track;
        _clip  = clip;
        _index = index;
    }

    public void Execute()
    {
        if (!_track.Items.Contains(_clip))
        {
            _clip.Order = _index;
            _track.Items.Add(_clip);
        }
    }

    public void Undo() => _track.Items.Remove(_clip);
}

/// <summary>Undo/redo for removing an <see cref="ImageClip"/> from a track.</summary>
internal sealed class RemoveImageClipCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly ImageClip     _clip;
    private readonly int           _index;

    public string Description => "Remove image clip";

    public RemoveImageClipCommand(TimelineTrack track, ImageClip clip, int index)
    {
        _track = track;
        _clip  = clip;
        _index = index;
    }

    public void Execute() => _track.Items.Remove(_clip);

    public void Undo()
    {
        if (!_track.Items.Contains(_clip))
        {
            _clip.Order = _index;
            _track.Items.Add(_clip);
        }
    }
}

// ── Phase 29: Applied Effects commands ───────────────────────────────────────────────

internal sealed class AddEffectCommand : IEditorCommand
{
    private readonly TrackItem  _item;
    private readonly Ben.Video.Editor.Effects.AppliedEffect _effect;

    public AddEffectCommand(TrackItem item, Ben.Video.Editor.Effects.AppliedEffect effect)
    {
        _item   = item;
        _effect = effect;
    }

    public void Execute() => GetEffectsList(_item).Add(_effect);
    public void Undo()    => GetEffectsList(_item).Remove(_effect);
    public string Description => $"Add effect {_effect.EffectId}";

    private static List<Ben.Video.Editor.Effects.AppliedEffect> GetEffectsList(TrackItem item) => item switch
    {
        VideoClip vc => vc.AppliedEffects,
        ImageClip ic => ic.AppliedEffects,
        _ => throw new InvalidOperationException($"Item type {item.GetType().Name} does not support effects."),
    };
}

internal sealed class RemoveEffectCommand : IEditorCommand
{
    private readonly TrackItem  _item;
    private readonly Ben.Video.Editor.Effects.AppliedEffect _effect;
    private int                 _index;

    public RemoveEffectCommand(TrackItem item, Ben.Video.Editor.Effects.AppliedEffect effect)
    {
        _item   = item;
        _effect = effect;
    }

    public void Execute()
    {
        var list = GetEffectsList(_item);
        _index = list.IndexOf(_effect);
        list.Remove(_effect);
    }

    public void Undo()
    {
        var list = GetEffectsList(_item);
        if (_index >= 0 && _index <= list.Count)
            list.Insert(_index, _effect);
        else
            list.Add(_effect);
    }

    public string Description => $"Remove effect {_effect.EffectId}";

    private static List<Ben.Video.Editor.Effects.AppliedEffect> GetEffectsList(TrackItem item) => item switch
    {
        VideoClip vc => vc.AppliedEffects,
        ImageClip ic => ic.AppliedEffects,
        _ => throw new InvalidOperationException($"Item type {item.GetType().Name} does not support effects."),
    };
}

internal sealed class UpdateEffectParameterCommand : IEditorCommand
{
    private readonly Ben.Video.Editor.Effects.AppliedEffect _effect;
    private readonly string  _key;
    private readonly double  _newValue;
    private readonly double  _oldValue;

    public UpdateEffectParameterCommand(
        Ben.Video.Editor.Effects.AppliedEffect effect, string key, double newValue)
    {
        _effect   = effect;
        _key      = key;
        _newValue = newValue;
        _oldValue = effect.Parameters.TryGetValue(key, out var v) ? v : 0.0;
    }

    public void Execute() => _effect.Parameters[_key] = _newValue;
    public void Undo()    => _effect.Parameters[_key] = _oldValue;
    public string Description => $"Update {_key}";
}

// ── Phase 36: Track locking ───────────────────────────────────────────────────

/// <summary>Bringing a source into the media bin.</summary>
internal sealed class AddToBinCommand : IEditorCommand
{
    private readonly List<TrackItem> _bin;
    private readonly TrackItem       _item;
    private readonly int             _index;

    public string Description => "Import media";

    public AddToBinCommand(List<TrackItem> bin, TrackItem item, int index)
    {
        _bin   = bin;
        _item  = item;
        _index = index;
    }

    public void Execute()
    {
        if (!_bin.Contains(_item)) _bin.Insert(Math.Min(_index, _bin.Count), _item);
    }

    public void Undo() => _bin.Remove(_item);
}

/// <summary>Taking a source out of the media bin, and putting it back where it was on undo.</summary>
internal sealed class RemoveFromBinCommand : IEditorCommand
{
    private readonly List<TrackItem> _bin;
    private readonly TrackItem       _item;
    private readonly int             _index;

    public string Description => "Remove from media";

    public RemoveFromBinCommand(List<TrackItem> bin, TrackItem item, int index)
    {
        _bin   = bin;
        _item  = item;
        _index = index;
    }

    public void Execute() => _bin.Remove(_item);

    public void Undo()
    {
        if (!_bin.Contains(_item)) _bin.Insert(Math.Min(_index, _bin.Count), _item);
    }
}

/// <summary>Muting or unmuting a track, so it can be undone like any other edit.</summary>
internal sealed class MuteTrackCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly bool          _muted;

    public string Description => _muted ? "Mute track" : "Unmute track";

    public MuteTrackCommand(TimelineTrack track, bool muted)
    {
        _track = track;
        _muted = muted;
    }

    public void Execute() => _track.IsMuted = _muted;
    public void Undo()    => _track.IsMuted = !_muted;
}

/// <summary>Undo/redo for locking or unlocking a <see cref="TimelineTrack"/>.</summary>
internal sealed class LockTrackCommand : IEditorCommand
{
    private readonly TimelineTrack _track;
    private readonly bool          _newLocked;

    public string Description => _newLocked ? "Lock track" : "Unlock track";

    public LockTrackCommand(TimelineTrack track, bool newLocked)
    {
        _track     = track;
        _newLocked = newLocked;
    }

    public void Execute() => _track.IsLocked = _newLocked;
    public void Undo()    => _track.IsLocked = !_newLocked;
}

/// <summary>Undo/redo for adding a new (empty) video or audio track.</summary>
internal sealed class AddTrackCommand : IEditorCommand
{
    private readonly List<TimelineTrack> _tracks;
    private readonly TimelineTrack       _track;

    public string Description => $"Add {_track.Type.ToString().ToLowerInvariant()} track";

    public AddTrackCommand(List<TimelineTrack> tracks, TimelineTrack track)
    {
        _tracks = tracks;
        _track  = track;
    }

    public void Execute()
    {
        if (!_tracks.Any(t => t.Id == _track.Id))
            _tracks.Add(_track);
    }

    public void Undo() => _tracks.RemoveAll(t => t.Id == _track.Id);
}

/// <summary>
/// Undo/redo for removing an empty track. Re-inserts at its original index on undo so
/// the track's <see cref="TimelineTrack.Order"/> position among its siblings is restored
/// (renumbering happens the same way on both the remove and the undo-reinsert path).
/// </summary>
internal sealed class RemoveTrackCommand : IEditorCommand
{
    private readonly List<TimelineTrack> _tracks;
    private readonly TimelineTrack       _track;
    private readonly int                 _index;
    private readonly Action              _renumber;

    public string Description => "Remove track";

    public RemoveTrackCommand(List<TimelineTrack> tracks, TimelineTrack track, int index, Action renumber)
    {
        _tracks   = tracks;
        _track    = track;
        _index    = index;
        _renumber = renumber;
    }

    public void Execute()
    {
        _tracks.Remove(_track);
        _renumber();
    }

    public void Undo()
    {
        var at = Math.Min(_index, _tracks.Count);
        _tracks.Insert(at, _track);
        _renumber();
    }
}

// ── Phase 53b: Callout / ClipArt / Image property commits ────────────────────

/// <summary>
/// Generic property-commit command for <see cref="CalloutClip"/> that captures
/// a snapshot of a single double property before and after a change.
/// Used by editor panels so slider adjustments are undoable.
/// </summary>
internal sealed class CommitCalloutPropertyCommand : IEditorCommand
{
    private readonly CalloutClip _clip;
    private readonly string      _propertyPath;
    private readonly Action<CalloutClip> _apply;
    private readonly Action<CalloutClip> _revert;

    public string Description => $"Edit callout {_propertyPath}";

    public CommitCalloutPropertyCommand(
        CalloutClip clip,
        string      propertyPath,
        Action<CalloutClip> apply,
        Action<CalloutClip> revert)
    {
        _clip         = clip;
        _propertyPath = propertyPath;
        _apply        = apply;
        _revert       = revert;
    }

    public void Execute() => _apply(_clip);
    public void Undo()    => _revert(_clip);
}

/// <summary>Undo/redo for the set of areas hidden on a clip.</summary>
/// <remarks>
/// Holds both lists outright rather than a closure that rebuilds one. Getting undo wrong here
/// means somebody exports a face they believe they covered.
/// </remarks>
internal sealed class SetRedactionsCommand : IEditorCommand
{
    private readonly Action<List<RedactionRegion>> _set;
    private readonly List<RedactionRegion> _after, _before;
    private readonly string _clipName;

    public string Description => $"Hidden areas on \"{_clipName}\"";

    public SetRedactionsCommand(
        Action<List<RedactionRegion>> set,
        List<RedactionRegion> after, List<RedactionRegion> before, string clipName)
    {
        _set      = set;
        _after    = after;
        _before   = before;
        _clipName = clipName;
    }

    // Fresh copies each time: the same command can be undone and redone repeatedly, and handing
    // out the stored list would let the panel edit the history.
    public void Execute() => _set([.. _after.Select(r => r with { })]);
    public void Undo()    => _set([.. _before.Select(r => r with { })]);
}

/// <summary>Undo/redo for a committed <see cref="TextOverlay"/> property change.</summary>
/// <remarks>
/// Titles were the one thing on the timeline whose edits could not be undone. Changing a font,
/// a colour, an alignment or the words themselves went straight into the model with nothing pushed
/// onto the stack, so Ctrl+Z after a title edit undid whatever had happened before it instead —
/// which is worse than doing nothing (2026-09-05 audit, titles-4).
/// </remarks>
internal sealed class CommitTextOverlayPropertyCommand : IEditorCommand
{
    private readonly TextOverlay _overlay;
    private readonly string      _propertyPath;
    private readonly Action<TextOverlay> _apply;
    private readonly Action<TextOverlay> _revert;

    public string Description => $"Edit title {_propertyPath}";

    public CommitTextOverlayPropertyCommand(
        TextOverlay overlay,
        string      propertyPath,
        Action<TextOverlay> apply,
        Action<TextOverlay> revert)
    {
        _overlay      = overlay;
        _propertyPath = propertyPath;
        _apply        = apply;
        _revert       = revert;
    }

    public void Execute() => _apply(_overlay);
    public void Undo()    => _revert(_overlay);
}

/// <summary>Undo/redo for a committed <see cref="ClipArtClip"/> property change — mirrors
/// <see cref="CommitCalloutPropertyCommand"/> exactly.</summary>
internal sealed class CommitClipArtPropertyCommand : IEditorCommand
{
    private readonly ClipArtClip _clip;
    private readonly string      _propertyPath;
    private readonly Action<ClipArtClip> _apply;
    private readonly Action<ClipArtClip> _revert;

    public string Description => $"Edit clip art {_propertyPath}";

    public CommitClipArtPropertyCommand(
        ClipArtClip clip,
        string      propertyPath,
        Action<ClipArtClip> apply,
        Action<ClipArtClip> revert)
    {
        _clip         = clip;
        _propertyPath = propertyPath;
        _apply        = apply;
        _revert       = revert;
    }

    public void Execute() => _apply(_clip);
    public void Undo()    => _revert(_clip);
}

/// <summary>Undo/redo for changing an <see cref="ImageClip"/>'s display duration.</summary>
internal sealed class CommitImageDurationCommand : IEditorCommand
{
    private readonly ImageClip _clip;
    private readonly double    _oldDuration;
    private readonly double    _newDuration;

    public string Description => "Set image duration";

    public CommitImageDurationCommand(ImageClip clip, double oldDuration, double newDuration)
    {
        _clip        = clip;
        _oldDuration = oldDuration;
        _newDuration = newDuration;
    }

    public void Execute() => _clip.Duration = _newDuration;
    public void Undo()    => _clip.Duration = _oldDuration;
}

/// <summary>
/// Undo/redo for changing an existing <see cref="Transition"/>'s style/duration (the
/// <c>TransitionEditor</c> "Apply" button) — captures <see cref="Transition.Name"/> and
/// <see cref="Transition.TimelinePosition"/> too, since <c>ClipStore.UpdateTransition</c>
/// derives both from style/duration on every apply.
/// </summary>
/// <summary>Bundles multiple commands into a single undo/redo unit (item #57 T4 — "Apply style
/// to all junctions" should undo in one click, not once per junction it touched).</summary>
internal sealed class CompositeCommand : IEditorCommand
{
    private readonly List<IEditorCommand> _commands;

    public string Description { get; }

    public CompositeCommand(string description, List<IEditorCommand> commands)
    {
        Description = description;
        _commands   = commands;
    }

    public void Execute()
    {
        foreach (var command in _commands) command.Execute();
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
    }
}

internal sealed class UpdateTransitionCommand : IEditorCommand
{
    private readonly Transition       _transition;
    private readonly TransitionStyle  _oldStyle;
    private readonly double           _oldDuration;
    private readonly string           _oldName;
    private readonly double           _oldPosition;
    private readonly TransitionStyle  _newStyle;
    private readonly double           _newDuration;
    private readonly string           _newName;
    private readonly double           _newPosition;

    public string Description => $"Change transition to {_newStyle}";

    public UpdateTransitionCommand(
        Transition transition,
        TransitionStyle oldStyle, double oldDuration, string oldName, double oldPosition,
        TransitionStyle newStyle, double newDuration, string newName, double newPosition)
    {
        _transition  = transition;
        _oldStyle    = oldStyle;
        _oldDuration = oldDuration;
        _oldName     = oldName;
        _oldPosition = oldPosition;
        _newStyle    = newStyle;
        _newDuration = newDuration;
        _newName     = newName;
        _newPosition = newPosition;
    }

    public void Execute()
    {
        _transition.Style            = _newStyle;
        _transition.Duration         = _newDuration;
        _transition.Name             = _newName;
        _transition.TimelinePosition = _newPosition;
    }

    public void Undo()
    {
        _transition.Style            = _oldStyle;
        _transition.Duration         = _oldDuration;
        _transition.Name             = _oldName;
        _transition.TimelinePosition = _oldPosition;
    }
}

/// <summary>
/// Undo/redo for a keyframe-branch canvas edit (item #63) — body-drag, resize/HUD type-in, and
/// arrow-key nudge all upsert a <c>MotionKeyframe</c> via <c>MotionKeyframeService</c>, a
/// separate scoped service from <see cref="ClipStore"/> that owns its own data and never
/// participated in the undo stack at all before this. Fully generic (plain <see cref="Action"/>
/// closures, no dependency on <c>MotionKeyframeService</c> or <c>MotionKeyframe</c> types here) so
/// this file doesn't need a new namespace dependency — the caller (<see cref="ClipStore"/>)
/// constructs closures that call back into whichever <c>MotionKeyframeService</c> instance it was
/// given.
/// </summary>
internal sealed class CommitMotionKeyframeCommand : IEditorCommand
{
    private readonly Action _apply;
    private readonly Action _revert;

    public string Description { get; }

    public CommitMotionKeyframeCommand(string description, Action apply, Action revert)
    {
        Description = description;
        _apply      = apply;
        _revert     = revert;
    }

    public void Execute() => _apply();
    public void Undo()    => _revert();
}

/// <summary>
/// Undo/redo for linking or unlinking two <see cref="TrackItem"/>s (item #52 — J-cuts/L-cuts).
/// A link is symmetric: both items' <see cref="TrackItem.LinkedClipId"/> point at each other.
/// Constructed with the state to move *to*; <see cref="Execute"/> applies it, <see cref="Undo"/>
/// restores each item's previous (possibly-null) partner.
/// </summary>
internal sealed class LinkClipsCommand : IEditorCommand
{
    private readonly TrackItem _a;
    private readonly TrackItem _b;
    private readonly Guid?     _oldALink;
    private readonly Guid?     _oldBLink;
    private readonly Guid?     _newALink;
    private readonly Guid?     _newBLink;

    public string Description => _newALink.HasValue ? $"Link {_a.Name} + {_b.Name}" : $"Unlink {_a.Name} + {_b.Name}";

    public LinkClipsCommand(TrackItem a, TrackItem b, Guid? oldALink, Guid? oldBLink, Guid? newALink, Guid? newBLink)
    {
        _a = a; _b = b;
        _oldALink = oldALink; _oldBLink = oldBLink;
        _newALink = newALink; _newBLink = newBLink;
    }

    public void Execute() { _a.LinkedClipId = _newALink; _b.LinkedClipId = _newBLink; }
    public void Undo()    { _a.LinkedClipId = _oldALink; _b.LinkedClipId = _oldBLink; }
}
