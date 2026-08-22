# Items 85 and 84 — organization subscriptions, and the wind-down when one ends

Two decisions taken with Ben before any schema was written:

1. **Domain plus a provider seam.** Everything except taking money — tiers, member counting,
   billing periods, lapse, and item 84's whole wind-down — is provider-agnostic. Square vs PayPal
   stays undecided and becomes a later, contained piece of work.
2. **Tiers are rows, not constants**, seeded with item 85's worked example (1–3 free, 4–10 at $15)
   plus an unbounded top band.

Ben added during the build: **coupon codes**, and **all of it managed under Administration by
SuperAdmin users**.

## Phase 1 — the domain (this commit)

**Tables.** `SubscriptionTier`, `OrganizationSubscription`, `OrganizationBillingContact`,
`Coupon`, `CouponRedemption`. Migration `AddOrganizationSubscriptionsAndCoupons`.

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

**Logic, with 30 tests.**

- `SubscriptionTierResolver` — resolves a member count to a band, and *validates the whole list
  every time*: must start at 1, no gaps, no overlaps, unbounded top band. The failure it exists to
  prevent is silent — delete the 4–10 band and a five-member group is not "unpriced", it is simply
  never charged, and nobody reports that. It throws rather than returning null for the same reason.
- `CouponMath` — percent or amount but never both, rounding **away from zero** (to-even would shave
  a cent off half of all discounts), a discount over the price is a free period rather than a
  credit, and the redemption rules.

One test initially passed for the wrong reason: 15% of $14.90 rounds to $2.24 under *both* rounding
modes, so it could not have caught the bug it was written for. Replaced with 5% of $24.50, and
verified by switching the mode and watching it fail.

## Still to build

**Item 85**
- SuperAdmin Administration screens for tiers, coupons and each organization's subscription
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
