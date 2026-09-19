/**
 * livePreviewInterop.js
 *
 * The sequence player: plays the timeline itself, by seeking between the source files, instead of
 * playing a proxy the editor re-encoded after the last edit.
 *
 * Served at: /_content/Ben.Video.Editor/js/livePreviewInterop.js
 *
 * Why the whole loop lives here
 * ────────────────────────────
 * A player runs at the display's refresh rate. Asking .NET what to show on each frame would cost
 * more in interop than the drawing does, so C# resolves the timeline once into a plan
 * (LivePlaybackPlan) and this follows it, reporting the clock back about ten times a second so the
 * playhead and the counter keep up.
 *
 * Two video elements
 * ──────────────────
 * Seeking a video element takes long enough to see. So while one plays, the next clip is loaded
 * and seeked in the other, and the cut is a swap of which one is visible — the difference between
 * a cut and a stutter.
 *
 * API
 * ───
 *  create(container, dotnet, timeMethod) → id
 *  setPlan(id, plan, urls)   → void   Replace what is being played, keeping the clock
 *  play(id) / pause(id)      → void
 *  seek(id, seconds)         → void
 *  dispose(id)               → void
 */

const players = new Map()
let nextId = 1

/** How far the element may drift from where the plan says it should be before it is re-seeked. */
const DRIFT_TOLERANCE = 0.25

/** How long before a cut the next clip is loaded into the idle element. */
const PRELOAD_LEAD = 1.5

/** How often the clock is reported back to .NET, in milliseconds. */
const REPORT_EVERY = 100

/**
 * The largest gap between frames the clock will credit, in seconds.
 *
 * requestAnimationFrame stops entirely while the tab is in the background, so the first frame
 * after somebody comes back is separated from the last one by however long they were away. Without
 * this, a five-minute detour advances the playhead five minutes.
 */
const MAX_FRAME_STEP = 0.25

export function create(container, dotnet, timeMethod) {
  const id = nextId++

  const videos = [...container.querySelectorAll('video')]
  const image = container.querySelector('img')

  const player = {
    container,
    dotnet,
    timeMethod,
    videos,
    image,
    audio: new Map(),      // clip id → HTMLAudioElement
    plan: { picture: [], audio: [], duration: 0 },
    urls: {},
    time: 0,
    playing: false,
    active: 0,             // which of the two video elements is on screen
    raf: null,
    lastFrame: 0,
    lastReport: 0,
  }

  players.set(id, player)
  return id
}

export function setPlan(id, plan, urls) {
  const player = players.get(id)
  if (!player) return

  // The plan arrives from .NET with .NET's casing.
  player.plan = {
    picture: (plan?.picture ?? []).map(normaliseSegment),
    audio: (plan?.audio ?? []).map(normaliseAudio),
    duration: plan?.duration ?? 0,
  }
  player.urls = urls ?? {}

  // Everything on screen belongs to the old plan; forget it so the next frame re-decides rather
  // than leaving a clip that has been deleted still showing.
  for (const v of player.videos) v.dataset.segment = ''

  releaseAudioNotIn(player)
  apply(player, true)
}

export function play(id) {
  const player = players.get(id)
  if (!player) return

  // Playing from the very end starts again, which is what every player does and what somebody
  // pressing play at the end of a timeline means.
  if (player.plan.duration > 0 && player.time >= player.plan.duration - 0.05) player.time = 0

  player.playing = true
  player.lastFrame = performance.now()
  loop(player)
}

export function pause(id) {
  const player = players.get(id)
  if (!player) return

  player.playing = false
  stopEverything(player)
  cancel(player)
  report(player, true)
}

export function seek(id, seconds) {
  const player = players.get(id)
  if (!player) return

  player.time = clampTime(player, seconds)
  apply(player, true)
  report(player, true)

  // A seek while paused still needs one frame of work; a seek while playing is picked up by the
  // loop that is already running.
  if (!player.playing) cancel(player)
}

export function dispose(id) {
  const player = players.get(id)
  if (!player) return

  cancel(player)
  stopEverything(player)

  for (const audio of player.audio.values()) audio.remove()
  player.audio.clear()

  players.delete(id)
}

// ── The loop ─────────────────────────────────────────────────────────────────

function loop(player) {
  cancel(player)

  const step = (now) => {
    if (!player.playing) return

    const elapsed = Math.min(MAX_FRAME_STEP, Math.max(0, (now - player.lastFrame) / 1000))
    player.lastFrame = now
    player.time += elapsed

    if (player.plan.duration > 0 && player.time >= player.plan.duration) {
      player.time = player.plan.duration
      player.playing = false
      apply(player, true)
      stopEverything(player)
      report(player, true)
      return
    }

    apply(player, false)
    report(player, false)

    player.raf = requestAnimationFrame(step)
  }

  player.raf = requestAnimationFrame(step)
}

function cancel(player) {
  if (player.raf !== null) cancelAnimationFrame(player.raf)
  player.raf = null
}

function report(player, force) {
  const now = performance.now()
  if (!force && now - player.lastReport < REPORT_EVERY) return

  player.lastReport = now
  player.dotnet?.invokeMethodAsync(player.timeMethod, player.time, player.playing)
    .catch(() => { /* the component may have been disposed mid-frame */ })
}

// ── What is on screen ────────────────────────────────────────────────────────

function apply(player, hard) {
  const segment = segmentAt(player, player.time)

  applyPicture(player, segment, hard)
  applyAudio(player)
  preloadNext(player, segment)
}

