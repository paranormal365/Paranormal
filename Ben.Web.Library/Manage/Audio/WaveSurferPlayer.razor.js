/**
 * WaveSurferPlayer.razor.js
 *
 * Blazor JS-isolation module for the WaveSurferPlayer component.
 * Served at: /_content/Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor.js
 *
 * WaveSurfer ESM bundles are in the host WebApp at /js/wavesurfer/.
 * Build command (from Ben.Web.WebApp/wwwroot/ts/wavesurfer/):
 *   npm run build:blazor
 */

// Map of containerId → { ws, regionsPlugin, envelopePlugin, resizeObserver }
const instances = new Map()

// ── Lazy-loaded module cache ──────────────────────────────────────────────────
let WaveSurfer = null
let RegionsPlugin = null
let HoverPlugin = null
let TimelinePlugin = null
let ZoomPlugin = null
let MinimapPlugin = null
let SpectrogramPlugin = null
let SpectrogramWindowedPlugin = null
let EnvelopePlugin = null

async function loadCore() {
  if (!WaveSurfer) {
    const mod = await import('/js/wavesurfer/wavesurfer.esm.js')
    WaveSurfer = mod.default
  }
}

async function ensurePlugin(flag, path, cache) {
  if (flag && !cache.value) {
    const mod = await import(path)
    cache.value = mod.default
  }
}

// ── Telerik theme color resolution ────────────────────────────────────────────

/**
 * Reads Telerik Kendo CSS custom properties from the document root and returns
 * sensible WaveSurfer color defaults. Falls back to universal values when the
 * CSS variables are not present (non-Telerik host or SSR).
 *
 * @returns {{ waveColor: string, progressColor: string, cursorColor: string }}
 */
