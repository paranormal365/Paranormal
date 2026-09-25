# Fabric.js — vendored

**Version:** 7.4.0 — the bundle's own version string, and `fabric.version` in the browser.
This note said 6.9.1 until 09/25/2026; the file here is 7.
**Source:** https://cdn.jsdelivr.net/npm/fabric@7.4.0/dist/index.min.js — byte-identical
(SHA-256 `d4e908b3b3654db92c08ff0f31aa6a72ba1ddeeeb43a93bc41c833e51fe37f58`, checked 09/25/2026)

**Licence:** MIT — see `LICENSE` beside this file.
**Vendored:** 2026-08-21

Fabric 7 places new objects by their centre (`originX`/`originY` default to `'center'`), where 6 used
the top-left corner. `js/image-editor.js` sets the default back to top-left when it loads Fabric,
because every shape it draws starts at the pointer.

## Why it is here rather than on a CDN

`App.razor` used to load Fabric from jsdelivr with a `<script defer>` tag **on every page of the
site** — the sign-in page, the public microsite, every case screen. Nothing on any of those pages
uses it. Only the image editor does.

That cost every visitor a DNS lookup and TLS handshake to a third party before `load` fired, put a
third party on the critical path of a site that may be opened on a restricted or air-gapped
network, and told a CDN about every page view. It also made the first navigation of a Playwright
run time out intermittently at 30s — which is how it was noticed (item 114).

Same treatment ApexCharts already gets under `plugins/apexcharts/`.

## The version was floating

The old tag was `fabric@6`, which resolves to whatever the latest 6.x happens to be **at request
time** — so the library could change under the site without a commit. This is pinned to 6.9.1 and
verified byte-identical to that exact version.

npm's `latest` is 7.x. Staying on 6 deliberately: the image editor module below is written against
the v6 API, and v7 is a separate upgrade with its own testing, not a side effect of vendoring.

## How it loads now

`wwwroot/js/image-editor.js` injects this script itself, once, the first time an editor is
initialised — so it is fetched only by people who actually open the image editor, and never by
anyone else.

## Updating it

Replace `fabric.min.js`, update the version above, and re-run the image editor tests. The module
uses `Canvas`, `Image`, `Line`, `Group`, `Text`, `IText`, `Ellipse`, `Rect`, `Circle` and the
`filters.*` classes; check those still exist before trusting a new major.
