// Keeps a case message thread at its newest message: jumps there after a load or a send, and stays there while content
// grows (a link card arriving, a picture decoding) — unless the reader has scrolled up, in which case it leaves them be.
// Blazor owns the list and its messages; this only sets how far the list is scrolled.

const _pinned = new WeakMap()

const atEnd = list => list.scrollHeight - list.scrollTop - list.clientHeight < 48

export function toEnd(list) {
    if (!list) return
    list.scrollTop = list.scrollHeight
    if (_pinned.has(list)) return

    let follow = true
    const onScroll = () => { follow = atEnd(list) }
    const grew = () => { if (follow) list.scrollTop = list.scrollHeight }
    const sizes = new ResizeObserver(grew)
    const watchChildren = () => { for (const child of list.children) sizes.observe(child) }
    const added = new MutationObserver(() => { watchChildren(); grew() })

    watchChildren()
    added.observe(list, { childList: true })
    list.addEventListener('scroll', onScroll, { passive: true })
    _pinned.set(list, () => {
        sizes.disconnect()
        added.disconnect()
        list.removeEventListener('scroll', onScroll)
    })
}

export function release(list) {
    if (!list) return
    _pinned.get(list)?.()
    _pinned.delete(list)
}
