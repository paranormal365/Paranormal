/**
 * googleFontsInterop.js
 *
 * Loads a Google Font web font on demand: injects its CSS2 stylesheet <link> (idempotent — safe to
 * call repeatedly for the same family) then waits for the browser's Font Loading API to confirm
 * it's actually usable, so both the live preview and the SVG-rasterization export pipeline
 * (createImageBitmap/canvas in svgFrameRenderer.js) see real glyphs instead of a fallback font.
 *
 * Called from GoogleFontService.cs via IJSRuntime.
 * Served at: /_content/Ben.Video.Editor/js/googleFontsInterop.js
 */

const loadedFamilies = new Set();

/**
 * @param {string} family     Google Fonts family name, e.g. "Open Sans".
 * @param {number} timeoutMs  Bounded wait for the font to become ready (default 3000ms) — a
 *                             slow/offline network degrades to the system fallback font instead of
 *                             hanging the caller.
 * @returns {Promise<boolean>} true once the font is confirmed loaded (or already was), false if the
 *                              timeout elapsed first (or the family failed to load at all).
 */
export async function ensureFontLoaded(family, timeoutMs = 3000) {
    if (loadedFamilies.has(family)) return true;

    if (!document.querySelector(`link[data-google-font="${family}"]`)) {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = 'https://fonts.googleapis.com/css2?family=' +
            encodeURIComponent(family).replace(/%20/g, '+') + ':wght@400;700&display=swap';
        link.dataset.googleFont = family;
        document.head.appendChild(link);
    }

    try {
        await Promise.race([
            Promise.all([
                document.fonts.load(`400 16px "${family}"`),
                document.fonts.load(`700 16px "${family}"`),
            ]),
            new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), timeoutMs)),
        ]);
        loadedFamilies.add(family);
        return true;
    } catch {
        // Slow/offline network, or fonts.googleapis.com unreachable — fail silent (same "no error,
        // just degrade" resilience as WatermarkService.EnsureLocalAsync). The caller's text still
        // renders, just in the browser's default fallback font instead of the requested one.
        return false;
    }
}
