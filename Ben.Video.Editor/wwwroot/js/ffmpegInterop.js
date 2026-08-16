/**
 * ffmpegInterop.js
 *
 * Central JS module for all ffmpeg.wasm interactions in Ben.Video.Editor.
 * Served at: /_content/Ben.Video.Editor/js/ffmpegInterop.js
 *
 * Requires the following UMD script to be loaded in the host index.html BEFORE Blazor:
 *   <script src="https://unpkg.com/@ffmpeg/ffmpeg@0.12.15/dist/umd/ffmpeg.js"></script>
 *
 * CDN bases:
 *   @ffmpeg/core (ST)  — https://cdn.jsdelivr.net/npm/@ffmpeg/core@0.12.10/dist/umd/
 *   @ffmpeg/core-mt    — https://cdn.jsdelivr.net/npm/@ffmpeg/core-mt@0.12.10/dist/umd/
 *
 * Why blob: URLs?
 *   Web Workers can only be created from same-origin URLs. The CDN files are
 *   cross-origin, so we fetch them (browser caches the HTTP response), wrap
 *   them in blob: URLs (same-origin), and pass those to ffmpeg.load().
 *   WASM compilation takes ~5-30 s on first load; subsequent loads use the
 *   HTTP-cached files but still recompile (blob: URLs bypass the WASM JIT cache).
 */

import { opfsExportsWriteBytes } from './opfsInterop.js';

const { FFmpeg } = FFmpegWASM;

/**
 * Download a cross-origin file, wrap it in a same-origin blob: URL, and
 * return that URL. The blob: URL allows Web Worker creation from CDN content
 * without violating the browser's same-origin restriction on workers.
 * Progress is logged to the console at every 10 %.
 * @param {string} url
 * @param {string} mimeType
 * @param {string} label
 * @returns {Promise<string>}
 */
async function toBlobURL(url, mimeType, label) {
    console.log(`[ffmpeg] ↓ ${label}…`);
    const resp = await fetch(url);
    if (!resp.ok) throw new Error(`[ffmpeg] ${label}: HTTP ${resp.status}`);

    const total = parseInt(resp.headers.get('Content-Length') || '0');
    if (total > 0 && resp.body) {
        const reader = resp.body.getReader();
        const chunks = [];
        let received = 0, lastPct = -10;
        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            chunks.push(value);
            received += value.length;
            // Content-Length is the compressed transfer size; `received` counts
            // decompressed bytes, so pct can exceed 100 when the response is gzipped.
            const pct = Math.min(100, Math.round(received / total * 100));
            if (pct >= lastPct + 10) { lastPct = pct; console.log(`[ffmpeg] ${label} ${pct}%`); }
        }
        const buf = new Uint8Array(received);
        let off = 0; for (const c of chunks) { buf.set(c, off); off += c.length; }
        console.log(`[ffmpeg] ${label} done ✓`);
        return URL.createObjectURL(new Blob([buf], { type: mimeType }));
    }

    const buf = await resp.arrayBuffer();
    console.log(`[ffmpeg] ${label} done ✓ (${(buf.byteLength / 1048576).toFixed(1)} MB)`);
    return URL.createObjectURL(new Blob([buf], { type: mimeType }));
}

/**
 * Read a File, Blob, or URL and return its content as a Uint8Array.
 * @param {File|Blob|string|URL} source
 * @returns {Promise<Uint8Array>}
 */
async function fetchFile(source) {
    if (source instanceof File || source instanceof Blob)
        return new Uint8Array(await source.arrayBuffer());
    const resp = await fetch(source);
    return new Uint8Array(await resp.arrayBuffer());
}

const CORE_BASE    = 'https://cdn.jsdelivr.net/npm/@ffmpeg/core@0.12.10/dist/umd';
const CORE_MT_BASE = 'https://cdn.jsdelivr.net/npm/@ffmpeg/core-mt@0.12.10/dist/umd';

// Singleton FFmpeg instance
let _ffmpeg = null;
// .NET object reference for progress/log callbacks
let _dotnetRef = null;

// ─── Lifecycle ───────────────────────────────────────────────────────────────

