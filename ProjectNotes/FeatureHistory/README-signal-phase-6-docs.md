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

## Done (2026-10-02)
- Help: ~170 web screenshots recaptured on Signal (two runs; the second after group pages got the slim
  band and help pictures stopped showing the operator's "Work waiting" card). Nine stay on their
  previous version because this database lacks their subject: checkout payment (Stripe test keys),
  the tour-launch feed card and tour-date field sessions (the item-252 capture world), reading a
  sent letter, investigating alone, sidecar telemetry, the field-session player, and the dietary
  sheet at both widths (needs confirmed bookings with dietary notes). The iPhone/iPad screenshots
  are the app's and did not change.
- The seven per-role web guides recaptured and rebuilt (`docs/IsHaunted-Web-*.pdf`).
- The product documentation PDF is printed from JPEG copies (`docs/.print-media/`, ignored):
  recaptured, the PNGs made it 105 MB — past GitHub's 100 MB file limit. Now 22 MB (was 46).
- Flyers: six ads, the whole-platform flyer and the hosted-events brochure rebuilt in Signal and
  published to the site; the six walk pictures they use retaken by `FlyerPageShots`.
- `/changes`: 2.13.0 for the redesign, and the 2.12.0 entry restored to a heading of its own — it
  had been pasted into the file's instructions and never showed.
