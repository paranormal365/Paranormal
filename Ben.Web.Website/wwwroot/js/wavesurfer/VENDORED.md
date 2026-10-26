# wavesurfer.js — vendored

**Version:** 8.0.3
**SHA-256 (`wavesurfer.esm.js`):** `4cc51ddaf880c7512456d5b62ef4ea836e5dd5b5967d042fa57d6588374d5aa0`
**Source:** npm's own build: `npm pack wavesurfer.js@8.0.3`, files copied from the package's `dist/`.
Upstream: https://github.com/katspaugh/wavesurfer.js (release `8.0.3`, 2026-10-19).
**Licence:** BSD-3-Clause, in `LICENSE` beside this file (unchanged from 7.12.11, compared byte for byte).
**Vendored:** 10/26/2026, phase 1b of `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`.
Previously 7.12.11, built from source in 2026-08 (audit item D4); that history is below.

## What is here

Stock files only; nothing is patched. Each one is self-contained, with no imports. The spectrogram
plugin builds its worker from a blob URL, which the site allows because it sends no
Content-Security-Policy beyond `frame-ancestors` (`Ben.Web.Website/Program.cs`).

| File | SHA-256 |
|---|---|
| `plugins/regions.esm.js` | `e073d6aea2fcb0e12076404e5a9c78ce377e28b40fe466f17dc0b1a2942081bb` |
| `plugins/timeline.esm.js` | `7e4818cb791da7acef97682e47928b2a1b64cb2a4526e0e2cf74fd23356cc2c2` |
| `plugins/minimap.esm.js` | `bb361e4e94a778638524e43cc7ff1ef790ff624d9273fdb3ae4a100d04a1d4d0` |
| `plugins/envelope.esm.js` | `d3fd27c019b926864f81adcda6b1ed5664d22f50d3c80bd93b2bcd31ed7d388a` |
| `plugins/spectrogram.esm.js` | `ca90f92fa498a0ebb948bf51885c2bdfedbf99aa1a7577f25d11a1feea8dee3e` |
| `plugins/hover.esm.js` | `9283029beeced107abb6a955c53e8ed7e2036c3de23020ccd90470303722b9dc` |
| `plugins/zoom.esm.js` | `7cf75a4caa3c0671f9ae6f4cc07d65e52e288915b16bd8071f60808d9a767193` |

**The plugin set is Ben's list (10/26/2026):** regions, timeline, minimap, envelope, spectrogram,
hover, plus zoom.
- **Zoom** was not on the list but is kept. The 8.0.3 core has no wheel handling, and only
  `src/plugins/zoom.ts` gives wheel zoom, so leaving it out would quietly drop it.
- **Removed:** `spectrogram-windowed.esm.js`, a deprecated shim for
  `SpectrogramPlugin.create({ rendering: 'windowed' })`, and `record.esm.js`, which nothing uses.

The source for checking what any of this does is `/Users/ben/Documents/GitHub/wavesurfer.js`
(8.0.3 + 3 commits). Read the code there, not the docs.

## The other files here are ours

`noise-gate-processor.js` is the listening chain's AudioWorklet, and is ours. The hand-built
spectrogram's two workers (`spectrogram-worker.js`, `spectrogram-draw-worker.js`) were deleted in
phase 1b, when the stock plugin replaced them; they are in history before that commit.

## Why it is vendored rather than fetched

Audit item D4: both hosts used to pull it from unpkg on a floating `@7` tag. A core feature
therefore depended on a third party being reachable, behaved differently offline, and took
whatever that tag meant that day. Fixed 2026-08-16 (`8bfec2b8`).

## Why it lives in the HOST's wwwroot and not in the RCL

This placement is deliberate, and comes from a real outage. `WaveSurferPlayer.razor.js` is in
`Ben.Web.Website.Library` and imports `/js/wavesurfer/wavesurfer.esm.js` by absolute path. When
`Ben.Web.WebApp` was removed on 2026-08-19, this folder went with it, and **every audio preview on
the site said "Player init failed" for four days**. Nothing failed at build; the error only appeared
inside the player, on click. The folder was restored here on 2026-08-23 (item 175).

Do not "tidy" this into `_content/`. If it ever does move, the import in
`Manage/Audio/WaveSurferPlayer.razor.js` moves in the same commit; `HostAssetPathTests` is the test
that will tell you if it did not.

The video editor carries its own, older copy (`Ben.Video.Editor/wwwroot/js/wavesurfer.esm.js`,
loaded by `waveformInterop.js`). Upgrading that one is a separate job.

## Updating it

1. `npm pack wavesurfer.js@<version>` into an empty folder, and copy `dist/wavesurfer.esm.js` and
   the plugins above from it.
2. Put the new core hash first in this note: `HostAssetPathTests` reads the first 64-character hash
   here as the core bundle's. Update the plugin table too.
3. Read the release notes' breaking changes against `WaveSurferPlayer.razor.js`.
4. Open an audio preview before believing it works. A broken module load looks like a working page
   until somebody clicks play.

## 7.12.11 (history)

7.12.11 was built here from source rather than taken from npm, and was the same code either way.
Its SHA-256 differed from npm's dist only because this build's minifier dropped redundant
parentheses, which made the file 238 bytes smaller. Its source tree was
`Ben.Web.WebApp/wwwroot/ts/wavesurfer/`, still in history at `1762dfcb^`.
