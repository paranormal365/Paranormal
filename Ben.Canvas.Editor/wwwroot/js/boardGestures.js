// The board's pointer, wheel and touch gestures.
//
// One delegated listener per board owns every gesture: pan, zoom, pinch, move, resize, marquee,
// connect, tap, double-tap and long-press. The rule that shapes all of it: while a gesture runs, this
// module changes only CSS custom properties and classes on elements Blazor rendered, and calls C#
// once when the gesture ends. A Blazor render during a drag rewrites the style attribute it owns, so
// a render mid-gesture would throw the dragged block back to where it started; the board does not
// render while a gesture is active, and C# re-snaps the final position when it commits.
//
// This module never creates, moves or removes an element. The one attribute it writes is the path
// data of connectors and of the connect preview: an SVG path cannot take its shape from CSS.
//
// Coordinates: "client" is the viewport, "board" is relative to the board's top-left, and "world" is
// the board's own space at zoom 1. world = (board - pan) / zoom.

const boards = new Map();

const IGNORE = 'input, textarea, select, [contenteditable=""], [contenteditable="true"], .k-editor, .k-popup, .k-animation-container, a[href], [data-bc-action]';

export function attach(boardEl, dotnet, opts) {
    if (!boardEl || boards.has(boardEl)) return;

    const state = {
        el: boardEl,
        dotnet,
        opts: Object.assign({
            dragThresholdMouse: 4, dragThresholdTouch: 10, longPressMs: 500, longPressTolerance: 10,
            doubleTapMs: 300, doubleTapPx: 24, minZoom: 0.1, maxZoom: 4,
        }, opts || {}),
        vp: { panX: 0, panY: 0, zoom: 1 },
        pointers: new Map(),
        gesture: null,
        spaceHeld: false,
        wheelTimer: 0,
        resizeTimer: 0,
        longPressTimer: 0,
        lastTap: null,
        suppressMenuUntil: 0,
        swallowClickUntil: 0,
        safariPinch: null,
        readOnly: !!(opts && opts.readOnly),
        handlers: {},
    };

    if (opts && opts.initial) state.vp = clampViewport(state, opts.initial);
    boards.set(boardEl, state);
    applyViewport(state);
    boardEl.classList.toggle('bc-board--readonly', state.readOnly);

    const h = state.handlers;
    h.pointerdown = e => onPointerDown(state, e);
    h.pointermove = e => onPointerMove(state, e);
    h.pointerup = e => onPointerUp(state, e);
    h.pointercancel = e => onPointerCancel(state, e);
    h.lostpointercapture = e => { if (state.gesture && state.gesture.pointerId === e.pointerId && state.pointers.size <= 1) cancelGesture(state, true); };
    h.wheel = e => onWheel(state, e);
    h.dblclick = e => onDoubleClick(state, e);
    h.contextmenu = e => onContextMenu(state, e);
    h.gesturestart = e => onSafariGesture(state, e, 'start');
    h.gesturechange = e => onSafariGesture(state, e, 'change');
    h.gestureend = e => onSafariGesture(state, e, 'end');
    h.keydown = e => onKey(state, e, true);
    h.keyup = e => onKey(state, e, false);
    h.blur = () => { state.spaceHeld = false; cancelGesture(state, true); };
    h.visibility = () => { if (document.visibilityState === 'hidden') cancelGesture(state, true); };
    // Lifting the finger after a long-press makes the browser send a click, which would land on the menu's
    // backdrop and close the menu the moment it opened. That one click is swallowed before anything sees it.
    h.swallowClick = e => {
        if (performance.now() >= state.swallowClickUntil) return;
        state.swallowClickUntil = 0;
        e.preventDefault();
        e.stopPropagation();
    };

    boardEl.addEventListener('pointerdown', h.pointerdown);
    boardEl.addEventListener('pointermove', h.pointermove);
    boardEl.addEventListener('pointerup', h.pointerup);
    boardEl.addEventListener('pointercancel', h.pointercancel);
    boardEl.addEventListener('lostpointercapture', h.lostpointercapture);
    boardEl.addEventListener('wheel', h.wheel, { passive: false });
    boardEl.addEventListener('dblclick', h.dblclick);
    boardEl.addEventListener('contextmenu', h.contextmenu);
    boardEl.addEventListener('gesturestart', h.gesturestart);
    boardEl.addEventListener('gesturechange', h.gesturechange);
    boardEl.addEventListener('gestureend', h.gestureend);
    document.addEventListener('keydown', h.keydown);
    document.addEventListener('keyup', h.keyup);
    window.addEventListener('blur', h.blur);
    document.addEventListener('visibilitychange', h.visibility);
    window.addEventListener('click', h.swallowClick, true);

    if (typeof ResizeObserver !== 'undefined') {
        state.observer = new ResizeObserver(() => {
            clearTimeout(state.resizeTimer);
            state.resizeTimer = setTimeout(() => postSize(state), 100);
        });
        state.observer.observe(boardEl);
    }

    postSize(state);
}

export function detach(boardEl) {
    const state = boards.get(boardEl);
    if (!state) return;
    cancelGesture(state, false);
    const h = state.handlers;
    boardEl.removeEventListener('pointerdown', h.pointerdown);
    boardEl.removeEventListener('pointermove', h.pointermove);
    boardEl.removeEventListener('pointerup', h.pointerup);
    boardEl.removeEventListener('pointercancel', h.pointercancel);
    boardEl.removeEventListener('lostpointercapture', h.lostpointercapture);
    boardEl.removeEventListener('wheel', h.wheel);
    boardEl.removeEventListener('dblclick', h.dblclick);
    boardEl.removeEventListener('contextmenu', h.contextmenu);
    boardEl.removeEventListener('gesturestart', h.gesturestart);
    boardEl.removeEventListener('gesturechange', h.gesturechange);
    boardEl.removeEventListener('gestureend', h.gestureend);
    document.removeEventListener('keydown', h.keydown);
    document.removeEventListener('keyup', h.keyup);
    window.removeEventListener('blur', h.blur);
    document.removeEventListener('visibilitychange', h.visibility);
    window.removeEventListener('click', h.swallowClick, true);
    if (state.observer) state.observer.disconnect();
    clearTimeout(state.wheelTimer);
    clearTimeout(state.resizeTimer);
    clearTimeout(state.longPressTimer);
    boards.delete(boardEl);
}

