/**
 * ApexChart.razor.js
 * ──────────────────
 * Colocated ES module for the ApexChart Blazor component.
 *
 * Every chart is tracked by its own container id in a module-level Map. That is not defensive
 * habit: a dashboard is precisely the page where several of these appear at once, and the sibling
 * map module carries a comment about what happens when a component reaches for
 * `document.querySelector(...)` instead — it finds whichever one is first in the DOM and the
 * second component silently drives the first one's chart.
 *
 * The vendored library is imported once, lazily, and shared. It is 563 KB; importing it per chart
 * would re-fetch nothing (the browser caches the module) but would still re-evaluate it.
 */

const _charts = new Map()
/**
 * Creates in flight, keyed by container id.
 *
 * `create` is async, and its first await — loading the library — is a yield point. Two calls for
 * the same container (Blazor's prerender and interactive passes, or a re-render arriving while
 * the first is still awaiting) both got past the destroy at the top before either had registered
 * anything to destroy, and both then rendered into the same element. ApexCharts APPENDS its SVG,
 * so the result was two complete charts stacked inside one card — visible immediately on the
 * admin dashboard, invisible on any page with a single chart, which is why the first test of this
 * module passed.
 *
 * Serialising per container makes the second call wait for the first, so its destroy finds a
 * chart to remove.
 */
const _pending = new Map()
let _apexPromise = null

/** The vendored MIT build. See wwwroot/plugins/apexcharts/VENDORED.md for why it is 4.7.0. */
function loadApex() {
    if (!_apexPromise) {
        _apexPromise = import('/plugins/apexcharts/apexcharts.esm.js').then(m => m.default ?? m)
    }
    return _apexPromise
}

/**
 * Colours come from the page, not from ApexCharts' own palette.
 *
 * The SmartAdmin stylesheet themes the chart's *chrome* — tooltips, legends, grid lines — but the
 * series colours and the axis text are drawn by the library from its config, so they know nothing
 * about the site's palette or which theme is showing. Reading the CSS custom properties at build
 * time is what keeps a chart from being the one element on a dark page still wearing its default
 * light-mode blues and near-black labels.
 */
function paletteFrom(el) {
    const css = getComputedStyle(el)
    const read = (name, fallback) => (css.getPropertyValue(name) || '').trim() || fallback

    // --bs-body-color is the text colour Bootstrap resolves for the active theme, so it flips with
    // the theme without this module having to know which one is on.
    const text = read('--bs-body-color', '#212529')
    const muted = read('--bs-secondary-color', text)
    const border = read('--bs-border-color', 'rgba(128,128,128,.25)')

    // A series palette drawn from the template's own accents, so charts match the buttons and
    // badges around them rather than introducing a second colour language.
    const series = [
        read('--bs-primary', '#37508a'),
        read('--bs-success', '#1a9e5c'),
        read('--bs-info', '#3a86c8'),
        read('--bs-warning', '#d2a24c'),
        read('--bs-danger', '#c0504d'),
        read('--bs-purple', '#7c5cbf'),
    ]

    return { text, muted, border, series }
}

/**
 * The width the chart should draw at: the container's own, or its parent's when the container has
 * not been laid out yet (which is the case on the render where the chart is created). Falls back
 * to the library's default only when neither is known.
 */
function measuredWidth(el) {
    const own = el.clientWidth
    if (own > 0) return own

    const parent = el.parentElement
    const inherited = parent ? parent.clientWidth : 0
    return inherited > 0 ? inherited : '100%'
}

