# The founding groups promotion, and group-to-group referrals (plan, 11/02/2026)

Branch: `feature/founding-groups` (from develop). Plan only; nothing built yet.

## Ben's rules (11/02/2026)

1. The **first 10 groups** that qualify get a free year on the plan their size earns:
   2–3 members **Small Group**, 4–10 **Standard Group**, 11–25 **Large Group**, 26+ **Enterprise**.
2. A group has **one week** to get its members signed up and in the group.
3. A member counts only if they are **new to the site, or not a member of another group**, and only once they have
   **confirmed their email address**.
4. Somebody counted in one promotion group **can't be counted again**, and can't start a second free group.
5. **A group can grow for free** all year: one of the 10 never gets a bill for getting bigger.
6. After the first year, the **three most active groups get a second year free**.
7. **Most active** = field sessions, investigations, events and evidence, scored **per member**. A group's admins see
   each member's score and the group's; a member sees their own.
8. **Keeping every group in it (11/02):** a live leaderboard (only once more than one group has qualified), a
   group of the month, a score every group can reach, and paid referrals add to the score. Groups join at different
   times, so the contest has to be fair to a group that joins three months after the first (see "Timing").
9. **Referrals:** a group that names the group that referred it earns that group **one free month when it pays**,
   whatever the size of either group. Months earned during a free year are added **after** it (and after a second
   free year for a winner). Referrals are tracked from the start; a month is only earned once the referred group pays.
