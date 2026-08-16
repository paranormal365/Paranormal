/**
 * viewportInterop.js
 *
 * Blazor JS-isolation module for reading the browser viewport size.
 * Served at: /_content/Ben.Video.Editor/js/viewportInterop.js
 *
 * API
 * ───
 *  getViewportSize() → { width: number, height: number }
 */

export function getViewportSize() {
    return { width: window.innerWidth, height: window.innerHeight };
}
