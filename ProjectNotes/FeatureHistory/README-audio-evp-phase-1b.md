# Audio editor + EVP plan, phase 1b: WaveSurfer 8.0.3 and its own spectrogram (10/26/2026)

Phase 1b of `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, added at Ben's request. It comes after
the editor's own page and before the toolbar rebuild, so everything after it is written against
version 8.

## The upgrade

- **Vendored** 8.0.3 from npm's own package (`npm pack wavesurfer.js@8.0.3`) into
  `Ben.Web.Website/wwwroot/js/wavesurfer/`.
  - Plugins (Ben's list): regions, timeline, minimap, envelope, spectrogram, hover, plus zoom. The
    8.0.3 core has no wheel zoom of its own, so dropping zoom would have dropped wheel zoom.
  - Removed: `spectrogram-windowed` (a deprecated shim), `record` (unused), and our two spectrogram
    workers.
  - `VENDORED.md` rewritten, with every file's SHA-256; `HostAssetPathTests` pins the core hash.
- **Checked against the 8.0.3 source** in `/Users/ben/Documents/GitHub/wavesurfer.js`, not the docs:
  `getState`, `getVisibleRange`, `destroy()` being final, `AbortError` on a superseded load,
  `getMediaElement()` returning null on WebAudio, the regions `region-out` fix, and every
  spectrogram option. All match what the review session reported.
- **What changed in our script** (`Manage/Audio/WaveSurferPlayer.razor.js`) was small, because it
  uses WaveSurfer's public events, which v8 kept. Loads go through `_load()`, which treats a
  superseded load's `AbortError` as nothing to report. The MediaElement backend stays, because the
  listening chain hangs off the media element.
- **Not done, deliberately:** moving the whole script onto v8's signals and `Scope`. The public
  events still work, and rewriting 1,600 lines for its own sake would risk the listening chain and
  the drag code. Later phases use `getVisibleRange()` where an overlay needs it.

## The spectrogram

The hand-built spectrogram is gone: two workers, an OffscreenCanvas pre-render, and a fixed 128 px
canvas that redrew itself on every zoom and scroll. WaveSurfer's own `SpectrogramPlugin` replaces it,
drawn into a pane under the waveform:
- 160 px tall; windowed rendering for recordings over 10 minutes.
- The plugin's own frequency labels.
- Our Jet / Viridis / Inferno maps passed in as its 256-entry `colorMap`; Grayscale is its `gray`.

The six functions the editor calls keep their names and arguments, so no C# changed. Resolution,
color map and mel change in place through the plugin's `setOptions`; labels rebuild it. The voice
band (300–3400 Hz, the detector's band) is drawn on a thin overlay canvas through the plugin's own
mel formula. The edges are now firm dashed lines, because on an 8 kHz recording the band is most of
the picture and a tint alone barely showed. Saved settings carried over: a recording saved with the
spectrogram on opened with it on.

## Bugs fixed (Ben: fix any you come across, pre-existing included)

1. **Every candidate's and marker's Play button played to the end of the recording.**
   `playRegion`/`playRange` let a `seeking` event cancel the stop. WaveSurfer reports its own seek
   with the old position, so the stop cancelled itself. `_stopAtEnd` now judges each time update,
   and arms only once the playhead is inside the stretch. In the browser, a 2–4 s region ran to
   6.0 s before the fix and stops at 4.1 s after it.
2. **"Heard" (PlayedFirst, item 253) was set when playback entered a candidate's padded region**, so
   a neighbor half a second away was marked heard from its lead-in. It now counts once playback
   passes the candidate's middle, and a "heard" chip shows it. The new test
   `Playing_one_candidate_does_not_mark_its_neighbor_heard` failed on the old code.
3. **The EVP lab lost its styling in phase 1.** Its scoped `.razor.css` stayed on
   `AudioFilePreview` when the markup moved. It moved with the markup, and the EVP lab test now checks
   the panel's stylesheet is applied.
4. **The timeline never followed zoom and scroll**, and the new spectrogram pane would not have
   either. Both draw into boxes outside the waveform's scroll box. `_syncPanesToWaveform` gives them
   the waveform's scroll position (with a spacer, since they draw lazily) and keeps the frequency
   labels at the left. Measured: waveform, timeline and spectrogram all at 1,700 px, with the 5 s
   tick exactly where the waveform's 5 s is. New test
   `The_timeline_and_spectrogram_follow_the_waveform_when_zoomed`.
5. **The case Files tab "ignoring" its first click** (reported in phase 1). The cause was in the
   tests, not the site. `OpenOrgCaseAsync` returned as soon as the address changed, while on a cold
   host the group page was still showing. The tab finder took the group rail's "Files" link, which
   then vanished, and four clicks timed out on nothing. The helper now waits for the case's own
   Overview tab; `ClickUntilAsync` prints Playwright's call log.
6. **Closing a page with a player** also threw `TaskCanceledException`, the mid-shutdown form of
   phase 1's `JSDisconnectedException`. Both are caught now.

## Tests

- AudioEditor + AudioPlayer + CaseMixer + ContentPicker + the toolbar test: **28 of 28** on 8.0.3,
  with no unhandled exceptions in the web log.
- New: `The_timeline_and_spectrogram_follow_the_waveform_when_zoomed`,
  `Playing_one_candidate_does_not_mark_its_neighbor_heard` (which also asserts that Play stops at
  the candidate's end), and the EVP lab's stylesheet check.
- In the browser:
  - spectrogram on and off, labels off and on (one plugin wrapper left), mel in place, and the
    voice band's rows against the formula: linear 0–4 kHz at 48/294 against 48/296 expected, bark
    at 18/261 against 18/262, and 0–2 kHz with 300 Hz at 271 against 272;
  - a recording saved with mel on reopened with mel on.

## Still open

- **The new spectrogram dials** (scale, frequency range, window, gain/range, pre-emphasis, split
  channels) have a JavaScript entry point, `setSpectrogramAnalysis`, but no controls yet. They go in
  the Display tab that phase 2 builds, rather than making today's crowded toolbar longer.
- **The envelope plugin is vendored but not wired.** What it should do (a volume curve made into a
  new file, listening only, or later) is Ben's call.
- **The video editor's own WaveSurfer copy** (`Ben.Video.Editor/wwwroot/js/wavesurfer.esm.js`) is
  still the old version. Upgrading it is separate work.

## Deploy

Website only. No migration, no API change. Static files change under `wwwroot/js/wavesurfer/`.
