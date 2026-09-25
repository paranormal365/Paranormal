// Where boards live on this device.
//
// localStorage holds only small things: the board list, which board is open, each board's view. On
// ishaunted.com it is shared with the video editor and holds about 5 MB, so board bodies go to IndexedDB
// (database bc-docs), which has room. When IndexedDB is refused (some private windows) a body falls back
// to localStorage, where a large board may not fit; every write answers true or false so the editor can
// say so instead of reporting a save that did not happen.

export function getItem(key) {
  try { return localStorage.getItem(key) } catch { return null }
}

export function setItem(key, value) {
  try { localStorage.setItem(key, value); return true } catch { return false }
}

export function removeItem(key) {
  try { localStorage.removeItem(key); return true } catch { return false }
}

const DB_NAME = 'bc-docs'
const STORE = 'docs'
let dbPromise = null

function openDb() {
  if (dbPromise) return dbPromise
  dbPromise = new Promise((resolve) => {
    try {
      const request = indexedDB.open(DB_NAME, 1)
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(STORE)) request.result.createObjectStore(STORE)
      }
      request.onsuccess = () => {
        // Another tab (or a test) deleting or upgrading the database must not wait on this page.
        request.result.onversionchange = () => { request.result.close(); dbPromise = null }
        resolve(request.result)
      }
      request.onerror = () => resolve(null)
      request.onblocked = () => resolve(null)
    } catch {
      resolve(null)
    }
  })
  return dbPromise
}

function run(mode, work) {
  return openDb().then((db) => {
    if (!db) return { ok: false }
    return new Promise((resolve) => {
      try {
        const tx = db.transaction(STORE, mode)
        const request = work(tx.objectStore(STORE))
        tx.oncomplete = () => resolve({ ok: true, value: request ? request.result : undefined })
        tx.onerror = () => resolve({ ok: false })
        tx.onabort = () => resolve({ ok: false })
      } catch {
        resolve({ ok: false })
      }
    })
  })
}

export async function docPut(key, text) {
  const written = await run('readwrite', store => store.put(text, key))
  if (written.ok) {
    removeItem(key)
    return true
  }
  return setItem(key, text)
}

export async function docGet(key) {
  const read = await run('readonly', store => store.get(key))
  if (read.ok && typeof read.value === 'string') return read.value
  return getItem(key)
}

export async function docDelete(key) {
  await run('readwrite', store => store.delete(key))
  removeItem(key)
  return true
}

// Asks the browser not to clear this site's storage under pressure. Safari may still clear it after seven
// days without a visit; the editor tells the person when the answer is no.
export async function persist() {
  try {
    if (!navigator.storage) return false
    if (navigator.storage.persisted && await navigator.storage.persisted()) return true
    return navigator.storage.persist ? await navigator.storage.persist() : false
  } catch {
    return false
  }
}
