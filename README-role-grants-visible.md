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

---

## Step 4 — the grandfather bridge is gone (Ben, 2026-08-26)

> Currently I am the only actual person using the site. Keep me as the super admin then change the
> security settings instead of grandfathering anyone. No one else is using the site yet.

Removed from `OrgRoleSeeder`: it used to create an **Investigator Role** (Cases + Investigations
Read) and hand it to every active non-admin member, so Phase D's enforcement flip took nothing
from anyone. It still backfills the seven default roles for a group that has none — roles are
CREATED, nobody is put in them.

**The runtime never had a bypass.** `HasAccessAsync` has always answered from grants alone, with
owners and administrators passing above it. This seeder was the bridge, and removing it is what
makes roles authoritative — a read grant can now be restrictive rather than only additive, which
was the point of IH-03 and impossible while a seeder quietly granted case access to everyone.

**Existing assignments were left in place**, deliberately. Being precise about why: the
grandfathered ones ARE identifiable — the seeder wrote a distinctive description ("Assigned to
everyone who was already a member when role-based case access arrived…") — so they could be
revoked. That is a data change on the shared dev/UAT database, and it is Ben's to make rather than
mine to slip in. Until he does, existing members keep the role they were auto-given and the
change applies to new groups and new members only.

**To see strict behaviour on the existing data**, the Investigator Role assignments in TGH (and
any other seeded group) need removing — either through Roles → Investigator Role → Edit → Members,
or by deleting the role outright. Ben's own SuperAdmin seat is unaffected either way: SuperAdmin
bypasses these checks entirely.

---

## What the gate helpers actually check (2026-08-26)

Ben's question, and the right one: *"I kinda would assume it meant a member of an organization
since we call people who have verified accounts Users and not Members. But you will have to check
what it points towards."*

Checked. **The naming is misleading in both directions, and it misled this branch's own audit.**

| What it really does | How many | Examples |
|---|---|---|
| **Calls `HasAccessAsync`** — already grant-aware | 16 | `IsOrgMember` (CaseFile, CaseReport, CaseResearch, ScheduleProposal, CaseAudioMix), `IsOrgMemberAsync` (CaseNote, CaseTransfer, Investigation), `IsMemberAsync` (OrgInvestigations), `MayManageAsync` (OrganizationAd), `CanManageAsync` (MembershipQuestion), `MayDecideAsync` (FeedAttribution), `IsAdminOrHasAsync` (Case, OrgCalendar) |
| **Owner or Administrator only** — deliberate | ~9 | the `IsOrgAdmin*` family, resolving to `Role <= Administrator`, i.e. Owner(1) or Administrator(2) |
| **Per-row rule** — about ONE record, not the area | 1 | `InvestigationController.CanManageAsync` → `InvestigationAccess`, "may this person manage THIS investigation" |
| **Genuinely any active member** | 1 | `OrgMessageController.IsMemberAsync` — correct, because no permission area covers the message board |

### Two corrections this forces

**My earlier "70 endpoints gated on tier rather than grant" was too high.** That audit read only the
endpoint body, so a grant check living one call away — inside a helper named `IsOrgMember` — looked
like no grant check at all. A large share of those 70 are already grant-aware through their
helpers. The number to trust is the table above, not the earlier one; step 2 is correspondingly
smaller than advertised.

**A helper called `IsOrgMember` that returns "has a Case.Read grant" is a lie in a method name.**
It cost this branch one wrong measurement and would cost the next reader the same. Renaming them as
step 2 touches each controller — `MayReadCasesAsync`, `IsOwnerOrAdminAsync`, and so on — is worth
doing while the intent is fresh.

**Ben's vocabulary should be the rule**: **User** = a verified account; **Member** = belongs to an
organization. A helper about a GRANT should say so and use neither word.

### What step 2 actually needs, per endpoint

1. Already grant-aware → the endpoint needs no change; only the UI affordance is missing.
2. Owner/admin-only → decide, deliberately, whether a granted role SHOULD pass. Some of these
   (member levels, area of operation, transfers) may be genuinely owner-only forever.
3. Per-row → leave alone. "May this person manage this investigation" is not an area grant and
   should not become one.
4. Any active member → only where no area covers the thing at all, as with messages.


## Step 2, second pass — the case sub-surfaces (2026-08-26)

Prompted by Ben mid-session: *"Be sure to check permissions for clients of organizations with
their case."* The sweep that followed found more than the prompt asked for.

### The defect, in one sentence

Seven controllers gated **create, update and delete** on the `Case.Read` grant.

| Controller | Was | Now |
| --- | --- | --- |
| `CaseNoteController` | Read for POST/PUT/DELETE | Create / Update / Delete |
| `CaseFileController` | Read for POST/DELETE | Create / Delete |
| `CaseResearchController` | Read for POST/PUT/DELETE | Create / Update / Delete |
| `CaseReportController` | Read for **all sixteen** | Read / Create / Update / Delete |
| `CaseMessageController` | bare active membership | Read to read, Update to answer the client |
| `ScheduleProposalController` | Read for all four | Read / Create / Delete / Investigations.Create |
| `CaseAudioMixController` | Read for the export | Create (the export attaches a case file) |
| `CaseContactController` | bare active membership | Read (write stays case-manager-or-admin) |

`CaseReportController` is the one that mattered most: **Publish** is what puts a report in front
of the client, and **Delete** removes a published one. Both were open to anyone who could read
the case.

**The naming did the damage.** Six of these were called `IsOrgMember` / `IsOrgMemberAsync` while
actually asking `HasAccessAsync(Case, Read)`. A helper called "is org member" reads as a
belonging check, so no reviewer asked what it permitted. Every one is now named for the question
it answers and takes the action as an argument.

It was survivable while the seeder handed case read to every member. Step 4 — ending the
grandfathering — is what turned a read grant into a deliberate act, and made this urgent.

### Two bugs found in the same sweep, neither about permissions

**`ScheduleProposalController.Convert` created an org-less investigation.** `OrganizationId` is a
direct FK, deliberately not derived through the case, and `Convert` never set it. The
investigation belonged to `Guid.Empty`, was absent from every org-scoped query, and the proposal
cheerfully reported `Converted`. Of the six `new Investigation` sites in the codebase this was
the only one missing it — and it is the one the **client** starts by accepting a date.

**A lapse never told the primary client.** `SubscriptionLapseJob` notified `CaseClientAccesses`,
which holds *co-clients* — people added by invitation. The primary client reaches their case
through `Case.ClientRequest.AppUserId` and has no row there (`MyCaseController.IsCaseClient`
checks both, which is why the client side otherwise works). So the person whose home is being
investigated was never told their case had been paused and never got the thirty-day
reassignment offer; a case with no invited co-clients notified **nobody** while the job logged
success. Both notices now go through one `ClientsOfCaseAsync` helper.

### The UI half

`CaseNotes`, `CaseFiles` and `CaseResearch` take a `Permissions` parameter from `CaseDetail`
rather than each fetching its own — one answer, one round trip, four surfaces that agree.
**Null means no**: while permissions load, and if the call fails, the buttons stay hidden.
Hiding a button from someone entitled to it is a visible annoyance they can report; showing one
to someone who is not is the bug this branch exists to close.

### The tests, and the hole in the first one

`ReadDoesNotGrantDestructionTests` asserts the rule (a `Case.Read` grant permits Read and refuses
Create/Update/Delete) and ratchets it across all seven controllers by pairing each HTTP verb with
the action guarding it.

**Its first version was wrong in a way that would have shipped.** It looked for `Forbid()` only,
and `CaseMessageController` refuses with `NotFound()` — so reverting its POST to a `Read` gate
passed the ratchet cleanly. Found by sabotaging the fix and watching the test stay green, which
is the only way that class of hole shows itself. The three lapse-notification tests were proven
the same way.

`TestSeeds.BridgeAsync` now grants `Read` by default and takes `TestSeeds.CaseWork` explicitly.
Seventy tests failed when the gates tightened; every one was a suite seeding a member with no
write grant and expecting a write to succeed. A suite must now say out loud that its member may
write. **3365 pass.**

### Still open on this branch

- **Step 5** — titles suggesting roles (copy-on-assign, not live inheritance).
- The remaining bare-membership helpers outside the case area: `EventEvidenceController`,
  `UploadFileShareController`, `UploadFileShareV2Controller`,
  `OrganizationAreaOfOperationController`, and `CaseController.IsOrgAdminOrSuperAsync` — the
  admin-shaped ones are candidates for `IsOwnerOrAdminAsync` rather than a grant.
- Playwright has not been run against the hidden-affordance changes.
