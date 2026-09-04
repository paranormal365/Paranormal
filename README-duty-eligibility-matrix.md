# Which title may hold which duty (item 160)

**Branch:** `feature/duty-eligibility-matrix`

Ben's spec from August: a place where the owner decides which investigation duties each **title**
is eligible for — a matrix, not just a minimum — with his worked example. A junior may *assist*
with equipment; an investigator may *run* it but is not the visit's point of contact; the lead of
a visit is the point of contact but does not move the date.

Plus, from 2026-09-04: every new group should start with a real ladder — Associate, Junior
Investigator, Investigator, Senior Investigator — already assigned, not an empty page.

## What this builds

- **`InvestigationDutyEligibility`** — one row per (duty, title) cell. Rows mean eligible.
- **`InvestigationDuty.Capabilities`** — what holding a duty lets somebody do **on that one visit**.
- **`DutyEligibility.CheckAsync`** — the single place both rules live. A duty whose matrix has rows
  is answered by the matrix. A duty with none falls back to `MinimumMemberLevelId`, the
  single-threshold rule item 158 shipped. So a group that never opens the grid behaves exactly as
  before, and **nothing had to be backfilled**.
- **`InvestigationAccess.HasDutyCapabilityAsync`** — asked *alongside* `CanManageAsync`, never
  instead of it.
- **`GET/PUT .../investigation-duties/matrix`** and a grid in group settings, directly under the
  ladder and the duty list.
- **Seeded defaults.** The bottom rung is now **Associate** rather than "Probationary" — the
  bottom of the ladder is where somebody new stands, and naming it after a probation period reads
  as a warning rather than a welcome. Equipment is split into **Equipment** and **Equipment
  Assist**, because Ben's example needs to distinguish assisting from running and one duty cannot.
  The matrix arrives filled in.

## The three design calls, since the item reserved them

1. **How the matrix and the role system (item 156) avoid disagreeing.** A duty capability can only
   ever *widen*, for one investigation, and is asked alongside the role check. Either says yes and
   the answer is yes; a duty can open a door the roles left shut for one night, and can never close
   one they opened. That is the same shape as the visit lead's manage right — delegated authority
   that expires with the visit.

2. **Only capabilities with a door.** Ben's example also describes inviting members to a visit and
   rescheduling one. Neither has a control anywhere in the product today, and a switch that reads
   well on a settings page and changes nothing is the write-only feature this codebase keeps having
   to go back and finish. Two capabilities ship because two have doors: **point of contact** (shown
   on the roster) and **hands out duties** (enforced at the assignment endpoints). The others go in
   with their doors.

3. **No new "Case Lead" position.** The item wanted one, overlapping the case manager and item
   158's case contacts, and asked which of the three the client sees. Rather than answer that
   silently by inventing a third, the investigation-administrator bundle is expressed as
   capabilities on the existing **Lead Investigator** duty, and the case manager remains the
   case-level lead the client sees. **This is the one call worth Ben's review** — if he wants a
   distinct Case Lead, it is a seeded duty and a capability away.

## Tests

`DutyEligibilityMatrixTests` — 15: what a new group starts with; the seeded matrix as a theory
over Ben's worked example (associate documents, investigator runs equipment, only the top two
rungs lead a visit); a member with no title; the fallback to the old minimum; no rule at all;
and the capability scope — the same person holding the same duty on a *different* visit is
refused, which is the test that keeps capabilities from becoming standing rank.

`OrganizationPurgeBehaviourTests` gained a matrix cell and a ladder rung, so the purge order is
proven against real foreign keys: the cell's key to the ladder is NoAction, and sweeping rungs
before cells is refused outright.

Discrimination, both confirmed against broken code: making the capability check ignore which
investigation it is on, and making the resolver ignore the matrix.

Playwright renders the grid and its capability columns without saving a row — shared database.

## Docs

`organization-administration.md` — the duty section now covers the split equipment duties, and a
new **Who may hold which duty** section explains the grid, the soft override, the fallback, and
what "on the night" means.

## Deployment

Migration `AddInvestigationDutyEligibilityMatrix` must be applied. `scripts/create-database.sql`
regenerated.
