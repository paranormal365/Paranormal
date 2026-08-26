# Making role grants visible (item 156 follow-on, IH-03)

## What the production sweep actually found

Ben's 2026-08-26 validation sweep filed IH-03 as *"Assigning a functional role changes nothing."*
Marcus Webb was given the Case Manager Role — full C/R/U/D on Cases and Investigations — and his
organization view stayed byte-for-byte identical to Tyler Brooks, a plain member with no role.

The conclusion in the report was that all nine roles are decorative. **That is not what is
happening**, and the difference matters for what gets built:

- `OrganizationSecurityService.HasAccessAsync` **already** joins role memberships → roles → role
  permissions. A granted role really does pass the check.
- `CaseController` **already** gates create and update on `Case.Create` / `Case.Update`. A Case
  Manager can create a case through the API; a plain member gets a 403.

Three things made the grant look inert from a chair:

1. **Tyler already had the same READ access.** Five of TGH's nine members hold Investigator Role,
   which grants case read — so both men saw the Cases and Investigations tabs. Case Manager's
   extra rights are all WRITES, and writes add no tabs.
2. **Only two booleans reach the browser.** `MyOrgPermissionsResponse` is
   `(CanReadCases, CanReadInvestigations)`. Nothing tells the UI whether you may CREATE a case,
   so no button can depend on it.
3. **The org Edit button is a different area.** It answers to
   `OrganizationSecurityTable.Organization`; Case Manager grants Cases and Investigations. It was
   correctly unchanged.

So the accurate statement is: **the grants are enforced but invisible.** A Case Manager is refused
nothing they try, and offered nothing either. It is this codebase's recurring write-only shape,
seen from the other side — and it is why an owner configuring roles gets no signal that anything
happened.

## The plan, and what this branch covers

| | | |
|---|---|---|
| 1 | Widen the permissions endpoint beyond two reads | **this branch** |
| 2 | Gate the write affordances on it (New Case, Edit, New Investigation, delete…) | next, and the bulk |
| 3 | Audit write endpoints for tier-only gates a granted role should also pass | **this branch** |
| 4 | Decide the grandfather bridge's future | Ben's call |
| 5 | Title → suggested roles | after enforcement is visible |

1 and 3 are deliberately first: together they say how big 2 really is, and 3 can turn up genuine
holes rather than merely missing buttons.

### Why 4 is a decision and not a task

Every existing member currently keeps read access regardless of grants — the grandfather bridge
from item 156 Phase D. It was right for the transition, and it is why removing somebody's role
changes nothing visible. Leaving it means read grants can only ever ADD, never restrict. Turning
it off makes roles authoritative and will take read access away from members who have it today.
That is a product decision with a migration behind it, not a code change.

### Why 5 waits

Ben's title→roles idea (a title suggesting a bundle, copied on assign, editable per person) is
right — his own example settles it: two Lead Investigators where only one may contact clients.
But bundling rights nobody can see would just multiply the invisible.

## The test that was missing

Everything in the suite compares membership TIERS — Viewer 6 tabs, Member 8, Administrator 14.
Nothing compares two members of one group who differ only by a role. That is exactly the gap IH-03
fell through, and this branch adds it.
