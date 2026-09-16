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

        // A playhead moving over a session sends a new pin four times a second, and rebuilding
        // the annotation each time is what made the marker jump from fix to fix — Ben, 2026-09-16:
        // "the green circle looks like it is bouncing. It should just follow the data instead of
        // bounce." When the set is the same shape as the one already drawn, the existing
        // annotations are MOVED instead, and a moved annotation glides.
        if (canMoveInPlace(entry, pins)) {
            pins.forEach((p, index) => moveAnnotation(entry.annotations[index], p))
            return
        }

        if (entry.annotations.length) map.removeAnnotations(entry.annotations)
        entry.annotations = (pins ?? []).map((p, index) => {
            // A pin that carries a bearing is a direction, not a place: a flat arrow centred on
            // the coordinate rather than a teardrop with something spinning inside it.
            if (p.rotationDegrees !== null && p.rotationDegrees !== undefined) {
                return arrowAnnotation(p, index)
            }

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

// ── Where somebody is, and which way they are facing ─────────────────────────

/** How long a step takes to glide. One playback tick, so the arrow arrives as the next one is sent. */
const ARROW_GLIDE_MS = 260

/**
 * An arrow centred on the coordinate, rotated to a bearing.
 *
 * Built from an element rather than an image so the rotation can be a CSS transform: a transform
 * is what the browser can interpolate, and interpolating it is the difference between an arrow
 * that turns and one that flicks between headings. Ben, 2026-09-16: "if the person turns, it
 * should just turn smoothly and if the next reading the person is walking, it should follow the
 * path smoothly. Not bouncing."
 */
function arrowAnnotation(p, index) {
    const annotation = new mapkit.Annotation(
        new mapkit.Coordinate(p.latitude, p.longitude),
        () => {
            // Two elements on purpose. MapKit positions the element it is handed, using its own
            // transform — so the rotation goes on an inner one it never touches. Sharing a
            // transform with the map is how a marker ends up jumping to the corner of the tile.
            const wrap = document.createElement('div')
            wrap.style.width = '28px'
            wrap.style.height = '28px'
            wrap.style.marginLeft = '-14px'
            wrap.style.marginTop = '-14px'

            const turner = document.createElement('div')
            turner.className = 'ben-map-arrow'
            turner.style.width = '28px'
            turner.style.height = '28px'
            turner.style.willChange = 'transform'
            turner.style.transition = `transform ${ARROW_GLIDE_MS}ms linear`
            turner.style.transform = `rotate(${p.rotationDegrees}deg)`
            turner.innerHTML =
                `<svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true">
                   <path d="M12 2 L20 21 L12 16.5 L4 21 Z"
                         fill="${p.color ?? '#28c76f'}"
                         stroke="rgba(0,0,0,.55)" stroke-width="1" stroke-linejoin="round"/>
                 </svg>`
            wrap.appendChild(turner)
            return wrap
        },
        { title: p.title ?? '', subtitle: p.subtitle ?? '', data: { index } })

    // Kept on the annotation so the next update can turn by the SHORTEST way round rather than
    // unwinding 350 degrees to get from 355 to 5.
    annotation.__benBearing = p.rotationDegrees
    annotation.__benIsArrow = true
    return annotation
}

/**
 * Whether the incoming set can be moved onto the annotations already drawn.
 *
 * Same count, and each one the same KIND as the annotation holding its place. A set that differs
 * in either is a different map and is rebuilt — moving a pin onto an arrow's annotation would
 * change what the marker means while pretending to be an update.
 */
function canMoveInPlace(entry, pins) {
    const incoming = pins ?? []
    if (!entry.annotations.length || entry.annotations.length !== incoming.length) return false
    return incoming.every((p, index) => {
        const isArrow = p.rotationDegrees !== null && p.rotationDegrees !== undefined
        return Boolean(entry.annotations[index]?.__benIsArrow) === isArrow
    })
}

/** Moves one annotation to where its pin now is, turning the shortest way if it is an arrow. */
function moveAnnotation(annotation, p) {
    if (!annotation) return
    // MapKit animates a coordinate change on an annotation that is already on the map, which is
    // the whole reason this path exists: the same object moving reads as somebody walking.
    annotation.coordinate = new mapkit.Coordinate(p.latitude, p.longitude)

    if (!annotation.__benIsArrow) return
    if (p.rotationDegrees === null || p.rotationDegrees === undefined) return

    // Unwound rather than clamped to 0–360: CSS interpolates the NUMBER, so going from 350 to 10
    // written plainly spins the arrow almost the whole way round the wrong way. Carrying the
    // accumulated angle and adding the shortest difference turns it 20 degrees, which is what a
    // person walking round a corner actually did.
    const previous = annotation.__benBearing ?? p.rotationDegrees
    let delta = (p.rotationDegrees - previous) % 360
    if (delta > 180) delta -= 360
    if (delta < -180) delta += 360

    const unwound = previous + delta
    annotation.__benBearing = unwound
    const element = annotation.element?.querySelector?.('.ben-map-arrow') ?? annotation.element
    if (element?.style) element.style.transform = `rotate(${unwound}deg)`
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
    if (!entry?.map) return
    if (entry.route) {
        entry.map.removeOverlay(entry.route.polyline)
        entry.map.removeAnnotations(entry.route.pins)
        entry.route = null
    }
    if (entry.legs?.length) {
        entry.map.removeOverlays(entry.legs)
        entry.legs = []
    }
}

// ── Routes through several stops (map blocks, 2026-09-14) ───────────────────
// Only lines: the stops themselves are the map's ordinary pins. The route colour follows the theme through a CSS custom
// property on the container, so it reads on light and dark maps alike.

function routeStyle(entry, dashed) {
    const color = getComputedStyle(entry.container).getPropertyValue('--ben-map-route').trim()
        || getComputedStyle(document.documentElement).getPropertyValue('--bs-primary').trim()
        || '#1a73e8'
    return new mapkit.Style({ strokeColor: color, strokeOpacity: .9, lineWidth: 4, lineDash: dashed ? [6, 6] : [] })
}

function frameLegs(entry) {
    if (!entry.legs?.length) return
    entry.framing = true
    entry.map.showItems([...entry.legs, ...entry.annotations], { animate: true, padding: new mapkit.Padding(40, 40, 40, 40) })
    setTimeout(() => { entry.framing = false }, 800)
}

/** Straight lines from stop to stop. */
export function routeStraight(containerId, stops) {
    const entry = _maps.get(containerId)
    if (!entry?.map) return
    clearRoute(containerId)
    const line = new mapkit.PolylineOverlay(stops.map(s => new mapkit.Coordinate(s.latitude, s.longitude)),
        { style: routeStyle(entry, false) })
    entry.map.addOverlay(line)
    entry.legs = [line]
    frameLegs(entry)
}

/**
 * Walking or driving directions leg by leg, asked for one after another. A leg the provider cannot route is drawn as a
 * dashed straight line and reported as not routed. Resolves to one { routed, distanceMeters, durationSeconds } per leg;
 * never rejects.
 */
export async function routeThrough(containerId, stops, transport) {
    const entry = _maps.get(containerId)
    if (!entry?.map) return []
    clearRoute(containerId)
    _directions ??= new mapkit.Directions()
    const type = transport === 'walking' ? mapkit.Directions.Transport.Walking : mapkit.Directions.Transport.Automobile
    const results = []
    entry.legs = []

    for (let i = 0; i + 1 < stops.length; i++) {
        const from = new mapkit.Coordinate(stops[i].latitude, stops[i].longitude)
        const to = new mapkit.Coordinate(stops[i + 1].latitude, stops[i + 1].longitude)
        const best = await new Promise(resolve =>
            _directions.route({ origin: from, destination: to, transportType: type },
                (err, data) => resolve(err || !data?.routes?.length ? null : data.routes[0])))
        if (!_maps.has(containerId)) return results   // the map went away while we waited

        if (best) {
            best.polyline.style = routeStyle(entry, false)
            entry.map.addOverlay(best.polyline)
            entry.legs.push(best.polyline)
            results.push({ routed: true, distanceMeters: best.distance, durationSeconds: best.expectedTravelTime })
        } else {
            const line = new mapkit.PolylineOverlay([from, to], { style: routeStyle(entry, true) })
            entry.map.addOverlay(line)
            entry.legs.push(line)
            results.push({ routed: false, distanceMeters: 0, durationSeconds: null })
        }
    }

    frameLegs(entry)
    return results
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
