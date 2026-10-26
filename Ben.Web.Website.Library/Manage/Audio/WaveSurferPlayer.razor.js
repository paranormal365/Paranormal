/**
 * WaveSurferPlayer.razor.js
 *
 * Blazor JS-isolation module for the WaveSurferPlayer component.
 * Served at: /_content/Ben.Web.Website.Library/Manage/Audio/WaveSurferPlayer.razor.js
 *
 * WaveSurfer ESM bundles are in the host WebApp at /js/wavesurfer/.
 * Build command (from Ben.Web.WebApp/wwwroot/ts/wavesurfer/):
 *   npm run build:blazor
 */

// Map of containerId → { ws, regionsPlugin, resizeObserver }
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
  }

  await Promise.all([
    ensurePlugin(plugins.regions,             '/js/wavesurfer/plugins/regions.esm.js',              caches.regions),
    ensurePlugin(plugins.hover,               '/js/wavesurfer/plugins/hover.esm.js',                caches.hover),
    ensurePlugin(plugins.timeline,            '/js/wavesurfer/plugins/timeline.esm.js',             caches.timeline),
    ensurePlugin(plugins.zoom,                '/js/wavesurfer/plugins/zoom.esm.js',                 caches.zoom),
    ensurePlugin(plugins.minimap,             '/js/wavesurfer/plugins/minimap.esm.js',              caches.minimap),
    ensurePlugin(plugins.spectrogram,         '/js/wavesurfer/plugins/spectrogram.esm.js',          caches.spectrogram),
  ])

  // Update module-level caches
  if (caches.regions.value)             RegionsPlugin             = caches.regions.value
  if (caches.hover.value)               HoverPlugin               = caches.hover.value
  if (caches.timeline.value)            TimelinePlugin            = caches.timeline.value
  if (caches.zoom.value)                ZoomPlugin                = caches.zoom.value
  if (caches.minimap.value)             MinimapPlugin             = caches.minimap.value
  if (caches.spectrogram.value)         SpectrogramPlugin         = caches.spectrogram.value

  // Build plugin instances
  const wsPlugins = []
  let regionsPlugin = null

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
          // The zoom actually in effect, not the one it was created with: these repaints run up to
          // 400 ms after load, and re-applying the starting zoom undid one set in that time,
          // including the editor restoring a saved zoom (10/26/2026).
          inst.ws.zoom(_currentZoom(inst))
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
  ws.on('zoom',       (pps)       => {
    const inst = instances.get(containerId)
    if (inst) inst._zoom = pps
    // ws.zoom() does not record the zoom in ws.options, and every setOptions() re-renders from
    // ws.options, so any later height change (the spectrogram pane, a resize) snapped the
    // waveform back to its starting zoom. Recording it here keeps it (10/26/2026).
    ws.options.minPxPerSec = pps
    safe(dotnetRef.invokeMethodAsync('OnWsZoom', pps))
  })
  ws.on('seeking',    (time)      => safe(dotnetRef.invokeMethodAsync('OnWsSeeking',    time)))

  // ── Regions events ───────────────────────────────────────────────────────
  if (regionsPlugin) {
    const rd = (r) => ({
      id: r.id, start: r.start, end: r.end, color: r.color, label: r.content ?? null,
      // What put this region here. Anything nobody registered was dragged by a person — see
      // _regionKind for why that default is the load-bearing part.
      kind: _regionKind(containerId, r.id),
    })
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
    regionsPlugin.on('region-removed', (r)  => {
      instances.get(containerId)?._regionKinds?.delete(r.id)
      safe(dotnetRef.invokeMethodAsync('OnWsRegionRemoved', r.id))
    })
    regionsPlugin.on('region-clicked', (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionClicked', r.id)))
    regionsPlugin.on('region-in',      (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionIn',      r.id)))
    regionsPlugin.on('region-out',     (r)  => safe(dotnetRef.invokeMethodAsync('OnWsRegionOut',     r.id)))
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

  instances.set(containerId, { ws, regionsPlugin, resizeObserver, containerId })

  // The timeline and the spectrogram draw into panes of their own, outside the waveform's scroll
  // box, so they are moved with it here. See _syncPanesToWaveform.
  const sync = () => {
    const inst = instances.get(containerId)
    _syncPanesToWaveform(inst)
    // The marks on the spectrogram are placed from the scroll position (phase 7).
    if (inst?._spectroSpans?.length) _drawSpectrogramOverlay(inst)
  }
  instances.get(containerId)._paneSyncUnsubs = [
    ws.on('scroll', sync),
    ws.on('zoom', () => requestAnimationFrame(sync)),
    ws.on('redrawcomplete', sync),
  ]

  // ── Custom drag-to-create / drag-to-scrub (only when requested) ─────────
  // We implement both manually instead of relying on RegionsPlugin's/WaveSurfer's
  // built-in options so that:
  //   1. A plain click still seeks (WaveSurfer handles it via 'click' event)
  //   2. In "region" mode, a click-and-drag creates a region with a live blue
  //      preview overlay, and WaveSurfer's click-to-seek is blocked afterward
  //   3. In "scrub" mode, a click-and-drag moves the playhead live and plays the
  //      audio audibly while dragging (see onPointerDown below)
  //
  // Mode is a single runtime switch (instance._dragMode, toggled via the exported
  // setDragMode()) — the two gestures share the same click-and-drag input, so only
  // one can be active at a time; there is no separate playhead-only hit-test.

  if (plugins.regionsDragToCreate && regionsPlugin) {
    const DRAG_THRESHOLD = 5   // px moved before a region-drag counts as a drag
    const instance = instances.get(containerId)
    instance._dragMode = 'region'   // 'region' | 'scrub'

    // Region-drag state
    let dragStartX    = null
    let dragStartTime = null
    let isDragging    = false
    let wasDragging   = false   // stays true until 'click' fires
    let previewDiv    = null

    // Scrub state — see setDragMode()'s doc comment for the audible-while-paused rationale
    let scrubbing             = false
    let wasPlayingBeforeScrub = false
    let scrubJustEnded        = false   // stays true until the trailing 'click' fires

    // True when the gesture started on an existing region body or one of its resize handles.
    // Regions live in the waveform's shadow DOM, so ev.target from the container's point of
    // view is only the shadow host — composedPath() is what actually sees them.
    //
    // Without this, the capture-phase handler below claimed *every* pointerdown (and called
    // setPointerCapture on the container), so the regions plugin's own move/resize handlers
    // never received the drag: grabbing a region's edge silently drew a brand new region
    // instead of resizing the existing one, i.e. move/resize were unreachable by mouse.
    const startedOnRegion = (ev) =>
      (ev.composedPath?.() ?? []).some((el) => {
        const part = el?.getAttribute?.('part')
        return !!part && /(^|\s)region(\s|$)|region-handle/.test(part)
      })

    const onPointerDown = (ev) => {
      if (ev.button !== 0) return
      const duration = ws.getDuration()
      if (!duration) return

      // Drawing the volume curve: the envelope plugin owns every press (phase 7).
      if (instance._envelope) return

      // Let the regions plugin own drags that begin on a region or its handles.
      if (instance._dragMode === 'region' && startedOnRegion(ev)) {
        dragStartX = null
        isDragging = false
        return
      }

      if (instance._dragMode === 'scrub') {
        scrubbing = true
        wasPlayingBeforeScrub = ws.isPlaying()
        try { container.setPointerCapture(ev.pointerId) } catch {}
        if (!wasPlayingBeforeScrub) ws.play().catch(() => {})
        ws.setTime(_wsTimeAtClientX(ws, container, ev.clientX))
        return
      }

      try { container.setPointerCapture(ev.pointerId) } catch {}
      dragStartX    = ev.clientX
      dragStartTime = _wsTimeAtClientX(ws, container, ev.clientX)
      isDragging    = false
      wasDragging   = false
    }

    const onPointerMove = (ev) => {
      if (scrubbing) {
        ws.setTime(_wsTimeAtClientX(ws, container, ev.clientX))
        return
      }
      if (dragStartX === null) return
      if (Math.abs(ev.clientX - dragStartX) > DRAG_THRESHOLD) isDragging = true
      if (!isDragging) return

      // Create / update the preview overlay
      if (!previewDiv) {
        previewDiv = document.createElement('div')
        previewDiv.style.cssText =
          'position:absolute;top:0;bottom:0;z-index:20;pointer-events:none;' +
          'background:rgba(var(--ben-audio-region-rgb, 91, 61, 245),0.18);' +
          'border:2px solid rgba(var(--ben-audio-region-rgb, 91, 61, 245),0.55);border-radius:2px;'
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
      if (scrubbing) {
        scrubbing = false
        scrubJustEnded = true
        try { container.releasePointerCapture(ev.pointerId) } catch {}
        if (!wasPlayingBeforeScrub) ws.pause()
        return
      }

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
            // The theme's token, resolved by the browser: custom properties inherit into the
            // regions' shadow DOM. It was a fixed blue that ignored the theme (phase 3).
            color: 'rgba(var(--ben-audio-region-rgb, 91, 61, 245),0.2)',
            drag: true, resize: true,
          })
        }
      }

      dragStartX    = null
      dragStartTime = null
      isDragging    = false
    }

    const onPointerCancel = () => {
      if (scrubbing) {
        scrubbing = false
        if (!wasPlayingBeforeScrub) ws.pause()
      }
      if (previewDiv) { previewDiv.remove(); previewDiv = null }
      dragStartX = null; dragStartTime = null
      isDragging = false; wasDragging = false
    }

    // Stop WaveSurfer's native click-to-seek after a real region-drag or a scrub —
    // in both cases the position is already exactly where the user released.
    const onClickCapture = (ev) => {
      if (wasDragging || scrubJustEnded) {
        ev.stopImmediatePropagation()
        wasDragging    = false
        scrubJustEnded = false
      }
    }

    container.addEventListener('pointerdown',   onPointerDown,   { capture: true })
    container.addEventListener('pointermove',   onPointerMove,   { capture: true })
    container.addEventListener('pointerup',     onPointerUp,     { capture: true })
    container.addEventListener('pointercancel', onPointerCancel, { capture: true })
    container.addEventListener('click',         onClickCapture,  { capture: true })

    // Store cleanup function so destroy() can remove the listeners
    instance.dragCleanup = () => {
      container.removeEventListener('pointerdown',   onPointerDown,   { capture: true })
      container.removeEventListener('pointermove',   onPointerMove,   { capture: true })
      container.removeEventListener('pointerup',     onPointerUp,     { capture: true })
      container.removeEventListener('pointercancel', onPointerCancel, { capture: true })
      container.removeEventListener('click',         onClickCapture,  { capture: true })
      if (previewDiv) { previewDiv.remove(); previewDiv = null }
    }
  }

  if (audioUrl) {
    await _load(ws, audioUrl)
  }
}

/**
 * Loads a recording, treating a load that was REPLACED as nothing to report.
 *
 * WaveSurfer 8 rejects a load() that a newer load() superseded with an AbortError, and emits no
 * 'error' for it (v7 resolved it silently). So OnWsError never hears of it, which is right, but the
 * rejection would otherwise surface through Blazor's interop as a failure of a load that simply
 * stopped mattering. A real failure still rejects, and still emits 'error' for the page to show.
 */
async function _load(ws, url) {
  try {
    await ws.load(url)
  } catch (err) {
    if (err?.name === 'AbortError') return
    throw err
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
export function setZoom(containerId, minPxPerSec) {
  const inst = instances.get(containerId)
  if (!inst?.ws) return
  inst._zoom = minPxPerSec
  inst.ws.zoom(minPxPerSec)
}

/** The zoom in effect: the last one set or reported, else the one the player was created with. */
function _currentZoom(instance) {
  return instance?._zoom ?? instance?.ws?.options?.minPxPerSec ?? 0
}

export async function load(containerId, url) {
  const ws = instances.get(containerId)?.ws
  if (ws) await _load(ws, url)
}

// ── Getters ───────────────────────────────────────────────────────────────────

export function isPlaying(containerId)    { return instances.get(containerId)?.ws?.isPlaying()    ?? false }
export function getCurrentTime(containerId) { return instances.get(containerId)?.ws?.getCurrentTime() ?? 0 }
export function getDuration(containerId)  { return instances.get(containerId)?.ws?.getDuration()   ?? 0 }
export function getVolume(containerId)    { return instances.get(containerId)?.ws?.getVolume()     ?? 1 }

// ── Regions API ───────────────────────────────────────────────────────────────

/**
 * Remembers what a region is, so it can be reported back and so clearing by kind is possible.
 *
 * Registration happens BEFORE addRegion, because the plugin fires `region-created`
 * synchronously from inside it — register afterwards and the event has already gone out
 * describing the region as user-drawn.
 */
function _rememberRegionKind(containerId, id, kind) {
  const instance = instances.get(containerId)
  if (!instance || !id) return
  instance._regionKinds ??= new Map()
  instance._regionKinds.set(id, kind)
}

/**
 * What put this region on the waveform, defaulting to 'user'.
 *
 * The default is the whole point. Regions arrive from four places — a person's drag, silence
 * detection, marker overlays and saved-clip overlays — and only the drag is a selection an edit
 * may act on. The editor used to infer this from a set of ids it had added itself, which missed
 * every region JavaScript created: silence detection adds its own inside `detectSilence`, so all
 * of them looked user-drawn, each one wiped the others and the person's selection, and the last
 * became what Cut and Silence would destroy (2026-09-06 audio walk, finding B).
 *
 * Unregistered means dragged, never the reverse: guessing "user" for an overlay shows a spurious
 * selection, and guessing "overlay" for a drag silently drops what somebody asked for.
 */
function _regionKind(containerId, id) {
  return instances.get(containerId)?._regionKinds?.get(id) ?? 'user'
}

export function addRegion(containerId, params) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  if (!regionsPlugin) return null

  // Anything added through here is the editor drawing, not a person selecting.
  _rememberRegionKind(containerId, params.id, params.kind ?? 'overlay')

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

  // The plugin assigns an id when none was given; the registration above missed that case.
  if (!params.id) _rememberRegionKind(containerId, r.id, params.kind ?? 'overlay')

  return r.id
}

export function removeRegion(containerId, regionId) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  regionsPlugin?.getRegions().find((r) => r.id === regionId)?.remove()
}

export function clearRegions(containerId) {
  const instance = instances.get(containerId)
  instance?._regionKinds?.clear()
  instance?.regionsPlugin?.clearRegions()
}

/**
 * Removes the regions a person drew, and only those.
 *
 * This used to take a list of ids to KEEP and remove everything else, which made it correct only
 * as long as the caller knew about every region on the waveform. It did not: silence detection
 * adds its regions in this file, so a caller passing "keep my overlays" swept away twenty silence
 * regions and the person's own selection along with them (2026-09-06 audio walk, finding B).
 * Clearing by kind cannot go wrong that way — a region that is not a drag is left alone whether or
 * not the caller has heard of it.
 *
 * `exceptId` keeps one drawn region, which is how "only one selection at a time" is enforced:
 * the newest survives and the previous ones go.
 */
export function clearUserRegions(containerId, exceptId) {
  const instance = instances.get(containerId)
  if (!instance?.regionsPlugin) return
  instance.regionsPlugin.getRegions()
    .filter(r => r.id !== exceptId && _regionKind(containerId, r.id) === 'user')
    .forEach(r => r.remove())
}

/**
 * Makes exactly one region draggable/resizable and locks every other one.
 *
 * Editing bounds is deliberately one-at-a-time: the waveform can carry dozens of detector
 * candidates at once, and if they were all draggable a reviewer would nudge the wrong one
 * constantly while trying to scrub. Passing a null regionId locks them all again.
 *
 * Returns the editable region's current bounds so the caller can seed its inputs without
 * a second round trip.
 */
export function setRegionEditable(containerId, regionId) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  if (!regionsPlugin) return null

  let bounds = null
  for (const r of regionsPlugin.getRegions()) {
    const editable = r.id === regionId
    r.setOptions({ drag: editable, resize: editable })
    if (editable) bounds = { id: r.id, start: r.start, end: r.end, color: r.color }
  }
  return bounds
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

  const duration = ws.getDuration()
  if (!duration) return

  // The LIVE region end is read on every check, so resizing or deleting the region while it plays
  // takes effect without touching the audio file itself.
  _stopAtEnd(instance, region.start, () => regionsPlugin.getRegions().find(r => r.id === regionId)?.end ?? null)
  ws.seekTo(region.start / duration)
  ws.play()
}

