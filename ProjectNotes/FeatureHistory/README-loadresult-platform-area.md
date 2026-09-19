# Item 120, slice 3 — the platform area

Organization (2026-08-21) and case (2026-08-22) are converted; the ratchet stands at **81**. This
branch does the platform slice: **14 methods** across 13 consumers, taking it to **67**.

## Why platform next, and not the biggest pile

Equipment has more (22), but its empty states are already the best-written on the site — it
distinguishes "no gear yet" from "you may not add gear", and was the model `BenListState` copied.

Platform holds the **internal messaging** surfaces — `GetMyMessagesAsync`, `GetOrgInboxAsync`,
`GetOrgSentAsync`. Item 120's own text names the phase-5 messaging faults as one of the things this
mechanism hid, alongside the item-119 bugs. An inbox is also the worst possible place for the
confusion: "no messages" and "we would not let you read your messages" look identical, and the
reader's natural conclusion — nobody has written to me — is the wrong one.

It also holds the calendar (events, types, attendees), the experience taxonomy, lookup types, the
audit log's entity filter, and sidecar telemetry.

## Rules carried forward from the case slice

1. **Convert the return type, never add a `LoadXAsync` beside it.** A parallel method doubles the
   interface and leaves the old one in place as the trap it already is.
2. **A list mutated in place cannot be wrapped in `BenListState`.** Where a page does `Insert`,
   `Add` or `RemoveAll`, the wrapper keeps rendering the *load's* emptiness and the first item
   added never appears. Those branch on `.Failed` beside the existing empty check.
3. **Invert the old tests, don't patch them to compile.** Six in the case slice asserted that a
   refusal "returns empty" — green tests defending the bug.
4. **Add the new method names to `LoadResultRenderedGuardTests.ConvertedMethods`**, so a page that
   can now see a refusal cannot quietly drop it.

## Found on the way in

`GetPublishedInvestigationsAsync` is declared on `IBenPlacesClient`, implemented here, and **called
by nothing**. A public endpoint listing an organization's published investigations, with no UI that
ever asks for it — the same write-only shape found in areas 4, 9 and the CMS work. Converted with
the rest and logged rather than quietly deleted.

## Done when

- `BenAdminClientAdapter.Platform.cs` has no `?? []`
- `SwallowedFailureRatchetTests.Ceiling` lowered 81 → 67
- Every converted list surface renders the failure, or is a recorded decision
- Zero-warning build, full suite green
