// The browser half of BenTabs' one-row strip: a fade on whichever edge has tabs past it, the mouse wheel scrolling the row
// sideways, and the chosen tab brought into view. Blazor owns the tabs; this only toggles two classes on the list and sets
// how far it is scrolled.

const _attached = new Map()

function mark(list) {
    const max = list.scrollWidth - list.clientWidth
    list.classList.toggle('is-more-before', list.scrollLeft > 1)
    list.classList.toggle('is-more-after', list.scrollLeft < max - 1)
}

export function attach(list) {
    if (!list || _attached.has(list)) return
    const update = () => mark(list)
    const wheel = e => {
        // Only a vertical wheel over a row that actually overflows; a trackpad's own sideways swipe is left alone.
        if (list.scrollWidth <= list.clientWidth || Math.abs(e.deltaX) >= Math.abs(e.deltaY)) return
        const before = list.scrollLeft
        list.scrollLeft += e.deltaY
        if (list.scrollLeft !== before) e.preventDefault()   // at either end the page scrolls as usual
    }
    const resize = new ResizeObserver(update)
    list.addEventListener('scroll', update, { passive: true })
    list.addEventListener('wheel', wheel, { passive: false })
    resize.observe(list)
    update()
    _attached.set(list, () => {
        list.removeEventListener('scroll', update)
        list.removeEventListener('wheel', wheel)
        resize.disconnect()
    })
}

export function reveal(list) {
    const active = list?.querySelector('.nav-link.active')
    if (!active) return
    const l = list.getBoundingClientRect()
    const a = active.getBoundingClientRect()
    if (a.left < l.left) list.scrollLeft -= l.left - a.left + 24
    else if (a.right > l.right) list.scrollLeft += a.right - l.right + 24
    mark(list)
}

export function detach(list) {
    _attached.get(list)?.()
    _attached.delete(list)
}