/**
 * Load the ffmpeg.wasm core. Call once before any other method.
 * @param {object} dotnetRef - DotNetObjectReference from C# for callbacks
 * @param {boolean} multiThread - true when crossOriginIsolated is confirmed
 */
export async function loadCore(dotnetRef, multiThread) {
    // Item #59-#65 flakiness investigation, phase 141 — this log used to print unconditionally
    // BEFORE the idempotence guard below, so "3-4 loadCore() lines in one session" looked like
    // 3-4 real core loads when it was usually 1 real load + several no-ops (an Initialize click
    // after any Error, for instance, always reaches this function). Logging only on the branch
    // actually taken makes the two cases distinguishable at a glance.
    if (_ffmpeg && _ffmpeg.loaded) {
        console.log('[ffmpeg] loadCore() skipped — already loaded (multiThread:', multiThread, ')');
        return;
    }
    console.log('[ffmpeg] loadCore() — multiThread:', multiThread, '| crossOriginIsolated:', self.crossOriginIsolated);

    _dotnetRef = dotnetRef;

    const notify = (label) => dotnetRef?.invokeMethodAsync('OnFfmpegDownload', label, -1);

    const loadFrom = async (base, mode) => {
        notify(`Downloading ${mode} core…`);
        _ffmpeg = new FFmpeg();
        // Note: this single-threaded core prints a benign "Aborted()" line as part of each
        // command's exit path — it is NOT a crash signal. Real failures are detected via the
        // exit code exec() returns (checked in FfmpegService), never by pattern-matching logs.
        _ffmpeg.on('log',      ({ message })        => {
            console.log('[ffmpeg-cmd]', message);
            _dotnetRef?.invokeMethodAsync('OnFfmpegLog', message);
        });
        _ffmpeg.on('progress', ({ progress, time }) => {
            // ffmpeg.wasm can emit NaN, Infinity, or large-negative values for short clips.
            // Math.round(NaN) → NaN → JSON null → System.Int32 deserialization throws.
            const raw = progress * 100;
            const pct = isFinite(raw) ? Math.max(0, Math.min(100, Math.round(raw))) : 0;
            _dotnetRef?.invokeMethodAsync('OnFfmpegProgress', pct, time ?? 0);
        });

        const config = {
            coreURL: await toBlobURL(`${base}/ffmpeg-core.js`,   'text/javascript', `core JS (${mode})`),
            wasmURL: await toBlobURL(`${base}/ffmpeg-core.wasm`, 'application/wasm', `core WASM (${mode})`),
        };
        if (mode === 'multi-thread')
            config.workerURL = await toBlobURL(`${base}/ffmpeg-core.worker.js`, 'text/javascript', 'worker');

        notify('Compiling WASM…');
        console.log('[ffmpeg] Calling ffmpeg.load()…');
        await _ffmpeg.load(config);
        console.log('[ffmpeg] ffmpeg.load() complete ✓');
    };

    const base = multiThread ? CORE_MT_BASE : CORE_BASE;
    const mode = multiThread ? 'multi-thread' : 'single-thread';

    try {
        await loadFrom(base, mode);
    } catch (err) {
        if (multiThread) {
            console.warn('[ffmpeg] Multi-thread failed, retrying single-thread:', err);
            await loadFrom(CORE_BASE, 'single-thread');
        } else {
            throw err;
        }
    }
}

/**
 * Terminate the worker and release all resources.
 */
export function terminate() {
    if (_ffmpeg) {
        _ffmpeg.terminate();
        _ffmpeg = null;
    }
    _dotnetRef = null;
}

/**
 * Returns true if SharedArrayBuffer is available AND the page is genuinely
 * cross-origin isolated (COOP + COEP headers confirmed by the browser).
 * Using SharedArrayBuffer without crossOriginIsolated=true causes the
 * multi-thread worker to silently hang on Chrome localhost.
 */
export function isMultiThreadSupported() {
    return typeof SharedArrayBuffer !== 'undefined' && self.crossOriginIsolated === true;
}

// ─── File I/O ────────────────────────────────────────────────────────────────

/**
 * Write a File or Blob from the browser into MEMFS.
 * For files >200MB use mountWorkerFs instead.
 * @param {string} name - MEMFS filename
 * @param {File|Blob} file
 */
