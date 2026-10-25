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
6. "Could we make the extra months apply to any new show but they can only extend the dates and not be
   considered being used to start a new one. It is separate than a credit for an event."
7. Answers, 10/24: **half their own price** ($49.50; $9.90 for a group with a paid plan) · bought only for a
   **published show that isn't over** · when a show is called off its months **always come back** · a held
   month **lapses a year after purchase**, with a warning · **up to 11 extra months** per show (a year in all).

## The model

An **extra month** is its own thing, never a credit:

- **Bought for a show.** The buyer chooses a published, unfinished show's run; the month is applied to it at
  once. That is the only way to buy one, and the only time the 50% price exists.
- **Applied to a show** adds one calendar month to how far its run may go: performances up to
  `first + (1 + months) months` (exclusive) — the boundary `RunTooLong` uses today with 0 months.
- **Held** when its show is called off (always, whatever the timing): it comes back to whoever paid, and can
  be applied to any other published, unfinished show's run, free. It lapses a year after it was bought.
- **Never publishes anything.** Publishing needs a credit; a month can't stand in for one, anywhere.
- **At most 11 on one show.**

**Its own table, not a kind of `EventCredit`.** Every credit query (spendable count, next to spend, spent
on, expiry warnings, grants, refunds) would otherwise need a filter, and one missed filter publishes an event
on a month. Separate rows can't leak into publishing.

## Phase 0 — merge what's built

- [ ] 0.1 Merge `feature/one-credit-one-run` into develop (the one-month rule, help, pricing, change log).
- [ ] 0.2 Branch `feature/show-run-extensions`; this file is its README.

## Phase 1 — data (migration owed at deploy)

- [ ] 1.1 Entity `EventRunMonth`: `Id`; `OwnerOrganizationId` / `OwnerAppUserId` (exactly one, check
      constraint, as `EventCredit`); `PriceAtPurchase`, `Currency`, `PurchasedUtc`, `ExpiresUtc` (+1 year),
      `ExpiryWarningSentUtc`; `AppliedUtc`, `AppliedToHostedEventId` (null while held); `GrantedReason`;
      `RefundedUtc`, `RefundedByAppUserId`, `RefundedReason`; `ProviderCheckoutRef`, `ProviderPaymentRef`,
      `ReceiptNumber`; audit columns. `IsHeld(now)`: not applied, not refunded, not lapsed.
- [ ] 1.2 Indexes: (`AppliedToHostedEventId`, `RefundedUtc`); (`OwnerOrganizationId`); (`OwnerAppUserId`);
      `ProviderPaymentRef`; (`ExpiresUtc`) for the warning job.
- [ ] 1.3 Migration `EventRunMonths`; add it to the deploy SQL script for Ben.
- [ ] 1.4 Site settings beside the credit price: `EventRunMonthPercent` (default 50),
      `EventRunMonthsPerShow` (default 11). `/admin/pricing` edits both.
- [ ] 1.5 Tests: the check constraint (none or two owners refused); defaults; `IsHeld`.

## Phase 2 — the rule

- [ ] 2.1 `EventRunMonths.AppliedToAsync(db, eventId)` — applied, not refunded. Count only.
- [ ] 2.2 `RunTooLong(dates, months)`: limit = `first.AddMonths(1 + months)`.
- [ ] 2.3 `ValidateAsync` passes the event's applied months on update (0 on create).
- [ ] 2.4 The refusal offers the way out when there is one: "…is past that. Add a month to this show, or make
      the later performances their own event." Only the second half for a draft, a stay, an ended show, or
      one already at 11.
- [ ] 2.5 The window follows the first performance wherever it moves; months no longer needed stay applied.
- [ ] 2.6 A stay is never offered a month. A published stay switched to a run is held to its run's limit;
      switched back, its months stay applied and stop mattering (help says so).
- [ ] 2.7 Tests: 0/1/11 months; boundaries; moving the first date; a refunded month stops counting.

## Phase 3 — buying

- [ ] 3.1 `EventRunMonthPricing.Quote(offer, subscriber, percent, count)`: half the buyer's own credit price
      per month, to the cent ($49.50 / $9.90). Never zero; off sale whenever credits are.
- [ ] 3.2 `EventRunMonthCheckout.StartAsync(db, stripe, showId, payer, count, …)` refuses in words unless:
      the buyer may edit the show; it is a run (`DatesAreSeparate`); Published or Live; its last
      performance hasn't ended; not called off; months already applied + count ≤ 11.
- [ ] 3.3 Who pays: the group (its tax, its paid-plan price) or the person (full half price, no tax
      address) — the two doors credits have.
- [ ] 3.4 Metadata: `ih_run_months` (count), `ih_run_show` (event id), plus the shared user/list/tax keys.
- [ ] 3.5 Endpoints: `POST api/organizations/{orgId}/events/{eventId}/run-months/checkout` and the person's;
      rate limited like credit checkout.
- [ ] 3.6 Tests: each refusal; group vs person; subscriber; the cap; frozen metadata.

## Phase 4 — fulfilment

- [ ] 4.1 `StripeFulfillmentService` branch for `ih_run_months`: idempotent by payment reference; writes the
      months applied to the show; charge + payment ledger rows with frozen tax; next receipt number; receipt
      "N extra month(s) for {show}".
- [ ] 4.2 The show changed between click and card (called off, ended, already at 11): the months are written
      **held** instead of applied, and the receipt says they're waiting to be used. Nothing lost or silent.
