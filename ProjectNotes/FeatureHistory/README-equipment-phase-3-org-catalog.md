# Equipment Phase 3 — The Group's Own Gear, and the Equipment Permissions

Branch: `feature/equipment-org-catalog` · Backlog item **#55**, third of five phases.
Stacked on `feature/equipment-sharing` (phase 2).

## Why

Phases 1 and 2 covered gear people own personally. Item #55's second half is the group's own
property: "a separate but similar catalog for gear the org itself owns", tracking "who currently
has a given piece, when it was last serviced, and any noted defects", managed by a new org-creatable
**Equipment Management** role.

## What shipped

**Permissions** — `OrganizationSecurityTable.Equipment = 35` and `EquipmentCheckout = 36`, both
added to `OrgRoleEditor`'s `Sections` list with the second nested under the first. No new
authorization machinery: `OrganizationSecurityService.HasAccessAsync` already resolves SuperAdmin →
owner/administrator → direct grant → named role, so an org-created "Equipment Management" role
works with zero new plumbing.

**Entity** — `EquipmentServiceLog` (migration `AddEquipmentServiceLog`), applied to the dev DB.

**API** on `api/organizations/{orgId}/equipment`

| Route | Who |
|---|---|
| `GET /` (list + `CanManage`), `GET /{id}` | any active member |
| `POST /`, `PUT /{id}`, `DELETE /{id}` | `Equipment` Create/Update/Delete |
| `PUT /{id}/holder` | `Equipment` Update |
| `GET /{id}/service-log` | any active member |
| `POST /{id}/service-log` | `Equipment` Update |

**UI** — the Equipment tab now has two sections: *The group's gear* (grid with holder, condition and
serial columns, plus `OrgEquipmentEditor` and an `OrgEquipmentServiceLog` history window) and phase
2's *Members' shared gear*. Help: the org-admin doc gained a rewritten Equipment tab section and a
new *The two equipment permissions* section.

## Decisions worth knowing

**Two permissions, not one.** `Equipment` manages the catalog; `EquipmentCheckout` runs the loans
desk. Split so a group can delegate lending without also delegating what the group buys and owns —
different jobs, often different people. Granting one does not grant the other, and neither has any
say over a member's personal gear, whose loans are always the owner's call.

**Reading is membership; changing is permission.** What the group owns is not a secret from the
people who use it, so any member sees the kit list. Serial numbers and every write need `Equipment`.
The serial is *withheld from the payload*, not flagged for the client to respect.

**The bug caught while building the UI.** Deriving "can I add equipment?" from whether any row is
editable leaves the Add button permanently hidden on an **empty** list — which is the state every
group starts in. The feature would have been dead on arrival for exactly the groups that had not
used it yet, the same shape as the empty-lookup-table lesson from `ContactTypeSeeder`. The list
endpoint now returns `OrgEquipmentListRecord(CanManage, Items)` so an empty list can still say "you
may add the first piece", and the adapter defaults a swallowed non-2xx to `CanManage: false` — a
permission gap should close, not open. `AnEmptyList_StillCarriesThePermissionToAddTheFirstPiece`
pins it.

**The service log's entry type does work, not labelling.** A fault report marks the item faulty and
becomes its reason; a fix clears it; a service entry moves the last-serviced date — each in the same
save as its entry, so the log can never disagree with the item it describes. Entries are kept:
fixing a fault does not erase the report of it. The item's own `DefectNotes`/`LastServicedDate` are
a cache of the log's latest word, which keeps "is this broken right now?" a column read.

**`EntryDate` is separate from `DateCreated`.** Gear is serviced on one day and logged on another;
back-dating an entry should not mean lying about when it was typed.

**Delete gives way to retire.** Any service history makes delete a `409`. Destroying a
serial-numbered asset would take the account of what happened to it along with it.

**The permission verdict is resolved once per request**, not per row — the N+1 that
`OrganizationController`'s own comments warn about.

## Verification

- Solution builds clean, 0 warnings.
- Suite **2,420 → 2,426**, all green: 19 new tests in `OrganizationEquipmentTests`.
- **The two load-bearing guards were run against deliberately broken code first** — serial
  withholding removed, and the delete-with-history refusal removed — and both tests failed.
  Restored and re-run green.
- Migration applied to the dev SQL box; `scripts/create-database.sql` regenerated.
- Live: all five new routes answer `401` anonymously (routed and gated, not missing); phase 1's
  anonymous catalog still `200`.
- Needs Ben's signed-in click-through: the group's Equipment tab, adding a piece, the history
  window's three entry types, and confirming a plain member sees no serials.

## Noted, not fixed

`Watchdog_GenuinelyStuckCommand_FlagsIsWorkerWedged` in `Ben.Video.Tests` is intermittently flaky —
it failed in 2 of 6 full-suite runs and passed 3/3 in isolation. It races a 2-second wall clock
waiting for a 30 ms watchdog, and is unrelated to equipment (no code path connects `Ben.Data`/
`Ben.Web` to `FfmpegService`). Spawned as its own task rather than fixed here.

## Not in this phase

Borrowing anything. `LoanAudience` is recorded and displayed, and `EquipmentCheckout` is grantable,
but no checkout entity exists yet — that is phase 4, along with the notification bucket and the
approval queue.
