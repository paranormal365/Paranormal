# Video Editor — full walk, gap audit and remediation plan

## Context

Ben asked (2026-09-05) for a top-to-bottom pass over the video editor: use it as a person would,
walk every use case, find gaps, defects and misplaced functionality, bring the tests in line with
the current code, and produce a plan for getting it all working "as it should". The reference is
TechSmith Camtasia. The intended architecture is explicit: everything is designed, assembled,
compiled and finished on the user's own computer; previously uploaded files can be pulled down to
use; only the finished product is uploaded. Real, non-blank test media from ~/Documents and
~/Downloads was used.

Two hosts exist and they differ:

- **Client-side host** `Ben.Wasm.Video` → deployed at `https://ishaunted.com/editors/video/`.
  Media and rendering stay in the browser. This is the host that matches the stated intent. It was
  walked signed out (its `/login` page is reachable only by typing the URL).
- **Site pages** `/my-videos`, `/video-editor`, case Video tab — the same `<VideoEditor>` under
  Blazor Server. Media bytes round-trip the server heap over SignalR. Signed-out behaviour was
  checked on screen; the signed-in walk needs a password and was covered by code reading instead
  (the repo rule forbids typing seeded passwords into tool calls).

## How the evidence was gathered

1. **Screen walk** of the client-side host at 1440×900 on an isolated three-host stack
   (api :5252, web :5078, wasm :5180, throwaway DB `IsHauntedDb_walk3`): initialise, import a
   4.8 s .mov, a 186 s .mp3 and a 1007×675 .png, play, scrub, select, split, marker, callout,
   delete, undo, ripple, rename a track, Mark In, Export Now, full-quality Preview, File → Save and
   Open, the Server tab, the sidecar panel, refresh. Findings F1–F19 below.
2. **Three read-only code explorations** (option gating, export/persistence/sidecar paths, test
   coverage) and **two design passes** (layout/timeline/playback; host parity/persistence/tests).
3. **Adversarial verification workflow** (51 agents): two skeptics per cluster of walk findings,
   one code-reading finder per editor area the screen could not reach, two skeptics per area, and
   a completeness critic. All 51 finished (18 needed a re-run after a session limit). Every
   sweep area was independently verified by two skeptics; two findings were dropped as documented
   design and a handful of severities were adjusted. The critic's list of use cases nobody had
   accounted for is in its own section below.

The good news first, so the plan is proportionate: the engine initialises, import of video and
image works, split, marker, callout, undo/redo, in/out working window, track rename, local project
save and the Open Project list all work, and a full export ran to completion in the browser.
Rendering is genuinely local-first: there is no server-side render path anywhere. The service
layer is well tested (~1,600 facts in `Ben.Video.Tests`); the gaps are in what the components and
the pipeline do with it.

## Findings from the walk (verified, corrected)

Severity: **S** = the core promise (assemble locally, render what you see, deliver it) is broken
or work is lost, **M** = a person hits it in a normal session, **L** = polish/docs. Corrections
from the skeptics are folded in.

### Layout and placement

