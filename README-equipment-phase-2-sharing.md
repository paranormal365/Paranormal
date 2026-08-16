# Equipment Phase 2 — Sharing Personal Gear With Groups

Branch: `feature/equipment-sharing` · Backlog item **#55**, second of five phases.
Builds on `feature/equipment-personal-inventory` (phase 1).

## Why

Phase 1 gave every account a private equipment list. Item #55's next ask is that an owner can
"optionally share their own equipment list with a specific organization so fellow members can see
what gear is available for an investigation" — refined by Ben during phase 1 into a **per piece,
per group** choice rather than a single list-level toggle.

## What shipped

**Entity** — `EquipmentItemShare` (migration `AddEquipmentItemShare`): one personal item joined to
one group. Unique on `(EquipmentItemId, OrganizationId)`.

**API**

| Route | Who |
|---|---|
| `GET api/me/equipment/{id}/shares` | owner — their groups, each flagged shared or not |
| `PUT api/me/equipment/{id}/shares` | owner — replaces the whole set |
| `POST api/me/equipment/shares/bulk` | owner — share/unshare all non-retired items with one group |
| `GET api/organizations/{orgId}/equipment/shared` | any active member of that group |

**UI** — `EquipmentShareEditor.razor` (per-item checkbox list), a bulk share row on `MyEquipment`,
and `Organization/Equipment/OrganizationEquipment.razor` mounted as a new **Equipment** tab on
`OrganizationView`. Help: a *Sharing with your groups* section in `your-equipment.md` and a *The
Equipment tab* section in `organization-administration.md`.

## Decisions worth knowing

**Sharing is gated on plain membership, not a permission.** A share *is* the owner's consent, so
requiring a group permission to read shared gear would be asking the group's permission for the
owner's decision. The `Equipment` permission arriving in phase 3 governs the group's **own**
property, which is a different thing.

**Membership is checked live, never inferred from the row.** Every read re-verifies that both the
owner and the viewer are currently active members. Leaving a group hides your gear from it
immediately — but **the share row survives**, because leaving is not a reason to destroy sharing
choices that should apply again if you rejoin. There is a test asserting exactly this pair: the
gear disappears, the row stays.

**The serial number does not travel with the share.** `SharedEquipmentItemRecord` has no serial
property at all — a projection that cannot carry it cannot leak it — and a test asserts that
against the type rather than a value, so a later change to the projection cannot quietly start
including one. The owner's *name* is present, because knowing whose gear it is, is the point.

**Bulk sharing writes the same per-item rows.** It is a convenience, not a second model, so an
owner can share everything with a group and then immediately exclude one piece without unpicking
anything.

**`Organization` is `NoAction`, not `Cascade`.** Cascading here would give SQL Server two paths to
this table (`Organization → EquipmentItem → Share` as well as `Organization → Share`). An orphaned
row is harmless precisely because every read re-checks membership.

**Visibility still isn't lendability.** Sharing says who can *see* a piece; `LoanAudience` (phase 1)
says who may borrow it. The group tab's *Borrowing* column shows the difference, including gear a
member shows the group but only ever lends personally.

## Verification

- Solution builds clean, 0 warnings.
- Suite **2,403 → 2,420**, all green: 17 new tests in `EquipmentSharingTests`.
- **The two load-bearing tests were run against deliberately broken guards first** — the
  share-into-your-own-groups check removed, and the owner-still-a-member filter dropped from the
  group listing — and both failed. Guards restored, re-run green.
- Live: all four new routes answer `401` anonymously (routed and gated, not missing), and phase 1's
  anonymous catalog endpoints still return `200`.
- The signed-in screens need a login, so their click-through is Ben's: the **Share** button on a
  piece, the **All my gear** bulk row, and the group's new **Equipment** tab.

## Not in this phase

Group-owned equipment, the `Equipment`/`EquipmentCheckout` permissions and the service log are
phase 3. Borrowing anything at all is phase 4 — `LoanAudience` is recorded and displayed here, but
no checkout exists yet to act on it.
