# Signal phase 6 — everything written and printed

Branch `feature/signal-phase-6-docs`, stacked on `feature/signal-phase-3-4-telerik-admin`.
Plan: `ProjectNotes/Signal-Redesign-Plan.md`, phase 6.

1. **Help screenshots and recordings** recaptured on the Signal UI through the existing harness —
   `BEN_CAPTURE=1 scripts/run-e2e.sh --filter "FullyQualifiedName~HelpMediaCapture"` (and
   `HelpMediaRecording` for the animations). Writes into `Ben.Web.Website/wwwroot/help/media/` and
   the embedded admin media in `Ben.Web.Services`. Every changed image is looked at, not taken on
   trust — a capture that ran against a half-loaded page is a broken help page.
2. **The product, persona and iOS PDFs** rebuilt from those screenshots (`docs/build-*.py`).
3. **The seven flyers restyled to Signal** and republished to the site (`docs/ads/build-ads.py`,
   `build-flyer.py`, `publish-to-site.py`). The ones already emailed or printed will no longer match.
