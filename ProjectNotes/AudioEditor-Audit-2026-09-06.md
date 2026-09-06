# Audio editor audit — what the walk saw (2026-09-06)

Phase 0 of `AudioEditor-Audit-2026-09-06-Plan.md`. The plan's findings came from reading the code;
this file records what the editor actually did when used, on an isolated stack (throwaway
database `IsHauntedDb_audio_walk`, its own uploads directory, api :5252, web :5078), signed in as
Sarah — an ordinary organisation administrator — through Playwright, with the seeded password read
from the gitignored dev settings by the harness and never typed. Screenshots and the raw verdict
log are in `AudioEditor-Walk-2026-09-06/`. The walk itself is
`Ben.Web.Playwright/Tests/AudioEditorWalkTests.cs`, kept `[Explicit]`.

Verdicts: **OBSERVED** = seen on screen and matches the plan's finding; **NEW** = not in the plan;
**CORRECTED** = the plan had it wrong; **UNREACHED** = the walk could not get there.

## Headline: the editor is a 300-pixel dialog

**P (NEW, S).** "Open Full View" opens the editor in `<BenModal … Size="sm">` — Bootstrap's
`modal-sm`, 300 px wide. The toolbar scrolls sideways inside it, the waveform is squeezed to a
270 px strip, the file-info block fills the top third with empty space, and panels (EQ, markers,
edit) stack below the fold. Every screenshot in the folder shows it. The commit that did it is
`bde4e03f` (2026-08-18, "wave C — the Manage area"), which replaced the previous `TelerikWindow`
with a `BenModal` and picked the smallest size. Nothing in the audit sweeps caught it because
nothing in the code says "300 px"; it says `sm`. This is the first thing to fix, because every
other defect in the full view is being experienced through a keyhole.
*Screenshots:* `01-fullview-open.png`, `05-saved-clips.png`, `07-after-scan.png`.

## Full view

| id | verdict | what happened |
|---|---|---|
| A | **OBSERVED** | The X closed the modal; the next parent render (clicking the compact player) brought it straight back. There is no way to leave the editor except navigating away. |
| B | **OBSERVED** | With a region drawn at 1:14.6–1:33.2, turning on Silence moved the edit target to 3:00.6–3:06.5 — a machine-found stretch — and the drawn region was gone (`02-silence-on.png` shows no region at all). Cut/Silence now act on detected silence. |
| C | **UNVERIFIABLE in the keyhole** | Colormap and resolution changes were made, but the spectrogram canvas sits below the fold of the 300 px modal and the three screenshots are identical. Stands as in the plan (code-confirmed); re-check on screen after P. |
| D | **CODE** | Four enable checkboxes toggle a bound bool and nothing else; not observable from outside. Stands as in the plan. |
| E | partly | Seven of the eight edits produced a saved clip. **Silence produced nothing within 60 s and showed no error** (`edit:Silence`). Cause not yet known — the region it would have used was the machine one from B. |
| F | **OBSERVED** | All seven saved clips carry the badge `0:00.0–0:00.0`. |
| G | **OBSERVED** | The edit panel's region readout sits beside Gain/Fade/Speed/Pitch, which ignore it. |
| H | **UNREACHED** | Blocked by I every time: the explorer for the first region came back before a second region could be right-clicked. The code reading stands; re-check after I is fixed. |
| I | **OBSERVED** (twice) | The Region Explorer's X closed it and the parent brought it back, exactly as A. In run 4 the count was 0 immediately after the X and the explorer was back again by the next region draw — it returns on whatever parent render comes next. |
| J | **OBSERVED** | With a confirmed marker in the list, its ▶ moved nothing: no media element playing, no Pause button. Candidates' ▶ (a different path) does play. |
| regions | PASS | One user region at a time, as designed; drawing works even in the keyhole. |
| scan | PASS | Medium sensitivity found 21 candidates on the fixture; the message says so plainly. |
| review | PASS | "Keep it" without a label is refused inline — the walk's first pass misread that validation as a defect. |

## Case mixer

| id | verdict | what happened |
|---|---|---|
| K-length | **OBSERVED** | Every clip block is 120 px wide whatever the file is; the fixture is 3:06 and the ruler only reaches 60 s. The grid cannot represent the file it holds. |
| K-9th | **OBSERVED** | Nine adds put nine blocks on the grid, no message; blocks stack at offset 0 once the eight tracks are full. |
| K-transport | **OBSERVED** | Play/Pause/Stop disabled, no player. |
| K-remove | **OBSERVED** | The clip block swallows the click meant for its ✕; Playwright could not deliver it in 8 s. |
| K-export | PASS | Exporting eight stacked copies produced a mix and returned to the case. |
| K-perm | partly | The Viewer persona cannot see the case at all, so the ungated Mixer button was not reachable from that seat; the gap in the plan (button shown to a member without `Cases.Create`) needs the Member persona, James — added to phase 4's checks. |

