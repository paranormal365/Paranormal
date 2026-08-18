// Returns the viewer's IANA timezone id (e.g. "America/Chicago"), or null if unavailable.
window.benGetBrowserTimeZone = function () {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
    } catch {
        return null;
    }
};
