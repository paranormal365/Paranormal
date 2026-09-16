// Case pictures and files, fetched and uploaded by the browser itself (review correction R17).
//
// The API's file routes need the bearer token, and a bare <img src> never sends one, so pictures are
// fetched here and shown through object URLs. Uploads read the device copy (a blob: address) and post it
// as a form. Either way the bytes never cross into .NET. C# decides which addresses may get the token.
//
// At most six requests run at once, so opening a board full of pictures does not flood the API.

const MAX_AT_ONCE = 6;
let running = 0;
const waiting = [];

function slot() {
  if (running < MAX_AT_ONCE) { running++; return Promise.resolve(); }
  return new Promise(resolve => waiting.push(resolve));
}

function release() {
  const next = waiting.shift();
  if (next) next(); else running--;
}

function authHeaders(token) {
  return token ? { Authorization: 'Bearer ' + token } : {};
}

/** Fetches an API address with the token and answers { status, url } - url is an object URL when status is 200. */
export async function fetchAsObjectUrl(url, token) {
  await slot();
  try {
    const response = await fetch(url, { headers: authHeaders(token), credentials: 'omit', cache: 'default' });
    if (response.status !== 200) return { status: response.status, url: null };
    const blob = await response.blob();
    return { status: 200, url: URL.createObjectURL(blob) };
  } catch {
    return { status: 0, url: null };
  } finally {
    release();
  }
}

/** Uploads the file behind a blob: address as form field "file"; answers { status, body }. */
export async function uploadFromUrl(url, token, sourceUrl, fileName, description) {
  await slot();
  try {
    const source = await fetch(sourceUrl);
    if (!source.ok) return { status: 0, body: '' };
    const form = new FormData();
    form.append('file', await source.blob(), fileName);
    form.append('description', description || '');
    const response = await fetch(url, { method: 'POST', body: form, headers: authHeaders(token), credentials: 'omit' });
    return { status: response.status, body: await response.text() };
  } catch {
    return { status: 0, body: '' };
  } finally {
    release();
  }
}

export function revokeObjectUrl(url) {
  try { URL.revokeObjectURL(url); } catch { /* already gone */ }
}
