// Audit #4 — small typed DOM helpers, replacing hand-built `eval("…")` strings.
//
// Beyond the CSP problem, the eval form produced a real, user-reachable crash (audit #7): a
// `getElementById(x).click()` string threw a TypeError when the element was absent, which
// propagated as an unhandled Blazor render exception and killed the whole circuit. Every lookup
// here is null-guarded, so a missing element is a no-op or a null/empty result the caller can
// reason about — never a dead app.

/** Focus and select an input, deferred one tick so it works when called during a render that is
 *  itself what puts the element on the page. No-op when the element isn't there. */
export function focusAndSelect(selector) {
    setTimeout(() => {
        const el = document.querySelector(selector);
        if (el) { el.focus(); if (el.select) el.select(); }
    }, 0);
}

/** Click an element. `defer` is needed when the element is rendered by the very interaction that
 *  triggers this call and doesn't exist yet at call time. */
export function click(selector, defer = false) {
    const go = () => document.querySelector(selector)?.click();
    if (defer) setTimeout(go, 0); else go();
}

// ── File <input> access ──────────────────────────────────────────────────────
// All of these take the input's id plus (where relevant) an index, instead of interpolating both
// into a source string. `?.` throughout: the inputs live inside components that unmount.

export function fileCount(inputId) {
    return document.getElementById(inputId)?.files?.length ?? 0;
}

export function fileName(inputId, index) {
    return document.getElementById(inputId)?.files?.[index]?.name ?? '';
}

export function fileSize(inputId, index) {
    return document.getElementById(inputId)?.files?.[index]?.size ?? 0;
}

/** The browser's own MIME type for the file — what the operating system says it is, rather than
 *  what somebody named it. Empty when the browser has no idea, which is common for the less usual
 *  formats; the caller falls back to the extension. */
export function fileType(inputId, index) {
    return document.getElementById(inputId)?.files?.[index]?.type ?? '';
}

/** The File itself, handed back as a JS object reference so C# can stream it without a byte[]
 *  copy (same pattern as sidecarInterop.fetchResultAsFile). Null when absent. */
export function fileAt(inputId, index) {
    return document.getElementById(inputId)?.files?.[index] ?? null;
}

export function fileObjectUrl(inputId, index) {
    const file = document.getElementById(inputId)?.files?.[index];
    return file ? URL.createObjectURL(file) : null;
}

export function clearFileInput(inputId) {
    const el = document.getElementById(inputId);
    if (el) el.value = '';
}

// ── Leaving the page ─────────────────────────────────────────────────────────

let _unloadGuard = null;

/**
 * Turns the browser's "leave site?" prompt on or off for this page.
 *
 * Nothing asked before this. Closing the tab, or following a link out of the editor, took whatever
 * was unsaved with it — and a page that has not registered a handler gets no warning of its own, so
 * there was no moment at which anybody could have noticed (2026-09-05 audit, F9).
 *
 * The listener is added and removed rather than left in place returning nothing, because a
 * registered beforeunload handler disables the browser's back/forward cache even when it does not
 * fire.
 */
export function setUnloadGuard(enabled, reason) {
    if (enabled) {
        if (_unloadGuard) { _unloadGuard.reason = reason; return; }

        _unloadGuard = { reason };
        _unloadGuard.handler = (e) => {
            e.preventDefault();
            // Ignored by most browsers now, which show their own wording. Set for the ones that
            // still honour it.
            e.returnValue = _unloadGuard.reason ?? '';
            return _unloadGuard.reason ?? '';
        };
        window.addEventListener('beforeunload', _unloadGuard.handler);
        return;
    }

    if (!_unloadGuard) return;
    window.removeEventListener('beforeunload', _unloadGuard.handler);
    _unloadGuard = null;
}

/**
 * Calls back one last time as the page is hidden, so a pending autosave can be written.
 *
 * pagehide is the event that actually fires on every path out — a closed tab, a followed link, and
 * on mobile a switch to another app, where beforeunload frequently does not fire at all.
 */
export function flushOnPageHide(dotnet, methodName) {
    window.addEventListener('pagehide', () => {
        try { dotnet.invokeMethodAsync(methodName); } catch { /* the page is going away */ }
    }, { once: true });
}

// ── Misc ─────────────────────────────────────────────────────────────────────

/**
 * Natural pixel dimensions of an image URL, as [width, height].
 *
 * The eval version this replaces set `img.src` and read `naturalWidth` on the very next statement,
 * synchronously — before the browser could possibly have decoded anything — so it returned [0,0]
 * essentially always. Awaiting decode is what makes the value real; the previous behaviour was a
 * latent bug, not a style problem. Returns [0,0] on a load failure rather than rejecting, since
 * every caller already treats 0 as "unknown".
 */
export async function imageDimensions(url) {
    return await new Promise(resolve => {
        const img = new Image();
        img.onload  = () => resolve([img.naturalWidth || 0, img.naturalHeight || 0]);
        img.onerror = () => resolve([0, 0]);
        img.src = url;
    });
}

/**
 * Save text to a file via a transient blob URL.
 *
 * The 30s deferred revoke is deliberate and load-bearing (phase 144): `a.click()` only *dispatches*
 * the browser's own download fetch, so revoking in the same tick races it and can produce a
 * silent, intermittent failure. Do not "tidy" this into an immediate revoke.
 */
/**
 * Read a blob: URL back as bytes — phase 176, for handing a finished render to a host that
 * publishes it (its API takes a byte[]).
 *
 * Deliberately reads the blob: URL rather than the OPFS file behind it. A retained export always
 * has one of these, whether it came from OPFS or — when OPFS is unavailable, e.g. Safari private
 * browsing — straight from MEMFS, so this is the single path that works in both cases. Reading
 * OPFS instead would silently hand back nothing on the fallback branch, which is the sort of thing
 * that only shows up in the one browser nobody tested.
 *
 * @param {string} url
 * @returns {Promise<Uint8Array>}
 */
export async function blobUrlAsBytes(url) {
    const resp = await fetch(url);
    if (!resp.ok) throw new Error(`Reading rendered output failed: HTTP ${resp.status}`);
    return new Uint8Array(await resp.arrayBuffer());
}

export function downloadText(text, fileName, mimeType) {
    const url = URL.createObjectURL(new Blob([text], { type: mimeType }));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
}