10. One month per referred group, earned at its first real payment. The promotion opens when Ben has checked it.
11. There are no real groups on the site yet (only Ben's own), so nothing existing needs grandfathering.

## Timing: groups that join at different times

- **Each group is scored on its own first 12 months**, from the day it qualified. Every group gets the same length
  of contest, whenever it joined.
- **The leaderboard compares like with like:** points per member per month so far, so a group 2 months in can be
  ranked fairly against one 9 months in. It appears once a second group has qualified.
- **Claims close when the 10 spots fill, or 3 months after opening**, whichever comes first, so the contest has an
  end. (Ben can change the 3 months from the admin page.)
- **The top three are decided when the last group finishes its 12 months**, comparing everyone's 12-month
  per-member scores. A group whose free year ended earlier pays as normal in between; if it wins, its next 12 months
  are free from the decision (renewals it already paid stand).
- **Group of the month** compares the same calendar month for every group still in its free year, so it's fair by
  construction and there's something to win from the first month to the last.
- **A score every group can reach** is checked at the end of each group's own 12 months: reach it and 3 free months
  are added straight away, no waiting for the others.

## What exists already

- The four plans are the live ladder (Admin → Subscription Tiers): bands 1–3, 4–10, 11–25, 26+, priced by member count.
- 100%-off coupons with a cap, a window and a number of periods (item 195), and a renewal job that keeps a free period
  going without a card (fixed 08/31).
- `AppUser.EmailConfirmed`, active `OrganizationUserMemberships`, `OrganizationJoinLink` and membership requests.
- Referral standings today are **person → coupon → cash commission** (`Coupon.ReferrerAppUserId`,
  `BillingLedgerKind.ReferralPayout`). Group-to-group months are new; they live beside it, not inside it.

## Phase 0 — plan and branch
- [ ] 0.1 Commit this README; branch `feature/founding-groups`.

## Phase 1 — data (migration owed at deploy; any `Sql()` touching a new column goes through `EXEC`)
- [ ] 1.1 `Promotion`: name, opens/closes, spots (10), days to gather (7), free months (12), winners (3),
      second-year months (12). One row; editable from the admin page, not code.
- [ ] 1.2 `PromotionEntry`: organization, claimed at, week ends at, state (Gathering / Qualified / Released /
      Disqualified with reason), plan earned, free until, second year won.
- [ ] 1.3 `PromotionMember`: entry, person, counted at. **Unique on person across the promotion** — rule 4 held
      by the database, not only by code.
- [ ] 1.4 `Organization.ReferredByOrganizationId` + when named; `ReferralMonth`: referrer, referred (unique —
      one month per referred group), earned at (the referred group's first payment), used at, taken back at.
- [ ] 1.5 Site settings for the score's weights.
- [ ] 1.6 Tests: the unique rules hold; the migration script runs twice on a reset copy (the 10/25 lesson).

## Phase 2 — who may claim, who counts (`PromotionRules`, one place)
- [ ] 2.1 A group may claim a spot while spots are left and it has no entry; its owner must pass 2.2 too.
- [ ] 2.2 A person counts when: email confirmed; active member; joined the group during its week; on the day they
      joined, no active membership of any other group (new accounts pass); not counted for another entry.
- [ ] 2.3 Each "doesn't count" has a plain reason the group page shows ("hasn't confirmed their email yet",
      "is already in another group", "is counted for another group in the promotion").
- [ ] 2.4 Tests for every rule and reason, each run against the code without it.

## Phase 3 — claiming and the week
- [ ] 3.1 Public page `/founding-groups`: what's on offer, the rules, spots left, the leaderboard (phase 6).
- [ ] 3.2 "Claim a spot" for a new group (from the page or the group's own page). Claiming starts the 7 days.
- [ ] 3.3 On the group's page during its week: a countdown, members who count, members who don't and why, the plan
      that count would earn now, and a join link to share.

## Phase 4 — the end of the week (a scheduled job)
- [ ] 4.1 At the week's end: 2+ counted members → Qualified; the plan for that count, free for 12 months; letter to
      the admins. Fewer than 2 → Released, spot back, letter saying so.
- [ ] 4.2 Free all year whatever the size (rule 5): the subscription is marked promotional and every price check
      answers $0 until it ends, while the plan name follows the group's size.
- [ ] 4.3 Tests: qualify at each band edge (2, 3, 4, 10, 11, 25, 26); release; growth never bills; no card needed.

## Phase 5 — the score
- [ ] 5.1 `PromotionScore`: per person, over the group's free year — field sessions (with readings, not empty
      ones), investigations they took part in, events their group ran that happened, evidence accepted. Weights
      from 1.5.
- [ ] 5.2 Group score = total ÷ counted members (rule 7), so a group of 3 can beat a group of 30.
- [ ] 5.3 A nightly snapshot, so pages are fast and a group can see its trend.
- [ ] 5.4 Group admins see each member's score and the group's; members see their own.
- [ ] 5.5 Tests: per-member division, ties, empty sessions don't count, only the year's window counts.

## Phase 6 — staying motivated (Ben chose all four, 11/02)
- [ ] 6.1 Leaderboard on `/founding-groups` and in each group: rank by points per member per month, updated nightly,
      shown only once 2+ groups have qualified.
- [ ] 6.2 Group of the month: the highest per-member score for the calendar month among groups in their free year
      earns a free month; not the same group two months running. Letter to the winner, line on the page.
- [ ] 6.3 The reachable score: a per-member threshold (setting) over a group's 12 months earns 3 free months at its
      year's end.
- [ ] 6.4 Each paid referral (phase 8) adds points (setting) to the referring group's score.
- [ ] 6.5 Tests: monthly winner with groups at different stages; no back-to-back; threshold at its edges.

## Phase 7 — the end of the year (a scheduled job)
- [ ] 7.1 When the last group's 12 months end (claims closed): top three by 12-month per-member score win a second
      free year, starting at the decision. Ties: higher total, then the earlier qualifier.
- [ ] 7.2 Everyone else: a letter a month and a week before the free year ends, asking for a card; after that, the
      group's normal price, or it lapses as any plan does.
- [ ] 7.3 Referral months (phase 8) are added after the free year, and after the second for a winner.

## Phase 8 — group-to-group referrals
- [ ] 8.1 "Which group told you about IsHaunted?" when creating a group and on its billing page, choosing from
      existing groups by name (not free text, so a typo can't lose a month). Fixed once the group first pays.
- [ ] 8.2 A month is earned when the referred group's **first real payment** succeeds — not a free plan, and not a
      $0 period. One month per referred group.
- [ ] 8.3 Refused: naming yourself, or a group that shares an owner or admin with yours.
- [ ] 8.4 Using a month: a paying group's next renewal is free; a group in a free year has it added after.
      A refund of the referred group's first payment takes the month back if it isn't used yet.
- [ ] 8.5 The billing card shows months earned, from whom, used and waiting.
- [ ] 8.6 Tests for each of the above.

## Phase 9 — the admin page
- [ ] 9.1 Admin → Promotions: settings, every entry and its state, counted members, scores, referrals; disqualify
      an entry with a reason; give or take back a month.

## Phase 10 — words
- [ ] 10.1 The promotion page's rules (written plainly; Ben reviews before it opens).
- [ ] 10.2 Letters: claimed, week ending tomorrow, qualified, released, monthly standing, year ending, winner.
- [ ] 10.3 Help (getting-started, organization-administration billing), change log, product PDF, screenshots.

## Phase 11 — proving it
- [ ] 11.1 Unit tests per phase, each run against the code without its change.
- [ ] 11.2 Browser test: claim → members join and confirm → the week ends → the free plan → a referred group pays →
      the referrer's month appears.
- [ ] 11.3 Full suite on a fresh database; deploy SQL run twice on a reset copy; merge; push on Ben's word.

## Gaps found

| # | Gap | Proposed answer |
|---|---|---|
| G1 | A member leaves during the week | They stop counting; the count is taken when the week ends |
| G2 | Members leave after qualifying | Nothing changes: the free year stands, shrinking or growing |
| G3 | A counted member wants to join another group later | They may; they just can't be counted for a second entry |
| G4 | A person joins two gathering groups the same week | Counted for the first group they joined; the second shows why not |
| G5 | A group qualifies, then is deleted | The spot isn't refilled after its week; Ben can reopen one from the admin page |
| G6 | Ties for the top three | Higher total score, then whoever qualified first |
| G7 | Somebody confirms their email on the last day | Counts, if confirmed before the week ends |
| G8 | Fake accounts to reach a bigger plan | Confirmed email + one-entry-per-person; Ben can disqualify from the admin page |
| G9 | Ben's own group | Excluded: SuperAdmin-owned groups can't claim |
| G10 | Contest fairness | Rules and weights shown on the page from day one; changes only apply going forward |
| G11 | The iPhone app | Claiming is on the website; the app just shows the plan the group has |
