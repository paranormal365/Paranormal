/**
 * AudioEditor.razor.js — the editor's keyboard shortcuts (phase 7, 10/26/2026).
 *
 * One listener on the page while the editor is open. A key is passed to the editor only when nobody
 * is typing (inputs, selects, editable text), no dialog is open, and no Ctrl, Cmd or Alt is held,
 * so the browser's and the site's own shortcuts keep working. Ben: "K keeps, no J/K/L shuttle".
 */

const KEYS = {
  ' ': 'space', ArrowLeft: 'left', ArrowRight: 'right', '[': 'start', ']': 'end',
  k: 'keep', K: 'keep', d: 'dismiss', D: 'dismiss', Delete: 'dismiss',
  n: 'next', N: 'next', p: 'previous', P: 'previous',
  z: 'zoom', Z: 'zoom', f: 'fit', F: 'fit', l: 'loop', L: 'loop',
  Escape: 'escape', '?': 'help',
}

let listener = null
let pending = Promise.resolve()

function typing(target) {
  const el = target instanceof Element ? target : null
  if (!el) return false
  if (el.closest('input, textarea, select, [contenteditable=""], [contenteditable="true"], .k-input, .k-picker')) return true
  return false
}

export function attach(dotnetRef) {
  detach()
  listener = (ev) => {
    if (ev.ctrlKey || ev.metaKey || ev.altKey || ev.repeat && ev.key === ' ') return
    const action = KEYS[ev.key]
    if (!action) return
    if (typing(ev.target) || typing(document.activeElement)) return
    if (document.querySelector('.modal.show')) return
    // A focused button would also act on Space; the editor's meaning wins while it is open.
    ev.preventDefault()
    // One at a time, in the order pressed. Sent together, the editor ran them side by side, so
    // "]" read the playhead before the arrow pressed just before it had moved it (10/26/2026).
    const shift = ev.shiftKey
    pending = pending.then(() => dotnetRef.invokeMethodAsync('OnShortcutAsync', action, shift)).catch(() => {})
  }
  document.addEventListener('keydown', listener)
}

export function detach() {
  if (listener) document.removeEventListener('keydown', listener)
  listener = null
}
