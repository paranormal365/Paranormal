// HostedEventShowcase — the phone's fixed "Ask for a place" button steps aside while the request
// form is on screen, and comes back once the form has scrolled away (10/16/2026). It jumps to the
// form; once the form is there, the button only covered its last lines and its send button.
//
// The form is "on screen" once it reaches above the bottom quarter of the window. While only its
// top edge peeks in at the bottom, the button stays: that is the moment it is pointing at it.
//
// Nothing here assumes the button or the form still exists. The page can redraw either one away
// between a scroll and the observer's callback, and a script that threw there would take the
// whole Blazor circuit down with it.

const watches = new WeakMap();
const AWAY = 'hosted-sticky-ask--away';

// The class hides it with visibility, which also takes it out of the tab order and away from a
// screen reader while it cannot be seen.
function setAway(button, away) {
    button.classList.toggle(AWAY, away);
}

export function watch(button, targetId) {
    if (!button || !targetId || watches.has(button)) return;
    if (typeof IntersectionObserver === 'undefined') return;   // the button simply stays

    const state = { io: null, mo: null };
    watches.set(button, state);

    const observe = (target) => {
        state.io = new IntersectionObserver((entries) => {
            if (!button.isConnected) { stop(button); return; }
            const last = entries[entries.length - 1];
            setAway(button, !!last && last.isIntersecting);
        }, { rootMargin: '0px 0px -25% 0px' });
        state.io.observe(target);
    };

    const target = document.getElementById(targetId);
    if (target) { observe(target); return; }

    // The form is drawn by the page around this component and can arrive a moment later.
    state.mo = new MutationObserver(() => {
        if (!button.isConnected) { stop(button); return; }
        const found = document.getElementById(targetId);
        if (!found) return;
        state.mo.disconnect();
        state.mo = null;
        observe(found);
    });
    state.mo.observe(document.body, { childList: true, subtree: true });
}

export function stop(button) {
    if (!button) return;
    const state = watches.get(button);
    if (!state) return;
    if (state.io) state.io.disconnect();
    if (state.mo) state.mo.disconnect();
    watches.delete(button);
    if (button.isConnected) setAway(button, false);
}
