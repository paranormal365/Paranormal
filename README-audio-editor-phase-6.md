# Audio editor phase 6 — test close-out

Branch: `feature/audio-editor-phase-6-test-closeout`, cut from `master` after phase 7 merged.

## What had no tests

- **Twenty-two audio API calls**, none of which had a test of any kind. A route with a typo in it
  is a 404, and every one of these methods turns a 404 into `null` or an empty list — which the
  pages render as "no markers", "no notes", "no saved clips". Nothing throws and nothing is logged.
  `AudioApiRoutesTests` checks the address and the verb of each, plus the two contracts that carry
  meaning: a scan that found nothing is an empty list and a scan that did not happen is null, and a
  refusal written as a sentence reaches the caller while a framework blob does not.
- **`EvpDetectionOptions.Validate`** — six rules, no tests. They are the server's only defence
  against a hand-built scan request, and a scan with a minimum length longer than its maximum
  returns nothing, which on this site looks exactly like a finding.
- **The fine-tune panel's dials.** Every detector test ran one of the three presets, so a change
  that stopped `options` being read would have left them all green while the panel did nothing.
- **Regression E0** — the capture-phase `pointerdown` handler once claimed every press, so grabbing
  a region's edge drew a new region instead of resizing it. Move and resize were unreachable by
  mouse and nothing in the suite exercised it.

## Two tests that were never running

`AudioScrubModeTests` — the repo's only audio browser test before this audit — looked for a **link**
containing "Belmont". The case list renders a card with its own Open button and no such link, so the
lookup found nothing, the helper returned false, and both tests `Assert.Ignore`d on every run.
Reported as skipped, which nobody reads as broken. They had never once exercised the drag mode they
are named for. They use the maintained `OpenOrgCaseAsync` helper now and both pass.

## A defect the close-out found

Changing a setting and closing the editor lost the change. The colour-ramp picker fires its save
without waiting for it, and the modal's close handler reset every one of those fields before the
request had landed — so a save that arrived late wrote the **defaults** over what the person chose.
It only ever looked reliable because a hand-run test paused between the two. The close handler
awaits the save before resetting now.

## One thing tried and abandoned

Phase 3 noted that every test in `AudioEditorTests` uploads its own 7 MB recording to the same case,
which makes the last tests slow. Sharing one recording across the class does not work: a saved clip
draws an overlay on the waveform where it came from, so the next test to drag across that stretch
grabs the overlay instead of drawing, and four tests failed in an order-dependent pattern. The
upload stays per test. What was kept is the change that made the slowness matter less — no test
waits on the mere presence of something an earlier one could have left behind.

## Verification

Every new test run once against code with the thing it tests removed. `scripts/run-e2e.sh` on a
fresh database with the three audio fixtures: 15 passed, 0 skipped. Then the whole suite.
