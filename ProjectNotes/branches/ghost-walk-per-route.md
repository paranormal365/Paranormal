# feature/ghost-walk-per-route: $29 a month per tour route, and the per-tour plan merged in

Branched from `develop` at 44dbe6ef ("Merge branch 'feature/event-credits-ghost-walk-plan' into
develop"), 10/21/2026, after Ben answered the questions from that branch.

> **DEPLOY NEEDS A MIGRATION: `20261021220216_GhostWalkPerRoute`.** It adds `Quantity` (int, default
> 1) and `UnitPriceAtPeriodStart` (decimal(18,2), default 0) to `GhostWalkSubscriptions`, and a new
> `GhostWalkRouteChanges` table. It must follow `20261021202712_GhostWalkSubscriptionsAndPersonalCredits`
> from the previous branch, which is not deployed yet either.

## Ben's answers (10/21/2026)

1. The Ghost Walk Tour subscription is **$29 a month**: now the default for
   `tours.ghost-walk-monthly-price-usd`. `tours.ghost-walk-enabled` stays a switch, off until Ben
   turns it on.
2. Printed pieces use the prices as they stand: **$99 an event, $19.80 with a paid plan**, and
   **$29 a month per tour route**.
3. "Per route tour, not for each tour, do whatever sells": **priced per tour route** — $29 a month for
   each active route (a `Tour`), with unlimited dates and guests on each. Not per date. This is what
   item 233's per-tour business tier did, so the two are **merged**: one Ghost Walk Tour subscription
   whose quantity is the routes, and the per-tour tier retired.

## How it works

**Quantity.** `GhostWalkSubscription.Quantity` is the routes paid for, never below one while the
subscription stands. `UnitPriceAtPeriodStart` is one route for the current period, frozen when the
period opened.

**Subscribing** pays for every route the group already runs (`POST …/checkout/ghost-walk?routes=`,
at least the routes running, at most 50). Fulfilment sets the quantity and the unit price from the
metadata (`ih_gw_routes`, `ih_gw_unit`).

**Gating.** A new **date** needs only a standing subscription: dates are unlimited on a paid route. A
new **route** (a tour added, or a retired one brought back) needs a free paid route. When every paid
route is running, the server refuses with a sentence saying so and what it costs, and the Tours page
shows a box, **Add a route to my Ghost Walk Tour subscription: $29.00 a month. For the rest of this
month only the days left are charged, $X, on your next bill.** Ticking it sends
`AddRouteToSubscription` (restore: `?addRoute=true`), and the route is added. The Billing card has
**Add a route** and **Take off an unused route** too (`…/billing/ghost-walk/routes/add|remove`).
Retiring a route takes one off automatically, down to the routes still running, never below one.

**Proration — Stripe's default, said plainly.** A change part-way through a month is worth the
route's whole days left (counting today) over the period's days, rounded once to the cent
(`TourBilling.Remainder`, the per-tour plan's own arithmetic). Like Stripe's default
(`create_prorations`), nothing is charged or refunded on the day: each change is a
`GhostWalkRouteChange` row waiting for the next monthly bill. What the customer sees:

- Billing card: "Adding a route adds $29 a month. For the rest of this month only the days left are
  charged — $X today — and that goes on your next bill. Retiring a route takes it off, and the unused
  days come off your next bill." Pending changes are shown as "Your next bill also has $X for routes
  added this month" / "…is $X less for routes given up this month".
- The bill (ledger line and receipt): "Ghost Walk Tour subscription — 3 tour routes × $29.00,
  monthly, plus $19.33 for routes added last month".

**Renewal** (`StripeRenewalJob`): routes × today's price + the waiting changes, on the saved card.
The changes are settled by that payment (`SettledUtc`, `SettledPaymentRef`). If the credits are
bigger than the bill, nothing is charged, the month opens anyway (a $0 charge row, no receipt), and
the rest is carried as a credit row to the next bill. Not renewed while the subscription is off sale
or set not to renew.

### Why not a Stripe Subscription object

Ben asked for "a quantity-based subscription" prorated the way Stripe does by default. The site's
billing has one rule — *the engine is the brain, Stripe is the arm* — and every recurring charge
(plans, overflow seats, the Ghost Walk month) goes through `StripeRenewalJob` as an off-session
charge on the saved card, priced by our code and recorded in our ledger. A Stripe Subscription for
this one product would be a second billing engine with its own invoices, webhooks and state to keep in
step. So the quantity and the proration live here, with Stripe's default behavior reproduced: same
rule, same timing (on the next invoice), same wording to the customer. If Ben wants real Stripe
Subscriptions, that is a separate piece of work (and would be worth doing for plans too).

## The per-tour plan, merged in — and nobody charged twice

`GhostWalkTierTransitionJob` runs **once, on the first scheduler pass after Ben turns the Ghost Walk
Tour subscription on** (while it is off, it does nothing and per-tour groups carry on exactly as
before). For every group with an active, paid-through plan on a tier that is not banded by members:

- It gets a `GhostWalkSubscription` with **Quantity = the tours its plan period was paid for**
  (`TourCountAtPeriodStart`, at least 1), **the same period** as the plan (start and end), a
  **price of $0** for that period (it is already paid, on the plan), and **the plan's saved card**
  (customer and payment method), so its next bill is the subscription's, on the date the plan would
  have renewed.
- Its per-tour plan is set **not to renew** (`CancelAtPeriodEnd`). It still stands until its paid
  period ends; the plan's lapse notices may say it is ending, which is true of the plan.
- Route changes in a carried-over period are prorated at $29 for each month in it, so a long
  (yearly) carried period is not prorated at a twelfth of the price.
- **Every per-tour tier is switched off** (`IsActive = false`), so no new group is quoted it and
  business kinds are priced by the member ladder for investigations, like anyone else.
- It records `tours.per-tour-plan-transition-done-utc` and never runs again.

While a moved group's plan is still standing, adding a route never goes through the old per-tour
add-on charge (`TourAddOnService`): the Tours controller uses the Ghost Walk subscription whenever one
stands, and the add-on only for a group without one (which, with the subscription on sale, is gated
anyway). A group on the per-tour plan whose plan has no saved card (paid by hand) is moved the same
way; when its period ends there is no card to charge, so the month runs out and new routes and dates
wait for it to subscribe.

## Settings

| Key | Label | Default |
|---|---|---|
| `tours.ghost-walk-enabled` | Sell the Ghost Walk Tour subscription | off |
| `tours.ghost-walk-monthly-price-usd` | Ghost Walk Tour price ($ a month per tour route) | **29** |

## Wording now

- Pricing page: "$29 a month per tour route", "A tour route is one walk, wherever and however often
  you run it", "Unlimited walks on every route", "Unlimited guests", and the proration sentence.
- Home ad: "$29 a month per tour route — unlimited walks and guests".
- Billing card and Tours page: as above.
- Help (organization administration, "Adding a tour, and what it costs"): the route, the ask, "What
  you see on your bill", and the move from the per-tour plan; the per-tour plan's paragraph is shown
  only while the subscription is off (`{{#if no-ghost-walk}}`), and the printed PDF leaves it out.
  Site administration: the per-tour tier is retired when the subscription is switched on; the
  subscription section rewritten per route.
- Changelog `## Unreleased`: three lines, plus one under Administration.
- Printed: the ads, the flyer and the hosted-events brochure say "$29 a month per tour route" and
  "$99 an event ($19.80 with a paid plan)"; the ghost-walk ad drops "$290 a year" and "three months
  free" (neither is offered). `docs/tour-business-pitch.html` the same. The investor overview's source
  text is updated; its PDF is not reprinted (it needs a capture run).

## Tests

`Ben.Web.Tests/Services/GhostWalkPerRouteTests.cs`, and two tests in
`EventCreditsAndGhostWalkTests` changed for the new rules (no price set now sells at $29; the
per-tour plan's pass is replaced by the move). Each fix was undone in turn, the tests rebuilt and
the ones named for it run; every one failed with its fix undone:

no $29 default · proration with no sign · checkout charging one route · checkout allowing fewer
routes than run · fulfilment ignoring the routes bought · gate ignoring the paid quantity · the yes
not adding a route · the old add-on still charged · retiring keeping the quantity · retiring below
one · dates limited by routes · renewal ignoring route changes · renewal at the frozen price ·
changes never settled · credit not carried · carried period re-dated · plan still renewing ·
carried period charged · move not waiting for the switch · tier not retired · route endpoint
unauthorized · removing below the routes running · help never saying the subscription is off.

`dotnet test Ben.Web.Tests`: 8490 passed, 6 skipped, 1 failed — `CanvasDeployScriptGuardTests`, which
reads the gitignored `Ben.Data.WebApi/appsettings.Development.json` this worktree does not have. `Ben.Web.Playwright/Tests/GhostWalkPerRouteTests.cs` (category
`EventCreditsAndTours`, all `Widths`) is written, not run.

## For Ben

1. **Stripe Subscription objects** were not used — see "Why not a Stripe Subscription object". The
   behavior and words are Stripe's default proration.
2. **The ghost-walk ad's "three months free for new tour businesses"** was dropped: there is no coupon
   on the Ghost Walk Tour subscription. Say if you want one.
3. **Yearly ghost-walk billing** ($290 a year in the old ads) is not offered; monthly only.
