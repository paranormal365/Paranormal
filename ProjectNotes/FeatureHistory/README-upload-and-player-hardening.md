# Upload and player hardening — five findings from the sweep

Branch: `feature/upload-and-player-hardening`, cut from `develop` at `5065e9e`.

Five things the 2026-09-02 production sweep found by hand-building uploads the app itself never
sends. Four are fixed here; the fifth turned out not to exist.

## 1. An undecodable recording got a silent, dead control

The "arrived damaged" badge fires only on a digest mismatch. A file whose digest matches but whose
bytes the browser cannot decode rendered a `<audio>` that did nothing when pressed — which reads
as the player being broken, not the file.

Now the media elements preload their **metadata** rather than nothing, so the browser reads the
header at once and reports a file it cannot decode before anybody presses play (a small ranged
request per recording). The element's error is caught with `@onerror`, the control is replaced by
a **"won't play in this browser"** badge and a download link, so the bytes stay reachable. It is
deliberately not the "arrived damaged" badge: the transport was fine, the content was not.

## 2. Admin lists ignored `page` / `pageSize`

`api/admin/organizations`, `app-users` and `cases` accepted both parameters and returned everything
— a caller asking for twenty rows got two thousand with no way to tell. `ListPaging.Apply` now
honours both when both are sent (page size clamped to 500) and stamps **`X-Total-Count`** on every
answer, so "20 of 20" and "20 of 2,000" are distinguishable. A request with neither still returns
everything: the admin grids page on the client and depend on that. Covers the 26 controllers on
`AdminEntityControllerBase` and the hand-written cases list.

## 3. Leaving out `recordedByAppUserId` played back as "nobody signed in"

The app sends the signed-in user's id, so the app was never affected; a hand-built upload from an
authenticated account was attributed to nobody, a claim the request itself contradicted. Now:
**not sent → the sender recorded it**; **sent as the empty id → nobody did**, the client's
deliberate statement (a handed-over device, a session from before sign-in), kept as such.

## 4. A document with zero readings was accepted

It made a row, a file and a "Play back" button for a page with nothing to play. Refused at the
door with a sentence, where the app can tell the person before they leave the building.

## 5. The video editor's "second, empty `<base href>`" — not real

The served page at `ishaunted.com/editors/video/` has exactly one `<base>` element; checked in a
live DOM (`document.querySelectorAll('base')` → one). The "second" is the phrase `<base href>`
inside an HTML **comment** on line 54 of `index.html`, which is what a text scan of the page
counts. Nothing to change; the deploy script's "exactly one" check was right all along.

## How it is proved

- Unit: `AdminListPagingTests` (4), three new `FieldSessionUploadControllerTests` (empty document
  refused and nothing stored; omitted recorder → sender; empty id → nobody). Full suite 3,865 pass.
- Browser, `FieldSessionHardeningTests` (2), run against the side database: an empty document is
  refused with the sentence; a session uploaded without a recorder is credited to the sender, and a
  file of non-audio bytes with a correct digest shows the "won't play" badge, no `<audio>`, and no
  "arrived damaged".
