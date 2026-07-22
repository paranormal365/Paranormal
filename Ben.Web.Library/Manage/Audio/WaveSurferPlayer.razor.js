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

// Timeline is hidden automatically when the player is shorter than this.
const TIMELINE_MIN_HEIGHT = 150   // px

/**
 * Show or hide the external timeline bar, respecting both the user's explicit
 * preference (_timelineEnabled on the instance) and the available height.
 * Called on create, on every resize, and when the user clicks the toggle.
 */
function _syncTimelineVisibility(containerId) {
  const instance = instances.get(containerId)
  const tlEl     = document.getElementById(containerId + '-tl')
  if (!tlEl) return

  const playerEl  = document.getElementById(containerId)?.parentElement
  const hasRoom   = playerEl ? playerEl.offsetHeight >= TIMELINE_MIN_HEIGHT : false
  const userWants = instance ? instance._timelineEnabled !== false : true

  tlEl.style.display = (hasRoom && userWants) ? '' : 'none'
}

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
    regionsPlugin = RegionsPlugin.create()
    // dragToCreate is implemented manually below for reliable seek/draw separation
    wsPlugins.push(regionsPlugin)
  }
  if (plugins.hover && HoverPlugin)
    wsPlugins.push(HoverPlugin.create(plugins.hoverOptions ?? {}))
  if (plugins.timeline && TimelinePlugin) {
    // Render the timeline in the dedicated external div so it is never clipped
    // by the waveform container's overflow:hidden.
    const tlEl = document.getElementById(containerId + '-tl')
    wsPlugins.push(TimelinePlugin.create({
      ...(plugins.timelineOptions ?? {}),
      ...(tlEl ? { container: tlEl } : {}),
    }))
  }
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

  ws.on('ready', (duration) => {
    // Force Timeline repaint after the TelerikWindow open animation completes.
    // Also re-evaluate height-based visibility at each attempt.
    ;[50, 200, 400].forEach(ms =>
      setTimeout(() => {
        const inst = instances.get(containerId)
        if (inst) {
          _syncTimelineVisibility(containerId)
          inst.ws.zoom(inst.ws.options.minPxPerSec ?? 0)
        }
      }, ms)
    )
    safe(dotnetRef.invokeMethodAsync('OnWsReady', duration))
  })
  ws.on('play',       ()          => safe(dotnetRef.invokeMethodAsync('OnWsPlay')))
  ws.on('pause',      ()          => safe(dotnetRef.invokeMethodAsync('OnWsPause')))
  ws.on('finish',     ()          => safe(dotnetRef.invokeMethodAsync('OnWsFinish')))
  ws.on('timeupdate', (time)      => safe(dotnetRef.invokeMethodAsync('OnWsTimeUpdate', time)))
  ws.on('loading',    (percent)   => safe(dotnetRef.invokeMethodAsync('OnWsLoading',    percent)))
  ws.on('error',      (err)       => safe(dotnetRef.invokeMethodAsync('OnWsError',      err?.message ?? String(err))))
  ws.on('zoom',       (pps)       => safe(dotnetRef.invokeMethodAsync('OnWsZoom',       pps)))
  ws.on('seeking',    (time)      => safe(dotnetRef.invokeMethodAsync('OnWsSeeking',    time)))
  // Keep spectrogram in sync with zoom and horizontal scroll
  ws.on('zoom',   () => _scheduleSpectrogramRedraw(containerId, false))
  ws.on('scroll', () => _scheduleSpectrogramRedraw(containerId, true))

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
        inst.ws.setOptions({ height: 'auto' })
        _syncTimelineVisibility(containerId)
      }
    }, 50)
  })
  resizeObserver.observe(container.parentElement ?? container)

  instances.set(containerId, { ws, regionsPlugin, envelopePlugin, resizeObserver })

  // ── Custom drag-to-create (only when requested) ────────────────────────
  // We implement drag-to-create manually instead of relying on RegionsPlugin's
  // built-in option so that:
  //   1. A plain click still seeks (WaveSurfer handles it via 'click' event)
  //   2. A click-and-drag creates a region with a live blue preview overlay
  //   3. WaveSurfer's click-to-seek is blocked when the user actually dragged

  if (plugins.regionsDragToCreate && regionsPlugin) {
    const DRAG_THRESHOLD = 5   // px moved before we treat this as a drag

    let dragStartX    = null
    let dragStartTime = null
    let isDragging    = false
    let wasDragging   = false   // stays true until 'click' fires
    let previewDiv    = null

    const onPointerDown = (ev) => {
      if (ev.button !== 0) return
      const duration = ws.getDuration()
      if (!duration) return
      try { container.setPointerCapture(ev.pointerId) } catch {}
      dragStartX    = ev.clientX
      dragStartTime = _wsTimeAtClientX(ws, container, ev.clientX)
      isDragging    = false
      wasDragging   = false
    }

    const onPointerMove = (ev) => {
      if (dragStartX === null) return
      if (Math.abs(ev.clientX - dragStartX) > DRAG_THRESHOLD) isDragging = true
      if (!isDragging) return

      // Create / update the preview overlay
      if (!previewDiv) {
        previewDiv = document.createElement('div')
        previewDiv.style.cssText =
          'position:absolute;top:0;bottom:0;z-index:20;pointer-events:none;' +
          'background:rgba(59,130,246,0.18);' +
          'border:2px solid rgba(59,130,246,0.55);border-radius:2px;'
        container.style.position = 'relative'
        container.appendChild(previewDiv)
      }
      const rect   = container.getBoundingClientRect()
      const leftPx = Math.min(dragStartX, ev.clientX) - rect.left
      const wdPx   = Math.abs(ev.clientX - dragStartX)
      previewDiv.style.left  = `${Math.max(0, leftPx)}px`
      previewDiv.style.width = `${wdPx}px`
    }

    const onPointerUp = (ev) => {
      if (previewDiv) { previewDiv.remove(); previewDiv = null }
      try { container.releasePointerCapture(ev.pointerId) } catch {}

      if (isDragging && dragStartTime !== null) {
        wasDragging = true
        const endTime = _wsTimeAtClientX(ws, container, ev.clientX)
        const start = Math.min(dragStartTime, endTime)
        const end   = Math.max(dragStartTime, endTime)

        if (end - start > 0.05) {   // at least 50 ms
          regionsPlugin.addRegion({
            start, end,
            color: 'rgba(59,130,246,0.2)',
            drag: true, resize: true,
          })
        }
      }

      dragStartX    = null
      dragStartTime = null
      isDragging    = false
    }

    const onPointerCancel = () => {
      if (previewDiv) { previewDiv.remove(); previewDiv = null }
      dragStartX = null; dragStartTime = null
      isDragging = false; wasDragging = false
    }

    // Stop WaveSurfer's seek when the user dragged instead of clicking
    const onClickCapture = (ev) => {
      if (wasDragging) {
        ev.stopImmediatePropagation()
        wasDragging = false
      }
    }

    container.addEventListener('pointerdown',   onPointerDown,   { capture: true })
    container.addEventListener('pointermove',   onPointerMove,   { capture: true })
    container.addEventListener('pointerup',     onPointerUp,     { capture: true })
    container.addEventListener('pointercancel', onPointerCancel, { capture: true })
    container.addEventListener('click',         onClickCapture,  { capture: true })

    // Store cleanup function so destroy() can remove the listeners
    instances.get(containerId).dragCleanup = () => {
      container.removeEventListener('pointerdown',   onPointerDown,   { capture: true })
      container.removeEventListener('pointermove',   onPointerMove,   { capture: true })
      container.removeEventListener('pointerup',     onPointerUp,     { capture: true })
      container.removeEventListener('pointercancel', onPointerCancel, { capture: true })
      container.removeEventListener('click',         onClickCapture,  { capture: true })
      if (previewDiv) { previewDiv.remove(); previewDiv = null }
    }
  }

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

