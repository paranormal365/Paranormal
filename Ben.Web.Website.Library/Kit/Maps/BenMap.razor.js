/**
 * BenMap.razor.js — the only file on the site that talks to MapKit JS (item 228).
 *
 * One script load and one mapkit.init for the page, however many maps are on it; each map keeps
 * its annotations by container id so two on one page cannot drive each other. The token comes
 * from the website's own endpoint and MapKit asks for a fresh one as each expires.
 *
 */

const MAPKIT_SRC = 'https://cdn.apple-mapkit.com/mk/5.x.x/mapkit.js'
const _maps = new Map()          // containerId → { map, annotations, dotnetRef, resize }
let _mapkitReady = null          // Promise<void>, shared

// ── Loading and initialising MapKit once ─────────────────────────────────────

function ensureMapKit(tokenPath) {
    if (_mapkitReady) return _mapkitReady
    _mapkitReady = new Promise((resolve, reject) => {
        const finish = () => {
            mapkit.init({
                authorizationCallback: done => fetch(tokenPath, { cache: 'no-store' })
                    .then(r => r.ok ? r.text() : Promise.reject(new Error(`mapkit token ${r.status}`)))
                    .then(done)
                    .catch(reject),
            })
            const onChange = e => {
                if (e.status === 'Initialized') { mapkit.removeEventListener('configuration-change', onChange); resolve() }
            }
            mapkit.addEventListener('configuration-change', onChange)
            mapkit.addEventListener('error', e => console.warn('MapKit JS:', e.status, e.message ?? ''))
        }
        if (window.mapkit) { finish(); return }
        const s = document.createElement('script')
        s.src = MAPKIT_SRC
        s.crossOrigin = 'anonymous'
        s.onload = finish
        s.onerror = () => reject(new Error('mapkit.js failed to load'))
        document.head.appendChild(s)
    })
    return _mapkitReady
}

// ── Theme ────────────────────────────────────────────────────────────────────
// The site stamps data-bs-theme on <html> (ben-boot.js). Watching it keeps the map matched when
// the person flips the switch, with no round trip through Blazor.

function currentScheme() {
    return document.documentElement.getAttribute('data-bs-theme') === 'dark'
        ? mapkit.Map.ColorSchemes.Dark : mapkit.Map.ColorSchemes.Light
}
let _themeObserver = null
function watchTheme() {
    if (_themeObserver) return
    _themeObserver = new MutationObserver(() => {
        const scheme = currentScheme()
        for (const entry of _maps.values()) if (entry.map) entry.map.colorScheme = scheme
    })
    _themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] })
}

// ── Zoom ↔ region ────────────────────────────────────────────────────────────
// MapKit thinks in regions, the rest of the site in Web-Mercator zoom levels (an OSM habit worth
// keeping: every caller already has a zoom in mind). The span for a zoom depends on how many
// pixels the map is, so it is worked out against the container each time.

function regionForZoom(container, lat, lon, zoom) {
    const w = Math.max(container.clientWidth, 200), h = Math.max(container.clientHeight, 200)
    const degPerPx = 360 / (256 * Math.pow(2, zoom))
    const lonDelta = degPerPx * w
    const latDelta = degPerPx * h * Math.cos(lat * Math.PI / 180)
    return new mapkit.CoordinateRegion(new mapkit.Coordinate(lat, lon), new mapkit.CoordinateSpan(latDelta, lonDelta))
}
function zoomForRegion(container, region) {
    const w = Math.max(container.clientWidth, 200)
    return Math.log2(360 * w / (256 * region.span.longitudeDelta))
}

// ── Public API ───────────────────────────────────────────────────────────────

