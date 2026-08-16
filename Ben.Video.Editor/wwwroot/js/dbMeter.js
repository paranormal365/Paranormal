// dbMeter.js
// Web Audio API stereo VU meter rendered onto a <canvas> element.
// Uses an AnalyserNode per channel for RMS-based dB calculation.
//
// Public API (all keyed by canvas element reference):
//   init(canvas, videoElementId)  — attaches Web Audio graph
//   resume(canvas)                — resumes AudioContext (call on play)
//   suspend(canvas)               — suspends AudioContext (call on pause)
//   destroy(canvas)               — tears down everything

// ── Constants ────────────────────────────────────────────────────────────────

const DB_MIN    = -60;   // bottom of meter (silence)
const DB_MAX    =   6;   // top of meter (clip level)
const DB_RANGE  = DB_MAX - DB_MIN;
const FFT_SIZE  = 2048;
const PEAK_HOLD_MS = 2000;

// Colour thresholds (in dB)
const COLOUR_GREEN  = -12;  // below this → green
const COLOUR_AMBER  =  -6;  // below this → amber; above → red

const DB_LABELS = [6, 0, -6, -12, -20, -40];

// ── Per-canvas state ──────────────────────────────────────────────────────────

const _meters = new Map(); // canvas → MeterState

class MeterState {
    constructor(ctx, analyserL, analyserR, canvas) {
        this.ctx       = ctx;
        this.analyserL = analyserL;
        this.analyserR = analyserR;
        this.canvas    = canvas;
        this.rafId     = null;
        this.peakL     = DB_MIN;
        this.peakR     = DB_MIN;
        this.peakLTime = 0;
        this.peakRTime = 0;
    }
}

// ── Public API ────────────────────────────────────────────────────────────────

export async function init(canvas, videoElementId) {
    const video = document.getElementById(videoElementId)
               ?? document.querySelector(videoElementId);
    if (!video) { console.warn('[DbMeter] video element not found:', videoElementId); return; }

    // AudioContext must be created in a user-gesture context.
    // We create it here (suspended) and resume on first play.
    const audioCtx = new (window.AudioContext ?? window.webkitAudioContext)();

    let source;
    try {
        source = audioCtx.createMediaElementSource(video);
    } catch (e) {
        // Already connected (e.g. hot reload) — bail gracefully
        console.warn('[DbMeter] createMediaElementSource failed:', e.message);
        return;
    }

    const splitter = audioCtx.createChannelSplitter(2);
    const analyserL = _makeAnalyser(audioCtx);
    const analyserR = _makeAnalyser(audioCtx);

    source.connect(splitter);
    splitter.connect(analyserL, 0);
    splitter.connect(analyserR, 1);
    // Reconnect to destination so audio still plays through
    source.connect(audioCtx.destination);

    const state = new MeterState(audioCtx, analyserL, analyserR, canvas);
    _meters.set(canvas, state);
    _startRaf(state);
}

export async function resume(canvas) {
    const state = _meters.get(canvas);
    if (!state) return;
    if (state.ctx.state === 'suspended') await state.ctx.resume();
    if (!state.rafId) _startRaf(state);
}

export async function suspend(canvas) {
    const state = _meters.get(canvas);
    if (!state) return;
    if (state.rafId) { cancelAnimationFrame(state.rafId); state.rafId = null; }
}

export async function destroy(canvas) {
    const state = _meters.get(canvas);
    if (!state) return;
    if (state.rafId) cancelAnimationFrame(state.rafId);
    try { await state.ctx.close(); } catch { /* ignore */ }
    _meters.delete(canvas);
}

// ── rAF draw loop ─────────────────────────────────────────────────────────────

function _startRaf(state) {
    const draw = (now) => {
        _drawMeter(state, now);
        state.rafId = requestAnimationFrame(draw);
    };
    state.rafId = requestAnimationFrame(draw);
}

