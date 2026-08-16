/**
 * rasterClipArtRenderer.js
 *
 * Renders a single raster clipart asset (PNG/AVIF/WebP/GIF) into a sequence of
 * full-canvas-size PNG frames, each with the asset composited at a different
 * position/size/opacity — used to export an animated (motion-keyframed) raster
 * clipart layer without needing per-frame ffmpeg overlay expressions.
 *
 * Called from RasterClipArtAnimationExporter.cs via IJSRuntime.
 * Runs on the main thread (OffscreenCanvas + createImageBitmap require browser APIs).
 *
 * Served at: /_content/Ben.Video.Editor/js/rasterClipArtRenderer.js
 */

/**
 * @param {Blob|File} sourceBlob    The raster asset's bytes — a JS File/Blob reference
 *                                  passed directly from .NET (an IJSObjectReference arrives
 *                                  here as the real object), no byte-array round-trip needed.
 * @param {number}    canvasWidth   Full output frame width in pixels.
 * @param {number}    canvasHeight  Full output frame height in pixels.
 * @param {Object[]}  frames        Array of {x, y, w, h, alpha, rotation, tintCss, tintAlpha}.
 *                                  x/y/w/h in pixel space, alpha 0–1, rotation in degrees
 *                                  (clockwise, around the sprite's own center), tintCss an
 *                                  "rgb(r,g,b)" string or null/undefined for no tint, tintAlpha
 *                                  the tint's blend strength 0–1 (meaningless when tintCss unset).
 * @returns {Promise<Uint8Array[]>} One PNG Uint8Array per frame, each the full canvas size
 *                                  with everything outside the sprite left transparent.
 */
export async function renderBatch(sourceBlob, canvasWidth, canvasHeight, frames) {
    const bitmap = await decodeImageBlob(sourceBlob);
    const canvas = new OffscreenCanvas(canvasWidth, canvasHeight);
    const ctx    = canvas.getContext('2d');

    const results = [];
    for (const f of frames) {
        ctx.clearRect(0, 0, canvasWidth, canvasHeight);
        ctx.save();
        if (f.rotation) {
            const cx = f.x + f.w / 2;
            const cy = f.y + f.h / 2;
            ctx.translate(cx, cy);
            ctx.rotate(f.rotation * Math.PI / 180);
            ctx.translate(-cx, -cy);
        }
        ctx.globalAlpha = f.alpha;
        ctx.drawImage(bitmap, f.x, f.y, f.w, f.h);
        ctx.globalAlpha = 1;

        // Recolor tint: paint the tint color only over the sprite's own already-drawn silhouette
        // ('source-atop' composites the new fill only where the destination already has alpha),
        // at the tint's own alpha as blend strength — same technique as the ffmpeg export path's
        // colorchannelmixer (ExportArgBuilders.BuildClipArtTintMixer), so both paths agree visually.
        if (f.tintCss && f.tintAlpha > 0) {
            ctx.globalCompositeOperation = 'source-atop';
            ctx.globalAlpha = f.tintAlpha;
            ctx.fillStyle = f.tintCss;
            ctx.fillRect(f.x, f.y, f.w, f.h);
            ctx.globalCompositeOperation = 'source-over';
            ctx.globalAlpha = 1;
        }

        ctx.restore();
        const pngBlob = await canvas.convertToBlob({ type: 'image/png' });
        const buf     = await pngBlob.arrayBuffer();
        results.push(new Uint8Array(buf));
    }

    if (bitmap.close) bitmap.close();
    return results;
}

/**
 * Decode an image Blob to a drawable bitmap, preferring createImageBitmap (covers
 * PNG/JPEG/WebP/GIF/AVIF — whatever the browser's decoder supports) with one retry for
 * transient flakiness, falling back to an Image()+objectURL decode if it fails outright.
 * Mirrors svgFrameRenderer.js's decodeSvgBlob — see its comments for why both paths proved
 * necessary in this environment (some Chromium builds fail createImageBitmap on every call
 * for certain sources, confirmed live in an earlier phase).
 */
async function decodeImageBlob(blob) {
    try {
        return await createImageBitmap(blob);
    } catch (err) {
        try {
            return await createImageBitmap(blob);
        } catch (err2) {
            return await decodeViaImageElement(blob);
        }
    }
}

function decodeViaImageElement(blob) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(blob);
        const img = new Image();
        img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
        img.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error('Raster clipart decode failed via both createImageBitmap and Image() fallback'));
        };
        img.src = url;
    });
}
