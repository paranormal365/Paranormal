# Item 120, slice 2 — the case area learns to say "I couldn't load this"

The organization area was converted first (merged 2026-08-21). This branch does the same for the
case area: the surfaces a **client** sees, which is where being refused and being empty look most
alike and matter most.

## The mechanism, once more

`GetAsync` answers any non-2xx with `default`, and the adapter follows it with `?? []`. A 403, a
500 and a genuinely empty list arrive at the component as the same value, and every list surface
renders the same sentence — *"No records available"*. The server refuses correctly; the page tells
somebody their case has no files.

Three bugs on 2026-08-20 (items 119, 122) and the blank admin pages on the deployment (item 126)
were all this.

## Why the case area next, and not the biggest one

Equipment has more call sites (22 vs 20), but equipment refusals are mostly *"you lack the
Equipment permission"* inside your own group, and its empty states already distinguish the two.
The case area is where a **client** — someone with no other window into the system, no logs, and
no second seat to compare against — is refused: files, reports, messages, co-clients, invites,
related people. If a case page says the case has no reports, the client has no way to find out
that it does.

## Scope

All 20 swallowing methods in `BenAdminClientAdapter.Case.cs`, their declarations in
`IBenCaseClient` / `IBenPlatformClient`, and every consumer.

Converting a method means **changing its return type**, not adding a `LoadXAsync` beside it — a
parallel method would double the interface and leave the old one in place as the trap it already
is.

Consumers fall into three groups:

1. **List surfaces** — wrapped in `BenListState`, which renders loading / couldn't-load / empty as
   three different things.
2. **Decorations** (vote summaries on cards, unread counts) — read `.Items` and move on. A failed
   lookup that only greys out a badge should not put a warning panel on the page.
3. **Feeders** — a list fetched to populate a dropdown or a count. These take `.Items`, but where
   the surrounding page is itself a list, the failure is propagated rather than dropped.

## Done — what shipped

- `BenAdminClientAdapter.Case.cs` has no `?? []` (20 methods converted)
- `SwallowedFailureRatchetTests.Ceiling` lowered 101 → 81
- 19 consumers updated; 16 render the failure, 3 are recorded decisions
- `CaseMessageThread` — the shared client↔org thread — takes a `LoadResult` delegate, so a refused
  thread no longer says "No messages yet" to somebody whose group has written to them
- **Six adapter tests inverted.** They asserted a refusal "returns empty", which made them green
  tests defending the bug rather than tests that would have caught it
- New `LoadResultRenderedGuardTests` (below)
- Zero-warning build; 5,045 tests green

### The guard, and why it is not the one this README promised

The plan said a test that feeds a surface a refusal and asserts the page does not print the
empty-state sentence. **There is no bUnit in this solution**, so rendering a component in a test is
not available, and adding a UI-test framework is not a thing to do incidentally in the middle of a
conversion.

What shipped instead follows the convention this codebase already uses for Razor rules — a source
scan, like `DateFormatSourceGuardTests` and `NoTelerikDialogTests`. It requires that any `.razor`
calling a converted method mentions `BenListState` or reads `.Failed`. That is a deliberately low
bar: it cannot check that the *right* thing renders, only that the failure was not dropped without
a decision. The half-conversion it stops is the likely one — fixing the compile error with
`.Items` and leaving the page exactly as wrong as before, while the ratchet records progress.

Verified to discriminate: regressing `CaseFiles.razor` to the half-converted shape fails it.

### Two things worth keeping

**Lists that are mutated in place cannot be wrapped.** Where a page does `_files.Insert(0, …)` or
`_notes.RemoveAll(…)`, a wrapping `BenListState` keeps rendering the *load's* emptiness and the
first item added never appears. Those surfaces branch on `.Failed` directly and leave the existing
empty-state logic on the live list. This is a trap for whoever converts the next area.

**The guard's own scanner nearly shipped a false accusation.** A naive `/* … */` strip ate a file
input's `accept="image/*,audio/*,video/*"` and everything up to the next real `*/` — 700 lines of
`MyCaseDetail.razor`, including every failure branch — so the guard reported the most carefully
converted page in the branch as the only offender. The lookbehind now requires `/*` to start a
token. That is the sixth scanning guard here to be defeated by the file it was reading.
