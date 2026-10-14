// The Field Kit session player's engine: the browser keeps time (10/14/2026).
//
// WHY THIS EXISTS. The first player kept time on the server: every quarter second each viewer's
// Blazor circuit re-rendered the whole page and made three to six calls back into the browser for
// the map and the media. That is why scrubbing lagged and snapped back, why the map stepped
// instead of gliding, and why every extra person watching cost the server more. Here the browser
// owns the clock, the scrubber, the media and the map at screen rate; the server draws the page
// once and hears from this file only when something rare happens (a recording that will not play,
// a clip placed once its length is known).
//
// WHAT IS TRUE AT A MOMENT is not decided here: fieldKitTimeline.js holds the shared replay rules
// (ProjectNotes/specs/replay-semantics.md), the same ones the phone's review obeys. This file asks
// it for a frame and draws it.
//
// DOM OWNERSHIP, which is what keeps Blazor and this file from fighting:
//   - Blazor renders the structure once. Anything that changes with the playhead is either an
//     element Blazor renders EMPTY (this file fills it), an attribute Blazor renders as a constant
//     (data-testid, hidden), or a child this file creates and removes itself.
//   - This file never removes a node Blazor made. After any Blazor render the host calls rebind().

import { frameAt, stepMarker, markerJump, SIGN_MS } from './fieldKitTimeline.js';
import * as benMap from '../Kit/Maps/BenMap.razor.js';

const SPEEDS = [1, 2, 4, 8, 16, 32];
/** Above this the sound would be gibberish and seeking it would hammer the server; it goes quiet. */
const MEDIA_MAX_RATE = 2;
/** A following recording is re-seeked once it drifts this far from the playhead. */
const DRIFT_S = 0.35;
/** Above MEDIA_MAX_RATE a video still shows its frame, at most this often. */
const FAST_SEEK_MS = 500;
/** While dragging, a recording is seeked at most this often; the last position always lands. */
const DRAG_SEEK_MS = 100;
/** Arrow keys move this far. */
const SKIP_MS = 5000;
/** The longest a buffering recording holds the clock. */
const WAIT_MS = 3000;   // kept for the state() readout; the clock no longer holds for buffering (10/14/2026)
/** How far ahead a recording still to come is buffered, so the hand-over to it does not stall. */
const PRIME_AHEAD_MS = 20000;
/** The most a recording is moved ahead of the playhead to allow for how long its seek takes. */
const MAX_LEAD_S = 1.5;

const ICONS = '/icons/sprite.svg#';
const players = new WeakMap();

// ── The module's surface ─────────────────────────────────────────────────────

/** Starts a player on a root element. The timeline is the server's SessionTimeline as JSON. */
export function init(root, dotnet, timelineJson, options) {
    dispose(root);
    const player = new Player(root, dotnet, JSON.parse(timelineJson), options ?? {});
    players.set(root, player);
    player.start();
}

/** After Blazor re-renders: elements may have been added, removed or replaced. */
export function rebind(root) {
    players.get(root)?.bind();
}

/** A recording's link was renewed: the element is loading again, and the playhead stays where it is. */
export function mediaRenewed(root, fileId) {
    players.get(root)?.renewed(fileId);
}

export function dispose(root) {
    const player = root && players.get(root);
    if (!player) return;
    player.stop();
    players.delete(root);
}

/** What the engine thinks — for a test, never for the page. */
export function state(root) {
    const p = players.get(root);
    if (!p) return null;
    return {
        playhead: p.playhead, playing: p.playing, rate: p.rate, clock: p.clockFile,
        duration: p.tl.durationMs, frames: p.frames, waiting: p.waiting,
    };
}

// ── Formatting, said the way the rest of the site says it ────────────────────

function hms(ms) {
    const total = Math.max(0, Math.floor(ms / 1000));
    const pad = n => String(n).padStart(2, '0');
    return `${pad(Math.floor(total / 3600))}:${pad(Math.floor(total / 60) % 60)}:${pad(total % 60)}`;
}

function compass(degrees) {
    const points = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];
    return points[Math.round((((degrees % 360) + 360) % 360) / 45) % 8];
}

function icon(name, cls) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', `sa-icon ${cls ?? ''}`.trim());
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('role', 'presentation');
    const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', ICONS + name);
    svg.appendChild(use);
    return svg;
}

function el(tag, attrs, ...children) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs ?? {})) {
        if (v === null || v === undefined) continue;
        if (k === 'class') node.className = v; else node.setAttribute(k, v);
    }
    for (const child of children) node.append(child);
    return node;
}

// ── The player ───────────────────────────────────────────────────────────────

