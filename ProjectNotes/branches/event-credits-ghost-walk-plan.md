# feature/event-credits-ghost-walk-plan: events are bought, tours have their own subscription

Branched from `develop` at ea4febeb ("Merge branch 'fix/ios-negative-zero-mg' into develop"),
10/21/2026. From Ben's notes of 10/20/2026 (*IsHaunted.com AI Errors*, Website → Events, and
"Define an Event") and his rules of 10/21/2026.

> **DEPLOY NEEDS A MIGRATION: `20261021202712_GhostWalkSubscriptionsAndPersonalCredits`.**
> It adds the `GhostWalkSubscriptions` table and a nullable `PayerAppUserId` column (with its index
> and foreign key) on `BillingLedgerEntries`. Nothing else changes shape; no data is rewritten.

## Ben's rules

1. **Subscriptions cover investigations, not events.** No band includes events, the top one
   included.
2. **Event credits are bought individually, by anyone** — a group or a person. One credit, one
   event, however many contiguous days it runs.
   - The SuperAdmin sets the price in Site Settings.
   - A group with an active paid subscription pays a percentage of it (a setting, default 20, so
     $99 → $19.80).
   - Bundles: a short list of *quantity = price* offers in Site Settings; the percentage applies to
     them too.
   - The SuperAdmin's "give an event credit" action stays as it is.
3. **The Ghost Walk Tour subscription** is its own monthly subscription, any group can buy it, the
   SuperAdmin sets its price and turns it on or off; no limit on tours or guests. Off = hidden
   everywhere, said nowhere.
4. **Prices shown come from the settings**: home page, Pricing page, billing pages, help.
5. **IsHaunted collects no money from attendees** — unchanged and still said.

## What was there before

| Piece | Before this branch |
|---|---|
| Plans and events | `HostedEventEntitlement` asked `TierCapability.HostEvents`. Capabilities are *exclusion* rows, so every band nobody had unticked "included" events: those groups published against a plan cap (`SubscriptionLimit.ActiveHostedEvents`, no row = no cap) and never spent a credit. |
| Buying credits | Checkout (`…/checkout/event-credits?quantity=`) and webhook fulfilment existed for a **group** at the single price; no subscriber price, no bundles, no way for a **person** to buy. |
| Ghost walks | Nothing could buy a tour subscription. Item 233's per-tour *business tier* existed as a tier row, but nothing gated tours at all: any group could define tours, date them and take sign-ups. The only door was `org.allow-tour-signups`, which only stops a NEW group calling itself a tour company. |
| Prices on screen | Home page hard-coded **$29** a ghost walk and **$99** an event. Help said "$99". The Pricing page said "Every plan includes hosted events" / "Hosted events included". |

## Every place that claimed a plan includes events, and what it says now

