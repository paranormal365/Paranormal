# Video editor phase 12 — the handoff, and a player that plays the timeline

Branch: `feature/video-editor-phase12-handoff-compositor`, cut from `master` after phase 11 merged.
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 12.
Follows `README-video-editor-phase11.md`.

## Why this phase exists

Two things were deliberately left until last, for the same reason: both are better built on top of
a timeline that can be trusted and a render that matches it, which is what phases 2 to 4 were for.

**The handoff.** The standalone editor at `/editors/video/` is the host that matches what this
product is meant to be — everything assembled and rendered on the person's own machine. But
somebody who is already signed in on the site and clicks through to it arrives signed out, and has
to type their password again into a second door. That is the whole reason the site's link to the
standalone editor reads as a curiosity rather than as where the work happens.

**The live compositor (decision D5).** Today's preview is a proxy: after every edit the editor
re-encodes a small video of the whole timeline and plays that. It is correct, it is what export is
verified against, and it is slow — a person editing a long recording waits for an encode to see a
cut. Camtasia plays the timeline itself, seeking between the source files. That is a sequence
player, and it is the last piece because the proxy is what proves it right.

## What the audit asked for

- `POST /api/auth/editor-handoff` [Authorize], issuing a 60 second single-use code.
- `POST /api/auth/editor-handoff/exchange` [AllowAnonymous, auth rate limit], minting bearer
  tokens the way `/login` does.
- The site's "Open in standalone editor" link carries `#handoff=<code>&project=<serverId>`.
- **Never relay the site's refresh token.**
- `PlaybackMode.Live` with a pure `TimelineSequencer.Resolve(t)`, two alternating `<video>`
  elements fed from OPFS blob URLs, a hidden `<audio>` per track, images and overlays drawn live,
  hard cuts where a transition is, and a per-clip fallback to the rendered proxy.

## Rules this phase works under

- The code travels in the URL **fragment**, which browsers never send to a server — so it stays
  out of access logs, out of `Referer`, and out of anything a proxy records.
- Single use and sixty seconds. A code that has been exchanged is gone; a code that was not is
  gone a minute later.
- The exchange mints its own tokens through Identity's own sign-in handler. The site's tokens are
  never handed to another origin.
- A handoff is a sign-in, so it is counted as one.

## Status

Slice A (handoff) and slice B (live player) are recorded below as they land.

## What phase 12 did

### Slice A — the handoff

- `POST /api/auth/editor-handoff` [Authorize] issues a code for whoever asked; `POST .../exchange`
  [AllowAnonymous, auth rate limit] spends it and mints tokens through Identity's own handler.
- `EditorHandoffCodeStore` keeps only a SHA-256 hash of each code, for sixty seconds, once.
- My Videos gained a **Standalone editor** button that carries `#handoff=<code>&project=<id>`.
- The standalone host exchanges the code before anything renders and erases the fragment either
  way, with `history.replaceState` — routing treats a URL that differs only by its fragment as the
  one it is already on, so `NavigateTo` changed nothing and left the code in the address bar.
- Teaching the link to name a project meant teaching the editor to fetch one back from the server,
  which never existed: work could be pushed somewhere it could not be reached again (F15). A
  project named while nobody is signed in is held rather than dropped.
- A handoff is recorded as its own kind of sign-in, so the dashboard's counts stay honest.

### Slice B — the live player (decision D5)

- `TimelineSequencer` answers what a player has to ask sixty times a second: what is on screen,
  where inside its source, what is audible, and when that changes.
- `LivePlaybackPlan` writes the whole timeline out as a list the player follows on its own, because
  asking .NET per frame would cost more than the drawing.
- `livePreviewInterop.js` runs the loop: two video elements so a cut is a swap rather than a seek,
  an image element for stills, an audio element per audio clip, drift correction, and a cap on how
  much time a single frame may credit — `requestAnimationFrame` stops while a tab is in the
  background, so without the cap a five-minute detour advanced the playhead five minutes.
- `LivePreview.razor` hosts it, with a **Live / Rendered** switch over the corner of the picture.

Opening it found a bug of exactly the kind this arc keeps finding: the player looked for a clip's
media under the clip's own id, and a clip placed from the media bin stores it under the bin entry's
id, so a clip plainly on the timeline played as black. The rule now lives in `MediaStorage` and the
project restore uses the same one.

**What Live deliberately is not.** It plays the picture and the sound. Transitions are plain cuts;
effects, titles and callouts are not drawn; a second video track is not composited. Rendered
remains what export is checked against, and the help says so.

### Numbers

| Suite | Before | After |
|---|---|---|
| `Ben.Video.Tests` | 2479 | 2542 |
| `Ben.Web.Tests` | 4209 | 4236 |
| `Ben.Wasm.Video.Tests` | 10 | 34 |
| Editor Playwright | 13 | 16 |

### Not verified end to end

The handoff's server half is covered by 27 tests rather than a live sign-in: that would need a real
password and would write a sign-in row to the live database. The client half was walked on screen —
the code is spent, the fragment is erased, and a refused handoff says what it is waiting for.