/**
 * The board became view-only or editable (R33). View-only, a drag that starts on a block, handle, port or group
 * label pans the board instead of moving anything; taps still select. C# refuses changes too - this only keeps
 * the pointer from promising a move that will not happen.
 */
export function setReadOnly(boardEl, readOnly) {
    const state = boards.get(boardEl);
    if (!state) return;
    state.readOnly = !!readOnly;
    boardEl.classList.toggle('bc-board--readonly', state.readOnly);
}

/** C# moved the camera (buttons, fit, keys). */
export function setViewport(boardEl, panX, panY, zoom, animate) {
    const state = boards.get(boardEl);
    if (!state) return;
    state.vp = clampViewport(state, { panX, panY, zoom });
    const world = boardEl.querySelector('.bc-world');
    if (world) {
        const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (animate && !reduce) {
            world.classList.add('bc-world--animate');
            setTimeout(() => world.classList.remove('bc-world--animate'), 180);
        } else {
            world.classList.remove('bc-world--animate');
        }
    }
    applyViewport(state);
}

/** The camera as the board has it now, so C# never zooms from a stale value. */
export function getViewport(boardEl) {
    const state = boards.get(boardEl);
    return state ? { panX: state.vp.panX, panY: state.vp.panY, zoom: state.vp.zoom } : { panX: 0, panY: 0, zoom: 1 };
}

/** Abandons whatever gesture is running, restoring every preview. */
export function cancel(boardEl) {
    const state = boards.get(boardEl);
    if (state) cancelGesture(state, true);
}

// ── Viewport ─────────────────────────────────────────────────────────────

function clampZoom(state, z) {
    if (!Number.isFinite(z)) return 1;
    return Math.min(state.opts.maxZoom, Math.max(state.opts.minZoom, z));
}

function clampViewport(state, vp) {
    return {
        panX: Number.isFinite(vp.panX) ? vp.panX : 0,
        panY: Number.isFinite(vp.panY) ? vp.panY : 0,
        zoom: clampZoom(state, vp.zoom),
    };
}

function applyViewport(state) {
    const s = state.el.style;
    s.setProperty('--bc-pan-x', num(state.vp.panX) + 'px');
    s.setProperty('--bc-pan-y', num(state.vp.panY) + 'px');
    s.setProperty('--bc-zoom', num(state.vp.zoom));
    state.el.classList.toggle('bc-board--coarse', state.vp.zoom < 0.5);
}

function zoomAbout(state, bx, by, factor) {
    const z2 = clampZoom(state, state.vp.zoom * factor);
    const ratio = z2 / state.vp.zoom;
    state.vp = { panX: bx - (bx - state.vp.panX) * ratio, panY: by - (by - state.vp.panY) * ratio, zoom: z2 };
    applyViewport(state);
}

function postViewport(state) {
    invoke(state, 'OnViewportChanged', { panX: state.vp.panX, panY: state.vp.panY, zoom: state.vp.zoom });
}

function postSize(state) {
    const r = state.el.getBoundingClientRect();
    invoke(state, 'OnBoardResized', { width: r.width, height: r.height });
}

function boardPoint(state, clientX, clientY) {
    const r = state.el.getBoundingClientRect();
    return { x: clientX - r.left, y: clientY - r.top };
}

function worldPoint(state, clientX, clientY) {
    const b = boardPoint(state, clientX, clientY);
    return { x: (b.x - state.vp.panX) / state.vp.zoom, y: (b.y - state.vp.panY) / state.vp.zoom };
}

// ── Pointer ──────────────────────────────────────────────────────────────

function onPointerDown(state, e) {
    const target = e.target instanceof Element ? e.target : null;
    if (!target) return;
    if (target.closest(IGNORE)) return;
    if (target.closest('button') && !target.closest('[data-bc-port]')) return;
    if (target.closest('.bc-node--editing .bc-node__body')) return;
    // A live Apple map takes its own pans and pinches; the still picture drags the block like any body (R14).
    if (target.closest('.bc-map__live')) return;

    if (!state.el.contains(document.activeElement) || document.activeElement === document.body) {
        state.el.focus({ preventScroll: true });
    }

    state.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, type: e.pointerType });
    state.lastPointerWasTouch = e.pointerType === 'touch';

    if (e.pointerType === 'touch' && state.pointers.size === 2) {
        startPinch(state);
        capture(state, e);
        return;
    }

    if (state.gesture) return;
    if (e.button === 2) return;

    const world = worldPoint(state, e.clientX, e.clientY);
    const base = {
        pointerId: e.pointerId, pointerType: e.pointerType || 'mouse',
        startX: e.clientX, startY: e.clientY, startWorld: world,
        shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey,
    };

    if (e.button === 1 || (state.spaceHeld && e.pointerType !== 'touch')) {
        e.preventDefault();
        startPan(state, base);
        capture(state, e);
        return;
    }

    if (e.button !== 0 && e.pointerType === 'mouse') return;

    const port = target.closest('[data-bc-port]');
    const handle = target.closest('[data-bc-handle]');
    const node = target.closest('.bc-node[data-bc-node]');
    const groupLabel = target.closest('.bc-group__label[data-bc-group]');
    const edge = target.closest('[data-bc-edge]');

    let pending;
    if (port) pending = { target: 'port', nodeId: port.getAttribute('data-bc-node'), port: port.getAttribute('data-bc-port') };
    else if (handle) pending = { target: 'handle', nodeId: handle.getAttribute('data-bc-node'), groupId: handle.getAttribute('data-bc-group'), handle: handle.getAttribute('data-bc-handle') };
    else if (node) pending = { target: 'node', nodeId: node.getAttribute('data-bc-node'), locked: node.hasAttribute('data-bc-locked') };
    else if (groupLabel) pending = { target: 'group', groupId: groupLabel.getAttribute('data-bc-group') };
    else if (edge) pending = { target: 'edge', edgeId: edge.getAttribute('data-bc-edge') };
    else pending = { target: 'empty' };

    state.gesture = Object.assign({ kind: 'pending' }, base, pending);
    capture(state, e);

    if (e.pointerType === 'touch') {
        clearTimeout(state.longPressTimer);
        state.longPressTimer = setTimeout(() => onLongPress(state), state.opts.longPressMs);
    }
}

