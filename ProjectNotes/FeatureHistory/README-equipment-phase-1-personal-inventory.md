# Equipment Phase 1 — Taxonomy, Personal Inventory, Public Catalog

Branch: `feature/equipment-personal-inventory` · Backlog item **#55**, first of five phases.

## Why

Item #55 asks for two related systems: a personal equipment list any account can keep, and
org-owned gear with a checkout/loan workflow. This phase builds the foundation both need — the
make/model catalog and the item record itself — and delivers the personal list end to end. Nothing
here is shared with anyone yet; that starts in phase 2.

## The five phases

| | Branch | What |
|---|---|---|
| **1** | `feature/equipment-personal-inventory` | **this one** — taxonomy, personal items, public catalog |
| 2 | `feature/equipment-sharing` | per-item × per-org opt-in sharing |
| 3 | `feature/equipment-org-catalog` | org-owned gear, `Equipment`/`EquipmentCheckout` permissions, service log |
| 4 | `feature/equipment-checkout` | the loan lifecycle, notifications, due/overdue reminders |
| 5 | `feature/equipment-loan-history` | condition photos, renewals, item history |

## What shipped

**Entities** (`Ben.Data.Source/Entities/`, migration `AddEquipmentCore`)

- `EquipmentCategory` — flat, seeded, SuperAdmin-maintained
- `EquipmentBrand` / `EquipmentModel` — accumulate from user entries, SuperAdmin-moderated
- `EquipmentItem` — one row per piece of gear
- `EquipmentItemPhoto` — gallery photos, on the existing `UploadFile` spine

**API**

- `api/equipment-catalog/*` — anonymous read of approved categories/makes/models, plus authenticated
  propose endpoints
- `api/me/equipment/*` — the caller's own items, photos, and primary-photo selection
- `api/equipment/photos/{id}/content` — authenticated photo bytes
- `api/admin/equipment-taxonomy/*` — SuperAdmin approve/reject and category CRUD

**UI** (`Ben.Web.Library/Equipment/`, `SuperAdmin/AdminEquipmentTaxonomy.razor`)

`MyEquipment`, `MyEquipmentItemEditor`, `EquipmentPhotoStrip`, `EquipmentCatalogBrowse`, the admin
moderation screen, drawer + admin-panel links, and `Help/Content/your-equipment.md`.

## Decisions worth knowing

**One item table for both ownership flavors.** `OwnerAppUserId` XOR `OwningOrganizationId`, plus the
org-only holder/service/defect columns, all ship now as nullable. Phase 3 adds org-owned gear
without reshaping the table, and phase 4 gets one checkout entity instead of two — the loan
lifecycle is identical either way, and only *who approves* differs, which is a function of which
ownership column is set.

**The XOR is enforced in the controller, not the database.** The InMemory provider the tests run
against ignores check constraints, so a database-level rule would hold in production and silently
not hold in every test — the same reasoning `Investigation.CaseId`/`PlaceId` already documents.

**Serial numbers are absent, not flagged.** The DTO field is `null` for anyone who may not see it,
resolved server-side, rather than being sent with a "don't show this" flag the client is trusted to
honour. Ownership checks match id *and* owner together and answer **404, not 403** — confirming an
id exists to a non-owner is its own small leak.

**Two shapes of "I don't know the brand", no schema branch.** A seeded `Generic / Unbranded` make
carries one generic model per category, for gear with no manufacturer at all. For a real make with
no product line — an Eveready flashlight — the editor's *I don't know the exact model* action
proposes one conventional generic model under that make, so everyone in that position lands on the
same row instead of a dozen spellings. Both are ordinary propose-and-dedupe calls.

**Visibility and lendability are separate fields, both off by default.** Ben asked for three
per-item choices: public listing, which groups see it, and whether it can be lent. Letting a group
know you own something is not offering it, so `IncludeInGlobalCatalog` and `LoanAudience` are
independent. `EquipmentLoanAudience` is `[Flags]` rather than a yes/no or a widening scale, because
the routes differ along two axes at once — a loan to a *shared group* is taken out for that group
and records which one, while a loan to a fellow group member or to any signed-in user is personal
and has no borrowing group. "My groups and people in them, but not strangers" is therefore a real
combination, and it makes `EquipmentCheckout.BorrowedForOrganizationId` nullable in phase 4.

**The public item projection cannot carry what it must not leak.** `PublicEquipmentItemRecord` has
no owner id, no owner name and no serial property at all, so a filter written wrongly later cannot
expose them; a test asserts that against the type, not the values. The photo-bytes endpoint drops
blanket `[Authorize]` for the same feature — a publicly listed item has to show photos to visitors
with no token — and answers 404 rather than 403 throughout so it cannot be used to probe ids.

**Seeder ships in the same commit as the tables.** `EquipmentTaxonomySeeder`, registered in
`Program.cs`, idempotent by name. An empty category picker makes every save fail, which is the
`ContactTypeSeeder` lesson: a feature dead on arrival for every existing deployment is not much of a
feature.

## Bugs found and fixed during this phase

**Dialog buttons rendered off-screen** — Ben hit this immediately on the item editor. Telerik's
`WindowActions` slot renders detached from its own window; both dialogs now use the in-content
`dialog-footer-actions` footer that `ConfirmDialog` already uses for exactly this reason. This is
the same defect item #68 fixed in the export dialog, reintroduced by reaching for the obvious API.

**A test helper that made its own assertion untestable** — `Build()` re-applied default Moq stubs on
top of a caller-supplied mock, so a test that configured its own storage path silently got the
default one (last `Setup` wins). Two photo tests failed for that reason and not the code's. The
helper now only stubs when the caller passed nothing.

**A stale API process served the whole feature as 404** — the dev script found an already-running
WebApi from hours earlier and reused it, so every equipment route returned 404 while the code was
correct. Verified by comparing process start time against the edit time, killed by PID, restarted.
This is the documented `dotnet run` stale-process trap; it cost real time here again.

## Verification

- Solution builds clean, 0 warnings.
- Suite **2,369 → 2,403**, all green: 34 new tests across `MyEquipmentControllerTests`,
  `EquipmentCatalogControllerTests`, `EquipmentTaxonomySeederTests`.
- **The four privacy tests were run against deliberately broken guards first** — the public-listing
  filter stubbed to `true` and the photo-access check stubbed to `false` — and all four failed, so
  they discriminate rather than passing either way. Guards restored and re-run green.
- Live, anonymously: the catalog renders all 16 seeded categories with their generic models, both
  tabs switch correctly, search and category filter return correct subsets,
  `/api/equipment-catalog/items` returns `200` with no owner or serial keys in the payload, and
  `/api/me/equipment` is `401` without a token.
- The signed-in screens (My Equipment, the item editor, the admin moderation page) need a login, so
  their click-through is Ben's — including confirming the dialog buttons now sit inside the window.
