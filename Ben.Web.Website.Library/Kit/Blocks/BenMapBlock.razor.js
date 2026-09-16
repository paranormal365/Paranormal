// A map block's reader draws its map only when it scrolls into view: every map a page loads, and every route it asks
// for, counts against the site's MapKit allowance, and a long research page can carry several (2026-09-14).

const _watchers = new Map()

export function whenVisible(element, dotnetRef) {
    if (!element) return
    if (!('IntersectionObserver' in window)) { dotnetRef.invokeMethodAsync('VisibleAsync'); return }
    const observer = new IntersectionObserver(entries => {
        if (entries.some(e => e.isIntersecting)) {
            observer.disconnect()
            _watchers.delete(element)
            dotnetRef.invokeMethodAsync('VisibleAsync').catch(() => { /* the block went away */ })
        }
    }, { rootMargin: '200px 0px' })
    observer.observe(element)
    _watchers.set(element, observer)
}

export function stop(element) {
    _watchers.get(element)?.disconnect()
    _watchers.delete(element)
}
