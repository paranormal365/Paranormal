# Video editor phase 6 — server edges without the server in the middle

Branch: `feature/video-editor-phase5-persistence` (phase 6 continues on it).
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 6.
Follows `README-video-editor-phase5.md`.

Phase 5 made the work survive the session. This phase is about what happens when the work has to
leave the machine — and about the fact that, in several places, it could not.

Six commits, `2c67b357` to `9e8c4c4b`.

## Slice 1 — the standalone editor can publish, and can let you in (F11, F12, F15, site-4, wasm-2, wasm-4, wasm-16)

The editor deployed at `/editors/video` could not upload a finished video **at all**. The editor
offers a server destination only when the page supplies a publish handler, and that page supplied
none, so every export went straight to the downloads folder. Its own sign-in page promised that
signing in lets you "publish finished renders". The one thing the whole local-first design exists
to end with — delivering the result — was the thing it could not do.

There was also no way to sign in. Nothing anywhere linked to `/login`, the sign-out method had no
caller, and the only way in was to know the URL and type it. Three more in the same area:

| | what it did |
|---|---|
| Sign-in redirect | Navigated to `"/"`, which under the production sub-path is the *site's* root — a successful sign-in walked straight out of the editor |
| Two-factor accounts | Could not sign in here at all, and were told their password was wrong while typing it correctly |
| A 404 or a 5xx | Said the same thing, sending somebody to reset a password that was right |

`WasmVideoExportPublisher` throws on every failure on purpose: the destination prompt catches,
stays open and keeps "Save to my machine" available. Returning normally tells the editor the video
is safe, at which point it discards the only remaining copy.

The toolbar's sign-in chip arrives through a slot (`HostStatusContent`) the editor offers hosts,
rather than the editor guessing at who is signed in. This host had no tests at all; it has
`Ben.Wasm.Video.Tests` now, over the sign-in paths, which is where being wrong means somebody
cannot get in.

## Slice 2 — bytes stop crossing the circuit (site-1, site-2, media-6)

Two things the site host did by pulling whole files through the SignalR circuit, both of which
stop working at the sizes this editor actually produces.

- **Publishing** read the finished render back as one JS-interop `byte[]` return. Blazor Server
  caps that at 32 KB by default and nothing raises it, so a real render could not be published
  from the site at all. The browser now posts the file straight to the API through this site's own
  upload relay: the circuit mints a short-lived ticket bound to one project, the browser posts to
  an endpoint that takes the ticket, the endpoint streams the body onward. The access token never
  reaches the page and the file never enters the site's memory.
- **Downloading** did the same in reverse — fetched into the server's memory, copied into a byte
  array, shipped over the circuit. Three copies of a file the browser could fetch itself, with the
  buffer sized by an `int` cast that goes negative past 2 GB, which uploads can now be since the
  size caps were removed.

Both are optional by construction: a host that registers neither falls back to exactly what it did
before. A missing registration degrades rather than breaks.

## Slice 3 — one authenticated server store (F13, persistence-13)

Nobody had ever successfully saved a project to the server from the site. The editor posted the
project itself over a named `HttpClient`, and on Blazor Server the bearer token lives in the
circuit where a handler registered at the application root cannot reach it. The button was drawn
because a URL was configured, which says the server exists, not that anybody can reach it.

`IProjectServerStore` because the two hosts genuinely reach the server differently, and pretending
otherwise is what produced a button that could not work on one of them. The same change fixes the
other half: every save created a new row, so a project saved five times became five projects with
the same name.

## Slice 4 — a refusal says so (site-10, site-11, callouts-5, persistence-15, F11)

A refusal looked exactly like an empty library: the site's provider returned an empty list for any
failed response, so an expired session read as "you have not uploaded anything". The standalone
host showed the raw HTTP exception, which is not much better.

Also here: opening the Assets tab signed out took the whole tab down, because a 401 from the
account library escaped and stopped the shapes that would have loaded fine from the shared
catalogue. Published videos were never cleaned up — publishing twice orphaned the first render,
deleting a project kept its video. The case Video tab was always shown while the page behind it is
feature-gated, so with the editor off the tab led to "Page not found".

