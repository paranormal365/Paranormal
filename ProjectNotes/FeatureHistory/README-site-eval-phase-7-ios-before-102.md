# Site evaluation 2026-09-06 — Phase 7: the iOS app before 1.0.2

Branch: `feature/site-eval-phase-7-ios-before-102`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 7 — The iOS app before 1.0.2*.

## The problem

The evaluation walked the phone as a group member and found one gap and four things the app says
badly.

- **iOS-8 (gap).** The app has no group-side case view at all. My Cases is the *client's* list, so
  a member rostered on a visit cannot open the case from the phone.
- **iOS-2.** My Cases shows a group member client copy: "When you ask a group to look into
  something, it appears here."
- **iOS-1.** The Events tab is hidden for a member who does not yet attend an event. Profile →
  Public events is the door, but the App Store review notes say "six tabs", and a reviewer
  following them may not find it.
- **iOS-4.** Field Kit list: an untitled session prints its timestamp twice, as title and subtitle.
- **iOS-5.** The review screen leaves the chart blank and shows "—" for the whole session when no
  base level was set, with the only hint on the live screen.

## What each one turned out to be

### iOS-8 — the gap, and the only place the app knew both ids

A member rostered on a visit now opens the case from the visit: **Investigations → the visit → the
case**. What the client reported, the timeline, the files on the case, and the group's thread with
them.

**Read-only on the phone.** A phone in a dark house is for looking something up. Writing to a case
is a desk job, and every write carries a permission the app would have to model correctly before
offering a button for it.

**No new API.** It reads the three org endpoints the website's case page already reads, through
Swift models that decode only the fields shown — Swift ignores keys it was not asked about, so
there is no second contract to keep in step and a field added on the server cannot break the phone.

**The door is offered only when the server already said yes.** `MyInvestigation.caseId` is nulled
by the roster endpoint for anybody who cannot open the case (phase 2, W-M1), so the presence of the
id *is* the permission. A door that is drawn and then refuses is worse than no door.

### iOS-1 — by design on a phone, and the review notes were wrong about it

`AppSection.compactTabs` lists five sections and Events is deliberately not one of them: the iPhone
tab bar holds Feed, My Cases, Investigations, Field Kit and Profile, and public events live on
Profile. The `attendsPublicEvents || hasGroups` rule that mentions Events is only ever reached on
iPad.

Verified on both: the iPad sidebar shows all six with Events among them; the iPhone shows five and
does not. So the app is right and **the review notes were not** — they said a reviewer "sees all
six", which is true only on an iPad. A reviewer on an iPhone following that sentence would look for
an Events tab that is not there, which is exactly the risk iOS-1 named. §4 item 7 now says where
Events actually is on each device.

### iOS-2, iOS-4, iOS-5 — three things said badly

- **iOS-2.** My Cases told a group member "when you ask a group to look into something, it appears
  here". They have cases; the cases are somebody else's, and they reach them through the visit. The
  empty state now says that, and only to somebody who is in a group.
- **iOS-4.** `FieldSessionSummary.title` fell back to the session's start time, and the row's
  subtitle *begins* with the same start time — so every untitled session printed its timestamp
  twice. The fallback is now "Untitled session", dimmed and italic. Nothing is lost: the date is in
  the subtitle, beside the length and the counts a reader is scanning for anyway.
- **iOS-5.** With no base level the chart is an empty grid with an axis, which reads as "nothing
  happened" — a claim about the night rather than about the setup. It now says **No base level was
  set** over the chart and explains what the line would have measured, and the Field readout says
  "no base" instead of "—", which previously meant two very different things.

### Fixture drift — the class iOS-3 belonged to

`FixtureNullDriftTests` takes each fixture, sets every key in turn to null everywhere it appears,
and decodes again. A key that breaks the whole payload is one the app *requires* the server to
send — a real claim, sometimes correct, and worth writing down. The per-fixture list is that claim,
and every name on it was checked against its C# record when it was added.

