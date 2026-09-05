/**
 * opfsInterop.js — Origin Private File System helpers for Ben.Video.Editor
 *
 * Source files (video/audio/image) imported into the editor are written here so
 * that saved projects can be reopened without manual re-import.
 *
 * Layout:
 *   OPFS root/
 *     bv-clips/
 *       {clipId}.mp4    ← video source files
 *       {clipId}.mp3    ← audio source files
 *       {clipId}.png    ← image source files  (any image ext)
 *
 * All functions are exported for use as a lazy-loaded JS module:
 *   await JS.InvokeAsync<IJSObjectReference>("import", "/_content/Ben.Video.Editor/js/opfsInterop.js")
 */

const BV_CLIPS_DIR   = 'bv-clips';
const BV_EXPORTS_DIR = 'bv-exports'; // item #38 phase D — finished export output, not source clips

async function getDir(dirName) {
    const root = await navigator.storage.getDirectory();
    return root.getDirectoryHandle(dirName, { create: true });
}

async function getClipsDir() {
    return getDir(BV_CLIPS_DIR);
}

async function getExportsDir() {
    return getDir(BV_EXPORTS_DIR);
}

/** Returns true when OPFS is available in this browser. */
export async function opfsIsAvailable() {
    if (typeof navigator === 'undefined'
        || !('storage' in navigator)
        || typeof navigator.storage.getDirectory !== 'function') {
        return false;
    }

    // Having the storage is not the same as being able to write to it. Every write here goes
    // through createWritable(), which Safari did not implement on the main thread until version
    // 26 — so this check said yes, each write then threw, and the person's media was never
    // persisted. Nothing surfaced that: the editor worked until the page was reloaded, at which
    // point every clip came back missing (2026-09-05 audit, the completeness critic's browser-
    // support item).
    //
    // Probed once rather than asserted from a feature list, because the prototype exists in some
    // builds where the call still fails.
    if (_writableProbe === null) _writableProbe = probeWritable();
    return await _writableProbe;
}

let _writableProbe = null;

async function probeWritable() {
    try {
        const root = await navigator.storage.getDirectory();
        const name = `.bv-write-probe-${Math.random().toString(36).slice(2)}`;
        const fh   = await root.getFileHandle(name, { create: true });

        try {
            const wr = await fh.createWritable();
            await wr.close();
            return true;
        } finally {
            try { await root.removeEntry(name); } catch { /* nothing to tidy */ }
        }
    } catch (err) {
        console.warn('[opfs] storage is present but not writable here:', err);
        return false;
    }
}

/**
 * Write a browser File or Blob to OPFS.
 * @param {string} clipId   - Clip GUID (used as filename base)
 * @param {string} ext      - File extension including dot, e.g. ".mp4"
 * @param {File|Blob} file  - The file object to persist
 */
export async function opfsWrite(clipId, ext, file) {
    const dir = await getClipsDir();
    const fh  = await dir.getFileHandle(`${clipId}${ext}`, { create: true });
    const wr  = await fh.createWritable();
    await wr.write(file);
    await wr.close();
}

/**
 * Write raw bytes (Uint8Array) to OPFS — used for files downloaded from a server API.
 * @param {string} clipId
 * @param {string} ext
 * @param {Uint8Array} bytes
 */
export async function opfsWriteBytes(clipId, ext, bytes) {
    const dir = await getClipsDir();
    const fh  = await dir.getFileHandle(`${clipId}${ext}`, { create: true });
    const wr  = await fh.createWritable();
    await wr.write(bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes));
    await wr.close();
}

/**
 * Returns true when a clip file exists in OPFS.
 * @param {string} clipId
 * @param {string} ext
 */
export async function opfsExists(clipId, ext) {
    try {
        const dir = await getClipsDir();
        await dir.getFileHandle(`${clipId}${ext}`);
        return true;
    } catch {
        return false;
    }
}

/**
 * Read a clip file from OPFS as a browser File object.
 * Throws if the file does not exist.
 * @param {string} clipId
 * @param {string} ext
 * @returns {File}
 */
export async function opfsReadAsFile(clipId, ext) {
    const dir = await getClipsDir();
    const fh  = await dir.getFileHandle(`${clipId}${ext}`);
    return fh.getFile();
}

/**
 * List all files in the bv-clips/ OPFS directory.
 * Returns an array of { clipId, ext, sizeBytes } objects.
 * clipId is the filename without extension (the Guid string).
 * @returns {Promise<Array<{clipId: string, ext: string, sizeBytes: number}>>}
 */
/**
 * How much of the browser's storage this site is using, and how much it is allowed.
 *
 * Nothing read this. Every import writes a copy of the file into that storage, nothing ever freed
 * one, and the first anybody knew about the quota was a save that failed (2026-09-05 audit,
 * media-2). Returns nulls where the browser declines to say, which some do.
 */
export async function opfsEstimate() {
    try {
        if (typeof navigator === 'undefined' || !navigator.storage?.estimate) return { usage: null, quota: null };
        const { usage, quota } = await navigator.storage.estimate();
        return { usage: usage ?? null, quota: quota ?? null };
    } catch {
        return { usage: null, quota: null };
    }
}