class Player {
    constructor(root, dotnet, timeline, options) {
        this.root = root;
        this.dotnet = dotnet;
        this.tl = timeline;
        this.urls = options.urls ?? {};
        this.clipById = new Map(timeline.clips.map(c => [c.fileId, c]));
        this.playhead = 0;
        this.playing = false;
        this.rate = 1;
        this.anchorMs = 0;
        this.anchorAt = 0;
        this.clockFile = null;
        this.waiting = false;
        this.waitingSince = 0;
        this.dragging = false;
        this.frames = 0;
        this.lastSeekAt = new Map();
        this.seekLead = new Map();   // per recording: seconds its last seek took to sound again
        this.pendingPlay = new WeakSet();
        this.attached = new WeakSet();
        this.media = new Map();
        this.last = {};
        this.mapReady = false;
        this.expanded = null;
        this.raf = 0;
        this.timer = 0;
    }

    start() {
        this.bind();
        this.render(true);
        this.lastFrameAt = 0;
        const loop = now => {
            if (!this.root.isConnected) { this.stop(); return; }
            this.lastFrameAt = performance.now();
            this.step(now);
            this.raf = requestAnimationFrame(loop);
        };
        this.raf = requestAnimationFrame(loop);
        // A page that gets no animation frames — a background tab, a window covered by another,
        // a panel that is collapsed — still has its sound playing, so the clock, the readouts and
        // the hand-over between recordings keep going on a plain timer. Keyed on "no frame lately"
        // rather than document.hidden: a covered window can report itself visible and still be
        // given no frames.
        this.timer = setInterval(() => {
            if (!this.root.isConnected) { this.stop(); return; }
            const now = performance.now();
            if (now - this.lastFrameAt > 200) this.step(now);
        }, 250);
    }

    stop() {
        cancelAnimationFrame(this.raf);
        clearInterval(this.timer);
        for (const element of this.media.values()) { try { element.pause(); } catch { } }
        this.closePhoto(false);
        this.playing = false;
    }

    // ── Finding the page's elements (and again after every Blazor render) ────

    bind() {
        const q = sel => this.root.querySelector(sel);
        this.buildTransport(q('[data-fk="transport"]'));
        this.stage = q('[data-fk="stage"]');
        this.stageNote = q('[data-fk="stage-note"]');
        this.stageNoteUse = this.stageNote?.querySelector('use') ?? null;
        this.stageNoteText = q('[data-fk="stage-note-text"]');
        this.readField = q('[data-fk="field"]');
        this.readSound = q('[data-fk="sound"]');
        this.readFacingTitle = q('[data-fk="facing-title"]');
        this.readFacing = q('[data-fk="facing"]');
        this.readPosition = q('[data-fk="position"]');
        this.roomSlot = q('[data-fk="room"]');
        this.photoStrip = q('[data-fk="photos"]');
        this.mapId = q('.ben-map')?.id ?? null;
        this.playheads = [...this.root.querySelectorAll('[data-fk="playhead"]')];
        this.last = {};   // everything is drawn again: elements may be new

        this.media = new Map();
        for (const element of this.root.querySelectorAll('[data-fk-media]')) {
            const fileId = element.getAttribute('data-fk-media');
            this.media.set(fileId, element);
            this.attachMedia(fileId, element);
        }

        for (const button of this.root.querySelectorAll('[data-fk-photo]')) {
            if (this.attached.has(button)) continue;
            this.attached.add(button);
            button.addEventListener('click', () => this.openPhoto(button.getAttribute('data-fk-photo')));
        }
        for (const button of this.root.querySelectorAll('[data-fk-jump]')) {
            if (this.attached.has(button)) continue;
            this.attached.add(button);
            button.addEventListener('click', () => {
                this.pause();
                this.seek(markerJump(Number(button.getAttribute('data-fk-jump'))));
            });
        }
        for (const chart of this.root.querySelectorAll('[data-fk-chart] svg')) {
            if (this.attached.has(chart)) continue;
            this.attached.add(chart);
            this.attachChart(chart);
        }
        if (!this.attached.has(this.root)) {
            this.attached.add(this.root);
            this.root.addEventListener('keydown', e => this.onKey(e));
        }
    }

