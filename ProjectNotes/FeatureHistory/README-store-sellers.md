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
| P2 | Product history | |
| P3 | Seller editing, sale requests, taking an item off sale (also extracts the shared editor pieces) | |
| P4 | Parts list and cost basis | |
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
