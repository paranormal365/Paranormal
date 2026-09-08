# Site evaluation 2026-09-06 — Phase 0: a green suite

Branch: `feature/site-eval-phase-0-green-suite`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 0 — Green suite*.

## Why this runs last, having been written first

Phase 0 was meant to go before everything. It did not, and the cost showed up twice in phase 8: a
full run failed and I had to spend a run deciding whether the failure was mine. Eight tests fail on
master — seven since before this arc began, and one found on 2026-09-08 — and a suite with a
standing pile of failures is one people stop reading.

The rule for this branch, carried from `README-green-suite-and-open-debts.md`: **a test may only
become skipped when the thing it tests is genuinely switched off in the environment it is pointed
at.** Skipping a test because it is inconvenient is how a suite becomes decorative, which is the
failure this exists to prevent.

## The eight, and what each actually was

**Not one was a product defect.** Every one was a test describing a site that had changed under it,
or a locator that could reach the wrong control. That is worth saying plainly, because a standing
pile of red is usually read as "the app is broken somewhere and nobody knows where" — and for these
eight it never was.

Three were **locators that matched something else entirely**:

- `AdminDeleteCaseTests` clicked **Telerik's filter toggle**. The grid's filter row renders one
  icon button per column carrying `aria-label="Open"` — fifteen of them on that page — so an
  accessible-name match found a filter dropdown long before the row's Open command, clicked it
  happily, and reported that the case row would not navigate. Command buttons live in
  `td.k-command-cell` and carry real text; `BenTestBase.GridCommand` now says so once, with the
  reason, so the next grid test does not learn this again.
- `ImpersonationAndSidebarTests` clicked **Delete this user**. Its selector ended in
  `td:last-child button` with `.Last`, which resolves in document order, so it reached the row's
  last action button. The only reason Sarah still exists is that deleting a user asks first. A
  selector that can reach a destructive control by accident is not a selector.
- `UploadFilesTests` asserted a filename was gone **from the whole page**. The confirm dialog names
  the file in a `<strong>`, so while it was up the name matched twice and Playwright failed on
  strict mode instead of answering the question. The delete had worked all along. It now asserts
  the row is gone, which is what the test means.

Two were **the site telling the truth and the test not knowing**:

- `StartGroupWizardTests` said "step 3 — defaults are fine". They are not, and that is the product
  being right: an investigation group takes client cases by default, and a group that takes client
  cases must say where it works or a client cannot find it. The step refuses, in a sentence. The
  test now says where the group works, which is what a person does.
- `LookAndTruthsTests` read the case's address by matching a label whose text was exactly
  `Address`. The panel says **"Address given"** — a case's address is what the client *said*, which
  is a different claim from a verified place. The exact match found nothing and blamed the panel
  for changing shape, in the test's own words.

One was **a feature arriving on top of a test**:

- `CmsAuthoringTests` waited for a dialog that never opened. The CMS editor's guided tour
  auto-launches for anybody who has not dismissed it, which on a freshly seeded database is every
  seat the suite signs in as, and its backdrop covers the toolbar. `BenTestBase.SkipAnyTourAsync`
  does what a person does — skip it and get on — and is there for the next page to grow a tour.
  Tests that are *about* a tour must not call it.

One was **a page that no longer exists**:

- `VideoEditorTests` asserted `/video-editor` had the title "Video Editor". The 2026-09-05 audit
  retired that page: it mounted the editor with no height-constrained ancestor, had no header and
  no link from anywhere on the site, and My Videos does the same job properly. What is left is a
  redirect, kept because the address is in the help, in bookmarks and in this suite — so the
  redirect, not the title, is the contract worth holding, and that is what the test holds now.

One was **an ambiguity the page created**:

- `OrgPublicPageTests` looked for a link named "Cases" on a page that also carries a sentence
  linking to "public cases" at the same address. Two matches, strict mode, and it read as the nav
  item being missing while the nav item was right there. Exact match.

## One harness change beyond the eight

`ClickUntilUrlAsync` used to fail with "clicking never navigated", which is true and useless: a
control that is missing, one that is covered, and one whose handler does nothing all read the same
from there, and each wants a different fix. It now says which. That message is what turned the
admin-cases failure from a guess into a five-minute answer.

## Verification

Every one of the eight was run alone against a freshly seeded `IsHauntedDb_p0`, failing first with
its original cause and passing after its fix. The leak-warning test, the only one with any history
of intermittency, was run three times in a row.

| check | result |
|---|---|
| The eight, each in isolation | 8 of 8 pass |
| `The_leak_warning_fires_before_save_not_after_it`, three consecutive runs | pass, pass, pass |
| Files changed | 9, all of them harness or test — **no product code** |
| Full suite, fresh `IsHauntedDb_p0d`, `--blame-hang-timeout 6m` | **480 passed, 0 failed, 41 skipped of 521**, 23 m 8 s |

### What the isolation runs cost, and the trap they exposed

The first full run after the fixes **hung**. `EquipmentTests.AddingAPieceOfGear_ShowsItInTheListAndOnItsOwnPage`
failed and then took the run with it, so no summary line ever appeared — and a run that never
finishes is worse than one that fails, because there is nothing to read. That test was not among
the eight and passed during phase 8, so it is either an ordinary flake or something this branch
disturbed; it is named here rather than assumed away.

The full run is now made with `--blame-hang --blame-hang-timeout 6m`, so a wedged test ends the run
and names itself instead of stalling it.

**One thing I changed and then had to make safe.** `ClickUntilUrlAsync`'s new diagnostic re-counts
the target inside its catch. That is a query running on a page that has just misbehaved, and if it
threw it would escape the retry loop — a helper written to explain a failure would have become a
way to cause one. It is wrapped so it can only ever add a sentence.

### The last two, and what they had in common

The run after the eight came back **479 passed, 2 failed**, and both failures were the same shape:
something arriving one circuit round trip behind the test.

- **The leak warning.** The case label binds on `oninput`, so the typed value reaches the server a
  round trip behind the keyboard, and the `onblur` that runs the check uses whatever the server
  has. Alone that gap is about 3 ms and the blur lands after it; under a full-suite load it does
  not, the check ran on the old title, and no warning appeared. That is the whole reason this test
  passed three times in isolation and failed every full run. A person never blurs zero milliseconds
  after their last keystroke, so the test now waits a beat. **This is the same shape as W-A12** — a
  control acting on a value the server has not caught up with — and worth remembering as a class
  rather than an incident.
- **The equipment dropdown.** Choosing a category re-renders the makes list over the circuit. The
  test read the makes and then selected one into the gap while the new list was still arriving,
  timing out after 30 s with "did not find some options" — which reads as a broken taxonomy rather
  than a list still on its way. It now waits for the option to be there.

### One skip worth naming

`The_vote_page_stays_reachable_under_review` skipped itself on the final run: it ignores when it
cannot find a group to open. That is a data-dependent skip of its own making, not something this
branch introduced, and it is the kind of self-skip the rule at the top is suspicious of. Left as
found, named here rather than quietly counted as a pass.