export async function writeFile(name, file) {
    const data = await fetchFile(file);
    await _ffmpeg.writeFile(name, data);
}

/**
 * Write a file into MEMFS from raw bytes (Uint8Array).
 * Used when a file is downloaded via HTTP (e.g. from the media library API)
 * rather than picked from the local filesystem.
 * @param {string} name - MEMFS filename
 * @param {Uint8Array} bytes
 */
export async function writeFileFromBytes(name, bytes) {
    await _ffmpeg.writeFile(name, bytes);
}

/**
 * Read a file from MEMFS and return it as a Uint8Array.
 * @param {string} name - MEMFS filename
 * @returns {Uint8Array}
 */
export async function readFile(name) {
    return await _ffmpeg.readFile(name);
}

/**
 * Delete a file from MEMFS to reclaim space.
 * @param {string} name - MEMFS filename
 */
export async function deleteFile(name) {
    try { await _ffmpeg.deleteFile(name); } catch { /* already gone */ }
}

/**
 * Rename a MEMFS file in place (item #38 phase D) — a genuine filesystem rename, not a
 * read-into-memory/write-under-new-name/delete-old round trip. ffmpeg.wasm's FFmpeg class already
 * exposes this (confirmed against the published UMD bundle); it was simply never called from here.
 * @param {string} from
 * @param {string} to
 */
export async function rename(from, to) {
    await _ffmpeg.rename(from, to);
}

/**
 * Mount a large File using WORKERFS (zero-copy) and return the MEMFS path.
 * @param {File} file
 * @param {string} mountDir - e.g. '/input'
 * @returns {string} path to the file inside the mount, e.g. '/input/video.mp4'
 */
export async function mountWorkerFs(file, mountDir) {
    try {
        await _ffmpeg.createDir(mountDir);
    } catch (err) {
        // Phase 143: a stale directory from a mount that was never cleanly torn down — most
        // notably the pre-fix "fake recovery" path, where Initialize silently no-op'd against a
        // still-wedged worker whose old mounts were still sitting there. Clean up once and retry
        // rather than failing the whole mount over a leftover directory.
        try { await _ffmpeg.unmount(mountDir); } catch { }
        try { await _ffmpeg.deleteDir(mountDir); } catch { }
        await _ffmpeg.createDir(mountDir);
    }
    await _ffmpeg.mount('WORKERFS', { files: [file] }, mountDir);
    return `${mountDir}/${file.name}`;
}

/**
 * Unmount a WORKERFS directory and remove it.
 * @param {string} mountDir
 */
export async function unmountWorkerFs(mountDir) {
    try { await _ffmpeg.unmount(mountDir); } catch { }
    try { await _ffmpeg.deleteDir(mountDir); } catch { }
}

// ─── Exec ────────────────────────────────────────────────────────────────────

/**
 * Execute an FFmpeg command. One at a time — callers must queue.
 * @param {string[]} args - FFmpeg CLI arguments
 * @param {number} [timeoutMs] - forwarded straight to the library's own exec(args, timeout);
 *   omitted/undefined means the library default (-1, infinite). Phase 143: a core-level timeout
 *   abort resolves gracefully with a non-zero exit code (verified live — see
 *   README-phase-143.md), so this needs no special handling beyond the existing exit-code check.
 * @returns {number} exit code (0 = success)
 */
export async function exec(args, timeoutMs) {
    console.log('[ffmpeg-exec]', args.join(' '));
    return await _ffmpeg.exec(args, timeoutMs ?? -1);
}

// ─── Video Operations ────────────────────────────────────────────────────────

/**
 * Extract clip metadata (duration, width, height) via ffprobe.
 * ffprobe cannot write to stdout in WASM — output goes to a temp file.
 * @param {string} inputName - MEMFS filename of the input video
 * @returns {{ duration: number, width: number, height: number }}
 */