/**
 * Plays a stretch of the recording between two times, then stops.
 *
 * The counterpart to {@link playRegion} for a marker that has no region worth playing. A point
 * marker is drawn with start === end, so playRegion on it seeks and pauses in the same instant —
 * which is why a confirmed marker's play button appeared to do nothing at all (2026-09-06 audio
 * walk, finding J). The caller decides how much context a point marker gets; this just plays what
 * it is given.
 *
 * The stop rules are playRegion's, minus the live re-read of the region bounds: a manual pause,
 * the end of the track, or a seek all cancel it, and the first seek — the one this function
 * performs itself — is absorbed.
 */
export function playRange(containerId, startSeconds, endSeconds) {
  const instance = instances.get(containerId)
  if (!instance?.ws) return
  const { ws } = instance

  const duration = ws.getDuration()
  if (!duration) return

  const start = Math.max(0, Math.min(startSeconds ?? 0, duration))
  const end   = Math.min(Math.max(start + 0.05, endSeconds ?? duration), duration)

  _stopAtEnd(instance, start, () => end)
  ws.seekTo(start / duration)
  ws.play()
}

/**
 * Pauses playback when it reaches the end of a stretch, for playRegion and playRange.
 *
 * Every candidate's and marker's Play button played on to the end of the recording (10/26/2026:
 * a 2–4 s region was still playing at 6.0 s). Both functions let a 'seeking' event cancel the
 * stop, ignoring only the first, on the assumption that the first was their own seekTo. WaveSurfer
 * reports seeks with the playhead's OLD position and not always once, so the function's own seek
 * looked like somebody clicking elsewhere, and it cancelled itself.
 *
 * Seeks are no longer listened to at all. Each time update decides:
 * - a small step past the end is playback arriving, so pause;
 * - a big jump backward past the start, or well past the end, is somebody clicking elsewhere on
 *   the waveform, so stop watching and let it play.
 * A pause or the end of the track also stops watching.
 *
 * @param {object}   instance  the player instance
 * @param {number}   start     where the stretch starts, in seconds
 * @param {Function} getEnd    returns where it ends now (read live), or null if it no longer exists
 */
