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

## The fixture moved out of the case

Phase 3 noted that every test in `AudioEditorTests` uploads its own 7 MB recording to the same case,
which makes the later ones slow. Sharing one recording across the class was tried first and does not
work: a saved clip draws an overlay on the waveform where it came from, so the next test to drag
across that stretch grabs the overlay instead of drawing. Four tests failed in an order-dependent
pattern.

What does work — Ben's suggestion — is to leave the case behind. The tests are about the editor, and
the case Files tab draws a waveform for **every** audio file on the case: eleven files was enough to
push the twelfth past a minute. The media library draws them on demand, one tap at a time. Each test
now uploads under a name of its own to Sarah's library and opens the editor from that card, so it is
always working on a recording it owns. The fixture went from ninety seconds to forty-six on a case
that already held eleven recordings, and from "one of these will be somebody else's file" to never.

Getting there needed three things the old helper hid: the upload panel is behind its own button, a
file type has to be chosen, and the page asks before making a second file of the same name — the
upload sits at "Waiting" behind that dialog for ever, which is what a silent three-minute timeout
had been.

## A second defect, found by the move

Once the tests were working on a file Sarah definitely owned, the save was **still** refused — and
the panel still said "this recording isn't yours to change". It was a 500: two saves a second apart
both found no row and both inserted, and the second hit the one-to-one unique index.

Three fixes. The component serialises its saves so two are never in flight, which is the actual
prevention. The server recovers from the duplicate by re-reading and updating, for any other client.
And the message stops asserting a cause it does not know: when the server sends nothing readable —
which covers a validation failure and a server error as much as a refusal — it says the save did not
happen and offers ownership as a possibility rather than a diagnosis.

## Verification

Every new test run once against code with the thing it tests removed. `scripts/run-e2e.sh` on a
fresh database with the three audio fixtures: 15 passed, 0 skipped. Then the whole suite.
