// Pictures and files pasted onto a board, kept on this device as bc-assets/{assetId}{ext}.
//
// The origin private file system (OPFS) is the first choice. Safari before 26 cannot write an OPFS file
// from the page (createWritable is missing) and some private windows refuse OPFS altogether, so the same
// functions fall back to IndexedDB. The fallback stores each picture's bytes (an ArrayBuffer), not a Blob:
// some WebKit builds and older Safari cannot store Blobs in IndexedDB at all, and bytes work everywhere.
// The probe writes and reads back real data: an API that exists is not an API that works.
//
// The bytes of a picture never pass through .NET. Display addresses are blob: URLs, one per asset for the
// life of the page, so a re-render keeps the same src and the browser does not decode the picture again.

const DIR = 'bc-assets'
const NAME = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.[a-z0-9]{1,8}$/
const IDB_NAME = 'bc-assets'
const IDB_STORE = 'assets'

let backendPromise = null
const urls = new Map()

function nameOf(assetId, ext) {
  const name = String(assetId).toLowerCase() + String(ext).toLowerCase()
  return NAME.test(name) ? name : null
}

async function opfsDirectory() {
  const root = await navigator.storage.getDirectory()
  return root.getDirectoryHandle(DIR, { create: true })
}

async function probeOpfs() {
  try {
    if (!navigator.storage || typeof navigator.storage.getDirectory !== 'function') return false
    const dir = await opfsDirectory()
    const handle = await dir.getFileHandle('probe.txt', { create: true })
    if (typeof handle.createWritable !== 'function') return false
    const writer = await handle.createWritable()
    await writer.write(new Blob(['ok']))
    await writer.close()
    const back = await (await handle.getFile()).text()
    try { await dir.removeEntry('probe.txt') } catch { /* harmless */ }
    return back === 'ok'
  } catch {
    return false
  }
}

let idbPromise = null
function openIdb() {
  if (idbPromise) return idbPromise
  idbPromise = new Promise((resolve) => {
    try {
      const request = indexedDB.open(IDB_NAME, 1)
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(IDB_STORE)) request.result.createObjectStore(IDB_STORE)
      }
      request.onsuccess = () => {
        // Another tab (or a test) deleting or upgrading the database must not wait on this page.
        request.result.onversionchange = () => { request.result.close(); idbPromise = null }
        resolve(request.result)
      }
      request.onerror = () => resolve(null)
      request.onblocked = () => resolve(null)
    } catch {
      resolve(null)
    }
  })
  return idbPromise
}

function idb(mode, work) {
  return openIdb().then((db) => {
    if (!db) return { ok: false }
    return new Promise((resolve) => {
      try {
        const tx = db.transaction(IDB_STORE, mode)
        const request = work(tx.objectStore(IDB_STORE))
        tx.oncomplete = () => resolve({ ok: true, value: request ? request.result : undefined })
        tx.onerror = () => resolve({ ok: false })
        tx.onabort = () => resolve({ ok: false })
      } catch {
        resolve({ ok: false })
      }
    })
  })
}

async function probeIdb() {
  const written = await idb('readwrite', store => store.put({ bytes: new TextEncoder().encode('ok').buffer, type: 'text/plain', at: Date.now() }, 'probe'))
  if (!written.ok) return false
  const read = await idb('readonly', store => store.get('probe'))
  await idb('readwrite', store => store.delete('probe'))
  return !!(read.ok && read.value && read.value.bytes && read.value.bytes.byteLength === 2)
}

function backend() {
  if (!backendPromise) {
    backendPromise = (async () => {
      if (await probeOpfs()) return 'opfs'
      if (await probeIdb()) return 'idb'
      return ''
    })()
  }
  return backendPromise
}

// 'opfs', 'idb', or '' when this browser will not keep anything.
export async function isAvailable() {
  return await backend()
}