function _stopAtEnd(instance, start, getEnd) {
  const { ws } = instance
  instance._regionPlayCleanup?.()
  instance._regionPlayCleanup = null

  // Larger than any step between two time updates while playing, even at 4× speed.
  const JUMP_SECONDS = 1.0

  let unsubs = []
  const stopAndClean = () => {
    unsubs.forEach(u => u?.())
    unsubs = []
    if (instance._regionPlayCleanup === stopAndClean) instance._regionPlayCleanup = null
  }

  // Not armed until the playhead has been seen inside the stretch: the first updates after the
  // seek can still carry the old position, which would otherwise read as a jump away.
  let armed = false

  unsubs.push(ws.on('timeupdate', () => {
    const end = getEnd()
    const t = ws.getCurrentTime()
    if (end == null) { stopAndClean(); ws.pause(); return }   // the region was deleted
    if (!armed) {
      if (t >= start - 0.05 && t <= end) armed = true
      else return
    }
    if (t < start - JUMP_SECONDS || t > end + JUMP_SECONDS) { stopAndClean(); return }
    if (t >= end) {
      // Looping (phase 7): back to the start and keep watching, rather than stopping.
      if (instance._loop) { ws.setTime(start); return }
      stopAndClean(); ws.pause()
    }
  }))
  unsubs.push(ws.on('pause',  stopAndClean))
  unsubs.push(ws.on('finish', stopAndClean))

  instance._regionPlayCleanup = stopAndClean
}

/**
 * Loops whatever stretch is being played: a region, a candidate, a marker or the selection
 * (phase 7). Off, they stop at their end as before.
 */
export function setLoop(containerId, on) {
  const instance = instances.get(containerId)
  if (instance) instance._loop = !!on
}

/**
 * Zooms so one stretch fills the window, with a margin either side, and scrolls to it (phase 7):
 * "zoom to the selection", "zoom to this candidate".
 */
export function zoomToRange(containerId, startSeconds, endSeconds, pad = 0.15) {
  const instance = instances.get(containerId)
  const ws = instance?.ws
  const scrollEl = _wsScrollEl(document.getElementById(containerId))
  if (!ws || !scrollEl) return
  const duration = ws.getDuration()
  const span = Math.max(0.05, endSeconds - startSeconds)
  if (!duration || !scrollEl.clientWidth) return
  const pxPerSec = scrollEl.clientWidth / (span * (1 + 2 * pad))
  instance._zoom = pxPerSec
  ws.zoom(pxPerSec)
  ws.setScrollTime(Math.max(0, startSeconds - span * pad))
}

export function updateRegionLabel(containerId, regionId, label) {
  const { regionsPlugin } = instances.get(containerId) ?? {}
  const region = regionsPlugin?.getRegions().find((r) => r.id === regionId)
  if (region) region.setOptions({ content: label ?? '' })
}

// ── Spectrogram (WaveSurfer 8's own plugin) ─────────────────────────────────
//
// Phase 1b of ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md. This was a hand-built spectrogram:
// two workers, an OffscreenCanvas pre-render and a fixed 128 px canvas that redrew itself on every
// zoom and scroll. It is now the stock SpectrogramPlugin, drawn into a pane under the waveform,
// which follows zoom and scroll itself and renders long recordings a window at a time. The
// functions keep their names and arguments, so nothing on the C# side changed.
//
// What stays ours is a thin overlay canvas over the pane for the voice band (and, from later phases,
// candidates and markers). It places frequencies through the plugin's own scale functions
// (hzToScale in spectrogram-render-utils), so it lines up with the picture under it.