## Tests and harness

- **NEW.** `AudioScrubModeTests` — the repo's only audio browser test — could not pass on the
  current site even with its password supplied: it waited for the `display:none` upload input to
  be *visible*. Fixed in this phase (wait for the "Upload File" label, then for the input to be
  attached). It had never run under `scripts/run-e2e.sh`, which passed no passwords at all; the
  script now derives them from the dev settings in-process.

## Server (long-file probe)

**Q (NEW, M).** `POST /api/upload-files` refuses a 908 MB multipart upload with a bare framework
400 — "Failed to read the request form. Multipart body length limit" — despite
`[DisableRequestSizeLimit]`, because that attribute lifts Kestrel's request limit and not
`FormOptions.MultipartBodyLengthLimit` (128 MB by default). The site's own Files tab goes through
`ChunkedUploadController`, so people do not meet this there; every direct caller of the classic
endpoint does, including the standalone editor's publish path
(`WasmVideoExportPublisher` → `api/video-projects/{id}/publish`), so a render over 128 MB cannot
be published from the WASM host. Not audio-specific, recorded here because this is where it was
found.

The probe itself (a 90-minute stereo 44.1 kHz WAV, 908 MB, generated locally) went in through the
chunked endpoint instead: 15 chunks of 64 MB, accepted in a second, and the resulting row carries
**no duration** (finding 11 — the metadata is never derived for audio on any path, not only the
derived ones).

**1 (OBSERVED, S).** One EVP scan of that file took the API from **1527 MB to a peak of 5057 MB**
resident, and it was still holding **4733 MB** afterwards — a single request, from one signed-in
member, on a machine with the file already on disk. The scan itself answered 200 in 7 seconds with
candidates, so this is not slow; it is a request that allocates roughly five times the file's
decoded size and does not give it back promptly. Two concurrent scans of an ordinary
investigation recording would exhaust an 8 GB host. This is the single most important server
finding and it is exactly the use case the feature exists for.

**1b (OBSERVED, S).** Normalize on the same file: **HTTP 201 in 8 seconds, peak 8629 MB**, still
holding 5333 MB afterwards. So a successful edit of one ninety-minute recording allocates over
eight gigabytes. The API had already been left at 4.6 GB by the previous scan and simply grew from
there; nothing in the pipeline is bounded, and nothing releases between requests. A second
concurrent request of either kind on a normal host is an out-of-memory kill, which arrives as a
dead API rather than as a refusal.

*(A second scan on the already-swollen process peaked at 6414 MB and stayed there; the figures
are cumulative because nothing is released, not because the second call was cheaper.)*

**R (NEW, M).** `AudioEditRequest.Operation` only binds from a **number**, not the enum's name:
`{"operation":"Normalize"}` is refused with `$.operation: The JSON value could not be converted`.
Every other enum the API takes over JSON accepts its name. Anything not generated from the C#
client — a script, a future WASM host, anyone reading the record definition — sends the name and
gets a 400 that reads like a missing field ("The request field is required").

## What this changes in the plan

1. **New phase 1a, before everything: the editor gets its size back.** P is a one-word change with
   a large effect, and it blocks honest verification of C, D, E and G — those were all being
   judged through a 300 px keyhole. Fix it first, re-run the walk, then re-judge.
2. **Server safety moves up and grows.** The measured numbers (8.6 GB for one Normalize, 5.1 GB
   for one scan, nothing released) make findings 1, 2 and 3 an availability problem rather than a
   theoretical one, and add two of their own: the missing duration on every audio upload (11
   applies to originals too, not just derived files) and the multipart ceiling (Q) that stops the
   classic upload endpoint above 128 MB.
3. **Silence needs its own investigation.** The one edit that produced nothing and said nothing is
   not explained by any finding in the plan; phase 2 starts by reproducing it with the region a
   person actually drew.
4. **The mixer's permission gap needs the Member seat.** The Viewer cannot see the case at all, so
   the ungated button has to be checked as James.
5. **Two plan items are already disproved as written:** the Keep dialog's validation is correct
   (the walk misread it), and the Region Explorer's X does close it — what it does not do is stay
   closed, which is the same defect as A rather than a separate one.

## How to re-run this

```
BEN_E2E_DB=IsHauntedDb_audio_walk scripts/run-e2e.sh --keep --filter TestCategory=AudioEditorWalk
```

The harness now derives the seeded passwords itself. Screenshots and `verdicts.md` land in
`ProjectNotes/AudioEditor-Walk-2026-09-06/`. Tear down afterwards: stop the three hosts, drop the
database, delete `.uploads-IsHauntedDb_audio_walk`.
