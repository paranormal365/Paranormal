'use strict';
/*
 * Layout/theme boot + the template's [data-action] contract.
 *
 * This replaces saveloadscript.js and the ~1,800-line smartApp.js, but deliberately keeps the
 * template's OWN conventions so its markup works here unmodified:
 *   - the same `layoutSettings` localStorage object  { theme, htmlRoot, themeStyle }
 *   - the same data-action names: toggle-theme, toggle, toggle-swap, app-fullscreen
 *   - the same `set-*` class filter on <html>
 *
 * The first block runs synchronously from <head> so theme and layout classes are applied before
 * first paint — without it the page renders light, then flips.
 *
 * What is deliberately NOT carried over from the original:
 *   - loadPanelState(): it removed and re-appended .panel nodes at parse time. Blazor owns that
 *     DOM; reordering it behind Blazor's back corrupts the render tree. Panel state is C# here.
 *   - the accordion/active-link scripts: BenNav owns sidebar state in C#.
 *   - injecting <link id="theme-style">: the Night skin is a plain <link> in the document head,
 *     so it is present with JS disabled and cannot flash a different palette first.
 *
 * The delegated listener below only ever mutates <html>'s class list and localStorage — never
 * Blazor-rendered markup — which is what makes it safe to run alongside the renderer.
 */

var htmlRoot = document.documentElement;
var layoutSettings = (function () {

    function read() {
        try {
            var raw = localStorage.getItem('layoutSettings');
            return raw ? (JSON.parse(raw) || {}) : {};
        } catch (e) { return {}; }
    }

    var s = read();

    // Theme: the template's own key first. 'ben-theme' is read as a fallback so a user arriving
    // from the original host keeps the light/dark choice they already made there.
    var theme = s.theme;
    if (theme !== 'light' && theme !== 'dark') {
        try {
            var legacy = localStorage.getItem('ben-theme');
            theme = (legacy === 'light' || legacy === 'dark')
                ? legacy
                : (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        } catch (e) { theme = 'light'; }
    }
    htmlRoot.setAttribute('data-bs-theme', theme);
    s.theme = theme;

    // Layout modifier classes. Only `set-*` survives the filter, so a tampered value cannot
    // inject arbitrary classes onto the document element.
    var kept = [];
    if (s.htmlRoot) {
        kept = String(s.htmlRoot).split(/[^\w-]+/).filter(function (c) { return /^set-/i.test(c); });
    }

    // The template's dark navigation treatment, which its own demos ship. Its rules apply only in
    // light mode (`.set-nav-dark:not([data-bs-theme=dark])`) and carry far more than a background:
    // the nav text, hover and active states, the active indicator and the logo panel all change
    // with it. That is why this is switched on rather than just recolouring the sidebar — a dark
    // background alone would leave the template's dark-on-light nav text sitting on it.
    //
    // app.css then greys the background off the palette; see --app-nav-bg there.
    //
    // A stored choice still wins, so this can be turned off per browser without a rebuild.
    if (!kept.some(function (c) { return /^set-nav-(dark|light)$/i.test(c); })) {
        kept.push('set-nav-dark');
    }

    if (kept.length) htmlRoot.className = (htmlRoot.className + ' ' + kept.join(' ')).trim();

    return s;
})();

/**
 * Re-applies the stored theme and layout classes to <html>.
 *
 * Needed because Blazor's enhanced navigation patches the document element from the server's
 * response, and the server never renders these classes — they are a client-side preference. So
 * every navigation silently wiped `set-nav-minified` and friends: the sidebar stayed narrow
 * (its width having been set before the wipe) while the labels un-faded and bled past its edge.
 *
 * Idempotent, so it is safe to call on every navigation.
 */
function benApplyLayoutSettings() {
    try {
        var raw = localStorage.getItem('layoutSettings');
        var s = raw ? (JSON.parse(raw) || {}) : {};

        if (s.theme === 'light' || s.theme === 'dark') {
            htmlRoot.setAttribute('data-bs-theme', s.theme);
        }

        if (s.htmlRoot) {
            var kept = String(s.htmlRoot)
                .split(/[^\w-]+/)
                .filter(function (c) { return /^set-/i.test(c); });
            kept.forEach(function (c) {
                if (!htmlRoot.classList.contains(c)) htmlRoot.classList.add(c);
            });
        }
    } catch (e) { /* storage unavailable — the page still renders, just without the preference */ }
}

// App.razor registers this against Blazor's "enhancedload" once Blazor has loaded.

function saveSettings() {
    try {
        layoutSettings.htmlRoot = String(htmlRoot.className)
            .split(/[^\w-]+/)
            .filter(function (c) { return /^set-/i.test(c); })
            .join(' ');
        layoutSettings.theme = htmlRoot.getAttribute('data-bs-theme') || 'light';
        localStorage.setItem('layoutSettings', JSON.stringify(layoutSettings));
        // Mirrored so the original host and this one agree on light/dark.
        localStorage.setItem('ben-theme', layoutSettings.theme);
    } catch (e) { /* storage unavailable — the change still applies for this page */ }
}

function setTheme(themeName) {
    if (themeName !== 'light' && themeName !== 'dark') return;
    htmlRoot.setAttribute('data-bs-theme', themeName);
    saveSettings();
    document.dispatchEvent(new CustomEvent('benThemeChange', { detail: themeName }));
}

function toggleTheme() {
    setTheme(htmlRoot.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark');
}

function benGetTheme() {
    return htmlRoot.getAttribute('data-bs-theme') || 'light';
}

function benToggleHtmlClass(className, force) {
    if (!/^set-[\w-]+$/i.test(className)) return false;
    var on = (typeof force === 'boolean') ? force : !htmlRoot.classList.contains(className);
    htmlRoot.classList.toggle(className, on);
    saveSettings();
    return on;
}

function benIsHtmlClassSet(className) {
    return htmlRoot.classList.contains(className);
}

function benRequestFullscreen() {
    if (document.fullscreenElement) { document.exitFullscreen(); return false; }
    if (htmlRoot.requestFullscreen) { htmlRoot.requestFullscreen(); return true; }
    return false;
}

function resetSettings() {
    try { localStorage.removeItem('layoutSettings'); } catch (e) { }
    window.location.reload();
}

// ── The template's delegated [data-action] dispatcher, reduced to the layout actions ────────
// Panel actions (panel-collapse/-fullscreen/-close/-style) are absent on purpose: those are
// Blazor components here, and letting a script also mutate them would give the same DOM two
// owners. Registered once, in the capture-free bubble phase, on document.
document.addEventListener('click', function (ev) {
    var el = ev.target instanceof Element ? ev.target.closest('[data-action]') : null;
    if (!el) return;

    switch (el.getAttribute('data-action')) {
        case 'toggle-theme':
            ev.preventDefault();
            toggleTheme();
            break;

        case 'toggle': {           // data-class="set-nav-minified"
            ev.preventDefault();
            var cls = el.getAttribute('data-class');
            if (cls) benToggleHtmlClass(cls);
            break;
        }

        case 'toggle-swap': {      // data-toggleclass="open" data-target="aside.js-drawer-settings"
            ev.preventDefault();
            var target = el.getAttribute('data-target');
            var swapCls = el.getAttribute('data-toggleclass');
            if (!swapCls) break;
            if (target) {
                var node = document.querySelector(target);
                if (node) node.classList.toggle(swapCls);
            } else {
                htmlRoot.classList.toggle(swapCls);
                saveSettings();
            }
            break;
        }

        case 'app-fullscreen':
            ev.preventDefault();
            benRequestFullscreen();
            break;
    }
});
