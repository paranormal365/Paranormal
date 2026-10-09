# Site audit — gaps, programming faults and awkward screens (10/09/2026)

Ben, 10/09/2026: *"Audit the site for any gaps programming issues or visually awkward pages or
elements. Fix them in a new feature."*

Branch `feature/site-audit-20261009`, cut from `fix/order-dependent-tests-255` so the audit's own
test runs have that branch's seeding and test fixes.

## How it is run

1. **Code, read only.** Two passes: the API (authorization gaps, silent failures, crashes on edge
   input, untranslatable queries, settings saved and never read) and the website (failures shown as
   success, auth races, dead links, layouts that cannot fit a phone, wording, missing empty states,
   leaked subscriptions).
2. **Every screen, as everybody.** `SiteSweep` opens every route as a visitor, a client, a viewer, an
   ordinary member, a group's owner and the site admin, then every public route on a phone. It records
   console errors, failed requests, stuck loading, sideways scrolling and the visual auditor's
   findings (text on a card edge, clipped content, contrast, broken images).
   `BEN_SWEEP_SHOTS=1` (new) also saves a picture of each screen for the owner, the admin on admin
   pages and the phone visitor, so awkward layouts the checks cannot name can be seen.
3. **Findings, then fixes.** Each finding is checked against the code before it is fixed. Programming
   faults get a test that fails without the fix. Help text and screenshots are updated for anything a
   person would notice.

## Findings and fixes

(Filled in as the audit runs.)