export async function opfsListClips() {
    try {
        const dir = await getClipsDir();
        const entries = [];
        for await (const [name, handle] of dir.entries()) {
            if (handle.kind !== 'file') continue;
            const dotIdx = name.lastIndexOf('.');
            if (dotIdx < 0) continue;
            const clipId = name.substring(0, dotIdx);
            const ext    = name.substring(dotIdx);
            const file   = await handle.getFile();
            entries.push({ clipId, ext, sizeBytes: file.size });
        }
        return entries;
    } catch {
        return [];
    }
}

/**
 * Read an OPFS clip file as a UTF-8 text string (e.g. SVG source).
 * @param {string} clipId
 * @param {string} ext  e.g. ".svg"
 * @returns {Promise<string>}
 */
export async function opfsReadAsText(clipId, ext) {
    const dir  = await getClipsDir();
    const fh   = await dir.getFileHandle(`${clipId}${ext}`);
    const file = await fh.getFile();
    return file.text();
}

/**
 * Read an OPFS clip file and return a blob: URL for it, for use directly as an <img src>
 * (or similar). The caller is responsible for revoking the URL via opfsRevokeBlobUrl once it's
 * no longer displayed — blob: URLs otherwise stay alive (and leak memory) until page unload.
 * @param {string} clipId
 * @param {string} ext
 * @returns {Promise<string>}
 */
export async function opfsReadAsBlobUrl(clipId, ext) {
    const dir  = await getClipsDir();
    const fh   = await dir.getFileHandle(`${clipId}${ext}`);
    const file = await fh.getFile();
    return URL.createObjectURL(file);
}

/**
 * Revoke a blob: URL previously returned by opfsReadAsBlobUrl.
 * @param {string} url
 */
export function opfsRevokeBlobUrl(url) {
    try { URL.revokeObjectURL(url); } catch { /* already revoked or invalid */ }
}

/**
 * Delete a clip file from OPFS. Silent no-op if file does not exist.
 * @param {string} clipId
 * @param {string} ext
 */
export async function opfsDelete(clipId, ext) {
    try {
        const dir = await getClipsDir();
        await dir.removeEntry(`${clipId}${ext}`);
    } catch { /* file may not exist */ }
}

/**
 * Returns storage quota information, or null if the API is unavailable.
 * @returns {{ usedBytes: number, totalBytes: number } | null}
 */
export async function opfsGetQuota() {
    if (!navigator.storage || typeof navigator.storage.estimate !== 'function') return null;
    const { usage, quota } = await navigator.storage.estimate();
    return { usedBytes: usage ?? 0, totalBytes: quota ?? 0 };
}

// ─── bv-exports/ — finished export output (item #38 phase D) ─────────────────
//
// A separate OPFS area from bv-clips/ above (source clips). Keyed by exportId (an ExportJob's own
// Guid, not a clip id) + real output extension. Written once by ffmpegInterop.js's exportToOpfs
// right after a render finishes; read back once (download or full-quality preview blob URL) and
// then deleted — see README-phase-119.md for why no retention policy exists yet.

/**
 * Write raw bytes into the bv-exports/ OPFS area. Used by ffmpegInterop.js's exportToOpfs — not
 * called directly from .NET.
 * @param {string} exportId
 * @param {string} ext - e.g. ".mp4"
 * @param {Uint8Array} bytes
 * @returns {number} size in bytes written
 */
export async function opfsExportsWriteBytes(exportId, ext, bytes) {
    const dir  = await getExportsDir();
    const fh   = await dir.getFileHandle(`${exportId}${ext}`, { create: true });
    const wr   = await fh.createWritable();
    const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    await wr.write(data);
    await wr.close();
    return data.byteLength;
}

/**
 * Read an export file from OPFS as a blob: URL. Caller is responsible for revoking it
 * (opfsRevokeBlobUrl, shared with the bv-clips/ path above) — though the download path
 * (ffmpegInterop.js's downloadBlobUrl) now owns that itself, deferred; see its own note.
 *
 * Item #59-#65 flakiness investigation, phase 144 (symptom S5) — this used to be
 * `URL.createObjectURL(file)` straight off the FileSystemFileHandle's own File snapshot, which
 * is still tied to OPFS storage under the hood: deleting the backing file right after (exactly
 * what ExportService's download path does, immediately after triggering the download) 404'd the
 * URL even without ever revoking it — live-found as the root cause of the "solo, symptomless
 * blob 404" symptom. Reading the bytes into memory and wrapping them in a fresh Blob makes the
 * URL fully independent of OPFS storage, so an immediate delete right after is safe.
 * @param {string} exportId
 * @param {string} ext
 * @returns {Promise<string>}
 */
export async function opfsExportsReadAsBlobUrl(exportId, ext) {
    const dir   = await getExportsDir();
    const fh    = await dir.getFileHandle(`${exportId}${ext}`);
    const file  = await fh.getFile();
    const bytes = new Uint8Array(await file.arrayBuffer());
    return URL.createObjectURL(new Blob([bytes], { type: file.type || 'application/octet-stream' }));
}

/**
 * Delete an export file from OPFS. Silent no-op if it does not exist.
 * @param {string} exportId
 * @param {string} ext
 */
export async function opfsExportsDelete(exportId, ext) {
    try {
        const dir = await getExportsDir();
        await dir.removeEntry(`${exportId}${ext}`);
    } catch { /* already gone */ }
}
