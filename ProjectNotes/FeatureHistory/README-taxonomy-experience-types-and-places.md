# Taxonomy staleness, part two: experience types and places

Closes the last piece of backlog item **#90**. The equipment catalog got typo detection, orphan
cleanup and rename-as-merge; this applies the same treatment to the other taxonomies that grow by
proposal — and fixes the reason none of it was reaching a user.

## What the investigation found

The prediction was that experience types and place names "have the same shape and, presumably, the
same problem". Half right.

**Experience types were worse off than equipment ever was.** A group cannot delete a type it
proposed — the only delete lives behind an app-administrator screen — so a mistyping was permanent
and its author was powerless. Five gaps:

| Gap | Before | Now |
| --- | --- | --- |
| Typo detection on propose | none | reviewed types in the same category, with an override |
| Admin create dedup | none — could make the twin everyone else was stopped from making | case-insensitive, returns the existing row |
| Rename onto a taken name | silently produced twins in one category | offered as a merge |
| Merge | did not exist | folds taggings onto the survivor |
| Orphan sweep | none, on untag or on occurrence delete | both, on the equipment rule |

Plus schema: the table had **no unique index and no length cap** — `nvarchar(max)`, with the
100-character limit the API advertised enforced nowhere.

**Places turned out to be fine.** `PlaceMatcher` already had a real rule (same address *and* within
a tenth of a mile) and already normalised case, punctuation and a leading "the". The single gap was
a mistyped landmark name, now tolerated.

## The finding that matters most

**The typo check was unreachable in the UI the whole time, including on the equipment side I built
earlier.** The server answers a probable typo with a 409 listing the near-misses; both callers
discarded it and rendered "could not be added". The person could not take the suggestion, could not
insist on their own name, and could not add anything resembling an existing entry.

That made the check **strictly worse than not having it** — before it, the word at least got
created.

Every unit test passed. They asserted the server returns suggestions; nothing asserted a person sees
them. Fifth instance of this shape in the codebase, after platform messages, permission requests,
`CanRequestCheckout` and `UserNameLink.ShowAvatar`.

Both screens now render a "did you mean" prompt — each suggestion one click, plus *"no, mine is
different"*. Two source-scan tests hold it: the suggestions must reach a screen, and **every** screen
showing them must also be able to overrule them.

## Design notes worth keeping

- **"Reviewed" is `IsApproved && ApprovedByAppUserId != null`.** An org-proposed type goes live
  immediately with the approver null, and that null is the whole marker. Testing `IsApproved` alone
  would sweep away deliberately-endorsed words.
- **The join's primary key is (entry, type)**, so a merge cannot repoint a tagging — EF refuses to
  modify a key property on a tracked entity. Rows are deleted and re-added. Found by reading the
  model config before writing the loop, not by a failing test.
- **Deleting a type in use was a 500.** The FK is `NoAction`, so the old unguarded delete failed at
  the database and told the administrator nothing. Now refused in words, with the count and the
  alternative (Reject removes the taggings too, and reports how many).
- **Typo tolerance is scoped to the category** for experience types. "Shadow" under Visual and
  "Shadow" under Tactile are not a pair worth conflating.
- **Place-name fuzziness is safe where equipment-style strictness would not be**: candidates are
  only offered, never applied, and proximity has already passed.

## Verification

- Full solution build, **0 warnings, 0 errors**.
- Full suite **green** (2,301 in Ben.Web.Tests; 4,579 across the solution).
- **Every guard broken deliberately and watched to fail**, including a re-do after a first break
  turned out not to compile — which proves nothing.
- Migration **applied to the real dev SQL Server**, dedupe pre-step and `nvarchar(100)` alter
  included, not just exercised in memory.
- `scripts/create-database.sql` regenerated.

## Not done

- The **UI has not been click-tested by a human.** The "did you mean" prompts are Telerik-rendered
  and cannot be verified in this environment; the source-scan tests prove the wiring exists, not
  that it looks right.
- **Experience categories** keep the same unguarded create/rename/delete that types had. Types were
  the reachable problem — groups propose those — while categories stay SuperAdmin-only, so the same
  fixes there are lower value. Worth doing, not urgent.