    buildTransport(host) {
        if (!host || host.getAttribute('data-fk-built') === '1') return;
        host.setAttribute('data-fk-built', '1');
        host.replaceChildren();

        this.playButton = el('button', { type: 'button', class: 'btn btn-primary' });
        this.playButton.addEventListener('click', () => this.toggle());

        const speeds = el('div', { class: 'btn-group btn-group-sm fk-speeds', role: 'group', 'aria-label': 'Speed' });
        this.speedButtons = SPEEDS.map(speed => {
            const button = el('button', { type: 'button', class: 'btn btn-outline-secondary' }, `${speed}×`);
            button.addEventListener('click', () => this.setRate(speed));
            speeds.append(button);
            return button;
        });

        const marks = el('div', { class: 'btn-group btn-group-sm', role: 'group', 'aria-label': 'Marks' });
        const previous = el('button', { type: 'button', class: 'btn btn-outline-secondary', 'aria-label': 'Previous mark', title: 'Previous mark' }, icon('skip-back'));
        const next = el('button', { type: 'button', class: 'btn btn-outline-secondary', 'aria-label': 'Next mark', title: 'Next mark' }, icon('skip-forward'));
        previous.addEventListener('click', () => this.step_marker(false));
        next.addEventListener('click', () => this.step_marker(true));
        marks.append(previous, next);
        if (this.tl.markers.length === 0) { previous.disabled = true; next.disabled = true; }

        this.elapsedLabel = el('span', { class: 'font-monospace', 'data-testid': 'elapsed' }, '00:00:00');
        this.totalLabel = el('span', { class: 'text-muted small' }, `of ${hms(this.tl.durationMs)}`);
        this.clockSlot = el('span');

        const row = el('div', { class: 'd-flex align-items-center gap-2 flex-wrap mb-2' },
            this.playButton, speeds, marks, this.elapsedLabel, this.totalLabel, this.clockSlot);

        this.scrubber = el('input', {
            type: 'range', class: 'form-range', min: '0', max: '1000', step: 'any',
            'aria-label': 'Position in the session', 'data-testid': 'scrubber',
        });
        this.scrubber.value = '0';
        this.scrubber.addEventListener('pointerdown', () => { this.dragging = true; });
        const release = () => { if (this.dragging) { this.dragging = false; this.render(true); } };
        this.scrubber.addEventListener('pointerup', release);
        this.scrubber.addEventListener('pointercancel', release);
        this.scrubber.addEventListener('change', release);
        this.scrubber.addEventListener('input', () => {
            this.seek(Number(this.scrubber.value) / 1000 * this.tl.durationMs, { keepScrubber: true });
        });

        host.append(row, this.scrubber);
        this.drawTransport();
    }

    drawTransport() {
        if (!this.playButton) return;
        this.playButton.replaceChildren(icon(this.playing ? 'pause' : 'play', 'me-1'), this.playing ? 'Pause' : 'Play');
        this.speedButtons.forEach((button, i) => {
            const on = SPEEDS[i] === this.rate;
            button.className = `btn ${on ? 'btn-secondary' : 'btn-outline-secondary'}`;
            button.setAttribute('aria-pressed', on ? 'true' : 'false');
        });
    }

    // ── The clock ─────────────────────────────────────────────────────────────

    reanchor(ms) {
        this.anchorMs = ms;
        this.anchorAt = performance.now();
    }

    /** The recording keeping time, when one covers the playhead and is actually sounding. */
    clockElement(frame) {
        if (!this.playing || this.rate > MEDIA_MAX_RATE || !frame.audio) return null;
        const element = this.media.get(frame.audio.clip.fileId);
        if (!element || element.paused || element.readyState < 3 || element.seeking) return null;
        return { element, clip: frame.audio.clip };
    }

    step(now) {
        if (this.playing) {
            const frame = frameAt(this.tl, this.playhead);
            const clock = this.clockElement(frame);
            // The page's own clock always runs (10/14/2026). It used to stop for up to three seconds
            // whenever the recording was buffering — at every Play on a recording not yet loaded —
            // and then follow the recording from wherever it had got to: 8 s of playing showed 4.9.
            let next = this.anchorMs + (now - this.anchorAt) * this.rate;
            this.clockFile = null;
            if (clock) {
                // The SOUND keeps time while it is in step with the page: what is heard and what is
                // shown are one moment, re-anchored every frame so a hand-over is seamless. A
                // recording that has fallen behind is brought to the playhead (syncMedia) — never
                // the playhead taken back to it.
                const heard = clock.clip.startMs + clock.element.currentTime * 1000;
                if (Math.abs(heard - next) <= DRIFT_S * 1000) {
                    // Never backwards: a recording a fraction behind is waited for (at most DRIFT_S),
                    // so the playhead only ever moves forward while playing.
                    next = Math.max(heard, this.playhead);
                    this.reanchor(next);
                    this.clockFile = clock.clip.fileId;
                }
            }
            if (next >= this.tl.durationMs) {
                this.playhead = this.tl.durationMs;
                this.pause();
                return;
            }
            this.playhead = Math.max(0, next);
        }
        this.render(false);
    }

    toggle() { this.playing ? this.pause() : this.play(); }

