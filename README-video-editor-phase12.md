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