function onPointerMove(state, e) {
    const tracked = state.pointers.get(e.pointerId);
    if (tracked) { tracked.x = e.clientX; tracked.y = e.clientY; }

    const g = state.gesture;
    if (!g) return;

    if (g.kind === 'pinch') { updatePinch(state); return; }
    if (e.pointerId !== g.pointerId) return;

    const dxClient = e.clientX - g.startX;
    const dyClient = e.clientY - g.startY;
    const distance = Math.hypot(dxClient, dyClient);

    if (g.kind === 'pending') {
        if (g.pointerType === 'touch' && distance > state.opts.longPressTolerance) clearTimeout(state.longPressTimer);
        const threshold = g.pointerType === 'touch' ? state.opts.dragThresholdTouch : state.opts.dragThresholdMouse;
        if (distance <= threshold) return;
        clearTimeout(state.longPressTimer);
        promote(state, g);
    }

    const gz = state.gesture;
    if (!gz) return;
    const dxWorld = dxClient / state.vp.zoom;
    const dyWorld = dyClient / state.vp.zoom;

    switch (gz.kind) {
        case 'pan':
            state.vp = { panX: gz.panX + dxClient, panY: gz.panY + dyClient, zoom: state.vp.zoom };
            applyViewport(state);
            break;
        case 'move': previewMove(state, gz, dxWorld, dyWorld); break;
        case 'resize': previewResize(state, gz, dxWorld, dyWorld, e.shiftKey); break;
        case 'marquee': previewMarquee(state, gz, e.clientX, e.clientY); break;
        case 'connect': previewConnect(state, gz, e.clientX, e.clientY); break;
    }
}

async function onPointerUp(state, e) {
    state.pointers.delete(e.pointerId);
    clearTimeout(state.longPressTimer);
    try { if (state.el.hasPointerCapture(e.pointerId)) state.el.releasePointerCapture(e.pointerId); } catch { /* already released */ }
    const g = state.gesture;
    if (!g) return;

    if (g.kind === 'pinch') {
        if (state.pointers.size === 1) {
            // One finger lifted: the other carries on as a pan from where it is now.
            const [id, p] = [...state.pointers.entries()][0];
            state.gesture = null;
            startPan(state, { pointerId: id, pointerType: 'touch', startX: p.x, startY: p.y });
        } else if (state.pointers.size === 0) {
            endBoardGesture(state);
            state.gesture = null;
            postViewport(state);
        }
        return;
    }

    if (e.pointerId !== g.pointerId) return;
    state.gesture = null;

    const dxWorld = (e.clientX - g.startX) / state.vp.zoom;
    const dyWorld = (e.clientY - g.startY) / state.vp.zoom;

    try {
        switch (g.kind) {
            case 'pending': onTap(state, g, e); break;
            case 'pan':
                endBoardGesture(state);
                postViewport(state);
                break;
            case 'move': await endMove(state, g, dxWorld, dyWorld); break;
            case 'resize': await endResize(state, g, dxWorld, dyWorld, e.shiftKey); break;
            case 'marquee': endMarquee(state, g, e); break;
            case 'connect': await endConnect(state, g, e); break;
        }
    } finally {
        endBoardGesture(state);
    }
}

function onPointerCancel(state, e) {
    state.pointers.delete(e.pointerId);
    clearTimeout(state.longPressTimer);
    const g = state.gesture;
    if (g && (g.pointerId === e.pointerId || g.kind === 'pinch')) cancelGesture(state, true);
}

function capture(state, e) {
    try { state.el.setPointerCapture(e.pointerId); } catch { /* the pointer may already be gone */ }
}

function promote(state, g) {
    if (state.readOnly && g.target !== 'empty' && g.target !== 'edge') {
        startPan(state, g);
        return;
    }

    switch (g.target) {
        case 'port': startConnect(state, g); break;
        case 'handle': startResize(state, g); break;
        case 'node':
            if (g.locked) { g.lockedDrag = true; return; }
            startMove(state, g, [g.nodeId], []);
            break;
        case 'group': startMove(state, g, [], [g.groupId]); break;
        default:
            if (g.pointerType === 'touch') startPan(state, g);
            else startMarquee(state, g);
    }
}

// ── Pan and pinch ────────────────────────────────────────────────────────

function startPan(state, base) {
    state.gesture = Object.assign({}, base, { kind: 'pan', panX: state.vp.panX, panY: state.vp.panY });
    state.el.classList.add('bc-board--panning', 'bc-board--gesture');
}

function startPinch(state) {
    const previous = state.gesture;
    if (previous && (previous.kind === 'move' || previous.kind === 'resize' || previous.kind === 'connect')) {
        restorePreview(state, previous);
        invoke(state, 'OnGestureCancelled');
    }
    clearTimeout(state.longPressTimer);
    const [a, b] = [...state.pointers.values()];
    const mid = boardPoint(state, (a.x + b.x) / 2, (a.y + b.y) / 2);
    state.gesture = {
        kind: 'pinch', dist0: Math.max(1, Math.hypot(a.x - b.x, a.y - b.y)), mid0: mid,
        vp0: { panX: state.vp.panX, panY: state.vp.panY, zoom: state.vp.zoom },
    };
    state.el.classList.add('bc-board--gesture');
}

