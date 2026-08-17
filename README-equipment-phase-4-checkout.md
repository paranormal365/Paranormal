# Equipment Phase 4 — The Checkout Lifecycle

Branch: `feature/equipment-checkout` · Backlog item **#55**, fourth of five phases.
Stacked on `feature/equipment-org-catalog` (phase 3).

## Why

Phases 1–3 built what exists and who can see it. This is the phase where `LoanAudience` and the
`EquipmentCheckout` permission stop being metadata and start governing something: item #55's
"members can request to check out equipment… the system tracks who has it now".

## What shipped

**Entity** — `EquipmentCheckout` (migration `AddEquipmentCheckout`, applied). **Enum** —
`EquipmentCheckoutStatus`: Requested → Approved → CheckedOut → Returned, with Denied and Cancelled
as the other two terminals.

**API** — one endpoint per transition on `api/equipment-checkouts`, plus
`GET eligibility/{itemId}`, `GET /api/me/equipment-checkouts?role=borrower|approver`,
`GET /api/organizations/{orgId}/equipment-checkouts`, and `GET /api/equipment/{itemId}/checkouts`.

**UI** — `MyCheckouts.razor` (`/my-checkouts`, in the nav), `EquipmentCheckoutRequestDialog`, a
**Loans** section on the group's Equipment tab, an **Ask to borrow** button on both gear grids, a
new notification bucket on the bell and the notifications page.

**Help** — new `borrowing-equipment.md`.

## Decisions worth knowing

**One entity, one state machine, two approvers.** The lifecycle is identical for group gear and
personal gear; only *who decides* differs, and that is a property of the item rather than the loan.
`EquipmentAccess.CanReviewCheckoutAsync` is the only place that answers it, so no transition can
drift from the others. Two entities would have meant two controllers, two queues, two history
queries and a UNION.

**`BorrowedForOrganizationId` is nullable — this is the loan-audience model paying off.** A loan
taken out for a group records which group and can be tied to a visit. A personal loan represents
nobody. Requiring a group would have forced personal borrowers to pretend to speak for one, which
is precisely the distinction Ben drew when splitting the audience into three flags.

**Each party confirms the transfer coming toward them.** The borrower confirms the hand-off; the
lender confirms the return. A loan should not be closable by asserting you gave something back, and
the person holding the equipment is the one who can truthfully say they hold it.

**Overdue is computed, never stored.** It is `CheckedOut` plus a due date in the past. Storing it
would mean a background job maintaining something already free, and a borrower with a wrong clock
would see a different answer from the lender. A test asserts the row carries no overdue state.

**A request cannot claim a group the server did not offer.** Eligibility is recomputed server-side
on submit and the chosen group must appear in that answer, so the form's own option list is a
convenience, never the authority.

**No re-open.** Denied, Cancelled and Returned are final; a fresh ask is a new row. A loan's history
then reads as a sequence of things that happened rather than one row that changed its mind.

**404 over 403 for non-parties.** Someone who is neither borrower nor approver is told the loan does
not exist, so the endpoints cannot be used to discover which loan ids are real.

**Notifications commit with the decision.** `UserMessage` rows are added to the same change set as
the transition, so a notice cannot survive a rolled-back decision. The bucket deliberately mixes two
obligations — requests awaiting my decision, and my gear that is late back — because both mean "go
and deal with a piece of equipment".

## Caught while building

I wrote a help section describing loans appearing on the group's calendar, which is **not built**.
Removed rather than shipped: that is exactly the overpromising-copy bug found in item #54, where
`MyProfile.razor` promised a public-case-page surface that never existed. The doc now describes the
org **Loans** queue, which does.

## Verification

- Solution builds clean, 0 warnings.
- Suite **2,426 → 2,455**, all green: 29 new tests in `EquipmentCheckoutTests`.
- **Four guards were run against deliberately broken code first** — the claimed-borrowing-group
  check, both party checks, and the personal-gear approver rule — and all four failed. Restored and
  re-run green.
- Live: all eleven new routes answer `401` anonymously (routed and gated, not missing).
- **Verified against the real database**, not just the build: the `EquipmentCheckouts` table exists
  and the `Equipment Checkout` message type seeded with its fixed id.
- Needs Ben's signed-in click-through: a full loan between two accounts — ask, approve with a due
  date, confirm hand-off, return — plus the bell count and the group's Loans tab.

## Not in this phase

Condition photos at hand-off and return, renewal requests, and the merged per-item history rollup
are phase 5. The group-calendar due-date overlay is designed (see the plan) but unbuilt.
