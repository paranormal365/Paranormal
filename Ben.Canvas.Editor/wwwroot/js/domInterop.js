// Small page-level helpers: the leave-page guard, flushing on hide, and downloads.
//
// Copied in intent from Ben.Video.Editor's domInterop.js. Every lookup is guarded, because a helper that
// throws on a missing element blanks the editor at the moment someone is trying to leave with their work.

let unloadGuard = null

export function setUnloadGuard(enabled, reason) {
  if (unloadGuard) {
    window.removeEventListener('beforeunload', unloadGuard)
    unloadGuard = null
  }
  if (!enabled) return
  unloadGuard = (e) => {
    e.preventDefault()
    e.returnValue = reason || ''
    return reason || ''
  }
  window.addEventListener('beforeunload', unloadGuard)
}

let hideHandlers = null

// pagehide covers closing and navigating away; visibilitychange covers the iPhone's app switcher, where
// pagehide may never come. beforeunload is not used for saving: it does not fire when the page is restored
// from the back-forward cache, and a handler on it takes the page out of that cache.
export function flushOnPageHide(dotnet) {
  stopFlushOnPageHide()
  const flush = () => { dotnet.invokeMethodAsync('OnPageHiding').catch(() => {}) }
  const onVisibility = () => { if (document.visibilityState === 'hidden') flush() }
  window.addEventListener('pagehide', flush)
  document.addEventListener('visibilitychange', onVisibility)
  hideHandlers = { flush, onVisibility }
}

export function stopFlushOnPageHide() {
  if (!hideHandlers) return
  window.removeEventListener('pagehide', hideHandlers.flush)
  document.removeEventListener('visibilitychange', hideHandlers.onVisibility)
  hideHandlers = null
}

export function isCoarsePointer() {
  try { return window.matchMedia('(pointer: coarse)').matches } catch { return false }
}

// A transient link is the only way a page starts a download on desktop browsers. It is not a board node and
// is gone before the next frame. Touch devices use the Blazor-rendered Download link instead, because iOS
// starts a download only from a tap.
export function downloadUrl(url, fileName) {
  if (!url) return false
  const link = document.createElement('a')
  link.href = url
  link.download = fileName || 'board.ishcanvas'
  link.rel = 'noopener'
  link.style.display = 'none'
  document.body.appendChild(link)
  link.click()
  link.remove()
  return true
}

// Safari on iOS reads a blob: URL after the click returns, so it is revoked well afterwards.
export function revokeUrlLater(url, milliseconds) {
  if (!url) return
  setTimeout(() => URL.revokeObjectURL(url), milliseconds || 60000)
}

export function clickElement(el) {
  if (el && typeof el.click === 'function') el.click()
}

// Tells .NET which of the given media queries match, now and whenever one changes. CSS already chooses what
// is visible at first paint; this only feeds behaviour, such as whether properties open as a sheet.
export function watchMedia(queries, dotnet) {
  const lists = queries.map(q => window.matchMedia(q))
  const report = () => { dotnet.invokeMethodAsync('OnMediaChanged', lists.map(l => l.matches)).catch(() => {}) }
  for (const list of lists) list.addEventListener('change', report)
  report()
  return { lists, report }
}

export function unwatchMedia(handle) {
  if (!handle || !handle.lists) return
  for (const list of handle.lists) list.removeEventListener('change', handle.report)
}