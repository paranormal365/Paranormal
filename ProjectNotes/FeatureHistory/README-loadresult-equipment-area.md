# Item 120, slice 4 — the equipment area

Organization (2026-08-21), case and platform (2026-08-22) are done. This is the largest remaining
pile: **22 methods across 15 consumers**, taking the ratchet **67 → 45**.

## What made this slice different

The equipment area uses the collection-spread idiom heavily — `_items = [.. await Client.GetX()]`
— so 27 of the call sites were spreads rather than plain assignments. Mechanically that is a
one-line change each (`[.. (await …).Items]`), and that is exactly the danger: **the whole slice
compiles green while every page still reports a refusal as an empty list.**

`LoadResultRenderedGuardTests` was written for that half-conversion, and it earned its place here.
Registering the 22 new method names turned it into a worklist of 14 files, which is how the twelve
real surfaces below were found rather than guessed at.

## A mutation, not a load

`SetMyEquipmentSharesAsync` is a PUT, so it does **not** return `LoadResult` — the question is "did
this happen?", not "is this list real?". But it had the same defect and a worse consequence: a
refused save became `null`, then `?? []`, and `EquipmentShareEditor` **discarded the result
entirely**, closed its dialog and reported success. Somebody believed their equipment was shared
with a group when nothing had been saved.

It now returns `(Shares, Error)` via `SendExpectingReasonAsync` — sharing is refused for reasons a
person can act on ("not a member of that group any more"), and "Save failed" is not one of them —
and the editor shows the reason instead of closing.

## Twelve surfaces, two decorations

Converted with failure rendering: my equipment, my checkouts, asked/received questions, item
history, FAQs, condition photos, share options, product reviews, the public catalogue browse (two
lists), the org's shared equipment, the org service log, and the admin taxonomy.

Recorded as decorations, with the reason, in the guard's allowlist: the cascading
category/brand/model pickers in `MyEquipmentItemEditor` and `OrgEquipmentEditor`. A failed fetch
leaves a picker empty, which the person sees and can retry; a warning panel inside a form field
would be worse than the gap.

Two worth calling out among the twelve:

- **Condition photos** are the evidence in a damage dispute. "No photos taken" when the fetch was
  refused is the wrong answer to have on record.
- **The public catalogue** is browsed by visitors with no account, no error console and no way to
  tell a broken page from an empty one.

## Found on the way in

`GetEquipmentItemCheckoutsAsync` is declared, implemented and **called by nothing** — the second
such method this conversion has turned up, after `GetPublishedInvestigationsAsync` in the platform
slice.

## Done when

- `BenAdminClientAdapter.Equipment.cs` has no `?? []`
- `SwallowedFailureRatchetTests.Ceiling` lowered 67 → 45
- Zero-warning build, full suite green