/**
 * The band the EVP detector listens in by default (EvpDetectionOptions.BandLowHz / BandHighHz).
 * Since phase 5 a scan's own range replaces it through setVoiceBand.
 */
const VOICE_BAND_HZ = [300, 3400]

/** An rgb triplet token from signal-tokens.css, for drawing on a canvas, which cannot read var(). */
function _themeRgb(name, fallback) {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return v || fallback
}

/** Height of the spectrogram pane, in px, until somebody changes it (phase 7: 100–480). */
const SPECTROGRAM_HEIGHT = 160

function _spectroHeight(instance) {
  return instance?._spectroHeight ?? SPECTROGRAM_HEIGHT
}

/** Longer than this, the spectrogram renders the visible window rather than the whole file. */
const WINDOWED_AFTER_SECONDS = 10 * 60

async function _loadSpectrogramPlugin() {
  if (!SpectrogramPlugin) {
    const mod = await import('/js/wavesurfer/plugins/spectrogram.esm.js')
    SpectrogramPlugin = mod.default
  }
  return SpectrogramPlugin
}

/** The plugin's options for what the editor has chosen. */
function _spectrogramOptions(instance, settings, pane) {
  const duration = instance.ws.getDuration?.() ?? 0
  return {
    container:        pane,
    height:           _spectroHeight(instance),
    labels:           settings.showLabels,
    labelsColor:      'rgba(255,255,255,0.85)',
    labelsHzColor:    'rgba(255,255,255,0.6)',
    labelsBackground: 'rgba(0,0,0,0.25)',
    fftSamples:       settings.fftSamples,
    scale:            settings.melScale ? 'mel' : 'linear',
    colorMap:         settings.colormap === 'grayscale' ? 'gray' : _makeColormap(settings.colormap),
    rendering:        duration > WINDOWED_AFTER_SECONDS ? 'windowed' : 'full',
    progressiveLoading: duration > WINDOWED_AFTER_SECONDS,
    useWebWorker:     true,
  }
}

/** Builds (or rebuilds) the plugin into the pane, with its events wired to the C# side. */
async function _mountSpectrogram(instance, dotnetRef) {
  const sp = instance.spectro
  const Plugin = await _loadSpectrogramPlugin()
  sp.plugin?.destroy()
  sp.pane.querySelectorAll(':scope > :not(canvas.ws-spectro-overlay)').forEach(el => el.remove())

  const safe = (fn) => fn.catch(() => {})
  const plugin = instance.ws.registerPlugin(Plugin.create(_spectrogramOptions(instance, sp.settings, sp.pane)))
  plugin.on('progress', (fraction) =>
    safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramProgress', Math.round((fraction ?? 0) * 100))))
  plugin.on('ready', () => {
    _drawSpectrogramOverlay(instance)
    safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramReady'))
  })
  plugin.on('error', () => safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramReady')))
  sp.plugin = plugin
  _syncPanesToWaveform(instance)
}

/**
 * Keeps the panes under the waveform scrolled with it.
 *
 * The timeline (in the "-tl" div since the audio walk, so it is never clipped) and the spectrogram
 * pane both lay their contents out across the recording's whole width, in the waveform's pixels,
 * and draw the part near the waveform's scroll position. But nothing scrolled them. Zoomed in, the
 * time ticks and the spectrogram stayed at the start of the recording while the waveform moved on
 * (found 10/26/2026; the timeline had done this since it moved out of the waveform).
 *
 * Each pane's scrolling box gets the waveform's scroll position. A spacer keeps its scroll width at
 * least the waveform's, since a box only scrolls as far as its content reaches and these draw lazily.
 * The spectrogram's frequency labels are shifted back so they stay at the left edge.
 */
function _syncPanesToWaveform(instance) {
  if (!instance) return
  const scrollEl = _wsScrollEl(document.getElementById(instance.containerId))
  if (!scrollEl) return
  const left = scrollEl.scrollLeft
  const width = scrollEl.scrollWidth

  const follow = (box) => {
    if (!box) return
    let spacer = box.querySelector(':scope > .ws-scroll-spacer')
    if (!spacer) {
      spacer = document.createElement('div')
      spacer.className = 'ws-scroll-spacer'
      spacer.style.cssText = 'position:absolute;top:0;height:1px;width:1px;pointer-events:none;'
      box.appendChild(spacer)
    }
    spacer.style.left = `${Math.max(0, width - 1)}px`
    box.scrollLeft = left
  }

  follow(document.getElementById(`${instance.containerId}-tl`)?.querySelector('[part="timeline"]'))

  const inner = instance.spectro?.pane?.querySelector(':scope > div')
  if (inner) {
    follow(inner)
    const labels = inner.querySelector('[part="spec-labels"]')
    if (labels) labels.style.transform = `translateX(${inner.scrollLeft}px)`
  }
}

/**
 * Shows or hides the spectrogram under the waveform.
 *
 * @param {string}                containerId  Player container ID
 * @param {boolean}               enable       true = show, false = remove
 * @param {boolean}               showLabels   Frequency-axis labels
 * @param {number}                fftSamples   FFT window size (128–4096)
 * @param {DotNetObjectReference} dotnetRef    progress / ready / right-click callbacks
 * @param {string}                colormap     jet | viridis | inferno | grayscale
 * @param {boolean}               melScale     mel-spaced frequency axis
 */
export async function toggleSpectrogram(containerId, enable, showLabels, fftSamples, dotnetRef, colormap, melScale) {
  const instance = instances.get(containerId)
  if (!instance) return

  if (!enable) {
    if (instance.spectro?.pane) instance._resizeSpectroOverlay?.unobserve(instance.spectro.pane)
    instance.spectro?.plugin?.destroy()
    instance.spectro?.pane?.remove()
    _restorePlayerHeight(containerId, instance)
    delete instance.spectro
    return
  }
  if (instance.spectro) return

  const waveformEl = document.getElementById(containerId)
  if (!waveformEl) return

  // The pane goes straight under the waveform; the player grows by its height so the waveform
  // keeps its own, unless that would pass the player's max-height.
  const pane = document.createElement('div')
  pane.id = `${containerId}-spectro-wrapper`
  pane.className = 'ws-spectro-pane'
  pane.style.cssText = `position:relative;width:100%;height:${_spectroHeight(instance)}px;cursor:context-menu;`
  const overlay = document.createElement('canvas')
  overlay.className = 'ws-spectro-overlay'
  overlay.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;pointer-events:none;z-index:5;'
  pane.appendChild(overlay)
  waveformEl.parentNode.insertBefore(pane, waveformEl.nextSibling)
  _growPlayerHeight(containerId, instance, _spectroHeight(instance))

  const safe = (fn) => fn.catch(() => {})
  pane.addEventListener('contextmenu', (ev) => {
    ev.preventDefault()
    ev.stopPropagation()
    safe(dotnetRef.invokeMethodAsync('OnWsSpectrogramContextMenu', ev.clientX, ev.clientY))
  })

  instance.spectro = {
    pane,
    overlay,
    plugin: null,
    dotnetRef,
    settings: { showLabels, fftSamples, colormap: colormap ?? 'jet', melScale: melScale ?? false },
    showVoiceBand: instance.spectro?.showVoiceBand ?? instance._showVoiceBand ?? false,
  }
  instance._resizeSpectroOverlay ??= new ResizeObserver(() => _drawSpectrogramOverlay(instance))
  instance._resizeSpectroOverlay.observe(pane)
  // Light / dark switches the token the overlay is drawn in; a canvas has to be told.
  instance._themeWatch ??= new MutationObserver(() => _drawSpectrogramOverlay(instance))
  instance._themeWatch.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] })

  await _mountSpectrogram(instance, dotnetRef)
}

