// The photo wall's one piece of browser work (item 235 phase 11): asking for full screen, which only a
// user's press may do. It changes nothing on the page — Blazor draws everything.
export function goFullScreen() {
    const el = document.documentElement;
    if (!document.fullscreenElement && el.requestFullscreen) {
        el.requestFullscreen().catch(() => { /* refused by the browser; the wall still fills the window */ });
    }
}
