/**
 * InvestigationsMap.razor.js
 * ──────────────────────────
 * Colocated ES module for the InvestigationsMap Blazor component.
 *
 * Kept separate from PublicCaseDiscovery.razor.js rather than generalised into it: that module
 * hangs its template functions off `window` under fixed names, because Telerik resolves template
 * functions by name string and gives no other hook. Two components sharing those names would
 * quietly overwrite each other's markers the moment both appeared on one page.
 *
 * Every lookup here is scoped to the component's own container id for the same reason. The
 * original module reaches for `document.querySelector('[data-role="map"]')`, which finds whichever
 * map happens to be first in the DOM — fine while only one existed, wrong as soon as a second does.
 */

const _instances = new Map()

// ── Tile URL ─────────────────────────────────────────────────────────────────

function investigationMapTileTemplate(ctx) {
    return `https://${ctx.subdomain}.tile.openstreetmap.org/${ctx.zoom}/${ctx.x}/${ctx.y}.png`
}

// ── Marker template ──────────────────────────────────────────────────────────

function investigationMapMarkerTemplate(ctx) {
    const count = ctx.Count || 1
    const key = ctx.ContainerId
    const idx = ctx.Index

    if (count > 1) {
        return `<span
            class="investigation-map-cluster"
            onclick="investigationMapMarkerClick('${key}', ${idx})"
            title="${count} investigations here"
            style="
                display:inline-flex;align-items:center;justify-content:center;
                width:2rem;height:2rem;border-radius:50%;
                background:var(--kendo-color-primary,#ff6358);color:#fff;
                font-size:.85rem;font-weight:700;cursor:pointer;
                box-shadow:0 2px 6px rgba(0,0,0,.45);
            ">${count}</span>`
    }

    // Past visits are dimmed rather than hidden or recoloured to something that reads as an alert:
    // a completed investigation is ordinary, it is just no longer upcoming.
    const dim = ctx.IsPast ? 'opacity:.55;' : ''
    return `<span
        class="investigation-map-single"
        onclick="investigationMapMarkerClick('${key}', ${idx})"
        title="${(ctx.Title || 'View investigation').replace(/"/g, '&quot;')}"
        style="font-size:1.5rem;cursor:pointer;${dim}
               filter:drop-shadow(0 2px 4px rgba(0,0,0,.5));">🔦</span>`
}

// ── Marker click → Blazor ────────────────────────────────────────────────────

function investigationMapMarkerClick(containerId, index) {
    const ref = _instances.get(containerId)
    if (ref) ref.invokeMethodAsync('OnMarkerClicked', index)
}

// ── Widget lookup, scoped to one component ───────────────────────────────────

/**
 * Asks the component to re-measure its map.
 *
 * The map caches its container width when it mounts and never re-measures on its own, so one
 * mounted inside a not-yet-laid-out container draws tiles at the wrong width forever. This used
 * to try to fix that by reaching for kendo.widgetInstance() — but Telerik UI for Blazor defines
 * no global `kendo`, so the guard was false every time and nothing happened. The redraw belongs
 * to the component (TelerikMap.Refresh()); this only carries the event.
 */
export function resizeMap(containerId) {
    _instances.get(containerId)?.invokeMethodAsync('OnContainerResized')
}

export function setMapCenter(containerId, lat, lon, zoom) {
    const map = widgetFor(containerId)
    if (!map) return
    map.center([lat, lon])
    map.zoom(zoom)
}

// ── Init / dispose ───────────────────────────────────────────────────────────

const _resizeHandlers = new Map()

export function init(containerId, dotnetRef) {
    _instances.set(containerId, dotnetRef)

    // Global by necessity — Telerik calls template functions by name string. They are stateless
    // and keyed by container id, so repeated registration from several instances is harmless.
    window.investigationMapTileTemplate = investigationMapTileTemplate
    window.investigationMapMarkerTemplate = investigationMapMarkerTemplate
    window.investigationMapMarkerClick = investigationMapMarkerClick

    let timeout
    const handler = () => {
        clearTimeout(timeout)
        timeout = setTimeout(() => resizeMap(containerId), 150)
    }
    _resizeHandlers.set(containerId, handler)
    window.addEventListener('resize', handler)
}

export function dispose(containerId) {
    _instances.delete(containerId)

    const handler = _resizeHandlers.get(containerId)
    if (handler) {
        window.removeEventListener('resize', handler)
        _resizeHandlers.delete(containerId)
    }

    // The shared template functions stay put while any instance is still alive. Deleting them on
    // the first teardown would break every other map on the page.
    if (_instances.size === 0) {
        delete window.investigationMapTileTemplate
        delete window.investigationMapMarkerTemplate
        delete window.investigationMapMarkerClick
    }
}
