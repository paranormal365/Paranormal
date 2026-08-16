/**
 * sidecarInterop.js — browser-side glue for the native ffmpeg sidecar (item #38 phase E).
 *
 * The pairing token lives in localStorage, not in a cookie or anywhere the sidecar itself sets —
 * the browser is the one place trusted to hold it once the user has pasted it in. Kept in its own
 * tiny module (rather than folded into ffmpegInterop.js/opfsInterop.js) since it has nothing to
 * do with ffmpeg.wasm or OPFS — it's purely "remember this string across page loads".
 *
 * Phase 173 widened its job: EVERY sidecar request now goes through the fetch helpers at the
 * bottom of this file, not just the ones that move bytes. The sidecar is bound to the *user's*
 * loopback interface, so the request has to originate in the user's browser — a C# HttpClient
 * call resolves 127.0.0.1 on whatever machine the Blazor code is executing on, which is the
 * server under Blazor Server, and sends no Origin header for SecurityMiddleware to allowlist.
 */

const STORAGE_KEY = 'benvideo.sidecar.token';

/**
 * Header name carrying the pairing token. Mirrors SidecarProtocol.TokenHeaderName (C#) — the one
 * constant that genuinely has to exist on both sides of the interop boundary. Kept honest by
 * SidecarTransportContractTests, which reads this file and asserts the literal still matches.
 */
const TOKEN_HEADER = 'X-BenVideo-Sidecar-Token';

/**
 * AbortControllers for requests currently in flight, keyed by the caller's request id.
 * A CancellationToken on the C# side only cancels the *await*; without this map the underlying
 * fetch would keep running — which matters most for the two long ones (a large export result
 * download, and any poll loop that outlives its deadline).
 */
const inFlight = new Map();

export function getStoredToken() {
    try { return localStorage.getItem(STORAGE_KEY); }
    catch { return null; } // localStorage unavailable (e.g. private-mode edge cases) — treat as unpaired
}

export function setStoredToken(token) {
    try { localStorage.setItem(STORAGE_KEY, token); }
    catch { /* best-effort — pairing will just be asked for again next session */ }
}

export function clearStoredToken() {
    try { localStorage.removeItem(STORAGE_KEY); }
    catch { /* nothing to clear */ }
}

/**
 * Pairing v2 — exchanges a 6-digit code (from the sidecar's /pair page) for the long pairing
 * token. Returns the token string, or null when the code is wrong/expired/used. Must run here in
 * the browser, not through a C# HttpClient: under Blazor Server that client executes on the
 * SERVER, whose loopback is a different machine than the user's (the phase-173 lesson).
 * @param {string} url  the sidecar's /v1/pair endpoint
 * @param {string} code the 6-digit code the user typed
 * @returns {Promise<string|null>}
 */
export async function exchangePairCode(url, code) {
    try {
        const resp = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ code })
        });
        if (!resp.ok) return null;
        const body = await resp.json();
        return body?.token ?? null;
    } catch {
        return null;
    }
}

/**
 * Item #38 phase 123 (F) — checks whether a source clip is already cached on the sidecar, without
 * downloading or uploading anything.
 */
export async function headSource(url, token) {
    try {
        const resp = await fetch(url, { method: 'HEAD', headers: { [TOKEN_HEADER]: token } });
        return resp.ok;
    } catch {
        return false;
    }
}

/**
 * Uploads a browser File object straight from OPFS to the sidecar via fetch's own streaming body
 * support — deliberately NOT read into a C# byte[] first. A long-form project's source clip can be
 * gigabytes; copying it through the Blazor JS-interop boundary as a byte[] would double the peak
 * WASM heap for no reason, exactly the class of cost item #38 phases A/B already eliminated for
 * ffmpeg.wasm itself. `fileRef` is the IJSObjectReference File from OPFSService.ReadAsJSFileAsync.
 */
export async function putSourceFile(url, token, fileRef) {
    const resp = await fetch(url, {
        method: 'PUT',
        headers: { [TOKEN_HEADER]: token },
        body: fileRef,
    });
    if (!resp.ok) throw new Error(`Sidecar upload failed: HTTP ${resp.status}`);
}

/**
 * Fetch a sidecar URL and hand back a blob: URL — item #70 phase 159.
 *
 * <b>The bytes never cross into the WASM heap.</b> That is the entire point: a thumbnail strip is
 * N webp files, and marshalling each one through C# as a byte[] would put the frames on the same
 * single-threaded heap whose contention this item exists to relieve (item #66). fetch() → Blob →
 * object URL all happens in JS, and only the short URL string is returned to Blazor.
 *
 * @param {string} url
 * @param {string} token
 * @returns {Promise<string>} blob: URL
 */
export async function fetchAsBlobUrl(url, token) {
    const resp = await fetch(url, { headers: { [TOKEN_HEADER]: token } });
    if (!resp.ok) throw new Error(`Sidecar fetch failed: HTTP ${resp.status}`);
    return URL.createObjectURL(await resp.blob());
}

/**
 * Revoke a URL minted by fetchAsBlobUrl. Separate from the ffmpeg-worker revoke path in
 * ffmpegInterop.js because these URLs have no MEMFS backing file to also clean up — see
 * BlobUrlLifecycle (phase 144) for why ownership of each URL has to be unambiguous.
 *
 * @param {string} url
 */
