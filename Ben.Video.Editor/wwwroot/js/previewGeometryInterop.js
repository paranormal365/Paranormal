/**
 * previewGeometryInterop.js
 *
 * Blazor JS-isolation module for measuring the preview screen element's live rendered box, used to
 * correctly map pointer coordinates onto the composition canvas (accounting for object-fit: contain
 * letterboxing, which nothing in this app measured before this phase).
 * Served at: /_content/Ben.Video.Editor/js/previewGeometryInterop.js
 *
 * API
 * ───
 *  getElementRect(el) → { left: number, top: number, width: number, height: number }
 */

export function getElementRect(el) {
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
}
