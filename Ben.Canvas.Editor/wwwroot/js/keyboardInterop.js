// The board's keyboard shortcuts.
//
// Copied from Ben.Video.Editor's keyboardInterop.js: one document listener that forwards keys to C#,
// where CanvasKeyMap decides what they mean. The guards are the part worth keeping. Typing into a
// field, a Telerik popup, a dialog or a menu keeps its own keys; Cmd counts as Ctrl so undo works on a
// Mac; and a key the editor handles is claimed so the browser does not also act on it.
//
// Canvas-specific rules:
//  - Keys pressed outside the editor are left alone, so the site around an embedded board keeps its
//    own shortcuts.
//  - Bare letters are claimed only while the board itself has focus. A bare R typed anywhere else is
//    the letter R.
//  - Space is not handled here: panning with Space held belongs to the gesture module.
//  - Ctrl+V is never claimed; the paste event carries the clipboard and a key handler does not.
//  - Shift+1 arrives as "!" on a US keyboard, so it is forwarded as "1" for "fit".

function aPopupIsOpen() {
  return [...document.querySelectorAll('.k-animation-container')].some(container =>
    container.getClientRects().length > 0
    && container.querySelector('.k-popup, .k-menu-group, .k-list')?.getClientRects().length > 0)
}

let dotnetRef = null
let handler = null

const BOARD_KEYS = ['r', 'R', 'c', 'C', 't', 'T', 'n', 'N', 'm', 'M', 'i', 'I', 'l', 'L', '[', ']', '?', 'Enter', 'F2']
const CTRL_KEYS = ['z', 'Z', 'y', 'Y', 'd', 'D', 'a', 'A', 's', 'S', 'g', 'G', '0', '=', '+', '-', '[', ']', '{', '}']

export function register(ref) {
  unregister()
  dotnetRef = ref

  handler = (e) => {
    const active = document.activeElement
    const tag = active?.tagName?.toLowerCase()
    if (tag === 'input' || tag === 'textarea' || tag === 'select') return
    if (active?.isContentEditable) return
    if (active?.closest('.k-popup, .k-animation-container, .k-editor, .k-dialog, .k-window, [role="dialog"], [role="menu"], .bc-sheet--open')) return

    if (active && active !== document.body && !active.closest('.bc-editor')) return

    if (e.key === 'Escape' && aPopupIsOpen()) return

    if (e.key === ' ' || e.key === 'Spacebar') return

    const ctrl = e.ctrlKey || e.metaKey
    const onBoard = !!active?.closest('.bc-board')

    let key = e.key
    if (e.shiftKey && !ctrl && e.code === 'Digit1') key = '1'
    if (ctrl && e.shiftKey && key === '+') key = '='

    const claimed = (onBoard && (e.key === 'Delete' || e.key === 'Backspace' || e.key.startsWith('Arrow') || e.key === 'Home'))
      || (onBoard && !ctrl && !e.altKey && BOARD_KEYS.includes(e.key))
      || (ctrl && !e.altKey && CTRL_KEYS.includes(e.key))
      || (ctrl && e.shiftKey && (e.key === 'l' || e.key === 'L'))
      // Ctrl+Shift+Arrow grows the next block beside this one; unclaimed it would drag the page's
      // text selection about instead.
      || (ctrl && e.shiftKey && e.key.startsWith('Arrow'))
    if (claimed) e.preventDefault()

    dotnetRef?.invokeMethodAsync('OnKeyDown', key, ctrl, e.shiftKey, e.altKey, onBoard)
      .catch(() => { /* the component may have been disposed */ })
  }

  document.addEventListener('keydown', handler)
}

export function unregister() {
  if (handler) {
    document.removeEventListener('keydown', handler)
    handler = null
    dotnetRef = null
  }
}