    play() {
        if (this.playhead >= this.tl.durationMs) this.playhead = 0;
        // Play where nothing was recorded, with a recording still to come: start at that recording
        // (rule T6, Ben 09/16/2026) — pressing Play at 0:00 on a session whose sound begins at
        // 0:04 means "play the sound".
        const here = frameAt(this.tl, this.playhead);
        if (!here.audio && !here.video) {
            const later = this.tl.clips
                .filter(c => c.startMs != null && c.startMs > this.playhead && !this.isBroken(c.fileId))
                .sort((a, b) => a.startMs - b.startMs)[0];
            if (later) this.playhead = later.startMs;
        }
        this.playing = true;
        this.waiting = false;
        this.reanchor(this.playhead);
        this.drawTransport();
        // Synchronous, inside the click: Safari plays a media element only from a user's gesture.
        this.render(true);
    }

    pause() {
        this.playing = false;
        this.clockFile = null;
        for (const element of this.media.values()) if (!element.paused) element.pause();
        this.drawTransport();
        this.render(true);
    }

    setRate(rate) {
        // Anchored BEFORE the change, from where the playhead is now, so the time already played
        // keeps the speed it was played at.
        this.reanchor(this.playhead);
        this.rate = rate;
        this.drawTransport();
        this.render(true);
    }

    seek(ms, how) {
        this.playhead = Math.max(0, Math.min(this.tl.durationMs, ms));
        this.reanchor(this.playhead);
        // The recording that was keeping time is moved like every other one. Left as the clock,
        // it was skipped by the seek and the next frame read its OLD time back — the drag thrown
        // away, the first player's snap-back in a new form. While its seek is under way it is not
        // the clock (clockElement skips a seeking element), so the page's own clock carries on
        // from where the drag put it.
        this.clockFile = null;
        this.render(true, how?.keepScrubber);
    }

    step_marker(forward) {
        const target = stepMarker(this.tl, this.playhead, forward);
        if (target !== null) this.seek(target);
    }

    // ── Drawing a frame ──────────────────────────────────────────────────────

    /** snap: the playhead jumped (a seek, play, pause) — media and the arrow follow at once. */
    render(snap, keepScrubber) {
        const frame = frameAt(this.tl, this.playhead);
        this.frames++;

        this.syncMedia(frame, snap);
        this.drawTime(keepScrubber);
        this.drawReadouts(frame);
        this.drawStage(frame);
        this.drawPhotos();
        this.drawMarkers(frame);
        this.drawMap(frame, snap);
        this.drawClock();
    }

    drawTime(keepScrubber) {
        const elapsed = hms(this.playhead);
        if (this.elapsedLabel && this.last.elapsed !== elapsed) {
            this.elapsedLabel.textContent = elapsed;
            this.last.elapsed = elapsed;
        }
        const total = `of ${hms(this.tl.durationMs)}`;
        if (this.totalLabel && this.last.total !== total) { this.totalLabel.textContent = total; this.last.total = total; }
        if (this.scrubber && !this.dragging && !keepScrubber) {
            const value = this.tl.durationMs > 0 ? this.playhead / this.tl.durationMs * 1000 : 0;
            if (Math.abs(Number(this.scrubber.value) - value) > 0.05) this.scrubber.value = String(value);
        }
        if (this.scrubber && this.last.valueText !== elapsed) {
            this.scrubber.setAttribute('aria-valuetext', `${elapsed} of ${hms(this.tl.durationMs)}`);
            this.last.valueText = elapsed;
        }
        const x = this.tl.durationMs > 0 ? this.playhead / this.tl.durationMs * 1000 : 0;
        for (const line of this.playheads) { line.setAttribute('x1', x); line.setAttribute('x2', x); }
    }

    drawReadouts(frame) {
        const key = `${frame.index}|${this.tl.positions}`;
        const position = frame.position;
        const posKey = position ? `${position.lat.toFixed(3)},${position.lon.toFixed(3)},${Math.round(position.accuracy ?? -1)}` : 'none';
        if (this.last.readouts === key && this.last.position === posKey) return;
        this.last.readouts = key;
        this.last.position = posKey;

        if (this.readField) {
            // Deviation when there is a base, absolute when there is not — and it SAYS which.
            const base = frame.emfBase ?? this.tl.fallbackEmfBase;
            this.readField.textContent = frame.emf == null ? '—'
                : base != null ? `${frame.fieldMg > 0.5 ? '+' : ''}${Math.round(frame.fieldMg)} mG`
                : `${Math.round(frame.emf * 10)} mG abs`;
        }
        if (this.readSound) this.readSound.textContent = frame.sound == null ? '—' : `${Math.round(frame.sound)} dB`;
        if (this.readFacing) {
            const facing = frame.facing;
            this.readFacing.textContent = facing
                ? `${compass(facing.deg)} ${String(Math.round(facing.deg) % 360).padStart(3, '0')}°` : '—';
            if (this.readFacingTitle) {
                this.readFacingTitle.textContent = facing
                    ? { looking: 'Looking', walking: 'Walking', moving: 'Moving' }[facing.kind] : 'Heading';
            }
        }
        if (this.readPosition) {
            // Three decimals, never five: roughly which building, not which room. The accuracy
            // travels with it, always.
            // Kilometres past one: the archive's public point is deliberately miles wide, and
            // "±9656 m" read like a measurement rather than a blur.
            const within = a => a >= 1000 ? `±${Math.round(a / 1000)} km` : `±${Math.round(a)} m`;
            this.readPosition.textContent = this.tl.positions === 1 ? 'not shared'
                : !position ? 'no fix'
                : position.accuracy != null
                    ? `~${position.lat.toFixed(3)}, ${position.lon.toFixed(3)} ${within(position.accuracy)}`
                    : `~${position.lat.toFixed(3)}, ${position.lon.toFixed(3)}`;
        }
        if (this.roomSlot && this.last.room !== frame.room) {
            this.last.room = frame.room;
            this.roomSlot.replaceChildren();
            if (frame.room) {
                this.roomSlot.append(el('span', { class: 'badge text-bg-secondary', 'data-testid': 'current-room' }, `Room: ${frame.room}`));
            }
        }
    }