- [ ] 4.3 Tests: delivered twice → one set; each "changed meanwhile" case → held.

## Phase 5 — applying held months, calling off, refunds

- [ ] 5.1 Apply held months: `POST …/events/{eventId}/run-months/apply` with a count; same checks as 3.2
      except payment; oldest-expiring first; one save. Not a credit: publishing never looks at this table.
- [ ] 5.2 Calling a show off (organizer, no-go, venue withdrew, IsHaunted removed): every applied month comes
      back held, whatever the timing; its year still counts from purchase. The cancel screen says so before
      the button: "Its 2 extra months come back to you to use on another show until MM/DD/YYYY."
- [ ] 5.3 "It's back on" doesn't take months back automatically: the organizer applies them again (they may
      have used them elsewhere meanwhile). The screen says so.
- [ ] 5.4 "Copy this event" copies no months.
- [ ] 5.5 Expiry warning job includes held months (a week before, once).
- [ ] 5.6 SuperAdmin: list, grant and refund months on the credits admin page. Refunding an applied month
      stops it counting; dates already past the new limit stay, and the next save asks to fix them.
- [ ] 5.7 Tests: apply held; apply refused (draft, stay, ended, cap, lapsed, someone else's); every kind of
      calling off returns them; back on doesn't re-apply; copy; expiry warning once; refund.

## Phase 5b — what confirmed guests are told when an event is called off (Ben, 10/24)

Ben: "When calling off and there are already confirmed guests, the organizer can choose to send a message and
email the guests with either the new date or how to request their refund."

- [ ] 5b.1 The cancel box, when anybody holds a confirmed place, offers a choice: **It's moving to a new date**
      (a date, and optionally where to book) or **How to get your money back** (the organizer's own words:
      who to contact, how). Either is optional; the reason stays.
- [ ] 5b.2 `CancelHostedEventRequest` carries `NewDate` and `RefundHow`; the called-off letter
      (`EventGuestMailer.SendCalledOffAsync`) says the one chosen, plainly, instead of only "nothing was paid
      here". The site message to each guest (bell) says the same.
- [ ] 5b.3 The event's public page, once called off, shows the new date or the refund words to people who had
      a place.
- [ ] 5b.4 Tests: each choice reaches the letter; neither chosen keeps today's letter; words kept as text.
- [ ] 5b.5 Help: going-to-an-event ("If the event changes") and organization-administration.

## Phase 6 — screens

- [ ] 6.1 The show's page (organizer): "This run can go until MM/DD/YYYY." and, when it may be extended,
      "Add a month — $49.50" and, when months are held, "Use a month you have (N)". The dates editor marks
      dates past the limit.
- [ ] 6.2 Billing card / My event credits: held months with their expiry, and months applied, by show.
- [ ] 6.3 Pricing page: "A show's run past a month: each extra month is half the price of a credit, up to a
      year. Extra months only lengthen a run; they can't publish an event."
- [ ] 6.4 Admin pricing: the percentage and the cap.
- [ ] 6.5 The site's own checks: touch targets, every width, dark and light.

## Phase 7 — words and records

- [ ] 7.1 Help: organization-administration (runs, extra months, calling off, applying held months),
      site-administration (settings, grants, refunds), the pricing help.
- [ ] 7.2 Change log (web app; Administration lines for SuperAdmin parts); product PDF.
- [ ] 7.3 Brochures: the event hosts sheet adds "A show's run up to a month; extra months half price".
- [ ] 7.4 Mail: the receipt (existing receipt mail) and the expiry warning (a kind beside the credit one).

## Phase 8 — proving it

- [ ] 8.1 Unit and controller tests above, each run against the code without the change first.
- [ ] 8.2 Browser test: publish a show; a date past the month is refused; buy a month (fake Stripe); the date
      saves; call it off; the month is held; apply it to another show.
- [ ] 8.3 Full suite on a fresh database; merge; push on Ben's word; deploy SQL handed over.

## Gaps found, and how they are handled

| # | Gap | Answer |
|---|---|---|
| G1 | How many months on one show | **Ben:** up to 11 (a year in all); a setting |
| G2 | A month after its show is called off | **Ben:** always comes back, usable on any other show |
| G3 | Do held months lapse | **Ben:** a year after purchase, warned first |
| G4 | Could a month publish an event | Never: publishing reads only credits (rule 6) |
| G5 | A show published before credits existed (spent nothing) | Can still take months; the rule is about the run |
| G6 | Planning a 3-month run before publishing | Publish with the first month's dates, then add months; help says so in that order |
| G7 | Months bought, then the late dates removed | No refund; the months stay applied. Said before paying |
| G8 | Moving a paid event's dates in general (open from the walk) | Untouched; the run window follows the first date |
| G9 | The show changes between checkout and payment | 4.2: the months arrive held |
| G10 | Paid plan lapses after buying at $9.90 | Price frozen at purchase, as credits |
| G11 | A person's credit spent on a group's show — who may buy | Anybody who may edit the show, paying as the group or as themselves; held months come back to whoever paid |
| G12 | Held months applied to another group's show | Only to shows the holder may edit (a person's months: shows they may edit) |
| G13 | Back on after being called off | Months don't re-apply by themselves (5.3) |
| G14 | A stay switched to a run to dodge "each weekend is its own event" | Held to the run's month limit like any run |
| G15 | "Running or in the future": last performance tonight | Allowed until it has ended (the event's own window rule) |
| G16 | Bundles of months | Not offered; buy as many as needed in one checkout, up to the cap |
