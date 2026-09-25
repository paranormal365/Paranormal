// The live Apple map inside a map box (M6-16, review correction R35).
//
// A map box at rest shows a still picture, so opening a board makes no MapKit load. Only when somebody asks for
// the live map is MapKit JS loaded (once for the page) and a map put inside that box; closing it removes the map
// again. Copied in shape from the site's Kit/Maps/BenMap.razor.js: one script load, one mapkit.init, the token
// fetched from the website and refreshed by MapKit as each one expires, and the colour scheme following the
// site's data-bs-theme switch.

const MAPKIT_SRC = 'https://cdn.apple-mapkit.com/mk/5.x.x/mapkit.js';
const maps = new WeakMap();
let ready = null;

function ensureMapKit(tokenUrl) {
    if (ready) return ready;
    ready = new Promise((resolve, reject) => {
        const finish = () => {
            mapkit.init({
                authorizationCallback: done => fetch(tokenUrl, { cache: 'no-store', credentials: 'omit' })
                    .then(r => (r.ok ? r.text() : Promise.reject(new Error('mapkit token ' + r.status))))
                    .then(done)
                    .catch(reject),
            });
            const onChange = e => {
                if (e.status === 'Initialized') { mapkit.removeEventListener('configuration-change', onChange); resolve(); }
            };
            mapkit.addEventListener('configuration-change', onChange);
            mapkit.addEventListener('error', e => { reject(new Error('MapKit JS: ' + e.status)); });
        };
        if (window.mapkit) { finish(); return; }
        const script = document.createElement('script');
        script.src = MAPKIT_SRC;
        script.crossOrigin = 'anonymous';
        script.onload = finish;
        script.onerror = () => reject(new Error('mapkit.js failed to load'));
        document.head.appendChild(script);
    });
    // A failed load may succeed later (offline, then online): forget the failure.
    ready.catch(() => { ready = null; });
    return ready;
}

function scheme() {
    return document.documentElement.getAttribute('data-bs-theme') === 'dark'
        ? mapkit.Map.ColorSchemes.Dark : mapkit.Map.ColorSchemes.Light;
}

function region(container, lat, lng, zoom) {
    const w = Math.max(container.clientWidth, 200);
    const h = Math.max(container.clientHeight, 200);
    const degPerPx = 360 / (256 * Math.pow(2, zoom));
    return new mapkit.CoordinateRegion(
        new mapkit.Coordinate(lat, lng),
        new mapkit.CoordinateSpan(degPerPx * h * Math.cos(lat * Math.PI / 180), degPerPx * w));
}

/** Puts a live map in the element. Answers false when MapKit or its token could not be had. */
export async function mount(element, tokenUrl, lat, lng, zoom, title) {
    if (!element || maps.has(element)) return !!element;
    try {
        await ensureMapKit(tokenUrl);
    } catch {
        return false;
    }
    if (!element.isConnected) return false;
    const map = new mapkit.Map(element, { colorScheme: scheme(), showsCompass: mapkit.FeatureVisibility.Hidden });
    map.region = region(element, lat, lng, zoom);
    map.addAnnotation(new mapkit.MarkerAnnotation(new mapkit.Coordinate(lat, lng), { title: title || '' }));
    const observer = new MutationObserver(() => { map.colorScheme = scheme(); });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
    maps.set(element, { map, observer });
    return true;
}

/** Takes the live map out of the element. */
export function unmount(element) {
    const entry = element && maps.get(element);
    if (!entry) return;
    entry.observer.disconnect();
    try { entry.map.destroy(); } catch { /* already gone with the page */ }
    maps.delete(element);
}