function resolveTelerikColors() {
  const style = getComputedStyle(document.documentElement)

  const get = (v) => style.getPropertyValue(v).trim()

  // Telerik CSS variable names (available in all Kendo themes)
  const primary = get('--kendo-color-primary')
  const primaryEmphasis = get('--kendo-color-primary-emphasis') || get('--kendo-color-primary-on-surface')
  const bodyText = get('--kendo-body-text') || get('--kendo-color-on-base')
  const bodyBg = get('--kendo-body-bg') || get('--kendo-color-base')

  // Detect dark mode: if the background is "dark" the body text will be light
  const isDark = (() => {
    if (!bodyBg) return false
    // Parse hex or rgb; resolve CSS vars that are already hex
    const hex = bodyBg.startsWith('#') ? bodyBg : null
    if (hex && hex.length >= 7) {
      const r = parseInt(hex.slice(1, 3), 16)
      const g = parseInt(hex.slice(3, 5), 16)
      const b = parseInt(hex.slice(5, 7), 16)
      // Perceived luminance
      return (0.299 * r + 0.587 * g + 0.114 * b) < 128
    }
    return false
  })()

  return {
    waveColor: primary || (isDark ? '#93C5FD' : '#3B82F6'),
    progressColor: primaryEmphasis || (isDark ? '#2563EB' : '#1D4ED8'),
    cursorColor: bodyText || (isDark ? '#F1F5F9' : '#1E293B'),
  }
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Initialise a WaveSurfer instance.
 *
 * @param {string}               containerId  id of the container element
 * @param {object}               options      WsOptions (serialised from C#; null colors resolved from theme)
 * @param {object}               plugins      WsPluginConfig (serialised from C#)
 * @param {DotNetObjectReference} dotnetRef   .NET callback reference
 * @param {string|null}          audioUrl     Pre-resolved URL or data URL (resolved in C# from WsAudioSource)
 */
export async function create(containerId, options, plugins, dotnetRef, audioUrl) {
  if (instances.has(containerId)) {
    destroy(containerId)
  }

  await loadCore()

  // Load only the requested plugin modules in parallel
  const caches = {
    regions:              { value: RegionsPlugin },
    hover:                { value: HoverPlugin },
    timeline:             { value: TimelinePlugin },
    zoom:                 { value: ZoomPlugin },
    minimap:              { value: MinimapPlugin },
    spectrogram:          { value: SpectrogramPlugin },
    spectrogramWindowed:  { value: SpectrogramWindowedPlugin },
    envelope:             { value: EnvelopePlugin },
  }

  await Promise.all([
    ensurePlugin(plugins.regions,             '/js/wavesurfer/plugins/regions.esm.js',              caches.regions),
    ensurePlugin(plugins.hover,               '/js/wavesurfer/plugins/hover.esm.js',                caches.hover),
    ensurePlugin(plugins.timeline,            '/js/wavesurfer/plugins/timeline.esm.js',             caches.timeline),
    ensurePlugin(plugins.zoom,                '/js/wavesurfer/plugins/zoom.esm.js',                 caches.zoom),
    ensurePlugin(plugins.minimap,             '/js/wavesurfer/plugins/minimap.esm.js',              caches.minimap),
    ensurePlugin(plugins.spectrogram,         '/js/wavesurfer/plugins/spectrogram.esm.js',          caches.spectrogram),
    ensurePlugin(plugins.spectrogramWindowed, '/js/wavesurfer/plugins/spectrogram-windowed.esm.js', caches.spectrogramWindowed),
    ensurePlugin(plugins.envelope,            '/js/wavesurfer/plugins/envelope.esm.js',             caches.envelope),
  ])

  // Update module-level caches
  if (caches.regions.value)             RegionsPlugin             = caches.regions.value
  if (caches.hover.value)               HoverPlugin               = caches.hover.value
  if (caches.timeline.value)            TimelinePlugin            = caches.timeline.value
  if (caches.zoom.value)                ZoomPlugin                = caches.zoom.value
  if (caches.minimap.value)             MinimapPlugin             = caches.minimap.value
  if (caches.spectrogram.value)         SpectrogramPlugin         = caches.spectrogram.value
  if (caches.spectrogramWindowed.value) SpectrogramWindowedPlugin = caches.spectrogramWindowed.value
  if (caches.envelope.value)            EnvelopePlugin            = caches.envelope.value

  // Build plugin instances
  const wsPlugins = []
  let regionsPlugin = null
  let envelopePlugin = null

  if (plugins.regions && RegionsPlugin) {
    regionsPlugin = RegionsPlugin.create({ dragToCreate: plugins.regionsDragToCreate ?? false })
    wsPlugins.push(regionsPlugin)
  }
  if (plugins.hover && HoverPlugin)
    wsPlugins.push(HoverPlugin.create(plugins.hoverOptions ?? {}))
  if (plugins.timeline && TimelinePlugin)
    wsPlugins.push(TimelinePlugin.create(plugins.timelineOptions ?? {}))
  if (plugins.zoom && ZoomPlugin)
    wsPlugins.push(ZoomPlugin.create(plugins.zoomOptions ?? {}))
  if (plugins.minimap && MinimapPlugin)
    wsPlugins.push(MinimapPlugin.create(plugins.minimapOptions ?? { height: 50 }))
  if (plugins.spectrogram && SpectrogramPlugin)
    wsPlugins.push(SpectrogramPlugin.create(plugins.spectrogramOptions ?? {}))
  if (plugins.spectrogramWindowed && SpectrogramWindowedPlugin)
    wsPlugins.push(SpectrogramWindowedPlugin.create(plugins.spectrogramWindowedOptions ?? {}))
  if (plugins.envelope && EnvelopePlugin) {
    envelopePlugin = EnvelopePlugin.create(plugins.envelopeOptions ?? {})
    wsPlugins.push(envelopePlugin)
  }

  const container = document.getElementById(containerId)
  if (!container) throw new Error(`WaveSurferPlayer: container #${containerId} not found`)

  // Resolve theme colors for any null color options
  const themeColors = resolveTelerikColors()

  // Build the WaveSurfer options object; strip undefined/null values so WaveSurfer
  // applies its own defaults. Colors fall back to Telerik theme values.
  const wsOptions = Object.fromEntries(
    Object.entries({
      container,
      height:        options.height ?? 'auto',  // 'auto' = fills container
      width:         options.width,
      waveColor:     options.waveColor     ?? themeColors.waveColor,
      progressColor: options.progressColor ?? themeColors.progressColor,
      cursorColor:   options.cursorColor   ?? themeColors.cursorColor,
      cursorWidth:   options.cursorWidth,
      barWidth:      options.barWidth,
      barGap:        options.barGap,
      barRadius:     options.barRadius,
      barHeight:     options.barHeight,
      barAlign:      options.barAlign,
      barMinHeight:  options.barMinHeight,
      minPxPerSec:   options.minPxPerSec,
      fillParent:    options.fillParent  ?? true,
      interact:      options.interact    ?? true,
      dragToSeek:    options.dragToSeek,
      hideScrollbar: options.hideScrollbar,
      audioRate:     options.audioRate,
      autoScroll:    options.autoScroll  ?? true,
      autoCenter:    options.autoCenter  ?? true,
      normalize:     options.normalize,
      sampleRate:    options.sampleRate,
      backend:       options.backend,
      mediaControls: options.mediaControls,
      autoplay:      options.autoplay,
    }).filter(([, v]) => v !== null && v !== undefined),
  )

  const ws = WaveSurfer.create({ ...wsOptions, plugins: wsPlugins })

  // ── Core event forwarding ────────────────────────────────────────────────
  const safe = (fn) => fn.catch(() => {})

  ws.on('ready',      (duration)  => safe(dotnetRef.invokeMethodAsync('OnWsReady',      duration)))
  ws.on('play',       ()          => safe(dotnetRef.invokeMethodAsync('OnWsPlay')))
  ws.on('pause',      ()          => safe(dotnetRef.invokeMethodAsync('OnWsPause')))
  ws.on('finish',     ()          => safe(dotnetRef.invokeMethodAsync('OnWsFinish')))
  ws.on('timeupdate', (time)      => safe(dotnetRef.invokeMethodAsync('OnWsTimeUpdate', time)))
  ws.on('loading',    (percent)   => safe(dotnetRef.invokeMethodAsync('OnWsLoading',    percent)))
  ws.on('error',      (err)       => safe(dotnetRef.invokeMethodAsync('OnWsError',      err?.message ?? String(err))))
  ws.on('zoom',       (pps)       => safe(dotnetRef.invokeMethodAsync('OnWsZoom',       pps)))
  ws.on('seeking',    (time)      => safe(dotnetRef.invokeMethodAsync('OnWsSeeking',    time)))

  // ── Regions events ───────────────────────────────────────────────────────
  if (regionsPlugin) {
    const rd = (r) => ({ id: r.id, start: r.start, end: r.end, color: r.color, label: r.content ?? null })
    regionsPlugin.on('region-created', (r)  => {
      safe(dotnetRef.invokeMethodAsync('OnWsRegionCreated', rd(r)))
      // Attach right-click (contextmenu) listener so the host can show a context menu
      if (r.element) {
        r.element.addEventListener('contextmenu', (e) => {
          e.preventDefault()
          e.stopPropagation()
          safe(dotnetRef.invokeMethodAsync('OnWsRegionContextMenu',
            { id: r.id, start: r.start, end: r.end, label: r.content ?? null, clientX: e.clientX, clientY: e.clientY }))
        })
      }
    })
    regionsPlugin.on('region-updated', (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionUpdated', rd(r))))
    regionsPlugin.on('region-removed', (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionRemoved', r.id)))
    regionsPlugin.on('region-clicked', (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionClicked', r.id)))
    regionsPlugin.on('region-in',      (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionIn',      r.id)))
    regionsPlugin.on('region-out',     (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionOut',     r.id)))
  }

  // ── Envelope events ──────────────────────────────────────────────────────
  if (envelopePlugin) {
    envelopePlugin.on('points-change',  (pts)    => safe(dotnetRef.invokeMethodAsync('OnWsEnvelopePointsChanged',  pts)))
    envelopePlugin.on('volume-change',  (volume) => safe(dotnetRef.invokeMethodAsync('OnWsEnvelopeVolumeChanged', volume)))
  }

  // ── ResizeObserver — keeps WaveSurfer in sync with CSS resize ───────────
  // WaveSurfer 7 has an internal ResizeObserver for fillParent, but attaching
  // our own ensures the waveform redraws promptly after a manual CSS resize.
  let resizeTimer = null
  const resizeObserver = new ResizeObserver(() => {
    clearTimeout(resizeTimer)
    resizeTimer = setTimeout(() => {
      const inst = instances.get(containerId)
      if (inst) {
        // Re-apply height:'auto' to trigger internal re-measure
        inst.ws.setOptions({ height: 'auto' })
      }
    }, 50)
  })
  resizeObserver.observe(container.parentElement ?? container)

  instances.set(containerId, { ws, regionsPlugin, envelopePlugin, resizeObserver })

  if (audioUrl) {
    await ws.load(audioUrl)
  }
}

// ── Playback controls ─────────────────────────────────────────────────────────

export function play(containerId)      { return instances.get(containerId)?.ws?.play()      ?? Promise.resolve() }
export function pause(containerId)     {        instances.get(containerId)?.ws?.pause() }
export function playPause(containerId) { return instances.get(containerId)?.ws?.playPause() ?? Promise.resolve() }

export function stop(containerId) {
  const ws = instances.get(containerId)?.ws
  if (ws) { ws.pause(); ws.seekTo(0) }
}

export function seekTo(containerId, progress)  { instances.get(containerId)?.ws?.seekTo(Math.max(0, Math.min(1, progress))) }
export function setVolume(containerId, volume) { instances.get(containerId)?.ws?.setVolume(Math.max(0, Math.min(1, volume))) }
export function setMuted(containerId, muted)   { instances.get(containerId)?.ws?.setMuted(muted) }
export function setPlaybackRate(containerId, rate) { instances.get(containerId)?.ws?.setPlaybackRate(rate) }
export function setZoom(containerId, minPxPerSec)  { instances.get(containerId)?.ws?.zoom(minPxPerSec) }

export async function load(containerId, url) {
  const ws = instances.get(containerId)?.ws
  if (ws) await ws.load(url)
}

// ── Getters ───────────────────────────────────────────────────────────────────

export function isPlaying(containerId)    { return instances.get(containerId)?.ws?.isPlaying()    ?? false }
export function getCurrentTime(containerId) { return instances.get(containerId)?.ws?.getCurrentTime() ?? 0 }
export function getDuration(containerId)  { return instances.get(containerId)?.ws?.getDuration()   ?? 0 }
export function getVolume(containerId)    { return instances.get(containerId)?.ws?.getVolume()     ?? 1 }

// ── Regions API ───────────────────────────────────────────────────────────────

export function addRegion(containerId, params) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  if (!regionsPlugin) return null
  const r = regionsPlugin.addRegion({
    id:        params.id,
    start:     params.start,
    end:       params.end,
    color:     params.color ?? 'rgba(0,0,0,0.1)',
    drag:      params.drag  ?? true,
    resize:    params.resize ?? true,
    content:   params.content,
    minLength: params.minLength,
    maxLength: params.maxLength,
  })
  return r.id
}

export function removeRegion(containerId, regionId) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  regionsPlugin?.getRegions().find((r) => r.id === regionId)?.remove()
}

export function clearRegions(containerId) {
  instances.get(containerId)?.regionsPlugin?.clearRegions()
}

export function getRegions(containerId) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  return regionsPlugin?.getRegions().map((r) => ({ id: r.id, start: r.start, end: r.end, color: r.color })) ?? []
}

export function playRegion(containerId, regionId) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  regionsPlugin?.getRegions().find((r) => r.id === regionId)?.play()
}

export function updateRegionLabel(containerId, regionId, label) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  const region = regionsPlugin?.getRegions().find((r) => r.id === regionId)
  if (region) region.setOptions({ content: label ?? '' })
}

// ── Envelope API ──────────────────────────────────────────────────────────────

export function setEnvelopePoints(containerId, points)  { instances.get(containerId)?.envelopePlugin?.setPoints(points) }
export function addEnvelopePoint(containerId, point)    { instances.get(containerId)?.envelopePlugin?.addPoint(point) }
export function removeEnvelopePoint(containerId, id)    { instances.get(containerId)?.envelopePlugin?.removePoint(id) }
export function setEnvelopeVolume(containerId, volume)  { instances.get(containerId)?.envelopePlugin?.setVolume(volume) }
export function getEnvelopePoints(containerId)          { return instances.get(containerId)?.envelopePlugin?.getPoints() ?? [] }

// ── Spectrogram (Web-Worker FFT) ─────────────────────────────────────────────

/**
 * Enables or disables the custom canvas spectrogram for the given player.
 * When enabling, a Web Worker computes FFT data; progress/ready events fire
 * back through the dotnetRef so Blazor can update its UI if desired.
 * The loading-progress text is managed purely in the DOM for performance.
 *
 * @param {string}               containerId  Player container ID
 * @param {boolean}              enable       true = show, false = remove
 * @param {boolean}              showLabels   Render frequency-axis labels
 * @param {DotNetObjectReference} dotnetRef   Used to fire progress/ready/menu callbacks
 */
export async function toggleSpectrogram(containerId, enable, showLabels, dotnetRef) {
  const instance = instances.get(containerId)
  if (!instance) return

  const canvasId = `${containerId}-spectro`

  if (!enable) {
    document.getElementById(`${canvasId}-wrapper`)?.remove()
    instance.spectrogramWorker?.terminate()
    delete instance.spectrogramWorker
    delete instance.spectrogramData
    delete instance.spectrogramMeta
    return
  }

  // Already computed — rebuild the canvas from cached data
  if (instance.spectrogramData) {
    _ensureSpectrogramCanvas(canvasId, containerId, dotnetRef)
    const canvas = document.getElementById(canvasId)
    if (canvas) {
      _drawSpectrogram(canvas, instance.spectrogramData, showLabels,
        instance.spectrogramMeta.sampleRate, instance.spectrogramMeta.fftSamples)
      _hideSpectrogramLoading(canvasId)
    }
    return
  }

  const ws          = instance.ws
  const audioBuffer = ws.getDecodedData?.()
  if (!audioBuffer) {
    console.warn('WaveSurfer audio not yet decoded — spectrogram unavailable')
    return
  }

  // Create wrapper + loading text + canvas in the DOM
  const canvas = _ensureSpectrogramCanvas(canvasId, containerId, dotnetRef)
  if (!canvas) return

  const fftSamples = 512
  const noverlap   = 256
  const channelCopy = new Float32Array(audioBuffer.getChannelData(0))

  const worker = new Worker('/js/wavesurfer/spectrogram-worker.js')
  instance.spectrogramWorker = worker
  instance.spectrogramMeta   = { sampleRate: audioBuffer.sampleRate, fftSamples }

  const safe = (fn) => fn.catch(() => {})

  worker.onmessage = (e) => {
    if (e.data.type === 'progress') {
      _updateSpectrogramLoading(canvasId, e.data.percent)
      safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramProgress', e.data.percent))
    } else if (e.data.type === 'done') {
      instance.spectrogramData = e.data.data
      const c = document.getElementById(canvasId)
      if (c) _drawSpectrogram(c, e.data.data, showLabels, e.data.sampleRate, e.data.fftSamples)
      _hideSpectrogramLoading(canvasId)
      safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramReady'))
      worker.terminate()
      delete instance.spectrogramWorker
    }
  }
  worker.onerror = () => {
    _hideSpectrogramLoading(canvasId)
    safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramReady'))
  }

  worker.postMessage(
    { channels: [channelCopy], sampleRate: audioBuffer.sampleRate, fftSamples, noverlap },
    [channelCopy.buffer]
  )
}

/**
 * Redraws the spectrogram canvas with the cached FFT data, toggling labels.
 */
export function updateSpectrogramLabels(containerId, showLabels) {
  const instance = instances.get(containerId)
  if (!instance?.spectrogramData) return
  const canvas = document.getElementById(`${containerId}-spectro`)
  if (!canvas) return
  _drawSpectrogram(canvas, instance.spectrogramData, showLabels,
    instance.spectrogramMeta.sampleRate, instance.spectrogramMeta.fftSamples)
}

// ── Internal helpers ──────────────────────────────────────────────────────────

function _ensureSpectrogramCanvas(canvasId, containerId, dotnetRef) {
  if (document.getElementById(canvasId)) return document.getElementById(canvasId)

  const waveformEl = document.getElementById(containerId)
  if (!waveformEl) return null

  const wrapper  = document.createElement('div')
  wrapper.id     = `${canvasId}-wrapper`
  wrapper.style.cssText = 'position:relative;width:100%;'

  const loadingEl = document.createElement('div')
  loadingEl.id    = `${canvasId}-loading`
  loadingEl.style.cssText = 'font-size:0.72rem;color:var(--kendo-color-subtle-text,#888);font-style:italic;padding:2px 4px;'
  loadingEl.textContent   = 'Generating spectrogram… 0%'

  const canvas = document.createElement('canvas')
  canvas.id    = canvasId
  canvas.style.cssText = 'display:block;width:100%;height:128px;cursor:context-menu;'

  wrapper.appendChild(loadingEl)
  wrapper.appendChild(canvas)

  // Insert immediately after the waveform container
  waveformEl.parentNode.insertBefore(wrapper, waveformEl.nextSibling)

  // Right-click → context menu
  const safe = (fn) => fn.catch(() => {})
  canvas.addEventListener('contextmenu', (ev) => {
    ev.preventDefault()
    ev.stopPropagation()
    safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramContextMenu', ev.clientX, ev.clientY))
  })

  return canvas
}

function _updateSpectrogramLoading(canvasId, percent) {
  const el = document.getElementById(`${canvasId}-loading`)
  if (el) el.textContent = `Generating spectrogram… ${percent}%`
}

function _hideSpectrogramLoading(canvasId) {
  const el = document.getElementById(`${canvasId}-loading`)
  if (el) el.style.display = 'none'
}

function _drawSpectrogram(canvas, data, showLabels, sampleRate, fftSamples) {
  if (!data?.length) return

  const W      = canvas.parentElement?.offsetWidth || 800
  const H      = 128
  canvas.width  = W
  canvas.height = H

  const ctx     = canvas.getContext('2d')
  const nFrames = data.length
  const nBins   = fftSamples / 2
  const colW    = W / nFrames

  // Normalise over the entire dataset
  let maxMag = 1e-9
  for (const frame of data)
    for (const v of frame)
      if (v > maxMag) maxMag = v

  for (let x = 0; x < nFrames; x++) {
    const frame = data[x]
    for (let y = 0; y < nBins; y++) {
      const t  = frame[y] / maxMag                 // 0..1
      // Viridis-inspired: dark-navy → cyan → yellow
      const r  = Math.floor(255 * Math.pow(t, 0.5))
      const g  = Math.floor(255 * Math.min(1, t * 1.5))
      const b  = Math.floor(255 * Math.max(0, 0.9 - t))
      ctx.fillStyle = `rgb(${r},${g},${b})`
      const cy = H - Math.floor((y / nBins) * H) - 1
      ctx.fillRect(x * colW, cy, colW + 1, 1)
    }
  }

  if (showLabels) {
    const nyquist = sampleRate / 2
    ctx.font      = '10px monospace'
    ctx.textAlign = 'left'
    for (const freq of [250, 500, 1000, 2000, 4000, 8000, 16000]) {
      if (freq >= nyquist) continue
      const y = H - Math.floor((freq / nyquist) * H)
      ctx.strokeStyle = 'rgba(255,255,255,0.3)'
      ctx.setLineDash([3, 3])
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke()
      ctx.setLineDash([])
      ctx.fillStyle = 'rgba(255,255,255,0.85)'
      const label = freq >= 1000 ? `${freq / 1000}kHz` : `${freq}Hz`
      ctx.fillText(label, 4, y - 2)
    }
  }
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

export function destroy(containerId) {
  const instance = instances.get(containerId)
  if (!instance) return
  instance.resizeObserver?.disconnect()
  try { instance.ws?.destroy() } catch { /* ignore */ }
  instances.delete(containerId)
}