function updatePinch(state) {
    const g = state.gesture;
    const points = [...state.pointers.values()];
    if (points.length < 2) return;
    const [a, b] = points;
    const dist = Math.max(1, Math.hypot(a.x - b.x, a.y - b.y));
    const mid = boardPoint(state, (a.x + b.x) / 2, (a.y + b.y) / 2);
    const z = clampZoom(state, g.vp0.zoom * dist / g.dist0);
    const ratio = z / g.vp0.zoom;
    state.vp = {
        panX: mid.x - (g.mid0.x - g.vp0.panX) * ratio,
        panY: mid.y - (g.mid0.y - g.vp0.panY) * ratio,
        zoom: z,
    };
    applyViewport(state);
}

function onWheel(state, e) {
    if (e.target instanceof Element && e.target.closest('.bc-node--editing .bc-node__body, .k-popup')) return;
    e.preventDefault();
    const unit = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? state.el.clientHeight : 1;
    const b = boardPoint(state, e.clientX, e.clientY);
    if (e.ctrlKey || e.metaKey) {
        zoomAbout(state, b.x, b.y, Math.exp(-e.deltaY * unit * 0.0015));
    } else {
        const dx = (e.shiftKey && !e.deltaX ? e.deltaY : e.deltaX) * unit;
        const dy = (e.shiftKey && !e.deltaX ? 0 : e.deltaY) * unit;
        state.vp = { panX: state.vp.panX - dx, panY: state.vp.panY - dy, zoom: state.vp.zoom };
        applyViewport(state);
    }
    clearTimeout(state.wheelTimer);
    state.wheelTimer = setTimeout(() => postViewport(state), 120);
}

// Safari on a Mac trackpad reports pinch as gesture events, not as ctrl+wheel.
function onSafariGesture(state, e, phase) {
    e.preventDefault();
    if (phase === 'start') {
        state.safariPinch = { zoom0: state.vp.zoom, point: boardPoint(state, e.clientX, e.clientY) };
        return;
    }
    const p = state.safariPinch;
    if (!p) return;
    const target = clampZoom(state, p.zoom0 * (e.scale || 1));
    zoomAbout(state, p.point.x, p.point.y, target / state.vp.zoom);
    if (phase === 'end') {
        state.safariPinch = null;
        postViewport(state);
    }
}

// ── Move ─────────────────────────────────────────────────────────────────

function nodeElement(state, id) {
    return id ? state.el.querySelector(`.bc-node[data-bc-node="${cssEscape(id)}"]`) : null;
}

function groupElement(state, id) {
    const label = id ? state.el.querySelector(`.bc-group__label[data-bc-group="${cssEscape(id)}"]`) : null;
    return label ? label.closest('.bc-group') : null;
}

function rectOf(el) {
    if (!el) return null;
    const s = el.style;
    return { x: parseFloat(s.left) || 0, y: parseFloat(s.top) || 0, w: parseFloat(s.width) || 0, h: parseFloat(s.height) || 0 };
}

function startMove(state, g, nodeIds, groupIds) {
    g.kind = 'move';
    const pressed = nodeElement(state, nodeIds[0]);
    if (pressed && pressed.hasAttribute('data-bc-selected')) {
        state.el.querySelectorAll('.bc-node[data-bc-selected]:not([data-bc-locked])').forEach(el => {
            const id = el.getAttribute('data-bc-node');
            if (id && !nodeIds.includes(id)) nodeIds.push(id);
        });
    }
    g.nodeIds = nodeIds;
    g.groupIds = groupIds;
    g.elements = nodeIds.map(id => nodeElement(state, id)).filter(Boolean)
        .concat(groupIds.map(id => groupElement(state, id)).filter(Boolean));
    g.primary = rectOf(g.elements[0]);
    g.ctx = null;
    g.dx = 0;
    g.dy = 0;
    state.el.classList.add('bc-board--gesture');
    g.elements.forEach(el => el.classList.add('bc-node--dragging'));

    g.ready = invokeResult(state, 'BeginMove', { nodeIds, groupIds }).then(ctx => {
        if (!ctx || state.gesture !== g) return ctx;
        g.ctx = ctx;
        // C# knows the whole moving set (group members, minus locked blocks).
        (ctx.nodeIds || []).forEach(id => {
            const el = nodeElement(state, id);
            if (el && !g.elements.includes(el)) { el.classList.add('bc-node--dragging'); g.elements.push(el); }
        });
        g.edges = prepareEdges(state, ctx.edges);
        previewMove(state, g, g.dx, g.dy);
        return ctx;
    });
}

function previewMove(state, g, dx, dy) {
    let sdx = dx;
    let sdy = dy;
    let guideX = null;
    let guideY = null;

    if (g.ctx && g.primary && g.ctx.snapEnabled !== false) {
        const sx = snapAxis(g.primary.x + dx, g.primary.w, g.ctx.guidesX || [], g.ctx.threshold || 0);
        const sy = snapAxis(g.primary.y + dy, g.primary.h, g.ctx.guidesY || [], g.ctx.threshold || 0);
        if (sx) { sdx = sx.position - g.primary.x; guideX = sx.guide; }
        if (sy) { sdy = sy.position - g.primary.y; guideY = sy.guide; }
    }

    g.dx = dx;
    g.dy = dy;
    for (const el of g.elements) {
        el.style.setProperty('--bc-dx', num(sdx) + 'px');
        el.style.setProperty('--bc-dy', num(sdy) + 'px');
    }
    showGuides(state, guideX, guideY);
    if (g.edges) redrawEdges(state, g.edges, id => g.ctx && (g.ctx.nodeIds || []).includes(id) ? { dx: sdx, dy: sdy } : null);
}