/**
 * Makes the spectrogram taller or shorter (phase 7). The player grows or shrinks by the same
 * amount, so the waveform keeps its height. Remembered while the spectrogram is off.
 */
export async function setSpectrogramHeight(containerId, px) {
  const instance = instances.get(containerId)
  if (!instance) return
  const height = Math.max(100, Math.min(480, Math.round(px)))
  const before = _spectroHeight(instance)
  instance._spectroHeight = height
  const sp = instance.spectro
  if (!sp || height === before) return

  sp.pane.style.height = `${height}px`
  const playerEl = document.getElementById(containerId)?.parentElement
  if (playerEl) {
    const maxH = parseFloat(getComputedStyle(playerEl).maxHeight) || 9999
    playerEl.style.height = `${Math.min(playerEl.offsetHeight + (height - before), maxH)}px`
  }
  // Not awaited: the plugin's redraw can take its time, and the page waited on it, so the new
  // height was never saved (10/26/2026). The pane already has its height; the picture follows.
  try { Promise.resolve(sp.plugin?.setOptions({ height })).catch(() => {}) } catch { /* the pane's size stands */ }
  instance.ws.setOptions({ height: 'auto' })
  _drawSpectrogramOverlay(instance)
}

/**
 * What to mark on the spectrogram (phase 7): candidates and markers as tinted bands with firm
 * edges, so a sound can be found in the picture where it was found in the list.
 * @param {Array<{start:number,end:number,kind:string}>} spans  kind: candidate | confirmed | marker
 */
export function setSpectrogramOverlays(containerId, spans) {
  const instance = instances.get(containerId)
  if (!instance) return
  instance._spectroSpans = Array.isArray(spans) ? spans : []
  _drawSpectrogramOverlay(instance)
}

/** A setting the plugin can change in place; the picture redraws from what it already computed. */
async function _setSpectrogramOption(containerId, change, options) {
  const instance = instances.get(containerId)
  const sp = instance?.spectro
  if (!sp?.plugin) return
  Object.assign(sp.settings, change)
  await sp.plugin.setOptions(options)
  _drawSpectrogramOverlay(instance)
}

export async function setSpectrogramResolution(containerId, fftSamples, showLabels, dotnetRef, colormap) {
  await _setSpectrogramOption(containerId, { fftSamples }, { fftSamples })
}

export async function setSpectrogramColormap(containerId, colormap, showLabels, fftSamples, dotnetRef) {
  await _setSpectrogramOption(containerId, { colormap },
    { colorMap: colormap === 'grayscale' ? 'gray' : _makeColormap(colormap) })
}

export async function setSpectrogramMelScale(containerId, useMel, dotnetRef) {
  const scale = useMel ? 'mel' : 'linear'
  await _setSpectrogramOption(containerId, { melScale: useMel, scale }, { scale })
}

/**
 * The plugin's other analysis and display settings, for the editor's Display tab (phase 2).
 * Only the names below are passed on; anything else in `opts` is ignored.
 *
 * @param {string} containerId
 * @param {object} opts  any of: scale ('linear'|'logarithmic'|'mel'|'bark'|'erb'), frequencyMin,
 *                       frequencyMax, windowFunc, gainDB, rangeDB, preEmphasis, splitChannels
 */
export async function setSpectrogramAnalysis(containerId, opts) {
  const allowed = ['scale', 'frequencyMin', 'frequencyMax', 'windowFunc', 'gainDB', 'rangeDB', 'preEmphasis', 'splitChannels']
  const change = Object.fromEntries(Object.entries(opts ?? {}).filter(([k, v]) => allowed.includes(k) && v !== null && v !== undefined))
  if (Object.keys(change).length === 0) return
  const settings = { ...change }
  if (change.scale) settings.melScale = change.scale === 'mel'
  await _setSpectrogramOption(containerId, settings, change)
}

/** Labels are fixed when the plugin is made, so turning them on or off builds it again. */
export async function updateSpectrogramLabels(containerId, showLabels) {
  const instance = instances.get(containerId)
  const sp = instance?.spectro
  if (!sp || sp.settings.showLabels === showLabels) return
  sp.settings.showLabels = showLabels
  await _mountSpectrogram(instance, sp.dotnetRef)
}

/**
 * Which frequencies the voice band covers: the range the EVP scan listens in (phase 5). Remembered
 * while the spectrogram is off, like whether the band shows.
 */
export function setVoiceBand(containerId, lowHz, highHz) {
  const instance = instances.get(containerId)
  if (!instance || !(highHz > lowHz)) return
  instance._voiceBandHz = [lowHz, highHz]
  _drawSpectrogramOverlay(instance)
}

/**
 * How far a background EVP scan has got, drawn over the waveform (phase 6): the part not yet
 * settled is dimmed, and the stretch being worked on is hatched. Fractions of the recording, 0–1;
 * null clears it. The layers sit inside WaveSurfer's own scrolling wrapper and are placed in
 * percent, so they follow zoom and scroll without being redrawn. They take no clicks.
 */
export function setScanProgress(containerId, processed, settled, state) {
  const instance = instances.get(containerId)
  const wrapper = instance?.ws?.getWrapper?.()
  if (!wrapper) return
  let mask = instance._scanMask
  if (processed == null) {
    mask?.remove()
    instance._scanMask = null
    return
  }
  if (!mask || !mask.isConnected) {
    mask = document.createElement('div')
    mask.className = 'ws-scan-mask'
    mask.setAttribute('aria-hidden', 'true')
    mask.style.cssText = 'position:absolute;inset:0;pointer-events:none;z-index:4;'
    const dim = document.createElement('div')
    dim.dataset.part = 'unscanned'
    dim.style.cssText = 'position:absolute;top:0;bottom:0;right:0;'
      + 'background:color-mix(in srgb, var(--ben-bg, #fff) 60%, transparent);'
    const working = document.createElement('div')
    working.dataset.part = 'scanning'
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    working.style.cssText = 'position:absolute;top:0;bottom:0;'
      + 'background:repeating-linear-gradient(135deg, rgba(var(--ben-audio-candidate-rgb, 217, 119, 6), .22) 0 6px, transparent 6px 12px);'
      + 'background-size:17px 17px;' + (reduce ? '' : 'animation:ws-scan-hatch 1s linear infinite;')
    mask.append(dim, working)
    // The wrapper lives in WaveSurfer's shadow root, so the keyframes go there too.
    const root = wrapper.getRootNode?.()
    const host = root instanceof ShadowRoot ? root : document.head
    if (!host.querySelector('#ws-scan-hatch-keyframes')) {
      const style = document.createElement('style')
      style.id = 'ws-scan-hatch-keyframes'
      style.textContent = '@keyframes ws-scan-hatch { to { background-position: 17px 0; } }'
      host.appendChild(style)
    }
    if (getComputedStyle(wrapper).position === 'static') wrapper.style.position = 'relative'
    wrapper.appendChild(mask)
    instance._scanMask = mask
  }
  const p = Math.max(0, Math.min(1, processed)) * 100
  const s = Math.max(0, Math.min(p / 100, settled ?? 0)) * 100
  mask.dataset.state = state ?? 'scanning'
  const [dim, working] = mask.children
  dim.style.left = `${s}%`
  working.style.left = `${s}%`
  working.style.width = `${Math.max(0, p - s)}%`
}