| Where | Before | Now |
|---|---|---|
| `PricingPage.razor` events card | "Included in every plan — Every plan includes hosted events, so there's nothing to buy" (shown whenever every tier fail-opened HostEvents, i.e. by default); "For groups on X. Y include hosted events." | "One event, one credit" with the full price, the paid-plan price and percentage, the bundles, and a buyer chooser (any of my groups, or Myself). Intro: "Events aren't part of any plan." |
| `PlanInclusions.razor` (What each plan includes) | "Hosted events included" / "Hosted events, bought one at a time" / "No hosted events" per plan | Nothing about events on any plan. |
| Pricing bands' limit checklist | Listed every tier limit, including "N hosted events live at a time" | Lists only enforced limits (`PricingWords.IsShown`), which now excludes `ActiveHostedEvents`. "Not included:" lists only sellable capabilities. |
| `/api/public/pricing` | `IncludedCapabilities` could list HostEvents / EventCredits / EventTicketing | Never lists them (`TierCapabilities.Sellable`). |
| Home page (`HomeJoin.razor`) | "$29 a month per ghost walk…", "$99 per hosted event…" | Ghost-walk line only while on sale, at the setting's price; event line at the settings' full and paid-plan prices. The group line now says "a month for a group's investigations". |
| Help — organization-administration.md "What publishing does" | "If your plan includes running events, publishing uses one of your plan's event slots. If it doesn't… $99" | "Publishing uses one event credit. No plan includes events, the largest included…" with live prices. |
| Help — "Your group's plan" | "what an event costs on a plan that doesn't include hosting" | "No plan includes events, so every event is bought on its own…" with live prices. |
| Help — event page index | "uses a slot or an event credit" | "uses an event credit" |
| Help — Tours | "a tour is what you sell and what your plan is priced on" | Ghost Walk Tour subscription paragraph (shown only while on sale); the per-tour business plan paragraph kept and labeled as such. |
| Site settings descriptions | "a group whose plan does not include hosting events can buy a credit"; section "…when their plan does not already include it" | "anyone can buy… No plan includes events". |
| Tier editor (`AdminSubscriptionTiers.razor`) | Checkboxes "Host events", "Buy event credits", "Sell tickets (reserved)"; limit "Events at once" | Gone. Saving a tier's capabilities never stores an event capability. |
| Publish sentence (`HostedEventController.PlanSentence`) | "Your plan covers N event(s) running at once…" | One sentence, about credits only. |
| Code granting capacity from a plan | `HostedEventEntitlement` plan path (`TierCapability.HostEvents` + `ActiveHostedEvents` cap) | Removed. `EntitlementKind.Plan` removed; `Credit` keeps value 1. `TierCapability.HostEvents`/`EventCredits` documented as retired (values kept: rows carry them). |

## The transition for existing events

- **Published events are untouched.** `FirstPublishedUtc` already meant "paid for for ever": an
  event published on a plan stays live, can be taken down and put back up for nothing, and a
  cancellation of it finds no credit to return (it says so) and leaves it paid for.
