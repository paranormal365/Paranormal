/**
 * svgFrameRenderer.js
 *
 * Renders SVG assets to PNG frames in the browser, applying per-frame
 * control-point patches (opacity, colour, transform) before rasterising.
 *
 * Called from SvgFrameRendererService.cs via IJSRuntime.
 * Runs on the main thread (DOMParser + OffscreenCanvas require browser APIs).
 *
 * Served at: /_content/Ben.Video.Editor/js/svgFrameRenderer.js
 */

/**
 * Render a single SVG frame with patches applied.
 *
 * @param {string}   svgSource  UTF-8 SVG markup string.
 * @param {Object[]} patches    Array of {targetSelector, type, value, x, y, color}.
 * @param {number}   width      Output PNG width in pixels.
 * @param {number}   height     Output PNG height in pixels.
 * @returns {Promise<Uint8Array>} PNG bytes.
 */
export async function renderFrame(svgSource, patches, width, height) {
    const patchedSvg = applyPatches(svgSource, patches);
    return svgToPng(patchedSvg, width, height);
}

/**
 * Render a sequence of frames in one call, returning an array of PNG byte arrays.
 * More efficient than calling renderFrame N times from C# because it reuses
 * the parsed SVG document and avoids N round-trips through IJSRuntime.
 *
 * @param {string}     svgSource   UTF-8 SVG markup.
 * @param {Object[][]} framesData  Array (one per frame) of patch arrays.
 * @param {number}     width       PNG width.
 * @param {number}     height      PNG height.
 * @returns {Promise<Uint8Array[]>} One PNG Uint8Array per frame.
 */
export async function renderBatch(svgSource, framesData, width, height) {
    const results = [];
    for (const patches of framesData) {
        const png = await renderFrame(svgSource, patches, width, height);
        results.push(png);
    }
    return results;
}

// ── Internal helpers ──────────────────────────────────────────────────────────

/**
 * Apply patches to the SVG source string and return the modified SVG string.
 * Uses DOMParser / XMLSerializer so the SVG namespace is preserved correctly.
 */
function applyPatches(svgSource, patches) {
    if (!patches || patches.length === 0) return svgSource;

    const parser = new DOMParser();
    const doc    = parser.parseFromString(svgSource, 'image/svg+xml');
    const errors = doc.querySelector('parsererror');
    if (errors) {
        console.warn('[svgFrameRenderer] SVG parse error:', errors.textContent);
        return svgSource;
    }

    for (const patch of patches) {
        const selector = patch.targetSelector || '*';
        // querySelectorAll on SVG documents works via namespace-agnostic CSS
        const elements = Array.from(
            selector === '*'
                ? [doc.documentElement]   // whole SVG root
                : doc.querySelectorAll(selector)
        );

        for (const el of elements) {
            patchElement(el, patch);
        }
    }

    return new XMLSerializer().serializeToString(doc);
}

/** Apply one patch operation to a single SVG element. */
function patchElement(el, patch) {
    const type = patch.type;

    switch (type) {
        case 'StrokeAlpha':
            el.style.strokeOpacity = String(patch.value);
            break;

        case 'FillAlpha':
            el.style.fillOpacity = String(patch.value);
            break;

        case 'FullAlpha':
            el.style.opacity = String(patch.value);
            break;

        case 'StrokeColor':
            if (patch.color) el.style.stroke = patch.color;
            break;

        case 'FillColor':
            if (patch.color) el.style.fill = patch.color;
            break;

        case 'StrokeWidth':
            el.style.strokeWidth = `${patch.value}px`;
            break;

        case 'Move': {
            const existing = el.getAttribute('transform') || '';
            el.setAttribute('transform',
                `${existing} translate(${patch.x ?? 0}, ${patch.y ?? 0})`.trim());
            break;
        }

        case 'Scale': {
            const existing = el.getAttribute('transform') || '';
            el.setAttribute('transform',
                `${existing} scale(${patch.value})`.trim());
            break;
        }

        case 'ScaleX': {
            const existing = el.getAttribute('transform') || '';
            el.setAttribute('transform',
                `${existing} scale(${patch.value}, 1)`.trim());
            break;
        }

        case 'ScaleY': {
            const existing = el.getAttribute('transform') || '';
            el.setAttribute('transform',
                `${existing} scale(1, ${patch.value})`.trim());
            break;
        }

        case 'Rotate': {
            const existing = el.getAttribute('transform') || '';
            el.setAttribute('transform',
                `${existing} rotate(${patch.value})`.trim());
            break;
        }

        default:
            console.warn('[svgFrameRenderer] Unknown patch type:', type);
    }
}

