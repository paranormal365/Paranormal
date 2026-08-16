/**
 * renderWorkerInterop.js
 *
 * A second, fully independent ffmpeg.wasm instance — item #36 phase C — used exclusively by
 * RenderWorkerService for background preview-region rendering, so it never contends with or
 * blocks the main FfmpegService instance (ffmpegInterop.js) that Export/Preview/thumbnails use.
 *
 * Deliberately NOT a refactor of ffmpegInterop.js into a shared multi-instance factory: that file
 * has ~15 exported functions used throughout the app (export pipeline, thumbnails, transitions,
 * drawtext, audio mixing, project download...); converting all of them to be instance-scoped to
 * touch/re-verify every one of those existing, working call sites for a feature that only needs
 * a handful of primitives (load, exec, basic file I/O, terminate). This module is a small,
 * independent, purpose-built subset instead — some duplication with ffmpegInterop.js's lifecycle
 * code, but zero risk to anything already shipping.
 *
 * Same CDN-loading / blob-URL rationale as ffmpegInterop.js — see that file's header comment.
 */

const { FFmpeg } = FFmpegWASM;

const CORE_BASE = 'https://cdn.jsdelivr.net/npm/@ffmpeg/core@0.12.10/dist/umd';

let _ffmpeg = null;
let _dotnetRef = null;

async function toBlobURL(url, mimeType) {
    const resp = await fetch(url);
    if (!resp.ok) throw new Error(`[render-worker] ${url}: HTTP ${resp.status}`);
    const buf = await resp.arrayBuffer();
    return URL.createObjectURL(new Blob([buf], { type: mimeType }));
}

/** Load the render worker's own ffmpeg.wasm core. Always single-thread — background rendering
 * doesn't need the multi-thread core's SharedArrayBuffer/crossOriginIsolated requirements, and
 * running single-thread keeps this worker's footprint predictable regardless of host COOP/COEP. */
export async function loadCore(dotnetRef) {
    if (_ffmpeg && _ffmpeg.loaded) return;
    _dotnetRef = dotnetRef;

    _ffmpeg = new FFmpeg();
    _ffmpeg.on('log', ({ message }) => console.log('[render-worker-cmd]', message));
    _ffmpeg.on('progress', ({ progress }) => {
        const raw = progress * 100;
        const pct = isFinite(raw) ? Math.max(0, Math.min(100, Math.round(raw))) : 0;
        _dotnetRef?.invokeMethodAsync('OnRenderWorkerProgress', pct);
    });

    await _ffmpeg.load({
        coreURL: await toBlobURL(`${CORE_BASE}/ffmpeg-core.js`, 'text/javascript'),
        wasmURL: await toBlobURL(`${CORE_BASE}/ffmpeg-core.wasm`, 'application/wasm'),
    });
}

export function terminate() {
    if (_ffmpeg) {
        _ffmpeg.terminate();
        _ffmpeg = null;
    }
    _dotnetRef = null;
}

export async function exec(args) {
    console.log('[render-worker-exec]', args.join(' '));
    return await _ffmpeg.exec(args);
}

export async function writeFileFromBytes(name, bytes) {
    await _ffmpeg.writeFile(name, bytes);
}

export async function readFile(name) {
    return await _ffmpeg.readFile(name);
}

export async function deleteFile(name) {
    try { await _ffmpeg.deleteFile(name); } catch { /* already gone */ }
}

/** Zero-copy mount of a browser File — used to give the render worker access to OPFS source
 * clips without duplicating their bytes into this instance's own MEMFS. Mirrors
 * ffmpegInterop.js's mountWorkerFs exactly (see that file for the WORKERFS rationale). */
export async function mountWorkerFs(file, mountDir) {
    await _ffmpeg.createDir(mountDir);
    await _ffmpeg.mount('WORKERFS', { files: [file] }, mountDir);
    return `${mountDir}/${file.name}`;
}

export async function unmountWorkerFs(mountDir) {
    try { await _ffmpeg.unmount(mountDir); } catch { }
    try { await _ffmpeg.deleteDir(mountDir); } catch { }
}

/** Stream-copy concat — no re-encode. Only safe when every input segment shares the same
 * codec/dimensions/fps and audio-stream layout; RenderWorkerBackend's encode args pin exactly
 * that (including always emitting a — possibly silent — audio stream) so this is always valid
 * for background-rendered segments specifically. See BuildBackgroundRenderArgs. */
export async function concatCopy(segmentNames, outputName) {
    const listContent = segmentNames.map(n => `file '${n}'`).join('\n');
    const listName = '_render_worker_concat_list.txt';
    await _ffmpeg.writeFile(listName, listContent);
    const args = ['-f', 'concat', '-safe', '0', '-i', listName, '-c', 'copy', outputName];
    console.log('[render-worker-exec]', args.join(' '));
    const code = await _ffmpeg.exec(args);
    await deleteFile(listName);
    return code;
}
