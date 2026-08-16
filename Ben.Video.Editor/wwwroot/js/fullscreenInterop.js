/**
 * fullscreenInterop.js
 *
 * Blazor JS-isolation module for the editor's fullscreen toggle.
 * Served at: /_content/Ben.Video.Editor/js/fullscreenInterop.js
 *
 * API
 * ───
 *  toggle(el)                → Promise<void>  Enter fullscreen on `el`, or exit if already active
 *  isFullscreen()             → bool           Whether any element is currently fullscreen
 *  addChangeListener(dotnetRef) → void          Forwards fullscreenchange events to
 *                                                dotnetRef.OnFullscreenChanged(bool)
 */

export function toggle(el) {
    if (!document.fullscreenElement) {
        return el.requestFullscreen();
    }
    return document.exitFullscreen();
}

export function isFullscreen() {
    return !!document.fullscreenElement;
}

export function addChangeListener(dotnetRef) {
    document.addEventListener('fullscreenchange', () => {
        dotnetRef.invokeMethodAsync('OnFullscreenChanged', !!document.fullscreenElement);
    });
}
