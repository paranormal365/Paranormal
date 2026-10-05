// HomeAdPlayer — the Home page ad. See the comment in HomeAdPlayer.razor for the why.
//
// Desktop: muted while playing; the pointer over it = sound, but only once the page has had a click,
// tap or key press (browsers refuse sound on hover alone, and Chrome pauses a video that is unmuted
// without one). Until then the badge says "Click for sound" and that first click turns sound on.
// After that, a click pauses / plays. Touch (no hover): a tap toggles sound; if paused, a tap plays.

const SOURCES = [
    { maxWidth: 900, src: '/static/video/ishaunted-ad-720.mp4' },
    { maxWidth: Infinity, src: '/static/video/ishaunted-ad-1080.mp4' },
];

const players = new WeakMap();

// Our own record of "the visitor has interacted", for browsers without navigator.userActivation.
let interactions = 0;
const markInteracted = () => { interactions++; };
['pointerdown', 'keydown', 'touchend'].forEach(ev =>
    document.addEventListener(ev, markInteracted, { capture: true, passive: true }));

function hasBeenActive() {
    const ua = navigator.userActivation;
    return ua ? ua.hasBeenActive : interactions > 0;
}

export function init(frame, video) {
    if (!frame || !video || players.has(frame)) return;

    const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const touch = matchMedia('(hover: none)').matches;
    const hintText = frame.querySelector('.ad-player__hint-text');

    const s = {
        loaded: false,
        inView: false,
        hover: false,
        userPaused: reduceMotion,  // the visitor's choice; scrolling away pauses without changing it
        refusedAt: -1,             // interaction count when the browser last refused sound
        touchSound: false,         // touch: the tap toggle
        keySound: false,           // keyboard: the M toggle
        listeners: [],
        observers: [],
    };
    players.set(frame, s);

    video.muted = true;
    video.defaultMuted = true;

    const on = (el, ev, fn, opt) => { el.addEventListener(ev, fn, opt); s.listeners.push([el, ev, fn, opt]); };

    function load() {
        if (s.loaded) return;
        s.loaded = true;
        const px = (frame.clientWidth || window.innerWidth) * Math.min(window.devicePixelRatio || 1, 2);
        video.src = SOURCES.find(x => px <= x.maxWidth).src;
        video.preload = 'auto';
        video.load();
    }

    function wantSound() {
        if (s.refusedAt === interactions) return false;   // refused; wait for a new click / key
        if (touch) return s.touchSound;
        return s.keySound || (s.hover && hasBeenActive());
    }
    function refused() {
        s.refusedAt = interactions;
        s.touchSound = false; s.keySound = false;
    }

    function shouldPlay() {
        return s.inView && !s.userPaused && !document.hidden;
    }

    function apply() {
        if (shouldPlay()) {
            load();
            const sound = wantSound();
            video.muted = !sound;
            const p = video.play();
            if (p && p.catch) {
                p.catch(() => {
                    // Refused (e.g. sound without activation): fall back to muted, keep going.
                    if (!video.muted) {
                        video.muted = true;
                        refused();
                        video.play().catch(() => { });
                    }
                    render();
                });
            }
        } else {
            video.pause();
            video.muted = true;
        }
        render();
    }

    function render() {
        const paused = s.userPaused;
        const sound = !video.muted && !video.paused;
        frame.dataset.paused = paused ? 'true' : 'false';
        frame.dataset.sound = sound ? 'on' : 'off';
        let text;
        if (paused) text = touch ? 'Tap to play' : 'Click to play';
        else if (touch) text = sound ? 'Tap to mute' : 'Tap for sound';
        else if (sound) text = 'Click to pause';
        else if (!hasBeenActive() || s.refusedAt === interactions) text = 'Click for sound';
        else text = 'Hover for sound';
        if (hintText && hintText.textContent !== text) hintText.textContent = text;
    }

    // ── in view / out of view ──────────────────────────────────────────────
    if ('IntersectionObserver' in window) {
        const near = new IntersectionObserver(es => { if (!s.userPaused && es.some(e => e.isIntersecting)) load(); }, { rootMargin: '400px 0px' });
        near.observe(frame);
        const seen = new IntersectionObserver(es => {
            s.inView = es[es.length - 1].intersectionRatio >= 0.35;
            if (!s.inView) s.touchSound = false;   // don't come back with sound the visitor forgot about
            apply();
        }, { threshold: [0, 0.35, 0.6] });
        seen.observe(frame);
        s.observers.push(near, seen);
    } else {
        s.inView = true; apply();
    }
    on(document, 'visibilitychange', apply);

    // ── pointer ────────────────────────────────────────────────────────────
    on(frame, 'mouseenter', () => { s.hover = true; if (!touch) apply(); });
    on(frame, 'mouseleave', () => { s.hover = false; if (!touch) apply(); });

    on(frame, 'click', () => {
        const hadSound = !video.muted && !video.paused;
        if (touch) {
            if (s.userPaused) { s.userPaused = false; s.touchSound = true; }
            else s.touchSound = !s.touchSound;
        } else if (s.userPaused) {
            s.userPaused = false;            // play (and, with the pointer over it, sound)
        } else if (!hadSound) {
            // Playing muted under the pointer: this click is what lets the browser allow sound,
            // so it turns sound on rather than pausing.
        } else {
            s.userPaused = true;             // sound was on: stop
        }
        apply();
    });

    // ── keyboard ───────────────────────────────────────────────────────────
    on(frame, 'keydown', e => {
        if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            s.userPaused = !s.userPaused;
            apply();
        } else if (e.key === 'm' || e.key === 'M') {
            e.preventDefault();
            s.keySound = !s.keySound;
            apply();
        }
    });
    on(frame, 'blur', () => { if (s.keySound) { s.keySound = false; apply(); } });

    // Chrome may pause a video it was not allowed to unmute; recover muted instead of sitting still.
    on(video, 'pause', () => {
        if (shouldPlay() && !video.ended) {
            setTimeout(() => {
                if (shouldPlay() && video.paused) { video.muted = true; refused(); video.play().catch(() => { }); render(); }
            }, 0);
        }
    });
    on(video, 'playing', () => { frame.dataset.state = 'ready'; render(); });
    on(video, 'volumechange', render);

    render();
}

export function dispose(frame) {
    const s = frame && players.get(frame);
    if (!s) return;
    s.observers.forEach(o => o.disconnect());
    s.listeners.forEach(([el, ev, fn, opt]) => el.removeEventListener(ev, fn, opt));
    const video = frame.querySelector('video');
    if (video) { video.pause(); video.removeAttribute('src'); video.load(); }
    players.delete(frame);
}
