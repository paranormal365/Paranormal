// The browser half of BenBlockDocument: dragging blocks into a new order, and pasting or dropping things onto the page.
//
// Blazor owns every element; this file only adds and removes classes while a drag is under way and tells .NET what
// happened — which block moved where, which file arrived, which link was pasted. (A plugin that moved the DOM itself would
// leave Blazor patching a tree it no longer matches — BenKitOwnsItsDomTests.)
//
// Touch: a drag starts after a 250 ms press on a block's handle, and a movement of more than 8 px before then is a scroll,
// not a drag — the same idiom as the seat picker. Mouse and pen start once the pointer has moved 4 px. A press that never
// became a drag is a click, and a click on the handle opens the block's options; the click that ends a real drag is
// swallowed so it does not open them too.

const _attached = new Map()

// Where a drawn (not yet editing) text block was pressed: the block id and how many characters into its words. The editor
// that replaces the drawing puts the cursor after the same character, so a click into a sentence edits it there. Counted in
// characters, not as a point on the screen: the editor lays the same words out differently (padding, a toolbar above), and
// a point taken from the drawing landed at the start of the editor's text (UI test pass, 2026-09-14).
let _pressedText = null

// Keys typed between a press that opens a text editor (a drawn text block, or Add Text) and that editor being ready. The
// editor arrives after a trip to the server, and a person who clicks and types at once used to lose their first letters —
// on this machine a few, on a slow connection a word (UI test pass, 2026-09-14). They are held here and given to the editor
// when it is ready, as the paste of a line of text. Only plain characters and Backspace are held; anything else — a
// shortcut, Enter, Tab, an arrow — ends the hold and goes where it was going.
let _typeAhead = null
const TypeAheadMs = 5000

function isUrl(text) {
    if (!text || /\s/.test(text) || text.length > 2000) return false
    try { const u = new URL(text); return u.protocol === 'http:' || u.protocol === 'https:' } catch { return false }
}

async function sendFiles(dotnet, files, maxBytes) {
    for (const file of files) {
        if (file.size > maxBytes) {
            await dotnet.invokeMethodAsync('FileTooLargeAsync', file.name, file.size)
            continue
        }
        const stream = DotNet.createJSStreamReference(file)
        await dotnet.invokeMethodAsync('ReceiveFileAsync', file.name || 'pasted', file.type || 'application/octet-stream', file.size, stream)
    }
}

