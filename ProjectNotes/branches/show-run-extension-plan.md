# Show-run extensions — plan (10/24/2026)

Branch: `feature/show-run-extensions` (from develop after `feature/one-credit-one-run`).

## Ben's rules

1. "The credit covers one contiguous event."
2. "If the event is a performance like a play or musical, one credit is for the run of the show. If it is a
   weekend of ghost hunting or seminars, each weekend would be separate events."
3. "For a show run, it cannot be performances beyond a month's time." (Built: `RunTooLong`, branch
   `feature/one-credit-one-run`, not yet merged.)
4. "Add a discount to purchase an event of shows running more than a month if only extra dates are
   required. It is 50% off to add each additional month."
5. "The discount only applies if they select the show to extend and it matches one currently running or in
   the future."
6. Answered 10/24: **half their own price** ($49.50; $9.90 for a group with a paid plan) · **published shows
   that aren't over** (a draft is published first).
7. Ben, 10/24 (replaces "returned like the credit"): "If they cancel after buying an extended month or months,
   the extra credit purchased is still tied to that record of an event and when they plan it, the dates can
   extend the allotted amount of time based on the credits applied." **Months never leave the show.** The
   show's own credit still follows the 48-hour rule as today.

## The model

An **extra month** ("run month") is bought for one show and adds one calendar month to how long its run may
last. A show with its credit and N extra months may hold performances up to `first + (N + 1) months`
(exclusive), the same boundary `RunTooLong` uses today with N = 0.

**Its own table, not a kind of `EventCredit`.** Every credit query (spendable count, next to spend, spent
on, expiry warnings, the SuperAdmin grant and refund screens) would otherwise need a filter, and one missed
filter publishes an event on half a credit. Separate rows cost a little duplication and can't leak.

## Phase 0 — merge what's built

- [ ] 0.1 Merge `feature/one-credit-one-run` into develop (the one-month rule, help, pricing, change log).
- [ ] 0.2 Branch `feature/show-run-extensions` from it; README is this file.

## Phase 1 — data (migration owed at deploy)

- [ ] 1.1 Entity `EventRunMonth`: `Id`, `HostedEventId` (required; the show it belongs to, for good),
      `PaidByOrganizationId` / `PaidByAppUserId` (exactly one, check constraint), `PriceAtPurchase`,
      `Currency`, `PurchasedUtc`, `GrantedReason`, `RefundedUtc` / `RefundedByAppUserId` / `RefundedReason`,
      `ProviderCheckoutRef`, `ProviderPaymentRef`, `ReceiptNumber`, audit columns. No expiry and no
      "spent": a month is the show's from the moment it is paid for.
- [ ] 1.2 Indexes: (`HostedEventId`, `RefundedUtc`); `ProviderPaymentRef`.
- [ ] 1.3 Migration `EventRunMonths`; add it to the deploy SQL script for Ben.
- [ ] 1.4 Site setting `EventRunMonthPercent` (default 50) beside the credit price; `/admin/pricing` edits it.
- [ ] 1.5 Tests: the check constraint (two owners or none refused); the setting's default.

## Phase 2 — the rule that uses them

- [ ] 2.1 `EventRunMonths.CountForAsync(db, eventId)` — the show's months, not refunded.
- [ ] 2.2 `RunTooLong(dates, extraMonths)`: limit = `first.AddMonths(1 + extraMonths)`.
- [ ] 2.3 `ValidateAsync` passes the event's extra months on update (0 on create).
- [ ] 2.4 The refusal names the way out: "…is past that. Buy an extra month for this show, or make the later
      performances their own event." Only the first half when the show can't be extended (a draft, or over).
- [ ] 2.5 Moving the first performance moves the window with it (the rule is always first + months). No
      refund for months no longer needed.
- [ ] 2.6 A stay (`DatesAreSeparate = false`) is never offered an extra month; switching a published show to a
      stay keeps its months spent (they stop mattering) — said in the help.
- [ ] 2.7 Tests: 0/1/2 extra months; boundary dates; moving the first date; a refunded month doesn't count.

## Phase 3 — price and checkout

- [ ] 3.1 `EventRunMonthPricing.Quote(offer, subscriber, percent)`: half the buyer's own credit price,
      rounded to the cent ($49.50 / $9.90). Never zero; off sale when credits are off sale.
- [ ] 3.2 `EventRunMonthCheckout.StartAsync(db, stripe, hostedEventId, payer, months, …)`, refusing in words
      unless: the show exists and the buyer may edit it; `DatesAreSeparate`; published (Published or Live);
      its last performance hasn't passed; not called off; a credit was spent on it (or it's a plan-era
      event — see gap G3).
- [ ] 3.3 Who pays: the group (group checkout, its tax, its paid-plan price) or the person (no tax address,
      full half price) — the same two doors as credits.
