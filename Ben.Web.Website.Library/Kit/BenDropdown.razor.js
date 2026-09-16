// The browser half of a Floating BenDropdown: the open menu placed against the window, so a parent that clips — a grid
// cell — cannot cut it off. Blazor owns the menu and its contents; this only sets where it sits, and puts that back
// when the menu closes.

const _placed = new Map()

const placedStyles = ['position', 'inset', 'left', 'top', 'right', 'bottom', 'transform', 'margin', 'maxHeight', 'overflowY', 'zIndex']

export function place(toggle, menu, alignEnd) {
    if (!toggle || !menu) return
    unplace(menu)

    const update = () => {
        if (!toggle.isConnected || !menu.isConnected) return
        const t = toggle.getBoundingClientRect()
        const vw = document.documentElement.clientWidth
        const vh = document.documentElement.clientHeight
        Object.assign(menu.style, { position: 'fixed', inset: 'auto', transform: 'none', margin: '0', zIndex: '1001', maxHeight: `${vh - 16}px`, overflowY: 'auto' })

        const w = menu.offsetWidth
        const h = menu.offsetHeight
        const left = Math.max(8, Math.min(alignEnd ? t.right - w : t.left, vw - w - 8))
        const roomBelow = vh - t.bottom
        const top = roomBelow >= h + 8 || roomBelow >= t.top ? t.bottom + 2 : t.top - h - 2
        menu.style.left = `${left}px`
        menu.style.top = `${Math.max(8, Math.min(top, vh - h - 8))}px`
    }

    update()
    window.addEventListener('scroll', update, true)
    window.addEventListener('resize', update)
    _placed.set(menu, () => {
        window.removeEventListener('scroll', update, true)
        window.removeEventListener('resize', update)
        for (const p of placedStyles) menu.style[p] = ''
    })
}

export function unplace(menu) {
    const undo = _placed.get(menu)
    if (!undo) return
    undo()
    _placed.delete(menu)
}
