// Everything that comes onto the board from outside it - paste, drop and the Paste button - and the
// board's own copy and cut.
//
// The browser only reads the clipboard. What an item becomes is decided in C# (Core's PasteClassifier), so
// this module never creates a block, never decides policy, and only toggles one class of its own
// (bc-board--dropping).
//
// Rules that each fixed a real failure:
//  - Clipboard and drop data are read synchronously at the top of the handler. After the first await the
//    browser has already emptied the DataTransfer, and two dropped PDFs arrive as none.
//  - Safari fires copy, cut and paste only into something editable. A board shortcut therefore moves focus
//    to a permanent off-screen textarea (#bc-paste-catcher, rendered by Blazor) for the one key press, and
//    the handler hands focus straight back.
//  - Pasted files are stored on this device before C# hears about them. Only the first twelve bytes cross
//    into .NET, so the classifier can tell a real picture from a renamed HEIC without a large copy.
//  - JPEG, PNG and WebP are redrawn before they are stored, which drops EXIF: a phone photo carries the GPS
//    position of the place it was taken.
//  - The Paste button reads the clipboard inside its own native click. A round trip through C# first loses
//    the user gesture, and iOS then refuses the read.

import { assetWrite } from './opfsInterop.js'

const attached = new WeakMap()

function isEditable(el, catcher) {
  if (!el || el === catcher) return false
  // The phone's paste box exists only to receive a paste.
  if (el.classList && el.classList.contains('bc-paste-box')) return false
  const tag = el.tagName ? el.tagName.toLowerCase() : ''
  if (tag === 'input' || tag === 'textarea' || tag === 'select') return true
  if (el.isContentEditable) return true
  return !!el.closest('.k-editor, .k-popup, .k-animation-container, [role="dialog"], [role="menu"]')
}

function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID()
  const b = crypto.getRandomValues(new Uint8Array(16))
  b[6] = (b[6] & 0x0f) | 0x40
  b[8] = (b[8] & 0x3f) | 0x80
  const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

function sniff(head) {
  if (!head || head.length < 4) return null
  if (head[0] === 0xff && head[1] === 0xd8 && head[2] === 0xff) return 'jpeg'
  if (head[0] === 0x89 && head[1] === 0x50 && head[2] === 0x4e && head[3] === 0x47) return 'png'
  if (head[0] === 0x47 && head[1] === 0x49 && head[2] === 0x46 && head[3] === 0x38) return 'gif'
  if (head[0] === 0x42 && head[1] === 0x4d) return 'bmp'
  if (head.length >= 12 && head[0] === 0x52 && head[1] === 0x49 && head[2] === 0x46 && head[3] === 0x46
      && head[8] === 0x57 && head[9] === 0x45 && head[10] === 0x42 && head[11] === 0x50) return 'webp'
  if (head.length >= 12 && head[4] === 0x66 && head[5] === 0x74 && head[6] === 0x79 && head[7] === 0x70) {
    const brand = String.fromCharCode(head[8], head[9], head[10], head[11])
    if (['heic', 'heix', 'hevc', 'heim', 'heis', 'mif1', 'msf1'].includes(brand)) return 'heic'
  }
  return null
}

const EXT = { jpeg: '.jpg', png: '.png', gif: '.gif', bmp: '.bmp', webp: '.webp' }

function extFromName(name) {
  const m = /\.([a-z0-9]{1,8})$/i.exec(name || '')
  return m ? '.' + m[1].toLowerCase() : '.bin'
}

function extFromMime(mime) {
  const m = /^image\/(png|jpeg|gif|webp|bmp)$/i.exec(mime || '')
  return m ? EXT[m[1].toLowerCase()] : '.bin'
}

async function headOf(blob) {
  try { return new Uint8Array(await blob.slice(0, 12).arrayBuffer()) } catch { return null }
}

// Redraws a picture without its metadata. createImageBitmap applies the EXIF orientation first, so a
// sideways phone photo stays upright once the tag is gone.
//
// OffscreenCanvas where the browser has it; otherwise an ordinary canvas element that is never put on the
// page (Safari before 16.4, and Playwright's WebKit, have no OffscreenCanvas). Without the fallback a photo
// pasted on those browsers is not kept at all.
async function redraw(file, kind) {
  try {
    const bitmap = await createImageBitmap(file)
    const { width, height } = bitmap
    const type = kind === 'png' ? 'image/png' : kind === 'webp' ? 'image/webp' : 'image/jpeg'
    let blob = null
    if (typeof OffscreenCanvas === 'function') {
      const canvas = new OffscreenCanvas(width, height)
      canvas.getContext('2d').drawImage(bitmap, 0, 0)
      blob = await canvas.convertToBlob({ type, quality: 0.92 })
    } else {
      const canvas = document.createElement('canvas')
      canvas.width = width
      canvas.height = height
      canvas.getContext('2d').drawImage(bitmap, 0, 0)
      blob = await new Promise(resolve => canvas.toBlob(resolve, type, 0.92))
      canvas.width = canvas.height = 0
    }
    bitmap.close()
    return blob ? { blob, width, height } : null
  } catch {
    return null
  }
}

