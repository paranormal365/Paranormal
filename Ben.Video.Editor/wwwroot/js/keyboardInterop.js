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

    // Item #57 P5 — arrow keys drive canvas-item nudge; without this the browser's own default
    // (scrolling the page) fires alongside every nudge, which would be a real, visible regression
    // for a feature whose entire point is precise, page-stable positioning.
    if (e.key.startsWith('Arrow')) e.preventDefault()

    // Forward to C#
    _dotnetRef.invokeMethodAsync('OnKeyDown', e.key, e.ctrlKey, e.shiftKey, e.altKey)
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
