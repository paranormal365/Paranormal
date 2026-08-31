# Feature: a green suite, and the debts left open

Branched from `develop` at `675c73a` (2026-08-31).

The goal is narrow and worth stating so it does not drift: **make `dotnet vstest` on
`Ben.Web.Playwright` report a truthful pass**, and close the small debts named in passing during
the day's work. This is not a feature branch in the usual sense — nothing here adds product. It
exists because a suite that reports 27 failures nobody intends to fix is a suite people stop
reading, and the next real regression will hide inside it.

## The baseline this starts from

Clean run against all three hosts, 2026-08-31: **350 passed, 27 failed, 28 skipped of 405, 23m11s.**
Every failure was triaged. None was caused by the day's work.

## The 27, and what each actually is

| Count | Tests | Cause | Intended fix |
|---|---|---|---|
| 12 | `CaseDetail_*Vote*`, `HomeList_*`, `List_AuthUser_SeesVoteButtons`, `VoteCounts_*`, `Home_HardNavigation*` | `features.voting` is **off** on the live database | The tests assume a feature that is switched off. Skip when the flag is off rather than fail. |
| 7 | `VideoEditorPage_*`, `Member_sees_their_group_and_their_own_billing` (`/my-videos`) | `features.video-editor` **off** | Same. |
| 2 | `Anonymous_visitor_sees_every_public_surface` (`/publications`), `EveryParameterisedRoute_RendersWithARealId` (`/publications/apple-beta`) | `features.publications` **off** | Same — the audit walks must treat a flagged-off route as "not expected", not "broken". |
| 2 | `Excluding_an_area_grays_the_role_editor_for_a_free_band_group`, `Unchecking_an_area_persists_and_rechecking_restores_it` | Look for a tier row named **"Free"**. The live ladder is Small Group / Standard / Large / Enterprise — there is no Free band. | Stale assumption. Point them at a band that exists, or seed the one they need. |
| 1 | `Pricing_renders_anonymously_and_the_toggle_switches_cadence` | Expects **`$15`**. Prices moved to whole dollars ($20/$40/$60/$100). | Stale assumption — assert the shape, not a price that changes. |
| 1 | `ThumbnailsActuallyLoad_NotJustTheirTiles` | localhost and ishaunted.com share ONE database but have SEPARATE file storage, so rows exist whose bytes were never written here. Documented in `feedback_side_database_not_rebuild`. | Not an app fault. The test must be able to tell "bytes were never written on this machine" from "the code is broken". |
| 1 | `OrgRequests_Tab_RendersWithRequestCards` | Page rendered its header and nothing else. | **Genuinely unknown — investigate first.** The only one of the 27 that might be a real defect. |

**The rule for this branch:** a test is only allowed to become "skipped" when the thing it tests is
genuinely switched off in the environment it is pointed at. Skipping a test because it is
inconvenient is how a suite becomes decorative, and that is the failure this branch exists to
prevent, not to commit.

## The other debts, named during the day

- **`IFileStorageService` has no directory delete.** `OrganizationPurge` deletes a group's files
  and leaves the emptied folder behind. Ben asked for "then location for the files if none other
  exist".

  **DECIDED 2026-08-31: do not add one.** Ben intends to move to cloud hosting if the site takes
  off, and `LocalFileStorageService` is the only implementation of the interface — a blob store is
  a new class, not a rewrite, precisely because the interface stays small and the database holds
  relative paths. A directory is a local-filesystem concept that does not exist in S3 or Azure
  Blob, so putting `DeleteDirectory` on the interface would make the cloud implementation lie
  about a concept it does not have. An emptied folder costs nothing locally and will not exist at
  all after the move. If prefix cleanup is ever wanted, `ListFiles` + `DeleteAsync` expresses it
  in terms both implementations can honour.
- **The insights panel has never been seen populated.** It draws only when other people have
  recorded at the same place, and no seeded session satisfies that. The logic has 12
  mutation-verified tests; the rendered panel has been seen only in its absent state.
- **iOS is not tap-verified beyond one capture test.** The Simulator integration needs
  `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer`, which needs Ben's password.

## Deliberately NOT in scope

The open backlog items (183 case deletion, 187, 189–192, 196–198, 200). Those are product
decisions and features; this branch is about the suite telling the truth. Mixing them would make
this branch impossible to review and impossible to abandon.

## What the suite found that was not a test problem

**There is no free band, so an unsubscribed group gets the whole product.**
`TierAreaResolution.FreeTierAsync` identifies the free band by PRICE — a banded, active tier whose
prices are all zero. The live ladder starts at $20, so nothing matches; `EffectiveTierAsync`
resolves no tier for a group that pays nothing, and the resolver fails open to ALL NINE AREAS.
That fail-open is deliberate and correct — "inventing a restriction would lock people out of a
site that never said it would" — but it means the free lane currently has no ceiling at all.

The fix is a pricing decision, not code: create a **$0 banded tier** in Admin → Price Bands whose
area checklist defines what free includes. Everything then resolves on its own — areas, limits,
the pricing page, and the role editor's plan note. `Excluding_an_area_grays_the_role_editor_for_a_free_band_group`
skips until that band exists, and enforces it again the moment it does.

## Done when

- A clean `vstest` run reports zero failures, with every skip explained by a flag that is genuinely off.
- `OrgRequests_Tab` is either fixed or understood and recorded.
- The storage interface can remove an emptied directory, or the gap is written down as a decision
  rather than an omission.
