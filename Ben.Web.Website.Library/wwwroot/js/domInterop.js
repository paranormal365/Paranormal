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

/** Scroll an element into the middle of the viewport, deferred one tick so it works when the
 *  element is rendered by the very navigation that triggers this call. */
export function scrollToElementId(id) {
    setTimeout(() => {
        document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }, 0);
}
