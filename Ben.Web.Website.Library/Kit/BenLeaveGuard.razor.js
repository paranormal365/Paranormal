// The browser half of BenLeaveGuard: the tab-closing / reload / typed-URL warning.
//
// Added and removed rather than left in place returning nothing: a registered beforeunload handler
// disables the back/forward cache even when it never fires. Ported from the video editor's
// setUnloadGuard (domInterop.js), which learned that on 2026-09-05.

const _guards = new Map()

export function setGuard(id, enabled) {
    const existing = _guards.get(id)
    if (enabled) {
        if (existing) return
        const handler = (e) => {
            e.preventDefault()
            // Browsers show their own wording now; returnValue must still be set for some to ask at all.
            e.returnValue = ''
            return ''
        }
        window.addEventListener('beforeunload', handler)
        _guards.set(id, handler)
        return
    }
    if (!existing) return
    window.removeEventListener('beforeunload', existing)
    _guards.delete(id)
}