    drawStage(frame) {
        if (!this.stage) return;
        const videoId = frame.video && !this.isBroken(frame.video.clip.fileId) ? frame.video.clip.fileId : null;
        const audio = frame.audio && !this.isBroken(frame.audio.clip.fileId) ? frame.audio : null;
        const key = `${videoId}|${audio?.clip.fileId}|${frame.motion?.t}`;
        if (this.last.stage === key) return;
        this.last.stage = key;

        for (const [fileId, element] of this.media) {
            if (element.tagName !== 'VIDEO') continue;
            element.style.display = fileId === videoId ? '' : 'none';
        }

        if (this.stageNote) {
            this.stageNote.style.display = videoId ? 'none' : '';
            const hasAnything = this.tl.clips.length > 0 || this.tl.photos.length > 0;
            this.stageNoteUse?.setAttribute('href', ICONS + (audio ? 'volume-2' : 'moon'));
            this.stageNote.setAttribute('data-sound', audio ? '1' : '0');
            if (this.stageNoteText) {
                this.stageNoteText.textContent = audio ? audio.clip.path.replace('media/', '')
                    : hasAnything ? 'No recording at this moment.'
                    : 'No sound, video or photos were recorded in this session.';
                if (audio) this.stageNoteText.removeAttribute('data-testid');
                else this.stageNoteText.setAttribute('data-testid', 'stage-nothing');
            }
        }

        // "Motion detected": created and removed, never merely hidden — a hidden sign still says
        // so to anything that reads the page.
        const showing = this.stage.querySelector('[data-testid="motion-detected"]');
        if (frame.motion && !showing) {
            this.stage.append(el('div', { class: 'fk-motion-sign', 'data-testid': 'motion-detected' }, icon('eye', 'me-1'), 'Motion detected'));
        } else if (!frame.motion && showing) {
            showing.remove();
        }
        this.stage.classList.toggle('fk-stage--motion', !!frame.motion && !!videoId);
    }

    drawPhotos() {
        if (!this.photoStrip) return;
        const ms = this.playhead;
        let lastPassed = null;
        for (const button of this.photoStrip.querySelectorAll('[data-fk-photo]')) {
            const t = Number(button.getAttribute('data-fk-t'));
            const glowing = t <= ms && ms - t < SIGN_MS;
            if (t <= ms) lastPassed = button;
            const was = button.getAttribute('data-testid') === 'photo-thumb-glowing';
            if (glowing !== was) {
                button.setAttribute('data-testid', glowing ? 'photo-thumb-glowing' : 'photo-thumb');
                button.classList.toggle('fk-thumb--glow', glowing);
            }
        }
        // While playing, the strip keeps the latest photograph passed in view, as the phone does.
        if (this.playing && lastPassed && this.last.photo !== lastPassed) {
            this.last.photo = lastPassed;
            lastPassed.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' });
        }
    }

    drawMarkers(frame) {
        const nearest = frame.nearestMarker?.t ?? null;
        if (this.last.marker === nearest) return;
        this.last.marker = nearest;
        for (const row of this.root.querySelectorAll('[data-fk-marker]')) {
            const on = nearest !== null && Number(row.getAttribute('data-fk-marker')) === nearest;
            row.classList.toggle('fk-marker--now', on);
            if (on) row.setAttribute('aria-current', 'true'); else row.removeAttribute('aria-current');
        }
    }

    drawMap(frame, snap) {
        if (!this.mapId) return;
        if (!this.mapReady) {
            if (!benMap.isMap(this.mapId)) return;   // the map component has not made it yet
            this.mapReady = true;
            benMap.playerInit(this.mapId, { track: this.tl.track, pins: this.pins() });
            snap = true;
        }
        const position = frame.position;
        benMap.playerMove(this.mapId,
            position?.lat ?? null, position?.lon ?? null,
            this.tl.positions === 0 ? (frame.facing?.deg ?? null) : null,
            position?.accuracy ?? null, snap, this.rate > 8);
    }

