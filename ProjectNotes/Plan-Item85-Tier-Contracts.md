# Item 85, arc 2 — tiers as contracts, administered live

Ben's specification, 2026-08-22, in his words near enough:

> Implement all of these but control them in administration settings. If we add something like
> extra megabytes per tier, it updates what is included on the page displaying the tiers. If we
> create a new tier, it is added. If we change a setting, it is updated on the site. Changes only
> upgrade existing paid accounts, because the tier they signed up for is a contract for the term of
> their contract. Changes are announced by internal message. If a tier changes downward, groups are
> notified a couple of weeks before their contract expires.

## The one rule everything hangs on

**Effective terms = the better of (what the group bought, what the tier now says), for the length
of the paid period.** Improvements reach everyone the moment a SuperAdmin saves them; reductions
reach a group only when a new period opens, and they hear about it first. Free-band groups have no
contract — nothing was paid, nothing is locked, changes apply immediately with a message.

That single rule is Ben's "upgrade-only" requirement with no special cases, and it means the
admin screens already built stay the only place terms are edited.

## What already exists (built earlier today)

- Tiers, per-cadence prices, and keyed limits as database rows, edited on /admin/subscription-tiers
- `PriceAtPeriodStart` frozen on the subscription — the price half of the contract is already done
- The quote endpoint, coupons, the manual provider screen
- Internal messages (`MyMessageRecord` pipeline), `ScheduledWorkService`, the notification bell

## Phase A — the contract snapshot (structure)

New table `SubscriptionContractTerms`: one row per subscription per period, written when a period
opens. Columns: subscription id, tier id, tier *name as sold*, interval, price, and the limits
**as JSON** (`SubscriptionLimit → int?`), plus period start/end.

- JSON, not rows: the snapshot is read whole, never queried by limit, and a frozen copy must not
  join back to live rows — drifting with the live tier is precisely what it exists to prevent.
- `EffectiveTermsResolver` (pure, tested like CouponMath): takes the snapshot and the live tier,
  returns the effective limit set under the better-of rule. Where all enforcement reads from.
- Backfill: existing subscriptions get a snapshot from their current tier at migration time.

Tests: better-of on every shape (cap added, cap raised, cap lowered, cap removed, feature turned
off, free band bypasses snapshot entirely). Regress the resolver both directions before trusting.

## Phase B — the public pricing page (front end)

- Anonymous endpoint `GET /api/public/subscription-tiers`: active bands, active prices, caps —
  a public projection, no audit fields, no org counts. **Traced on the anonymous path** per the
  authors-see-what-visitors-cannot rule.
- `/pricing` page: cards from the endpoint — new tier appears, changed cap updates, nothing
  deployed. Yearly saving shown from the same derived figure the admin screen uses.
- The signed-in variant of the same page marks the group's current band and, when their contract
  is better than the live tier, shows *their* numbers with "your current terms until {date}".

## Phase C — change detection and the messages (administration)

- On tier save, a diff engine classifies each change per affected group: **improvement** (cap
  raised/added-as-unlimited, price down, cadence added) or **reduction** (cap lowered, feature
  off, price up, cadence withdrawn). Mixed edits classify per field, not per save.
- Improvements: internal message to each affected group's owner + billing contacts immediately —
  "Your plan now includes…". Free-band changes always take this path.
- Reductions: a `TierChangeNotice` row per affected paid group, delivered by ScheduledWorkService
  at `max(period end − 14 days, now)` — the monthly-cadence floor — saying what changes at
  renewal on {date}. Delivered-at is recorded; the job is idempotent and catches groups whose
  period end moves.
- The admin save gets an **impact preview**: "this lowers storage for 12 paid groups — they will
  be notified before renewal; 30 free groups change immediately." A SuperAdmin should see the
  blast radius before confirming, not learn it from support tickets.

## Phase D — enforcement (the caps become real)

- `SubscriptionLimitGuard.CheckAsync(orgId, limit, currentCount)` reading effective terms via the
  resolver — one seam, used by the create endpoints for open cases, equipment, loans,
  investigations, invites, published pages, storage.
- Refusals are sentences naming the cap and the band ("Your plan includes 25 pieces of equipment"),
  and **every refusal gets a UI path** — the standing five-instances lesson. Grep the Razor for
  each payload.
- Storage is the odd one out (a running total, not a row count): counted from UploadFiles sizes,
  cached per org, checked on upload.

## Phase E — renewal application

- When a period renews (today: the manual screen; later: the provider), the new snapshot is taken
  from the *live* tier — reductions land here, which is what the phase-C notice promised.
- Renewal also re-bands on current member count (already shown as "→ Large at renewal" in the
  admin grid) and re-freezes price and member count. One code path, `PeriodOpener`, shared by the
  manual screen and any future provider, so the contract semantics cannot fork.

## Phase F — verification and docs

- Playwright: edit a tier both directions, watch /pricing change anonymously, watch the paid
  group's terms hold, watch the notice appear.
- Help docs + HelpLink for the pricing page and the group-facing subscription card, same branch.

## Order and independence

A → (B, C in either order) → D → E → F. B is independently shippable the moment A exists; D
needs only A; E needs C's notices to have gone out to be honest. Each phase lands green on its
own with the ratchet/guard suites intact.
