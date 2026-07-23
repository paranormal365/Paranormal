// Tile URL function for the directions map (mirrors addressMapTileTemplate)
window.directionsMapTileTemplate = (ctx) =>
    `https://${ctx.subdomain}.tile.openstreetmap.org/${ctx.zoom}/${ctx.x}/${ctx.y}.png`;

// "From" marker — blue label A
window.directionsFromMarkerTemplate = () =>
    '<span style="display:inline-flex;align-items:center;justify-content:center;' +
    'width:22px;height:22px;border-radius:50%;background:#1a73e8;color:#fff;' +
    'font-weight:700;font-size:11px;border:2px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,.4)">A</span>';

// "To" marker — red label B
window.directionsToMarkerTemplate = () =>
    '<span style="display:inline-flex;align-items:center;justify-content:center;' +
    'width:22px;height:22px;border-radius:50%;background:#d93025;color:#fff;' +
    'font-weight:700;font-size:11px;border:2px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,.4)">B</span>';

// Print the directions panel
window.printDirections = () => window.print();

// Called from OnAfterRenderAsync after a route is displayed.
// Retries until the Kendo Map widget is ready, then calls resize(true)
// which forces the tile layer to calculate viewport tiles and load them —
// the same internal path triggered when the user pans or zooms.
// Fits the Kendo Map to the bounding box of the full route.
// Uses kendo.dataviz.map.Extent which auto-computes the zoom level to fill
// the current pixel viewport — same internal path as panning/zooming.
export function fitMapToRoute(minLat, maxLat, minLon, maxLon) {
    const attempt = (triesLeft) => {
        if (triesLeft <= 0) return;
        setTimeout(() => {
            if (typeof kendo === 'undefined' || !kendo.dataviz?.map) {
                attempt(triesLeft - 1); return;
            }
            const els = document.querySelectorAll('[data-role="map"], .k-map');
            if (!els.length) { attempt(triesLeft - 1); return; }
            let found = false;
            els.forEach(el => {
                const w = kendo.widgetInstance(el);
                if (w && !found) {
                    found = true;
                    // Add 15% padding on each side so markers at the edges
                    // are not clipped by the viewport border.
                    const latPad = Math.max((maxLat - minLat) * 0.15, 0.005);
                    const lonPad = Math.max((maxLon - minLon) * 0.15, 0.005);
                    const extent = new kendo.dataviz.map.Extent(
                        new kendo.dataviz.map.Location(maxLat + latPad, minLon - lonPad), // NW
                        new kendo.dataviz.map.Location(minLat - latPad, maxLon + lonPad)  // SE
                    );
                    w.extent(extent);
                }
            });
            if (!found) attempt(triesLeft - 1);
        }, 150);
    };
    attempt(8);
}