async function endMove(state, g, dx, dy) {
    try {
        await g.ready;
        await invoke(state, 'OnMoveEnd', { nodeIds: g.nodeIds, groupIds: g.groupIds, dx, dy });
    } finally {
        // C# has rendered the committed positions by now, so removing the preview does not flash.
        for (const el of g.elements) {
            el.classList.remove('bc-node--dragging');
            el.style.removeProperty('--bc-dx');
            el.style.removeProperty('--bc-dy');
        }
        showGuides(state, null, null);
        if (g.edges) settleEdges(state, g.edges);
    }
}

function snapAxis(position, size, guides, threshold) {
    if (!guides.length || !(threshold > 0)) return null;
    let best = null;
    for (const offset of [0, size / 2, size]) {
        for (const guide of guides) {
            const d = Math.abs(position + offset - guide);
            if (d <= threshold && (!best || d < best.d)) best = { d, guide, position: guide - offset };
        }
    }
    return best;
}

function showGuides(state, gx, gy) {
    const x = state.el.querySelector('.bc-guide-x');
    const y = state.el.querySelector('.bc-guide-y');
    if (x) { x.classList.toggle('bc-guide--on', gx !== null); if (gx !== null) x.style.setProperty('--bc-guide-pos', num(gx) + 'px'); }
    if (y) { y.classList.toggle('bc-guide--on', gy !== null); if (gy !== null) y.style.setProperty('--bc-guide-pos', num(gy) + 'px'); }
}

// ── Resize ───────────────────────────────────────────────────────────────

function startResize(state, g) {
    g.kind = 'resize';
    g.element = g.groupId ? groupElement(state, g.groupId) : nodeElement(state, g.nodeId);
    g.ctx = null;
    state.el.classList.add('bc-board--gesture');
    const payload = { nodeId: g.nodeId || null, groupId: g.groupId || null, handle: g.handle };
    g.ready = invokeResult(state, 'BeginResize', payload).then(ctx => {
        if (!ctx || state.gesture !== g) return ctx;
        g.ctx = ctx;
        g.edges = prepareEdges(state, ctx.edges);
        if (g.element) g.element.classList.add('bc-node--resizing');
        return ctx;
    });
}

function resizeRect(ctx, handle, dx, dy, keepAspect) {
    let left = ctx.x, top = ctx.y, right = ctx.x + ctx.width, bottom = ctx.y + ctx.height;
    const L = /l/.test(handle) && handle !== 't' && handle !== 'b';
    const R = handle === 'tr' || handle === 'r' || handle === 'br';
    const T = handle === 'tl' || handle === 't' || handle === 'tr';
    const B = handle === 'bl' || handle === 'b' || handle === 'br';
    if (L) left = Math.min(left + dx, right - ctx.minWidth);
    if (R) right = Math.max(right + dx, left + ctx.minWidth);
    if (T) top = Math.min(top + dy, bottom - ctx.minHeight);
    if (B) bottom = Math.max(bottom + dy, top + ctx.minHeight);
    let r = { x: left, y: top, w: right - left, h: bottom - top };
    if (keepAspect && handle.length === 2 && ctx.width > 0 && ctx.height > 0) {
        const minScale = Math.max(ctx.minWidth / ctx.width, ctx.minHeight / ctx.height);
        const scale = Math.max(r.w / ctx.width, r.h / ctx.height, minScale);
        const w = ctx.width * scale;
        const h = ctx.height * scale;
        r = { x: L ? ctx.x + ctx.width - w : ctx.x, y: T ? ctx.y + ctx.height - h : ctx.y, w, h };
    }
    return r;
}

function previewResize(state, g, dx, dy, shift) {
    if (!g.ctx || !g.element) return;
    const r = resizeRect(g.ctx, g.handle, dx, dy, shift);
    const s = g.element.style;
    s.setProperty('--bc-rx', num(r.x) + 'px');
    s.setProperty('--bc-ry', num(r.y) + 'px');
    s.setProperty('--bc-rw', num(r.w) + 'px');
    s.setProperty('--bc-rh', num(r.h) + 'px');
    if (g.edges && g.nodeId) redrawEdges(state, g.edges, id => id === g.nodeId ? { rect: r } : null);
}

async function endResize(state, g, dx, dy, shift) {
    try {
        await g.ready;
        if (g.ctx) await invoke(state, 'OnResizeEnd', { nodeId: g.nodeId || null, groupId: g.groupId || null, handle: g.handle, dx, dy, keepAspect: !!shift });
    } finally {
        if (g.element) {
            g.element.classList.remove('bc-node--resizing');
            ['--bc-rx', '--bc-ry', '--bc-rw', '--bc-rh'].forEach(p => g.element.style.removeProperty(p));
        }
        if (g.edges) settleEdges(state, g.edges);
    }
}

// ── Marquee ──────────────────────────────────────────────────────────────

function startMarquee(state, g) {
    g.kind = 'marquee';
    g.box = state.el.querySelector('.bc-marquee');
    state.el.classList.add('bc-board--gesture');
    if (g.box) g.box.classList.add('bc-marquee--on');
}

function previewMarquee(state, g, clientX, clientY) {
    if (!g.box) return;
    const a = boardPoint(state, g.startX, g.startY);
    const b = boardPoint(state, clientX, clientY);
    const s = g.box.style;
    s.setProperty('--bc-mq-x', num(Math.min(a.x, b.x)) + 'px');
    s.setProperty('--bc-mq-y', num(Math.min(a.y, b.y)) + 'px');
    s.setProperty('--bc-mq-w', num(Math.abs(a.x - b.x)) + 'px');
    s.setProperty('--bc-mq-h', num(Math.abs(a.y - b.y)) + 'px');
}

