/**
 * BenMap.razor.js — the only file on the site that talks to MapKit JS (item 228).
 *
 * One script load and one mapkit.init for the page, however many maps are on it; each map keeps
 * its annotations by container id so two on one page cannot drive each other. The token comes
 * from the website's own endpoint and MapKit asks for a fresh one as each expires.
 *
 * The Telerik functions at the bottom serve the fallback path and go when it does.
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
        map: null, container, annotations: [], dotnetRef, options, observer: null,
        view: { lat: options.centerLatitude, lon: options.centerLongitude, zoom: options.zoom },
        userMoved: false,
    }
    _maps.set(containerId, entry)

    try { await ensureMapKit(options.tokenPath) }
    catch (err) {
        // A map that cannot initialise says so where the map would be, rather than staying blank.
        console.warn('MapKit JS unavailable:', err)
        container.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100 text-secondary small">The map could not be loaded.</div>'
        return
    }
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
                glyphText: p.glyph ?? '',
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
        colorScheme: m.colorScheme,
    }
}

export function dispose(containerId) {
    const entry = _maps.get(containerId)
    if (!entry) return
    entry.observer?.disconnect()
    try { entry.map?.destroy() } catch { /* already gone with its container */ }
    _maps.delete(containerId)
    if (_maps.size === 0) telerikTeardown()
}

// ── The Telerik fallback ─────────────────────────────────────────────────────
// Telerik resolves template functions by NAME STRING, so these hang off window. They are keyed
// by container id and stateless, so several maps registering them is harmless.

const _telerikRefs = new Map()

function benMapTileTemplate(ctx) {
    return `https://${ctx.subdomain}.tile.openstreetmap.org/${ctx.zoom}/${ctx.x}/${ctx.y}.png`
}
function benMapMarkerTemplate(ctx) {
    const title = (ctx.Title || '').replace(/"/g, '&quot;')
    const dim = ctx.Dimmed ? 'opacity:.55;' : ''
    const glyph = ctx.Glyph || '📍'
    const color = ctx.Color ? `color:${ctx.Color};` : ''
    return `<span onclick="benMapMarkerClick('${ctx.ContainerId}', ${ctx.Index})" title="${title}"
        style="font-size:1.5rem;cursor:pointer;${dim}${color}filter:drop-shadow(0 2px 4px rgba(0,0,0,.5));">${glyph}</span>`
}
function benMapMarkerClick(containerId, index) {
    _telerikRefs.get(containerId)?.invokeMethodAsync('OnPinSelected', index)
}

export function initTelerik(containerId, dotnetRef) {
    _telerikRefs.set(containerId, dotnetRef)
    const entry = { map: null, annotations: [], dotnetRef, options: {}, observer: null }
    _maps.set(containerId, entry)
    window.benMapTileTemplate = benMapTileTemplate
    window.benMapMarkerTemplate = benMapMarkerTemplate
    window.benMapMarkerClick = benMapMarkerClick
    // Telerik's map measures its container once; the component re-measures it on our word.
    let timeout
    entry.observer = new ResizeObserver(() => {
        clearTimeout(timeout)
        timeout = setTimeout(() => dotnetRef.invokeMethodAsync('OnContainerResized'), 150)
    })
    entry.observer.observe(document.getElementById(containerId))
}

function telerikTeardown() {
    _telerikRefs.clear()
    delete window.benMapTileTemplate
    delete window.benMapMarkerTemplate
    delete window.benMapMarkerClick
}
