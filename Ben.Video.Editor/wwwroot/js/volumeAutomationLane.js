// volumeAutomationLane.js
// Handles all SVG rendering and pointer interaction for the volume automation lane.
// Called from VolumeAutomationLane.razor via IJSRuntime module isolation.
//
// Coordinate mapping:
//   x : 0 → svgWidth   ←→   position : 0.0 → 1.0
//   y : 0 → laneHeight ←→   volume   : 2.0 → 0.0  (top = loud, bottom = silent)

// ── Constants ────────────────────────────────────────────────────────────────

const RULER_WIDTH    = 32;   // px reserved for dB scale labels
const HANDLE_RADIUS  = 7;    // px hit radius for existing handles
const LINE_HIT_TOL   = 7;    // px distance from polyline to trigger insert
const VOL_MAX        = 2.0;
const VOL_MIN        = 0.0;
const DB_LABELS      = [6, 0, -6, -12, -20, -40]; // displayed on ruler

// ── Per-SVG state ─────────────────────────────────────────────────────────────

const _lanes = new Map(); // svgEl → LaneState

class LaneState {
    constructor(svg, dotnet, keyframes, height) {
        this.svg       = svg;
        this.dotnet    = dotnet;
        this.keyframes = keyframes.slice(); // [{id, position, volume}, ...]
        this.height    = height;
        this.drag      = null; // { id, isNew } | null
    }
    get width() { return this.svg.clientWidth || 600; }
    get trackWidth() { return Math.max(1, this.width - RULER_WIDTH); }
}

// ── Public API ────────────────────────────────────────────────────────────────

export function init(svg, dotnet, keyframes, height) {
    // Blazor resolves an ElementReference to its DOM node lazily, at the moment of this JS call —
    // if the component's Clip became null (and the <svg> was removed from the @if block) while
    // VolumeAutomationLane.razor's OnAfterRenderAsync was still awaiting the module import above
    // it, svg resolves to null/undefined here instead of a real element (item #34). Guard rather
    // than let _ensureElements' querySelector throw and permanently break this component's render
    // (Blazor WASM isolates the crash to this one component, but never recovers on its own).
    if (!svg) return;
    _ensureElements(svg);
    const state = new LaneState(svg, dotnet, keyframes, height);
    _lanes.set(svg, state);
    _bindEvents(svg, state);
    _render(state);
}

export function updateKeyframes(svg, keyframes, height) {
    if (!svg) return;
    const state = _lanes.get(svg);
    if (!state) return;
    state.keyframes = keyframes.slice();
    state.height    = height;
    _render(state);
}

export function destroy(svg) {
    if (!svg) return;
    const state = _lanes.get(svg);
    if (!state) return;
    svg.removeEventListener('pointerdown', state._onPointerDown);
    svg.removeEventListener('pointermove', state._onPointerMove);
    svg.removeEventListener('pointerup',   state._onPointerUp);
    svg.removeEventListener('pointercancel', state._onPointerUp);
    svg.removeEventListener('dblclick',    state._onDblClick);
    _lanes.delete(svg);
}

// ── DOM bootstrap ─────────────────────────────────────────────────────────────

function _ensureElements(svg) {
    if (!svg.querySelector('.bv-vol-lane__ruler'))
        svg.appendChild(_el('g', { class: 'bv-vol-lane__ruler' }));
    if (!svg.querySelector('.bv-vol-lane__line'))
        svg.appendChild(_el('polyline', { class: 'bv-vol-lane__line', points: '' }));
    if (!svg.querySelector('.bv-vol-lane__handles'))
        svg.appendChild(_el('g', { class: 'bv-vol-lane__handles' }));
    if (!svg.querySelector('.bv-vol-lane__tooltip'))
        svg.appendChild(_el('text', { class: 'bv-vol-lane__tooltip', visibility: 'hidden' }));
}

// ── Rendering ─────────────────────────────────────────────────────────────────

function _render(state) {
    const { svg, keyframes, height } = state;
    const w = state.trackWidth;
    _setAttr(svg, { height: `${height}px`, width: '100%' });

    _renderRuler(state);
    _renderPolyline(state);
    _renderHandles(state);
}

function _renderRuler(state) {
    const ruler  = state.svg.querySelector('.bv-vol-lane__ruler');
    const { height } = state;
    ruler.innerHTML = '';

    // Vertical line separating ruler from track
    ruler.appendChild(_el('line', {
        x1: RULER_WIDTH, y1: 0, x2: RULER_WIDTH, y2: height,
        class: 'bv-vol-lane__ruler-line'
    }));

    for (const db of DB_LABELS) {
        const vol  = _dbToLinear(db);
        const y    = _volToY(vol, height);
        // Tick mark
        ruler.appendChild(_el('line', {
            x1: RULER_WIDTH - 4, y1: y, x2: RULER_WIDTH, y2: y,
            class: 'bv-vol-lane__ruler-tick'
        }));
        // Label
        const label = db === -Infinity ? '-\u221e' : (db > 0 ? `+${db}` : `${db}`);
        const txt = _el('text', {
            x: RULER_WIDTH - 6, y: y + 4,
            class: 'bv-vol-lane__ruler-label',
            'text-anchor': 'end',
            'font-size': '9'
        });
        txt.textContent = label;
        ruler.appendChild(txt);
    }
}