export async function assetWrite(assetId, ext, blob) {
  const name = nameOf(assetId, ext)
  if (!name || !blob) return false
  const kind = await backend()
  try {
    if (kind === 'opfs') {
      const dir = await opfsDirectory()
      const handle = await dir.getFileHandle(name, { create: true })
      const writer = await handle.createWritable()
      await writer.write(blob)
      await writer.close()
      return true
    }
    if (kind === 'idb') {
      const bytes = await blob.arrayBuffer()
      return (await idb('readwrite', store => store.put({ bytes, type: blob.type || '', at: Date.now() }, name))).ok
    }
  } catch {
    try { if (kind === 'opfs') await (await opfsDirectory()).removeEntry(name) } catch { /* nothing written */ }
  }
  return false
}

export async function assetWriteBytes(assetId, ext, bytes) {
  return assetWrite(assetId, ext, new Blob([bytes]))
}

// The stored Blob, or null. Used by the export; not exported to .NET, which never takes the bytes.
export async function readBlob(assetId, ext) {
  const name = nameOf(assetId, ext)
  if (!name) return null
  const kind = await backend()
  try {
    if (kind === 'opfs') {
      const dir = await opfsDirectory()
      return await (await dir.getFileHandle(name)).getFile()
    }
    if (kind === 'idb') {
      const read = await idb('readonly', store => store.get(name))
      if (!read.ok || !read.value) return null
      if (read.value.bytes) return new Blob([read.value.bytes], { type: read.value.type || '' })
      return read.value.blob || null
    }
  } catch {
    return null
  }
  return null
}

export async function assetExists(assetId, ext) {
  return (await readBlob(assetId, ext)) !== null
}

export async function assetUrl(assetId, ext) {
  const name = nameOf(assetId, ext)
  if (!name) return null
  if (urls.has(name)) return urls.get(name)
  const blob = await readBlob(assetId, ext)
  if (!blob) return null
  const url = URL.createObjectURL(blob)
  urls.set(name, url)
  return url
}

export function assetRevoke(assetId, ext) {
  const name = nameOf(assetId, ext)
  if (!name || !urls.has(name)) return
  URL.revokeObjectURL(urls.get(name))
  urls.delete(name)
}

export function assetRevokeAll() {
  for (const url of urls.values()) URL.revokeObjectURL(url)
  urls.clear()
}

// [{ assetId, ext, size, modified }] for every stored asset. modified is milliseconds since 1970, so a sweep
// can leave alone a file another tab stored a moment ago and has not saved into a board yet.
export async function assetList() {
  const kind = await backend()
  const found = []
  try {
    if (kind === 'opfs') {
      const dir = await opfsDirectory()
      for await (const [name, handle] of dir.entries()) {
        if (handle.kind !== 'file' || !NAME.test(name)) continue
        const file = await handle.getFile()
        found.push({ assetId: name.slice(0, 36), ext: name.slice(36), size: file.size, modified: file.lastModified })
      }
    } else if (kind === 'idb') {
      const db = await openIdb()
      if (!db) return found
      await new Promise((resolve) => {
        const tx = db.transaction(IDB_STORE, 'readonly')
        const cursor = tx.objectStore(IDB_STORE).openCursor()
        cursor.onsuccess = () => {
          const c = cursor.result
          if (!c) return
          if (typeof c.key === 'string' && NAME.test(c.key)) {
            found.push({ assetId: c.key.slice(0, 36), ext: c.key.slice(36), size: c.value?.bytes?.byteLength ?? c.value?.blob?.size ?? 0, modified: c.value?.at ?? 0 })
          }
          c.continue()
        }
        tx.oncomplete = () => resolve()
        tx.onerror = () => resolve()
      })
    }
  } catch {
    /* a partial list is still a list; the sweep only deletes what it saw */
  }
  return found
}

export async function assetDelete(assetId, ext) {
  const name = nameOf(assetId, ext)
  if (!name) return false
  assetRevoke(assetId, ext)
  const kind = await backend()
  try {
    if (kind === 'opfs') {
      await (await opfsDirectory()).removeEntry(name)
      return true
    }
    if (kind === 'idb') return (await idb('readwrite', store => store.delete(name))).ok
  } catch {
    return false
  }
  return false
}

export async function estimate() {
  try {
    const e = await navigator.storage.estimate()
    return { usage: e.usage ?? 0, quota: e.quota ?? 0 }
  } catch {
    return { usage: 0, quota: 0 }
  }
}
