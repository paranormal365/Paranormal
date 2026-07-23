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
