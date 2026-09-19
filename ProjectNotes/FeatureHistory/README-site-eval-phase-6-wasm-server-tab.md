# Site evaluation 2026-09-06 — Phase 6: the WASM editor's Server tab

Branch: `feature/site-eval-phase-6-wasm-server-tab`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 6 — The WASM editor's server tab*.

## The problem

The video editor's **Server** tab lists the media somebody has on the site and brings it into the
editor. The evaluation walked it and found one defect and three things the screen was not saying.

- **V-2 (defect).** Download on an audio file: progress ran to 100%, the download answered 200,
  and the file was nowhere. Audio bin still said "No audio yet", the library size was unchanged,
  and nothing said anything. Twice.
- **V-3.** "All media" listed seven identical `test-audio.mp3` rows — other people's uploads —
  with no owner and no case to tell them apart.
- **V-4.** The Video bin marked two clips "on timeline" while Live playback said "2 clips are
  missing their media". The bin knew nothing about the state the clip's own warning showed.
- **V-6.** `/my-videos` offers "Standalone editor", which goes to `/editors/video/` — a path that
  exists only as an IIS mount and is a 404 on the dev stack.

## What each one actually was

### V-2 — the download worked; the import never reached a bin

The bytes arrived and were cached correctly. What did not happen was anything else.

**`AddCachedFileToTimelineAsync` — the Server tab's import — never called `Clips.AddToBin`.** The
local-file import beside it always has. So a file brought in from the server was on the timeline
and absent from the bin that claims to list what the project has; and **audio, in an editor with
audio tracks switched off, was not placed either** — so it went nowhere at all, which is exactly
what the evaluation walked. All three kinds now reach their bin, and audio that cannot be placed
says why in the import row instead of being dropped.

**The second half was the affordance.** Bringing a file over is deliberately two clicks — download,
then add — and after the first one the card showed a green tick whose only explanation was a hover
tooltip. Somebody who had just watched a progress bar finish went looking in the bins and found
nothing, with no message, because the message was a `title` attribute. The card says
**Click to add** in words now.

**A third thing the walk turned up:** the second click needs the ffmpeg engine, and refuses with
"click Initialize" when it is not running. That is correct — downloading needs no engine and
decoding does — but it is another way the file appears to go nowhere. The help now says which step
needs the engine.

### V-3 — a file name is not an identity

"All media" spans several people's uploads, and the evaluation met seven rows all called
`test-audio.mp3`. Every row now carries **who owns it** and **which case it belongs to**, when
there is one. A file handed to a group (item 180 Phase B) has no owning person, so the group's
name is the answer and it is labelled as a group.

Both are computed by the listing endpoint in two batched lookups over the page being returned, and
are null everywhere else `UploadFileRecord` is used — a join every other consumer would pay for
unread.

### V-4 — the bin and the preview disagreed

The Video bin said "on timeline" while Live playback, reading the same project, said "2 clips are
missing their media". The bin's label was built from the duration and the placement count, and
never asked whether the bytes were there.

That is the worse half of a disagreement: the bin is where somebody looks to see what the project
**has**, so "on timeline" reads as a promise that the thing will render. All three bins now say
**media missing**, in place of the placement count rather than beside it — the placement does not
matter while there is nothing to play — and the line is coloured so a missing clip is findable in a
grid of twenty.

### V-6 — a link that only works in production

"Standalone editor" on `/my-videos` linked to the literal `/editors/video/`: a path IIS mounts in
production and nothing serves in development. The site's one route into the standalone editor was a
404 on every machine the site is built on, and the evaluation had to drive the phase-12 handoff by
hand against `:5180`.

The address now comes from the environment — `VideoEditor:StandaloneUrl` where a deployment sets
it, the WebAssembly dev server in development, and the mount path everywhere else. The plan's other
option was hiding the button when the mount is absent; that trades a broken link for a missing
feature, and would have hidden the editor on exactly the machines where somebody is working on it.

## Key files

| what | where |
|---|---|
| The Server tab's import, now reaching all three bins | `Ben.Video.Editor/Components/ClipBrowser.razor` |
| What a bin card says | `Ben.Video.Editor/Models/BinCardState.cs` (new) |
| Owner and case, on the listing | `Ben.Data.WebApi/Controllers/Entities/MediaLibraryController.cs`, `UploadFileRecord`, both media-library providers |
| Owner and case, on the card | `Ben.Video.Editor/Models/MediaLibraryFile.cs` — `Provenance` |
| Where the standalone editor is | `Ben.Web.Services/StandaloneEditorAddress.cs` (new) |

**No migration.**

## Verified

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 errors, 0 warnings |
| Ben.Web.Tests | 4,570 passed, 0 failed |
| Ben.Video.Tests | 2,573 passed, 0 failed. Ben.Wasm.Video.Tests 34, Ben.Service.RepositoryService.Tests 319 |
| Playwright `WasmEditorTests` | 16 passed, 0 failed, 0 skipped |
| The whole Playwright suite on a fresh database | 474 passed, 7 failed, 40 skipped in 24 minutes. All seven are the pre-existing set recorded in `ProjectNotes/AudioEditor-Audit-2026-09-06.md` |
| V-6, on screen | `/my-videos` now links "standalone editor" at `http://localhost:5180/` rather than a 404 |

### Each new test seen to fail without its fix

| reverted | failed |
|---|---|
| `Clips.AddToBin(audioClip)` removed from the Server import | `Every_branch_of_the_server_import_adds_its_clip_to_the_bin` (unit) and `AServerAudioFileLandsInTheAudioBin` (browser, 2m timeout waiting for the bin entry) |

## Two things the browser walk found that no unit test could

**The import window's overlay covers the tab strip.** The first version of the walk switched to the
Audio tab straight after the second click and spent thirty seconds being told a Kendo overlay
intercepted the pointer. The import dialog is part of the flow and has to be dismissed; the test
follows it now.

**An unscoped emptiness check reads the wrong bin.** The media panel keeps every tab's markup in the
DOM, so `.bv-browser__empty` resolves against whichever bin answers first. The walk now follows the
file by name rather than asking whether some bin is empty.

**And the second click needs the engine.** Downloading a server file uses no ffmpeg by design;
adding one to the project reads the cached bytes and probes them, so it refuses with "click
Initialize" when the engine is not running. The walk starts it, and the help now says which of the
two steps needs it.

## A third thing, in the suite rather than the product

The full run failed `The_leak_warning_fires_before_save_not_after_it` — a phase 4 test that had
passed in phase 5's full run and passed twice in isolation here, including beside
`PublishLeakWarningTests`. Both fixtures drive the Edit Case dialog on the same seeded case, each
changing it, asserting, and restoring it. In parallel one fixture's restore lands in the middle of
the other's assertion.

`[NonParallelizable]` is what this suite already uses for shared seeded state — a dozen fixtures
carry it. Three more do now: the two above and `FirstClickAfterTypingTests`, which creates reports
on the same case.

