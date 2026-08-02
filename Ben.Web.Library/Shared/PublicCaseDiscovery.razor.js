/**
 * PublicCaseDiscovery.razor.js
 * ─────────────────────────────
 * Colocated ES module for the PublicCaseDiscovery Blazor component.
 *
 * Provides:
 *  - caseMapTileTemplate(ctx)     — OpenStreetMap tile URL
 *  - caseMapMarkerTemplate(ctx)   — Ghost icon (single case) or count badge (cluster)
 *  - caseMapMarkerClick(index)    — Routes marker clicks back to Blazor
 *  - tryGetUserLocation(dotnetRef) — Requests browser geolocation
 */

let _dotnetRef = null

// ── Tile URL ─────────────────────────────────────────────────────────────────

function caseMapTileTemplate(ctx) {
    return `https://${ctx.subdomain}.tile.openstreetmap.org/${ctx.zoom}/${ctx.x}/${ctx.y}.png`
}

// ── Marker template ───────────────────────────────────────────────────────────

function caseMapMarkerTemplate(ctx) {
    const count = ctx.Count || 1
    const idx   = ctx.Index

    if (count > 1) {
        return `<span
            class="case-map-cluster"
            onclick="caseMapMarkerClick(${idx})"
            title="${count} cases here"
            style="
                display:inline-flex;align-items:center;justify-content:center;
                width:2rem;height:2rem;border-radius:50%;
                background:var(--kendo-color-primary,#ff6358);color:#fff;
                font-size:.85rem;font-weight:700;cursor:pointer;
                box-shadow:0 2px 6px rgba(0,0,0,.45);
            ">${count}</span>`
    }

    // Single case — ghost emoji in a styled pin
    const isHaunted = ctx.IsHaunted
    return `<span
        class="case-map-single"
        onclick="caseMapMarkerClick(${idx})"
        title="${ctx.Title || 'View case'}"
        style="
            font-size:1.6rem;cursor:pointer;
            filter:drop-shadow(0 2px 4px rgba(0,0,0,.5));
            ${isHaunted ? 'filter:drop-shadow(0 0 6px gold) drop-shadow(0 2px 4px rgba(0,0,0,.5));' : ''}
        ">👻</span>`
}

// ── Marker click → Blazor ─────────────────────────────────────────────────────

function caseMapMarkerClick(index) {
    if (_dotnetRef) _dotnetRef.invokeMethodAsync('OnMarkerClicked', index)
}

// ── User geolocation ──────────────────────────────────────────────────────────

export function tryGetUserLocation(dotnetRef) {
    if (!navigator.geolocation) {
        dotnetRef.invokeMethodAsync('SetUserLocation', null, null)
        return
    }
    navigator.geolocation.getCurrentPosition(
        pos  => dotnetRef.invokeMethodAsync('SetUserLocation', pos.coords.latitude, pos.coords.longitude),
        _err => dotnetRef.invokeMethodAsync('SetUserLocation', null, null),
        { timeout: 6000, maximumAge: 300000 }
    )
}

// ── Init / dispose ────────────────────────────────────────────────────────────

export function init(dotnetRef) {
    _dotnetRef = dotnetRef
    // Must be global — Telerik calls template functions by name string
    window.caseMapTileTemplate    = caseMapTileTemplate
    window.caseMapMarkerTemplate  = caseMapMarkerTemplate
    window.caseMapMarkerClick     = caseMapMarkerClick
}

export function dispose() {
    _dotnetRef = null
    delete window.caseMapTileTemplate
    delete window.caseMapMarkerTemplate
    delete window.caseMapMarkerClick
}