export function attach(root, dotnet, maxBytes) {
    if (!root || _attached.has(root)) return

    let drag = null   // { block, id, pointerId, startY, started, timer, target, before }

    const blocks = () => [...root.querySelectorAll('[data-block-id]')]

    const clearMarks = () => {
        root.querySelectorAll('.is-drop-before, .is-drop-after').forEach(el => el.classList.remove('is-drop-before', 'is-drop-after'))
    }

    const begin = () => {
        if (!drag) return
        drag.started = true
        drag.block.classList.add('is-dragging')
        try { drag.handle.setPointerCapture(drag.pointerId) } catch { /* not capturable */ }
    }

    const onDown = e => {
        const drawn = e.target.closest?.('[data-testid=text-block]')
        if (drawn && root.contains(drawn)) {
            const at = rangeAtPoint(e.clientX, e.clientY)
            _pressedText = at && drawn.contains(at.startContainer)
                ? { id: drawn.closest('[data-block-id]')?.dataset.blockId, offset: charactersBefore(drawn, at.startContainer, at.startOffset) }
                : null
        }
        const opensText = drawn ?? e.target.closest?.('[data-testid=block-add-text]')
        _typeAhead = opensText && root.contains(opensText) && e.button <= 0 ? { text: '', since: performance.now() } : null
        const handle = e.target.closest?.('[data-block-handle]')
        if (!handle || !root.contains(handle) || e.button > 0) return
        const block = handle.closest('[data-block-id]')
        drag = { block, handle, id: block.dataset.blockId, pointerId: e.pointerId, touch: e.pointerType === 'touch', startY: e.clientY, startX: e.clientX, started: false, before: null, end: false }
        if (drag.touch) {
            drag.timer = setTimeout(begin, 250)
        } else {
            e.preventDefault()   // no text selection while the pointer moves; the click still arrives
        }
    }

    const onMove = e => {
        if (!drag || e.pointerId !== drag.pointerId) return
        if (!drag.started) {
            const moved = Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY)
            if (drag.touch) {
                if (moved > 8) { clearTimeout(drag.timer); drag = null }
                return
            }
            if (moved <= 4) return
            begin()
        }
        e.preventDefault()
        clearMarks()
        const others = blocks().filter(b => b !== drag.block)
        const over = others.find(b => { const r = b.getBoundingClientRect(); return e.clientY < r.top + r.height / 2 })
        if (over) { over.classList.add('is-drop-before'); drag.before = over.dataset.blockId; drag.end = false }
        else if (others.length) { others[others.length - 1].classList.add('is-drop-after'); drag.before = null; drag.end = true }
    }

    const onUp = e => {
        if (!drag || e.pointerId !== drag.pointerId) return
        clearTimeout(drag.timer)
        const { started, id, before, end, block } = drag
        drag = null
        block.classList.remove('is-dragging')
        clearMarks()
        if (started && (before || end)) {
            // The click that follows this pointerup belongs to the drag, not to the handle's options. It is dispatched in
            // this same task, so the listener is gone by the next one and cannot eat a later, real click.
            const swallow = ev => { ev.stopPropagation(); ev.preventDefault() }
            root.addEventListener('click', swallow, { capture: true, once: true })
            setTimeout(() => root.removeEventListener('click', swallow, { capture: true }), 0)
            dotnet.invokeMethodAsync('MovedAsync', id, before)
        }
    }

    const onCancel = () => {
        if (!drag) return
        clearTimeout(drag.timer)
        drag.block.classList.remove('is-dragging')
        drag = null
        clearMarks()
    }

    // Paste anywhere on the page (OneNote-like, Ben 2026-09-14). Inside a text block's editor, text is the editor's to
    // paste; files are still turned into picture and file blocks rather than pasted into the text as data.
    const onPaste = e => {
        const active = document.activeElement
        if (!active || !root.contains(active)) return
        if (active.matches('input, textarea, select')) return
        const inEditor = !!active.closest('.k-editor')
        const data = e.clipboardData
        if (!data) return

        const files = [...(data.files ?? [])]
        if (files.length) {
            e.preventDefault()
            e.stopPropagation()
            sendFiles(dotnet, files, maxBytes)
            return
        }
        if (inEditor) return

        const text = (data.getData('text/plain') ?? '').trim()
        if (isUrl(text)) {
            e.preventDefault()
            dotnet.invokeMethodAsync('PastedLinkAsync', text)
            return
        }
        const html = data.getData('text/html') ?? ''
        if (text || html) {
            e.preventDefault()
            dotnet.invokeMethodAsync('PastedTextAsync', html, text)
        }
    }

    const onDragOver = e => {
        if ([...(e.dataTransfer?.types ?? [])].includes('Files')) { e.preventDefault(); root.classList.add('is-file-over') }
    }
    const onDragLeave = e => { if (!root.contains(e.relatedTarget)) root.classList.remove('is-file-over') }
    const onDrop = e => {
        root.classList.remove('is-file-over')
        const files = [...(e.dataTransfer?.files ?? [])]
        if (!files.length) return
        e.preventDefault()
        sendFiles(dotnet, files, maxBytes)
    }

    const onKeyDown = e => {
        if (!_typeAhead) return
        const target = e.target
        if (performance.now() - _typeAhead.since > TypeAheadMs
            || target.closest?.('.ProseMirror, input, textarea, select, [contenteditable=true]')) {
            _typeAhead = null   // the editor is already there, or the person is typing somewhere else
            return
        }
        if (e.isComposing) return
        if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
            _typeAhead.text += e.key
        } else if (e.key === 'Backspace' && _typeAhead.text) {
            _typeAhead.text = _typeAhead.text.slice(0, -1)
        } else if (!['Shift', 'CapsLock'].includes(e.key)) {
            _typeAhead = null
            return
        }
        e.preventDefault()   // a space on the Add Text button would press it again
    }

    root.addEventListener('pointerdown', onDown)
    root.addEventListener('pointermove', onMove)
    root.addEventListener('pointerup', onUp)
    root.addEventListener('pointercancel', onCancel)
    root.addEventListener('dragover', onDragOver)
    root.addEventListener('dragleave', onDragLeave)
    root.addEventListener('drop', onDrop)
    document.addEventListener('paste', onPaste, true)
    document.addEventListener('keydown', onKeyDown, true)
    window.addEventListener('blur', onCancel)

    _attached.set(root, () => {
        root.removeEventListener('pointerdown', onDown)
        root.removeEventListener('pointermove', onMove)
        root.removeEventListener('pointerup', onUp)
        root.removeEventListener('pointercancel', onCancel)
        root.removeEventListener('dragover', onDragOver)
        root.removeEventListener('dragleave', onDragLeave)
        root.removeEventListener('drop', onDrop)
        document.removeEventListener('paste', onPaste, true)
        document.removeEventListener('keydown', onKeyDown, true)
        window.removeEventListener('blur', onCancel)
    })
}

