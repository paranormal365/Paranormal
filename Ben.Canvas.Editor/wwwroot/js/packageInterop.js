// The .ishcanvas export and import, done in the browser.
//
// An export is a ZIP: document.json (deflated) and assets/{assetId}{ext} (stored, because pictures are
// already compressed). Core's CanvasPackage is the reference for the same format.
//
// It is built here rather than in .NET so a board with 300 MB of photos never passes through the .NET heap
// on a phone: the ZIP is assembled from Blob slices of the stored files, and an import writes each entry to
// the store as a slice of the chosen file. Only document.json crosses into C#.
//
// Entry names are rebuilt from the parsed id and extension, never used as paths.

import { readBlob, assetWrite } from './opfsInterop.js'

const ASSET = /^assets\/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(\.[a-zA-Z0-9]{1,8})$/

const CRC_TABLE = (() => {
  const table = new Uint32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    table[n] = c >>> 0
  }
  return table
})()

async function crc32(blob) {
  let crc = 0xffffffff
  const add = (bytes) => {
    for (let i = 0; i < bytes.length; i++) crc = CRC_TABLE[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8)
  }
  if (typeof blob.stream === 'function') {
    const reader = blob.stream().getReader()
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break
      add(value)
    }
  } else {
    add(new Uint8Array(await blob.arrayBuffer()))
  }
  return (crc ^ 0xffffffff) >>> 0
}

async function deflate(blob) {
  if (typeof CompressionStream !== 'function') return null
  try {
    return await new Response(blob.stream().pipeThrough(new CompressionStream('deflate-raw'))).blob()
  } catch {
    return null
  }
}

async function inflate(blob) {
  return await new Response(blob.stream().pipeThrough(new DecompressionStream('deflate-raw'))).blob()
}

function dosDateTime(date) {
  const time = (date.getHours() << 11) | (date.getMinutes() << 5) | Math.floor(date.getSeconds() / 2)
  const day = ((date.getFullYear() - 1980) << 9) | ((date.getMonth() + 1) << 5) | date.getDate()
  return { time, day }
}

function header(size) {
  const buffer = new ArrayBuffer(size)
  return { buffer, view: new DataView(buffer) }
}

// Export: returns { url, fileName, bytes, missing, tooLarge, totalBytes, largest }.
export async function exportPackage(documentJson, assets, fileName, maxBytes) {
  const encoder = new TextEncoder()
  const docBlob = new Blob([encoder.encode(documentJson)])

  const found = []
  const missing = []
  let total = docBlob.size
  for (const asset of assets || []) {
    const blob = await readBlob(asset.assetId, asset.ext)
    if (!blob) { missing.push(asset.name || asset.assetId); continue }
    found.push({ asset, blob })
    total += blob.size
  }

  if (total > maxBytes) {
    const largest = [...found].sort((a, b) => b.blob.size - a.blob.size).slice(0, 3).map(f => f.asset.name || f.asset.assetId)
    return { url: null, fileName, bytes: 0, missing, tooLarge: true, totalBytes: total, largest }
  }

  const parts = []
  const central = []
  let offset = 0
  const { time, day } = dosDateTime(new Date())

  const add = async (name, data, compress) => {
    const nameBytes = encoder.encode(name)
    const crc = await crc32(data)
    let body = data
    let method = 0
    if (compress) {
      const packed = await deflate(data)
      if (packed) { body = packed; method = 8 }
    }

    const local = header(30)
    local.view.setUint32(0, 0x04034b50, true)
    local.view.setUint16(4, 20, true)
    local.view.setUint16(6, 0x0800, true)
    local.view.setUint16(8, method, true)
    local.view.setUint16(10, time, true)
    local.view.setUint16(12, day, true)
    local.view.setUint32(14, crc, true)
    local.view.setUint32(18, body.size, true)
    local.view.setUint32(22, data.size, true)
    local.view.setUint16(26, nameBytes.length, true)
    local.view.setUint16(28, 0, true)
    parts.push(local.buffer, nameBytes, body)

    const entry = header(46)
    entry.view.setUint32(0, 0x02014b50, true)
    entry.view.setUint16(4, 20, true)
    entry.view.setUint16(6, 20, true)
    entry.view.setUint16(8, 0x0800, true)
    entry.view.setUint16(10, method, true)
    entry.view.setUint16(12, time, true)
    entry.view.setUint16(14, day, true)
    entry.view.setUint32(16, crc, true)
    entry.view.setUint32(20, body.size, true)
    entry.view.setUint32(24, data.size, true)
    entry.view.setUint16(28, nameBytes.length, true)
    entry.view.setUint32(42, offset, true)
    central.push(entry.buffer, nameBytes)

    offset += 30 + nameBytes.length + body.size
  }

  await add('document.json', docBlob, true)
  const seen = new Set()
  for (const { asset, blob } of found) {
    const name = `assets/${String(asset.assetId).toLowerCase()}${String(asset.ext).toLowerCase()}`
    if (seen.has(name)) continue
    seen.add(name)
    await add(name, blob, false)
  }

  const centralSize = central.reduce((sum, part) => sum + part.byteLength, 0)
  const end = header(22)
  end.view.setUint32(0, 0x06054b50, true)
  end.view.setUint16(8, seen.size + 1, true)
  end.view.setUint16(10, seen.size + 1, true)
  end.view.setUint32(12, centralSize, true)
  end.view.setUint32(16, offset, true)

  const zip = new Blob([...parts, ...central, end.buffer], { type: 'application/zip' })
  return { url: URL.createObjectURL(zip), fileName, bytes: zip.size, missing, tooLarge: false, totalBytes: total, largest: [] }
}

