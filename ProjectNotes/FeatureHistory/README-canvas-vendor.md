# Bringing the canvas editor into the site

Branch: `feature/canvas-vendor` (off develop, 2026-09-16). Ben: *"I have created another git repository where I
developed a new canvas editor which is a WASM. I would like to pull that work into my ben app as Ben.Wasm.Canvas to
host it, but it will be used in the site under Ben.Web.Website.Library.Manage.Messenger. This is going to be used in
the site as the Research Notes page."*

## What was already here

The server half, built on `feature/canvas-editor` and merged to develop before this: the boards API
(`api/canvas-documents`, revisions and conflicts), the guarded link unfurl and its image proxy, the published board
picture, `features.canvas-editor` (off by default), `StandaloneCanvasAddress`, and the deploy scripts that create and
publish `/editors/canvas/`. The editor itself was the missing half.

## What this branch does

1. **Vendors the five projects** from `github.com/paranormal365/MessageEditor` (`main` @ `b3b30b9`) to the repository
   root, unchanged — `Ben.Canvas.Core`, `Ben.Canvas.Editor`, `Ben.Wasm.Canvas`, `Ben.Canvas.Tests`,
   `Ben.Canvas.Playwright` — and adds them to `Ben.slnx` under a **Canvas Editor** folder. Root, because every project
   here lives at the root and because `deploy-ishaunted.ps1` already looked for `Repo\Ben.Wasm.Canvas`. Recorded in
   `Ben.Canvas.VENDORED.md`; the editor's own notes are `ProjectNotes/README-canvas-editor.md`.
2. **Adds the Research Notes page**, `Ben.Web.Website.Library/Manage/Messenger/ResearchNotesPage.razor` at
   `/organizations/{org}/cases/{case}/research-notes`. It waits for auth, asks the server what the person may do, and
   hands the case over to the canvas with a one-use code in the URL fragment — the same handover the video editor uses,
   through the same endpoint. Behind `features.canvas-editor`, like the page it opens.
3. **Puts a Research Notes tab on the case**, shown only when that switch is on, for the reason the Video tab beside it
   records: a link that always shows and sometimes works is worse than no link.
4. **Points the canvas docs and the deploy script's error at the in-repo path** instead of Ben's other machine.

## What is not done yet

- **The site still has its own research pages** (the block editor shipped in `feature/beta-feedback-1`). Both are
  reachable; which one becomes *the* research notes, and what happens to pages already written, is Ben's call.
- **The canvas is not wired into the site's Playwright suite.** `Ben.Canvas.Playwright` has its own browser tests and
  its own README; they run against the standalone host.
- **Nothing is switched on.** `features.canvas-editor` stays off until Ben turns it on in Site Settings.

## Proof

Solution builds; `Ben.Canvas.Tests` 763 green inside this repo; the site's own 6,216 green, including the deploy-script
guards that pin the canvas block.