    /** Pins for photos and marks that carry a position — only where positions are exact. */
    pins() {
        if (this.tl.positions !== 0) return [];
        const r = this.tl.readings, out = [];
        const at = ms => {
            let i = -1, low = 0, high = r.t.length - 1;
            while (low <= high) { const mid = (low + high) >> 1; if (r.t[mid] <= ms) { i = mid; low = mid + 1; } else high = mid - 1; }
            return i >= 0 && r.lat[i] != null ? [r.lat[i], r.lon[i]] : null;
        };
        for (const photo of this.tl.photos) {
            const p = at(photo.t);
            if (p) out.push({ lat: p[0], lon: p[1], title: `Photo at ${hms(photo.t)}`, color: '#22d3ee', glyph: '◉' });
        }
        for (const marker of this.tl.markers) {
            const p = at(marker.t);
            if (p) out.push({ lat: p[0], lon: p[1], title: `${marker.title} at ${hms(marker.t)}`, color: marker.automatic ? '#f2b037' : '#22d3ee', glyph: marker.automatic ? '!' : '⚑' });
        }
        return out;
    }

    drawClock() {
        if (!this.clockSlot) return;
        const following = this.clockFile !== null;
        if (this.last.clock === following) return;
        this.last.clock = following;
        this.clockSlot.replaceChildren();
        if (following) {
            this.clockSlot.append(el('span', { class: 'badge text-bg-info', 'data-testid': 'media-clock' }, 'following the recording'));
        }
    }

    // ── The recordings ───────────────────────────────────────────────────────

    attachMedia(fileId, element) {
        if (this.attached.has(element)) return;
        this.attached.add(element);
        const onLength = () => this.learnedLength(fileId, element.duration);
        element.addEventListener('loadedmetadata', onLength);
        if (element.readyState >= 1) onLength();
        element.addEventListener('waiting', () => {
            if (!this.playing || !this.isCoveringAudio(fileId)) return;
            this.waiting = true;
            this.waitingSince = performance.now();
        });
        element.addEventListener('playing', () => {
            this.waiting = false;
            // How long a move took to sound again: the next correction aims that far ahead, so a
            // recording on a slow connection catches the playhead instead of chasing it.
            if (element.__fkSeekAt) {
                const took = (performance.now() - element.__fkSeekAt) / 1000;
                this.seekLead.set(fileId, Math.min(MAX_LEAD_S, Math.max(0, took)));
                element.__fkSeekAt = 0;
            }
        });
        element.addEventListener('canplay', () => { this.waiting = false; });
        element.addEventListener('error', () => this.failed(fileId, element));
        // A file the browser could not decode can fail before this attached — preload starts the
        // moment Blazor renders the element — and the event has been and gone.
        if (element.error) queueMicrotask(() => this.failed(fileId, element));
        // The browser's own controls on a recording in the list drive the session too.
        element.addEventListener('play', () => {
            if (this.playing || this.pendingPlay.has(element)) return;
            const clip = this.clipById.get(fileId);
            if (!clip || clip.startMs == null) return;
            this.playhead = Math.min(this.tl.durationMs, clip.startMs + element.currentTime * 1000);
            this.playing = true;
            this.reanchor(this.playhead);
            this.drawTransport();
        });
        element.addEventListener('pause', () => {
            // Paused by something other than the engine — its own controls, the system's media
            // keys — while it is the recording being heard: the session pauses with it. Asked of
            // the recording at the playhead, not of the clock, which steps aside whenever the
            // element is briefly short of data.
            if (!this.playing || this.pendingPlay.has(element) || element.ended) return;
            if (element.getAttribute('data-fk-pausing') === '1') return;
            if (this.isCoveringAudio(fileId)) this.pause();
        });
    }

    isCoveringAudio(fileId) {
        const frame = frameAt(this.tl, this.playhead);
        return frame.audio?.clip.fileId === fileId;
    }