export async function create(containerId, dotnetRef, options) {
    const container = document.getElementById(containerId)
    if (!container) return
    const entry = {
        map: null, container, annotations: [], circle: null, route: null, dotnetRef, options, observer: null,
        view: { lat: options.centerLatitude, lon: options.centerLongitude, zoom: options.zoom },
        userMoved: false,
    }
    _maps.set(containerId, entry)

    // A map that cannot initialise says so where the map would be, rather than staying blank —
    // and a deployment with no key says so without asking Apple first.
    const unavailable = why => {
        console.warn('MapKit JS unavailable:', why)
        container.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100 text-secondary small">The map could not be loaded.</div>'
    }
    if (options.configured === false) { unavailable('no Maps key configured'); return }
    try { await ensureMapKit(options.tokenPath) }
    catch (err) { unavailable(err); return }
    if (!_maps.has(containerId)) return   // disposed while the script loaded

    const map = new mapkit.Map(container, {
        colorScheme: currentScheme(),
        showsCompass: mapkit.FeatureVisibility.Hidden,     // rotation is off, so a compass would only warn
        showsScale: mapkit.FeatureVisibility.Adaptive,
        showsMapTypeControl: false,
        isRotationEnabled: false,
    })
    entry.map = map
    applyView(entry)
    watchTheme()

    // The container is measured when the map is made, and a map made mid-layout — the home page
    // swaps a loader for its content, and the content arrives narrower than it ends up — frames
    // a region for the wrong width and keeps it. So the view is re-applied whenever the container
    // settles at a new size, until the person moves the map themselves; after that their view
    // is the one that matters. Window resizes come through here too.
    let sizeTimer
    entry.observer = new ResizeObserver(() => {
        clearTimeout(sizeTimer)
        sizeTimer = setTimeout(() => {
            if (!entry.map) return
            if (entry.options.fitToPins && entry.annotations.length) fit(containerId)
            else if (!entry.userMoved) applyView(entry)
        }, 100)
    })
    entry.observer.observe(container)

    // Only a person's own gesture reports a viewport: MapKit fires region-change-end for the
    // programmatic framing too, so the flag around setPins/fit keeps those quiet.
    //
    // And one report per BURST of gestures, not per gesture. A drag on MapKit ends with momentum,
    // so its region-change-end arrives only when the map stops — and a second drag interrupts
    // the first, so three quick drags produce ends at odd moments spread wider than the caller's
    // debounce. The report waits for a short quiet after an end, and any new start cancels it.
    let settle
    map.addEventListener('region-change-start', () => {
        clearTimeout(settle)
        if (!entry.framing) entry.userMoved = true
    })
    map.addEventListener('region-change-end', () => {
        if (entry.framing) return
        clearTimeout(settle)
        settle = setTimeout(() => {
            const r = map.region, half = { lat: r.span.latitudeDelta / 2, lon: r.span.longitudeDelta / 2 }
            dotnetRef.invokeMethodAsync('OnRegionChanged',
                r.center.latitude + half.lat, r.center.latitude - half.lat,
                r.center.longitude + half.lon, r.center.longitude - half.lon,
                zoomForRegion(container, r))
        }, 250)
    })
    // A click on the map itself, for a caller that asked (an address's "click to set the edge").
    // MapKit's single-tap fires for a click on empty map, not for one that selected a pin.
    if (options.reportClicks) {
        map.addEventListener('single-tap', e => {
            const c = map.convertPointOnPageToCoordinate(e.pointOnPage)
            entry.lastTap = [c.latitude, c.longitude]
            dotnetRef.invokeMethodAsync('OnMapTapped', c.latitude, c.longitude)
        })
    }

    map.addEventListener('select', e => {
        const a = e.annotation
        if (!a) return
        if (a.memberAnnotations) {           // a cluster: zoom into it rather than pick one blindly
            entry.framing = true
            map.showItems(a.memberAnnotations, { animate: true, padding: new mapkit.Padding(40, 40, 40, 40) })
            setTimeout(() => { entry.framing = false }, 600)
            return
        }
        if (typeof a.data?.index === 'number') {
            entry.lastSelected = a.data.index
            dotnetRef.invokeMethodAsync('OnPinSelected', a.data.index)
        }
    })

}

/** Frames the caller's centre and zoom against the container as it is now. Programmatic, so not reported. */
function applyView(entry) {
    const { map, container, view } = entry
    entry.framing = true
    // Measured against OUR container, never map.element: MapKit's element is not the box the
    // map fills, and measuring it framed a 944px map as though it were 115px wide.
    map.region = regionForZoom(container, view.lat, view.lon, view.zoom)
    setTimeout(() => { entry.framing = false }, 300)
}