export async function getMetadata(inputName, timeoutMs) {
    // Item #38 phase B: outFile must NOT be derived from inputName — inputName can now be a
    // WORKERFS-mounted path like "/src_xxx/clip.mp4" (zero-copy source mount), and that directory
    // is read-only, so writing "<mountDir>/<name>_meta.txt" there throws an FS error. A name
    // rooted at MEMFS root, independent of inputName's shape, works for both mounted and flat
    // (MEMFS-copy) inputs alike.
    const outFile = `meta_${crypto.randomUUID()}.txt`;
    // Use ffprobe (not ffmpeg) — ffprobe cannot write to stdout in WASM,
    // so direct output to a temp file with -o and read it back.
    // Item #59-#65 flakiness investigation, phase 141 — this call goes through _ffmpeg.ffprobe
    // directly rather than the exported exec() above, so it previously never printed an
    // [ffmpeg-exec] line at all: a hang here (this is a real, synchronous worker command, same
    // as any exec) looked identical to silence, not to a command in flight.
    const probeArgs = ['-v', 'quiet', '-print_format', 'json', '-show_streams', '-i', inputName, '-o', outFile];
    console.log('[ffmpeg-exec]', 'ffprobe', probeArgs.join(' '));
    await _ffmpeg.ffprobe(probeArgs, timeoutMs ?? -1);
    const raw = await _ffmpeg.readFile(outFile);
    await deleteFile(outFile);
    const json = JSON.parse(new TextDecoder().decode(raw));
    const video = json.streams?.find(s => s.codec_type === 'video') ?? {};
    const audio = json.streams?.find(s => s.codec_type === 'audio') ?? {};
    // Audio-only files (mp3, wav, ...) have no video stream, so its duration
    // is always missing — fall back to the audio stream's duration so
    // audio-only imports don't silently get a 0-second clip.
    return {
        duration: parseFloat(video.duration ?? audio.duration ?? '0'),
        width: parseInt(video.width ?? '0', 10),
        height: parseInt(video.height ?? '0', 10),
    };
}

/**
 * Extract thumbnail frames from a video as WebP blob URLs.
 * @param {string} inputName - MEMFS filename
 * @param {number} count - number of thumbnails to extract
 * @param {number} duration - total duration in seconds
 * @returns {string[]} array of blob: URLs
 */
export async function extractThumbnails(inputName, count, duration, timeoutMs) {
    // Item #59-#65 flakiness investigation, phase 145 (symptom S3) — this used to be N separate
    // _ffmpeg.exec() calls, each opening/seeking/decoding the input from scratch (real, measured
    // overhead — this was the dominant cost of a server-library import). ffmpeg supports multiple
    // -ss/-frames:v/output groups after a single -i, reusing the same already-open input and
    // decoder across all of them — one exec, N output files, same per-frame timestamps as before
    // (t = interval * i), zero change to what the thumbnails actually show.
    const interval = duration / (count + 1);
    const base = crypto.randomUUID();
    const outNames = [];
    const args = ['-i', inputName];
    for (let i = 1; i <= count; i++) {
        const t = (interval * i).toFixed(2);
        const outName = `thumb_${base}_${i}.webp`;
        outNames.push(outName);
        args.push('-ss', t, '-frames:v', '1', '-vf', 'scale=160:-1', outName);
    }
    console.log('[ffmpeg-exec]', args.join(' '));
    await _ffmpeg.exec(args, timeoutMs ?? -1);

    const urls = [];
    for (const outName of outNames) {
        try {
            const data = await _ffmpeg.readFile(outName);
            await deleteFile(outName);
            urls.push(URL.createObjectURL(new Blob([data], { type: 'image/webp' })));
        } catch (err) {
            // A frame very close to end-of-stream can occasionally not get written — skip it
            // rather than failing the whole batch over one missing thumbnail.
            console.warn('[ffmpeg] thumbnail frame missing:', outName, err);
        }
    }
    return urls;
}

/**
 * Trim a clip using frame-accurate re-encode (not -c copy).
 * @param {string} inputName - MEMFS filename
 * @param {string} outputName - MEMFS filename for output
 * @param {number} startSec - trim start in seconds
 * @param {number} endSec - trim end in seconds
 */
