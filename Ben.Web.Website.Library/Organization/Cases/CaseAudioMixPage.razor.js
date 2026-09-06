/**
 * CaseAudioMixPage.razor.js
 *
 * Two jobs: dragging a placed clip along its lane, and playing the mix so somebody can hear
 * what they are arranging before they render it.
 */

// ── Dragging ──────────────────────────────────────────────────────────────────

/**
 * Minimal single-element drag for repositioning a placed clip block within its track's
 * timeline lane. Position is tracked purely in JS while dragging (cheap, no per-pixel SignalR
 * round-trips); only the final offset is reported back to Blazor on pointerup.
 */
export function makeDraggable(blockId, pxPerSecond, dotNetRef) {
  const block = document.getElementById(blockId)
  if (!block || block.dataset.dragWired) return
  block.dataset.dragWired = '1'

  let dragging  = false
  let startX    = 0
  let startLeft = 0

  block.addEventListener('pointerdown', (e) => {
    // The remove button lives inside the block, and preventDefault here used to swallow the
    // click meant for it — so the ✕ could not be clicked at all, and a clip once placed could
    // not be taken off (2026-09-06 audio walk, finding K-remove). A press that starts on the ✕
    // is not a drag.
    if (e.target?.closest?.('.mix-clip-remove')) return

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

// ── Preview ───────────────────────────────────────────────────────────────────

/**
 * The browser twin of the server's AudioMixer: every audible clip scheduled at its own offset
 * through a gain and a pan, summed by the audio context.
 *
 * The transport was three buttons hard-disabled with a tooltip saying preview was not available,
 * so the only way to hear an arrangement was to render it, look at the result on the case page and
 * come back to change it (2026-09-06 audio walk, finding K-transport). Nothing here touches the
 * server: the clips are fetched once and decoded once, and moving a clip or a fader only changes
 * where the next play starts them.
 */
const previews = new Map()

/**
 * Fetches and decodes one clip, keeping it for as long as the page lives.
 *
 * A mono recording is widened to two identical channels first. StereoPannerNode applies a
 * DIFFERENT law to a mono input than to a stereo one — equal-power, which is 3 dB down at centre —
 * and the server's mixer treats a mono source as the same signal on both sides. Widening here is
 * what makes the preview and the export the same mix rather than two mixes three decibels apart.
 */
async function _buffer(state, url) {
  if (state.buffers.has(url)) return state.buffers.get(url)

  const pending = fetch(url, { credentials: 'include' })
    .then(r => (r.ok ? r.arrayBuffer() : Promise.reject(new Error(`HTTP ${r.status}`))))
    .then(bytes => state.ctx.decodeAudioData(bytes))
    .then(buffer => {
      if (buffer.numberOfChannels !== 1) return buffer
      const wide = state.ctx.createBuffer(2, buffer.length, buffer.sampleRate)
      const mono = buffer.getChannelData(0)
      wide.copyToChannel(mono, 0)
      wide.copyToChannel(mono, 1)
      return wide
    })

  state.buffers.set(url, pending)
  return pending
}

/**
 * Loads (or reuses) the audio for a set of clips and reports how many are ready.
 *
 * @param {string} key         identifies this page's preview
 * @param {Array}  clips       [{ url, offsetSeconds, gainDb, pan }]
 */
export async function preparePreview(key, clips) {
  let state = previews.get(key)
  if (!state) {
    state = { ctx: new (window.AudioContext || window.webkitAudioContext)(), buffers: new Map(), sources: [], startedAt: 0 }
    previews.set(key, state)
  }

  const results = await Promise.allSettled(clips.map(c => _buffer(state, c.url)))
  return results.filter(r => r.status === 'fulfilled').length
}

/**
 * Plays every clip at its offset. Returns the length of the arrangement in seconds, so the caller
 * can say when it will end without measuring anything itself.
 */
export async function playPreview(key, clips) {
  const state = previews.get(key)
  if (!state) return 0

  stopPreview(key)
  // A context created before any gesture starts suspended; a play button IS the gesture.
  if (state.ctx.state === 'suspended') await state.ctx.resume()

  let longest = 0
  const startAt = state.ctx.currentTime + 0.08   // a beat of headroom so clip one is not clipped

  for (const clip of clips) {
    let buffer
    try { buffer = await _buffer(state, clip.url) } catch { continue }
    if (!buffer) continue

    const source = state.ctx.createBufferSource()
    source.buffer = buffer

    const gain = state.ctx.createGain()
    gain.gain.value = Math.pow(10, (clip.gainDb ?? 0) / 20)

    // StereoPannerNode on a stereo input is exactly the law AudioMixer.PanCoefficients applies:
    // the identity at centre, and panning moves one channel across rather than turning it down.
    const panner = state.ctx.createStereoPanner()
    panner.pan.value = Math.max(-1, Math.min(1, clip.pan ?? 0))

    source.connect(gain).connect(panner).connect(state.ctx.destination)
    source.start(startAt + Math.max(0, clip.offsetSeconds ?? 0))

    state.sources.push(source)
    longest = Math.max(longest, (clip.offsetSeconds ?? 0) + buffer.duration)
  }

  state.startedAt = state.ctx.currentTime
  return longest
}

export async function pausePreview(key) {
  const state = previews.get(key)
  if (state && state.ctx.state === 'running') await state.ctx.suspend()
}

export async function resumePreview(key) {
  const state = previews.get(key)
  if (state && state.ctx.state === 'suspended') await state.ctx.resume()
}

export function stopPreview(key) {
  const state = previews.get(key)
  if (!state) return
  for (const source of state.sources) { try { source.stop() } catch { /* already ended */ } }
  state.sources = []
}

/** True while the context is actually running — what a test should ask rather than a class name. */
export function previewIsRunning(key) {
  return previews.get(key)?.ctx?.state === 'running'
}

export function disposePreview(key) {
  const state = previews.get(key)
  if (!state) return
  stopPreview(key)
  try { state.ctx.close() } catch { /* already closed */ }
  previews.delete(key)
}
