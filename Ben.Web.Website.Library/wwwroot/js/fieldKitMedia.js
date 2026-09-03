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
    };
    for (const name in handlers) el.addEventListener(name, handlers[name]);
    attached.set(el, handlers);
}

export function detach(el) {
    const handlers = el && attached.get(el);
    if (!handlers) return;
    for (const name in handlers) el.removeEventListener(name, handlers[name]);
    attached.delete(el);
}

// play() returns a promise the browser may reject when it decides the page has no user
// activation. The page's Play button IS a click, and activation is sticky for the document, so
// in practice it resolves; when it does not, the reason comes back as a sentence rather than an
// exception, and the page falls back to its own tick loop.
export async function play(el) {
    if (!el) return "no element";
    try { await el.play(); return null; }
    catch (e) { return (e && e.message) || "the browser refused to play"; }
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
