// Infinite scroll for the feed (item 186).
//
// An IntersectionObserver on a sentinel that sits just above the "Load more" button. The button
// STAYS: it is what works when this module fails to load, when JS is off, and when the observer
// is unsupported — and it is also the honest bottom of the page for somebody who wants to stop.
// Scrolling is the hook; the button is the floor.
//
// The observer is deliberately given a generous rootMargin so the next page is already arriving
// by the time the reader reaches the end of this one — the difference between "endless" and
// "waiting".

let observer = null;

export function observeSentinel(sentinel, dotnetRef) {
    disconnect();

    if (!sentinel || typeof IntersectionObserver === 'undefined') return false;

    observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            // Fire and forget: LoadMoreAsync guards its own re-entry and no-more-pages cases, so
            // a burst of intersections while the page settles costs nothing.
            dotnetRef.invokeMethodAsync('LoadMoreFromScrollAsync');
        }
    }, { rootMargin: '600px 0px' });

    observer.observe(sentinel);
    return true;
}

export function disconnect() {
    if (observer) {
        observer.disconnect();
        observer = null;
    }
}
