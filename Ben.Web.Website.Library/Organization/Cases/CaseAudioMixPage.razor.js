/**
 * CaseAudioMixPage.razor.js
 *
 * Minimal single-element drag for repositioning a placed clip block within its
 * track's timeline lane. Position is tracked purely in JS while dragging (cheap,
 * no per-pixel SignalR round-trips); only the final offset is reported back to
 * Blazor on pointerup.
 */

export function makeDraggable(blockId, pxPerSecond, dotNetRef) {
  const block = document.getElementById(blockId)
  if (!block || block.dataset.dragWired) return
  block.dataset.dragWired = '1'

  let dragging  = false
  let startX    = 0
  let startLeft = 0

  block.addEventListener('pointerdown', (e) => {
    dragging  = true
    startX    = e.clientX
    startLeft = parseFloat(block.style.left || '0')
    block.setPointerCapture(e.pointerId)
    e.preventDefault()
  })

  block.addEventListener('pointermove', (e) => {
    if (!dragging) return
    const newLeft = Math.max(0, startLeft + (e.clientX - startX))
    block.style.left = `${newLeft}px`
  })

  block.addEventListener('pointerup', (e) => {
    if (!dragging) return
    dragging = false
    block.releasePointerCapture(e.pointerId)
    const offsetSeconds = parseFloat(block.style.left || '0') / pxPerSecond
    dotNetRef.invokeMethodAsync('OnClipMoved', block.dataset.clipId, offsetSeconds)
  })
}