function _drawMeter(state, now) {
    const canvas = state.canvas;
    const ctx2d  = canvas.getContext('2d');
    const W = canvas.width;
    const H = canvas.height;
    const barW  = Math.floor((W - 6) / 2);  // 2 bars + 2px gap + 2px padding each side
    const barX  = [2, 2 + barW + 2];

    ctx2d.clearRect(0, 0, W, H);

    const dbL = _rmsDb(state.analyserL);
    const dbR = _rmsDb(state.analyserR);

    // Update peak hold
    if (dbL > state.peakL) { state.peakL = dbL; state.peakLTime = now; }
    else if (now - state.peakLTime > PEAK_HOLD_MS) state.peakL = dbL;

    if (dbR > state.peakR) { state.peakR = dbR; state.peakRTime = now; }
    else if (now - state.peakRTime > PEAK_HOLD_MS) state.peakR = dbR;

    _drawBar(ctx2d, barX[0], barW, H, dbL, state.peakL);
    _drawBar(ctx2d, barX[1], barW, H, dbR, state.peakR);
    _drawLabels(ctx2d, W, H);
}

function _drawBar(ctx, x, w, H, db, peak) {
    const fillH  = _dbToHeight(db, H);
    const peakY  = H - _dbToHeight(peak, H) - 1;

    // Background (dark track)
    ctx.fillStyle = '#1a1a2e';
    ctx.fillRect(x, 0, w, H);

    // Gradient segments — draw from bottom up
    const greenH = _dbToHeight(COLOUR_GREEN, H);
    const amberH = _dbToHeight(COLOUR_AMBER, H);

    // Green segment (bottom)
    if (fillH > 0) {
        const segH = Math.min(fillH, greenH);
        ctx.fillStyle = _cssVar('--kendo-color-success', '#22c55e');
        ctx.fillRect(x, H - segH, w, segH);
    }
    // Amber segment
    if (fillH > greenH) {
        const segH = Math.min(fillH - greenH, amberH - greenH);
        ctx.fillStyle = _cssVar('--kendo-color-warning', '#f59e0b');
        ctx.fillRect(x, H - greenH - segH, w, segH);
    }
    // Red segment (top)
    if (fillH > amberH) {
        const segH = fillH - amberH;
        ctx.fillStyle = _cssVar('--kendo-color-error', '#ef4444');
        ctx.fillRect(x, H - amberH - segH, w, segH);
    }

    // Peak hold tick
    if (peak > DB_MIN) {
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(x, peakY, w, 2);
    }
}

function _drawLabels(ctx, W, H) {
    ctx.fillStyle = 'rgba(255,255,255,0.45)';
    ctx.font      = '8px monospace';
    ctx.textAlign = 'right';
    for (const db of DB_LABELS) {
        const y = H - _dbToHeight(db, H);
        ctx.fillRect(0, y, 2, 1);  // tiny left tick
        // labels are handled by CSS ::before on the container, not canvas
    }
}

// ── Audio helpers ─────────────────────────────────────────────────────────────

function _makeAnalyser(audioCtx) {
    const a = audioCtx.createAnalyser();
    a.fftSize = FFT_SIZE;
    a.smoothingTimeConstant = 0.7;
    return a;
}

function _rmsDb(analyser) {
    const buf = new Float32Array(analyser.fftSize);
    analyser.getFloatTimeDomainData(buf);
    let sum = 0;
    for (let i = 0; i < buf.length; i++) sum += buf[i] * buf[i];
    const rms = Math.sqrt(sum / buf.length);
    return rms === 0 ? DB_MIN : Math.max(DB_MIN, 20 * Math.log10(rms));
}

function _dbToHeight(db, H) {
    const clamped = Math.max(DB_MIN, Math.min(DB_MAX, db));
    return Math.round(((clamped - DB_MIN) / DB_RANGE) * H);
}

// ── Theme helper ──────────────────────────────────────────────────────────────

const _cssCache = {};
function _cssVar(name, fallback) {
    if (_cssCache[name]) return _cssCache[name];
    const val = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    const result = val || fallback;
    _cssCache[name] = result;
    return result;
}