async function entryData(file, entry) {
  const local = new DataView(await file.slice(entry.offset, entry.offset + 30).arrayBuffer())
  if (local.getUint32(0, true) !== 0x04034b50) throw new Error('bad entry')
  const start = entry.offset + 30 + local.getUint16(26, true) + local.getUint16(28, true)
  const raw = file.slice(start, start + entry.compressed)
  if (entry.method === 0) return raw
  if (entry.method === 8) return await inflate(raw)
  throw new Error('unsupported compression')
}

async function entriesOf(file) {
  const tailSize = Math.min(file.size, 22 + 0xffff)
  const tail = new DataView(await file.slice(file.size - tailSize).arrayBuffer())
  let end = -1
  for (let i = tail.byteLength - 22; i >= 0; i--) {
    if (tail.getUint32(i, true) === 0x06054b50) { end = i; break }
  }
  if (end < 0) return null

  const count = tail.getUint16(end + 10, true)
  const size = tail.getUint32(end + 12, true)
  const start = tail.getUint32(end + 16, true)
  if (start + size > file.size) return null

  const directory = new DataView(await file.slice(start, start + size).arrayBuffer())
  const decoder = new TextDecoder()
  const entries = []
  let p = 0
  for (let i = 0; i < count; i++) {
    if (p + 46 > directory.byteLength || directory.getUint32(p, true) !== 0x02014b50) return null
    const nameLength = directory.getUint16(p + 28, true)
    const extraLength = directory.getUint16(p + 30, true)
    const commentLength = directory.getUint16(p + 32, true)
    entries.push({
      name: decoder.decode(new Uint8Array(directory.buffer, p + 46, nameLength)),
      method: directory.getUint16(p + 10, true),
      compressed: directory.getUint32(p + 20, true),
      size: directory.getUint32(p + 24, true),
      offset: directory.getUint32(p + 42, true),
    })
    p += 46 + nameLength + extraLength + commentLength
  }
  return entries
}

// Import: returns { problem, documentJson, stored, tooLarge: [names], notStored: [names] }.
// problem is 'none' (no file chosen), 'not-a-board', or null.
export async function importPackage(inputEl, maxAssetBytes) {
  const file = inputEl && inputEl.files ? inputEl.files[0] : null
  if (!file) return { problem: 'none', documentJson: null, stored: 0, tooLarge: [], notStored: [] }
  try {
    const entries = await entriesOf(file)
    const docEntry = entries ? entries.find(e => e.name === 'document.json') : null
    if (!docEntry) return { problem: 'not-a-board', documentJson: null, stored: 0, tooLarge: [], notStored: [] }

    const documentJson = await (await entryData(file, docEntry)).text()
    const tooLarge = []
    const notStored = []
    let stored = 0
    for (const entry of entries) {
      const m = ASSET.exec(entry.name)
      if (!m) continue
      const label = m[1].toLowerCase() + m[2].toLowerCase()
      if (entry.size > maxAssetBytes) { tooLarge.push(label); continue }
      try {
        const data = await entryData(file, entry)
        if (await assetWrite(m[1].toLowerCase(), m[2].toLowerCase(), data)) stored++
        else notStored.push(label)
      } catch {
        notStored.push(label)
      }
    }
    return { problem: null, documentJson, stored, tooLarge, notStored }
  } catch {
    return { problem: 'not-a-board', documentJson: null, stored: 0, tooLarge: [], notStored: [] }
  } finally {
    try { inputEl.value = '' } catch { /* read-only in some browsers */ }
  }
}
