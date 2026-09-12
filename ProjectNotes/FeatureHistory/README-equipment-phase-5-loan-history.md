# Equipment Phase 5 — Condition Photos, Renewals, and Item History

Branch: `feature/equipment-loan-history` · Backlog item **#55**, last of the five planned phases.
Branched from `develop` after phases 1–4 were merged.

## What shipped

**Entities** — `EquipmentCheckoutPhoto` (stage: Handoff/Return) and `EquipmentCheckoutRenewal`,
migration `AddEquipmentCheckoutPhotosAndRenewals`, applied.

**API** — condition photos (`GET`/`POST` multipart/`DELETE`, plus an authed bytes endpoint),
renewals (`GET`, `POST`, `POST .../review`), and `GET api/equipment/{itemId}/history`.

**UI** — `CheckoutConditionPhotos` (before/after side by side), `EquipmentCheckoutDetail` (a loan in
full, reached from a **Details** button on both My Checkouts stacks), and `EquipmentItemHistory`,
embedded in the group's item-history window above the service log.

**Help** — new sections in `borrowing-equipment.md` on condition photos and asking for more time;
the org-admin doc's History paragraph updated to describe both halves.

## Decisions worth knowing

**Photos hang off the loan, not the item.** Condition is a fact about a particular hand-over, not
about the gear in general. That is also what makes the before/after comparison possible from one
query.

**Either party may photograph, but only where the stage means something.** A hand-off photo needs
the loan approved and not yet over; a return photo needs it out or just back. Both parties can add
either, because either may be the one holding the camera.

**A renewal is a child row, not a state.** The gear never changes hands during one, so the loan
stays `CheckedOut` and only its due date moves. Keeping it as its own row preserves the
conversation — *asked for another week, was given three days* — which editing the due date in place
would erase. One pending ask at a time; refusing requires a reason.

**History is assembled in memory, deliberately.** Three short per-item queries concatenated beats a
database union nobody can read, and each list is small for a single piece of gear. The server
writes each line, so every surface describing an event says the same thing about it.

**History carries no serial number**, asserted against the type — it is visible to people the
serial deliberately is not.

## Verification

- Solution builds clean, **0 warnings** (checked for warnings this time, not just errors — see the
  phase 4 README's correction for why that is called out).
- Suite **2,455 → 2,476**, all green: 21 new tests in `EquipmentLoanHistoryTests`.
- **Three guards were run against deliberately broken code first** — the photo-stage rule, the
  due-date move on approval, and the one-pending-renewal rule — and exactly the four tests covering
  them failed. Restored and re-run green.
- Migration applied to the dev SQL box; `scripts/create-database.sql` regenerated.
- Live: all seven new routes answer `401` anonymously; phase 1's anonymous catalog still `200`.
- Needs Ben's signed-in click-through, together with phases 1–4.

## Raised during this phase, not built — see the plan file for detail

Ben added three asks while this was in flight. All are **phase 6** material and none are started:

1. **Model pages** — a per-item website link aggregated across every record of a make/model, plus
   photos from all records shown anonymously, with click-through to a record's detail page only
   where the viewer has permission. Photo contribution is **opt-out per photo** (Ben's own
   refinement), so a photo containing personal information can be withheld without losing the rest.
   ⚠ Flagged: this makes photo contribution opt-out rather than opt-in, so it is worth deciding
   deliberately whether pre-existing photos are grandfathered out or retroactively contributed.
2. **Interest counters** — link clicks and equipment views, visible to org Administrators and
   SuperAdmin. Recommended as aggregate counters rather than per-viewer rows, so the feature does
   not become a log of who browsed whose equipment.
3. **Mutual loan feedback** — a lender's comment about a borrower, and a borrower's comment about
   the lender and the product. ⚠ **Needs a decision before any of it is built**: whether the
   subject of a comment can see it, and who else can, are product-defining questions. The product
   half is a review with no personal subject and belongs on the model page; the person half needs
   an audience decided first. Recommend splitting them at the schema level.