/**
 * Rasterise an SVG string to a PNG Uint8Array via OffscreenCanvas.
 *
 * Decodes the SVG Blob directly via createImageBitmap — no ObjectURL/fetch
 * round-trip. The previous implementation went Blob -> createObjectURL ->
 * fetch(url) -> blob() -> createImageBitmap, which is unnecessary (
 * createImageBitmap accepts a Blob directly) and was an intermittent source
 * of "InvalidStateError: The source image could not be decoded".
 *
 * That error turned out NOT to be transient in every environment: some
 * Chromium builds fail createImageBitmap(svgBlob) on every call, including
 * for a plain <rect> with no filters — confirmed by testing the exact same
 * SVG blob against an <img> element in the same session, which decoded fine.
 * So a same-API retry doesn't help there; decodeSvgBlob() below tries
 * createImageBitmap first (fastest path, works in mainline Chrome/Firefox)
 * and falls back to the Image()+canvas route — which has broader decoder
 * support — when every createImageBitmap attempt fails.
 *
 * @param {string}  svgSource  SVG markup (may have been patched).
 * @param {number}  width
 * @param {number}  height
 * @returns {Promise<Uint8Array>}
 */
async function svgToPng(svgSource, width, height) {
    const blob   = new Blob([svgSource], { type: 'image/svg+xml' });
    const canvas = new OffscreenCanvas(width, height);
    const ctx    = canvas.getContext('2d');
    ctx.clearRect(0, 0, width, height);

    const bitmap = await decodeSvgBlob(blob, width, height);
    if (bitmap instanceof ImageBitmap) {
        ctx.drawImage(bitmap, 0, 0, width, height);
        bitmap.close();
    } else {
        // HTMLImageElement fallback — drawImage accepts it directly.
        ctx.drawImage(bitmap, 0, 0, width, height);
    }

    const pngBlob = await canvas.convertToBlob({ type: 'image/png' });
    const buf     = await pngBlob.arrayBuffer();
    return new Uint8Array(buf);
}

/**
 * Decode an SVG Blob to a drawable image, preferring createImageBitmap (one
 * retry for its known transient flakiness) and falling back to an
 * Image()+objectURL decode when createImageBitmap fails outright in this
 * browser/environment. Returns either an ImageBitmap or an HTMLImageElement —
 * both are valid drawImage() sources.
 */
async function decodeSvgBlob(blob, width, height) {
    try {
        return await createImageBitmap(blob);
    } catch (err) {
        try {
            return await createImageBitmap(blob); // one retry for transient flakiness
        } catch (err2) {
            return await decodeSvgViaImageElement(blob, width, height);
        }
    }
}

function decodeSvgViaImageElement(blob, width, height) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(blob);
        const img = new Image();
        img.onload = () => { URL.revokeObjectURL(url); resolve(img); };
        img.onerror = () => {
            URL.revokeObjectURL(url);
            reject(new Error('SVG decode failed via both createImageBitmap and Image() fallback'));
        };
        // Explicit dimensions — some engines need them for an SVG with a
        // percentage or missing intrinsic size to rasterise via <img>.
        img.width = width;
        img.height = height;
        img.src = url;
    });
}
