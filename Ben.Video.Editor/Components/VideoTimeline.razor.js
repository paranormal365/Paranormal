// VideoTimeline.razor.js — JS isolation module for inline trim handle dragging
// and drag-and-drop position measurement.

/**
 * Returns the rendered pixel width of the given element.
 * Called once at the start of a trim drag so C# can convert pixel delta → seconds.
 * @param {Element} el
 * @returns {number} width in pixels
 */
export function measureWidth(el) {
    if (!el) return 0;
    return el.getBoundingClientRect().width;
}

/**
 * Sets pointer capture on the clip chip / trim handle at the given viewport
 * coordinates, so pointermove/pointerup keep firing on it even when a fast
 * drag moves the cursor off the element before Blazor's next render lands
 * (or, for the chip body, before the draggable="false" attribute takes
 * effect — capture also stops the native HTML5 drag from hijacking the
 * gesture, since the captured element keeps receiving pointer events
 * instead of the browser starting drag-and-drop).
 * @param {number} x  clientX from the pointerdown event
 * @param {number} y  clientY from the pointerdown event
 * @param {number} pointerId
 */
export function capturePointerAt(x, y, pointerId) {
    const el = document.elementFromPoint(x, y);
    // .bv-transition-chip/.bv-transition-resize-handle — item #57 T4's edge-drag resize, same
    // capture-on-the-topmost-matching-ancestor pattern as clip trim handles.
    const target = el?.closest('.bv-clip-chip, .bv-trim-handle, .bv-transition-chip, .bv-transition-resize-handle');
    if (target && target.setPointerCapture) {
        target.setPointerCapture(pointerId);
    }
}

/**
 * Returns the bounding-rect of the track content area (the scrollable clip strip,
 * excluding the label gutter) for a given track ID.
 *
 * @param {string} trackId  The track's Guid as a string (matches data-track-id attribute)
 * @returns {{ left: number, width: number, scrollLeft: number }}  Viewport-relative left edge, pixel width, and current scroll offset
 */
export function measureTrackContent(trackId) {
    const trackEl = document.querySelector(`[data-track-id="${trackId}"]`);
    if (!trackEl) return { left: 0, width: 0, scrollLeft: 0 };
    const content = trackEl.querySelector('.bv-track__items');
    if (!content) return { left: 0, width: 0, scrollLeft: 0 };
    const rect = content.getBoundingClientRect();
    return { left: rect.left, width: rect.width, scrollLeft: content.scrollLeft };
}

/**
 * Returns the pixel width of the scrollable tracks container (excluding the
 * track-label gutter). Used by the "Fit" button to compute zoom-to-fit.
 * @returns {number} visible pixel width of the clip area
 */
export function getTracksAreaWidth() {
    const el = document.querySelector('.bv-timeline__tracks');
    if (!el) return 0;
    // Subtract the gutter width (120px) from the total track container width
    return Math.max(0, el.getBoundingClientRect().width - 120);
}

/**
 * Returns the viewport-relative vertical bounds of every track row, keyed by track id.
 * Called once at the start of a clip body-drag so C# can determine which row the pointer
 * is over on every pointermove using pure (cheap, synchronous) math against these cached
 * bounds, rather than a JS round-trip per pointermove — same rationale as
 * measureTrackContent's per-drag-session caching for the ClipBrowser-drop path.
 * @returns {{trackId: string, top: number, bottom: number}[]}
 */
/**
 * Scrolls a clip chip into view (both axes) if it isn't already fully visible within the
 * timeline's scroll container — after placing a new clip (item #24/#25) or moving an existing
 * one, possibly to a different track. Deferred one tick so it runs after Blazor's render has
 * actually applied the new position/track to the DOM (this is called synchronously from the C#
 * side right after the state change, before that render has necessarily landed).
 * A no-op — not an error — if the chip isn't found (e.g. called for an id that was since removed).
 * @param {string} itemId
 */
export function scrollItemIntoView(itemId) {
    // setTimeout, not requestAnimationFrame: rAF is fully suspended by the browser while the
    // document is hidden/backgrounded, which would silently drop this call in that case. A
    // macrotask still fires either way and is just as sufficient to wait for Blazor's render
    // (queued as part of the same JS-interop call) to land before querying the DOM.
    setTimeout(() => {
        const chip = document.querySelector(`[data-item-id="${itemId}"]`);
        if (chip) chip.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
    }, 0);
}

export function measureAllTrackRows() {
    const rows = [...document.querySelectorAll('[data-track-id]')];
    return rows.map(el => {
        const r = el.getBoundingClientRect();
        return { trackId: el.getAttribute('data-track-id'), top: r.top, bottom: r.bottom };
    });
}

