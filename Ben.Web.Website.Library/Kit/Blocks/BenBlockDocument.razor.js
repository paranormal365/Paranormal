// The browser half of BenBlockDocument: dragging blocks into a new order, and pasting or dropping things onto the page.
//
// Blazor owns every element; this file only adds and removes classes while a drag is under way and tells .NET what
// happened — which block moved where, which file arrived, which link was pasted. (A plugin that moved the DOM itself would
// leave Blazor patching a tree it no longer matches — BenKitOwnsItsDomTests.)
//
// Touch: a drag starts after a 250 ms press on a block's handle, and a movement of more than 8 px before then is a scroll,
// not a drag — the same idiom as the seat picker. Mouse and pen start at once.

const _attached = new Map()

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
        const handle = e.target.closest?.('[data-block-handle]')
        if (!handle || !root.contains(handle) || e.button > 0) return
        const block = handle.closest('[data-block-id]')
        drag = { block, handle, id: block.dataset.blockId, pointerId: e.pointerId, startY: e.clientY, startX: e.clientX, started: false, before: null, end: false }
        if (e.pointerType === 'touch') {
            drag.timer = setTimeout(begin, 250)
        } else {
            e.preventDefault()
            begin()
        }
    }

    const onMove = e => {
        if (!drag || e.pointerId !== drag.pointerId) return
        if (!drag.started) {
            if (Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) > 8) { clearTimeout(drag.timer); drag = null }
            return
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
        if (started && (before || end)) dotnet.invokeMethodAsync('MovedAsync', id, before)
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

    root.addEventListener('pointerdown', onDown)
    root.addEventListener('pointermove', onMove)
    root.addEventListener('pointerup', onUp)
    root.addEventListener('pointercancel', onCancel)
    root.addEventListener('dragover', onDragOver)
    root.addEventListener('dragleave', onDragLeave)
    root.addEventListener('drop', onDrop)
    document.addEventListener('paste', onPaste, true)
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
        window.removeEventListener('blur', onCancel)
    })
}

export function detach(root) {
    _attached.get(root)?.()
    _attached.delete(root)
}

export function focusBlock(id) {
    const el = document.querySelector(`[data-block-id="${CSS.escape(id)}"]`)
    if (!el) return
    el.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
    ;(el.querySelector('.ProseMirror') ?? el.querySelector('[data-block-handle]'))?.focus()
}