/**
 * Removes all regions except those whose IDs are in keepIds.
 * Used to clear only user-drawn regions while preserving saved-clip overlays.
 */
export function clearUserRegions(containerId, keepIds) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  if (!regionsPlugin) return
  const keep = new Set(keepIds ?? [])
  regionsPlugin.getRegions()
    .filter(r => !keep.has(r.id))
    .forEach(r => r.remove())
}

export function getRegions(containerId) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  return regionsPlugin?.getRegions().map((r) => ({ id: r.id, start: r.start, end: r.end, color: r.color })) ?? []
}

export function playRegion(containerId, regionId) {
  const instance = instances.get(containerId)
  if (!instance) return
  const { ws, regionsPlugin } = instance

  const region = regionsPlugin?.getRegions().find(r => r.id === regionId)
  if (!region) return

  // Cancel any in-progress region-play monitor from a previous call
  instance._regionPlayCleanup?.()
  instance._regionPlayCleanup = null

  const duration = ws.getDuration()
  if (!duration) return

  ws.seekTo(region.start / duration)
  ws.play()

  // WaveSurfer 7’s .on() returns an unsubscribe function
  let unsubs = []

  const stopAndClean = () => {
    unsubs.forEach(u => u?.())
    unsubs = []
    if (instance._regionPlayCleanup === stopAndClean)
      instance._regionPlayCleanup = null
  }

  // On every timeupdate, read the LIVE region end so that resize/expand/delete
  // all take effect without touching the audio file itself.
  unsubs.push(ws.on('timeupdate', () => {
    const live = regionsPlugin.getRegions().find(r => r.id === regionId)
    if (!live) {
      // Region was deleted while playing — stop immediately
      stopAndClean()
      ws.pause()
      return
    }
    if (ws.getCurrentTime() >= live.end) {
      stopAndClean()
      ws.pause()
    }
  }))

  // Clean up if the user manually pauses, the track finishes, or seeks away
  unsubs.push(ws.on('pause',   stopAndClean))
  unsubs.push(ws.on('finish',  stopAndClean))
  unsubs.push(ws.on('seeking', stopAndClean))

  instance._regionPlayCleanup = stopAndClean
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
 * @param {number}               fftSamples   FFT window size (128/256/512/1024/2048/4096)
 * @param {DotNetObjectReference} dotnetRef   Used to fire progress/ready/menu callbacks
 */
export async function toggleSpectrogram(containerId, enable, showLabels, fftSamples, dotnetRef) {
  const instance = instances.get(containerId)
  if (!instance) return

  const canvasId = `${containerId}-spectro`

  if (!enable) {
    document.getElementById(`${canvasId}-wrapper`)?.remove()
    if (instance._spectrogramDebounceTimer) { clearTimeout(instance._spectrogramDebounceTimer); delete instance._spectrogramDebounceTimer }
    instance.spectrogramWorker?.terminate()
    delete instance.spectrogramWorker
    instance._spectrogramDrawWorker?.terminate()
    delete instance._spectrogramDrawWorker
    delete instance._spectrogramDrawVersion
    delete instance._spectrogramCache
    delete instance.spectrogramData
    delete instance.spectrogramMeta
    // Restore the player container to its pre-spectrogram height
    const _waveEl2 = document.getElementById(containerId)
    const _playerEl2 = _waveEl2?.parentElement
    if (_playerEl2 && instance._savedPlayerHeight !== undefined) {
      _playerEl2.style.height = instance._savedPlayerHeight
      delete instance._savedPlayerHeight
      instance.ws.setOptions({ height: 'auto' })
    }
    return
  }

  // Expand the player container so the waveform keeps its current height and
  // the spectrogram adds below it.  Only shrink the waveform if the expanded
  // size would exceed the player's CSS max-height.
  const SPECTRO_H  = 128   // canvas height (px) defined in _ensureSpectrogramCanvas
  const _waveEl    = document.getElementById(containerId)
  const _playerEl  = _waveEl?.parentElement
  if (_playerEl && instance._savedPlayerHeight === undefined) {
    const curH = _playerEl.offsetHeight
    const maxH = parseFloat(getComputedStyle(_playerEl).maxHeight) || 9999
    instance._savedPlayerHeight = _playerEl.style.height   // save inline value
    _playerEl.style.height = `${Math.min(curH + SPECTRO_H, maxH)}px`
    instance.ws.setOptions({ height: 'auto' })
  }

  // Already computed — rebuild the canvas from cached data (respects current zoom/scroll)
  if (instance.spectrogramData) {
    _ensureSpectrogramCanvas(canvasId, containerId, dotnetRef)
    instance.spectrogramMeta.showLabels = showLabels
    _hideSpectrogramLoading(canvasId)
    _redrawSpectrogramViewport(containerId)
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

  const noverlap   = Math.floor(fftSamples / 2)
  const channelCopy = new Float32Array(audioBuffer.getChannelData(0))

  const worker = new Worker('/js/wavesurfer/spectrogram-worker.js')
  instance.spectrogramWorker = worker
  instance.spectrogramMeta   = { sampleRate: audioBuffer.sampleRate, fftSamples, showLabels }

  const safe = (fn) => fn.catch(() => {})

  worker.onmessage = (e) => {
    if (e.data.type === 'progress') {
      _updateSpectrogramLoading(canvasId, e.data.percent)
      safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramProgress', e.data.percent))
    } else if (e.data.type === 'done') {
      instance.spectrogramData = e.data.data
      _hideSpectrogramLoading(canvasId)
      _redrawSpectrogramViewport(containerId)
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
 * Re-computes the spectrogram canvas at a new FFT resolution.
 * Clears cached data, shows a fresh loading indicator, and re-runs the Web Worker.
 *
 * @param {string}               containerId  Player container ID
 * @param {number}               fftSamples   FFT window size (128/256/512/1024/2048/4096)
 * @param {boolean}              showLabels   Whether to render frequency-axis labels
 * @param {DotNetObjectReference} dotnetRef   Progress / ready callbacks
 */
export async function setSpectrogramResolution(containerId, fftSamples, showLabels, dotnetRef) {
  const instance = instances.get(containerId)
  if (!instance?.spectrogramMeta) return   // spectrogram not currently shown

  const canvasId = `${containerId}-spectro`

  // Terminate any in-progress computation (FFT worker and draw worker)
  if (instance._spectrogramDebounceTimer) { clearTimeout(instance._spectrogramDebounceTimer); delete instance._spectrogramDebounceTimer }
  instance.spectrogramWorker?.terminate()
  delete instance.spectrogramWorker
  instance._spectrogramDrawWorker?.terminate()
  delete instance._spectrogramDrawWorker
  delete instance._spectrogramDrawVersion
  delete instance._spectrogramCache
  delete instance.spectrogramData

  // Show loading text again
  const loadingEl = document.getElementById(`${canvasId}-loading`)
  if (loadingEl) {
    loadingEl.textContent = 'Generating spectrogram… 0%'
    loadingEl.style.display = ''
  }

  // Clear the canvas
  const canvas = document.getElementById(canvasId)
  if (canvas) {
    const ctx = canvas.getContext('2d')
    ctx.clearRect(0, 0, canvas.width, canvas.height)
  }

  const ws          = instance.ws
  const audioBuffer = ws.getDecodedData?.()
  if (!audioBuffer) return

  const noverlap    = Math.floor(fftSamples / 2)
  const channelCopy = new Float32Array(audioBuffer.getChannelData(0))

  const safe = (fn) => fn.catch(() => {})

  const worker = new Worker('/js/wavesurfer/spectrogram-worker.js')
  instance.spectrogramWorker = worker
  instance.spectrogramMeta   = { sampleRate: audioBuffer.sampleRate, fftSamples, showLabels }

  worker.onmessage = (e) => {
    if (e.data.type === 'progress') {
      _updateSpectrogramLoading(canvasId, e.data.percent)
      safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramProgress', e.data.percent))
    } else if (e.data.type === 'done') {
      instance.spectrogramData = e.data.data
      _hideSpectrogramLoading(canvasId)
      _redrawSpectrogramViewport(containerId)
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
 * Shows or hides the WaveSurfer Timeline plugin bar.
 * When making it visible, fires zoom() so the Timeline plugin repaints its
 * notches at the current container width.
 */
export function toggleTimeline(containerId, visible) {
  const instance = instances.get(containerId)
  // Store the user's preference so _syncTimelineVisibility can respect it
  if (instance) instance._timelineEnabled = visible
  _syncTimelineVisibility(containerId)
  if (visible && instance) {
    instance.ws.zoom(instance.ws.options.minPxPerSec ?? 0)
  }
}

/**
 * Redraws the spectrogram canvas with the cached FFT data, toggling labels.
 */
export function updateSpectrogramLabels(containerId, showLabels) {
  const instance = instances.get(containerId)
  if (!instance?.spectrogramData) return
  if (instance.spectrogramMeta) instance.spectrogramMeta.showLabels = showLabels
  _redrawSpectrogramViewport(containerId)
}

// ── Internal helpers ──────────────────────────────────────────────────────────

/**
 * Returns WaveSurfer's internal scroll container.
 * WaveSurfer 7 creates its waveform inside a shadow root attached to a wrapper
 * div it appends to the container we pass, so [part="scroll"] is only reachable
 * via the shadow root — not via a plain querySelector on the outer container.
 *
 * @param {HTMLElement} container  The outer WaveSurfer container element
 * @returns {HTMLElement|null}
 */
function _wsScrollEl(container) {
  if (!container) return null
  for (const child of container.children) {
    if (child.shadowRoot) {
      const el = child.shadowRoot.querySelector('[part="scroll"]')
      if (el) return el
    }
  }
  return null
}

/**
 * Shows a "Recalculating spectrogram…" message in the loading div above the
 * canvas while a new viewport render is being computed in the background.
 */
function _showSpectrogramRecalculating(canvasId) {
  const el = document.getElementById(`${canvasId}-loading`)
  if (el) { el.textContent = 'Recalculating spectrogram…'; el.style.display = '' }
}

/**
 * Schedules a spectrogram viewport redraw, debouncing scroll events so that
 * rapid scrolling only triggers one redraw per idle window.
 *
 * @param {string}  containerId  Player container ID
 * @param {boolean} debounce     true = scroll (60 ms debounce), false = zoom (immediate)
 */
function _scheduleSpectrogramRedraw(containerId, debounce) {
  const instance = instances.get(containerId)
  if (!instance?.spectrogramData) return

  if (instance._spectrogramDebounceTimer) {
    clearTimeout(instance._spectrogramDebounceTimer)
    instance._spectrogramDebounceTimer = null
  }

  if (debounce) {
    // Scroll: silent background update — no loading message during playback auto-scroll.
    // The render fires once the scroll settles (60 ms quiet window).
    instance._spectrogramDebounceTimer = setTimeout(() => {
      instance._spectrogramDebounceTimer = null
      _redrawSpectrogramViewport(containerId)
    }, 60)
  } else {
    _redrawSpectrogramViewport(containerId)
  }
}

/**
 * Returns the WaveSurfer time (seconds) at a given viewport X position.
 * Mirrors WaveSurfer's own click-to-seek formula so times are always accurate,
 * even when the waveform is zoomed in and scrolled.
 *
 * @param {WaveSurfer} ws          WaveSurfer instance
 * @param {HTMLElement} container  The outer WaveSurfer container element
 * @param {number}      clientX    Viewport X coordinate from a pointer event
 * @returns {number}               Time in seconds, clamped to [0, duration]
 */
function _wsTimeAtClientX(ws, container, clientX) {
  const duration = ws.getDuration?.() ?? 0
  if (!duration) return 0
  // WaveSurfer 7 renders into a scrollable [part="scroll"] div inside the container.
  // Using its scrollLeft + scrollWidth mirrors WaveSurfer's own seek formula exactly.
  const scrollEl = _wsScrollEl(container)
  if (scrollEl && scrollEl.scrollWidth > scrollEl.clientWidth + 1) {
    // Zoomed: position = (scrollOffset + x within visible area) / total scrollable width
    const rect = scrollEl.getBoundingClientRect()
    const x    = clientX - rect.left + scrollEl.scrollLeft
    return Math.max(0, Math.min(duration, (x / scrollEl.scrollWidth) * duration))
  }
  // Default (fill parent): simple proportion across the container width
  const rect = container.getBoundingClientRect()
  return Math.max(0, Math.min(duration, ((clientX - rect.left) / rect.width) * duration))
}

/**
 * Redraws the spectrogram canvas showing only the currently visible time window.
 * Called after zoom and scroll events so the spectrogram stays aligned with the waveform.
 *
 * @param {string} containerId  Player container ID
 */
function _redrawSpectrogramViewport(containerId) {
  const instance = instances.get(containerId)
  if (!instance?.spectrogramData) return

  const canvasId = `${containerId}-spectro`
  const canvas   = document.getElementById(canvasId)
  if (!canvas) return

  const container  = document.getElementById(containerId)
  const scrollEl   = _wsScrollEl(container)
  const scrollLeft  = scrollEl?.scrollLeft  ?? 0
  const scrollWidth = scrollEl?.scrollWidth ?? 0
  const clientWidth = scrollEl?.clientWidth ?? canvas.parentElement?.offsetWidth ?? 800
  const isZoomed    = scrollWidth > clientWidth + 1
  const canvasW     = Math.max(1, canvas.parentElement?.offsetWidth ?? clientWidth ?? 800)

  const { sampleRate = 44100, fftSamples = 512, showLabels = false } = instance.spectrogramMeta ?? {}
  const nFrames = instance.spectrogramData.length
  const nBins   = Math.floor(fftSamples / 2)

  let startFrame = 0, endFrame = nFrames
  if (isZoomed && scrollWidth > 0) {
    startFrame = Math.max(0, Math.floor((scrollLeft / scrollWidth) * nFrames))
    endFrame   = Math.min(nFrames, Math.ceil(((scrollLeft + clientWidth) / scrollWidth) * nFrames))
  }
  endFrame = Math.max(startFrame + 1, endFrame)

  // ── Cache lookup ───────────────────────────────────────────────────────
  if (!instance._spectrogramCache) instance._spectrogramCache = new Map()
  const cacheKey = `${startFrame}:${endFrame}:${canvasW}:${showLabels ? 1 : 0}`
  const cached   = instance._spectrogramCache.get(cacheKey)

  if (cached) {
    canvas.width  = canvasW
    canvas.height = 128
    canvas.getContext('2d').putImageData(cached, 0, 0)
    _hideSpectrogramLoading(canvasId)
    return
  }

  // ── Async draw via persistent Web Worker ────────────────────────────────
  _showSpectrogramRecalculating(canvasId)

  // Create the persistent draw worker on first use
  if (!instance._spectrogramDrawWorker) {
    const w = new Worker('/js/wavesurfer/spectrogram-draw-worker.js')
    instance._spectrogramDrawWorker  = w
    instance._spectrogramDrawVersion = 0

    w.onmessage = (e) => {
      const { pixels, width, height, version, cacheKey: ck } = e.data
      const inst = instances.get(containerId)
      if (!inst || version !== inst._spectrogramDrawVersion) return  // stale — discard

      const c = document.getElementById(canvasId)
      if (!c) return

      c.width  = width
      c.height = height
      const ctx = c.getContext('2d')
      ctx.putImageData(new ImageData(pixels, width, height), 0, 0)

      // Apply frequency labels on top (fast canvas text, always fresh)
      if (ck.endsWith(':1')) {
        const { sampleRate: sr = 44100, fftSamples: fs = 512 } = inst.spectrogramMeta ?? {}
        _drawSpectrogramLabels(c, sr, Math.floor(fs / 2))
      }

      // Cache the fully-rendered image (including labels if applicable)
      if (!inst._spectrogramCache) inst._spectrogramCache = new Map()
      if (inst._spectrogramCache.size >= 30)
        inst._spectrogramCache.delete(inst._spectrogramCache.keys().next().value)
      inst._spectrogramCache.set(ck, ctx.getImageData(0, 0, width, height))

      _hideSpectrogramLoading(canvasId)
    }
    w.onerror = () => _hideSpectrogramLoading(canvasId)
  }

  // Bump version so any in-flight render is discarded when it arrives
  instance._spectrogramDrawVersion = (instance._spectrogramDrawVersion ?? 0) + 1

  // Flatten the visible frame slice into a transferable Float32Array
  const sliceLen = endFrame - startFrame
  const flat     = new Float32Array(sliceLen * nBins)
  for (let i = 0; i < sliceLen; i++)
    flat.set(instance.spectrogramData[startFrame + i], i * nBins)

  instance._spectrogramDrawWorker.postMessage(
    { flat, nFrames: sliceLen, nBins, width: canvasW, height: 128,
      version: instance._spectrogramDrawVersion, cacheKey },
    [flat.buffer]
  )
}

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

/**
 * Draws a spectrogram to a canvas synchronously using a pixel-buffer approach
 * (O(W×H) output iterations instead of O(nFrames×nBins) fillRect calls).
 * Used for the initial render once FFT data is ready. Subsequent viewport
 * redraws triggered by zoom/scroll use the async Worker path in
 * _redrawSpectrogramViewport instead.
 */
function _drawSpectrogram(canvas, data, showLabels, sampleRate, fftSamples) {
  if (!data?.length) return

  const W     = Math.max(1, canvas.parentElement?.offsetWidth || 800)
  const H     = 128
  canvas.width  = W
  canvas.height = H

  const nFrames = data.length
  const nBins   = Math.floor(fftSamples / 2)

  // Normalise over the visible slice
  let maxMag = 1e-9
  for (const frame of data)
    for (const v of frame)
      if (v > maxMag) maxMag = v

  // O(W×H) pixel-buffer render — one putImageData instead of millions of fillRects
  const pixels = new Uint8ClampedArray(W * H * 4)
  for (let px = 0; px < W; px++) {
    const frame = data[Math.min(nFrames - 1, Math.floor(px * nFrames / W))]
    for (let py = 0; py < H; py++) {
      const binIdx = nBins - 1 - Math.min(nBins - 1, Math.floor(py * nBins / H))
      const t  = frame[binIdx] / maxMag
      const r  = Math.floor(255 * Math.pow(t, 0.5))
      const g  = Math.floor(255 * Math.min(1, t * 1.5))
      const b  = Math.floor(255 * Math.max(0, 0.9 - t))
      const idx = (py * W + px) * 4
      pixels[idx]     = r
      pixels[idx + 1] = g
      pixels[idx + 2] = b
      pixels[idx + 3] = 255
    }
  }
  canvas.getContext('2d').putImageData(new ImageData(pixels, W, H), 0, 0)

  if (showLabels) _drawSpectrogramLabels(canvas, sampleRate, nBins)
}

/**
 * Overlays frequency-axis gridlines and labels on an already-drawn spectrogram canvas.
 * Fast canvas text calls only — safe to call on the main thread after putImageData.
 */
function _drawSpectrogramLabels(canvas, sampleRate, nBins) {
  const W = canvas.width
  const H = canvas.height
  if (!W || !H) return
  const ctx     = canvas.getContext('2d')
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
    ctx.fillText(freq >= 1000 ? `${freq / 1000}kHz` : `${freq}Hz`, 4, y - 2)
  }
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

export function destroy(containerId) {
  const instance = instances.get(containerId)
  if (!instance) return
  instance._regionPlayCleanup?.()
  instance.dragCleanup?.()
  instance.resizeObserver?.disconnect()
  if (instance._spectrogramDebounceTimer) clearTimeout(instance._spectrogramDebounceTimer)
  instance.spectrogramWorker?.terminate()
  instance._spectrogramDrawWorker?.terminate()
  try { instance.ws?.destroy() } catch { /* ignore */ }
  instances.delete(containerId)
}