## Slice 5 — signed out, the editor stops offering the server (F13's other half)

`IEditorSignInState`, optional: a host that can say whether anybody is signed in gets asked, and
one that cannot keeps the old behaviour, which is right for a host with no accounts.

## Slice 6 — a case's video work belongs to the case (persistence-14, site-6, site-7, site-13, media-10, wasm-12)

Case projects were private to whoever pressed Save. Each member saw a different list on the case's
Video tab, and the "By" column was hard-coded to say *You* — while help described the tab as shared
work. Reading is shared now; writing is not, and buttons the server would refuse are no longer
drawn.

A render published to a case now appears on the case's Files tab. It was written into the case's
folder and then nothing on the case pointed at it. Publishing again replaces the case's copy, and
removing a render takes its case link with it.

Plus: a refresh button on the Server tab (the list was fetched once and never again, and a failed
load had no retry because the gate was "no files yet", which an error satisfies); the signed-out
site pages, which stated the requirement with nothing to click; and `MediaLibraryPicker`, deleted —
a second, diverged import path no component referenced, with no audio track, four hard-coded
thumbnails, no scope, and a comment citing a method that no longer exists.

## What the screen found that the tests did not

- **The standalone editor still offered "Save to Server" while signed out.** Every test passed:
  the store was available because a URL was configured, which was exactly the wrong question. This
  is what slice 5 exists for, and it was found by looking at the toolbar.
- **A server the editor cannot reach printed `TypeError: Failed to fetch`.** Slice 4 taught the
  Server tab to explain a *refusal*; verifying that on screen showed it had learned nothing about a
  connection that never happened. `MediaLibraryProblemText` fixes it and is pure, so the wording
  can be checked without a server to fail against.
- **Phase 5's Razor guard caught a phase 6 mistake.** A comment I put inside `<Toolbar … />`'s
  attribute list failed `RazorMarkupGuardTests` at build time instead of at render time in the
  browser. The guard has now paid for itself once.

## Verified on screen

Standalone host at 1440×900, browser storage cleared, no API running.

- The toolbar carries **Sign in**; **Save to server** is absent while signed out.
- The Server tab, refused with a 401, reads *"Sign in to see the files you have uploaded.
  Everything else in the editor works signed out."* with the host's own sign-in button beneath it.
- The Server tab, with the server unreachable, reads *"Could not reach the server. Check your
  connection, then try again."* The refresh button beside the scope lists fires a second request.
- Import a 4.8 s `.mov`, Export Now. The destination prompt offers **Upload to server** *disabled*,
  titled "Sign in to upload this to your media library", beside **Save to my machine** and
  **Discard**. Discard asks before deleting the render. The project prompt that follows offers
  Save Locally and Skip, with no server option — the same gate, in a second place.
- Reload. Phase 5's autosave brings the project, the clip, the bin entry and the footage back, and
  the engine starts itself.

## Not done in this phase

- **`ProjectListDialog` as one "Projects" dialog with "On this computer" and "On the server"
  sections**, backed by a testable `ProjectsDialogState`. The two lists still live in different
  places. Worth doing, but it is a dialog rewrite rather than a server edge, and nothing in it is
  currently wrong — only duplicated.
- **Server-tab previews and thumbnails consulting the OPFS cache first** (media-5). Placement
  already does; previews still fetch and discard. A wasted fetch, not a failure.
- **`IProjectServerStore.ListAsync` / `GetAsync` / `DeleteAsync` / `PublishAsync`.** The plan named
  a fuller interface. Saving was the operation that was broken on one host and duplicating rows on
  both; the rest work where they are, and moving them would be a refactor with no defect behind it.
- **A Playwright pass over any of this.** The e2e suite needs three hosts and a signed-in account,
  and this phase's whole subject is what happens with and without one. Phase 11 is where that
  belongs.
