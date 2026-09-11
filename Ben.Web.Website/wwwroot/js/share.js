// Handing a page's address to whatever the reader shares with.
//
// Two paths, in order of how well they serve the person pressing the button:
//
//   1. The browser's own share sheet, where there is one. On a phone that is the list of
//      applications they actually use, which is a better answer than any list this site could
//      draw, and it costs no backend at all — the address IS the thing being shared.
//   2. The clipboard, everywhere else. The caller says "Link copied" only when this is what
//      happened, which is why it returns true here and false above: a share sheet that was
//      dismissed must not claim anything was done.
//
// Every refusal is ordinary, not an error. A dismissed share sheet throws AbortError; a clipboard
// write can be refused by permission or by the document not being focused. None of that deserves a
// message, and none of it may escape into the Blazor circuit as an unhandled exception.
window.benShareLink = async function (url) {
    if (!url) return false;

    if (navigator.share) {
        try {
            await navigator.share({ url });
            return false; // shared, but nothing was copied — say nothing.
        } catch {
            // Dismissed, or the sheet refused. Fall through and try the clipboard.
        }
    }

    try {
        await navigator.clipboard.writeText(url);
        return true;
    } catch {
        return false;
    }
};