function endMarquee(state, g, e) {
    if (g.box) g.box.classList.remove('bc-marquee--on');
    const a = g.startWorld;
    const b = worldPoint(state, e.clientX, e.clientY);
    invoke(state, 'OnMarquee', {
        x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(a.x - b.x), height: Math.abs(a.y - b.y),
        additive: !!(g.shift || g.ctrl || e.shiftKey || e.ctrlKey || e.metaKey),
    });
}

// ── Connect ──────────────────────────────────────────────────────────────

function startConnect(state, g) {
    g.kind = 'connect';
    g.preview = state.el.querySelector('.bc-connect-preview');
    state.el.classList.add('bc-board--gesture');
}

function previewConnect(state, g, clientX, clientY) {
    if (!g.preview) return;
    const from = rectOf(nodeElement(state, g.nodeId));
    if (!from) return;
    const p0 = anchor(from, g.port);
    const p3 = worldPoint(state, clientX, clientY);
    const offset = Math.max(40, 0.4 * Math.hypot(p3.x - p0.x, p3.y - p0.y));
    const n = normal(g.port);
    g.preview.setAttribute('d', `M ${num(p0.x)} ${num(p0.y)} C ${num(p0.x + n.x * offset)} ${num(p0.y + n.y * offset)}, ${num(p3.x)} ${num(p3.y)}, ${num(p3.x)} ${num(p3.y)}`);
}

async function endConnect(state, g, e) {
    if (g.preview) g.preview.removeAttribute('d');
    const under = document.elementFromPoint(e.clientX, e.clientY);
    const toNode = under ? under.closest('.bc-node[data-bc-node]') : null;
    const toPort = under ? under.closest('[data-bc-port]') : null;
    const world = worldPoint(state, e.clientX, e.clientY);
    await invoke(state, 'OnConnectEnd', {
        fromNodeId: g.nodeId, fromSide: g.port,
        toNodeId: toNode ? toNode.getAttribute('data-bc-node') : (toPort ? toPort.getAttribute('data-bc-node') : null),
        toPort: toPort ? toPort.getAttribute('data-bc-port') : null,
        worldX: world.x, worldY: world.y,
    });
}

// ── Connectors that follow a drag ────────────────────────────────────────

function prepareEdges(state, edges) {
    if (!edges || !edges.length) return null;
    return edges.map(info => {
        const group = state.el.querySelector(`[data-bc-edge="${cssEscape(info.id)}"]`);
        const paths = group ? [...group.querySelectorAll('path')] : [];
        return { info, paths, original: paths.map(p => p.getAttribute('d')) };
    });
}

function redrawEdges(state, edges, change) {
    for (const edge of edges) {
        const from = edgeEnd(state, edge.info.fromNodeId, change);
        const to = edgeEnd(state, edge.info.toNodeId, change);
        if (!from || !to) continue;
        const d = edgePath(from, to, edge.info.fromSide, edge.info.toSide);
        edge.paths.forEach(p => { if (!p.classList.contains('bc-edge__head')) p.setAttribute('d', d); });
        const heads = edge.paths.filter(p => p.classList.contains('bc-edge__head'));
        heads.forEach(p => p.setAttribute('visibility', 'hidden'));
    }
}

function settleEdges(state, edges) {
    // After C# has rendered, redraw from the committed rectangles: equal to what Blazor drew, and right
    // even when the commit changed nothing and Blazor had no reason to touch the path.
    for (const edge of edges) {
        const from = rectOf(nodeElement(state, edge.info.fromNodeId));
        const to = rectOf(nodeElement(state, edge.info.toNodeId));
        edge.paths.forEach((p, i) => {
            if (p.classList.contains('bc-edge__head')) { p.removeAttribute('visibility'); return; }
            if (from && to) p.setAttribute('d', edgePath(from, to, edge.info.fromSide, edge.info.toSide));
            else if (edge.original[i] !== null) p.setAttribute('d', edge.original[i]);
        });
    }
}

function edgeEnd(state, id, change) {
    const base = rectOf(nodeElement(state, id));
    if (!base) return null;
    const c = change(id);
    if (!c) return base;
    if (c.rect) return c.rect;
    return { x: base.x + c.dx, y: base.y + c.dy, w: base.w, h: base.h };
}

function edgePath(from, to, fromSide, toSide) {
    const auto = autoSides(from, to);
    const fs = fromSide || auto[0];
    const ts = toSide || auto[1];
    const p0 = anchor(from, fs);
    const p3 = anchor(to, ts);
    const offset = Math.max(40, 0.4 * Math.hypot(p3.x - p0.x, p3.y - p0.y));
    const n0 = normal(fs);
    const n3 = normal(ts);
    return `M ${num(p0.x)} ${num(p0.y)} C ${num(p0.x + n0.x * offset)} ${num(p0.y + n0.y * offset)}, ${num(p3.x + n3.x * offset)} ${num(p3.y + n3.y * offset)}, ${num(p3.x)} ${num(p3.y)}`;
}

function autoSides(a, b) {
    const overlap = b.x <= a.x + a.w && b.x + b.w >= a.x && b.y <= a.y + a.h && b.y + b.h >= a.y;
    if (overlap) return ['Bottom', 'Top'];
    const dx = (b.x + b.w / 2) - (a.x + a.w / 2);
    const dy = (b.y + b.h / 2) - (a.y + a.h / 2);
    if (Math.abs(dx) >= Math.abs(dy)) return dx > 0 ? ['Right', 'Left'] : ['Left', 'Right'];
    return dy > 0 ? ['Bottom', 'Top'] : ['Top', 'Bottom'];
}

function anchor(r, side) {
    switch (side) {
        case 'Top': return { x: r.x + r.w / 2, y: r.y };
        case 'Right': return { x: r.x + r.w, y: r.y + r.h / 2 };
        case 'Bottom': return { x: r.x + r.w / 2, y: r.y + r.h };
        default: return { x: r.x, y: r.y + r.h / 2 };
    }
}