/** Shared config: the parts every chart on this site should agree about. */
function baseOptions(el, spec) {
    const p = paletteFrom(el)
    const sparkline = spec.type === 'sparkline'
    const type = sparkline ? (spec.sparklineType || 'line') : spec.type

    const options = {
        chart: {
            type,
            height: spec.height || 320,
            // Measured, not '100%'. A percentage is resolved by ApexCharts against whatever it
            // thinks the parent is, and for a sparkline it fell back to its own 300px default —
            // measured at 300px inside a 258px card, hanging 119px into the card beside it. The
            // wrapper element clips that, but a clipped chart is a cut-off chart; giving the
            // library the real number makes it draw one that fits.
            width: measuredWidth(el),
            // The library's own toolbar duplicates things the page does better, and its animations
            // re-run on every Blazor re-render, which reads as flicker rather than polish.
            toolbar: { show: false },
            animations: { enabled: false },
            fontFamily: 'inherit',
            background: 'transparent',
            sparkline: { enabled: sparkline },
        },
        colors: p.series,
        theme: { mode: 'light' },   // chrome comes from CSS; this only stops Apex second-guessing
        dataLabels: { enabled: false },
        tooltip: { theme: 'light' },
        grid: { borderColor: p.border, strokeDashArray: 3 },
        stroke: { curve: 'smooth', width: type === 'bar' ? 0 : 2 },
        plotOptions: {
            // ApexCharts sizes a bar as a share of its slot, and with one category the slot is
            // the whole chart — a single value renders as a slab most of the panel wide, which
            // reads as a filled progress bar rather than one bar among others. Scaling the share
            // with the number of categories keeps a lone bar bar-shaped and still lets a busy
            // chart use the room. Measured against the sidecar page, which has exactly one
            // version and was the first thing to show the problem.
            bar: {
                columnWidth: `${Math.min(60, 18 * Math.max(1, (spec.categories || []).length))}%`,
                borderRadius: 3,
            },
        },
        legend: { labels: { colors: p.text } },
        noData: {
            text: spec.noDataText || 'Nothing to show yet',
            style: { color: p.muted, fontSize: '13px' },
        },
    }

    if (!sparkline) {
        const axisLabels = { style: { colors: p.muted, fontSize: '12px' } }
        options.xaxis = {
            categories: spec.categories || [],
            labels: axisLabels,
            axisBorder: { color: p.border },
            axisTicks: { color: p.border },
        }
        options.yaxis = { labels: axisLabels }
    }

    // Donut and pie take a flat number[] plus labels; everything else takes named series.
    if (type === 'donut' || type === 'pie') {
        options.series = spec.values || []
        options.labels = spec.categories || []
        options.stroke = { width: 0 }
        options.legend = { position: 'bottom', labels: { colors: p.text } }
    } else {
        options.series = spec.series || []
    }

    return options
}

export async function create(containerId, spec) {
    // Wait for any create already running for this container, so the destroy below has something
    // to find. Failures are swallowed: a previous attempt that threw must not stop this one.
    const inFlight = _pending.get(containerId)
    if (inFlight) await inFlight.catch(() => {})

    const run = (async () => {
        const el = document.getElementById(containerId)
        if (!el) return

        // Blazor can re-run first-render work (prerender then interactive). Replacing rather than
        // stacking keeps one chart per container.
        destroy(containerId)

        const ApexCharts = await loadApex()
        const chart = new ApexCharts(el, baseOptions(el, spec))
        await chart.render()
        _charts.set(containerId, { chart, spec })
    })()

    _pending.set(containerId, run)
    try {
        await run
    } finally {
        if (_pending.get(containerId) === run) _pending.delete(containerId)
    }
}

/** New numbers, same chart — avoids the flash of a full teardown. */
export async function update(containerId, spec) {
    const entry = _charts.get(containerId)
    if (!entry) return create(containerId, spec)

    entry.spec = spec
    const type = spec.type === 'sparkline' ? (spec.sparklineType || 'line') : spec.type

    if (type === 'donut' || type === 'pie') {
        await entry.chart.updateOptions({ labels: spec.categories || [] }, false, false)
        await entry.chart.updateSeries(spec.values || [], false)
    } else {
        await entry.chart.updateOptions({ xaxis: { categories: spec.categories || [] } }, false, false)
        await entry.chart.updateSeries(spec.series || [], false)
    }
}

/**
 * Re-reads the palette and redraws. Called when the site's theme toggle flips, because the
 * colours were resolved from CSS at build time and will otherwise stay whichever theme was on
 * when the chart was created.
 */
export async function retheme(containerId) {
    const entry = _charts.get(containerId)
    if (!entry) return

    const el = document.getElementById(containerId)
    if (!el) return

    const p = paletteFrom(el)
    await entry.chart.updateOptions({
        colors: p.series,
        grid: { borderColor: p.border, strokeDashArray: 3 },
        legend: { labels: { colors: p.text } },
        xaxis: { labels: { style: { colors: p.muted } } },
        yaxis: { labels: { style: { colors: p.muted } } },
    }, false, false)
}

export function destroy(containerId) {
    const entry = _charts.get(containerId)
    if (!entry) return
    try { entry.chart.destroy() } catch { /* already gone with its DOM */ }
    _charts.delete(containerId)

    // Belt and braces: if a chart ever did get appended without being registered — the exact
    // failure this module now serialises to prevent — destroy() would leave it on screen forever
    // because it is not in the Map. Clearing the container makes recovery possible.
    const el = document.getElementById(containerId)
    if (el) el.replaceChildren()
}
