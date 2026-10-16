// eventCountdown.js — ONE ticker for every countdown on the page (10/16/2026).
//
// The home card and every row of the event lists draw an EventCountdown (Kit/EventCountdown.razor).
// The server draws each one correctly for the moment the page was made; this keeps them moving. It
// is one setInterval for the whole page however many countdowns there are: each tick finds them by
// their data-cd attribute, so a row Blazor adds is picked up on the next tick and a row it removes
// simply stops being found. When none are left the ticker stops itself; the next countdown to be
// drawn starts it again.
//
// IT KNOWS NO TIME ZONES. "Tomorrow", "Tonight" and "Starts in 2 days" are calendar facts on the
// event's own clock, and the server already worked out the exact moments they change — the steps,
// in data-cd-steps — with the DST arithmetic in C# where it is tested (CountdownMath.Steps). This
// only compares those moments with the browser's clock.
//
// NOT A LIVE REGION. Each countdown carries an aria-label with the start in words; nothing here is
// announced, so a screen reader is not read a number every second.
//
// The pure functions are exported so the unit tests can run them under Jint
// (EventCountdownScriptTests); everything that touches the page is in paint() and tick().

const SELECTOR = '[data-cd]';
const DAY = 86400000, HOUR = 3600000, MINUTE = 60000, SECOND = 1000;

let timer = 0;
const stepCache = new WeakMap();

/** The step in force at `now`: the last whose moment has come. Steps are [ms, phase, words]. */
export function stepAt(steps, now) {
    let current = steps[0];
    for (const step of steps) {
        if (step[0] <= now) current = step;
        else break;
    }
    return current;
}

/** Whole days, hours, minutes and seconds in `ms`, never negative. */
export function units(ms) {
    const total = Math.max(0, Math.floor(ms / SECOND));
    return {
        d: Math.floor(total / 86400),
        h: Math.floor((total % 86400) / 3600),
        m: Math.floor((total % 3600) / 60),
        s: total % 60,
    };
}

const two = n => (n < 10 ? '0' : '') + n;

/** The time left as words on a row: the same shapes as CountdownMath.Remaining in C#. */
export function remaining(ms, seconds) {
    const left = Math.max(0, ms);
    const u = units(left);
    if (u.d > 0) return `${u.d}d ${two(u.h)}h ${two(u.m)}m`;
    if (u.h > 0) return `${u.h}h ${two(u.m)}m`;
    if (seconds) return `${u.m}m ${two(u.s)}s`;
    return `${Math.max(1, Math.ceil(left / MINUTE))} min`;
}

/** Which of the big tiles show: days only while there are any; seconds only in the last hour. */
export function cells(ms, seconds) {
    const left = Math.max(0, ms);
    if (left >= DAY) return ['d', 'h', 'm'];
    if (left >= HOUR) return ['h', 'm'];
    return seconds ? ['m', 's'] : ['m'];
}

/** How full the bar is, 0 to 1: empty at the baseline, full at the start. */
export function progress(base, start, now) {
    if (now >= start) return 1;
    if (now <= base) return 0;
    const whole = start - base;
    return whole <= 0 ? 1 : Math.min(1, Math.max(0, (now - base) / whole));
}

/** Writes text into an element without replacing the text node Blazor is holding on to. */
function setText(node, text) {
    if (!node) return;
    const first = node.firstChild;
    if (first && first.nodeType === 3 && node.childNodes.length === 1) {
        if (first.nodeValue !== text) first.nodeValue = text;
    } else if (node.textContent !== text) {
        node.textContent = text;
    }
}

function stepsOf(el) {
    const raw = el.getAttribute('data-cd-steps') || '[]';
    const cached = stepCache.get(el);
    if (cached && cached.raw === raw) return cached.steps;
    let steps;
    try { steps = JSON.parse(raw); } catch { steps = []; }
    stepCache.set(el, { raw, steps });
    return steps;
}

/** Brings one countdown up to `now`. */
export function paint(el, now, seconds) {
    const steps = stepsOf(el);
    if (!steps.length) return;

    const start = Number(el.getAttribute('data-cd-start'));
    const base = Number(el.getAttribute('data-cd-base'));
    const step = stepAt(steps, now);

    if (el.getAttribute('data-phase') !== step[1]) el.setAttribute('data-phase', step[1]);
    // "Soon" is the last hour: the CSS quickens the pulse.
    const soon = now < start && start - now < HOUR;
    if (el.hasAttribute('data-soon') !== soon) el.toggleAttribute('data-soon', soon);

    setText(el.querySelector('[data-cd-words]'), step[2]);

    const left = start - now;
    const digits = el.querySelector('[data-cd-digits]');
    if (digits) setText(digits, remaining(left, seconds));

    const tiles = el.querySelectorAll('[data-cd-cell]');
    if (tiles.length) {
        const shown = cells(left, seconds);
        const u = units(left);
        for (const tile of tiles) {
            const unit = tile.getAttribute('data-cd-cell');
            const on = shown.includes(unit);
            if (tile.hidden === on) tile.hidden = !on;
            if (on) setText(tile.querySelector('[data-cd-unit]'), two(u[unit]));
        }
    }

    const fill = el.querySelector('[data-cd-fill]');
    if (fill) {
        const width = (progress(base, start, now) * 100).toFixed(2) + '%';
        if (fill.style.width !== width) fill.style.width = width;
    }
}

function lessMotion() {
    try { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); }
    catch { return false; }
}

function tick() {
    const found = document.querySelectorAll(SELECTOR);
    if (!found.length) { stop(); return; }
    // A reader who asked for less motion gets minutes, not a number flicking every second.
    const seconds = !lessMotion();
    const now = Date.now();
    for (const el of found) {
        try { paint(el, now, seconds); } catch { /* one odd row must not stop the rest */ }
    }
}

function stop() {
    if (timer) clearInterval(timer);
    timer = 0;
}

/** Starts the page's ticker, once. Safe to call from every countdown on every render. */
export function start() {
    if (typeof document === 'undefined') return;
    tick();
    if (!timer && document.querySelector(SELECTOR)) timer = setInterval(tick, SECOND);
}
