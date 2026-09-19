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

    // The playhead follows the frames, not the browser's own timer.
    //
    // 'timeupdate' fires roughly four times a second, and it was the only thing driving the
    // playhead, the frame counter and every overlay positioned from the current time. So the
    // playhead advanced in visible quarter-second jumps, a title timed to a particular moment
    // appeared up to a quarter of a second late, and stepping a single frame reported a time that
    // could be several frames out (2026-09-05 audit, preview-10).
    //
    // A frame loop reports on every painted frame while playing and stops when it does not, so
    // there is no cost at all when nothing is moving. 'timeupdate' is kept for the paused cases
    // it still covers — seeking, scrubbing, loading — where no frames are being painted.
    let frameLoop = 0;

    const reportTime = () => dotnet.invokeMethodAsync('OnVideoTimeUpdate', el.currentTime);

    const tick = () => {
        if (el.paused || el.ended) { frameLoop = 0; return; }
        reportTime();
        frameLoop = requestAnimationFrame(tick);
    };

    const startLoop = () => { if (!frameLoop) frameLoop = requestAnimationFrame(tick); };
    const stopLoop  = () => { if (frameLoop) { cancelAnimationFrame(frameLoop); frameLoop = 0; } };

    el.addEventListener('timeupdate', () => { if (el.paused) reportTime(); });
    el.addEventListener('seeked', reportTime);
    el.addEventListener('ended',  () => { stopLoop(); reportTime(); dotnet.invokeMethodAsync('OnVideoEnded'); });
    el.addEventListener('play',   () => { startLoop(); dotnet.invokeMethodAsync('OnVideoPlay'); });
    el.addEventListener('pause',  () => { stopLoop(); reportTime(); dotnet.invokeMethodAsync('OnVideoPause'); });
    // Item #59-#65 flakiness investigation, phase 144 — previously nothing observed this at all,
    // so a dead/revoked blob: URL (or any other media load failure) failed completely silently:
    // a blank preview with no error anywhere. Explicitly NO retry here — the whole point of this
    // phase is surfacing these failures, not quietly working around them again.
    el.addEventListener('error', () => {
        const err = el.error;
        const detail = err ? `code ${err.code}${err.message ? `: ${err.message}` : ''}` : 'unknown error';
        dotnet.invokeMethodAsync('OnVideoError', detail, el.currentSrc || el.src || '');
    });

    _refs.set(elementId, { el, dotnet, stopLoop });
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

/**
 * Loads a new source and puts the playhead back where it was, resuming if it was playing.
 *
 * The Working Window is rebuilt after every edit, and each rebuild used to drop the playhead to
 * zero and stop playback. Trimming the end of a clip five minutes into a project meant scrubbing
 * back to five minutes, five times (2026-09-05 audit, preview-2).
 *
 * The seek has to wait for metadata: setting currentTime before the browser knows how long the new
 * source is silently does nothing, which is the shape this would have failed in.
 */
export function loadSrcPreservingTime(elementId, src, seconds, resume) {
    const ref = _refs.get(elementId);
    if (!ref) return;

    const el = ref.el;
    const restore = () => {
        el.removeEventListener('loadedmetadata', restore);
        try {
            // A rebuild can make the timeline shorter than where the playhead was — after a
            // ripple delete, say — so the far end is the closest thing to "where you were".
            const target = Number.isFinite(el.duration) && el.duration > 0
                ? Math.min(seconds, Math.max(0, el.duration - 0.05))
                : seconds;
            if (target > 0) el.currentTime = target;
            if (resume) el.play().catch(() => { /* autoplay refused; the person can press play */ });
        } catch { /* a source that failed to load has nothing to seek */ }
    };

    el.addEventListener('loadedmetadata', restore);
    el.src = src;
    el.load();
}

/**
 * Detaches whatever is loaded, so an empty timeline shows an empty player rather than the render
 * of the clips that were just deleted (2026-09-05 audit, preview-19).
 */
export function clearSrc(elementId) {
    const ref = _refs.get(elementId);
    if (!ref) return;
    ref.el.pause();
    ref.el.removeAttribute('src');
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
    // Stop the frame loop first: it calls into .NET, and the component is going away.
    ref.stopLoop?.();
    // Remove src to release memory
    ref.el.src = '';
    ref.el.load();
    _refs.delete(elementId);
}
