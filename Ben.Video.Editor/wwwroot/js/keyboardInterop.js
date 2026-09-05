/**
 * keyboardInterop.js
 *
 * Blazor JS-isolation module for KeyboardShortcutService.
 * Served at: /_content/Ben.Video.Editor/js/keyboardInterop.js
 *
 * Registers a single keydown listener on `document` and forwards events to the
 * provided DotNetObjectReference. Only one listener is active at a time.
 *
 * API
 * ───
 *  register(dotnetRef)   → void   Install keydown listener
 *  unregister()          → void   Remove listener (call on dispose)
 */

/**
 * Whether a Kendo popup is actually on screen.
 *
 * The editor keeps a dozen animation containers in the DOM at all times — every dropdown, menu and
 * picker has one — and all but the open ones are display:none. `offsetParent` cannot tell them
 * apart, because a popup is position:fixed and its offsetParent is always null. Client rects can:
 * a hidden container has none.
 */
function _aPopupIsOpen() {
  return [...document.querySelectorAll('.k-animation-container')].some(container =>
    container.getClientRects().length > 0
    && container.querySelector('.k-popup, .k-menu-group, .k-list')?.getClientRects().length > 0)
}

let _dotnetRef = null
let _handler   = null

/**
 * Register the document-level keydown handler.
 * Calling register() again replaces the previous registration.
 *
 * @param {DotNetObjectReference} dotnetRef  Reference to KeyboardShortcutService C# object
 */
export function register(dotnetRef) {
  unregister()

  _dotnetRef = dotnetRef

  _handler = (e) => {
    // Skip when focus is inside a text input, textarea, or contenteditable
    const tag = document.activeElement?.tagName?.toLowerCase()
    if (tag === 'input' || tag === 'textarea' || tag === 'select') return
    if (document.activeElement?.isContentEditable) return

    // Skip while a Telerik popup, dropdown, dialog or window owns the focus. Its own keyboard
    // handling comes first — Escape closing the File menu, arrows moving through its items — and
    // the editor stealing those keys is why Escape used to clear the timeline selection while
    // leaving the menu open (2026-09-05 audit, F10).
    if (document.activeElement?.closest(
          '.k-popup, .k-animation-container, .k-dialog, .k-window, [role="dialog"], [role="menu"]')) return

    // Escape with a popup open belongs to the popup even when focus never moved into it (Telerik's
    // DropDownButton keeps focus on its anchor). Nothing is forwarded, and an outside click closes
    // it — the component exposes no Open parameter to bind in 14.1.
    if (e.key === 'Escape' && _aPopupIsOpen()) {
      // Telerik's own outside-click close: its DropDownButton keeps focus on the anchor, so the
      // guard above does not catch it, and 14.1 exposes no Open parameter to bind. Dispatched on
      // document as the three events its components variously listen for.
      for (const type of ['pointerdown', 'mousedown', 'click']) {
        const Ctor = type === 'pointerdown' ? PointerEvent : MouseEvent
        document.dispatchEvent(new Ctor(type, { bubbles: true, cancelable: true }))
      }
      return
    }

    // The editor claims these, so the browser must not also act on them: Space scrolls the page,
    // Backspace navigates back in some setups, arrows scroll, and Cmd/Ctrl+Z reaches the browser's
    // own undo stack. Item #57 P5 established the arrow-key rule; the rest joined it once the keys
    // below were actually handled.
    const claimed = e.key === ' ' || e.key === 'Spacebar' || e.key === 'Backspace' || e.key === 'Delete'
      || e.key.startsWith('Arrow') || e.key === 'Home' || e.key === 'End'
      || ((e.ctrlKey || e.metaKey) && ['z', 'Z', 'y', 'Y'].includes(e.key))
    if (claimed) e.preventDefault()

    // Cmd counts as the modifier. Forwarding only ctrlKey meant undo and redo — the two shortcuts
    // every editor has — did nothing at all on a Mac, which is the platform this is developed on
    // (2026-09-05 audit, timeline-8).
    _dotnetRef.invokeMethodAsync('OnKeyDown', e.key, e.ctrlKey || e.metaKey, e.shiftKey, e.altKey)
      .catch(() => { /* component may have been disposed */ })
  }

  document.addEventListener('keydown', _handler)
}

/**
 * Remove the document-level keydown handler.
 * Safe to call multiple times or before register().
 */
export function unregister() {
  if (_handler) {
    document.removeEventListener('keydown', _handler)
    _handler   = null
    _dotnetRef = null
  }
}