export function setPins(containerId, pins) {
    const entry = _maps.get(containerId)
    if (!entry) return
    const apply = () => {
        const map = entry.map
        if (!map) return
        if (entry.annotations.length) map.removeAnnotations(entry.annotations)
        entry.annotations = (pins ?? []).map((p, index) => {
            const a = new mapkit.MarkerAnnotation(new mapkit.Coordinate(p.latitude, p.longitude), {
                title: p.title ?? '',
                subtitle: p.subtitle ?? '',
                glyphText: p.iconSvgPath ? '' : (p.glyph ?? ''),
                glyphImage: p.iconSvgPath ? glyphImageFor(p.iconSvgPath) : undefined,
                color: p.color ?? undefined,
                clusteringIdentifier: p.cluster ?? null,
                data: { index },
            })
            if (p.dimmed) a.element?.style && (a.element.style.opacity = '.55')
            return a
        })
        // Clusters draw their size in the pin, so the count is what a person reads first.
        map.annotationForCluster = cluster => {
            cluster.glyphText = String(cluster.memberAnnotations.length)
            cluster.title = `${cluster.memberAnnotations.length} here`
            cluster.subtitle = ''
            return cluster
        }
        if (entry.annotations.length) map.addAnnotations(entry.annotations)
        if (entry.options.fitToPins) fit(containerId)
    }
    if (entry.map) apply()
    else _mapkitReady?.then(() => setTimeout(apply, 0))   // pins arrived before the map did
}

/**
 * The chosen icon, drawn white on the pin. MapKit wants a raster-sized image per scale; an SVG
 * data URI with an explicit size serves both, and the 512-unit path the icon registry stores
 * is scaled into it.
 */
function glyphImageFor(svgPath) {
    const svg = size => 'data:image/svg+xml;utf8,' + encodeURIComponent(
        `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 512 512"><path fill="#fff" d="${svgPath}"/></svg>`)
    return { 1: svg(20), 2: svg(40), 3: svg(60) }
}

/** Draws, replaces or removes the one region circle a map may carry. */
export function setCircle(containerId, circle) {
    const entry = _maps.get(containerId)
    if (!entry) return
    const apply = () => {
        const map = entry.map
        if (!map) return
        if (entry.circle) { map.removeOverlay(entry.circle); entry.circle = null }
        if (!circle) return
        entry.circle = new mapkit.CircleOverlay(
            new mapkit.Coordinate(circle.latitude, circle.longitude), circle.radiusMeters, {
                style: new mapkit.Style({
                    fillColor: circle.fillColor, fillOpacity: circle.fillOpacity,
                    strokeColor: circle.strokeColor, strokeOpacity: circle.strokeOpacity,
                    lineWidth: circle.strokeWidth,
                }),
            })
        map.addOverlay(entry.circle)
    }
    if (entry.map) apply()
    else _mapkitReady?.then(() => setTimeout(apply, 0))
}

// ── Directions ───────────────────────────────────────────────────────────────

let _directions = null

/**
 * A driving route from the provider, drawn on the map with A and B pins. Resolves to what the
 * page should say: distance, time and the steps, or an error in words. Never rejects.
 */
