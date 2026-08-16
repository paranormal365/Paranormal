// Pointer-based bridge for dragging a clip from ClipBrowser onto a VideoTimeline track
// (backlog item #24 — replaces the previous native HTML5 drag-and-drop, which required a
// window.__bvDragClipId global handoff and doesn't compose with the rest of the timeline's
// pointer-based interactions). ClipBrowser and VideoTimeline are sibling components with no
// shared DOM ancestor, so this module-scoped state is the bridge between them: VideoTimeline
// registers itself once as the drop target; ClipBrowser calls startClipDrag() on pointerdown
// and this module owns the rest of the gesture (document-level pointermove/pointerup, since the
// drag crosses out of ClipBrowser's own DOM subtree into VideoTimeline's).
//
// Deliberately does not reproduce the old native-drag path's live snap-guide-line visual during
// the drag — only the drop-target track highlight. The actual drop position still snaps
// correctly (computed once, in C#, at pointerup) via the same SnapEngine every other drop path
// uses; only the live guide line while dragging is a (documented) reduced-fidelity trade-off.

let _dropTargetRef = null; // DotNetObjectReference<VideoTimeline>
let _dragSourceRef = null; // DotNetObjectReference<ClipBrowser>
let _dragState      = null; // { clipId, hoveredTrackEl }

export function registerDropTarget(dotNetRef) { _dropTargetRef = dotNetRef; }
export function unregisterDropTarget()        { _dropTargetRef = null; }

export function registerDragSource(dotNetRef) { _dragSourceRef = dotNetRef; }
export function unregisterDragSource()        { _dragSourceRef = null; }

function findTrackEl(x, y) {
    const el = document.elementFromPoint(x, y);
    return el?.closest('.bv-track[data-track-id]') ?? null;
}

function onPointerMove(e) {
    if (!_dragState) return;
    const trackEl = findTrackEl(e.clientX, e.clientY);
    if (trackEl !== _dragState.hoveredTrackEl) {
        _dragState.hoveredTrackEl?.classList.remove('bv-track--drop-target');
        trackEl?.classList.add('bv-track--drop-target');
        _dragState.hoveredTrackEl = trackEl;
    }
}

function onPointerUp(e) {
    if (!_dragState) return;
    document.removeEventListener('pointermove', onPointerMove);
    document.removeEventListener('pointerup', onPointerUp);
    document.removeEventListener('pointercancel', onPointerUp);

    _dragState.hoveredTrackEl?.classList.remove('bv-track--drop-target');
    const trackEl = findTrackEl(e.clientX, e.clientY);
    const trackId = trackEl?.getAttribute('data-track-id') ?? null;
    const clipId  = _dragState.clipId;
    const clientX = e.clientX;
    _dragState = null;

    if (trackId && _dropTargetRef) {
        _dropTargetRef.invokeMethodAsync('HandlePointerDropFromJs', clipId, trackId, clientX);
    }
    _dragSourceRef?.invokeMethodAsync('OnClipDragEnded');
}

/// Starts a pointer-driven drag for the given clip id (a Guid, passed as its string form).
/// No-op if a drag is already in progress.
export function startClipDrag(clipId) {
    if (_dragState) return;
    _dragState = { clipId, hoveredTrackEl: null };
    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
}

// ── Transitions gallery → timeline junction pointer-based drop (item #57 T3) ──────────────
//
// Parallel to the clip-drag bridge above, but the source is a TransitionStyle (a string enum
// name, not a Guid) and the drop targets are the two kinds of element VideoTimeline already
// renders at every valid junction: the dashed "+" insert button (a brand new junction — carries
// data-from-id/data-to-id/data-track-id) and an existing transition chip (a style replace —
// carries data-item-id). Reusing those exact rendered elements as hit targets, via
// elementFromPoint + closest(), is simpler and more precise than reimplementing "nearest
// junction within a px threshold" distance math the plan sketched: every element this can land
// on is already exactly where a drop should be valid, nothing else.

let _transitionDropRef       = null; // DotNetObjectReference<VideoTimeline>
let _transitionDragSourceRef = null; // DotNetObjectReference<AssetBrowser>
let _transitionDragState     = null; // { style, hoveredEl }

export function registerTransitionDropTarget(dotNetRef) { _transitionDropRef = dotNetRef; }
export function unregisterTransitionDropTarget()        { _transitionDropRef = null; }

export function registerTransitionDragSource(dotNetRef) { _transitionDragSourceRef = dotNetRef; }
export function unregisterTransitionDragSource()        { _transitionDragSourceRef = null; }

function findTransitionDropEl(x, y) {
    const el = document.elementFromPoint(x, y);
    return el?.closest('.bv-transition-insert, .bv-transition-chip') ?? null;
}

function onTransitionPointerMove(e) {
    if (!_transitionDragState) return;
    const dropEl = findTransitionDropEl(e.clientX, e.clientY);
    if (dropEl !== _transitionDragState.hoveredEl) {
        _transitionDragState.hoveredEl?.classList.remove('bv-transition-drop-hover');
        dropEl?.classList.add('bv-transition-drop-hover');
        _transitionDragState.hoveredEl = dropEl;
    }
}

function onTransitionPointerUp(e) {
    if (!_transitionDragState) return;
    document.removeEventListener('pointermove', onTransitionPointerMove);
    document.removeEventListener('pointerup', onTransitionPointerUp);
    document.removeEventListener('pointercancel', onTransitionPointerUp);

    _transitionDragState.hoveredEl?.classList.remove('bv-transition-drop-hover');
    const dropEl = findTransitionDropEl(e.clientX, e.clientY);
    const style  = _transitionDragState.style;
    _transitionDragState = null;

    if (dropEl && _transitionDropRef) {
        if (dropEl.classList.contains('bv-transition-chip')) {
            const transitionId = dropEl.getAttribute('data-item-id');
            if (transitionId) {
                _transitionDropRef.invokeMethodAsync('HandleTransitionDropOnChip', transitionId, style);
            }
        } else {
            const trackId = dropEl.getAttribute('data-track-id');
            const fromId  = dropEl.getAttribute('data-from-id');
            const toId    = dropEl.getAttribute('data-to-id');
            if (trackId && fromId && toId) {
                _transitionDropRef.invokeMethodAsync('HandleTransitionDropOnJunction', trackId, fromId, toId, style);
            }
        }
    }
    _transitionDragSourceRef?.invokeMethodAsync('OnTransitionDragEnded');
}

/// Starts a pointer-driven drag for the given TransitionStyle (its C# enum name, e.g. "Fade").
/// No-op if a drag is already in progress.
export function startTransitionDrag(style) {
    if (_transitionDragState) return;
    _transitionDragState = { style, hoveredEl: null };
    document.addEventListener('pointermove', onTransitionPointerMove);
    document.addEventListener('pointerup', onTransitionPointerUp);
    document.addEventListener('pointercancel', onTransitionPointerUp);
}