function _renderPolyline(state) {
    const line = state.svg.querySelector('.bv-vol-lane__line');
    const { keyframes, height } = state;
    const w = state.trackWidth;

    if (keyframes.length === 0) {
        line.setAttribute('points', '');
        return;
    }

    // Sort by position
    const sorted = keyframes.slice().sort((a, b) => a.position - b.position);

    // Build points: start anchor at x=RULER_WIDTH, end anchor at right edge
    const pts = [];

    // Left anchor: hold first volume
    pts.push(`${RULER_WIDTH},${_volToY(sorted[0].volume, height)}`);

    for (const kf of sorted) {
        const x = RULER_WIDTH + kf.position * w;
        const y = _volToY(kf.volume, height);
        pts.push(`${x.toFixed(1)},${y.toFixed(1)}`);
    }

    // Right anchor: hold last volume
    pts.push(`${RULER_WIDTH + w},${_volToY(sorted[sorted.length - 1].volume, height)}`);

    line.setAttribute('points', pts.join(' '));
}

function _renderHandles(state) {
    const group    = state.svg.querySelector('.bv-vol-lane__handles');
    const { keyframes, height } = state;
    const w = state.trackWidth;

    // Diff update: reuse existing circles, add/remove as needed
    const existing = Array.from(group.querySelectorAll('circle'));
    const sorted   = keyframes.slice().sort((a, b) => a.position - b.position);

    // Remove excess
    for (let i = sorted.length; i < existing.length; i++) existing[i].remove();

    sorted.forEach((kf, i) => {
        const x = RULER_WIDTH + kf.position * w;
        const y = _volToY(kf.volume, height);
        let circle = existing[i];
        if (!circle) {
            circle = _el('circle', { r: HANDLE_RADIUS, class: 'bv-vol-lane__handle' });
            group.appendChild(circle);
        }
        _setAttr(circle, { cx: x.toFixed(1), cy: y.toFixed(1), 'data-id': kf.id });
    });
}

// ── Event binding ─────────────────────────────────────────────────────────────

function _bindEvents(svg, state) {
    state._onPointerDown = e => _onPointerDown(e, state);
    state._onPointerMove = e => _onPointerMove(e, state);
    state._onPointerUp   = e => _onPointerUp(e, state);
    state._onDblClick    = e => _onDblClick(e, state);

    svg.addEventListener('pointerdown',   state._onPointerDown);
    svg.addEventListener('pointermove',   state._onPointerMove);
    svg.addEventListener('pointerup',     state._onPointerUp);
    svg.addEventListener('pointercancel', state._onPointerUp);
    svg.addEventListener('dblclick',      state._onDblClick);
}

// ── Pointer FSM ───────────────────────────────────────────────────────────────

function _onPointerDown(e, state) {
    if (e.button !== 0) return;
    const pt  = _svgPoint(e, state.svg);
    const hit = _hitHandle(pt, state);

    if (hit) {
        // Drag existing handle
        state.drag = { id: hit, isNew: false, ghostVol: null, ghostPos: null };
        state.svg.setPointerCapture(e.pointerId);
        e.preventDefault();
        return;
    }

    if (_nearPolyline(pt, state)) {
        // Insert new keyframe and immediately drag it
        const pos = _xToPos(pt.x, state);
        const vol = _yToVol(pt.y, state.height);
        const newKf = { id: _tempId(), position: pos, volume: vol };
        state.keyframes.push(newKf);
        state.drag = { id: newKf.id, isNew: true, ghostVol: vol, ghostPos: pos };
        _render(state);
        state.svg.setPointerCapture(e.pointerId);
        e.preventDefault();
    }
}

function _onPointerMove(e, state) {
    if (!state.drag) return;
    const pt     = _svgPoint(e, state.svg);
    const newPos = Math.max(0, Math.min(1, _xToPos(pt.x, state)));
    const newVol = Math.max(VOL_MIN, Math.min(VOL_MAX, _yToVol(pt.y, state.height)));

    // Find neighbours to constrain horizontal movement
    const sorted = state.keyframes.slice().sort((a, b) => a.position - b.position);
    const idx    = sorted.findIndex(k => k.id === state.drag.id);
    const leftBound  = idx > 0                  ? sorted[idx - 1].position : 0;
    const rightBound = idx < sorted.length - 1  ? sorted[idx + 1].position : 1;
    const clampedPos = Math.max(leftBound, Math.min(rightBound, newPos));

    // Update local ghost state (no round-trip to Blazor yet)
    const kf = state.keyframes.find(k => k.id === state.drag.id);
    if (kf) { kf.position = clampedPos; kf.volume = newVol; }

    state.drag.ghostPos = clampedPos;
    state.drag.ghostVol = newVol;

    // Show tooltip
    _updateTooltip(state, clampedPos, newVol);
    _render(state);
    e.preventDefault();
}