- **Drafts a plan was going to cover** get a credit. `EventPlanTransitionJob` runs **once** on the
  first scheduler pass after deploy: for every never-published draft whose first date is still
  ahead, in a group with an active, paid-through plan on a tier without a HostEvents exclusion, it
  grants one credit (price 0, nothing on the ledger — the same shape as the SuperAdmin's grant)
  with the reason *"Plans no longer include events (10/21/2026). This credit covers an event you
  were already preparing when your plan still did."* It records that it ran in the hidden setting
  `events.plan-transition-done-utc` and never acts again.
- Existing tier rows (exclusions, `ActiveHostedEvents` limits) are left in the database as they
  are: nothing reads them. Saving a tier in the editor drops the events-at-once limit row.

## Settings (all in Site Settings; keys are stable)

| Key | Label | Default when unset |
|---|---|---|
| `events.credits-enabled` | Sell event credits | on (existing) |
| `events.credit-price-usd` | Event credit price ($) | 99 (existing) |
| `events.credit-subscriber-percent` | Paid-subscriber price (% of the full price) | **20**; 1–100 accepted |
| `events.credit-bundles` | Event credit bundles (one `quantity = price` per line) | none; 2–20 credits each, ≤ 6 bundles, distinct quantities |
| `tours.ghost-walk-enabled` | Sell the Ghost Walk Tour subscription | **off** |
| `tours.ghost-walk-monthly-price-usd` | Ghost Walk Tour subscription price ($ a month) | none — not on sale without one |

`AdminSiteSettingController.WhyNotValid` refuses a bad price, percentage or bundle line with a
sentence, and the Site Settings page now shows the server's sentence instead of "Couldn't save
that setting."

## How it works

**Pricing** — `EventCreditPricing` (pure): `Quote(unit, quantity, bundle, isSubscriber, percent)`
prices the whole purchase and rounds once, to the cent, half away from zero; `Share` spreads a total
across the credits so they add up to the receipt; `ParseBundles`/`ParsePercent` read the settings.
`EventCreditOffer.ReadAsync` is the one reader of the settings (checkout, the cards, the price list,
the refusal sentence). A *paid subscriber* is a group with an Active, paid-through
`OrganizationSubscription` **or** Ghost Walk Tour subscription.

**Buying credits** — `EventCreditCheckout.StartAsync` serves both doors:
`POST api/organizations/{id}/subscription/checkout/event-credits?quantity=&bundle=` (settings key
Update) and `POST api/me/event-credits/checkout?quantity=&bundle=` (signed in). Price, basis and tax
are frozen into the metadata. Fulfilment (`StripeFulfillmentService.FulfillEventCreditsAsync`) is
idempotent on the payment reference, writes the frozen total, shares it across the credits, and for
a person writes the ledger rows with no group and `PayerAppUserId` set, and posts the receipt to the
person. `GET api/me/event-credits`, `/history`, `/receipts/{id}` back the new **My event credits**
page (`/my-event-credits`).

**Spending** — publishing spends the group's oldest usable credit, then the publisher's own.

**Ghost Walk Tour subscription** — `GhostWalkSubscription` (one row per group, beside the plan).
`POST …/subscription/checkout/ghost-walk` (refused while off, or already paid through), fulfilment
via metadata key `ih_ghost_walk` (idempotent, period dated from `ih_period_start` on renewals,
never touches the plan), renewal in `StripeRenewalJob` at the **live** price on the saved card —
skipped while off sale or set not to renew. `GET/POST …/billing/ghost-walk[/cancel|/resume]` back
the billing card.

**What gates tours now** — `GhostWalkAccess.WhyNotAsync`: while the subscription is on sale, adding
a tour, restoring a retired one, and creating a calendar date for a tour need a standing Ghost Walk
Tour subscription — or an active plan on the per-tour (non-banded) business tier, so nobody already
paying for tours is caught out. Existing tours, existing dates and their guests carry on. While it
is off, nothing is gated and nothing is said.

**Help** — `HelpContentService.ApplyLiveValues`: `{{price.event-credit}}`,
`{{price.event-credit-subscriber}}`, `{{percent.event-credit-subscriber}}`,
`{{price.ghost-walk-month}}` and `{{#if ghost-walk}} … {{/if}}` passages, filled from
`/api/public/pricing/extras` on the help page. The PDF build substitutes words ("the price shown on
the Pricing page") and keeps every passage.

## Things Ben must decide

1. **Ghost walk price.** No default was given; the subscription stays off until a price is set and
   the switch is turned on.
2. **When the Ghost Walk subscription is off, tours are ungated** (my reading of "hide it everywhere
   and say nothing"). The alternative — gate tours but sell nothing — would be a door with no sign.
3. **Does a 100%-off trial plan count as "paid" for the 20% price?** As built, yes: any Active,
   paid-through plan (and the Ghost Walk subscription) qualifies.
4. **The per-tour business tier (item 233)** still exists as a tier row and still covers tours for
   groups on it. With the Ghost Walk subscription on sale you may want to retire that tier so
   business-kind groups are priced by the ladder for investigations and buy tours separately.
5. **"Maybe the $100 band with one open event"** (Ben's note 6): not built — rule 1 says no band
   includes events.
6. **Persons and tax.** A person has no address on file, so their credits are taxed at 0%.
7. **Existing published plan events** stay free to republish for ever, by the existing
   `FirstPublishedUtc` rule.
8. **Printed pieces quote fixed prices.** The ads (`docs/ads/build-ads.py`) still print "$29 a month
   per tour" and "$99 an event", and `docs/tour-business-pitch.html` offers $29 per tour with three
   months free. Print cannot read the settings; they need Ben's chosen prices before they go out
   again. The two ad lines that said a business plan hosts events were corrected and the ads rebuilt;
   the investor overview's text was corrected but its PDF was not reprinted (it needs a capture run
   first, per docs/README.md).
9. **The iPhone app** was not changed: it shows no prices, but any app text about events and plans
   should be checked against rule 1.
10. **No nav entry for My event credits.** It is linked from the Events page, the publish refusal and
    the Pricing page; a sidebar item under My Work is one line if wanted.

## Tests

- `Ben.Web.Tests/Services/EventCreditPricingTests.cs` — base, 20% (99 → 19.80), several singles
  rounded once, half-cent away from zero, bundles (price, quantity, subscriber), share to the cent,
  bundle/percent parsing and refusal, settings validation, new keys listed.
- `Ben.Web.Tests/Services/EventCreditsAndGhostWalkTests.cs` — no plan includes events (top band,
  nothing excluded, still needs a credit); the price list never lists an event capability; the
  handover (once, only plan-covered future drafts); checkout authorization and prices
  (subscriber, full, bundle, missing bundle, off sale, stranger, signed out, personal); fulfilment
  idempotency (bundle and ghost walk, concurrent double delivery); a person's credit spent at
  publish; ghost-walk gating (off, on, on without price, standing/expired, per-tour plan, calendar
  date); ghost-walk checkout and renewal (live price, off sale, cancel); price list extras; help
  values and passages; no help quotes a fixed event price.
- Each was proved to discriminate by a mutation run: each fix below was undone in turn, the test
  project rebuilt, and the tests named for it run. Every one failed with its fix undone:

  | Fix undone | Failed |
  |---|---|
  | Plan path restored (HostEvents publishes without a credit) | Even_the_top_plan_with_nothing_excluded… |
  | Price list reports every capability | The_price_list_never_reports_an_event_capability… |
  | Subscriber price ignored at checkout | A_group_with_a_paid_plan_is_charged_twenty_percent; A_bundle_is_charged… |
  | Bundle ignored | A_bundle_is_charged_its_own_price… |
  | Credit checkout not authorized | Somebody_outside_the_group_cannot_spend… |
  | Credit fulfilment not idempotent / not shared to the cent | A_bundle_delivered_twice… |
  | Ghost walk fulfilment not idempotent | A_month_delivered_twice… |
  | Tours not gated | On_with_a_price…; A_new_tour_date…; A_standing_subscription… |
  | Gate ignores whether it is on sale | While_the_subscription_is_off…; On_but_with_no_price… |
  | Per-tour business plan not honored | A_group_already_on_the_per_tour_business_plan… |
  | Renewed while off sale / at the old price | Taken_off_sale…; The_renewal_charges_the_live_price… |
  | Handover runs twice / ignores which plans covered events | The_handover_gives_a_credit…once_only; The_handover_gives_nothing_where_the_plan_had_hosting_switched_off |
  | Help keeps the off passage / leaves markers | Help_reads_today_s_prices…; Every_value_marker_in_the_help… |
  | Settings saved unchecked | The_settings_page_refuses… (5 cases) |
  | Banker's rounding / percentage per credit | Half_a_cent_rounds_away…; Several_single_credits… |
  | A person's own credit never spent | A_person_s_credit_is_spent… |
  | Ghost walk offer sent while off | While_the_subscription_is_off… |

- Four existing `HostedEventControllerTests` published with no credit, relying on the old plan
  path; they now give the group a credit first (`GiveCreditAsync`).
- `dotnet test Ben.Web.Tests`: 8475 passed, 6 skipped, 1 failed —
  `CanvasDeployScriptGuardTests.The_development_CORS_list_names_the_canvas_host`, which reads the
  gitignored `Ben.Data.WebApi/appsettings.Development.json` that a fresh worktree does not have.
- `Ben.Web.Playwright/Tests/EventCreditsAndToursTests.cs` (category `EventCreditsAndTours`, on
  `Oct16TestBase`'s `Widths`) — written, not run.
