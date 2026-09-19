# wavesurfer.js — vendored

**Version:** 7.12.11
**Source:** https://github.com/katspaugh/wavesurfer.js (tag `v7.12.11`)
**SHA-256 (`wavesurfer.esm.js`):** `3956bf9f2bc6b93b9d68cef75961aff5774ca3d0c1dbde680745ad7a25b7fb84`
**Licence:** BSD-3-Clause — see `LICENSE` beside this file.
**Vendored:** built from source 2026-08 (audit item D4); provenance written down 2026-09-16.

## What this actually is

**Stock 7.12.11, built here rather than taken from npm — and the same code either way.**

Its SHA-256 does not match npm's `dist/wavesurfer.esm.js` for 7.12.11, so a hash check against
unpkg fails and looks alarming. It is not alarming. Compared byte for byte:

| | ours | npm 7.12.11 dist |
|---|---|---|
| size | 42,533 | 42,771 |
| exports | `export{w as default}` | same |
| worker handling | one `createObjectURL`, no `new Worker` | same |
| distinct string literals | 110 | 110 |

The 238 bytes are minifier output, not code: this build's terser omits the redundant parentheses
around function expressions that npm's build emits (`(function(…)` against `((function(…)`). The
one string literal that differs between the two files differs only by that same pair of brackets,
inside a snippet of code held as a string.

So **replacing this with npm's own 7.12.11 dist is a swap of identical code**, not a gamble. It
was built here because the source tree was checked out for a Blazor rollup config
(`rollup.blazor.config.js`, `rollup-plugin-web-worker-loader`) — but the result is stock.

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

Take npm's own dist for the version you want —
`https://unpkg.com/wavesurfer.js@<version>/dist/wavesurfer.esm.js` — and update the version and
hash above. The comparison in the table was
run to establish that this is a plain swap; there is no local patch to carry forward. The worker
files beside the bundle (`spectrogram-worker.js`, `spectrogram-draw-worker.js`,
`noise-gate-processor.js`) are separate assets and are not inside it, so check them against the
same release.

Then open an audio preview before believing it works: a broken module load looks like a working
page until somebody clicks play.
