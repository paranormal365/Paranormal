/**
 * AddressMapPlayer.razor.js
 * ─────────────────────────
 * Colocated ES module for the AddressMapPlayer Blazor component.
 *
 * Provides:
 *  - addressMapTileTemplate(ctx)   — OpenStreetMap tile URL for the Tile layer
 *  - addressMapMarkerTemplate(ctx) — Custom SVG marker pin with per-data Color + IconSvgPath
 *
 * The functions are referenced by name string in the TelerikMap UrlTemplate /
 * MapLayerMarkerSettings Template parameters. They must be in the global scope;
 * the Blazor JS isolation does NOT apply here — Telerik calls them as global
 * functions. We attach them to window in the init export.
 */

// ── Tile URL template ─────────────────────────────────────────────────────────

/** Returns the OpenStreetMap tile URL for the given context. */
function addressMapTileTemplate(ctx) {
  return `https://${ctx.subdomain}.tile.openstreetmap.org/${ctx.zoom}/${ctx.x}/${ctx.y}.png`
}

// ── Marker template ───────────────────────────────────────────────────────────

/**
 * Renders a colored SVG map-marker pin using the data item's Color and
 * IconSvgPath fields.  The function argument is the bound data item object.
 *
 * ctx properties expected:
 *   ctx.Color       — CSS color string (e.g. "#e63535")
 *   ctx.IconSvgPath — SVG path data string (512×512 viewBox)
 *   ctx.Title       — tooltip text (used by Telerik natively, not in template)
 */
function addressMapMarkerTemplate(ctx) {
  const color = ctx.Color || '#e63535'
  const path  = ctx.IconSvgPath || defaultPinPath

  return `<span
    class="k-svg-icon k-icon-xl"
    style="color:${color};filter:drop-shadow(0 2px 4px rgba(0,0,0,.45));cursor:pointer;"
    title="${ctx.Title || ''}"
    aria-hidden="true">
    <svg viewBox="0 0 512 512" focusable="false">
      <path d="${path}"></path>
    </svg>
  </span>`
}

// Default fallback: map-marker-target icon path
const defaultPinPath =
  'M256 0C158.8 0 80 78.8 80 176s176 336 176 336 176-238.8 176-336S353.2 0 256 0' +
  'm0 288c-61.9 0-112-50.1-112-112S194.1 64 256 64s112 50.1 112 112-50.1 112-112 112' +
  'm48-112c0 26.5-21.5 48-48 48s-48-21.5-48-48 21.5-48 48-48 48 21.5 48 48'

// ── Exported init ─────────────────────────────────────────────────────────────

/**
 * Call once on component initialization to register the global template
 * functions that TelerikMap references by name string.
 */
export function initAddressMapTemplates() {
  window.addressMapTileTemplate   = addressMapTileTemplate
  window.addressMapMarkerTemplate = addressMapMarkerTemplate
}