function _onPointerUp(e, state) {
    if (!state.drag) return;
    const { id, isNew, ghostPos, ghostVol } = state.drag;
    state.drag = null;
    _hideTooltip(state);

    if (ghostPos === null) return;

    if (isNew) {
        state.dotnet.invokeMethodAsync('OnKeyframeAdded', ghostPos, ghostVol);
    } else {
        state.dotnet.invokeMethodAsync('OnKeyframeUpdated', id, ghostPos, ghostVol);
    }
}

function _onDblClick(e, state) {
    const pt  = _svgPoint(e, state.svg);
    const hit = _hitHandle(pt, state);
    if (!hit) return;

    // Optimistically remove from local state for instant feedback
    state.keyframes = state.keyframes.filter(k => k.id !== hit);
    _render(state);

    state.dotnet.invokeMethodAsync('OnKeyframeRemoved', hit);
}

// ── Hit testing ───────────────────────────────────────────────────────────────

function _hitHandle(pt, state) {
    const { keyframes, height } = state;
    const w = state.trackWidth;
    for (const kf of keyframes) {
        const x = RULER_WIDTH + kf.position * w;
        const y = _volToY(kf.volume, height);
        if (Math.hypot(pt.x - x, pt.y - y) <= HANDLE_RADIUS + 2) return kf.id;
    }
    return null;
}

function _nearPolyline(pt, state) {
    const { keyframes, height } = state;
    if (keyframes.length === 0) return pt.x >= RULER_WIDTH;

    const w      = state.trackWidth;
    const sorted = keyframes.slice().sort((a, b) => a.position - b.position);
    const pos    = _xToPos(pt.x, state);

    const leftKf  = sorted.reduce((acc, k) => (k.position <= pos ? k : acc), sorted[0]);
    const rightKf = sorted.find(k => k.position > pos) ?? sorted[sorted.length - 1];

    const x1 = RULER_WIDTH + leftKf.position  * w;
    const y1 = _volToY(leftKf.volume,  height);
    const x2 = RULER_WIDTH + rightKf.position * w;
    const y2 = _volToY(rightKf.volume, height);

    const dist = _distToSegment(pt, { x: x1, y: y1 }, { x: x2, y: y2 });
    return dist <= LINE_HIT_TOL;
}

// ── Tooltip ───────────────────────────────────────────────────────────────────

function _updateTooltip(state, position, volume) {
    const tooltip = state.svg.querySelector('.bv-vol-lane__tooltip');
    if (!tooltip) return;
    const x = RULER_WIDTH + position * state.trackWidth;
    const y = Math.max(14, _volToY(volume, state.height) - 8);
    const db = volume > 0 ? (20 * Math.log10(volume)).toFixed(1) : '-\u221e';
    _setAttr(tooltip, { x: x.toFixed(1), y: y.toFixed(1), visibility: 'visible' });
    tooltip.textContent = `${db} dB`;
}

function _hideTooltip(state) {
    const tooltip = state.svg.querySelector('.bv-vol-lane__tooltip');
    if (tooltip) tooltip.setAttribute('visibility', 'hidden');
}

// ── Coordinate helpers ────────────────────────────────────────────────────────

function _svgPoint(e, svg) {
    const r = svg.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
}

function _xToPos(x, state) {
    return (x - RULER_WIDTH) / state.trackWidth;
}

function _yToVol(y, height) {
    const t = y / height;
    return VOL_MAX - t * (VOL_MAX - VOL_MIN);
}

function _volToY(vol, height) {
    const t = (VOL_MAX - Math.max(VOL_MIN, Math.min(VOL_MAX, vol))) / (VOL_MAX - VOL_MIN);
    return t * height;
}

function _dbToLinear(db) {
    if (db <= -100) return 0;
    return Math.pow(10, db / 20);
}

function _distToSegment(p, a, b) {
    const dx = b.x - a.x, dy = b.y - a.y;
    if (dx === 0 && dy === 0) return Math.hypot(p.x - a.x, p.y - a.y);
    const t = Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / (dx * dx + dy * dy)));
    return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
}

// ── Misc ──────────────────────────────────────────────────────────────────────

let _tempCounter = 0;
function _tempId() { return `__tmp_${++_tempCounter}`; }

function _el(tag, attrs = {}) {
    const el = document.createElementNS('http://www.w3.org/2000/svg', tag);
    _setAttr(el, attrs);
    return el;
}

function _setAttr(el, attrs) {
    for (const [k, v] of Object.entries(attrs))
        el.setAttribute(k, v);
}