export function revokeBlobUrl(url) {
    try { URL.revokeObjectURL(url); } catch { /* already revoked */ }
}

/**
 * Fetch a sidecar job result as a File handle — item #70 phase 162.
 *
 * Returned as a File (not bytes) so it can go straight into
 * FfmpegService.WriteFileAsync(name, fileRef), which streams it into MEMFS without ever
 * materializing a byte[] on the WASM heap. An export body is the largest single artifact this app
 * moves, so this is the one place where avoiding that copy matters most.
 *
 * @param {string} url
 * @param {string} token
 * @param {string} fileName
 * @returns {Promise<File>}
 */
export async function fetchResultAsFile(url, token, fileName) {
    const resp = await fetch(url, { headers: { [TOKEN_HEADER]: token } });
    if (!resp.ok) throw new Error(`Sidecar result fetch failed: HTTP ${resp.status}`);
    const blob = await resp.blob();
    return new File([blob], fileName, { type: 'video/mp4' });
}

// ── Generic request transport (phase 173) ────────────────────────────────────
//
// Every non-byte-moving sidecar call lands here. The outcome is always RETURNED, never thrown:
// C# distinguishes "the sidecar answered 409" from "nothing is listening on this port" from "we
// gave up waiting", and an exception crossing the interop boundary flattens all three into one
// JSException. The port scan in particular depends on telling a refused connection apart from a
// real response cheaply, five times in a row.
//
// Shape: { status, body, outcome } where outcome is 'ok' | 'aborted' | 'failed'. `status` is a
// real HTTP status only when outcome is 'ok'; it is 0 otherwise.

async function run(requestId, timeoutMs, work) {
    const controller = new AbortController();
    if (requestId) inFlight.set(requestId, controller);
    // A timeout and a caller abort both land on the same controller — C# tells them apart by
    // checking its own CancellationToken, which is the only thing that actually knows which
    // happened first.
    const timer = timeoutMs > 0 ? setTimeout(() => controller.abort(), timeoutMs) : null;
    try {
        return await work(controller.signal);
    } catch (e) {
        return {
            status: 0,
            body: e && e.name === 'AbortError' ? '' : String(e && e.message ? e.message : e),
            outcome: e && e.name === 'AbortError' ? 'aborted' : 'failed',
        };
    } finally {
        if (timer) clearTimeout(timer);
        if (requestId) inFlight.delete(requestId);
    }
}

/**
 * Issue one sidecar request and return its status and text body.
 *
 * @param {string} requestId  caller-generated id, so abortRequest can cancel this exact call
 * @param {string} method     'GET' | 'POST' | 'DELETE' | ...
 * @param {string} url        absolute sidecar URL
 * @param {string} token      pairing token
 * @param {string|null} bodyJson  pre-serialized JSON body, or null for no body
 * @param {number} timeoutMs  0 for no timeout
 */
export async function sendRequest(requestId, method, url, token, bodyJson, timeoutMs) {
    return run(requestId, timeoutMs, async (signal) => {
        // Omitted entirely when there is no token, rather than sent empty: /v1/health is the one
        // endpoint reachable unauthenticated, and it should look the same on the wire as it did
        // when the C# probe sent no token header at all.
        const headers = {};
        if (token) headers[TOKEN_HEADER] = token;
        if (bodyJson !== null && bodyJson !== undefined) headers['Content-Type'] = 'application/json';
        const resp = await fetch(url, {
            method,
            headers,
            body: bodyJson ?? undefined,
            signal,
        });
        return { status: resp.status, body: await resp.text(), outcome: 'ok' };
    });
}

/**
 * Same as sendRequest but returns the response body as bytes — used only for a rendered segment,
 * which its caller writes into MEMFS as a byte[] anyway. Anything the caller does NOT need in the
 * WASM heap (thumbnails, assembled previews, export bodies) still goes through fetchAsBlobUrl /
 * fetchResultAsFile instead, which never marshal the payload through C# at all.
 *
 * Unlike sendRequest this one returns the Uint8Array BARE and throws on every failure, rather than
 * wrapping it in a result object. Blazor transfers a directly-returned Uint8Array to byte[] over
 * its dedicated binary path; the same array nested inside an object would go through JSON
 * instead and arrive as {"0":31,"1":139,...} — a numeric-keyed object several times the size of
 * the payload it is meant to carry. The throw path is the price of that, so the message is tagged
 * with the outcome for SidecarTransport to map back to the right exception type.
 */
export async function sendRequestForBytes(requestId, url, token, timeoutMs) {
    const result = await run(requestId, timeoutMs, async (signal) => {
        const resp = await fetch(url, { method: 'GET', headers: { [TOKEN_HEADER]: token }, signal });
        if (!resp.ok) throw new Error(`Sidecar returned HTTP ${resp.status}.`);
        return new Uint8Array(await resp.arrayBuffer());
    });

    if (result instanceof Uint8Array) return result;
    throw new Error(`${result.outcome}:${result.body}`);
}

/** Aborts the in-flight request with this id, if it is still running. No-op otherwise. */
export function abortRequest(requestId) {
    const controller = inFlight.get(requestId);
    if (controller) controller.abort();
}
