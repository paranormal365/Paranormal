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

---

## Step 1 — the permissions endpoint (done)

`GET /api/security/organizations/{id}/my-permissions` returned exactly two booleans:
`(CanReadCases, CanReadInvestigations)`. It now also returns an **area → {create, read, update,
delete}** map covering all nine permission areas, computed through the same
`HasAccessAsync` the server enforces with. The two original booleans stay, so existing callers
keep working.

Client side, `MyOrgPermissionsItem.May(area, action)` is the accessor to use. It answers **no**
for anything the server did not mention — an affordance appearing because the server stayed
silent would lead somebody to a refusal, which is the failure this endpoint exists to prevent.

A guard test asserts every `OrganizationPermissionArea` is answered for, that each is probed
through a table that genuinely belongs to it, and that none is probed twice. A tenth area added to
the role editor with nothing here to report it would put the next feature straight back in this
hole, silently.

## Step 3 — the write-endpoint audit (done)

147 org-scoped write endpoints, classified:

| | |
|---|---|
| **73** | grant-aware — already consult `HasAccessAsync` / `IsAdminOrHasAsync` |
| **70** | gated, but on membership tier or a bespoke helper, so a granted role cannot pass |
| **4** | no gate this script can see — inspected by hand, all four are fine |

**Half the write surface already honours grants.** The 70 are where step 2's buttons would appear
but the server would still refuse a role-holder — so step 2 needs those endpoints converted as it
goes, or it will offer affordances that 403. The heaviest are `CaseReportController` (11),
`OrgInvestigationsController` (8), `InvestigationController` (7), `OrgCalendarController` (5),
`OrganizationAdController` (5). The full classification is regenerable — the script is in the
commit message's method, and the buckets are per-endpoint with file and line.

### One genuine hole, found and closed

`OrgMessageController` carried `[Authorize]` and **nothing else**. The organization id came from
the route, the author from the token, and no step in between asked whether the two had anything to
do with each other — so any signed-in person could read a group's message board, and post to it,
by knowing its id. The same broken-ID-chain shape the Phase-B audit found across nine controllers,
in a controller that audit missed.

Now gated on active membership: inbox, sent and send. `GetById` is deliberately **not** — it
already decides per message, and public feed posts are meant to be readable by anyone. That
exemption is not a guess: a blanket gate broke
`GetById_PublicFeedMessage_AnyoneCanView` immediately, which is the test doing its job.

Three new tests, each checked against the un-gated code first: a stranger is refused the inbox, a
stranger's post is refused **and writes nothing**, and a member still gets through.

## What should be next

**Step 2, converting the 70 as it goes** — and the two are genuinely one piece of work, not two.
Adding a button without converting its endpoint offers an affordance the server refuses; converting
an endpoint without adding the button leaves the grant as invisible as it is today. Suggested
order, by how much a role-holder gains: Cases and Investigations first (that is what Case Manager
and Investigator actually grant), then Calendar, then Membership and Equipment.

Before that, two things are worth deciding rather than discovering:

- **The grandfather bridge** (step 4). While every member keeps read access regardless of grants,
  a read grant can only ever ADD. Roles cannot be made restrictive until that ends, and ending it
  takes read access from members who have it today.
- **What `IsMemberAsync` should mean** in the 70. Some of those helpers are "any member", some are
  "owner or admin". Converting them to grants is only safe once it is clear which of the two each
  one was trying to say.