async function dimensions(file) {
  try {
    const bitmap = await createImageBitmap(file)
    const size = { width: bitmap.width, height: bitmap.height }
    bitmap.close()
    return size
  } catch {
    return { width: null, height: null }
  }
}

async function store(file, opts) {
  const name = file.name || ''
  const item = { kind: 'file', mimeType: file.type || '', fileName: name, size: file.size, assetId: null, ext: null, head: null, width: null, height: null }
  const head = await headOf(file)
  item.head = head
  const kind = sniff(head)

  // Refused by the classifier: nothing is stored, so nothing needs deleting.
  if (kind === 'heic' || /\.(heic|heif)$/i.test(name) || /^image\/hei[cf]/i.test(file.type || '')) return item
  const isPicture = !!EXT[kind]
  if (file.size > (isPicture ? opts.maxImageBytes : opts.maxFileBytes)) return item

  let blob = file
  let ext = EXT[kind] || extFromName(name)
  if (kind === 'jpeg' || kind === 'png' || kind === 'webp') {
    const clean = await redraw(file, kind)
    if (!clean) return item
    blob = clean.blob
    item.width = clean.width
    item.height = clean.height
    item.size = blob.size
    item.mimeType = blob.type || item.mimeType
    item.head = await headOf(blob)
    ext = EXT[sniff(item.head)] || ext
  } else if (isPicture) {
    Object.assign(item, await dimensions(file))
  }

  const id = uuid()
  if (await assetWrite(id, ext, blob)) {
    item.assetId = id
    item.ext = ext
  }
  return item
}

// Synchronous: every read happens before the handler's first await.
function capture(dt, opts) {
  const files = []
  try {
    for (const entry of dt.items || []) {
      if (entry.kind !== 'file') continue
      const file = entry.getAsFile()
      if (file) files.push(file)
    }
  } catch { /* some browsers expose files only */ }
  if (files.length === 0) {
    try { for (const file of dt.files || []) files.push(file) } catch { /* none */ }
  }

  const strings = []
  for (const type of ['text/plain', 'text/html', 'text/uri-list']) {
    let text = ''
    try { text = dt.getData(type) } catch { text = '' }
    if (!text) continue
    const limit = type === 'text/html' ? opts.maxHtmlChars : opts.maxTextChars
    strings.push({ kind: 'string', mimeType: type, text: text.length > limit + 1 ? text.slice(0, limit + 1) : text })
  }
  return { files, strings }
}

async function deliver(state, captured, source, boardX, boardY) {
  const items = [...captured.strings]
  for (const file of captured.files.slice(0, state.opts.maxItems + 1)) {
    items.push(await store(file, state.opts))
  }
  for (const file of captured.files.slice(state.opts.maxItems + 1)) {
    items.push({ kind: 'file', mimeType: file.type || '', fileName: file.name || '', size: file.size })
  }
  if (items.length === 0) {
    state.dotnet.invokeMethodAsync('OnPasteRefused', 'empty').catch(() => {})
    return
  }
  state.dotnet.invokeMethodAsync('OnPasteEnvelope', { items, source, boardX, boardY }).catch(() => {})
}

async function readClipboard(state) {
  const captured = { files: [], strings: [] }
  try {
    if (!navigator.clipboard || typeof navigator.clipboard.read !== 'function') throw new Error('no read')
    const list = await navigator.clipboard.read()
    for (const entry of list) {
      const picture = entry.types.find(t => t.startsWith('image/'))
      if (picture) {
        const blob = await entry.getType(picture)
        captured.files.push(new File([blob], 'pasted' + extFromMime(picture), { type: picture }))
        continue
      }
      for (const type of ['text/plain', 'text/html', 'text/uri-list']) {
        if (!entry.types.includes(type)) continue
        const text = await (await entry.getType(type)).text()
        if (text) captured.strings.push({ kind: 'string', mimeType: type, text })
      }
    }
  } catch {
    try {
      const text = navigator.clipboard ? await navigator.clipboard.readText() : ''
      if (text) captured.strings.push({ kind: 'string', mimeType: 'text/plain', text })
    } catch {
      state.dotnet.invokeMethodAsync('OnPasteRefused', 'permission').catch(() => {})
      return
    }
  }
  await deliver(state, captured, 'button', null, null)
}

