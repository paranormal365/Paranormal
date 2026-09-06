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

## What the full size then showed

With the editor at its proper size (phase 1a), two more defects were visible that the keyhole had
been hiding.

**S (NEW, S) — FIXED.** **The spectrogram never draws.** "Show Spectrogram" toggles to "Hide Spectrogram",
the extra controls appear, the waveform is pushed down to make room — and the space above it stays
empty, at every FFT size and after every colormap change. This is not a colormap problem and not a
size problem; the canvas has no pixels in it. It is the feature the EVP workflow leans on hardest
and the plan had it as a working feature with two small defects. Console evidence is captured in
the dark-mode run.
*Cause:* the draw worker shipped with a second, older copy of its whole implementation appended
after the first, carrying that copy's header comment without its opening `/**`. The file threw
`SyntaxError: Unexpected token '*'` on load, so the worker never ran and the canvas stayed at the
browser's default 300×150 with nothing in it — since the asset move on 2026-08-23. Removing the
stale copy also saved the newer implementation from being overwritten by it, which would have lost
colormaps and the mel scale even had the file parsed. Guarded by `ShippedScriptsParseTests`.
*Screenshots:* `03-spectrogram-jet.png` (before), `03-spectrogram-viridis.png` (after).

**T (NEW, M).** **The colormap picker cannot read its own options.** They were declared as
`List<(string Value, string Label)>`, and a value tuple's element names exist only at compile time:
at runtime it has `Item1`/`Item2` and no properties, so `SelectValue.GetMember(item, "Label")`
finds nothing and falls back to the item itself. Every option rendered as `(viridis, Viridis)`,
and because the value never matched the bound string either, the picker showed **blank** until
something was chosen. The resolution picker beside it always worked because it uses a record.
**Fixed in phase 1a**, along with the identical bug in the image editor's filter presets, plus a
guard test that fails on any picker fed a list of tuples.

## Full view

