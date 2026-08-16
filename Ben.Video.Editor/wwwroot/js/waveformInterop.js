/**
 * waveformInterop.js
 *
 * Blazor JS-isolation module for the AudioWaveform component.
 * Served at: /_content/Ben.Video.Editor/js/waveformInterop.js
 *
 * Loads WaveSurfer itself, from the copy shipped beside this file
 * (/_content/Ben.Video.Editor/js/wavesurfer.esm.js).
 *
 * It used to expect the host to have put a UMD build on window.WaveSurfer, which every host did by
 * fetching it from unpkg on a floating @7 tag: a third-party runtime dependency for a core feature,
 * broken offline, and free to change under us. Owning the file removes all three, and removes the
 * ordering trap where this module could run before the host's script tag had finished.
 *
 * API summary
 * ───────────
 *  create(containerId, blobUrl, peaks, options, dotnetRef)  → void
 *  play(containerId)                                         → void
 *  pause(containerId)                                        → void
 *  seek(containerId, progress)                               → void   0–1
 *  setVolume(containerId, volume)                            → void   0–1
 *  destroy(containerId)                                      → void
 *  getPeaks(containerId, samples)                            → float[]
 */

// containerId → WaveSurfer instance
const _instances = new Map()

/**
 * WaveSurfer, loaded once and shared. Deferred to first use rather than fetched with this module,
 * so pages that never show a waveform never pay for it. The path is relative to this file, so it
 * resolves under whichever host is serving the RCL without either of them configuring anything.
 */
let _wsPromise = null
function _loadWaveSurfer() {
  _wsPromise ??= import('./wavesurfer.esm.js')
    .then(m => m.default)
    .catch(err => {
      console.error('[waveformInterop] could not load wavesurfer.esm.js', err)
      _wsPromise = null   // let a later attempt retry rather than caching the failure forever
      return null
    })
  return _wsPromise
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function _get(containerId) {
  return _instances.get(containerId) ?? null
}

/**
 * Resolve theme-aware waveform colours from Telerik/Kendo CSS custom properties.
 * Falls back to sensible dark-mode defaults when the host doesn't expose them.
 */
function _resolveColors() {
  const style    = getComputedStyle(document.documentElement)
  const primary  = style.getPropertyValue('--kendo-color-primary').trim()
  const bodyText = style.getPropertyValue('--kendo-body-text').trim()
              || style.getPropertyValue('--kendo-color-on-base').trim()
  return {
    waveColor:     primary    || '#6c8ebf',
    progressColor: primary    || '#3a5fa0',
    cursorColor:   bodyText   || '#cdd6f4',
  }
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Create and mount a WaveSurfer instance.
 *
 * @param {string}               containerId  id of the container element
 * @param {string}               blobUrl      object URL for the audio data
 * @param {number[]|null}        peaks        pre-computed peak data (optional)
 * @param {object}               options      { height, showControls, waveColor, progressColor }
 * @param {DotNetObjectReference} dotnetRef   .NET callbacks (OnReady, OnTimeUpdate, OnFinish)
 */
export async function create(containerId, blobUrl, peaks, options, dotnetRef) {
  if (_instances.has(containerId)) {
    destroy(containerId)
  }

  const WS = await _loadWaveSurfer()
  if (!WS) {
    console.error('[waveformInterop] WaveSurfer failed to load from _content/Ben.Video.Editor/js/wavesurfer.esm.js')
    return
  }

  const colors = _resolveColors()

  const wsOptions = {
    container:     `#${containerId}`,
    // 'auto' means let CSS control the height (used in mini/chip mode where Height=0)
    height:        (options?.height === 'auto' || options?.height == null) ? 'auto' : options.height,
    waveColor:     options?.waveColor     ?? colors.waveColor,
    progressColor: options?.progressColor ?? colors.progressColor,
    cursorColor:   options?.cursorColor   ?? colors.cursorColor,
    cursorWidth:   1,
    barWidth:      2,
    barGap:        1,
    barRadius:     2,
    normalize:     true,
    interact:      options?.showControls  ?? true,
    url:           blobUrl,
  }

  // Supply pre-computed peaks when available (avoids re-decode)
  if (peaks && peaks.length > 0) {
    wsOptions.peaks = [peaks]
    wsOptions.duration = options?.duration ?? undefined
  }

  const ws = WS.create(wsOptions)

  ws.on('ready', (dur) => {
    dotnetRef?.invokeMethodAsync('OnWaveformReady', dur)
  })

  ws.on('timeupdate', (t) => {
    dotnetRef?.invokeMethodAsync('OnWaveformTimeUpdate', t)
  })

  ws.on('finish', () => {
    dotnetRef?.invokeMethodAsync('OnWaveformFinish')
  })

  ws.on('error', (err) => {
    console.warn('[waveformInterop] WaveSurfer error:', err)
    dotnetRef?.invokeMethodAsync('OnWaveformError', String(err))
  })

  _instances.set(containerId, ws)
}

/** Start or resume playback. */
export function play(containerId) {
  _get(containerId)?.play()
}

/** Pause playback. */
export function pause(containerId) {
  _get(containerId)?.pause()
}

/**
 * Seek to a position.
 * @param {number} progress  0–1 fraction of total duration
 */
export function seek(containerId, progress) {
  _get(containerId)?.seekTo(Math.max(0, Math.min(1, progress)))
}

/**
 * Set playback volume.
 * @param {number} volume  0–1
 */
export function setVolume(containerId, volume) {
  _get(containerId)?.setVolume(Math.max(0, Math.min(1, volume)))
}

/**
 * Extract peak data from the decoded audio (useful to cache in AudioClip.WaveformPeaks).
 * @param {number} samples  Number of peak values to return (default 200)
 * @returns {number[]}
 */
export function getPeaks(containerId, samples) {
  const ws = _get(containerId)
  if (!ws) return []
  return ws.exportPeaks({ channels: 1, maxLength: samples ?? 200, precision: 5 })[0] ?? []
}

/**
 * Destroy the WaveSurfer instance and release resources.
 * Call this when the Blazor component is disposed.
 */
export function destroy(containerId) {
  const ws = _instances.get(containerId)
  if (ws) {
    try { ws.destroy() } catch { /* already destroyed */ }
    _instances.delete(containerId)
  }
}