function applyPicture(player, segment, hard) {
  if (!segment || segment.kind === 'Gap') {
    hideAll(player)
    return
  }

  if (segment.kind === 'Image') {
    const url = player.urls[segment.clipId]
    if (!url) { hideAll(player); return }

    if (player.image.src !== url) player.image.src = url
    player.image.style.display = ''
    for (const v of player.videos) { v.style.display = 'none'; v.pause() }
    return
  }

  const url = player.urls[segment.clipId]

  // A source the browser could not open. Black rather than the previous clip frozen on screen,
  // which would read as the timeline still playing that clip.
  if (!url) { hideAll(player); return }

  const key = segmentKey(segment)
  let element = player.videos[player.active]

  if (element.dataset.segment !== key) {
    // Prefer whichever element was preloaded with this segment; that is the whole point of having
    // two of them.
    const other = player.videos[1 - player.active]
    if (other.dataset.segment === key) {
      player.active = 1 - player.active
      element = other
    } else {
      element = load(player.videos[1 - player.active], segment, url)
      player.active = 1 - player.active
    }
  }

  const expected = expectedSourceTime(segment, player.time)

  if (Number.isFinite(element.duration) || element.readyState > 0) {
    if (Math.abs(element.currentTime - expected) > DRIFT_TOLERANCE) element.currentTime = expected
  } else {
    element.currentTime = expected
  }

  element.playbackRate = segment.speed || 1
  element.volume = segment.volume
  element.muted = segment.volume <= 0

  element.style.display = ''
  player.image.style.display = 'none'

  const idle = player.videos[1 - player.active]
  idle.style.display = 'none'
  idle.pause()

  if (player.playing) {
    // A play() interrupted by the next seek rejects; that is expected and not an error worth
    // showing anybody.
    element.play().catch(() => {})
  } else if (!element.paused) {
    element.pause()
  }

  if (hard) element.currentTime = expected
}

function load(element, segment, url) {
  element.dataset.segment = segmentKey(segment)
  if (element.src !== url) element.src = url
  element.currentTime = segment.sourceStart
  return element
}

/**
 * Loads the clip after this one into the element that is not on screen, so the cut is a swap
 * rather than a seek.
 */
function preloadNext(player, segment) {
  if (!segment || !player.playing) return
  if (segment.end - player.time > PRELOAD_LEAD) return

  const next = player.plan.picture.find(s => s.start >= segment.end && s.kind === 'Video')
  if (!next) return

  const url = player.urls[next.clipId]
  if (!url) return

  const idle = player.videos[1 - player.active]
  if (idle.dataset.segment === segmentKey(next)) return

  load(idle, next, url)
}

function hideAll(player) {
  for (const v of player.videos) { v.style.display = 'none'; v.pause() }
  player.image.style.display = 'none'
}

// ── What is audible ──────────────────────────────────────────────────────────

function applyAudio(player) {
  for (const segment of player.plan.audio) {
    const url = player.urls[segment.clipId]
    if (!url) continue

    const inside = player.time >= segment.start && player.time < segment.end
    let element = player.audio.get(segment.clipId)

    if (!inside) {
      if (element && !element.paused) element.pause()
      continue
    }

    if (!element) {
      element = new Audio(url)
      element.preload = 'auto'
      player.audio.set(segment.clipId, element)
    }

    const expected = segment.sourceStart + (player.time - segment.start)

    if (Math.abs(element.currentTime - expected) > DRIFT_TOLERANCE) element.currentTime = expected

    element.volume = segment.volume

    if (player.playing) element.play().catch(() => {})
    else if (!element.paused) element.pause()
  }
}

function stopEverything(player) {
  for (const v of player.videos) v.pause()
  for (const audio of player.audio.values()) audio.pause()
}

/** Drops audio elements whose clip is no longer in the plan, so a deleted clip stops playing. */
function releaseAudioNotIn(player) {
  const wanted = new Set(player.plan.audio.map(a => a.clipId))

  for (const [clipId, element] of player.audio) {
    if (wanted.has(clipId)) continue
    element.pause()
    element.remove()
    player.audio.delete(clipId)
  }
}

// ── Reading the plan ─────────────────────────────────────────────────────────

function normaliseSegment(s) {
  return {
    start: s.start ?? s.Start ?? 0,
    end: s.end ?? s.End ?? 0,
    kind: String(s.kind ?? s.Kind ?? 'Gap'),
    clipId: String(s.clipId ?? s.ClipId ?? ''),
    sourceStart: s.sourceStart ?? s.SourceStart ?? 0,
    speed: s.speed ?? s.Speed ?? 1,
    volume: s.volume ?? s.Volume ?? 0,
  }
}

function normaliseAudio(a) {
  return {
    start: a.start ?? a.Start ?? 0,
    end: a.end ?? a.End ?? 0,
    clipId: String(a.clipId ?? a.ClipId ?? ''),
    sourceStart: a.sourceStart ?? a.SourceStart ?? 0,
    volume: a.volume ?? a.Volume ?? 1,
  }
}

function segmentAt(player, t) {
  return player.plan.picture.find(s => t >= s.start && t < s.end)
    // The last instant of the timeline belongs to the last segment rather than to nothing, so
    // pausing at the end leaves the final frame on screen instead of black.
    ?? (player.plan.picture.length > 0 && t >= player.plan.duration
      ? player.plan.picture[player.plan.picture.length - 1]
      : null)
}

function segmentKey(segment) {
  return `${segment.clipId}@${segment.start}`
}

function expectedSourceTime(segment, t) {
  const elapsed = Math.max(0, t - segment.start)
  return segment.sourceStart + elapsed * (segment.speed || 1)
}

function clampTime(player, seconds) {
  if (!Number.isFinite(seconds)) return 0
  return Math.max(0, Math.min(seconds, player.plan.duration || 0))
}