    /** The browser has read a recording's length: place it if its start waited on that. */
    learnedLength(fileId, seconds) {
        const clip = this.clipById.get(fileId);
        if (!clip || !(seconds > 0) || !isFinite(seconds)) return;
        const ms = Math.round(seconds * 1000);
        let moved = false;
        if (clip.durationMs == null) { clip.durationMs = ms; moved = true; }
        if (clip.startMs == null && clip.notedEndMs != null) {
            // Noted when it ENDED (rule M4): it began its length earlier.
            clip.startMs = Math.max(0, clip.notedEndMs - clip.durationMs);
            moved = true;
            const label = this.root.querySelector(`[data-fk-start="${fileId}"]`);
            if (label && clip.startMs > 0) {
                label.textContent = `from ${hms(clip.startMs)}`;
                label.setAttribute('data-testid', 'media-starts-at');
            }
        }
        // A session the phone never closed ends at the last thing it recorded (rule S2).
        if (!this.tl.hasEndTime && clip.startMs != null && clip.startMs + clip.durationMs > this.tl.durationMs) {
            this.tl.durationMs = clip.startMs + clip.durationMs;
            moved = true;
        }
        if (moved) { this.last = {}; this.render(false); }
    }

    isBroken(fileId) {
        return this.broken?.has(fileId) ?? false;
    }

    failed(fileId, element) {
        // A recording that already told us its length DID decode; failing later means its link ran
        // out (the ticket in it lasts an hour), and the host renews it. Otherwise the browser
        // cannot play this file, and the host says so beside it.
        const decodedBefore = element.duration > 0 && isFinite(element.duration);
        if (this.clockFile === fileId || this.isCoveringAudio(fileId)) this.pause();
        this.broken ??= new Set();
        if (!decodedBefore) this.broken.add(fileId);
        this.dotnet.invokeMethodAsync('OnMediaFailed', fileId, decodedBefore).catch(() => { });
    }

    renewed(fileId) {
        this.broken?.delete(fileId);
        this.bind();
        this.render(true);
    }

    seekElement(element, seconds, fileId, now) {
        this.lastSeekAt.set(fileId, now);
        element.__fkSeekAt = now;
        if (element.readyState < 1) {
            // Before metadata, an assignment to currentTime may be dropped: remember the latest
            // target and apply it once, rather than queueing one listener per seek.
            element.__fkTarget = seconds;
            if (!element.__fkWaitingForMetadata) {
                element.__fkWaitingForMetadata = true;
                element.addEventListener('loadedmetadata', () => {
                    element.__fkWaitingForMetadata = false;
                    if (element.__fkTarget != null) element.currentTime = Math.min(element.__fkTarget, element.duration || element.__fkTarget);
                }, { once: true });
                if (element.preload === 'none') element.load();
            }
            return;
        }
        element.currentTime = Math.min(seconds, element.duration || seconds);
    }

    /** Every recording where the playhead says it should be: playing, paused, seeked, muted. */
    syncMedia(frame, snap) {
        const now = performance.now();
        const audioId = frame.audio && !this.isBroken(frame.audio.clip.fileId) ? frame.audio.clip.fileId : null;
        const videoId = frame.video && !this.isBroken(frame.video.clip.fileId) ? frame.video.clip.fileId : null;
        const audible = this.playing && this.rate <= MEDIA_MAX_RATE;

        for (const [fileId, element] of this.media) {
            const covering = fileId === audioId ? frame.audio : fileId === videoId ? frame.video : null;
            if (!covering) {
                if (!element.paused) this.quietly(element, () => element.pause());
                continue;
            }
            const target = covering.offsetMs / 1000;
            const isVideo = element.tagName === 'VIDEO';
            // One sound at a time (rule M7): a video is muted while a recording covers it.
            element.muted = isVideo ? audioId !== null : false;

            this.prime(element);
            if (audible) {
                element.playbackRate = this.rate;
                const isClock = this.clockFile === fileId;
                const drift = Math.abs(element.currentTime - target);
                const since = now - (this.lastSeekAt.get(fileId) ?? 0);
                if (!isClock) {
                    if (snap) {
                        // Already there (it was buffered there while paused): moving it again would
                        // throw the buffer away for nothing.
                        if (drift > 0.05) this.seekElement(element, target, fileId, now);
                    } else if (drift > DRIFT_S && !element.seeking && since > FAST_SEEK_MS) {
                        // Out of step while playing: moved to where the playhead will be once it
                        // sounds again, not where it is now — and not again until that move settles.
                        const lead = (this.seekLead.get(fileId) ?? 0.25) * this.rate;
                        this.seekElement(element, Math.min(target + lead, (element.duration || Infinity)), fileId, now);
                    }
                }
                if (element.paused && !element.ended && !this.pendingPlay.has(element)) {
                    this.pendingPlay.add(element);
                    const done = () => this.pendingPlay.delete(element);
                    try { element.play().then(done, done); } catch { done(); }
                }
            } else {
                if (!element.paused) this.quietly(element, () => element.pause());
                // Paused, or too fast to hear: the stage still shows the frame at the playhead.
                const since = now - (this.lastSeekAt.get(fileId) ?? 0);
                const drift = Math.abs(element.currentTime - target);
                const due = snap
                    || (this.playing ? since > FAST_SEEK_MS && drift > DRIFT_S
                       : this.dragging ? since > DRAG_SEEK_MS && drift > 0.05
                       : drift > 0.05);
                if (due) this.seekElement(element, target, fileId, now);
            }
        }
        this.primeNext(now);
    }

