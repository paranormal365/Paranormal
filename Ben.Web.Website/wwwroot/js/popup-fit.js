// Keeps a Telerik popup on the screen.
//
// THE DEFECT (Ben, 2026-09-09)
//
// On the case Investigations tab: Propose Dates → a date/time field → the calendar runs off the
// bottom of the window, and the buttons that commit the choice cannot be reached. Nothing
// scrolls to them.
//
// Telerik places a popup below whatever opened it and, when there is no room below, flips it
// above (`collision: {horizontal:"fit", vertical:"flip"}` — the default, and there is no
// parameter that reaches it). Flip is all it does: when the popup fits NEITHER side it is left
// hanging off the bottom edge. That is not an exotic case. Measured on the seeded case at
// 1280x720: the date/time popup is 417px tall, the field sits 411px down, so there is 309px
// below it and 411px above — and 417 fits in neither. A dialog centres its content vertically,
// which is exactly what puts a field in the half of the window where this is true.
//
// The page cannot rescue it either: `.k-animation-container` is position:absolute on <body>, so
// the popup adds no scroll height, and a dialog is a fixed layer anyway.
//
// WHAT THIS DOES
//
// After a popup appears — or changes size, which it does when its content streams in over the
// circuit, when the Date/Time tab is switched, or when the month changes — it is nudged back
// inside the window. Position first: a popup that fits the window at all is simply moved up
// until it does, even if that means covering the field it belongs to (a native <select> does the
// same, and a covered field is better than an unreachable button). Only a popup taller than the
// whole window gets a height cap and an internal scrollbar, because then there is nowhere to
// put it.
//
// A popup that already fits is not touched, so dropdowns, tooltips, grid filter menus and the
// editor's own popups behave exactly as they did.
//
// Why a script and not a stylesheet: the decision depends on where the anchor happens to be in
// the window, which CSS cannot see. Why not per-dialog settings: eleven dialogs in the library
// put a picker, dropdown, combo box or multi-select inside a modal, and the next one somebody
// writes would arrive with the bug already in it.

(function () {
    'use strict';

    // Breathing room against the window edge — enough that the popup reads as inside the
    // window rather than welded to it.
    const EDGE = 8;

    const watched = new Set();

    function popupOf(container) {
        return container.querySelector(':scope > .k-child-animation-container > .k-popup')
            || container.querySelector(':scope > .k-popup')
            || container.firstElementChild;
    }

    function clearCap(popup) {
        if (!popup.dataset.benPopupCapped) return;
        popup.style.maxHeight = '';
        popup.style.overflowY = '';
        delete popup.dataset.benPopupCapped;
    }

    // `remeasure` drops any cap first, so a window that has since grown gets its full popup
    // back. It is passed only from the window's own resize handler: doing it on every call
    // would fight the ResizeObserver below, which fires *because* the cap changed the height.
    function fit(container, remeasure) {
        if (!container.isConnected || container.offsetParent === null) return;

        const popup = popupOf(container);
        if (!popup) return;

        if (remeasure) clearCap(popup);

        const limit = window.innerHeight - 2 * EDGE;
        let rect = container.getBoundingClientRect();
        if (rect.height === 0) return;

        if (rect.height > limit) {
            popup.style.maxHeight = limit + 'px';
            popup.style.overflowY = 'auto';
            popup.dataset.benPopupCapped = '1';
            rect = container.getBoundingClientRect();
        }

        // Bottom first, then top: on a window too short for the popup the top edge wins, which
        // keeps the head of the content visible rather than its tail.
        let delta = 0;
        if (rect.bottom > window.innerHeight - EDGE) delta = (window.innerHeight - EDGE) - rect.bottom;
        if (rect.top + delta < EDGE) delta = EDGE - rect.top;
        if (Math.abs(delta) < 1) return;

        // `top` is in document coordinates and the delta is in viewport coordinates. They are
        // the same scale, so adding one to the other is all that is needed — and reading the
        // computed value rather than tracking our own means Telerik repositioning the popup
        // (it does, on scroll) always wins the starting point.
        const top = parseFloat(getComputedStyle(container).top) || 0;
        container.style.top = (top + delta) + 'px';
    }

    // One frame's worth of coalescing. A popup opening fires the size observer several times as
    // it animates, and there is no reason to measure more than once per paint.
    let pending = null;
    function schedule(container, remeasure) {
        if (pending) cancelAnimationFrame(pending.id);
        const targets = pending ? pending.targets : new Set();
        targets.add(container);
        const all = remeasure || (pending ? pending.remeasure : false);
        pending = {
            targets,
            remeasure: all,
            id: requestAnimationFrame(() => {
                const work = pending;
                pending = null;
                work.targets.forEach(t => fit(t, work.remeasure));
            }),
        };
    }

    const sizes = new ResizeObserver(entries => {
        for (const entry of entries) schedule(entry.target, false);
    });

    // Telerik keeps a container in the DOM and reuses it for the next open, so "it was added"
    // fires once and cannot be the only trigger. Watching each container's own style and class
    // catches every reopen and every reposition. Our own write to `style.top` comes back through
    // here, which is harmless: the second pass measures a popup that now fits and returns.
    const attributes = new MutationObserver(records => {
        for (const record of records) schedule(record.target, false);
    });

    function watch(container) {
        if (watched.has(container)) return;
        watched.add(container);
        sizes.observe(container);
        attributes.observe(container, { attributes: true, attributeFilter: ['style', 'class'] });
    }

    function scan(node) {
        if (node.nodeType !== 1) return;
        if (node.classList.contains('k-animation-container')) {
            watch(node);
            schedule(node, false);
        }
    }

    function start() {
        document.querySelectorAll('.k-animation-container').forEach(scan);

        new MutationObserver(records => {
            for (const record of records) {
                record.addedNodes.forEach(scan);
                record.removedNodes.forEach(node => {
                    if (node.nodeType === 1 && watched.has(node)) {
                        watched.delete(node);
                        sizes.unobserve(node);
                    }
                });
            }
        }).observe(document.body, { childList: true });

        const refit = () => watched.forEach(container => schedule(container, true));
        window.addEventListener('resize', refit);
        window.addEventListener('orientationchange', refit);
    }

    // The tag is in <head>, so <body> may not exist yet.
    if (document.body) start();
    else document.addEventListener('DOMContentLoaded', start, { once: true });
})();