**F1 (M) Primary actions vanish into "…".** Initialize, Open, Preview, Export, Undo and Redo are
the only collapsible items (`Toolbar.razor:18-79`, Telerik default `Overflow=Auto`); everything
after the spacer is `Overflow="Never"` (`:148-201`) and template items never collapse. What eats
the width is the 120 px progress bar plus the longer "Processing… 40 %" label, and on the site host
the feature badges and chips. The comment at `:57-61` and `Toolbar.razor.css:229-230` are stale.
(The walk's "File collapsed" observation is not reproducible from code — re-check on screen.)

**F2 (S) The client-side host ships with most of the editor switched off.**
`Ben.Wasm.Video/Program.cs:31-47` sets only MediaLibrary, AssetCatalogUrl, DocumentPostUrl and
NativeSidecar, and returns early at `:35` when `WebApiBaseUrl` is empty. `Ben.Web.Website/Program.cs:37-63`
sets eleven flags. Seen at :5180: no "+ Video"/"+ Audio", no "+ Text", no transitions, no Visual
Effects, no restore on reload, no error-log export. An **mp3 import is decoded and then orphaned
with status "Done"** (`ClipBrowser.razor:1943-1950`) and the phantom clip is even selected into
Properties (`VideoEditor.razor:1385-1389`). A project saved with transitions or titles on the site
opens here with them hidden and dropped from export (`VideoTimeline.razor:370,584`;
`ExportService.cs:205,232`). No test asserts host parity.

**F3 (M) The floating Media & Properties window hides the timeline's own buttons.** Floated at the
right edge at 320×420 (`VideoEditor.razor:473-476`), it covers Ripple, Callout and Marker
(`VideoTimeline.razor.css:69-73` right-aligns them) at any viewport width. Position is never
persisted; the first-render self-heal (`:645-649`) resets it on every load. The Properties tab
scrolling internally and the re-open on select are by design; the gaps are the overlap, the
un-persisted position and discoverability of the scroll.

**F4 (S) The preview is a 38 px strip; the timeline is 700 px of empty surface.**
`.bv-timeline { height:100% }` (`VideoTimeline.razor.css:3-6`) as a bare flex child of `.bv-editor`
gives the footer a basis of the whole editor, so the 180 px preview row shrinks to ~140 px and the
`<video>` inside `.bv-preview__screen` is 1280×38. `.bv-timeline-row` and `--bv-timeline-h`
(`VideoEditor.razor.css:169-175`, `LayoutService.TimelineHeight`) are dead. The three toolbar
panel toggles are wired to parameters `VideoEditor.razor` never passes (`Toolbar.razor:332-358`).
A person can drag the divider up to 400 px, so it does not block, but it is the first thing anyone
sees and the callout position overlay lands on the strip.

### Timeline semantics and playback

**F5 (S) Same-track overlap is possible and invisible.** Three ways in: a drag commit
(`VideoTimeline.razor:2030-2034` → `ClipStore.CommitDraggedPosition:1309` writes the position
verbatim); a **second local video import, which lands at position 0** (`ClipBrowser.razor:1855-1862`
→ `AddClipToTrack` sets only `Order`); and a drag that never re-sorts `Order`, so export and the
chip loop sequence by `Order` while positions say otherwise. The insert/overwrite prompt fires only
through `AddClipToTimeline` (server placement and the bin's Add-to-timeline, `ClipBrowser.razor:1499, 1965-1969`).
The lane draws items end-to-end (`VideoTimeline.razor:395` `gapPx = Max(0, …)`), so the overlap is
hidden; `TimelineTrack.TotalDuration` (`Models/TimelineTrack.cs:69`) said 6.9 s while the drawing
suggested 9.8 s and the export dialog counted "2 clip(s)".

**F6 (M, reframed) One playhead, two coordinate systems.** Live timeline playback does exist: the
Working Window is an auto-assembled proxy re-encoded after every edit. What the walk hit is
(a) selecting a chip switches the Working Window to a raw-source "Clip Preview" with the clip's own
duration and resets the playhead to 0 (`VideoEditor.razor:1261-1279` → `LoadUrlAsync(…, PlaybackMode.Clip)`),
with no way back except another edit; (b) `Playback.State.CurrentTime` is then clip-relative while
the ruler, markers, "+ Text" and split read it as timeline time, so **split at playhead, markers and
ruler seeks land in the wrong place** (`VideoEditor.razor:978-984` passes it straight to the
clip-relative `ClipStore.SplitClip:1386`); (c) the auto-assembly is silently skipped whenever
ffmpeg is not Ready (`:1489, 2224-2231`), so after the F7 crash the window showed stale content;
(d) image-only playback never updates the counter, overlays or scrubber (`PlayImageLoopAsync:641-646`);
(e) on the client-side host every refresh is a synchronous encode because BackgroundRendering is
off (F2). Camtasia's compositor is still a design gap, but a deliberate one.

**F7 (S) Split → engine crash → Preview hang with no cancel.** The crash is not the split: it is
the debounced Working Window re-encode after it (`OnClipsChanged:2175` → `RefreshWorkingWindowAsync:1487-1687`
re-encodes both halves at full source resolution, `:1544-1555`) while thumbnail extraction may
still be decoding the original (`ffmpegInterop.js:366-375` decodes the whole clip with output-side
seeking, ignores the exit code). `RecordFailure` (`FfmpegService.cs:830`) leaves the instance dead
until manual Initialize. The preview window (`RenderFullQualityPreviewAsync:774-841`,
`OnCloseFullQualityPreview:864-874`) never keeps its job, so closing it cannot call the existing
`ExportJob.Cancel()`; the 90 s exec timeout and 45 s `WorkerWatchdog` exist but their only UI is
the admin-only diagnostics chip. Whether the stall was "slow" or "wedged" is not recorded; the
console `[ffmpeg-cmd]` lines would tell.

**F8 (M, reframed) Imports auto-place by three different rules.** Local video at 0 regardless of
what is there (no prompt); server video at the playhead with the prompt; images and audio at the
untrimmed end of the track. A partial bin exists (card "Add to timeline"), but the Video tab is a
view of the timeline (`VideoEditor.razor:1187-1192` says so), so declining the prompt leaves the
clip nowhere. The `ImageClips=false` clause is dead configuration.

**F9 (M) Refresh silently opens an empty editor.** The saved project survives and File → Open… is
ungated, but the WASM host writes the last-active pointer and never reads it back
(`VideoEditor.razor:630` gate; the gate's own comment `:627-629` describes the defect it reintroduces).
No autosave, no beforeunload; restore polls 30 s for a Ready engine the WASM host never
auto-initialises (`ProjectStore.cs:350-365`).

**F10 (L) Escape clears the selection instead of closing the popup.** `keyboardInterop.js:33-36`
forwards every keydown unless focus is in an input; `OnEditorKeyDown:1012-1019` clears the
selection (and misses image/callout/clip-art selections). The "popups stay open" half needs an
on-screen re-check; the deselect is fully explained.

### Server, sign-in and delivery

**F11 (M) Signed-out Server tab shows a raw error, and the standalone host has no sign-in door.**
`ClipBrowser.razor:1166` shows `ex.Message` raw (`HttpMediaLibraryProvider.cs:63` throws on 401).
Nothing links to `/login` (`MainLayout.razor` has only a theme toggle); there is no sign-out at all
(`AuthService.LogoutAsync` has no caller); two-factor accounts cannot sign in (`AuthService.cs:31-45`
posts email+password and maps every 401 to "check the address and password"); and the login page's
"Back to the editor" navigates to `/`, which under the production sub-path is the site root
(`Login.razor:36,53`). The site's `/video-editor` signed-out page has a help link but no sign-in link.

**F12 (S) The client-side host cannot upload a finished render.** `Ben.Wasm.Video/Pages/Editor.razor:20`
passes no `OnPublishExport`, so `DeferDelivery` (`VideoEditor.razor:335`) is false and every export
goes straight to the Downloads folder via an anchor click (`ffmpegInterop.js:501`). `Login.razor:14-17`
promises that signing in lets you "publish finished renders". Queued exports always downloading is
documented design (`Future-Improvements.md` #76) — kept, but the help must say so.

**F13 (M) "Save to Server" is a 401 on the site host.** `Ben.Web.Website/Program.cs:62` sets
`DocumentPostUrl` but never attaches auth to the `BenVideo.ProjectPersistence` client (a
`DelegatingHandler` cannot reach the circuit-scoped token store, which is why
`BenMediaLibraryProvider.cs:105-117` sets the header by hand). The toolbar cloud button and the
post-export prompt both fail there; on the WASM host the post-export prompt works (gated only on
`DocumentPostUrl`, `VideoEditor.razor:356`). Inferred from code, not observed. Every save POSTs a
new row (`ProjectService.cs:100-117`) and discards the returned id; `LoadFromServerHandlerAsync:2086`
is dead; `DocumentSaveUrl` is never configured.

**F14 (M) Projects are not portable.** Clips persist only `OriginalFileName` and `OpfsExt`
(`ProjectService.cs:322-323, 342-343, 407-408`); restore reads this browser's OPFS by clip Guid;
server placement re-keys under a fresh Guid (`ClipBrowser.razor:1446-1459`). A manual
"Replace Media…" exists on a missing chip (`VideoTimeline.razor:899-902` → `ClipStore.RelinkClip`)
but writes MEMFS only, so the clip is missing again on the next open. Help promises "pick it up on
another machine".

**F15 (M) The standalone host can push a project to the server but never bring one back**, and the
site keeps a browser-local list beside its server list. A server project opened from the site then
saved with File → Save lands in localStorage under the same Guid; the reverse never carries an id,
so the server list fills with duplicates once F13 is fixed.

**F16 (L) The watermark feature can never turn on.** `VideoAssetCatalogService.cs:225` requests
`/api/video-assets/watermark-config` once per export/preview and on Sync; no controller serves it;
the catch swallows the 404, so `WatermarkService` always reports disabled.

### Sidecar and docs

**F17 (M) "Download and run it" with nothing to click.** `NativeSidecarPanel.razor:22` has no link
("pair.sh reopens it" is macOS wording shown to Windows users). The downloads page exists only
under the WASM app. `deploy-ishaunted.ps1:677-707` silently stages the `.zip` when the `.exe` is
absent (as now) and removes any previously deployed `.exe`, while the page hard-links the `.exe`;
`docs/deploy-editor.md:82` says `.zip`. The panel correctly found Ben's installed sidecar.

**F18 (L) Help over-promises.** "Your machine's video hardware" (no hwaccel anywhere; the panel's
own "all of your CPU's cores" is right); Preview "in a separate window" (an in-page window); the
Export dialog note "entirely in your browser" (untrue with a sidecar); the downloads page repeats
the over-promise (`downloads/index.html:78-82`). Transitions, text, callouts, clip-art, watermark
and chapters always run in ffmpeg.wasm even with a sidecar — deliberate, but undocumented.

### Tests

**F19 (M) The Playwright layer cannot fail and nothing drives the components.** No bUnit is a
documented choice (logic lives in plain classes, and that layer is heavily tested). But
`VideoEditorTests.cs:111-174` matches `[class*='toolbar']` and its ffmpeg test `Assert.Pass()`es
unconditionally (no element has a class containing "ffmpeg"); `AudioScrubModeTests` tests the
site's audio player and soft-passes twice; `WasmEditorTests` fails raw when :5180 is down and
exercises only the Server tab and one clip. Nothing drives `#bv-file-input`, Play, split, export,
preview popout, the sidecar panel, or any dialog. F4, F5 and F6 are exactly what an e2e that
imported a file and pressed Play would have caught. Seed media in `Ben.Data.WebApi/SeedData/Media/`
(four real files) and `Ben.Web.Playwright/Fixtures/test-audio.mp3` are usable.

## Findings from the code sweeps

Twelve areas the screen walk could not reach were read end to end and every finding was then
judged by two independent skeptics (a correctness lens and a product lens) against the source;
all twelve areas are verified. Only S and the more important M items are listed; the full lists
(243 findings with fix sketches and verdicts) are in the workflow journal referenced in the notes
at the end.

### Export pipeline (verified) — the render does not match the timeline

| id | sev | finding | where |
|---|---|---|---|
| export-1 | S | Secondary video tracks are never exported; a clip on track 2 reaches the output only as the "to" side of a cross-track transition. MultiTrack exists in the UI only. | `ExportService.cs:149-151` |
| export-2 | S | Gaps between clips are collapsed on export while audio, overlays and chapters keep timeline time. | `ExportService.cs:189-197, 671-689` |
| export-3 / audio-2 | S | Mixed audio/no-audio segments break concat: an image or muted clip first drops audio or fails (image segments are always `-an`). | `ExportArgBuilders.cs:141, 220-238`; `ffmpegInterop.js:431` |
| export-5 | M | "Source resolution" silently exports 1920×1080. | `ExportService.cs:1612-1623` |
| export-6 | M | A watermarked export downloads as `wm_<guid>.mp4`. | `ExportService.cs:274-281, 1657` |
| export-8 | M | Progress runs backwards several times per job. | `ExportService.cs:1499-1504` |
| export-9 | M | The queue holds one job from the UI and renders the live timeline, not a snapshot. | `ExportDialog.razor:334`; `ExportQueueService.cs:101` |
| export-13 | M | Fps default disagrees with itself (24 vs "30 (default)"); presets never set fps. | `ExportSettings.cs:68-73`; `ExportDialog.razor:314` |
| export-14 | M | Dialog offers Opus in mp4/mov (ffmpeg refuses without `-strict`) and no `hvc1` tag for H.265. | `ExportDialog.razor:328-332` |
| export-17 | L | Closing the destination prompt deletes the finished render without confirmation. | `ExportDestinationPrompt.razor:13, 169-173` |
| export-20 | M | An image-only timeline can open Export but the dialog's own predicate refuses. | `VideoEditor.razor:37-41` vs `ExportDialog.razor:334` |
| export-19 | M | No test drives `ExportService`, the queue's processing, or an export in a browser. | `ExportMemoryFlatteningTests.cs:6-13` |

### Transitions (verified)

| id | sev | finding | where |
|---|---|---|---|
| transitions-1 | S | Any same-track transition exports a **silent** video; with an audio track the export errors (xfade pass maps `[vout]` only). | `ExportService.cs:708-712`; `ExportArgBuilders.cs` |
| transitions-2 | S | Transitions are matched to junctions by index, and once one exists every other junction gets an unrequested 1 s fade. | `ExportArgBuilders.cs:657-661`; `ExportService.cs:700` |
| transitions-3 | S | The timeline says one duration, the render is shorter by every transition; everything after it (overlays, audio, markers) drifts. Camtasia overlaps the clips; here nothing moves. | `ClipStore.cs:1600-1601`; `TimelineTrack.cs` |
| transitions-5 | S | Transitions are never reconciled when their clips are trimmed, split, moved, ripple-deleted or removed. | `ClipStore.cs:742-756, 514-538, 1598-1599` |
| transitions-9 | M | Cross-track transition export lengthens the timeline and corrupts later offsets; invisible in preview. Recommend retiring it for clip fade-in/out. | `ExportService.cs:514-531` |
| transitions-6/7/10 | M | Undo after an edge-drag resize does nothing; no duration clamp; Delete ignores a selected transition. | `VideoTimeline.razor:1865-1866`; `ClipStore.cs:1580-1602`; `VideoEditor.razor:917-950` |
| transitions-15 | M | The `Transitions` flag gates export, not only creation, so restored projects lose them on a host with the flag off. | `ExportService.cs:205` |
| transitions-11 | M | Help never mentions transitions. | `using-the-video-editor.md` |

### Titles (verified)

| id | sev | finding | where |
|---|---|---|---|
| titles-1 / callouts-11 | M | Google Fonts render in the live preview but fall back to a system font in export (SVG rasterised as an isolated image document). | `svgFrameRenderer.js:173-227`; `CalloutShapeRenderer.cs:152-155` |
| titles-2 | M | First drag of a title jumps, usually off the bottom of the frame (handle anchor ≠ override anchor). | `TextPositionOverlay.razor:41-42`; `MotionEffectiveGeometry.cs:46-59` |
| titles-4 | M | No undo for any title edit; callout edits are undoable. | `ClipStore.UpdateTextOverlay:1787-1821` |
| titles-5 | M | Text edits are edit-then-Apply, unlike every other panel; pending edits are lost on reselect. | `TextOverlayEditor.razor:199-203, 416-447` |
| titles-6 | M | Titles never word-wrap. | `TextOverlayRenderer.cs:61, 76-77` |
| titles-7 | M | Delete/Backspace does nothing on a selected title. | `VideoEditor.razor:917-950` |
| titles-9 | M | Preview stacks text under callouts/clip art; export orders differently. | `ExportService.cs:231-238` |
| titles-10 | M | Culture-sensitive `F3` formatting in the animated-title filter breaks export on non-English browsers (`Ben.Wasm.Video.csproj` has no `InvariantGlobalization`). | `ExportService.cs:815, 946` |
| titles-11 | M | Titles restored from a project are previewed but dropped from export when the flag is off (same class as transitions-15). | `ExportService.cs:232` |
| titles-12/13 | L | `.ass` export is dead code; no opacity/italic/outline controls; no captions flag. | `ExportService.cs:1304-1310` |

### Motion keyframes (verified)

| id | sev | finding | where |
|---|---|---|---|
| motion-1 / persistence-2 | S | Per-axis scale and rotation keyframes are silently dropped on save/load. | `ProjectFile.cs:225-248`; `ProjectService.cs:214-235, 496` |
| motion-3 | S | Keyframes are stored in absolute project time; moving, trimming or rippling a layer leaves its animation behind. `ClipStore` never references `MotionKeyframeService`. | `MotionKeyframe.cs:20`; `ClipStore.cs:1286-1382` |
| motion-8 | S/M | Zoom In/Out, Ken Burns and Pulse effects build `on/fps` zoompan expressions with an undefined `fps` variable. | `Ben.Video.Core/Plugins/Video/ZoomInEffect.cs:39-43` and siblings |
| motion-6 | M | Most keyframe edits (panel sliders, Delete, + Keyframe, path/bezier drags, easing) are not undoable. | `MotionKeyframeEditor.razor:367-486`; `MotionPathOverlay.razor` |
| motion-9 | M | No keyframe animation for video or image clips — Camtasia's zoom-n-pan — although the model lists `ImageClip` as a layer type. | `MotionKeyframe.cs:102,113`; `ExportArgBuilders.cs:1054-1125` |
| motion-4/5/7/10 | M | Seeding a keyframe drops ScaleX/Y/Rotation; double-click add bypasses the seed; keyframes can be created outside the layer's span; overlay drags never capture the pointer. | `MotionKeyframeService.cs:167-181`; `VideoEditor.razor:2363-2398` |
| motion-11 | M | Overlays on any video track other than the first are exported but never previewed or selectable. | `CanvasSelectionOverlay.razor:179`; `LiveOverlayPreview.razor:84` |
| motion-15 | M | Help does not document keyframes, Animate, the motion path or canvas snapping. | help article |

### Callouts and clip art (verified; one row per overlay lane is documented design — row order encodes z-order — and was dropped)

| id | sev | finding | where |
|---|---|---|---|
| callouts-1 | S/M | Saving drops callout text-layout fields (align, wrap, shadow, padding) and clip-art control-point definitions. | `ProjectFile.cs`; `ProjectService.MapCallout ~415-445` |
| callouts-2 | S | Exported Rectangle/Ellipse callouts go through ffmpeg `drawbox` and do not look like the SVG preview. | `ExportService.cs:982-986, 857-877` |
| callouts-3 | S | Arrow/Line control points are absolute canvas coordinates, so moving, resizing or animating the callout leaves the arrow behind. | `CalloutShapeRenderer.cs:183-188, 239-244` |
| callouts-4 | M | The Assets tab's search box and type filter never filter. | `AssetBrowser.razor:12-20, 185` |
| callouts-5 | M | Opening the Assets tab signed out on the WASM host throws (account-library 401 unhandled). | `VideoAssetCatalogService.cs:50-56` |
| callouts-7 | M | Assets are placed after the end of the timeline, not at the playhead. | `AssetBrowser.razor:241, 264` |
| callouts-8/17 | M | Clip-art edits are not undoable; control-point sliders push one undo entry per tick. | `ClipArtEditor.razor:237-276`; `CalloutEditor.razor:217-263` |
| callouts-10 | M | Clip-art aspect ratio is never resolved: selection box, preview and export each pick a different height. | `ClipArtClip.cs:50-51`; `AssetBrowser.razor:255-266` |
| callouts-12/13 | M | Changing shape leaves control points undefaulted; no rotation handle and the overlays ignore Rotation. | `CalloutEditor.razor:461`; overlays |
| callouts-16 | M | Shapes and the asset library are split across three places (timeline button always makes a Rectangle; Assets tab off by default). | `VideoTimeline.razor:101-106`; `VideoEditor.razor:126-131, 468` |
| callouts-6/9/23 | M/L | "My Imported Files" lists videos as PNG clip art; Lottie can be published but never renders; watermark/texture assets are offered as clip art. | `AssetProviders.cs`; `AdminVideoAssetController.cs:206`; `VideoAssetController.cs:48-53` |

### Audio (verified; skeptics rate audio-3 and audio-5 as M and audio-9 as M)

| id | sev | finding | where |
|---|---|---|---|
| audio-1 | S | `amix` assumes the assembled video has an audio stream (`[0:a]` unconditional): Separate Audio on the only clip, or an image slideshow with music, fails. | `ExportArgBuilders.cs:66` |
| audio-3 | S | `amix` defaults: adding one music track drops dialogue ~6 dB (`normalize=1`) and the mix swells for 2 s after an input ends. | `ExportArgBuilders.cs:66` |
| audio-4 | S | Volume keyframes and fade-in on a head-trimmed video clip are applied to the discarded head (`-ss` after `-i`, filters see source time). | `ExportArgBuilders.cs:154-159, 489-528` |
| audio-5 / timeline-11 | S | "Mute Track" is cosmetic: muted tracks still play and are mixed into the export; not undoable. | `TimelineTrack.cs:21-22`; `VideoTimeline.razor:1488-1492` |
| audio-6 / preview-15 | M | Audio tracks are never audible in the Working Window or Play; only the slow full-quality Preview mixes them. | `VideoEditor.razor:1520-1590` |
| audio-7 | M | Separate Audio and clip links are not persisted (no MuteAudio/HasAudio/LinkedClipId in the project file). | `ProjectFile.cs:76-135` |
| audio-8 | M | Detached audio ignores the clip's trim and speed. | `FfmpegService.cs:563-570` |
| audio-9 / timeline-1 | S | Split at playhead passes absolute timeline time to the clip-relative `SplitClip` for audio, video and images. | `VideoEditor.razor:978-1005`; `ClipStore.cs:1386, 1425` |
| audio-10/11/16 | M | Splitting erases a two-point ramp; `AudioClip` has no trimmed-duration notion; fades clamp to the source length. | `ClipStore.cs:1537-1555, 1026`; `AudioClip.cs` |
| audio-12 / timeline-15 | M | Audio chips (and clip art) have no drag-to-trim handles though help says every clip trims by its edges. | `VideoTimeline.razor:482-511` |
| audio-13 / media-11 | M | The waveform always shows the whole source, so trimmed and split chips display the wrong audio. | `VideoTimeline.razor:464-469`; `waveformInterop.js:105-112` |
| audio-14/15/17/18/19 | M | Properties sliders snap back on every playback tick; mono balance silences the right channel; envelope handles drift after zoom; the lane misleads at 0–1 keyframes; no mute toggle for a clip's own audio. | `AudioClipEditor.razor:254-266`; `ExportArgBuilders.cs:536-543`; `volumeAutomationLane.js` |
| audio-20 | L | Help says linked audio moves together; the code documents the opposite as a scope cut. | help `:124-125`; `ClipStore.LinkClips` |

### Media panel (verified; media-2 rated M)

| id | sev | finding | where |
|---|---|---|---|
| media-1 | S | Thumbnail extraction decodes the whole clip twice per import with output-side seeking, can outrun its own timeout and ignores the exit code — the likeliest contributor to the F7 out-of-bounds trap. | `ffmpegInterop.js:366-375` |
| media-2 / persistence-12 | S | Nothing ever frees a source: Remove clip, project delete and page life leave every OPFS copy and MEMFS mount; nothing reads the quota. | `ClipBrowser.razor:1971-1979`; `SourceMounter.cs:16-20`; `opfsInterop.js:163-174` |
| media-3 | M | The Video tab is a view of the timeline, not a bin. | `ClipBrowser.razor:510`; `VideoEditor.razor:1187-1189` |
| media-4 | M | Local import and server placement place clips by different rules (see F5/F8). | `ClipBrowser.razor:1862, 1896-1908, 1947` |
| media-5 | M | Server previews and thumbnails download whole files and throw them away; the OPFS cache is not consulted first. | `ClipBrowser.razor:1233-1259` |
| media-6 | M | Server downloads are buffered three times in the .NET heap and fail outright at 2 GB (`(int)Content-Length`). | `HttpMediaLibraryProvider.cs:101-113` |
| media-7 | M | The sidecar probe drops `HasAudio`, so a silent clip is marked as having sound when a sidecar is paired. | `SidecarMediaProbe.cs:73-74`; `MediaProbeInfo.cs:16` |
| media-8 | M | Kind is decided by extension lists that miss .aiff/.caf/.heic/.tiff/.avif; anything unlisted becomes a 0×0 video clip. | `ClipBrowser.razor:1983-1993` |
| media-9 | M | The import window's Cancel button is dead (nothing assigns `Cts`); local imports cannot be cancelled. | `ClipBrowser.razor:195-204, 417` |
| media-10 | M | The Server tab never refreshes after first load; `MediaLibraryPicker` is an unused, diverged second import path. | `ClipBrowser.razor:1068-1076` |
| media-13 / wasm-6 / wasm-7 | M | The ffmpeg core is fetched from cdn.jsdelivr.net at runtime (no local copy, undocumented, retry loop because it fails intermittently), and the multi-thread core can never be selected because production never sends COOP/COEP. | `ffmpegInterop.js:110-111, 160-164, 199-206`; `web.config` |
| media-12 | M | Help says the import summary waits to be dismissed; local imports auto-close after 1.2 s. | help `:79`; `ClipBrowser.razor:1793-1797` |

### Timeline interaction (verified; timeline-1 and timeline-3 rated M, timeline-2 S)

| id | sev | finding | where |
|---|---|---|---|
| timeline-2 | S | Dragging a Media-tab card onto a track adds a second `TrackItem` with the same Guid (duplicate `@key`). | `ClipBrowser.razor:510, 1008-1012`; drop handler in `VideoTimeline.razor` |
| timeline-3 / preview-1 | S | Selecting a chip resets the playhead to 0 and switches the clock to clip-relative time (the F6 mechanism). | `VideoEditor.razor:1261-1279`; `PlaybackService.cs:42-52` |
| timeline-8 | M | **Cmd is never forwarded, so undo/redo are dead on macOS**; handled keys are not `preventDefault`ed. | `keyboardInterop.js:42` (`e.ctrlKey` only) |
| timeline-10 | M | A drag never re-sorts `Order`; export and the chip loop sequence by `Order`. | `ClipStore.cs:1286-1381` |
| timeline-4 | M | Markers live on a separate un-scrolled ruler positioned as a percentage of the visible width; they misalign once the timeline scrolls. | `VideoTimeline.razor:121-166` |
| timeline-5 | M | Ruler scrub has no pointer capture; releasing outside leaves `_rulerScrubbing` true. | `VideoTimeline.razor:1262-1278` |
| timeline-6/7 | M | Inline trim pushes one undo entry per pointermove; image/callout/text trims are never committed to the undo stack. | `VideoTimeline.razor:1872-1896`; `ClipStore.cs:413-429, 866-887` |
| timeline-9 | M | Multi-select is Ctrl-click only: no group drag, Shift-range, marquee, copy/paste/duplicate shortcut, Home/End. | `VideoTimeline.razor:415, 1565-1581` |
| timeline-12 | M | Locked tracks: context-menu actions stay enabled and silently do nothing; Delete clears the selection. | `VideoTimeline.razor:853-926` |
| timeline-13/14 | M | Snapping ignores images, overlays, transitions and the playhead; the timeline never follows the playhead, never auto-scrolls during a drag, zoom is not anchored. | `SnapEngine.cs:37-57`; `VideoTimeline.razor.js:82-91` |
| timeline-16 | L | Help says ripple applies to trims; it applies only to removes and moves. | help `:103-105` |
| timeline-17 | L | The Frames ruler assumes 30 fps while the preview steps at 24. | `TimelineViewState.cs:18, 86` |
| timeline-19 | L | The legacy HTML5 drag-reorder path is still wired and mutates `Order` live without undo. | `VideoTimeline.razor:413-419, 1710-1728` |

### Preview and playback (verified; preview-1 S)

| id | sev | finding | where |
|---|---|---|---|
| preview-2 | M | Every auto-refresh throws the playhead back to 0 and stops playback. | `VideoPreview.LoadUrlAsync` via `RefreshWorkingWindowAsync:1686` |
| preview-3 | M | Gaps are drawn on the timeline but collapsed in the Working Window (as in export). | `VideoEditor.razor:1492-1590` |
| preview-4 | M | Every full-quality Preview leaves a full-size render in OPFS forever. | `VideoEditor.razor:806` |
| preview-5 | M | The Working Window and the full-quality player share one `PlaybackService` and corrupt each other. | `VideoPreview.razor:406, 437-451` |
| preview-6 | M | Arrow keys do not step frames although the buttons and the shortcut help say they do. | `VideoEditor.razor:390`; `OnEditorKeyDown` |
| preview-7 | M | Working fps and canvas size are disconnected from the rendered preview, the ruler and export. | `VideoPreview.razor:336-337`; `VideoEditor.razor:1521` |
| preview-13 | M | Mark In/Out is a source-clip three-point trim that only applies when re-adding an already placed clip. | `VideoEditor.razor:224-245, 1184-1212` |
| preview-18 | M | Working Window duration is wrong for speed-changed clips. | `VideoEditor.razor:1604-1605` vs the total |
| preview-19/20 | L | Removing the last clip leaves a stale playable render; auto-refresh interleaves into a running export. | `VideoEditor.razor:1499, 777` |
| preview-10/11/14 | L | Playhead updates at ~4 Hz (`timeupdate` only); dispose logs a spurious media error; frame counter off by one. | `videoPreviewInterop.js:16-18, 82-89`; `VideoPreview.razor:336` |

### Persistence (verified; persistence-1 rated M, persistence-2 S)

| id | sev | finding | where |
|---|---|---|---|
| persistence-1 | S* | Project JSON has no shared options object: the editor writes PascalCase with string enums; the site pages read case-insensitively (property names survive) but enum encoding and any future naming policy are unguarded. *Partly mitigated; one `ProjectFileJson.Options` is still required.* | `ProjectService.cs:35-41`; `MyVideosPage.razor:220` |
| persistence-2 | S | Save drops user-set fields: per-axis keyframes, callout text layout, `MuteAudio`/`HasAudio`/`LinkedClipId`. The round-trip test is a hand copy of the mapper, so it cannot notice. | `ProjectFile.cs`; `ProjectServiceTests.cs:36-152` |
| persistence-4 | M | Save / Save to Device / Export JSON / Save to server are disabled unless a video or image clip exists. | `VideoEditor.razor:41`; `Toolbar.razor:93-135` |
| persistence-5 | M | Renaming a dirty project bakes " *" into the saved name. | `VideoEditor.razor:52`; `Toolbar.razor:124-128` |
| persistence-6 | M | Open…/Import JSON replace unsaved work without confirmation. | `VideoEditor.razor:2037-2062` |
| persistence-7 | M | Import JSON and the legacy Open never remount OPFS media, so every clip is "missing" after a same-browser round trip. | `VideoEditor.razor:2061, 2009` |
| persistence-8 | M | A non-project file imports silently and can leave the store with no video track. | `ProjectService.LoadAsync:130-142` |
| persistence-9 | M | localStorage write failures (quota, private mode) are reported as "Project saved." | `storageInterop.js:18-21`; `ProjectStore.cs:168, 448, 456` |
| persistence-11 | M | Keyframe edits never mark the project dirty. | `ProjectStore.cs:98-105` |
| persistence-13 / site-3 | M | Every Save to Server creates a new row; nothing PUTs. | `ProjectService.cs:100-117`; `MyVideosPage.razor:195-201` |
| persistence-14 / site-7 | M | Case projects are private to their author; help promises group work on the case tab. | `VideoProjectController.cs:47-51, 64, 110` |
| persistence-15 | M | Re-publishing leaks the previous rendered upload; deleting a project keeps its video. | `VideoProjectController.cs:144-199` |
| persistence-18 | L | File menu: two identical download items, "Open…" is the browser list, "Import JSON…" is the file picker. | `Toolbar.razor:94-103` |

### Site host pages (verified; site-1 and site-2 S)

| id | sev | finding | where |
|---|---|---|---|
| site-1 | S | "Upload to server" on the site pulls the whole render back through one JS-interop `byte[]` return over SignalR (32 KB default message limit; nothing raises it in `Program.cs`). A real render cannot publish this way. | `ExportService.cs:1582-1595`; `domInterop.js:97-101` |
| site-2 | S | Server-tab downloads on the site buffer the file on the site heap and ship it as one SignalR message; `(int)totalBytes` overflows at 2 GB. | `BenMediaLibraryProvider.cs:75-103` |
| site-4 | M | A stale session publish id outranks the open project, so a later export attaches to the wrong project. | `VideoExportPublisher.cs:46` |
| site-5 | M | Editor state and server-project identity leak between the personal and the case editor within one circuit. | scoped `ClipStore`/`ProjectStore`; `App.razor:103,116` |
| site-6 | M | A render "published to case" never becomes a case file; the pages never show what was published. | `VideoProjectController.cs:158-199` vs `CaseFileController` |
| site-8/9 | M | My Videos sizes the editor to `100vh` and overshoots the app bar; `/video-editor` is a third, unlinked, header-less host with no height container. | `MyVideosPage.razor:31`; `VideoEditorPage.razor:29-32` |
| site-10 | M | The case Video tab is not feature-gated, so it links to "Page not found" when the editor is off. | `CaseDetail.razor:289-297` |
| site-11 | M | On the site a dead or refused token makes the Server tab look empty instead of refused. | `BenMediaLibraryProvider.cs:51-52, 134` |
| site-13 | L | Signed-out on the three site pages is a dead end; the case editor shows it as a red error. | page bodies |
| site-14/15 | L | Save/Delete handlers let transport exceptions kill the circuit; the case project list has a dead loader and no retry. | `MyVideosPage.razor:189-251`; `CaseVideoEditorPage.razor:229-234` |
| site-17 | M | No test touches My Videos, the case editor or the three host services. | — |

### WASM shell and deployment (verified; wasm-1 S, the COOP/COEP item rated L)

| id | sev | finding | where |
|---|---|---|---|
| wasm-1/2/3/4 | S/M | Folded into F11: no reachable sign-in, sub-path-broken login redirect, no 2FA, no sign-out. | `Login.razor`, `AuthService.cs`, `MainLayout.razor` |
| wasm-5 | M | The static app ships no security headers; `TokenStore`'s stated CSP mitigation does not exist. | `web.config:30-63`; `index.html` |
| wasm-8 | M | The editor deploy has no build identity; the smoke check passes on the previous build. | `deploy-ishaunted.ps1:869` |
| wasm-9 | L | No cache headers for `index.html`/`appsettings.json`; a cached shell references fingerprinted files `/MIR` deleted. | `web.config:38-55` |
| wasm-11 | M | The IDE launch profile opens `http://127.0.0.1:5180`, an origin neither the API's CORS list nor the sidecar allows. | `launchSettings.json:7-9` |
| wasm-12 | M | The standalone editor is not discoverable: no link from the site, and help never gives its address. | help `:17-22` |
| wasm-13 | L | The theme toggle overwrites the site's whole `layoutSettings` object. | `index.html:38` |
| wasm-15 | L | `BearerTokenHandler` sends known-expired tokens and buffers the whole body on retry. | `TokenStore.cs:35-40`; `BearerTokenHandler.cs:29-33` |
| wasm-16 | M | `Ben.Wasm.Video` has zero unit coverage. | — |

### Camtasia-class use cases nobody had accounted for (completeness critic)

Priorities are the critic's, judged for this product (members cutting short evidence reels from
camera, phone and DVR footage and audio), with my agreement noted where the plan places them.

| pri | use case | status in the repo | plan |
|---|---|---|---|
| S | Region blur / pixelate for redaction (faces, plates, house numbers, client interiors) | Absent; the only Blur is a whole-frame `gblur` effect. Matters more here than in Camtasia because of the private-engagement redaction rule. | Phase 8 (new) |
| S | Picture-in-picture / side-by-side of two cameras | Absent at the model level: `VideoClip`/`ImageClip` carry only Width/Height, no X/Y/Scale/Opacity/Rotation, so track 2 can only replace, never overlay (with export-1). | Phase 8 with zoom-n-pan (clip transforms) |
| S | Audio clean-up (noise reduction, high/low-pass, normalise) | Absent; no audio effect type at all. | Phase 9 (already) |
| S | Safari and Firefox support | `opfsIsAvailable` checks only `getDirectory`; every write uses `createWritable` on the main thread, which Safari before 26 lacks → silent work loss. | Phase 4 (feature-detect + sync-access-handle worker fallback, or a plain "use Chrome/Edge" gate) |
| M | Voice-over narration from the microphone | Absent (no `getUserMedia`/`MediaRecorder`). | Phase 12 candidate |
| M | Auto-captions (speech-to-text) | Absent; SRT/VTT export of titles only. | Phase 12 candidate (the mobile app already transcribes) |
| M | Ducking / levelling across tracks | Absent; `amix` defaults lower dialogue instead of protecting it. | Phase 9 (`sidechaincompress` after the mix fix) |
| M | Crop and rotate a clip (portrait phone footage, DVR bars) | Absent for video/image clips; no autorotate handling. | Phase 8 with clip transforms |
| M | Still-frame snapshot export (the frame where the anomaly appears) | Absent; formats are mp4/webm/mov only. | Phase 3 (one `-frames:v 1` path in the export dialog) |
| M | Keyboard-shortcut discoverability | A `?` overlay exists but nothing opens it; help has no shortcut table. | Phase 0 (File → Keyboard shortcuts; help table) |
| M | Accessibility of timeline and canvas | Six aria attributes in the whole library; chips, handles and diamonds have no role/label/focus. | Phase 11 follow-up |
| M | Touch / iPad; mobile Safari | No coarse-pointer sizing; under 700 px the media panel is hidden with no way to import and no "use a desktop browser" message. | Phase 1 (desktop-required gate under 900 px) |
| M | Large single local source (500 MB – 2 GB+) | Never exercised; no import gate, quota check or staged write for local files. | Phase 11 walk with the Downloads media; Phase 5 quota display |
| M | Multi-hour timelines / cutting a clip out of a session recording | Known ("today that breaks" past ~60 min 1080p, item #38); the sidecar is the answer but nothing routes to it. | Phase 4 |
| M | ffmpeg.wasm memory ceiling: detect, explain, route to the sidecar | Nothing detects an OOM abort or suggests the helper. | Phase 4 (crash classifier → "too big for the browser engine; install the sidecar" with the Phase 10 link) |
| L | Screen/webcam recording, cursor effects, green screen, device frames, social sharing, GIF/batch export, project templates, bin folders/tags, localisation | Absent; honestly out of scope for evidence reels (the critic's view and mine). | not planned |

## Use-case checklist as walked (client-side host, signed out)

| Use case | Result |
|---|---|
| Engine start / status chip | ✅ Ready; ❌ died after the post-split re-encode (F7) |
| Import video / image / audio from machine | ✅ / ✅ / ❌ orphaned (F2) |
| Import from server (scope, two-click) | ❌ raw 401 signed out (F11); signed-in path covered by `WasmEditorTests` |
| Move / trim / split / delete / undo | drag moved a clip into an overlap (F5); split cut at the wrong place and crashed the engine (F6/F7); delete+undo ✅ |
| Ripple, snapping, Fit, TC toggle, Mark In/Out | ✅ (Ripple/Marker hidden under the panel, F3; Mark In/Out misplaced, preview-13) |
| Tracks add/remove/reorder | ❌ hidden on this host (F2); rename ✅ |
| Insert vs overwrite prompt | only on server placement / bin add (F5) |
| Markers | ✅ (misalign when scrolled, timeline-4) |
| Clip properties | present; needs undisclosed scroll (F3) |
| Titles (+ Text), transitions, effects | ❌ hidden on this host (F2) |
| Callout add + full property sheet | ✅; position overlay lands on the 38 px preview (F4) |
| Playback / scrub / frame step | ❌ clip-mode playback of the selected clip only (F6) |
| Preview (full quality) | ❌ >60 s then hang, no cancel (F7) |
| Export presets → Export Now | ✅ rendered; ❌ no destination choice (F12) |
| Project save local / Open list | ✅ / ✅; ❌ not restored on refresh (F9) |
| Save to server / server list | post-export prompt works here; toolbar button hidden (F2); 401 on the site host (F13) |
| Sidecar chip + pairing panel | ✅ found the installed sidecar; ❌ no download link (F17) |
| Signed-out behaviour | editing/export work; Server tab error, no way to sign in (F11) |

## Test media available on this machine (all real, none blank)

| File (Downloads unless noted) | Size | Use |
|---|---|---|
| `GirlsInTrouble-2013.MOV` | 538 MB | large-source ceiling (ffmpeg.wasm memory, OPFS quota) |
| `eye-level-view-over-an-old-abandoned-mansion-…utc.mp4` | 365 MB | large 4K-class stock clip |
| `ghost-covered-in-sheet-…utc.mov` / `.mp4` | 298 MB / 2.8 MB | same footage, big vs small |
| `funny-made-it.mp4` (also Documents) | 49 MB | 29.5 s 1080p, the mid-size walk clip |
| `abandoned-house-in-foggy-forest-…utc.mov` | 29 MB | .mov container path |
| `ghost-sheet-loop.mp4`, `ghost-house-pan.mp4`, `ghost-looping.mp4` | 29 / 9.6 / 0.8 MB | short loops for transitions and ripple tests |
| `ghost-loop-transparent.mp4` | 1.6 MB | alpha footage (AlphaCompositing flag) |
| `spooky-haunted-house-animation-…utc.mp4` | 13 MB | animated stock |
| `output-720p.mp4`, `output.mp4`, `output-mobile.mp4` | 5.8 / 2.9 / 1.0 MB | previous exports, re-import round trip |
| `__ClaudeTestMusic.mp3` (= `Ben.Web.Playwright/Fixtures/test-audio.mp3`) | 7.5 MB | long audio track |
| Documents: `b6c6f102-…cf75.mp3` | 186 s | audio import gate |
| Documents: `AMFX-meteor-Take_0012.mov` | 0.4 MB | 4.8 s 568×320 h264+aac, the fast walk clip |
| `old-stone-stairs-…utc.jpg`, `halloween-background-…utc.jpg` | 20 MB / 11 MB | very large stills |
| `IsHauntedLogo-2.png`, `IsHauntedLogo-3-4k.png`, `IsHauntedDarkGlassFull.png` | 19 / 13 / 2 MB | transparent logos for watermark/clip-art |
| Documents: `Monk.png`, `Artboard 1.webp` | 1007×675 | png and webp still paths |
| Seed: `Ben.Data.WebApi/SeedData/Media/{porch-camera.mp4, hallway-camera.mp4, basement-evp.m4a, site-photo.jpg}` | 33–83 KB | tiny CI-safe fixtures already in the repo |

## Plan

Ordering principle: the client-side host is the product Ben described, so it gets the full feature
set first (Phase 0); then the timeline model must be trustworthy (Phase 2) and the render must
match it (Phase 3), because a Camtasia-class editor that exports something other than what the
timeline shows is broken however good the UI is; then the things a person hits in the first five
minutes (layout, playhead, crash/cancel, refresh); then server edges, portability, feature polish,
deployment, tests. Every phase updates `Ben.Web.Services/Help/Content/using-the-video-editor.md`
and `docs/deploy-editor.md` in the same PR (repo rule: a feature is not done until help says so),
and every phase is verified by the walk in "Verification", not by tests alone. Sizes: S ≈ half a
day, M ≈ 1–2 days, L ≈ 3+ days.

Before Phase 0, clean up the walk stack: stop the three hosts on :5252/:5078/:5180, drop
`IsHauntedDb_walk3`, remove `.uploads-IsHauntedDb_walk3`, stop the :5999 media server, reset the
browser viewport preset.

Decisions I have taken that Ben may want to reverse (each is called out where it applies):
**D1** transitions overlap the clips (Camtasia) rather than keeping the timeline length;
**D2** cross-track transitions are retired in favour of clip fade-in/out;
**D3** imports go to a Media bin and are placed on request, with the first import onto an empty
timeline auto-placed; **D4** `/video-editor` redirects to `/my-videos`; **D5** the true live
compositor (a sequence player instead of the re-encoded proxy) is the last phase, not the first.

### Phase 0 — Foundations (S–M) — F1, F2, F9-gate, F10, F16, timeline-8, wasm-11, half of F19

**Host parity by construction.** New `Ben.Video.Editor/Extensions/VideoEditorHostDefaults.cs`:
`ApplyEditingDefaults(options)` (MultiTrack, AudioTracks, Transitions, TextOverlays, VideoEffects,
ProjectPersistence, ErrorLog, RippleEdit, BackgroundRendering, NativeSidecar, plus new
`SidecarDownloadUrl` and `SignInUrl`) and `ApplyServerIntegration(options, apiBaseUrl)`
(MediaLibrary, MediaLibraryBaseUrl, AssetCatalogUrl, DocumentPostUrl). `Ben.Web.Website/Program.cs:37-63`
and `Ben.Wasm.Video/Program.cs:31-47` call both; the WASM call to `ApplyEditingDefaults` goes
**before** the early return at `:35`. Leave `ProjectOptionsSnapshot` (`Models/ProjectFile.cs:41-51`)
as an inert DTO. Make the feature flags gate **creation only**: drop the `_options.Transitions` /
`_options.TextOverlays` guards in `ExportService.cs:205, 232` so a restored project renders what
it contains (transitions-15, titles-11). Tests: `VideoEditorHostDefaultsTests` (reflection over
every bool on `VideoEditorOptions` minus an explicit allow-list; a new flag fails until classified);
source-scan that both `Program.cs` files call the helper and contain no inline `options.MultiTrack =`;
rewrite `Ben.Web.Tests/VideoEditorRegistrationTests.cs:47-74` to use the helper.

**Toolbar overflow policy** (`Toolbar.razor`). Order: File · project name (`flex:0 1 160px`,
hidden under the existing 1100 px container query) · Initialize · Open · Undo · Redo · Preview ·
Export — all `Overflow="Never"` — · spacer · status chip capped (`.bv-toolbar__progress` 80 px,
`.bv-toolbar__status` 180 px with ellipsis) · Fullscreen · queue badge · reopen-panel · Assets
toggle (`Overflow="Auto"`, labelled) · Save to server (`Auto`) · chips. Delete the three dead panel
toggles, their parameters and callbacks (`Toolbar.razor:148-168, 332-358`),
`LayoutService.ToggleClipBrowser/TogglePreview/ToggleTimeline` and `.bv-timeline-row--hidden`;
rewrite the comments at `:57-61` and `Toolbar.razor.css:229-230`. Test: extend
`ToolbarOverflowLabelTests` with `Primary_Actions_Never_Overflow`; Playwright at 1280×800 that
Export stays visible while an import's progress bar shows.

**Keyboard layer.** `keyboardInterop.js`: forward `e.ctrlKey || e.metaKey` as the modifier
(Cmd+Z/Cmd+Shift+Z work on macOS), `preventDefault()` for the handled set (Space, Delete,
Backspace, arrows when a canvas item is selected, Home/End), and do not forward when
`activeElement.closest('.k-popup, .k-animation-container, .k-dialog, .k-window, [role=dialog]')`;
for Escape with an open `.k-animation-container .k-popup`, dispatch an outside click on
`document.body` instead (Telerik 14.1 `TelerikDropDownButton` has no `@bind-Open`). Extend the
Escape branch (`VideoEditor.razor:1012-1019`) to clear image/callout/clip-art selections and the
Delete branch (`:917-950`) to remove a selected title or transition (titles-7, transitions-10).
Arrow keys step frames when nothing on the canvas is selected (preview-6) — make
`VideoPreview.StepBack/StepForward/Rewind` public and add `SeekToEnd`. Playwright: click File →
Escape closes it and Properties still shows the clip editor; Cmd+Z undoes on a Mac context.

**Watermark endpoint.** Add `GET /api/video-assets/watermark-config` to
`Ben.Data.WebApi/Controllers/Entities/VideoAssetController.cs` returning the first active
Watermark asset as `{Enabled, FileUrl, Version}` or `{Enabled=false}`, always 200; filter
`Type != Watermark` out of `GetCatalog` (callouts-23). Fix the watermark filename (export-6: rename
after `ApplyWatermarkAsync`) and vertical placement (export-7: overlay expressions `H-h-{my}`).
Controller test with and without an asset; Playwright asserts no 404 under `/api/` during an export.

**Launch profile and small things.** `Ben.Wasm.Video/Properties/launchSettings.json` →
`http://localhost:5180` (wasm-11); the theme toggle read-merge-writes `layoutSettings` (wasm-13);
File → "Keyboard shortcuts" opens the existing `?` overlay and help gains the shortcut table
(critic).

**Playwright hygiene.** `VideoEditorTests.cs:111-174`: exact `.bv-toolbar`, `.bv-timeline`,
`.bv-preview`, `.bv-toolbar__status` with real assertions. `AudioScrubModeTests.cs:71-80, 101-110`:
`Assert.Ignore` only for the seed precondition; the upload/waveform wait becomes a real failure.
`WasmEditorTests`: `[OneTimeSetUp]` GET of the WASM URL with a 5 s timeout → `Assert.Ignore`. Link
`Ben.Data.WebApi/SeedData/Media/*` into the Playwright output as `Fixtures/Media/*`, add a
generated 5 s `test-clip.mp4` (lavfi testsrc) and `test-image.png`, and helpers
`EnsureFfmpegReadyAsync()` / `ImportFixtureAsync(name)` that open the Media panel and call
`SetInputFilesAsync` on `#bv-file-input`. New `Ben.Web.Tests` guard: no file under the Playwright
tests contains `Assert.Pass(`.

### Phase 1 — Layout like Camtasia (M) — F3, F4, site-8, site-9

**Preview owns the height; the timeline is sized to its tracks.** Wrap `<VideoTimeline>` in the
unused `.bv-timeline-row` (`VideoEditor.razor.css:169-175`) driven by `--bv-timeline-h`
(`LayoutService.TimelineHeight`, default 260, max 600, min 120), make `.bv-preview-row`
`flex:1 1 auto; min-height:220px`, and change `.bv-timeline { height:100% }`
(`VideoTimeline.razor.css:3-6`) to fill its row only. The one `ResizableDivider`
(`VideoEditor.razor:246`) gets `Target="next"` (`ResizableDivider.razor` + `.razor.js`: use
`nextElementSibling`, negate delta) and resizes the timeline; remove `PreviewHeight/SetPreviewHeight/PreviewMaxHeight`
from `LayoutService.cs`. `VideoTimeline` exposes `PreferredHeightPx` (header + ruler + track rows +
one 40 px lane per overlay, which is by design: row order encodes z-order); `VideoEditor.OnClipsChanged` calls
`Layout.AutoFitTimeline(preferred)`, ignored once the person has dragged the divider. Aspect-lock the
stage: `.bv-preview__screen { aspect-ratio: var(--bv-canvas-aspect); max-width:100%; max-height:100% }`
so `LiveOverlayPreview`, the control-point overlays and `PreviewGeometryService`
(`previewGeometryInterop.js:14`) line up with the picture. Site pages: one
`.ben-editor-page { height: calc(100vh - var(--ben-appbar-h, 58px)) }` rule used by both
`MyVideosPage.razor:31` and `CaseVideoEditorPage.razor:31-34`; `VideoEditorPage.razor` becomes a
redirect to `/my-videos` inside its `FeatureGate` (**D4**). Tests: `LayoutServiceTests`
(`SetTimelineHeight_MarksUserSet`, `AutoFitTimeline_IgnoredAfterUserResize`); Playwright at
1440×900: screen box ≥ 350 px tall, aspect within 1 % of the canvas, timeline ≤ 45 % of the editor,
divider drag grows the timeline.

**Docked right-hand panel instead of a floating window.** Replace the `TelerikWindow`
(`VideoEditor.razor:92-104`, defaults `:473-476`, self-heal `:636-665`) with
`<aside class="bv-side-panel">` in a workspace row `[preview | horizontal divider | panel]` above
the timeline row; revive `.bv-workspace` CSS (`VideoEditor.razor.css:18-52`), delete
`.bv-media-panel-window*`, the position fields and ClipBrowser's own inner pin window
(`ClipBrowser.razor:29-52`, media-18); keep the `TelerikTabStrip` with `PersistTabContent`. Header
chevron → `Layout.TogglePanel()`; the existing reopen button reopens it; `OpenFilesAsync:1105-1118`
still un-collapses and switches to Media. `LayoutService`: reuse `BrowserWidth` as panel width
(280–560, default 340), add `PanelCollapsed`, `PanelTab`, and a `LayoutSnapshot` record persisted as
`bv-layout` through `storageInterop.js` (300 ms debounce), applied in `OnAfterRenderAsync(firstRender)`
before `Projects.InitAsync()`. Give the Assets/Transitions gallery its own tab beside Media and
Properties instead of the off-by-default toggle (transitions-12, callouts-16). Under 900 px wide the editor shows a
"This editor needs a desktop browser" panel instead of hiding the media panel silently (critic).
Tests: snapshot round-trip, width clamp, toggle; Playwright: panel does not intersect
`.bv-timeline__header`, `.bv-props-tab` ≥ 600 px tall, width survives reload; a 375 px viewport
shows the desktop message.

### Phase 2 — A trustworthy timeline model (L) — F5, F6b, F8, timeline-1/2/10/11, audio-9, transitions-3/5/6/7, motion-3

**One playhead, one coordinate system.** `PlaybackState` gets `TimelineTime` (absolute; the only
value the timeline, markers, split, "+ Text" and placement read) and `MediaTime` (what the loaded
`<video>` reports). Selecting a chip no longer loads the raw source into the Working Window
(`VideoEditor.razor:1261-1279` → keep the clip preview for the Media tab's card only); in Timeline
mode selection seeks to the clip's start. Add `ClipStore.SplitClipAtTimelineTime(id, absolute)`
(subtracts `TimelinePosition`, validates against the trimmed length; `AudioClip` gains
`TrimmedDuration`, audio-11) and route the S key, context menu and Properties Split through it.
`AddTextOverlay` uses `TimelineTime` like `AddCalloutClip` (titles-8); `AssetBrowser` places at the
playhead (callouts-7). Tests: `PlaybackStateTests` for the two clocks;
`ClipStoreTests.SplitClipAtTimelineTime_*` for video/audio/image; Playwright: import, seek to 2 s,
press S → the cut is at 2 s on the ruler.

**No same-track overlap, ever.** New pure `Services/TrackLayout.cs` (`SequentialItems`,
`Overlaps(track, pos, dur, excludeId)`, `Validate(track)`); `ClipStore` validates after every
mutating command in DEBUG and exposes `ValidateAll()`; a private `ResortSequential(track)`
stable-sorts by `TimelinePosition` and renumbers `Order` at the end of every commit (timeline-10).
Commit methods (`CommitDraggedPosition:1309`, `RippleCommitDraggedPosition:546`,
`CommitDraggedPositionAndTrack:1349`) take a `DropMode { Insert, Overwrite }` (Insert = Camtasia
push via a new `MoveWithRippleCommand` mirroring `InsertClipRippleCommand`; Overwrite generalises
`OverwriteInsert:680` from `VideoClip` to `TrackItem` through `OverwriteEditCalculator`). Draw chips
at absolute positions (`position:absolute; left:pos·PxPerSecond`, as the overlay row `:489` and
transition chips `:796` already do) and delete the `runningEndSeconds`/`gapPx` block
(`VideoTimeline.razor:374-397`) and the legacy HTML5 drag path (`:413-419, 1710-1728`,
timeline-19). On `FinalizeMove:2022`, if `TrackLayout.Overlaps`: ripple on → auto-push; otherwise
raise `OnDropConflict` and reuse `RippleInsertPrompt` (`VideoEditor.razor:361`, handlers
`:1232-1257`) with Insert / Overwrite / Cancel. The Media-card drop handler resolves through
`AllVideoClips` and treats an already-placed clip as a move (timeline-2). Tests to add to
`ClipStoreTests`: `CommitDraggedPosition_OntoOccupied_Insert_PushesLaterClips`,
`…_Overwrite_TrimsUnderlying`, `…_Undo_RestoresShiftedClips`,
`CommitDraggedPositionAndTrack_CrossTrack_ResolvesOnTargetTrack`,
`RippleCommitDraggedPosition_NeverOverlaps`, `AddImageClip_NeverOverlaps`, `SplitClip_KeepsSequential`,
`OverwriteInsert_AudioAndImage`, `ValidateAll_ThrowsOnOverlap`, `Move_ResortsOrder`; new
`TrackLayoutTests`; Playwright drag of chip B onto A → no box intersection and the duration label
equals the sum.

**One placement policy; a real Media bin (D3).** `ClipStore.MediaBin` (`AddToBin/RemoveFromBin`,
undoable) persisted as `ProjectFile.Bin` (SchemaVersion 2, tolerant read); ClipBrowser tabs list
bin items with an "on timeline ×N" badge; every placement (local import, server placement, card
"Add to timeline", drag, assets) goes through one `VideoEditor.PlaceAsync(TrackItem)` that anchors
to the playhead, checks overlap on the target track (audio tracks too) and awaits the prompt
(media-4). `VideoEditorOptions.AutoPlaceFirstImport` (default true) for the empty-timeline case.
Never silent: pure `ImportPolicy.Decide(options, contentType) → Bin | Skip(reason)`; skipped files
show `ImportStage.Skipped` with the reason and a toast (replaces `ClipBrowser.razor:1871` and
`:1943-1950`). `MediaKindRouter` decides kind from the File's MIME type first, extension second,
with the missing extensions added (media-8). Tests: `AddToBin_DoesNotTouchTracks/_Undoable`,
`Bin_RoundTrips`, `ImportPolicyTests`, `MediaKindRouterTests`; Playwright: png import → card,
0 chips → Add to timeline → 1 chip.

**Transitions live on the model (D1).** `AddTransition/UpdateTransition/RemoveTransition` shift the
"to" clip and every later item on the track by ∓duration through a new command (Camtasia overlap),
so `TimelineTrack.TotalDuration` matches the render (transitions-3). New pure
`Services/TransitionReconciler.cs` called at the end of `RemoveClip`, `RippleDeleteClip`,
`SplitClip`, `UpdateTrim` and every commit: drop a transition whose clips are no longer adjacent
(transitions-5). Clamp durations through `TransitionDurationClamp` in Add/Update (transitions-7);
`CommitTransitionResize(id, originalDuration, originalPosition)` for the edge drag (transitions-6).
Retire `AddCrossTrackTransition` and its menu item; give `VideoClip` fade-in/out through the
existing `BuildVideoEffectsFilter` (**D2**, transitions-9). Move the junction insert button out of
the flex flow (transitions-4). Tests: `TransitionReconcilerTests`,
`ClipStoreTests.AddTransition_OverlapsClips/_Undo`, `Split_DropsTransitionAtCut`.

**Keyframes travel with their layer.** Store `MotionKeyframe.Time` relative to the layer's
`TimelinePosition` (convert at `Evaluate`/overlay/diamond boundaries; migrate v1 files on load)
so moving, trimming or rippling a layer keeps its animation (motion-3); remove a layer's motion
path when the layer is removed (motion-18, undoable). Tests: `MotionKeyframeServiceTests` for the
relative clock and the migration.

**Track state means something.** Muted tracks are skipped by the audio mix, the native assemble
path and the Working Window, and a muted video track's clips render `MuteAudio`; mute/lock/rename
are undoable; the context menu disables mutating items on a locked track (audio-5, timeline-11/12).
Track menu: audio tracks reorder among audio tracks; "Remove track and N clips" confirms
(timeline-18).

### Phase 3 — The render matches the timeline (L) — export-1/2/3/5/8/9/13/14/20, transitions-1/2, audio-1/3/4, callouts-2, titles-9/10, motion-8/11

All of this is one refactor of `ExportService.RunPipelineAsync` around a pure plan, plus a
recording ffmpeg fake so the pipeline is finally testable.

- **`ExportSegmentPlanner`** (pure, `Ben.Video.Core`): walks the merged `(pos, seg, dur)` list of
  the primary track and yields `Filler(start, len)` entries where `pos > runningEnd` (black+silence
  via `color`/`anullsrc`), a leading filler when the first clip starts after 0, and the junction
  list `IReadOnlyList<Transition?>` resolved by `(FromClipId, ToClipId)` rather than by index
  (export-2, transitions-2). The Working Window uses the same planner (preview-3).
- **Always-audio segments** when `IncludeAudio`: produce every video and image segment with the
  always-audio builders (`BuildBackgroundRenderVideoArgs`/`BuildBackgroundRenderImageArgs`, which
  attach `anullsrc`), so concat never sees mixed stream layouts (export-3/audio-2).
- **Trim before input**: `-ss start -to end` before `-i` in `BuildTrimArgs` and
  `BuildBackgroundRenderVideoArgs` (mirror `BuildAudioClipTrimArgs`) so volume keyframes and fades
  see clip time (audio-4). Preview trims get `PreviewScaleTarget()` as `outputWidth/Height`
  (transitions-8, F7 contributor).
- **Transitions with sound**: `BuildXfadeFilterComplex` emits a parallel `acrossfade` chain per
  junction and maps `[aout]` (transitions-1).
- **Mix that respects the timeline**: `BuildAmixArgs(videoHasAudio)` drops `[0:a]` or inserts
  `anullsrc` when the assembled video is silent; `amix=…:normalize=0:dropout_transition=0` with an
  `alimiter` guard (audio-1, audio-3); muted tracks skipped (Phase 2); audio segments written as
  PCM `.wav` so `amix` encodes once (audio-24); skipped clips land in `job.Warnings`.
- **Layers export**: after concat/transitions and before overlays, call the existing
  `ComposeVideoLayersAsync` over `VideoTracks.Where(t => t.Id != primary.Id)` (export-1);
  `CanvasSelectionOverlay` and `LiveOverlayPreview` enumerate all video tracks (motion-11).
- **One overlay pass** ordered by `LayerIndex` across text, callouts and clip art (titles-9);
  every callout goes through the SVG renderer — delete the `drawbox` branch (callouts-2); all
  numeric formatting through `CultureInfo.InvariantCulture` and `<InvariantGlobalization>` in
  `Ben.Wasm.Video.csproj` (titles-10); fonts embedded as base64 `@font-face` in the rasterised SVG
  (titles-1, callouts-11); zoompan expressions rewritten as `scale+crop` in `t` (motion-8).
- **Still-frame export**: a "Save this frame" action (`-frames:v 1` at the playhead, PNG) in the
  Export dialog and the Working Window's right-click menu — for an evidence site the frame is the
  most shared artefact after the clip (critic).
- **Settings honesty**: `ResolveCanvas(settings, clips)` uses the first clip's dimensions for
  "Source resolution" (export-5); `Fps = 30` everywhere with presets carrying fps (export-13);
  Opus only for webm, `-tag:v hvc1` for H.265 in mp4/mov, `-movflags +faststart` (export-14);
  progress clamped monotonic with re-banded phases (export-8); one `HasExportableContent`
  predicate shared by toolbar and dialog (export-20); `NativeClipEncoder` passes the chosen
  resolution (export-4); a wedged worker is Failed, not Cancelled (export-12).
- **Queue**: `CanQueue` separate from `CanExportNow`; a queued job renders a snapshot
  (`ProjectService.BuildCurrentProjectFile`) not the live timeline (export-9); reopening the dialog
  re-attaches to a running job (export-11); the destination prompt's X keeps the file and Discard
  confirms inline (export-17).

Tests: introduce `IFfmpegCommands` (Exec, ConcatClips, WriteFile, DeleteFile, Rename, GetMetadata,
ExportToOpfs, DownloadBlobUrl) implemented by `FfmpegService` and a `RecordingFfmpeg` fake; new
`Ben.Video.Tests/Services/ExportServiceTests.cs` asserting the argv per scenario: gap filler,
image-first slideshow with music, transition with audio, junction matching, secondary track
composited, muted track absent, head-trimmed clip with fade, cancel between phases;
`ExportSegmentPlannerTests`, `AudioMixPlannerTests`. Playwright: export the walk project and probe
the download with the sidecar's ffprobe (duration equals the timeline, has audio).

### Phase 4 — Playback, preview and robustness (M–L) — F6, F7, preview-2/4/5/7/9/18/19/20, media-1/9, transitions-8, audio-6

**The Working Window behaves like a player.** `LoadUrlAsync(preserveTime: true)` keeps the
playhead and playing state across auto-refreshes (preview-2); image-only playback drives
`_currentTime`, `_duration` and `OnTimeUpdate` (preview-9); duration sums `EffectiveDuration`
(preview-18); the audio mix runs on the preview too (audio-6) through the shared planner; removing
the last clip clears the player (preview-19); a `Standalone` `VideoPreview` gets its own
`PlaybackService` so the popout and full-quality player stop corrupting each other (preview-5); a
`ProjectSettingsService { Width, Height, Fps }` replaces `ExportResolutionService` +
`PlaybackService.SessionFps` and feeds the ruler (preview-7, timeline-17); the quality picker
schedules a refresh (preview-8); `AutoPreviewGate.Decide(ffmpegState, exportRunning)` keeps
auto-refresh out of a running export (preview-20); a rAF-driven time callback replaces the 4 Hz
`timeupdate` (preview-10); Mark In/Out becomes "Set In/Out from playhead" in the clip's trim section
(preview-13). Full-quality previews are discarded from OPFS on close (preview-4).

**Cancel, watchdog, auto-recovery.** Keep `_fullQualityJob`; window close and a Cancel button call
`ExportJob.Cancel()` (`Models/ExportJob.cs:85`); after 10 s without progress the button reads
"Stop and reset ffmpeg" → `LoadCoreAsync()`. `FfmpegStatusPresentation` gains a `wedged` case shown
to everyone; `RecordFailure` classifies `RuntimeError`/"memory access out of bounds"/"unreachable"
as a crash → `OnWorkerCrashed`; an out-of-memory abort is told apart from a generic trap and the
toast says "this file is too big for the browser engine" with the sidecar download link (Phase 10);
`VideoEditor` auto-reloads the core once per 60 s (`FfmpegCrashRecoveryPolicy`), toasts,
re-schedules the preview; browsers without `createWritable` (Safari before 26) are detected up
front and either routed to a sync-access-handle worker for OPFS writes or shown a "use Chrome or
Edge" gate before any work can be lost (critic); `RefreshWorkingWindowAsync` gets a
`catch` that logs through `ErrorLog` and keeps the last-good URL. Thumbnail extraction uses
keyframe-only decode (`-skip_frame nokey`) with one input-side `-ss` per frame, honours the exit
code and is cancelled for clips a split removed (media-1); every `FileImportStatus` gets a real
`Cts` threaded through OPFS write, mount, probe and thumbnails so the import Cancel works (media-9).
Tests: `RecordFailure_WasmRuntimeError_RaisesOnWorkerCrashed`, `FfmpegCrashRecoveryPolicyTests`,
`Wedged_ShowsResetLabel`, `ExportAsync_CancelBetweenPhases_EndsCancelled`, `AutoPreviewGateTests`,
`FrameMathTests` (preview-14); Playwright: close the Preview window → status back to Ready within
30 s; import → play → counter reaches the full total and the image overlay shows.

### Phase 5 — Persistence integrity and not losing work (M) — F9, persistence-2…13, motion-1, audio-7, callouts-1

**The project file holds everything the model holds.** One `ProjectFileJson.Options` (camelCase,
string enums, case-insensitive, `WhenWritingNull`) used by `ProjectService`, `ProjectStore`, both
site pages and `VideoProjectController` (persistence-1); missing DTO fields added: keyframe
`ScaleX/ScaleY/Rotation`, callout `TextAlign/TextVerticalAlign/TextWrap/TextShadow/TextPadding`,
clip `MuteAudio/HasAudio/LinkedClipId`, clip-art control-point definitions re-resolved from the
asset on load, `ProjectFile.Bin`, `ProjectFile.Export` settings (export-18); `SchemaVersion` 2
with `ProjectFileMigrations.Upgrade` and one `ProjectService.Parse(json) → (file, error)` that
validates and is used by every reader (persistence-8). The round-trip test is replaced by a
reflection parity test: every settable property on `VideoClip/AudioClip/ImageClip/CalloutClip/
ClipArtClip/TextOverlay/Transition/MotionKeyframe` must survive `BuildCurrentProjectFile` →
`RestoreAsync` unless listed in an explicit exclusions set (persistence-24).

**Saving is honest.** `setItem` returns `bool` and a failure throws `ProjectStorageException`
surfaced as an error toast (persistence-9); read-modify-write of the index per mutation plus a
`storage` listener (persistence-10); `MotionKeyframeService.OnChanged` marks dirty (persistence-11);
the four File items enable on `CanSave` (any item, marker or dirty) not on video clips
(persistence-4); rename passes the bare name and draws the dirty mark separately (persistence-5);
one `ConfirmDiscardIfDirtyAsync()` guards New, Open…, Import and the site pages' Open
(persistence-6); Import JSON and the legacy Open route through `LoadFromFileAsync` so OPFS media
is remounted (persistence-7); the import input is cleared after use (persistence-19); the Open
Project grid sorts by real dates and sizes (persistence-20). File menu: New · Open… · Save ·
Save As… · Export project file… · Import project file… · Export Error Log · Help (persistence-18).

**Autosave, restore, unload guard.** `ProjectStore.EnableAutosave(2 s idle)` on `IsDirty`
(creating "Untitled – date" if no project), `_restoring` guard, "Saved · hh:mm" in the name
tooltip. Replace the 30 s Ready poll in `RestoreOpfsFilesAsync` with a remount on
`Ffmpeg.OnStateChanged → Ready`, auto-call `LoadCoreAsync()` when a restored project has clips, and
offer "Reconnect media" on a missing chip (persistence-16). `domInterop.js`: `setUnloadGuard(bool)`
and `flushOnPageHide`; guard when autosave is pending or a job is running (`UnloadGuardPolicy`).
**OPFS housekeeping**: a `SourceRegistry` (source id → ext, refcount) fed by add/remove/undo-discard
deletes OPFS copies and MEMFS mounts when the undo stack drops the last reference; an
`OpfsGarbageCollector` reconciles against every saved project in the index and the library cache,
and the Media panel shows `navigator.storage.estimate` (media-2, persistence-12). Tests:
`Autosave_WritesAfterDebounce_AndRestoresOnReload`, `Autosave_SkippedDuringRestore`,
`UnloadGuardPolicyTests`, `SourceRegistryTests`, `OpfsGarbageCollectorTests` with the fake OPFS
module; Playwright reload restores the chip; `Page.Dialog` of type beforeunload when leaving
mid-export.

### Phase 6 — Server edges without the server in the middle (M–L) — F11, F12, F13, F15, site-1…7, persistence-13/14/15, wasm-1…4

**One authenticated server store.** `IProjectServerStore` (`IsAvailable`, `ListAsync`, `GetAsync`,
`SaveAsync(file, existingId, caseId) → Guid`, `DeleteAsync`, `PublishAsync(id, exported)`) with
`HttpProjectServerStore` (WASM, over `ProjectPersistenceHttpClientName`, folding in
`ProjectService.SaveToServerAsync/LoadFromServerAsync:96-200`; PUT when an id is known, POST and
capture the id otherwise) and `Ben.Web.Services/BenProjectServerStore` over `IBenAdminClient`.
`ProjectStore` splits `CurrentProjectId` into `CurrentLocalId` and `CurrentServerId`; the site
pages reuse the id (site-3) and `VideoExportPublisher.cs:46` prefers the open project over the
stale session id (site-4). `SaveToServerHandlerAsync:1961` → the store; toolbar and
`ExportSavePrompt` gates → `IsAvailable`. Delete `LoadFromServerHandlerAsync:2086-2133`; mark
`DocumentSaveUrl` obsolete. `VideoProjectController`: case projects visible to everyone who can
access the case (`CanAccessCaseAsync`), owner-only update/delete, `CreatedBy` in the list
(persistence-14/site-7); publish deletes or versions the previous upload and deleting a project
removes its video (persistence-15); a render published to a case becomes a `CaseFile` through a
shared `CaseFileLinker` (site-6). Tests: `HttpProjectServerStoreTests` (stub handler pattern from
`ProjectServiceSaveToServerTests.cs:18-47`), `BenProjectServerStoreTests`, controller tests for
case visibility and publish versioning; site Playwright: cloud button → "Project saved to server"
and a 2xx.

**Bytes never cross the circuit.** Site-host publish: the browser posts the retained OPFS blob
straight to the API through the existing upload relay (`UploadTicketService` + `UploadRelay`,
`Ben.Web.Website/Program.cs:466-475, 552-572`) instead of `blobUrlAsBytes` over SignalR (site-1,
site-12). Site-host downloads: extend `IMediaLibraryProvider` with `GetDownloadUrlAsync(fileId)` /
`GetThumbnailUrlAsync(fileId)` so the browser fetches the file itself and streams it into OPFS
(`opfsDownloadToClip(url, headers, clipId, ext)` — `fetch → response.body.pipeTo(createWritable())`
with a byte counter), which also removes the triple buffering and the 2 GB `int` cast on the WASM
host (site-2, media-6). Server-tab previews consult the OPFS cache first (media-5); a Refresh
button reloads the list (media-10); `MediaLibraryPicker` is deleted. Tests: relay round trip in
`Ben.Web.Tests`; Playwright on the site: place a 49 MB file without a SignalR message over 32 KB
(assert via `Page.WebSocket` frame sizes or a server-side counter).

**The client-side host publishes and signs in.** `Ben.Wasm.Video/Pages/Editor.razor:20` passes
`OnPublishExport` (save-first-if-never-saved, then `PublishAsync`) and
`EnablePublish="@Tokens.IsAuthenticated"`; `[Parameter] bool EnablePublish` on `VideoEditor` drives
`ExportDestinationPrompt.ShowUpload`, and signed out the prompt still appears with a "Sign in to
upload" hint. `VideoEditor` gains `[Parameter] RenderFragment? HostStatusContent` rendered beside
the status chip; the WASM host supplies `Components/SignInChip.razor` ("Sign in" → `login`
relative to `<base>`; email + "Sign out" calling `AuthService.LogoutAsync` when signed in).
`Login.razor` navigates to `Nav.BaseUri`, always shows the back link, links to the site's
forgot-password/register pages, and handles `RequiresTwoFactor` by asking for the code
(`AuthService.LoginAsync` returns `Ok | RequiresTwoFactor | RateLimited | BadCredentials | Unreachable`).
`HttpMediaLibraryProvider.GetFilesAsync:63` and `BenMediaLibraryProvider:51-52` throw a typed
`MediaLibraryUnauthorizedException`; `ClipBrowser` renders `.bv-browser__signin` with the host's
sign-in fragment (site-11); `VideoAssetCatalogService.GetAllAssetsAsync` isolates provider
failures (callouts-5). Site signed-out pages redirect to `/login?returnUrl=` (site-13); the case
Video tab is feature-gated (site-10); the site pages link to the standalone editor (wasm-12).
`ProjectListDialog` → "Projects" with "On this computer" and "On the server" sections backed by a
testable `ProjectsDialogState`. Tests: `AuthServiceTests` for the result enum with a stub handler;
`GetFiles_401_ThrowsUnauthorized`; source guards that `Editor.razor` passes `OnPublishExport=` and
that nothing under `Ben.Wasm.Video` navigates to `"/"`; Playwright signed in → prompt with
"Upload to server"; signed out → hint + download; the Server tab shows the sign-in link.

### Phase 7 — Portable projects (L) — F14, callouts-14

On `TrackItem` add `Guid? SourceFileId`, `long? SourceFileSize`, `string? SourceContentHash`,
mirrored on the DTOs and carried through `ProjectService.Map*` and `ClipStore.Restore*`.
`ClipBrowser.AddCachedFileToTimelineAsync:1442-1459` sets id and size; SHA-256 computed in
`opfsInterop.js` inside `DownloadAndCacheAsync:1394`. New `MediaRelinkService`, run at the end of
`RestoreOpfsFilesAsync:338-419` for missing clips with a source id (and for clip art with an
`AssetSource`, callouts-14): reuse the library cache `bv-clips/{SourceFileId}{ext}` if present,
else `IMediaLibraryProvider.DownloadFileAsync` → cache → verify size/hash → copy under the clip id
and `SourceMounter.MountAsync`. Auto-run under 50 MB total, otherwise `MissingMediaPrompt.razor`
("3 clips (412 MB) are on the server — Download / Later / Locate…"). "Replace Media…"
(`OnRelinkFileChangedAsync:2151`) writes OPFS through `ClipBrowser.WriteAndMountAsync:1815-1825`,
sets `OpfsExt`, clears the source id on hash mismatch. Help: "on another machine" becomes true
when signed in; say so. Tests: round-trip of the three fields, fake-provider re-fetch clears
`IsMediaMissing`, hash mismatch keeps it; Playwright: place `porch-camera`, save, delete `bv-clips`
through `navigator.storage.getDirectory()`, reload → chip loses `.bv-clip-chip--missing`.

### Phase 8 — Titles, callouts, clip art and keyframes behave like the rest (M–L) — titles-2…6, callouts-3/4/8/10/12/13/15/17, motion-2/4/5/6/7/9/10

Titles: `ClipStore.CommitTextOverlayUpdate(id, apply, revert)` modelled on `CommitCalloutUpdate`
and per-field commit like `CalloutEditor` (titles-4/5); drag anchor equals the alignment anchor
(titles-2); `MaxWidth` with `RichTextTspanBuilder.WrapLines` (titles-6); opacity/italic/outline
controls and an `IsCaption` flag feeding `.srt/.vtt` export (titles-12/13). Callouts and clip art:
control points stored relative to the bounding box (callouts-3); `SetDefaults` on shape change
(callouts-12); a rotation handle and rotation applied in both overlays (callouts-13);
`CommitClipArtUpdate` for every clip-art edit and sliders that commit on `OnChange` (callouts-8/17);
`NativeWidth/NativeHeight` on `ClipArtClip` so aspect is resolved once (callouts-10);
`DuplicateOverlay` and Ctrl+D (callouts-15); Assets tab search/filter wired (callouts-4); the
`LocalOpfsAssetProvider` lists only images (callouts-6); Lottie removed from the admin candidates
until a renderer exists (callouts-9). Keyframes: `RemoveKeyframe` picks the nearest within the
Upsert tolerance (motion-2); `FrameToKeyframe` carries ScaleX/Y/Rotation and double-click add uses
the same seed (motion-4/5); + Keyframe disabled outside the layer's span (motion-7); every
keyframe edit through `CommitMotionKeyframeEdit` (motion-6); overlay drags capture the pointer via
the timeline's `capturePointerAt` (motion-10); `SelectedIndex` flows to `MotionPathOverlay`
(motion-17); other layers' edges become snap guides (motion-19). **Clip transforms** (motion-9 and the critic's
PiP/crop/rotate items): `X/Y/Width/Height`, `Rotation`, `Opacity` and a `Crop` rectangle on
`VideoClip`/`ImageClip`, rendered through the existing overlay compositing (`ComposeVideoLayersAsync`
from Phase 3) so a clip on track 2 can sit in a corner or side by side, portrait phone footage can
be rotated and DVR bars cropped; `ApplyMotionFrame` overloads, a `StaticSeed`, and the same Animate
button give zoom-n-pan — Camtasia's most used animation. **Redaction**: a `RegionBlur` clip effect
(rectangle or ellipse mask, blur or pixelate, keyframeable position) built on the effect plugin
seam, because the private-engagement rule makes it the one effect this product cannot ship
without. Tests on the pure services and renderers; Playwright: add a title, drag it, undo; animate
an image; blur a region and check the export differs inside the region only.

### Phase 9 — Audio like a real NLE (M) — audio-8…19, audio-21/22/23/25/26, timeline-15

Detached audio honours trim and speed and copies volume (audio-8); split inserts an interpolated
keyframe at the cut (audio-10); `AudioClip.TrimmedDuration` used everywhere `VideoClip`'s is
(audio-11); audio and clip-art chips get trim handles through the shared handler and
`CommitAudioTrim` (audio-12, timeline-15); `WaveformPeakSlicer` shows only the trimmed region and
split halves get their own slice (audio-13, media-11); `AudioClipEditor` reseeds only when the clip
changes (audio-14); `aformat=channel_layouts=stereo` before `pan` (audio-15); fades clamp to the
trimmed length in store, panel and builder (audio-16); the envelope lane observes resize and draws
the baseline at the scalar volume (audio-17/18); `SetClipMuted` with a Mute checkbox and context
item (audio-19); link/unlink on the audio side and a visible link glyph (audio-21); dB meter labels
drawn on the canvas (audio-22); lock honoured by volume/balance/fade/keyframe edits (audio-23); the
Properties waveform is a seek surface, not a second player (audio-26). First audio effects on the
existing plugin seam: `afftdn` noise removal and `loudnorm` levelling (audio-25). Help gets an
Audio section that matches the code (audio-20). Ducking: a per-track "duck under narration"
option implemented with `sidechaincompress` once the mix is correct (critic). Tests: `AudioMixPlannerTests`,
`WaveformPeakSlicerTests`, `ClipStoreTests` for the new commits; Playwright: import the 186 s mp3,
trim by its edge, play, hear it in the Working Window (assert the mixed proxy has an audio stream).

### Phase 10 — Deployment, shell and sidecar (M) — F17, F18, media-13, wasm-5…9, wasm-15/17/18

Vendor `@ffmpeg/core` and `core-mt` 0.12.10 under `Ben.Video.Editor/wwwroot/js/ffmpeg-core/{st,mt}/`
resolved like `moduleLoader.js` does, delete the CDN retry loop (media-13, wasm-6). `web.config`:
`X-Frame-Options DENY`, `Content-Security-Policy frame-ancestors 'none'`,
`X-Content-Type-Options nosniff`, `Cross-Origin-Opener-Policy: same-origin` and
`Cross-Origin-Embedder-Policy: require-corp` so the multi-thread core can be selected (wasm-5/7),
plus `DisableCache` for `index.html` and `appsettings.json` (wasm-9); the editor publish writes
`build-info.json` and the smoke check demands the new stamp (wasm-8). `BearerTokenHandler`
refreshes before sending an expired token and only retries small bodies (wasm-15).
`VideoEditorOptions.SidecarDownloadUrl` (site `/editors/video/downloads/`, WASM `downloads/`
relative to `<base>`); `NativeSidecarPanel.razor:22` renders "Download the sidecar" in the
Disconnected/TokenRejected states with platform-neutral wording. `deploy-ishaunted.ps1:677-707`
rewrites the `href` in the deployed `downloads/index.html` to the format it staged and prints
which; the page gains the `.zip` fallback sentence. `docs/deploy-editor.md` describes both formats,
the vendored core, the headers, `SidecarDownloadUrl` and the empty-`WebApiBaseUrl` behaviour;
`publish-editor.sh` stops claiming a SPA rewrite (wasm-17). Help: "all of your CPU cores, outside
the browser's memory limits" replaces the hardware claim (also on `downloads/index.html:78-82`);
Preview is an in-page window; the Export dialog note reads "in your browser, or in the paired
sidecar on this computer". Tests: source-scan that `downloads/index.html` hrefs ⊆ the deploy
script's candidates, that the panel references `SidecarDownloadUrl`, that `ffmpegInterop.js` has no
`cdn.jsdelivr.net`, and that the help no longer says "video hardware".

### Phase 11 — Tests on real media (M) — F19, export-19, motion-16, titles-15, site-17, wasm-16, media-20, timeline-20

New `WasmEditorEditingTests.cs` on http://localhost:5180 (guarded, 1440×900), each after
Initialize → Ready: import `porch-camera.mp4`, `basement-evp.m4a` (audio track appears),
`site-photo.jpg` (bin card → Add to timeline); seek to 2 s and press S → cut at 2 s; Marker →
`.bv-marker-flag` aligned after a scroll; Callout, "+ Text", a transition between two clips;
Play → counter reaches the total and the image overlay shows; Export Now → destination prompt /
download with a `.mp4` suggested name, then `ffprobe` through the paired sidecar: duration equals
the timeline, an audio stream exists; Save → reload → chips restored and none missing; signed-out
Server tab shows the sign-in link; OPFS-wipe re-fetch; Escape/Cmd+Z behaviour. Extract testable
seams the sweeps named — `TimelineDragSession`, `EditorKeyMap`, `AssetFilter`, `OverlayPlacement`,
`MediaKindRouter`, `ImportPlacementPolicy`, `ProjectsDialogState`,
`ExportDestinationPromptState` — and unit-test them; `SidecarPreviewAssemblerTests`; a new
`Ben.Wasm.Video.Tests` project (stub handler + fake `IJSRuntime`) for `TokenStore`, `AuthService`,
`BearerTokenHandler`; `Ben.Web.Tests` for `VideoExportPublisher`, `BenProjectServerStore`,
`BenMediaLibraryProvider` and the two page flows. Manual walk with the big Downloads media (538 MB
.mov, 365 MB .mp4, 20 MB stills) to record where ffmpeg.wasm and OPFS quota give out, and put the
numbers in help.

### Phase 12 — Later: site→editor handoff and a true live compositor (M + L)

Handoff: `POST /api/auth/editor-handoff` [Authorize] issuing a 60 s single-use code;
`POST /api/auth/editor-handoff/exchange` [AllowAnonymous, auth rate-limit] minting bearer tokens
the way `/login` does; the site's "Open in standalone editor" link carries
`#handoff=<code>&project=<serverId>`; never relay the site's refresh token. **Sequence player
(D5)**: `PlaybackMode.Live` with a pure `TimelineSequencer.Resolve(t)`, two alternating `<video>`
elements fed from OPFS blob URLs, hidden `<audio>` per track, images and overlays drawn live, hard
cuts for transitions, per-clip fallback to the rendered proxy — the compositor Camtasia has and the
proxy approximates. Do this only after Phases 3–4 have made the proxy correct, because the proxy is
also what export verifies against.

### Recommended not to do

bUnit for the dialogs: every one is a `TelerikWindow`/`TelerikGrid` (Telerik 14.1) that needs a
root component, a licence and per-component JS stubs, and the repo already documents the
"logic in plain classes" rule. Keep that rule and put rendered behaviour on Playwright.

## Verification (every phase)

1. `dotnet build Ben.sln` with 0 warnings (a per-project build is not a solution build).
2. `dotnet test` for `Ben.Video.Tests`, `Ben.Web.Tests`, `Ben.Video.Sidecar.Tests`.
3. Playwright: `scripts/run-e2e.sh --keep` with `BEN_E2E_DB=<throwaway>` and all three hosts
   (api :5252, web :5078, wasm :5180; without the WASM host eight tests fail in ~90 ms), then
   `dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter Category!=Billing` or the editor
   classes directly.
4. Open the page. Walk the client-side host at 1440×900 and 1280×800 with the media table above:
   import each type, drag onto an occupied spot, split at 2 s, marker, callout, text, transition,
   play the whole timeline, Preview and cancel it, Export to machine and to server and probe the
   file, refresh, sign in, Server tab, Projects dialog, pair-sidecar panel link. Then the site pages
   signed in as an ordinary member (not SuperAdmin). Screenshot before/after for each phase into
   `ProjectNotes/`.
5. Help and deploy docs updated in the same PR; help screenshots re-captured where the layout
   changed; the Future-Improvements file records each closed id.

## Notes

- The full sweep output (243 findings with evidence and fix sketches, plus the verdicts) is in the
  workflow journal at
  `~/.claude/projects/-Users-ben-Source-Ben/a16949ca-610a-4b2c-9aca-6a6a17ec8768/subagents/workflows/wf_12945e28-397/journal.jsonl`;
  the first implementation step should copy the per-area lists into `ProjectNotes/` so they survive
  the session directory.
- Walk artefacts: `scratchpad/walk3/FINDINGS.md`, `scratchpad/media/` (copies of the Documents test
  media), `scratchpad/walk3/start.sh` (the isolated stack).
