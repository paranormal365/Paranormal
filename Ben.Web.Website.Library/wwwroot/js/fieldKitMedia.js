// The Field Kit player's media clock.
//
// When a session has audio or video, that element is the clock. The browser owns the element's
// time; the page cannot read currentTime from the server, and Blazor's @ontimeupdate carries no
// value, so a listener here reports it back through a .NET reference — about four times a
// second, the same cadence the page's own tick loop already renders at. Play, pause and seek go
// the other way for the same reason: they are methods and a settable property, not attributes.

const attached = new WeakMap();

export function attach(el, dotnet, key) {
    if (!el || attached.has(el)) return;
    const handlers = {
        timeupdate: () => dotnet.invokeMethodAsync("OnMediaTime", key, el.currentTime),
        play:       () => dotnet.invokeMethodAsync("OnMediaPlay", key, el.currentTime),
        pause:      () => dotnet.invokeMethodAsync("OnMediaPause", key),
        ended:      () => dotnet.invokeMethodAsync("OnMediaEnded", key),
        // How long the recording runs, which only the browser knows. Without it the page has no
        // idea where a recording ENDS, and treated one as covering the whole session from its
        // start onwards — so scrubbing anywhere and pressing play handed the clock to a
        // recording that had finished minutes earlier.
        loadedmetadata: () => dotnet.invokeMethodAsync("OnMediaDuration", key, el.duration),
    };
    for (const name in handlers) el.addEventListener(name, handlers[name]);
    attached.set(el, handlers);

    // Already loaded by the time this attached — preload="metadata" often wins the race — so the
    // event has been and gone and nobody would ever hear the duration.
    if (!isNaN(el.duration) && el.duration > 0) handlers.loadedmetadata();
}

export function detach(el) {
    const handlers = el && attached.get(el);
    if (!handlers) return;
    for (const name in handlers) el.removeEventListener(name, handlers[name]);
    attached.delete(el);
}

/**
 * Seeks and then plays, in one call, waiting for the seek to actually land.
 *
 * Two calls could not do this. `seek` followed by `play` from the server is two round trips, and
 * between them the element may still be loading — in which case the assignment to currentTime is
 * dropped, play starts at zero, and the first timeupdate reports second zero as the playhead.
 * That is the playback starting over from the beginning, and no amount of ordering on the C# side
 * fixes it, because the wait that matters is inside the browser.
 */
export async function playFrom(el, seconds) {
    if (!el) return "no element";
    try {
        await ready(el);
        const target = Math.max(0, Math.min(seconds, el.duration || seconds));
        if (Math.abs(el.currentTime - target) > 0.25) await seekTo(el, target);
        await el.play();
        return null;
    } catch (e) {
        return (e && e.message) || "the browser refused to play";
    }
}

/** Resolves once the element knows its own duration, or gives up after a moment. */
function ready(el) {
    if (!isNaN(el.duration) && el.duration > 0) return Promise.resolve();
    if (el.preload === "none") el.load();
    return new Promise(resolve => {
        const done = () => { el.removeEventListener("loadedmetadata", done); resolve(); };
        el.addEventListener("loadedmetadata", done, { once: true });
        // Never hang the button on a file that will not load: playing from the wrong place beats
        // a Play that does nothing at all, and the element reports where it really is anyway.
        setTimeout(done, 1500);
    });
}

/** Resolves when the element has finished moving, so play() starts from where it was sent. */
function seekTo(el, seconds) {
    return new Promise(resolve => {
        const done = () => { el.removeEventListener("seeked", done); resolve(); };
        el.addEventListener("seeked", done, { once: true });
        el.currentTime = seconds;
        setTimeout(done, 1000);
    });
}

export function pause(el) {
    if (el && !el.paused) el.pause();
}

export function rate(el, r) {
    if (el) el.playbackRate = r;
}

export function seek(el, seconds) {
    if (!el) return;
    const s = Math.max(0, seconds);
    // Before metadata has loaded, duration is NaN and some browsers drop the assignment; wait
    // for it once, then seek.
    if (isNaN(el.duration)) {
        el.addEventListener("loadedmetadata", () => { el.currentTime = s; }, { once: true });
        if (el.preload === "none") el.load();
        return;
    }
    el.currentTime = Math.min(s, el.duration);
}