export function route(containerId, req) {
    const entry = _maps.get(containerId)
    if (!entry?.map) return Promise.resolve({ distanceMeters: 0, durationSeconds: 0, steps: [], error: 'The map is not ready.' })

    clearRoute(containerId)
    _directions ??= new mapkit.Directions()

    const origin = req.originAddress && !req.originLatitude
        ? req.originAddress
        : new mapkit.Coordinate(req.originLatitude, req.originLongitude)
    const destination = new mapkit.Coordinate(req.destinationLatitude, req.destinationLongitude)

    return new Promise(resolve => {
        _directions.route({ origin, destination, transportType: mapkit.Directions.Transport.Automobile }, (err, data) => {
            if (err || !data?.routes?.length) {
                resolve({ distanceMeters: 0, durationSeconds: 0, steps: [],
                    error: err?.message ? `No route: ${err.message}` : 'No route could be found between these places.' })
                return
            }
            const best = data.routes[0]
            const map = entry.map
            const polyline = best.polyline
            polyline.style = new mapkit.Style({ strokeColor: '#1a73e8', strokeOpacity: .9, lineWidth: 4 })
            const from = new mapkit.MarkerAnnotation(polyline.points[0], { glyphText: 'A', color: '#1a73e8', title: 'Start' })
            const to = new mapkit.MarkerAnnotation(polyline.points[polyline.points.length - 1], { glyphText: 'B', color: '#d93025', title: 'Destination' })
            map.addOverlay(polyline)
            map.addAnnotations([from, to])
            entry.route = { polyline, pins: [from, to] }
            entry.framing = true
            map.showItems([polyline, from, to], { animate: true, padding: new mapkit.Padding(40, 40, 40, 40) })
            setTimeout(() => { entry.framing = false }, 800)
            resolve({
                distanceMeters: best.distance,
                durationSeconds: best.expectedTravelTime,
                steps: best.steps
                    .filter(s => s.instructions && s.instructions.trim().length)   // Apple's departure step has no words
                    .map(s => ({ instructions: s.instructions, distanceMeters: s.distance })),
                error: null,
            })
        })
    })
}

export function clearRoute(containerId) {
    const entry = _maps.get(containerId)
    if (!entry?.map || !entry.route) return
    entry.map.removeOverlay(entry.route.polyline)
    entry.map.removeAnnotations(entry.route.pins)
    entry.route = null
}

export function fit(containerId) {
    const entry = _maps.get(containerId)
    if (!entry?.map) return
    const map = entry.map
    entry.framing = true
    if (entry.annotations.length === 1) {
        const a = entry.annotations[0]
        map.region = regionForZoom(entry.container, a.coordinate.latitude, a.coordinate.longitude, 12)
    } else if (entry.annotations.length > 1) {
        map.showItems(entry.annotations, { animate: false, padding: new mapkit.Padding(40, 40, 40, 40) })
    }
    setTimeout(() => { entry.framing = false }, 300)
}

export function setCenter(containerId, lat, lon, zoom) {
    const entry = _maps.get(containerId)
    if (!entry?.map) return
    entry.view = { lat, lon, zoom }
    entry.userMoved = false      // the caller's view again, until the person moves it
    applyView(entry)
}

/**
 * Selects a pin as a click would, firing the same 'select' event. For tests: MapKit draws pins on
 * canvas, so there is no element for a test to click, and a synthetic click at a guessed offset
 * is a test of the guess. A test imports this module — the same instance the page holds — and
 * selects by index instead.
 */
export function selectPin(containerId, index) {
    const a = _maps.get(containerId)?.annotations[index]
    if (!a) return false
    a.selected = true
    return true
}

export function pinCount(containerId) {
    return _maps.get(containerId)?.annotations.length ?? 0
}

/** The pins as drawn — title, subtitle, glyph — for a test that cannot read a canvas. */
export function pins(containerId) {
    return (_maps.get(containerId)?.annotations ?? []).map(a => ({
        title: a.title, subtitle: a.subtitle, glyph: a.glyphText,
        latitude: a.coordinate.latitude, longitude: a.coordinate.longitude,
    }))
}

/** The index last reported to .NET through a select, or -1. Lets a test tell a missed click from a broken seam. */
export function lastSelectedPin(containerId) {
    return _maps.get(containerId)?.lastSelected ?? -1
}

/** What the map thinks it is showing. For a test or a person at the console, never for the page. */
export function describe(containerId) {
    const m = _maps.get(containerId)?.map
    if (!m) return null
    const r = m.region
    return {
        center: [r.center.latitude, r.center.longitude],
        span: [r.span.latitudeDelta, r.span.longitudeDelta],
        cameraDistance: m.cameraDistance,
        size: [_maps.get(containerId).container.clientWidth, _maps.get(containerId).container.clientHeight],

        annotations: m.annotations.length,
        overlays: m.overlays.length,
        lastTap: _maps.get(containerId).lastTap ?? null,
        colorScheme: m.colorScheme,
    }
}

export function dispose(containerId) {
    const entry = _maps.get(containerId)
    if (!entry) return
    entry.observer?.disconnect()
    try { entry.map?.destroy() } catch { /* already gone with its container */ }
    _maps.delete(containerId)
}
