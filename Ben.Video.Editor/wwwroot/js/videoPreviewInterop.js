// videoPreviewInterop.js
// Collocated JS isolation module for VideoPreview.razor
// Served at: /_content/Ben.Video.Editor/js/videoPreviewInterop.js

const _refs = new Map(); // elementId -> { el, dotnet }

/**
 * Initialise event listeners for a <video> element.
 * @param {string} elementId
 * @param {DotNetObjectReference} dotnet
 */
export function init(elementId, dotnet) {
    const el = document.getElementById(elementId);
    if (!el) return;

    el.addEventListener('timeupdate', () => {
        dotnet.invokeMethodAsync('OnVideoTimeUpdate', el.currentTime);
    });
    el.addEventListener('ended',  () => dotnet.invokeMethodAsync('OnVideoEnded'));
    el.addEventListener('play',   () => dotnet.invokeMethodAsync('OnVideoPlay'));
    el.addEventListener('pause',  () => dotnet.invokeMethodAsync('OnVideoPause'));
    // Item #59-#65 flakiness investigation, phase 144 — previously nothing observed this at all,
    // so a dead/revoked blob: URL (or any other media load failure) failed completely silently:
    // a blank preview with no error anywhere. Explicitly NO retry here — the whole point of this
    // phase is surfacing these failures, not quietly working around them again.
    el.addEventListener('error', () => {
        const err = el.error;
        const detail = err ? `code ${err.code}${err.message ? `: ${err.message}` : ''}` : 'unknown error';
        dotnet.invokeMethodAsync('OnVideoError', detail, el.currentSrc || el.src || '');
    });

    _refs.set(elementId, { el, dotnet });
}

/**
 * Load a new src into the video element.
 * @param {string} elementId
 * @param {string} src  blob: URL or object URL
 */
export function loadSrc(elementId, src) {
    const ref = _refs.get(elementId);
    if (!ref) return;
    ref.el.src = src;
    ref.el.load();
}

/** Play the video. */
export function play(elementId) {
    _refs.get(elementId)?.el.play();
}

/** Pause the video. */
export function pause(elementId) {
    _refs.get(elementId)?.el.pause();
}

/**
 * Seek to a specific time in seconds.
 * @param {string} elementId
 * @param {number} seconds
 */
export function seek(elementId, seconds) {
    const ref = _refs.get(elementId);
    if (!ref) return;
    ref.el.currentTime = seconds;
}

/**
 * Set muted state.
 * @param {string} elementId
 * @param {boolean} muted
 */
export function setMuted(elementId, muted) {
    const ref = _refs.get(elementId);
    if (ref) ref.el.muted = muted;
}

/**
 * Clean up event listeners and remove from registry.
 * @param {string} elementId
 */
export function dispose(elementId) {
    const ref = _refs.get(elementId);
    if (!ref) return;
    // Remove src to release memory
    ref.el.src = '';
    ref.el.load();
    _refs.delete(elementId);
}
