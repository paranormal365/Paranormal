// The live content of a BenEditor, read at the moment it is asked for.
//
// TelerikEditor reports its value to .NET on a 100 ms debounce. A person who types and presses Send or Save inside
// that window submits the value from before their last keystrokes — an empty message, or a note missing its last
// words (found by the e2e suite, 2026-09-14). Reading the editable region when the submit is handled is exact: by
// then the browser has applied every keystroke. The server sanitizes whatever arrives, as it always does.

export function currentHtml(host) {
    const editable = host?.querySelector('.ProseMirror')
    if (!editable) return null
    const copy = editable.cloneNode(true)
    // ProseMirror's own placeholders for an empty line, not content.
    copy.querySelectorAll('br.ProseMirror-trailingBreak').forEach(br => br.remove())
    copy.querySelectorAll('img.ProseMirror-separator').forEach(img => img.remove())
    return copy.innerHTML
}