/** Shows or hides the voice band over the spectrogram. Remembered while the spectrogram is off. */
export function toggleVoiceBandOverlay(containerId, show) {
  const instance = instances.get(containerId)
  if (!instance) return
  instance._showVoiceBand = show
  if (instance.spectro) instance.spectro.showVoiceBand = show
  _drawSpectrogramOverlay(instance)
}

/**
 * The plugin's frequency-to-scale mapping, copied from spectrogram-render-utils.ts (hzToScale and
 * the functions it calls, 8.0.3) so the voice band sits where the picture puts those frequencies on
 * every scale the plugin offers. If the plugin changes these, change them here.
 */
const _ERB_A = (1000 * Math.log(10)) / (24.7 * 4.37)
const _BARK_ZERO_OFFSET = 0.15 * (2 + 0.53) - 0.53
function _hzToScale(hz, scale) {
  switch (scale) {
    case 'mel':         return 2595 * Math.log10(1 + hz / 700)
    case 'logarithmic': return Math.log10(Math.max(1, hz))
    case 'erb':         return _ERB_A * Math.log10(1 + hz * 0.00437)
    case 'bark': {
      let bark = (26.81 * hz) / (1960 + hz) - 0.53
      if (bark < 2) bark += 0.15 * (2 - bark)
      if (bark > 20.1) bark += 0.22 * (bark - 20.1)
      return bark - _BARK_ZERO_OFFSET
    }
    default:            return hz
  }
}

/** Redraws the overlay canvas: the voice band, at the pane's current size. */
function _drawSpectrogramOverlay(instance) {
  const sp = instance?.spectro
  if (!sp) return
  const canvas = sp.overlay
  const W = canvas.clientWidth, H = canvas.clientHeight
  if (!W || !H) return
  const dpr = window.devicePixelRatio || 1
  if (canvas.width !== Math.round(W * dpr) || canvas.height !== Math.round(H * dpr)) {
    canvas.width = Math.round(W * dpr)
    canvas.height = Math.round(H * dpr)
  }
  const ctx = canvas.getContext('2d')
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, W, H)
  _drawSpectrogramSpans(instance, ctx, W, H)
  if (!sp.showVoiceBand) return

  const sampleRate = instance.ws.getDecodedData?.()?.sampleRate
  if (!sampleRate) return
  // The axis runs from frequencyMin to frequencyMax (Nyquist when unset), as the plugin draws it.
  const scale = sp.settings.scale ?? (sp.settings.melScale ? 'mel' : 'linear')
  const fMin = sp.settings.frequencyMin ?? 0
  const fMax = sp.settings.frequencyMax || sampleRate / 2
  const lo = _hzToScale(fMin, scale), hi = _hzToScale(fMax, scale)
  const yFor = (hz) => H - ((_hzToScale(hz, scale) - lo) / (hi - lo)) * H

  const band = instance._voiceBandHz ?? VOICE_BAND_HZ
  const yTop = Math.max(0, yFor(band[1]))
  const yBottom = Math.min(H, yFor(band[0]))
  if (yBottom <= yTop) return
  // A light fill and firm edges: on a recording sampled at 8 kHz the band is most of the picture,
  // and a 15% tint alone over the spectrogram's blues barely showed (10/26/2026).
  // A canvas cannot read var(), so the theme's token is resolved here, on every draw: switching
  // between light and dark redraws the overlay with the other color.
  const voice = _themeRgb('--ben-audio-voice-rgb', '52, 211, 153')
  ctx.fillStyle = `rgba(${voice},0.12)`
  ctx.fillRect(0, yTop, W, yBottom - yTop)
  ctx.strokeStyle = `rgba(${voice},0.95)`
  ctx.lineWidth = 1.5
  ctx.setLineDash([6, 4])
  ctx.beginPath()
  ctx.moveTo(0, yTop + 0.75);    ctx.lineTo(W, yTop + 0.75)
  ctx.moveTo(0, yBottom - 0.75); ctx.lineTo(W, yBottom - 0.75)
  ctx.stroke()
  ctx.setLineDash([])
}

/**
 * The candidates and markers on the spectrogram, placed from the waveform's own scroll position and
 * width, so they line up with the picture at any zoom (phase 7).
 */
function _drawSpectrogramSpans(instance, ctx, W, H) {
  const spans = instance._spectroSpans
  if (!spans?.length) return
  const duration = instance.ws.getDuration?.()
  const scrollEl = _wsScrollEl(document.getElementById(instance.containerId))
  if (!duration || !scrollEl?.scrollWidth) return
  const pxPerSec = scrollEl.scrollWidth / duration
  const left = scrollEl.scrollLeft

  const tokens = {
    candidate: _themeRgb('--ben-audio-candidate-rgb', '217, 119, 6'),
    confirmed: _themeRgb('--ben-audio-confirmed-rgb', '5, 150, 105'),
    marker:    _themeRgb('--ben-audio-possible-rgb', '100, 116, 139'),
  }
  for (const span of spans) {
    const x1 = span.start * pxPerSec - left
    const x2 = Math.max(x1 + 2, span.end * pxPerSec - left)
    if (x2 < 0 || x1 > W) continue
    const rgb = tokens[span.kind] ?? tokens.marker
    ctx.fillStyle = `rgba(${rgb},0.16)`
    ctx.fillRect(x1, 0, x2 - x1, H)
    ctx.fillStyle = `rgba(${rgb},0.85)`
    ctx.fillRect(x1, 0, 1, H)
    ctx.fillRect(x2 - 1, 0, 1, H)
  }
}

/** Grows the player by `extra` px so the waveform keeps its height, within the player's max-height. */
function _growPlayerHeight(containerId, instance, extra) {
  const playerEl = document.getElementById(containerId)?.parentElement
  if (!playerEl || instance._savedPlayerHeight !== undefined) return
  const maxH = parseFloat(getComputedStyle(playerEl).maxHeight) || 9999
  instance._savedPlayerHeight = playerEl.style.height
  playerEl.style.height = `${Math.min(playerEl.offsetHeight + extra, maxH)}px`
  instance.ws.setOptions({ height: 'auto' })
}

function _restorePlayerHeight(containerId, instance) {
  const playerEl = document.getElementById(containerId)?.parentElement
  if (!playerEl || instance._savedPlayerHeight === undefined) return
  playerEl.style.height = instance._savedPlayerHeight
  delete instance._savedPlayerHeight
  instance.ws.setOptions({ height: 'auto' })
}

/**
 * Switches what click-and-drag does on the waveform: 'region' (default) draws a
 * selection region for clipping/editing; 'scrub' moves the playhead live, playing
 * audio audibly while dragging (temporarily starting playback if paused, restoring
 * the prior paused/playing state on release). Only meaningful when the player was
 * created with plugins.regionsDragToCreate — that flag registers the pointer
 * handlers this mode switch controls.
 */