export function attach(boardEl, catcherEl, dotnet, opts) {
  if (!boardEl || !catcherEl) return
  detach(boardEl)
  const root = boardEl.closest('.bc-editor') || document.body
  const state = { boardEl, catcherEl, dotnet, opts, root, returnTo: null }

  const giveFocusBack = () => {
    const target = state.returnTo
    state.returnTo = null
    catcherEl.value = ''
    if (document.activeElement !== catcherEl) return
    if (target && target.isConnected) target.focus({ preventScroll: true })
    else boardEl.focus({ preventScroll: true })
  }

  const inScope = () => {
    if (!boardEl.isConnected) return false
    const active = document.activeElement
    if (active === catcherEl) return true
    if (!active || active === document.body) return true
    if (isEditable(active, catcherEl)) return false
    return root.contains(active)
  }

  const onKeyDown = (e) => {
    if (!(e.ctrlKey || e.metaKey) || e.altKey || e.defaultPrevented) return
    const key = (e.key || '').toLowerCase()
    if (key !== 'v' && key !== 'c' && key !== 'x') return
    const active = document.activeElement
    if (!active || !boardEl.contains(active) || isEditable(active, catcherEl)) return
    if (key !== 'v' && !boardEl.querySelector('.bc-node[data-bc-selected]')) return
    state.returnTo = active
    catcherEl.value = ' '
    catcherEl.focus({ preventScroll: true })
    catcherEl.select()
    setTimeout(giveFocusBack, 100)
  }

  const onPaste = (e) => {
    if (e.defaultPrevented || !inScope() || !e.clipboardData) return
    const captured = capture(e.clipboardData, opts)
    e.preventDefault()
    giveFocusBack()
    deliver(state, captured, 'paste', null, null)
  }

  const onCopyOrCut = (e, cut) => {
    if (document.activeElement !== catcherEl || !e.clipboardData) return
    let payload = null
    try { payload = dotnet.invokeMethod('GetClipboardPayload') } catch { payload = null }
    giveFocusBack()
    if (!payload) return
    e.clipboardData.setData('text/plain', payload.json)
    e.clipboardData.setData('text/html', payload.html)
    e.preventDefault()
    if (cut) dotnet.invokeMethodAsync('OnCutCopied').catch(() => {})
  }
  const onCopy = (e) => onCopyOrCut(e, false)
  const onCut = (e) => onCopyOrCut(e, true)

  const accepts = (dt) => {
    const types = dt ? [...dt.types] : []
    return types.includes('Files') || types.includes('text/uri-list') || types.includes('text/plain')
  }
  const onDragOver = (e) => {
    if (isEditable(e.target, catcherEl) || !accepts(e.dataTransfer)) return
    e.preventDefault()
    e.dataTransfer.dropEffect = 'copy'
    boardEl.classList.add('bc-board--dropping')
  }
  const onDragLeave = (e) => {
    if (!e.relatedTarget || !boardEl.contains(e.relatedTarget)) boardEl.classList.remove('bc-board--dropping')
  }
  const onDrop = (e) => {
    boardEl.classList.remove('bc-board--dropping')
    if (!e.dataTransfer || isEditable(e.target, catcherEl)) return
    const captured = capture(e.dataTransfer, opts)
    if (captured.files.length === 0 && captured.strings.length === 0) return
    e.preventDefault()
    const rect = boardEl.getBoundingClientRect()
    deliver(state, captured, 'drop', e.clientX - rect.left, e.clientY - rect.top)
  }

  // The permanent photo picker (#bc-photo). Its files are handled like a paste, then the input is cleared so
  // the same photo can be picked again.
  const onChange = (e) => {
    const input = e.target
    if (!input || input.id !== 'bc-photo' || !input.files || input.files.length === 0) return
    const files = [...input.files]
    input.value = ''
    deliver(state, { files, strings: [] }, 'photo', null, null)
  }

  const onClick = (e) => {
    const button = e.target && e.target.closest ? e.target.closest('[data-bc-action="paste"]') : null
    if (!button || !root.contains(button)) return
    e.preventDefault()
    readClipboard(state)
  }

  document.addEventListener('keydown', onKeyDown, true)
  document.addEventListener('paste', onPaste)
  document.addEventListener('copy', onCopy)
  document.addEventListener('cut', onCut)
  boardEl.addEventListener('dragenter', onDragOver)
  boardEl.addEventListener('dragover', onDragOver)
  boardEl.addEventListener('dragleave', onDragLeave)
  boardEl.addEventListener('drop', onDrop)
  root.addEventListener('click', onClick)
  root.addEventListener('change', onChange)

  attached.set(boardEl, () => {
    document.removeEventListener('keydown', onKeyDown, true)
    document.removeEventListener('paste', onPaste)
    document.removeEventListener('copy', onCopy)
    document.removeEventListener('cut', onCut)
    boardEl.removeEventListener('dragenter', onDragOver)
    boardEl.removeEventListener('dragover', onDragOver)
    boardEl.removeEventListener('dragleave', onDragLeave)
    boardEl.removeEventListener('drop', onDrop)
    root.removeEventListener('click', onClick)
    root.removeEventListener('change', onChange)
    boardEl.classList.remove('bc-board--dropping')
  })
}

export function detach(boardEl) {
  const undo = boardEl ? attached.get(boardEl) : null
  if (!undo) return
  undo()
  attached.delete(boardEl)
}
