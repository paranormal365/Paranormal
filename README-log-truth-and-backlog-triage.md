# Feature: a log that can show a fault, and a backlog that tells the truth

Branched from `develop` at `235de3c` (2026-08-31), immediately after the green-suite merge.

## Why this branch exists

Asked what was next, I read the backlog rather than guessing — and found two things wrong with the
backlog itself before finding anything wrong with the code.

## 1. Three items were already done and still marked open

`194`, `200` and `201` all carry a `### Done` or `### Built` section in their bodies and a header
that still says `found`. This is the **standing caution** recorded in
`reference_future_improvements_backlog`: *headers lag bodies, believe the body*. Corrected in
place. Nine items are genuinely open: **183, 187, 189, 190, 191, 192, 196, 197, 198**.

## 2. Item 191 asked for a measurement, so I took it — and it says don't build

Item 191 (archive the audit log) says plainly: *"do the sums before choosing the window."*

| | |
|---|---|
| Whole database | **272 MB** |
| `AuditLogs` | **444 rows, 1.1 MB** |
| Largest table, `Logs` | 6,066 rows, **36.5 MB** |

~111 audit rows a day is ~40,000 a year, under 100 MB. The design in item 191 is right and is kept
written down; **building it now would be effort spent on the smallest number in the table.**
Deferred with the numbers attached, to be revisited around a million rows.

## 3. What the measurement actually found — item 202, fixed here

**The biggest table is the ERROR log, and 96% of it was one avoidable message.**

1,978 of 2,022 rows were `An unhandled exception has occurred while executing the request` carrying
a `FileNotFoundException` and a full stack trace. **1,934 stood for three files**; one was
requested 1,695 times in four days.

The handler in `Program.cs` already gets this right — 404 and a **Warning**, with a comment saying
a routine data gap must not hide real faults. **The decision never took effect:** ASP.NET Core's
`ExceptionHandlerMiddleware` logs at Error, with the trace, *before* invoking the handler. The code
was correct and was being overruled by the framework, which is why reading it would never reveal
this. Only counting rows did.

The damage is not the 36 MB. It is that a log where one missing avatar outnumbers everything else
twenty to one **cannot show a real fault** — exactly what the handler set out to prevent.

**Fix:** `LogNoise.IsDuplicateOfAHandledMissingFile`, scoped to that one middleware, that one
exception family, that one level. Same exception elsewhere still logs at Error. Other exceptions
from that middleware still log in full. The handler's Warning survives untouched, because **the
Warning is the record being kept**.

In code, not configuration — a correctness rule about not contradicting ourselves, and a config
switch would bring the noise straight back.

**Six tests, one per clause, mutation-verified:** dropping the source-context clause fails exactly
`The_same_exception_from_anywhere_else_still_logs`, on a clean build.

## Deliberately NOT in scope

- The remaining eight open items. **183** (case delete), **197** (haunted hotels) and **198** (tour
  pricing) each say "Ben's call" in their own bodies and must not be decided for him. **196**
  (hold-harmless waiver) turns on wording that should not be written by a website — it needs a
  lawyer's default or an explicit "starting point, not legal advice" decision. **189/190** are
  large designs. **187** is a macOS/.NET diagnosis. **192** is answered and now documented in
  `docs/deploy-production.md`.
- Nothing prunes `Logs` at all. Worth doing **next**, and a far better use of item 191's roll-off
  design than the audit log — but only now that the table is signal instead of noise.

## Done when

- The three stale headers are corrected. ✅
- Item 191 carries its measurement and a deferral rather than an unbuilt plan. ✅
- The error log no longer double-writes a decision already made, with tests that discriminate. ✅