    /**
     * Buffers ahead: the recording under the playhead (so Play starts at once) and the next one to
     * come, at its start (so the hand-over does not stall). On a page that loads recordings only as
     * metadata, the first Play used to wait on the network with the clock stopped (10/14/2026).
     */
    prime(element) {
        if (element.preload !== 'auto') element.preload = 'auto';
    }

    primeNext(now) {
        const horizon = this.playing ? PRIME_AHEAD_MS * this.rate : Infinity;
        let next = null;
        for (const clip of this.tl.clips) {
            if (clip.startMs == null || clip.startMs <= this.playhead || this.isBroken(clip.fileId)) continue;
            if (clip.startMs - this.playhead > horizon) continue;
            if (!next || clip.startMs < next.startMs) next = clip;
        }
        const element = next && this.media.get(next.fileId);
        if (!element || !element.paused) return;
        this.prime(element);
        if (element.currentTime > 0.05 && now - (this.lastSeekAt.get(next.fileId) ?? 0) > FAST_SEEK_MS)
            this.seekElement(element, 0, next.fileId, now);
    }

    /** Pauses an element without that pause being read as the person pausing the session. */
    quietly(element, act) {
        element.setAttribute('data-fk-pausing', '1');
        try { act(); } finally { setTimeout(() => element.removeAttribute('data-fk-pausing'), 0); }
    }

    // ── Photographs ──────────────────────────────────────────────────────────

    openPhoto(fileId) {
        const photo = this.tl.photos.find(p => p.fileId === fileId);
        if (!photo) return;
        // Tapping a photo goes to the moment it was taken (rule T7), pauses, and shows it full size.
        const resume = this.playing;
        this.pause();
        this.seek(photo.t);
        this.closePhoto(false);

        const caption = resume ? 'Click to keep playing' : 'Click to close';
        const overlay = el('div', {
            class: 'fk-photo-overlay', role: 'button', tabindex: '0', 'data-testid': 'photo-expanded',
            'aria-label': `Photo at ${hms(photo.t)}. ${caption}.`,
        },
            el('img', { src: this.urls[fileId] ?? '', alt: `Photo at ${hms(photo.t)}` }),
            el('div', { class: 'fk-photo-caption' }, `${hms(photo.t)} into the session`,
                el('span', { class: 'opacity-75' }, ` · ${caption}`)));
        const close = () => this.closePhoto(resume);
        overlay.addEventListener('click', close);
        overlay.addEventListener('keydown', e => {
            if (e.key === 'Escape' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); e.stopPropagation(); close(); }
        });
        this.root.append(overlay);
        this.expanded = overlay;
        overlay.focus();
    }

    closePhoto(resume) {
        if (!this.expanded) return;
        this.expanded.remove();
        this.expanded = null;
        // Inside the click that closed it, so the browser lets the sound start again.
        if (resume) this.play();
    }

    // ── The chart and the keyboard ───────────────────────────────────────────

    attachChart(svg) {
        // Dragging on the chart pauses and seeks, as on the phone.
        const at = e => {
            const box = svg.getBoundingClientRect();
            const f = Math.min(1, Math.max(0, (e.clientX - box.left) / Math.max(1, box.width)));
            this.seek(f * this.tl.durationMs);
        };
        svg.addEventListener('pointerdown', e => {
            this.pause();
            this.dragging = true;
            svg.setPointerCapture?.(e.pointerId);
            at(e);
        });
        svg.addEventListener('pointermove', e => { if (this.dragging && e.buttons) at(e); });
        const end = () => { if (this.dragging) { this.dragging = false; this.render(true); } };
        svg.addEventListener('pointerup', end);
        svg.addEventListener('pointercancel', end);
    }

    onKey(e) {
        if (this.expanded) return;
        const target = e.target;
        const typing = target instanceof HTMLElement
            && (target.isContentEditable || ['TEXTAREA', 'SELECT'].includes(target.tagName)
                || (target.tagName === 'INPUT' && target.type !== 'range'));
        if (typing) return;
        switch (e.key) {
            case ' ':
                // A focused button already answers Space with a click; toggling here too would
                // undo it.
                if (target instanceof HTMLButtonElement) return;
                e.preventDefault();
                this.toggle();
                break;
            case 'ArrowLeft': e.preventDefault(); this.seek(this.playhead - SKIP_MS); break;
            case 'ArrowRight': e.preventDefault(); this.seek(this.playhead + SKIP_MS); break;
            case 'Home': e.preventDefault(); this.seek(0); break;
            case 'End': e.preventDefault(); this.seek(this.tl.durationMs); break;
        }
    }
}
