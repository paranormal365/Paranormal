const THEME_KEY = 'ben-theme';

// Returns the active theme: saved preference → OS preference → 'light'
function getPreferredTheme() {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'light' || saved === 'dark') return saved;
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

// Swaps the CSS href in-place (no remove/re-add = no flash)
function applyTheme(themeName) {
    const link = document.getElementById('theme-link');
    if (link) {
        const url = `./theme/ben-${themeName}/dist/css/ben-${themeName}.css`;
        if (!link.href.endsWith(url.replace('./', '/'))) {
            link.href = url;
        }
    }
    document.documentElement.setAttribute('data-bs-theme', themeName);
}

// Persist choice and apply immediately
function setTheme(themeName) {
    localStorage.setItem(THEME_KEY, themeName);
    applyTheme(themeName);
}

// Called by Blazor on first render — returns the resolved theme name
function initTheme() {
    const theme = getPreferredTheme();
    applyTheme(theme);
    return theme;
}
