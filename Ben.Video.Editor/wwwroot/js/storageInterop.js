// Audit #4 — typed localStorage access, replacing interpolated `eval("localStorage.…")` calls.
//
// The old form was not an injection risk (every interpolated key was a constant or a typed Guid,
// and user-controlled values were double-JSON-encoded), but it had two real costs: it needs
// `unsafe-eval` in any Content-Security-Policy, and it pushed correctness onto string-building at
// every call site. These take values as ordinary arguments, so escaping stops being a concern.
//
// localStorage throws in two situations worth surviving rather than crashing over: quota exhausted
// on write, and access denied entirely (Safari private browsing, blocked third-party storage).
// Reads return null and writes report false, which lets the C# side treat "couldn't persist" as a
// normal outcome — the same shape it already handles for a missing key.

export function getItem(key) {
    try { return localStorage.getItem(key); }
    catch { return null; }
}

export function setItem(key, value) {
    try { localStorage.setItem(key, value); return true; }
    catch { return false; }
}

export function removeItem(key) {
    try { localStorage.removeItem(key); return true; }
    catch { return false; }
}