export async function trimClip(inputName, outputName, startSec, endSec, timeoutMs) {
    await _ffmpeg.exec([
        '-ss', startSec.toFixed(3),
        '-to', endSec.toFixed(3),
        '-i', inputName,
        '-c:v', 'libx264',
        '-c:a', 'aac',
        '-avoid_negative_ts', 'make_zero',
        outputName
    ], timeoutMs ?? -1);
}

/**
 * Concatenate multiple trimmed clips into one output file.
 * Writes a concat list to MEMFS, runs the concat demuxer.
 * @param {string[]} segmentNames - ordered array of MEMFS filenames
 * @param {string} outputName - MEMFS filename for the final output
 * @param {number|null} [scaleWidth] - when set (with scaleHeight), scales+pads the concatenated
 *   output down to this size — used by the editor's own Preview render, never by export. Omitted
 *   (null/undefined) leaves the output at its clips' native size, matching prior behavior exactly.
 * @param {number|null} [scaleHeight]
 */
export async function concatClips(segmentNames, outputName, scaleWidth, scaleHeight, listName, timeoutMs) {
    // Phase 142: caller (FfmpegService.ConcatClipsAsync) now passes a per-invocation name — the
    // worker lock already makes concurrent concats structurally impossible, but a caller-supplied
    // GUID name removes the need for that guarantee to hold perfectly. Falls back to the old fixed
    // name if omitted, so this stays callable exactly as before if invoked directly.
    listName = listName || '_concat_list.txt';
    const listContent = segmentNames.map(n => `file '${n}'`).join('\n');
    await _ffmpeg.writeFile(listName, listContent);
    const args = ['-f', 'concat', '-safe', '0', '-i', listName, '-c:v', 'libx264', '-c:a', 'aac'];
    if (scaleWidth && scaleHeight) {
        args.push('-vf', `scale=${scaleWidth}:${scaleHeight}:force_original_aspect_ratio=decrease,pad=${scaleWidth}:${scaleHeight}:(ow-iw)/2:(oh-ih)/2`);
    }
    args.push(outputName);
    console.log('[ffmpeg-exec]', args.join(' '));
    try {
        return await _ffmpeg.exec(args, timeoutMs ?? -1);
    } finally {
        // try/finally, not the old unconditional post-exec await — a thrown exec no longer
        // leaks the list file.
        await deleteFile(listName);
    }
}

/**
 * Stream-copy concat (`-c copy`) — near-instant, NO re-encode. Only valid when every segment
 * shares identical codec/dimensions/fps and an identical audio-stream layout, which the
 * background render worker's pinned encode args guarantee (item #36 phases C/D). The re-encoding
 * concatClips above remains for mixed/legacy segment sets. Mirrors renderWorkerInterop.js's
 * concatCopy, but for the MAIN instance (where preview assembly runs).
 */
export async function concatCopy(segmentNames, outputName, listName, timeoutMs) {
    listName = listName || '_concat_copy_list.txt'; // see concatClips' own note
    const listContent = segmentNames.map(n => `file '${n}'`).join('\n');
    await _ffmpeg.writeFile(listName, listContent);
    const args = ['-f', 'concat', '-safe', '0', '-i', listName, '-c', 'copy', outputName];
    console.log('[ffmpeg-exec]', args.join(' '));
    try {
        return await _ffmpeg.exec(args, timeoutMs ?? -1);
    } finally {
        await deleteFile(listName);
    }
}

// Item #59-#65 flakiness investigation, phase 144 (symptom S5) — every download function below
// used to revoke its blob: URL synchronously, in the same tick as a.click(). That click only
// DISPATCHES the browser's own save-dialog/download-manager fetch of the blob; it doesn't wait
// for that fetch to actually read the bytes. Revoking immediately raced it — live-found as a
// solo, symptomless blob: ERR_FILE_NOT_FOUND with no other visible effect (the download usually
// still worked, since the race is narrow, but not always). Every download path now defers its
// revoke by this long instead.
const DOWNLOAD_REVOKE_DELAY_MS = 30_000;

/**
 * Read a processed output file from MEMFS and trigger a browser download.
 * @param {string} name - MEMFS filename
 * @param {string} downloadAs - filename shown in the browser save dialog
 * @param {string} mimeType - e.g. 'video/mp4'
 */
