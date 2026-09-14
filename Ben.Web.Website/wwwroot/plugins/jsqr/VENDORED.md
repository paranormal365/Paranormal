# jsQR — vendored

**Version:** 1.4.0
**Source:** https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js
**SHA-256:** `bc40c8a15196236b2314db0856f72ca0b49980cd5413b8c852a7349f5fee0859`
**Licence:** Apache-2.0 — see `LICENSE` beside this file.
**Vendored:** 2026-09-13 (item 235 phase 7)

## Why there is a QR decoder here at all

The door screen scans a guest's pass. Where the browser has `BarcodeDetector` — Chrome and Edge
on any platform, and Android's WebView — nothing here is loaded at all: the platform's own decoder
is faster, cheaper and already installed.

**Safari has no `BarcodeDetector`**, and an iPhone is the single likeliest thing to be held at a
door. Without this, every iPhone would fall back to typing a six-character code by hand for every
party — which works, and is the difference between a queue that moves and one that does not.

## Why it is vendored rather than fetched

The same reason Fabric and ApexCharts are: a third party on the critical path of a screen somebody
is using in a hotel lobby with two bars of signal, plus a CDN told about every scan. A venue on a
restricted network would find the door simply did not work.

## How it loads

`Kit/Scan/BenQrScanner.razor.js` injects it **once, on demand**, and only after finding that
`BarcodeDetector` is missing. Nobody who never opens the door screen fetches it, and neither does
anybody whose browser can already decode.

## Updating it

Replace `jsQR.js`, update the version and hash above, and re-run `EventDoorTests`. The module uses
exactly one export — `jsQR(imageData, width, height, options)` — so a new major is unlikely to
matter, but check that it still returns `{ data }` for a decoded code.