/**
 * The minimap: the whole recording in a strip under the waveform, with the part in view outlined,
 * to drag around a long recording while zoomed in (phase 7).
 */
export async function toggleMinimap(containerId, show) {
  const instance = instances.get(containerId)
  if (!instance?.ws) return
  if (!show) {
    instance.minimap?.destroy()
    instance.minimap = null
    instance._minimapPane?.remove()
    instance._minimapPane = null
    _restorePlayerHeightBy(containerId, MINIMAP_HEIGHT)
    return
  }
  if (instance.minimap) return
  if (!MinimapPlugin) MinimapPlugin = (await import('/js/wavesurfer/plugins/minimap.esm.js')).default

  // A pane of its own under the waveform, like the spectrogram's. Left to itself the plugin goes
  // inside the waveform's shadow DOM, where the waveform's fixed height hides it.
  const waveformEl = document.getElementById(containerId)
  if (!waveformEl) return
  const pane = document.createElement('div')
  pane.id = `${containerId}-minimap`
  pane.className = 'ws-minimap-pane'
  pane.style.cssText = `width:100%;height:${MINIMAP_HEIGHT}px;margin-top:2px;`
  waveformEl.parentNode.insertBefore(pane, waveformEl.nextSibling)
  instance._minimapPane = pane
  _growPlayerHeightBy(containerId, MINIMAP_HEIGHT)

  const accent = _themeRgb('--ben-accent-rgb', '91, 61, 245')
  instance.minimap = instance.ws.registerPlugin(MinimapPlugin.create({
    container: pane,
    height: MINIMAP_HEIGHT,
    waveColor: `rgba(${accent},0.35)`,
    progressColor: `rgba(${accent},0.7)`,
    overlayColor: `rgba(${accent},0.12)`,
  }))
}

const MINIMAP_HEIGHT = 36

/** Grows the player by a pane's height so the waveform keeps its own (the minimap's). */
function _growPlayerHeightBy(containerId, px) {
  const instance = instances.get(containerId)
  const playerEl = document.getElementById(containerId)?.parentElement
  if (!instance || !playerEl) return
  const maxH = parseFloat(getComputedStyle(playerEl).maxHeight) || 9999
  playerEl.style.height = `${Math.min(playerEl.offsetHeight + px, maxH)}px`
  instance.ws.setOptions({ height: 'auto' })
}

function _restorePlayerHeightBy(containerId, px) {
  const instance = instances.get(containerId)
  const playerEl = document.getElementById(containerId)?.parentElement
  if (!instance || !playerEl) return
  playerEl.style.height = `${Math.max(0, playerEl.offsetHeight - px)}px`
  instance.ws.setOptions({ height: 'auto' })
}

/**
 * Drawing the volume (phase 7): a line over the waveform with points to drag; double-click adds
 * one, double-click a point removes it. It starts flat at full volume, and playback follows it as
 * it is drawn. It can only turn sound down: full volume is the top.
 * While it is up, dragging the waveform does not draw regions or scrub.
 */
export async function startEnvelope(containerId) {
  const instance = instances.get(containerId)
  const ws = instance?.ws
  if (!ws || instance._envelope) return
  const duration = ws.getDuration()
  if (!duration) return
  if (!EnvelopePlugin) EnvelopePlugin = (await import('/js/wavesurfer/plugins/envelope.esm.js')).default
  instance._volumeBeforeEnvelope = ws.getVolume()
  const accent = _themeRgb('--ben-accent-rgb', '91, 61, 245')
  instance._envelope = ws.registerPlugin(EnvelopePlugin.create({
    // Points at the very start and end, so nothing fades in from silence: the plugin treats the
    // stretch before its first point and after its last as silent.
    points: [{ time: 0, volume: 1 }, { time: duration, volume: 1 }],
    volume: 1,
    lineColor: `rgba(${accent},0.9)`,
    lineWidth: '3',
    dragPointSize: 14,
    dragPointFill: `rgba(${accent},0.9)`,
    dragPointStroke: 'rgba(255,255,255,0.9)',
  }))
}

/** The curve as drawn: [{ time, volume }], sorted by time. Empty when it is not up. */
export function getEnvelopePoints(containerId) {
  const env = instances.get(containerId)?._envelope
  if (!env) return []
  return env.getPoints()
    .map(p => ({ time: p.time, volume: Math.max(0, Math.min(1, p.volume)) }))
    .sort((a, b) => a.time - b.time)
}

/** Puts the curve away and the volume back where it was. */
export function stopEnvelope(containerId) {
  const instance = instances.get(containerId)
  if (!instance?._envelope) return
  instance._envelope.destroy()
  instance._envelope = null
  instance.ws.setVolume(instance._volumeBeforeEnvelope ?? 1)
}

export function setDragMode(containerId, mode) {
  const instance = instances.get(containerId)
  if (!instance) return
  instance._dragMode = mode === 'scrub' ? 'scrub' : 'region'
  const container = document.getElementById(containerId)
  if (container) container.style.cursor = instance._dragMode === 'scrub' ? 'ew-resize' : ''
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
    instance.ws.zoom(_currentZoom(instance))
  }
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

/** Toggles the voice-frequency-band overlay and forces a redraw so it appears/disappears immediately. */

/**
 * Detects low-amplitude (silent) stretches of the decoded audio and shades them as
 * light, non-interactive regions on the waveform. Replaces any previously-detected
 * silence regions. Requires the track to already be decoded (i.e. loaded into the player).
 */
export function detectSilence(containerId, thresholdDb, windowSeconds) {
  const instance = instances.get(containerId)
  if (!instance?.ws || !instance.regionsPlugin) return

  clearSilenceDetection(containerId)

  const audioBuffer = instance.ws.getDecodedData?.()
  if (!audioBuffer) return

  const sampleRate    = audioBuffer.sampleRate
  const channels      = []
  for (let c = 0; c < audioBuffer.numberOfChannels; c++) channels.push(audioBuffer.getChannelData(c))
  const windowSize     = Math.max(1, Math.floor((windowSeconds ?? 0.15) * sampleRate))
  const totalSamples   = audioBuffer.length
  const thresholdLinear = Math.pow(10, thresholdDb / 20)

  const ids = []
  let runStart = null

  const closeRun = (runEndSample) => {
    const startTime = runStart / sampleRate
    const endTime   = runEndSample / sampleRate
    runStart = null
    if (endTime - startTime < 0.1) return  // skip tiny blips
    const id = `silence-${Math.round(startTime * 1000)}`
    // Before addRegion: the plugin fires region-created synchronously from inside it.
    _rememberRegionKind(containerId, id, 'silence')
    instance.regionsPlugin.addRegion({
      id, start: startTime, end: endTime,
      color: 'rgba(var(--ben-audio-silence-rgb, 148, 163, 184),0.22)',
      drag: false, resize: false,
    })
    ids.push(id)
  }

  for (let start = 0; start < totalSamples; start += windowSize) {
    const end = Math.min(start + windowSize, totalSamples)
    let sumSquares = 0
    let count = 0
    for (const data of channels) {
      for (let i = start; i < end; i++) { sumSquares += data[i] * data[i]; count++ }
    }
    const rms = count > 0 ? Math.sqrt(sumSquares / count) : 0

    if (rms < thresholdLinear) {
      if (runStart === null) runStart = start
    } else if (runStart !== null) {
      closeRun(start)
    }
  }
  if (runStart !== null) closeRun(totalSamples)

  instance._silenceRegionIds = ids
}