export function detach(root) {
    _attached.get(root)?.()
    _attached.delete(root)
}

// Puts focus where the person will work next: in a text block, its editor — at the point they clicked, or at the end.
//
// UI test pass, 2026-09-14: the editor of a text block is created after the render that opens it, so looking for it
// straight away found nothing and focused the drag handle instead — "Text" then typing lost every key, and clicking into
// an existing block opened its editor with nothing focused. A text block now waits for its editor to appear.
export function focusBlock(id, pasteHtml, pasteText) {
    const el = document.querySelector(`[data-block-id="${CSS.escape(id)}"]`)
    if (!el) return
    el.scrollIntoView({ block: 'nearest', behavior: 'smooth' })

    const pressed = _pressedText?.id === id ? _pressedText : null
    _pressedText = null

    const ready = async pm => {
        await placeCaret(pm, pressed)
        if (pasteHtml || pasteText) pasteInto(pm, pasteHtml, pasteText)
        const typed = _typeAhead?.text
        _typeAhead = null
        if (typed) pasteInto(pm, null, typed)
    }

    const editable = el.querySelector('.ProseMirror')
    if (editable) { ready(editable); return }
    if (el.dataset.kind !== 'text') { _typeAhead = null; el.querySelector('[data-block-handle]')?.focus(); return }

    const watch = new MutationObserver(() => {
        const pm = el.querySelector('.ProseMirror')
        if (!pm) return
        watch.disconnect()
        clearTimeout(giveUp)
        ready(pm)
    })
    watch.observe(el, { childList: true, subtree: true })
    const giveUp = setTimeout(() => { watch.disconnect(); _typeAhead = null }, TypeAheadMs)
}

// The page's own paste handler leaves a paste inside an editor to the editor, so this reaches ProseMirror's clipboard
// parsing — the same path as a person pasting there.
function pasteInto(editable, html, text) {
    const data = new DataTransfer()
    if (html) data.setData('text/html', html)
    data.setData('text/plain', text ?? '')
    editable.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }))
}

// Resolves once the editor knows where the cursor is. ProseMirror learns of a cursor moved from outside on the browser's
// next selectionchange event; something pasted before then goes where its cursor last was — the start (UI test pass 5.14).
function placeCaret(editable, pressed) {
    editable.focus()
    const selection = window.getSelection()
    const range = document.createRange()
    const at = pressed ? pointAfterCharacters(editable, pressed.offset) : null
    if (at) {
        range.setStart(at.node, at.offset)
        range.collapse(true)
    } else {
        range.selectNodeContents(editable)
        range.collapse(false)
    }

    const current = selection.rangeCount ? selection.getRangeAt(0) : null
    if (current && current.compareBoundaryPoints(Range.START_TO_START, range) === 0
        && current.compareBoundaryPoints(Range.END_TO_END, range) === 0) {
        return Promise.resolve()   // already there: no event will come, and none is needed
    }
    const seen = new Promise(resolve => document.addEventListener('selectionchange', () => resolve(), { once: true }))
    selection.removeAllRanges()
    selection.addRange(range)
    return seen
}

// Safari and Chrome answer caretRangeFromPoint; Firefox only the standard caretPositionFromPoint.
function rangeAtPoint(x, y) {
    if (document.caretRangeFromPoint) return document.caretRangeFromPoint(x, y)
    const at = document.caretPositionFromPoint?.(x, y)
    if (!at) return null
    const range = document.createRange()
    range.setStart(at.offsetNode, at.offset)
    return range
}

// The words of a block, as text nodes in reading order. Whitespace-only nodes holding a line break are the stored HTML's
// formatting between paragraphs, which the editor does not keep, so they are not counted on either side.
function wordNodes(root) {
    const nodes = []
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
        if (!/^\s*$/.test(n.textContent) || !n.textContent.includes('\n')) nodes.push(n)
    }
    return nodes
}

function charactersBefore(root, node, offset) {
    const before = document.createRange()
    before.setStart(root, 0)
    before.setEnd(node, offset)
    let count = 0
    for (const n of wordNodes(root)) {
        if (n === node) return count + offset
        if (!before.intersectsNode(n)) break
        count += n.textContent.length
    }
    return count
}

function pointAfterCharacters(root, count) {
    for (const n of wordNodes(root)) {
        if (count <= n.textContent.length) return { node: n, offset: count }
        count -= n.textContent.length
    }
    return null   // past the end, or no words yet: the caller puts the cursor at the end
}
