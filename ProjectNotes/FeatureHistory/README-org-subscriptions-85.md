# Items 85 and 84 — organization subscriptions, and the wind-down when one ends

Two decisions taken with Ben before any schema was written:

1. **Domain plus a provider seam.** Everything except taking money — tiers, member counting,
   billing periods, lapse, and item 84's whole wind-down — is provider-agnostic. Square vs PayPal
   stays undecided and becomes a later, contained piece of work.
2. **Tiers are rows, not constants**, seeded with item 85's worked example (1–3 free, 4–10 at $15)
   plus an unbounded top band.

Ben added during the build, in this order: **coupon codes**; **all of it managed under
Administration by SuperAdmin users**; **coupons configurable by date, amount, percent, single-use
and single-use by generation**; **tiers billed monthly or yearly, possibly with a percent discount
for yearly**; **a coupon that applies to a renewal**; and **a coupon tied to one person's account**.

## Phase 1 — the domain

**Tables.** `SubscriptionTier`, `SubscriptionTierPrice`, `OrganizationSubscription`,
`OrganizationBillingContact`, `Coupon`, `CouponCode`, `CouponRedemption`. Migration
`AddSubscriptionsCouponsAndBillingIntervals`.

### The reshape, and why it happened before anything was built on top

The first cut had `MonthlyPrice` on the tier and `Code` on the coupon. Ben's later requirements are
not additions to that shape — they change what the shapes are — so the pending migration was
removed and one clean migration generated in its place. Nothing had been deployed against the old
shape, which made this free; the same change a month from now would not be.

- **Cadence is a row, not a column.** `MonthlyPrice` plus `YearlyPrice` is two columns, then three
  for quarterly, and pricing code that switches on field names. `SubscriptionTierPrice` is keyed by
  `BillingInterval`, whose values *are* the month counts — so a period end is
  `start.AddMonths((int)interval)` and a new cadence is a row somebody types.
- **The code is a row, not a column.** "Single-use" and "single-use per generated code" are one
  rule at two scopes, and the only way to have both without a special case is to put the limit next
  to the string it limits. A shared campaign is one `CouponCode`; a generated batch is many. The
  redemption lookup is the same query for both.

**Decisions worth keeping, all of them written into the entity docs:**

- **Closing is not a status.** Item 84's rule is that a group which cancels "is simply active with
  a known end date". So `CancelAtPeriodEnd` is a flag on an active subscription, not a fourth
  `SubscriptionStatus`. Modelling it as a state would make every read ask whether closing counts as
  active, and get it wrong somewhere.
- **The member count is frozen at period start**, not recomputed. Item 85 flagged that member count
  becomes a billing input and the tier boundary creates an incentive to under-report. Growing
  mid-period bills next period; shrinking the day before renewal is not a refund.
- **Billing contacts are nominated, not inferred from roles** — a treasurer is not necessarily an
  Administrator. The owner is always notified regardless, so an empty list is valid.
- **Retired, never deleted**, for tiers and coupons alike: a band or code that has priced a period
  is part of the billing record. Same rule the equipment work settled on.
- **`CouponRedemption` is separate from the discount currently applied.** When a coupon runs out
  the subscription stops pointing at it and the row stays, because "why was this group charged less
  in March?" has to remain answerable.
- **The yearly discount is stored as the price, never as a percentage beside it.** Ben asked for a
  percent discount on yearly. Storing both gives the same figure two homes and they drift the first
  time somebody edits one. The editor's "make yearly N% off" button *writes* the price;
  `SavingPercentAgainstMonthly` *reads* the saving back out. A round-trip test pins that the two
  agree, which is what lets the percentage go unstored.
- **A saving is floored, never rounded up.** $150 against twelve months at $15 is 16.66%, shown as
  16. Overstating a discount is a claim a customer can check with a calculator.
- **Months, not days.** A yearly subscription starting 31 January renews on 31 January. Adding 365
  days drifts a day every leap year and eventually bills twice in one month.
- **A missing price row means "not sold at that cadence"** — a real thing to want, and not an
  error. The free band is monthly-only, because a yearly price of zero is a question asked for no
  reason. Checkout offers what `AvailableIntervals` returns.
- **A coupon restricted to a person is restricted to the *person*, not their group.** They may run
  one group this year and another next year, and a code that stopped working when they moved would
  be worthless. It sits on the code rather than the campaign so one batch can be individually
  addressed; a campaign aimed at one person is a batch of one.
- **"Renewal" means "has ever paid", not "is paying now".** A group that lapsed last month is
  exactly who a win-back coupon is for; reading it as "currently active" shuts out the only people
  it was written to reach. Hence `FirstPaidPeriodStartUtc`, set once and never cleared.

**Logic, with 79 tests.**

- `SubscriptionTierResolver` — resolves a member count to a band, and *validates the whole list
  every time*: must start at 1, no gaps, no overlaps, unbounded top band. The failure it exists to
  prevent is silent — delete the 4–10 band and a five-member group is not "unpriced", it is simply
  never charged, and nobody reports that. It throws rather than returning null for the same reason.
- `SubscriptionPricing` — the price for a cadence, period-end arithmetic, and the yearly-saving
  round trip described above.
- `CouponMath` — percent or amount but never both, rounding **away from zero** (to-even would shave
  a cent off half of all discounts), a discount over the price is a free period rather than a
  credit, and the redemption rules in the order their sentences should be read.
- `CouponCodeGenerator` — batches of distinct codes with `O/0`, `I/1/l`, `S/5`, `B/8` and `Z/2`
  dropped from the alphabet. These get read off printed cards and forwarded emails, and every one
  of those confusions is a support message rather than a redemption. Uniqueness is still the
  database's job; the generator only avoids collisions within one call.

### Tests that were made to fail on purpose

One test initially passed for the wrong reason: 15% of $14.90 rounds to $2.24 under *both* rounding
modes, so it could not have caught the bug it was written for. Replaced with 5% of $24.50, and
verified by switching the mode and watching it fail.

After that, five of the new rules were regressed one at a time and each failed the test written for
it before being restored — the saving's floor, the month arithmetic, the per-code redemption limit,
the new-versus-renewal direction, and the per-person restriction. A rule nobody has watched fail is
a rule nobody has tested.

## Arc 2 — tiers as contracts, administered live

Ben's follow-on specification (edit tiers in admin → the public pricing page updates; paid groups
keep the terms they bought for the length of the period; improvements immediate, reductions at
renewal with two weeks' notice by internal message) is planned in detail in
`ProjectNotes/Plan-Item85-Tier-Contracts.md` — six phases, A through F.

## Still to build

**Item 85**
- SuperAdmin Administration screens for tiers (with their per-cadence prices), coupon campaigns
  (including generating a batch) and each organization's subscription
- `IPaymentProvider` seam with a manual implementation; provider chosen later
- Billing period lifecycle: open a period, freeze the count, apply a redemption, close, lapse
- The escalating notices — 2 weeks, 1 week, and at the date — on the existing `ScheduledWorkService`

**Item 84**
- `CaseStatus.Paused`, and the ~118 places that branch on `CaseStatus` checked before adding it
- Read-only after the period ends: everything stays, nothing new is added
- Client reassignment, extending `CaseTransferLog` rather than a parallel path
- Per-category share consent (history, investigations) in the two-key shape already used for
  private member photos — findings are **dual-owned**, which is why the client can share them on
  without the original group's permission