export async function downloadFile(name, downloadAs, mimeType) {
    const data = await _ffmpeg.readFile(name);
    const blob = new Blob([data], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = downloadAs;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), DOWNLOAD_REVOKE_DELAY_MS);
}

/**
 * Trigger a browser download from an already-created object URL (item #38 phase D) — used for the
 * OPFS-backed export path, where the URL comes from an OPFS-backed blob (see
 * opfsInterop.js's opfsExportsReadAsBlobUrl, made memory-backed in phase 144) rather than a fresh
 * MEMFS readFile + Blob. This function now owns revoking it (deferred, per the note above) — the
 * caller no longer needs to revoke it separately.
 * @param {string} url
 * @param {string} downloadAs
 */
export function downloadBlobUrl(url, downloadAs) {
    const a = document.createElement('a');
    a.href = url;
    a.download = downloadAs;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), DOWNLOAD_REVOKE_DELAY_MS);
}

/**
 * Move a finished export from MEMFS into the OPFS `bv-exports/` area, entirely JS-side — item #38
 * phase D. The byte array never crosses into .NET (unlike the old flow, where the finished output
 * stayed resident in MEMFS through the whole download/preview step). Returns the file's byte size
 * so the caller can report it without a second read.
 * @param {string} memFsName
 * @param {string} exportId - GUID string (no dashes), used as the OPFS filename base
 * @param {string} ext - e.g. ".mp4"
 * @returns {number} size in bytes
 */
export async function exportToOpfs(memFsName, exportId, ext) {
    const data = await _ffmpeg.readFile(memFsName);
    const size = await opfsExportsWriteBytes(exportId, ext, data);
    await deleteFile(memFsName);
    return size;
}

/**
 * Create an object URL for a MEMFS file for use in a <video> element.
 * @param {string} name - MEMFS filename
 * @param {string} mimeType - e.g. 'video/mp4'
 * @returns {string} blob: URL
 */
export async function createPreviewUrl(name, mimeType) {
    const data = await _ffmpeg.readFile(name);
    const blob = new Blob([data], { type: mimeType });
    return URL.createObjectURL(blob);
}

/**
 * Revoke a previously created object URL to free browser memory.
 * @param {string} url
 */
export function revokePreviewUrl(url) {
    URL.revokeObjectURL(url);
}

// ── Advanced filtergraph helpers ─────────────────────────────────────────────

/**
 * Execute an arbitrary ffmpeg command with a filter_complex graph.
 * This is a thin wrapper over exec() — the caller builds the full args array.
 *
 * @param {string[]} args  - Full ffmpeg argument list (no leading "ffmpeg")
 * @returns {number} ffmpeg exit code (0 = success)
 */
export async function execFilterComplex(args, timeoutMs) {
    return await exec(args, timeoutMs);
}

/**
 * Write a Uint8Array directly to MEMFS (no browser File object required).
 * Used by the export pipeline when re-routing data between pipeline stages.
 *
 * @param {string} name      - MEMFS destination filename
 * @param {Uint8Array} data  - Raw bytes to write
 */
export async function writeBytes(name, data) {
    await _ffmpeg.writeFile(name, data);
}

/**
 * Build and execute an xfade transition filter_complex command.
 * Stitches consecutive input segments with configurable crossfade transitions.
 *
 * @param {string[]} inputNames     - Ordered MEMFS segment filenames
 * @param {string}   outputName     - MEMFS output filename
 * @param {Array<{style: string, duration: number, offset: number}>} transitions
 *   - One entry per segment boundary (length = inputNames.length - 1)
 * @param {string[]} extraArgs      - Additional ffmpeg args appended before outputName
 *   (e.g. ["-c:v","libx264","-crf","23","-pix_fmt","yuv420p"])
 * @returns {number} exit code
 */