| id | verdict | what happened |
|---|---|---|
| A | **OBSERVED → FIXED** | The X closed the modal; the next parent render (clicking the compact player) brought it straight back. After phase 1a the re-walk records it staying closed and reopening cleanly. |
| B | **OBSERVED → FIXED** | With a region drawn at 1:14.6–1:33.2, turning on Silence moved the edit target to 3:00.6–3:06.5 — a machine-found stretch — and the drawn region was gone (`02-silence-on.png` shows no region at all). Fixed in phase 2: the region's kind travels with it, and a drawn region at 0:37.2–1:14.5 now survives detection unchanged. |
| C | **OBSERVED → FIXED** | With S and T fixed, measured: choosing Viridis repainted the canvas (`71,13,91` against jet's `6,7,145`), and a resolution change reverted it to `6,14,164`. The settings object was rebuilt from scratch on every recompute, so anything the caller did not pass was invented — jet, and mel off. It is merged now, and the editor passes its own colour and mel state on every call. Re-measured: colours survive (`70,21,95`), and the mel scale survives exactly (centroid 0.529 before and after). |
| D | **CODE** | Four enable checkboxes toggle a bound bool and nothing else; not observable from outside. Stands as in the plan. |
| E | **WITHDRAWN** | The walk clicked the toolbar's silence-DETECTION toggle, not the edit panel's Silence: both are named "Silence" and a name lookup resolves in DOM order. Detection produces no clip and no error, correctly. Silencing a region a person drew produces a saved clip — verified in phase 2, which also gave the eight edit buttons ids so this cannot recur. |
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

### Both fixed, and re-measured

Same 90-minute recording, same machine, through the same probe.

| Request | Before | After |
|---|---|---|
| Normalize | HTTP 201 in 8 s, peak **8629 MB** | HTTP 400 in 0 s, peak **724 MB** — refused with a sentence |
| EVP scan | HTTP 200 in 7 s, peak **5057 MB** (+3530 over baseline) | HTTP 200 in 6 s, peak **1748 MB** (+1023 over baseline) |

Three things were wrong, and all three are in `AudioSourceReader` now:

1. **Every reader grew a `List<float>` and then called `ToArray` on it** — the list's doubling
   buffer and a full copy alive at once, three times the recording before the operation began.
   `AudioEditor`, `AudioMixer`, `EvpDetector` and the clip normaliser each had their own copy of
   that loop. They all read through one place now, which allocates one buffer from the length the
   header already states; for WAV that is exact, so nothing is copied at all.
2. **The detector decoded at full rate and in stereo**, then averaged — five and a half times the
   memory for information it discards, since it band-passes 300–3400 Hz. It reads mono at 16 kHz
   now, mixed down and resampled as the stream is read, so the recording is never held at its own
   rate. 90 minutes costs 329 MB instead of 1.9 GB, and the scan keeps no length limit: long
   recordings are its whole purpose.
3. **Edits have a stated ceiling** of 30 minutes, checked from the header before a byte is
   decoded, refused as a 400 through the `NotSupportedException` path the endpoints already
   answer. The message says what to do instead.

*One mistake of my own, worth recording:* the first version of the new reader asked the provider
for the whole remaining array in one call. Every stage in front of it sizes its scratch buffer
from what it is asked for, so the mixdown allocated an interleaved copy of the entire recording
and the resampler did the same — 5.2 GB of allocation to return 329 MB. Reading in quarter-million
sample chunks brought that to 674 MB. It is invisible from the outside, so a test measures the
marginal allocation of a longer recording and fails against the un-chunked version.

**R (NEW, M).** `AudioEditRequest.Operation` only binds from a **number**, not the enum's name:
`{"operation":"Normalize"}` is refused with `$.operation: The JSON value could not be converted`.
Every other enum the API takes over JSON accepts its name. Anything not generated from the C#
client — a script, a future WASM host, anyone reading the record definition — sends the name and
gets a 400 that reads like a missing field ("The request field is required").

## Phase 1 — what shipped, and two claims it disproved

Findings 2–17, F and R are fixed (branch `feature/audio-editor-phase-1-server-safety`). The two
security ones first:

- **6, privacy laundering.** A viewer of a private recording could publish it by deriving a copy;
  the derived file's `IsPublic` came straight from the request. The source's visibility is a ceiling
  now on both the edit and the clip endpoint, and the refusal names the recording as private and
  says to publish the original instead.
- **9, config auth.** PUT and DELETE asked only whether the caller could *view* the file, so anyone
  it had been shared with could overwrite the owner's saved player settings; GET had no per-file
  check at all. Verified live against the running API: another signed-in member gets 403 on read,
  on write and on delete, and the owner gets 204/200.

The rest of the bounds now live in one `AudioRequestLimits` both the edit endpoint and the mixer
share, so they cannot drift. Probed live on the isolated stack, each answering in a sentence:

| asked for | answered |
|---|---|
| `{"operation":"Normalize"}` — the name, not the number | **201** (was a 400 that read as a missing field) |
| publish an edit of a private recording | 400 — "That recording is private, so an edit of it cannot be made public here…" |
| `speedRatio: 0.001` | 400 — "SpeedRatio must be between 0.25 and 4." |
| `gainDb: 500` | 400 — "GainDb must be between -60 and 24 dB." |
| cut a region starting at 60s of a 3s recording | 400 — "That region starts at 60s, and the recording is only 3s long." |
| a 600-character name | 400 — "A name may be at most 200 characters; this one is 600." |
| cut 1s–2s, which is legitimate | 201, and the derived row records **region 1–2** (finding F) |

And the derived files now say how long they are (finding 11): the 3-second source produced a
3-second Normalize and a 2-second Cut, each with its sample rate and channel count, measured off the
bytes that were produced rather than inherited.

**A refusal nobody can read is worse than no refusal.** The editor answered *every* failed edit with
one hardcoded line — "only WAV and MP3 sources can be edited" — because the client returns null on
failure and drops the body. So every refusal above would have reached the screen as a message about
file formats. The client now carries the server's sentence, and two browser tests
(`AudioEditorRefusalTests`) hold it there; run against the un-fixed component on the isolated stack,
both showed the old catch-all for a fade problem and for the privacy refusal.

### Two things this audit got wrong

- **R's premise.** The note said "every other enum the API takes over JSON accepts its name." It does
  not. This API configures no `JsonStringEnumConverter` anywhere, deliberately — several enums carry
  comments warning that they cross as integers and must not be renumbered. `AudioEditOperation` was
  not the odd one out; it was the only enum a person writes by hand. The fix stands, and is narrower
  than the finding implied: a converter on that one enum, accepting names *and* integers, so no
  existing caller changes.
- **11 was too broad.** "No audio upload gets a duration on any path" was drawn from the 908 MB
  probe file. A short WAV through the same chunked endpoint comes back with 3 s, 8000 Hz, 1 channel.
  What was true is the narrower claim: *derived* audio had no duration, because the metadata row is
  only created when the source has one to inherit. Whether long or unusual uploads lose theirs is a
  separate question this has not answered.

### Still open from this phase's neighbourhood

Finding Q (the 128 MB multipart ceiling on the classic upload endpoint) is untouched — it is not
audio, and it also stops the WASM editor publishing a render over 128 MB, so it wants its own fix.

## Phase 2 — the full-view editor

Findings B, D, G, J, M, N and O are fixed, and E turned out not to be a finding at all.

**B, verified on screen.** A region drawn at 0:37.2–1:14.5 now reads 0:37.2–1:14.5 after silence
detection, where before it was replaced by a machine stretch. The cause was that "not user-drawn"
was tracked as a set of ids the component had added itself, which covered the overlays it drew and
missed every region JavaScript drew — silence detection adds its own inside `detectSilence`, so
each arrived looking like a drag, each cleared the others, and the last became the edit target. The
kind travels with the region now, and `clearUserRegions` clears by kind rather than by "everything
except this list". The decision moved into `RegionSelection`, a plain class with tests; the browser
test was run against take-everything behaviour and reported the selection gone.

**E is withdrawn.** The walk clicked the wrong button. Two buttons are named "Silence" — the
toolbar's silence-DETECTION toggle and the edit panel's Silence operation — and the walk located
them by accessible name, which resolves in DOM order, so it toggled detection and recorded
"produced nothing within 60 s and showed no error" against the edit. Detection produces no clip and
no error, correctly. The eight edit buttons now carry ids (`#edit-op-cut` and so on), the walk uses
them, and a browser test confirms that silencing a region a person drew produces a saved clip.

**A mistake of my own, and the most instructive thing in this phase.** The kind was added to
`WsRegionData` as a string plus a computed `Kind` property. Under the web naming policy that
computed property claims the JSON name `kind` — the same name the string is annotated with — and a
collision makes `System.Text.Json` throw for the *whole type* on every deserialization. The player
wraps its interop calls in a `safe()` helper that swallows rejections, so there was no error in the
browser console, none on the server, and no failing unit test: `region-created` simply stopped
reaching C#, and dragging on the waveform drew a region and selected nothing. Fifteen new unit
tests for the new behaviour all passed while the feature was entirely disconnected. It was found by
opening the page. `WsRegionDataBindingTests` binds the exact payload the module sends so the next
shadowed property fails there instead.

**The rest.** The four listening-chain checkboxes apply when ticked instead of waiting for a slider
(D). Gain, Fade, Speed and Pitch stop sending region bounds they ignore, and the region readout is
now grouped with the two operations that use it (G). A confirmed marker's play button plays, with
two seconds of context either side for a point marker that has no span of its own (J). A failed
file-type, marker or saved-clip load says so rather than rendering as an empty recording (E's other
half, and O). The two Save-as-clip buttons now share one Normalize default, and the region explorer
offers the choice it used to decide silently (N). A region's note appears on the clip saved from it,
through a matching rule both places share (M).

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
