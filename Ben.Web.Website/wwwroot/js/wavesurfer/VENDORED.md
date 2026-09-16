# wavesurfer.js — vendored

**Version:** 7.12.11
**Source:** https://github.com/katspaugh/wavesurfer.js (tag `v7.12.11`)
**SHA-256 (`wavesurfer.esm.js`):** `3956bf9f2bc6b93b9d68cef75961aff5774ca3d0c1dbde680745ad7a25b7fb84`
**Licence:** BSD-3-Clause — see `LICENSE` beside this file.
**Vendored:** built from source 2026-08 (audit item D4); provenance written down 2026-09-16.

## What this actually is

**Stock 7.12.11, built here rather than taken from npm.** That matters, because the bundle does not
match npm's own `dist/wavesurfer.esm.js` for any 7.x release — ours is 42,533 bytes, and every
published 7.x dist from 7.7.0 to the current 7.12.12 is smaller (25,934 → 41,736). Somebody
comparing hashes against unpkg will conclude it has been tampered with. It has not: it was built
from the 7.12.11 source tree with a rollup config written for Blazor
(`rollup.blazor.config.js`, `rollup-plugin-web-worker-loader`), which bundles differently from
upstream's own build.

The source tree was `Ben.Web.WebApp/wwwroot/ts/wavesurfer/` and went when that project did. It is
still in history and can be read or restored:

    git show 1762dfcb^:Ben.Web.WebApp/wwwroot/ts/wavesurfer/package.json
    git checkout 1762dfcb^ -- Ben.Web.WebApp/wwwroot/ts/wavesurfer

**The sources were never patched.** Three commits in the whole history touch that tree: the two
that added it and the one that removed it. So this is upstream's code, and replacing it with a
stock npm build is a real option rather than a gamble — it would need the worker files checked,
which is the one thing this build does differently.

## Why it is vendored rather than fetched

Audit item D4: both hosts used to pull it from unpkg on a floating `@7` tag, so a core feature
depended on a third party being reachable, behaved differently offline, and took whatever that tag
meant that day. Fixed 2026-08-16 (`8bfec2b8`).

## Why it lives in the HOST's wwwroot and not in the RCL

Deliberate, and the result of a real outage. `WaveSurferPlayer.razor.js` is in
`Ben.Web.Website.Library` and imports `/js/wavesurfer/wavesurfer.esm.js` by absolute path. When
`Ben.Web.WebApp` was removed on 2026-08-19 that folder went with it, and **every audio preview on
the site said "Player init failed" for four days** — nothing failed at build, and the error only
appeared inside the player, on click. The folder was restored here from
`git show 1762dfc^:...` on 2026-08-23 (item 175).

So: do not "tidy" this into `_content/`. If it ever does move, the import in
`Manage/Audio/WaveSurferPlayer.razor.js` moves in the same commit, and
`HostAssetPathTests` is the test that will tell you if it did not.

## Updating it

Either rebuild from the source tree above at a newer tag, or switch to a stock npm dist and check
the spectrogram still draws — that is the part that depends on how the workers are bundled
(`spectrogram-worker.js` and `spectrogram-draw-worker.js` sit beside the bundle here). Update the
version and hash above either way, and open an audio preview before believing it works: a broken
module load looks like a working page until somebody clicks play.
