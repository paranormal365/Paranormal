/**
 * waveformInterop.js
 *
 * Blazor JS-isolation module for the AudioWaveform component.
 * Served at: /_content/Ben.Video.Editor/js/waveformInterop.js
 *
 * Depends on WaveSurfer.js v7 UMD loaded globally by the host (window.WaveSurfer).
 * AverageBen host loads it from: /js/wavesurfer/wavesurfer.esm.js (build artefact).
 * Ben.Video.App dev shell loads it from CDN (see index.html).
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
export function create(containerId, blobUrl, peaks, options, dotnetRef) {
  if (_instances.has(containerId)) {
    destroy(containerId)
  }

  const WS = window.WaveSurfer
  if (!WS) {
    console.error('[waveformInterop] WaveSurfer not loaded globally. Add the CDN script to index.html.')
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