- [ ] 3.4 Metadata keys: `ih_run_months`, `ih_run_event`, plus the credit keys it shares (user, list, tax).
      Several months in one purchase allowed (cap: gap G1).
- [ ] 3.5 Endpoint `POST api/organizations/{orgId}/events/{eventId}/run-months/checkout` (and the person's).
      Rate limited like credit checkout.
- [ ] 3.6 Tests: each refusal; group vs person price; subscriber price; frozen metadata.

## Phase 4 — fulfilment

- [ ] 4.1 `StripeFulfillmentService` branch for `ih_run_months`: idempotent by payment reference; writes the
      months spent on the show at once; charge + payment ledger rows with frozen tax; next receipt number;
      receipt email "N extra month(s) for {show}".
- [ ] 4.2 Fulfilled even if the show was called off between click and card: the months are the show's, and
      count when it is put back on (rule 7).
- [ ] 4.3 Tests: delivered twice → one set; called off meanwhile → still on the show; ledger totals and tax.

## Phase 5 — calling off, putting back on, refunds

- [ ] 5.1 Calling a show off: the cancel screen's sentence adds "Its 2 extra months stay with this show, and
      count again if you put it back on." The credit's own 48-hour rule is unchanged.
- [ ] 5.2 "It's back on" → republish: spends a credit as today if the old one came back; the months still
      count, so the dates can run as far as before.
- [ ] 5.3 "Copy this event" doesn't copy months (a copy is a new show).
- [ ] 5.4 SuperAdmin: list, grant and refund months, on the existing credits admin page. A refunded month
      stops counting; dates already past the new limit are kept, and the next save asks to fix them.
- [ ] 5.5 Tests: called off and back on keeps the run; copy has none; refunded month stops counting.

## Phase 6 — screens

- [ ] 6.1 The show's page (organizer): "This run can go until {date}." and, when it can be extended, "Add a
      month — $49.50" (or "Use a month you have"); the dates editor marks dates past the limit.
- [ ] 6.2 Billing card / My event credits: months bought, with the show each belongs to (a record, not a
      balance).
- [ ] 6.3 Pricing page: "Run a show longer than a month: each extra month is half the price of a credit."
- [ ] 6.4 Admin pricing: the percentage.
- [ ] 6.5 Touch targets, every width, dark/light (the site's own checks).

## Phase 7 — words and records

- [ ] 7.1 Help: organization-administration (the run, extra months, returns), site-administration (the
      setting, grants, refunds), pricing.
- [ ] 7.2 Change log (web app, Administration lines for the SuperAdmin parts); product PDF.
- [ ] 7.3 Brochures: the event hosts sheet mentions "a show's run up to a month, extra months half price".
- [ ] 7.4 Mail kind for the receipt if it isn't the generic one; templates list.

## Phase 8 — proving it

- [ ] 8.1 Unit and controller tests above; each run against the code without the change first.
- [ ] 8.2 Browser test: publish a show, refuse a date past the month, buy a month (fake Stripe), add the date.
- [ ] 8.3 Full suite on a fresh database; merge; push on Ben's word; deploy SQL handed over.

## Gaps found, and how they are handled

| # | Gap | Proposed answer |
|---|---|---|
| G1 | How many extra months may one show have? Unlimited means one credit + halves runs for years | **Needs Ben.** Suggest up to 11 (a year in all) |
| G2 | A month after its show is called off | **Ben, 10/24:** stays with the show; counts when it is put back on |
| G3 | A show published before credits existed (spent nothing) | Can still buy months; the rule is about the run, not the credit |
| G4 | Planning a 3-month run before publishing | Publish with the first month's dates, then add months; the help says so in that order |
| G5 | Buying months then removing the late dates | No refund; months were spent. Said before paying |
| G6 | Moving a paid event's dates in general (still open from the walk) | Untouched here; the run window follows the first date |
| G7 | Called off between checkout and payment | 4.2: written to the show, counts if it is put back on |
| G12 | A show called off for good, months never used | Nothing comes back (rule 7); the cancel screen says so before the button |
| G13 | The credit came back on cancel but expires before the show is put back on | Republishing needs a credit as today; the months wait on the show |
| G8 | A group's paid plan lapses after buying at $9.90 | Price frozen at purchase, as credits |
| G9 | A person's credit spent on a group's show — who may buy months? | Anybody who may edit the show, paying as the group or as themselves |
| G10 | A stay converted to a run after publish, to escape "each weekend is its own event" | Allowed; the run is then held to a month like any other. Converting back keeps months spent |
| G11 | "Running or in the future" vs. the window: a show whose last performance is tonight | Allowed until its last performance has ended (the event's own window rule) |