export async function applyXfadeTransitions(inputNames, outputName, transitions, extraArgs, timeoutMs) {
    const args = [];

    // Add all inputs
    for (const name of inputNames) {
        args.push('-i', name);
    }

    // Build filter_complex xfade chain
    let prev = '[0:v]';
    let filterParts = [];

    for (let i = 0; i < inputNames.length - 1; i++) {
        const t      = transitions[i] ?? { style: 'fade', duration: 1.0, offset: 0 };
        const outTag = i < inputNames.length - 2 ? `[x${String(i).padStart(2,'0')}]` : '[vout]';
        filterParts.push(
            `${prev}[${i + 1}:v]xfade=transition=${t.style}:duration=${t.duration.toFixed(2)}:offset=${t.offset.toFixed(2)}${outTag}`
        );
        prev = outTag;
    }

    if (filterParts.length === 0) {
        // Single segment — no transition needed, just copy
        filterParts.push('[0:v]copy[vout]');
    }

    args.push('-filter_complex', filterParts.join(';'));
    args.push('-map', '[vout]');

    // Include audio from first input (passthrough)
    args.push('-map', '0:a?');

    // Append caller-supplied quality/codec args
    if (extraArgs && extraArgs.length) {
        args.push(...extraArgs);
    }

    args.push(outputName);

    return await exec(args, timeoutMs);
}

/**
 * Apply a chain of drawtext filters to a video file.
 *
 * @param {string}   inputName    - Source MEMFS file
 * @param {string}   outputName   - Destination MEMFS file
 * @param {string[]} filterChain  - drawtext filter expressions (chained with ,)
 * @param {string[]} extraArgs    - Codec/quality args
 * @returns {number} exit code
 */
export async function applyDrawtext(inputName, outputName, filterChain, extraArgs, timeoutMs) {
    const chain  = filterChain.join(',');
    const filter = `[0:v]${chain}[vout]`;

    const args = [
        '-i', inputName,
        '-filter_complex', filter,
        '-map', '[vout]',
        '-map', '0:a?',
        '-c:a', 'copy',
        ...(extraArgs ?? []),
        outputName
    ];

    return await exec(args, timeoutMs);
}

/**
 * Mix multiple audio tracks (MEMFS files) into a video output using amix.
 *
 * @param {string}   videoInput     - Video-with-audio MEMFS file (input 0)
 * @param {string[]} audioInputs    - Additional audio-only MEMFS files
 * @param {string}   outputName     - MEMFS destination
 * @param {string}   audioCodec     - e.g. "aac"
 * @param {number}   audioBitrateK  - e.g. 192
 * @returns {number} exit code
 */
export async function mixAudio(videoInput, audioInputs, outputName, audioCodec, audioBitrateK, timeoutMs) {
    const args = ['-i', videoInput];
    for (const a of audioInputs) args.push('-i', a);

    const n      = audioInputs.length;
    const inputs = ['[0:a]', ...audioInputs.map((_, i) => `[${i + 1}:a]`)].join('');
    const filter = `${inputs}amix=inputs=${n + 1}:duration=longest[aout]`;

    args.push(
        '-filter_complex', filter,
        '-map', '0:v',
        '-map', '[aout]',
        '-c:v', 'copy',
        '-c:a', audioCodec,
        '-b:a', `${audioBitrateK}k`,
        outputName
    );

    return await exec(args, timeoutMs);
}

// ── Project file helpers ────────────────────────────────────────────────────

/**
 * Trigger a browser file download for arbitrary bytes (no MEMFS required).
 * Used by ProjectService to download the .benvideo JSON file.
 * @param {Uint8Array} bytes - file content
 * @param {string} downloadAs - suggested filename
 * @param {string} mimeType - e.g. 'application/json'
 */
export function downloadBytes(bytes, downloadAs, mimeType) {
    const blob = new Blob([bytes], { type: mimeType });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement('a');
    a.href     = url;
    a.download = downloadAs;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), DOWNLOAD_REVOKE_DELAY_MS); // see downloadFile's own note
}

/**
 * Read the content of an <input type="file"> element as a UTF-8 string.
 * @param {HTMLInputElement} inputElement
 * @returns {Promise<string>}
 */
export function readInputFileAsText(inputElement) {
    return new Promise((resolve, reject) => {
        const file = inputElement.files?.[0];
        if (!file) { reject(new Error('No file selected')); return; }
        const reader = new FileReader();
        reader.onload  = () => resolve(/** @type {string} */ (reader.result));
        reader.onerror = () => reject(reader.error);
        reader.readAsText(file, 'utf-8');
    });
}
