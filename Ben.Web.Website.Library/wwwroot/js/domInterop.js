// Typed DOM helpers for Ben.Web.Website.Library components.
//
// This file existed only as an import path until 2026-08-22: MyVideosPage and
// CaseVideoEditorPage imported /_content/Ben.Web.Website.Library/js/domInterop.js, but the
// only domInterop.js shipped in Ben.Video.Editor's static assets — so the import rejected and
// both Publish buttons died with the circuit. Everything here is null-guarded for the same
// reason as the editor's copy: a missing element must be a no-op, never a dead app.

/** Click an element by id. No-op when the element isn't on the page. */
export function clickElementById(id) {
    document.getElementById(id)?.click();
}

/** Scroll an element into view, deferred one tick so it works when the element is rendered by
 *  the very interaction that triggers this call. `block` defaults to 'center' (right for rows);
 *  pass 'start' for elements taller than the viewport, where centering puts the top off-screen.
 *  Smooth scrolling silently does nothing on some engines when the scroller is an inner
 *  container rather than the document — measured, not theoretical — so after a beat we check
 *  whether the element actually became visible and jump instantly if not. */
export function scrollToElementId(id, block = 'center') {
    setTimeout(() => {
        const el = document.getElementById(id);
        if (!el) return;
        el.scrollIntoView({ behavior: 'smooth', block });
        setTimeout(() => {
            const r = el.getBoundingClientRect();
            if (r.bottom < 0 || r.top > window.innerHeight)
                el.scrollIntoView({ block });
        }, 350);
    }, 0);
}

/** Brings the heading the address names (#the-public-catalog) into view once the page has
 *  actually drawn it — and keeps it there while the pictures above it load.
 *
 *  A help guide renders after the circuit connects, so the browser's own jump-to-anchor runs
 *  against a loading spinner and finds nothing; and a jump made before the screenshots above have
 *  loaded is pushed down by every one that arrives. "How the catalogue works" on the equipment
 *  page landed on "Retiring a piece", two sections early (Ben, 2026-10-02). So: jump now, jump
 *  again as each unloaded image settles, and stop the moment the reader scrolls for themselves. */
export function scrollToHashWhenSettled() {
    const id = decodeURIComponent((location.hash || '').slice(1));
    if (!id) return;
    const el = document.getElementById(id);
    if (!el) return;

    let readerMoved = false;
    const stop = () => { readerMoved = true; };
    for (const type of ['wheel', 'touchmove', 'keydown', 'mousedown'])
        window.addEventListener(type, stop, { once: true, passive: true });

    const go = () => { if (!readerMoved) el.scrollIntoView({ block: 'start' }); };
    go();
    for (const img of document.images)
        if (!img.complete) img.addEventListener('load', go, { once: true });
    setTimeout(go, 1500);
}
