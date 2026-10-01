// BenTour's measuring half (item 166 W0). Finds the step's target, scrolls it into view with
// the verified fallback from domInterop's lesson (smooth scrollIntoView silently no-ops on
// some engines when the scroller is an inner container), and reports where the highlight ring
// and the step card should sit — in viewport coordinates, because both are position:fixed.

/** @returns {null | {top,left,width,height,cardTop,cardLeft}} */
export function positionFor(selector) {
    // The step's own target — unless it is not on screen and something stands in for it. A group
    // page's tabs are hidden where the rail lists the same pages (Signal, 2026-10-01), and each
    // rail entry says which tab it replaces with data-tour-for.
    const shown = e => !!e && e.getClientRects().length > 0;
    let el = document.querySelector(selector);
    if (!shown(el)) {
        const stand = document.querySelector(`[data-tour-for="${CSS.escape(selector)}"]`);
        if (shown(stand)) el = stand;
    }
    if (!el) return null;

    el.scrollIntoView({ behavior: 'smooth', block: 'center' });

    const measure = () => {
        const r = el.getBoundingClientRect();

        // The card prefers below the target, flips above when there is no room, and clamps
        // into the viewport horizontally.
        const cardWidth = 340, cardHeight = 170, gap = 10;
        let cardTop = r.bottom + gap;
        if (cardTop + cardHeight > window.innerHeight) cardTop = Math.max(gap, r.top - cardHeight - gap);
        let cardLeft = Math.min(Math.max(gap, r.left), window.innerWidth - cardWidth - gap);

        return {
            top: r.top, left: r.left, width: r.width, height: r.height,
            cardTop, cardLeft,
        };
    };

    // Verify the smooth scroll actually happened; jump instantly if not, then measure.
    return new Promise(resolve => {
        setTimeout(() => {
            const r = el.getBoundingClientRect();
            if (r.bottom < 0 || r.top > window.innerHeight)
                el.scrollIntoView({ block: 'center' });
            resolve(measure());
        }, 380);
    });
}
