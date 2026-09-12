# Chunked uploads — past Cloudflare's 100 MB, up to a configurable 2 GiB

The site is served through a Cloudflare tunnel, and Cloudflare refuses any request body over
100 MB. Evidence recordings are bigger than that. This branch makes large uploads a series of
small PUTs — each chunk comfortably under the ceiling — governed by two new site settings, with
the website's Upload Files page converted as the first surface.

## The two settings (Administration → Site Settings)

| Key | Meaning | Default when unset |
|---|---|---|
| `upload.max-file-bytes` | Largest file anyone may upload, either path | 2 GiB (2147483648) |
| `upload.chunk-max-bytes` | Largest single chunk | 64 MiB (67108864) |

Defaults live at the read site (`UploadLimitsReader`), per the settings convention. The chunk
setting's description warns about the 100 MB ceiling; the reader also clamps the chunk size to
the file limit so the pair can never contradict. The classic multipart upload now enforces
`upload.max-file-bytes` too — one number governs both doors (the old "no practical limit"
comments are gone on both sides).

## The API (`api/chunked-uploads`)

- `POST /` — opens a session from the file's facts (name, size, type, file-type id); refuses
  oversize, wrong-extension, and SVG (sanitised whole-document on the classic path only) before
  a byte is sent. Returns the session with `chunkMaxBytes`, so the ceiling lives on the server.
- `PUT /{id}/chunks/{n}` — one raw chunk; any order; idempotent per index; counted against the
  cap as it streams (`CappedReadStream`) and against the declared total.
- `GET /{id}` — received chunks and bytes, for resume.
- `POST /{id}/complete` — refuses gaps or a byte-count mismatch with 409; otherwise assembles
  chunks in index order (`ConcatenatingReadStream` — one open chunk at a time, never the whole
  file in memory) into an ordinary `UploadFile` with the same audit and metadata-extraction
  behaviour as the classic upload.
- `DELETE /{id}` — abort, removing every trace.

Sessions live on the file storage (`chunk-sessions/{owner}/…`): a JSON manifest beside the chunk
files. No schema migration, and a session survives an app restart because the disk is the state.
Sessions abandoned for 24 h are swept on the owner's next start — which needed one new member on
`IFileStorageService` (`ListFiles`), the abstraction's first way to find files it didn't already
know the names of. Ownership is the caller's token; anyone else gets 404, never 403.

## The website path

Bytes never ride the Blazor circuit (InputFile streams over SignalR in 32 KB messages — wrong
pipe for gigabytes). Instead:

1. The circuit starts the session through the API client and mints an **upload ticket** —
   `UploadTicketService`, the `MediaTicketService` pattern in the opposite direction: Data
   Protection-encrypted, bound to one session id, 12 h lifetime, a distinct protector purpose so
   media and upload tickets can never impersonate each other.
2. `wwwroot/js/chunked-upload.js` slices the file and PUTs chunks to this site's relay
   endpoints (`/uploads/chunked/…?t=ticket`), with per-chunk retry (1 s / 3 s / 7 s; 4xx is
   fatal immediately) and resume from the server's status. The JS never sees a token.
3. The relays (`Program.cs` + `UploadRelay`) unprotect the ticket and stream the body to the
   API with the uploader's own bearer — the API stays the only authority.

`/uploads/classic/{nonce}` is the same relay idea for the files chunking refuses: SVGs go
through the classic multipart endpoint browser-side, the nonce existing only to bind a ticket.

**Upload Files** (`/upload-files`) now takes **multiple files**, shows per-file progress bars,
and uploads them sequentially — parallel files would split the same home upstream without
finishing anything sooner. The same-name prompt lost its "Replace It" shortcut (it needed the
circuit-held IBrowserFile that no longer exists); the grid's Replace button is unchanged and the
dialog points there.

## For the iPhone/iPad apps

The API contract above is client-agnostic and already whole: start → chunk PUTs (raw bodies,
`Authorization: Bearer`) → complete. A native client talks to `api/chunked-uploads` directly —
no tickets or relays, those exist purely because browser JS must not hold a token. Use a
background `URLSession`; chunk sizes come from the session record, not from the app.

## Tests

- `ChunkedUploadControllerTests` — the lifecycle against an in-memory storage with real
  semantics (a Moq stub that ignores writes would pass a controller that assembles garbage):
  out-of-order assembly byte-for-byte, idempotent re-send, resume status, both size caps with
  settings rows, the declared-total promise, gap/mismatch conflicts at complete, SVG and
  extension policy at start, stranger-gets-404 everywhere, abort, and the 24 h sweep
  (backdating a manifest, proving the live session survives).
- `ConcatenatingReadStreamTests` — order, laziness, disposal-when-drained.
- `UploadTicketServiceTests` — round trip, session binding, tamper/foreign key ring, and
  media↔upload ticket non-interchangeability.
- `UploadFileSizeLimitTests` — the classic path refuses over the configured limit by sentence,
  and under it proceeds to the next check.
- Not covered here: the browser JS and the relay endpoints end-to-end — that is a Playwright
  pass (needs the running stack and credentials) once this lands.

Also in this branch: `RegisterOrganization_CreatesOrgAndOwnerMembership` updated for the eight
default roles — the expectation was never updated when the Investigator Role joined the defaults
(564f332d), and it failed on every branch cut from develop since.