The first run reported exactly the fields that are non-nullable server-side and nothing else, which
is the answer you want: the models are in step with the contract today. Reverting `didAttend` to a
non-optional `Bool` fails it immediately, which is iOS-3 exactly.

## Key files

| what | where |
|---|---|
| The group's side of a case | `BenKit/Sources/BenKit/Models/Cases/GroupCaseRecords.swift`, `Stores/GroupCaseStore.swift`, `IsHaunted/Features/Cases/GroupCaseView.swift` (all new) |
| The door to it | `IsHaunted/Features/Investigations/InvestigationDetailView.swift`, and the `orgCase` route in `RootShell` |
| HTML bodies on a phone | `BenKit/Sources/BenKit/Formatting/PlainText.swift` (new) |
| Untitled sessions | `BenKit/Sources/BenKit/Field/FieldSessionSummary.swift` |
| No base level | `IsHaunted/Features/FieldKit/ReplayViews.swift`, `SessionReviewView.swift` |
| The fixture guard | `BenKit/Tests/BenKitTests/FixtureNullDriftTests.swift` (new) |
| Where Events is | `Ben.iOS/APP-STORE-1.0.2.md` §4 item 7 |

**No migration, and no server change at all.**

## Verified

| check | result |
|---|---|
| `scripts/build.sh` | BUILD SUCCEEDED |
| BenKit (`scripts/test.sh`) | 330 passed, 0 failed (12 added) |
| `EverySurfaceUITests` on iPhone 17e, as the member seat | 8 passed, 0 failed |
| `EverySurfaceUITests` on iPad Pro 11-inch (M5), as the member seat | 8 passed, 0 failed |
| The four group-case endpoints, as the member seat | all 200 — the case, 3 timeline entries, 0 files, 0 messages |
| iOS-1, on both devices | the iPad sidebar carries Events; the iPhone tab bar does not |
| Each new test against the un-fixed code | `didAttend` reverted to non-optional: `FixtureNullDriftTests` fails on `[1].didAttend`; the group-case walk failed against the un-fixed view (see below) |

### The defect the new UI test caught on its first real run

`GroupCaseStore` was written from the shape of the stores beside it and **lost the `@Observable`
attribute**. SwiftUI therefore never watched it: the screen rendered its first frame — the spinner
— and never redrew. The test reported "the group case screen opened but drew neither the case nor
a reason", which is what a permanent `ProgressView` looks like from the outside. Every other store
in that folder carries the attribute.

That is the whole reason the walk was worth writing rather than trusting a build that compiled.

### Two mistakes in the test itself, both found by probing rather than assuming

- **It tapped the wrong row.** `app.cells.firstMatch` on Investigations is the "Where you've been"
  map card, so the tap went nowhere and the test then reported "this visit has no case" about a
  visit it had never opened. It now taps `investigation-row` by identifier and asserts it reached
  the detail before looking for anything else.
- **It queried the wrong element type.** A SwiftUI `Button` with `.buttonStyle(.plain)` inside a
  `List` surfaces as a *cell*, not a button, so `app.buttons["open-group-case"]` found nothing.

## The simulators, and what it cost

CoreSimulator wedged repeatedly with *"Application failed preflight checks / Busy"*, refusing to
launch the test runner. Shutting every device down, restarting the CoreSimulator service,
uninstalling both apps and rebuilding the runner did not clear it; **erasing the device did**, and
Ben chose to erase one spare (iPhone 17e) rather than all of them.

The reliable recipe, once found: `simctl shutdown all`, then **boot the target device explicitly
and wait ~25 seconds** before `xcodebuild test`. Letting xcodebuild boot it is what wedges.

One run was also read wrongly: `xcodebuild` reused a stale UI-test build after the source was
restored from a backup, so a removed probe still fired. `touch` on the file before the run settles
it.