function normal(side) {
    switch (side) {
        case 'Top': return { x: 0, y: -1 };
        case 'Right': return { x: 1, y: 0 };
        case 'Bottom': return { x: 0, y: 1 };
        default: return { x: -1, y: 0 };
    }
}

// ── Taps, menus and keys ─────────────────────────────────────────────────

function tapPayload(state, g, clientX, clientY) {
    const world = worldPoint(state, clientX, clientY);
    return {
        nodeId: g.target === 'node' || g.target === 'port' || g.target === 'handle' ? (g.nodeId || null) : null,
        edgeId: g.edgeId || null,
        // Which side handle was pressed, when one was. A press that never became a drag is a request
        // for the next block on that side, so C# has to know which side was asked for.
        port: g.target === 'port' ? (g.port || null) : null,
        groupId: g.groupId || null,
        worldX: world.x, worldY: world.y,
        shift: !!g.shift, ctrl: !!g.ctrl,
        pointerType: g.pointerType || 'mouse',
        lockedDrag: !!g.lockedDrag,
    };
}

function onTap(state, g, e) {
    const payload = tapPayload(state, g, e.clientX, e.clientY);
    if (g.pointerType === 'touch') {
        const now = performance.now();
        const last = state.lastTap;
        if (last && now - last.at <= state.opts.doubleTapMs && Math.hypot(e.clientX - last.x, e.clientY - last.y) <= state.opts.doubleTapPx) {
            state.lastTap = null;
            invoke(state, 'OnDoubleTap', payload);
            return;
        }
        state.lastTap = { at: now, x: e.clientX, y: e.clientY };
    }
    invoke(state, 'OnTap', payload);
}

function onDoubleClick(state, e) {
    // The board captured the pointer when it was pressed, and a browser delivers click and dblclick to
    // the capturing element - the board - so the event's target cannot say which block was under it.
    const under = document.elementFromPoint(e.clientX, e.clientY);
    const t = under instanceof Element ? under : (e.target instanceof Element ? e.target : null);
    if (t && t.closest(IGNORE + ', .bc-node--editing .bc-node__body')) return;
    if (state.lastPointerWasTouch) return;
    const node = t ? t.closest('.bc-node[data-bc-node]') : null;
    const label = t ? t.closest('.bc-group__label[data-bc-group]') : null;
    const world = worldPoint(state, e.clientX, e.clientY);
    invoke(state, 'OnDoubleTap', {
        nodeId: node ? node.getAttribute('data-bc-node') : null, edgeId: null,
        groupId: label ? label.getAttribute('data-bc-group') : null,
        worldX: world.x, worldY: world.y, shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey,
        pointerType: 'mouse', lockedDrag: false,
    });
}

function onLongPress(state) {
    const g = state.gesture;
    if (!g || g.kind !== 'pending' || g.pointerType !== 'touch') return;
    state.gesture = null;
    state.suppressMenuUntil = performance.now() + 800;
    state.swallowClickUntil = performance.now() + 700;
    const world = g.startWorld;
    invoke(state, 'OnLongPress', {
        nodeId: g.target === 'node' ? g.nodeId : null, edgeId: g.edgeId || null, groupId: g.groupId || null,
        clientX: g.startX, clientY: g.startY, worldX: world.x, worldY: world.y,
    });
}

function onContextMenu(state, e) {
    if (e.target instanceof Element && e.target.closest('input, textarea, [contenteditable="true"], .k-editor')) return;
    e.preventDefault();
    if (performance.now() < state.suppressMenuUntil) return;
    const t = e.target instanceof Element ? e.target : null;
    const node = t ? t.closest('.bc-node[data-bc-node]') : null;
    const edge = t ? t.closest('[data-bc-edge]') : null;
    const label = t ? t.closest('.bc-group__label[data-bc-group]') : null;
    const world = worldPoint(state, e.clientX, e.clientY);
    invoke(state, 'OnContextMenu', {
        nodeId: node ? node.getAttribute('data-bc-node') : null,
        edgeId: edge ? edge.getAttribute('data-bc-edge') : null,
        groupId: label ? label.getAttribute('data-bc-group') : null,
        clientX: e.clientX, clientY: e.clientY, worldX: world.x, worldY: world.y,
    });
}

function onKey(state, e, down) {
    if (e.key === ' ' || e.key === 'Spacebar') {
        const active = document.activeElement;
        const typing = active && (active.matches('input, textarea, select') || active.isContentEditable);
        if (typing || !active || !active.closest('.bc-board') || active.closest('.bc-board') !== state.el) {
            if (!down) state.spaceHeld = false;
            return;
        }
        e.preventDefault();
        state.spaceHeld = down;
        state.el.classList.toggle('bc-board--space', down);
        return;
    }
    if (down && e.key === 'Escape' && state.gesture) {
        e.preventDefault();
        e.stopPropagation();
        cancelGesture(state, true);
    }
}

// ── Ending ───────────────────────────────────────────────────────────────

function restorePreview(state, g) {
    if (g.elements) g.elements.forEach(el => {
        el.classList.remove('bc-node--dragging');
        el.style.removeProperty('--bc-dx');
        el.style.removeProperty('--bc-dy');
    });
    if (g.element) {
        g.element.classList.remove('bc-node--resizing');
        ['--bc-rx', '--bc-ry', '--bc-rw', '--bc-rh'].forEach(p => g.element.style.removeProperty(p));
    }
    if (g.box) g.box.classList.remove('bc-marquee--on');
    if (g.preview) g.preview.removeAttribute('d');
    if (g.edges) g.edges.forEach(edge => edge.paths.forEach((p, i) => {
        if (p.classList.contains('bc-edge__head')) p.removeAttribute('visibility');
        else if (edge.original[i] !== null) p.setAttribute('d', edge.original[i]);
    }));
    showGuides(state, null, null);
}