/** Removes regions previously added by {@link detectSilence}. */
export function clearSilenceDetection(containerId) {
  const instance = instances.get(containerId)
  if (!instance) return
  for (const id of instance._silenceRegionIds ?? [])
    instance.regionsPlugin?.getRegions().find((r) => r.id === id)?.remove()
  instance._silenceRegionIds = []
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

export function destroy(containerId) {
  const instance = instances.get(containerId)
  if (!instance) return
  instance._regionPlayCleanup?.()
  instance.dragCleanup?.()
  instance.resizeObserver?.disconnect()
  instance._paneSyncUnsubs?.forEach(u => u?.())
  instance.spectro?.plugin?.destroy()
  instance.minimap?.destroy()
  instance._minimapPane?.remove()
  instance._envelope?.destroy()
  instance._resizeSpectroOverlay?.disconnect()
  instance._themeWatch?.disconnect()
  try { instance.audioCtx?.close() } catch { /* ignore */ }
  // Final in WaveSurfer 8: a destroyed instance cannot be loaded again. The editor and every player
  // make a new one through create(), so nothing here reuses it.
  try { instance.ws?.destroy() } catch { /* ignore */ }
  instances.delete(containerId)
}

// ── Phase C: Web Audio processing chain ──────────────────────────────────────

const EQ_FREQS = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000]

export async function initAudioProcessing(containerId) {
  const inst = instances.get(containerId)
  if (!inst || inst.audioCtx) return

  const mediaEl = inst.ws?.getMediaElement?.()
  if (!mediaEl) return

  const audioCtx = new AudioContext()

  // Register noise gate worklet (non-fatal if unsupported)
  let noiseGateNode = null
  try {
    await audioCtx.audioWorklet.addModule('/js/wavesurfer/noise-gate-processor.js')
    noiseGateNode = new AudioWorkletNode(audioCtx, 'noise-gate-processor')
    noiseGateNode.port.postMessage({ enabled: false }) // disabled by default
  } catch { /* AudioWorklet not supported — skip noise gate */ }

  const source = audioCtx.createMediaElementSource(mediaEl)

  const eqFilters = EQ_FREQS.map((freq, i) => {
    const f    = audioCtx.createBiquadFilter()
    f.type     = i === 0 ? 'lowshelf' : i === EQ_FREQS.length - 1 ? 'highshelf' : 'peaking'
    f.frequency.value = freq
    f.gain.value      = 0
    f.Q.value         = 1.4
    return f
  })

  const hpFilter = audioCtx.createBiquadFilter()
  hpFilter.type  = 'highpass'
  hpFilter.frequency.value = 20
  hpFilter.Q.value = 0.5

  const lpFilter = audioCtx.createBiquadFilter()
  lpFilter.type  = 'lowpass'
  lpFilter.frequency.value = 20000
  lpFilter.Q.value = 0.5

  const compressor = audioCtx.createDynamicsCompressor()
  compressor.threshold.value = -24
  compressor.knee.value      = 30
  compressor.ratio.value     = 1
  compressor.attack.value    = 0.003
  compressor.release.value   = 0.25

  const outputGain = audioCtx.createGain()
  outputGain.gain.value = 1.0

  // source → [noiseGate →] hp → eq[0..9] → lp → compressor → outputGain → destination
  let prev = source
  if (noiseGateNode) { prev.connect(noiseGateNode); prev = noiseGateNode }
  prev.connect(hpFilter);    prev = hpFilter
  for (const f of eqFilters) { prev.connect(f); prev = f }
  prev.connect(lpFilter);    prev = lpFilter
  prev.connect(compressor);  prev = compressor
  prev.connect(outputGain)
  outputGain.connect(audioCtx.destination)

  inst.audioCtx      = audioCtx
  inst.eqFilters     = eqFilters
  inst.hpFilter      = hpFilter
  inst.lpFilter      = lpFilter
  inst.compressor    = compressor
  inst.outputGain    = outputGain
  inst.noiseGateNode = noiseGateNode
}

export function setEqBand(containerId, bandIndex, gainDb) {
  const inst = instances.get(containerId)
  if (!inst?.eqFilters) return
  const f = inst.eqFilters[bandIndex]
  if (f) f.gain.value = gainDb
}

export function setAllEqBands(containerId, gains) {
  const inst = instances.get(containerId)
  if (!inst?.eqFilters) return
  gains.forEach((g, i) => { if (inst.eqFilters[i]) inst.eqFilters[i].gain.value = g })
}

export function setHighPass(containerId, freqHz, enabled) {
  const inst = instances.get(containerId)
  if (!inst?.hpFilter) return
  inst.hpFilter.frequency.value = enabled ? freqHz : 20
}

export function setLowPass(containerId, freqHz, enabled) {
  const inst = instances.get(containerId)
  if (!inst?.lpFilter) return
  inst.lpFilter.frequency.value = enabled ? freqHz : 20000
}

export function setCompressor(containerId, enabled, threshold, ratio, attack, release) {
  const inst = instances.get(containerId)
  if (!inst?.compressor) return
  inst.compressor.threshold.value = enabled ? threshold : 0
  inst.compressor.ratio.value     = enabled ? ratio     : 1
  inst.compressor.attack.value    = attack
  inst.compressor.release.value   = release
}

// ── Spectrogram colormap ──────────────────────────────────────────────────────

// Generates a 256-entry RGBA colormap array for WaveSurfer's spectrogram plugin.
function _makeColormap(name) {
  const map = []
  for (let i = 0; i < 256; i++) {
    const t = i / 255
    let r, g, b
    switch (name) {
      case 'grayscale':
        r = g = b = t; break
      case 'viridis': {
        // simplified viridis approximation
        const stops = [[0.267,0.005,0.329],[0.190,0.407,0.554],[0.208,0.678,0.473],[0.678,0.863,0.190],[0.993,0.906,0.144]]
        const si = Math.min(Math.floor(t * (stops.length - 1)), stops.length - 2)
        const u  = t * (stops.length - 1) - si
        ;[r, g, b] = stops[si].map((v, j) => v + u * (stops[si + 1][j] - v))
        break
      }
      case 'inferno': {
        const stops = [[0,0,0],[0.278,0.039,0.439],[0.627,0.078,0.353],[0.906,0.361,0.078],[0.988,0.729,0.208],[0.988,1,0.643]]
        const si = Math.min(Math.floor(t * (stops.length - 1)), stops.length - 2)
        const u  = t * (stops.length - 1) - si
        ;[r, g, b] = stops[si].map((v, j) => v + u * (stops[si + 1][j] - v))
        break
      }
      default: { // jet
        const u = t * 4
        r = Math.min(Math.max(Math.min(u - 1.5, 4.5 - u), 0), 1)
        g = Math.min(Math.max(Math.min(u - 0.5, 3.5 - u), 0), 1)
        b = Math.min(Math.max(Math.min(u + 0.5, 2.5 - u), 0), 1)
        break
      }
    }
    map.push([r, g, b, 1])
  }
  return map
}

export function setNoiseGate(containerId, enabled, threshold, attack, release) {
  const inst = instances.get(containerId)
  if (!inst?.noiseGateNode) return
  inst.noiseGateNode.port.postMessage({ enabled })
  const params = inst.noiseGateNode.parameters
  params.get('threshold').value = threshold
  params.get('attack').value    = attack
  params.get('release').value   = release
}
