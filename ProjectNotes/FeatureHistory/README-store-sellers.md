# Store sellers (backlog 251), branch `store-sellers`

Taken from `storefront` on 09/24/2026. It won't merge until the store ships. **The plan** is at
`~/.claude/plans/lively-jingling-peach.md` (approved 09/24/2026): sixteen phases, P0–P15. Ben's decisions
and the defaults for the smaller questions are recorded there, and summarised below.

## Goal
Member sellers (the additive Seller site role) list their own items in the store and ship their own
packages. The site takes payment, files the tax, and pays each seller by hand against a tracked earnings
ledger. Alongside that, every product gains:
- a parts list and cost basis;
- seller files that buyers can download;
- FAQs and questions to the seller;
- versions;
- a reviews switch;
- return and warranty text;
- videos.

## Ben's decisions (09/24/2026)
- **Payouts:** by hand. The site is the merchant of record, and an admin marks a seller paid. There's no Stripe Connect.
- **Packages:** each seller ships their own package. The buyer pays the flat rate per package, and a package ships free once its items reach the free-shipping amount.
- **Status:** an order is **Partially shipped** while some of its packages have shipped and others haven't. Each package has its own carrier and tracking, or "No tracking provided". Checkout tells the buyer when an order will arrive in more than one package.
- **Labels:** the seller buys the label and is credited the package's shipping, or the flat rate if the package shipped free.
- **Tax:** always worked out from the site's own address.
- **What the buyer sees:** "Package n · Ships from {seller's display name}".
- **What a seller earns:** their cost basis plus the asking price they propose in the sale request, fixed at checkout. The site sets the selling price and absorbs discounts and Stripe fees.
- **What a seller can do:**
  - Add drafts, and delete a draft that has never sold.
  - Manage options and variants (a new variant starts off, unpriced) and their own stock.
  - See the price but never change it.
  - Take an item off sale.
  - An admin approves and prices the item before it goes on sale.
- **Seller files:** each file is marked for buyers or kept private.
- **Point 8** of Ben's list becomes the seller's earnings summary.

## Phases (status)
| Phase | What | Status |
|---|---|---|
| P0 | Groundwork: sale counters (bug: product `UnitsSold` was never written) and the shared package-pricing function | Built 09/25/2026 — StoreStockTests + `A_sale_counts_on_the_product_too_and_says_when` (1 break caught), migration StoreProductSaleCounters (backfill), StoreParcelPlan + StoreParcelPlanTests 7 (3 breaks caught); StoreLowStockJobTests made date-independent (it failed from midnight UTC 09/25); unit suite green (7,457) |
| P1 | Seller role end to end, read-only workspace, help by role, demo seller | Built 09/25/2026 — Seller policy (a SuperAdmin is NOT a seller), `IsSeller` through Me → token → user state → persisted layout state, `api/seller/store/products` behind `SellerStoreControllerBase.Mine`, Selling → My Items at `/store/selling`, help `role:` front matter, Hazel Marsh seeded with one item on sale and one draft; unit tests (SellerHandler, seller controller, guards, help by role, seeder) with 5 breaks caught; Playwright StoreSellerWorkspaceTests 2/2, both shown failing against a leaking list and an unguarded page; unit suite green (7,479) |
| P2 | Product history | Built 09/25/2026 — append-only `StoreProductChange` (migration StoreProductHistory, backfills a "Created it." line per product), `StoreProductHistory` writes one plain sentence per area in the same save as the edit (stock wrapped in one transaction with its line); every admin product write and both stock-page writes leave a line, an empty or refused save leaves none; admin History tab (shared `Store/Editor/StoreProductHistoryList`) and `api/seller/store/products/{id}/history` (no price lines, staff shown as "The store"); the seller's tab arrives with the seller editor in P3. StoreProductHistoryTests 11 + EveryProductWriteLeavesHistoryTests 3, 7 breaks caught; Playwright `The_history_tab_says_who_changed_what` (shown failing when the tab doesn't load); AdminStoreCatalogTests + StoreSellerWorkspaceTests 12/12; unit suite green (7,493); help: site-administration "A product's history"; go-live and deploy docs name the migrations |
| P3 | Seller editing, sale requests, taking an item off sale (also extracts the shared editor pieces) | Built 09/25/2026 — every product rule moved from the admin controller into `StoreProductEditor` (+ `StoreProductSale`, `StoreProductRecords`), shared by `SellerStoreProductEditController`; seller requests carry no store field, a seller's new/generated variant starts off at $0.00 and their saves keep the store's price; `StoreProductSaleRequest` (one open per item, filtered unique index + conditional-update claims) with `/admin/store/sale-requests` (readiness list, approve fixes `SellerAskPerUnit`, decline needs a reason), nav entry, dashboard tile, Waiting badge; admin Put on sale refused on a seller's item; stale-edit 409 on details/options saves (both editors); seller editor `/store/selling/items/{id}` and Add an item, built from shared `Store/Editor` components (specs, options, variants grid, pictures, stock dialog, preview) that the admin editor now uses too (829 → ~590 lines); `StoreSellerAlerts` bells both ways; migration StoreSellerSaleRequests (FirstOnSaleUtc backfill). StoreSaleRequestTests 19, guards extended (history through the editor, refusals through shared components), 10/11 breaks caught (the survivor is equivalent: approval re-checks inside the switch and rolls the claim back); Playwright Store category 92 + AdminPageTests green incl. `A_draft_goes_on_sale_by_asking_and_comes_off_by_the_seller`; StoreBrowseTests' sold-out test now finds its card by name — P0's real sold counts reorder "Popular"; unit suite green (7,519) |
| P4 | Parts list and cost basis | Built 09/25/2026 — `StoreProductPart` (per-pack or per-piece price to 4 places, pieces a pack, used a unit, links, picture, on hand) + product `OtherCostPerUnit`/note (migration StoreProductParts); shared pure `StoreCostMath` (rounds once) used by the server and the editors' live total; `StoreCostBasisReader`; Parts & cost tab in both editors (`Store/Editor/StorePartsEditor`), history area Parts; Hazel's REM pod seeded at $34.50 a unit (each seeded piece now idempotent). StoreCostMathTests 4, StorePartsTests 9, StorePublicRecordsCarryNoCostTests; 6/6 breaks caught; Playwright `The_parts_tab_says_what_a_unit_costs_to_make`; unit suite 7,533 |
| P5 | Packages on the server | |
| P6 | Packages for the buyer | |
| P7 | Shipping each package, and Partially shipped | |
| P8 | Refunds with packages | |
| P9 | Economics and the Stripe fee | |
| P10 | Earnings ledger, payouts, seller summary | |
| P11 | Seller files and buyer downloads | |
| P12 | FAQs and questions | |
| P13 | Versions | |
| P14 | Page extras | |
| P15 | Help, screenshots, final runs | |

## Rules that bit
- (P0) Pulling the editor pieces out into shared components moved to P3, where the seller editor first uses them. Pulling them out earlier, with nothing else using them yet, would only move code around without being able to prove the move worked.