function cancelGesture(state, notify) {
    clearTimeout(state.longPressTimer);
    const g = state.gesture;
    state.gesture = null;
    state.pointers.clear();
    if (!g) { endBoardGesture(state); return; }
    restorePreview(state, g);
    if (g.kind === 'pan' || g.kind === 'pinch') {
        g.panX !== undefined && (state.vp = { panX: g.panX, panY: g.panY, zoom: state.vp.zoom });
        if (g.vp0) state.vp = g.vp0;
        applyViewport(state);
    }
    endBoardGesture(state);
    if (notify && (g.kind === 'move' || g.kind === 'resize' || g.kind === 'connect')) invoke(state, 'OnGestureCancelled');
}

function endBoardGesture(state) {
    state.el.classList.remove('bc-board--gesture', 'bc-board--panning');
}

// ── Helpers ──────────────────────────────────────────────────────────────

function invoke(state, method, payload) {
    try {
        const call = payload === undefined ? state.dotnet.invokeMethodAsync(method) : state.dotnet.invokeMethodAsync(method, payload);
        return call.catch(() => { /* the component was disposed */ });
    } catch {
        return Promise.resolve();
    }
}

function invokeResult(state, method, payload) {
    try {
        return state.dotnet.invokeMethodAsync(method, payload).catch(() => null);
    } catch {
        return Promise.resolve(null);
    }
}

function num(v) {
    return Number.isFinite(v) ? Math.round(v * 1000) / 1000 : 0;
}

function cssEscape(value) {
    return window.CSS && CSS.escape ? CSS.escape(value) : String(value).replace(/["\\]/g, '\\$&');
}

// ── Phone bottom sheet ─────────────────────────────────────────────────────────
//
// The grip drags the sheet with the same rule as the board: during the drag only --bc-sheet-dy and a class
// change, and .NET hears once, at the end, which snap point won. A short grip press is left to the grip's
// own click, which cycles half and full for keyboard and VoiceOver users.
//
// iOS does not resize the layout for the on-screen keyboard (it ignores interactive-widget), so the
// visual viewport's height tells how much of the sheet the keyboard covers; that goes in --bc-kb-inset.

const sheets = new Map();

export function attachSheet(sheetEl, dotnet) {
  if (!sheetEl) return;
  detachSheet(sheetEl);
  const grip = sheetEl.querySelector('.bc-sheet__grip');
  const s = { el: sheetEl, dotnet, drag: null };

  const down = (e) => {
    if (!grip || !grip.contains(e.target) || (e.pointerType === 'mouse' && e.button !== 0)) return;
    s.drag = { id: e.pointerId, y: e.clientY, dy: 0, moved: false };
    try { grip.setPointerCapture(e.pointerId); } catch { /* already released */ }
  };
  const move = (e) => {
    if (!s.drag || e.pointerId !== s.drag.id) return;
    const dy = e.clientY - s.drag.y;
    if (!s.drag.moved && Math.abs(dy) < 4) return;
    s.drag.moved = true;
    const top = sheetEl.getBoundingClientRect().top - s.drag.dy;
    s.drag.dy = Math.max(-Math.max(0, top - 56), dy);
    sheetEl.classList.add('bc-sheet--dragging');
    sheetEl.style.setProperty('--bc-sheet-dy', s.drag.dy + 'px');
    e.preventDefault();
  };
  const up = (e) => {
    if (!s.drag || e.pointerId !== s.drag.id) return;
    const { dy, moved } = s.drag;
    s.drag = null;
    sheetEl.classList.remove('bc-sheet--dragging');
    sheetEl.style.removeProperty('--bc-sheet-dy');
    if (!moved) return;
    const height = window.innerHeight || 800;
    const full = sheetEl.classList.contains('bc-sheet--full');
    // Down far enough closes; down a little from full settles at half; up enough opens full.
    const snap = dy > height * 0.5 || (!full && dy > 120) ? 'closed'
      : full && dy > height * 0.15 ? 'half'
      : dy < -height * 0.15 ? 'full'
      : full ? 'full' : 'half';
    dotnet.invokeMethodAsync('OnSheetSnap', snap).catch(() => {});
    // The click that follows a drag must not also cycle the snap.
    grip.addEventListener('click', swallow, { capture: true, once: true });
  };
  const cancel = () => {
    if (!s.drag) return;
    s.drag = null;
    sheetEl.classList.remove('bc-sheet--dragging');
    sheetEl.style.removeProperty('--bc-sheet-dy');
  };
  const swallow = (e) => { e.stopPropagation(); e.preventDefault(); };

  const vv = window.visualViewport;
  const inset = () => {
    if (!vv) return;
    const covered = Math.max(0, window.innerHeight - vv.height - vv.offsetTop);
    sheetEl.style.setProperty('--bc-kb-inset', covered + 'px');
  };
  const focusin = (e) => {
    if (e.target && typeof e.target.scrollIntoView === 'function') setTimeout(() => e.target.scrollIntoView({ block: 'nearest' }), 50);
  };

  sheetEl.addEventListener('pointerdown', down);
  sheetEl.addEventListener('pointermove', move);
  sheetEl.addEventListener('pointerup', up);
  sheetEl.addEventListener('pointercancel', cancel);
  sheetEl.addEventListener('focusin', focusin);
  window.addEventListener('blur', cancel);
  if (vv) { vv.addEventListener('resize', inset); vv.addEventListener('scroll', inset); inset(); }

  sheets.set(sheetEl, () => {
    sheetEl.removeEventListener('pointerdown', down);
    sheetEl.removeEventListener('pointermove', move);
    sheetEl.removeEventListener('pointerup', up);
    sheetEl.removeEventListener('pointercancel', cancel);
    sheetEl.removeEventListener('focusin', focusin);
    window.removeEventListener('blur', cancel);
    if (vv) { vv.removeEventListener('resize', inset); vv.removeEventListener('scroll', inset); }
  });
}

export function detachSheet(sheetEl) {
  const undo = sheets.get(sheetEl);
  if (!undo) return;
  undo();
  sheets.delete(sheetEl);
}