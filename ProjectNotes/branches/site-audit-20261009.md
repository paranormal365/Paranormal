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

## What the sweep found

1,158 screens opened as six kinds of person and a phone: no console errors, no failed requests, no page
that scrolls sideways, no crash banner. `/styleguide` (a developer page) never finishes loading. The
contrast checker's "1.00:1" rows are text over photographs it can't see; the screenshots, read page by
page by two reviewers, found the real visual problems listed below.

## Findings and fixes

Code reviews of the API (six parts) and the website found about 120 issues. Each was checked against the
code before it was fixed; a few were dropped as wrong.

**Phase 1 — rights in one group reaching another, and private locations** (commit f1106706, tests b06626d5).
Case transfers, case notes and files, CMS sections and page permissions, member groups, investigation
rosters, evidence voters, hosted-event bookings/menus/files/door, group messages (public-feed bypass,
parent, case, recipients), role shaping (a secretary could make an all-rights role), my-permissions for
non-members, the bell's case messages, removed members' lingering access, Apple/Microsoft sign-in taking
over an unconfirmed account's password, confirm-email revealing a waiting request, SVG edited versions,
a client's Accept answering 500, field session files and filing, co-clients moving a case. Locations: a
hidden group's exact pin and a trilaterable distance from nearby search and group search, the case map's
exact box, hosted events with a hidden address, a group's next event, private homes in the place lookup.
Every fix has a test shown to fail without it.

**Phase 2 — money and records** (commits b06626d5, f195bc3b, and this branch's last commit).
Stripe fulfillment doubled when its two events arrived together; closed accounts' seats renewed;
audit failures after a saved change answered 500 and skipped price-change notices; group-rule refusals
answered 500; "may not apply again" was never read; approving a file request granted nothing; a proposal
could be converted repeatedly; a withdrawn request could be accepted; a refused group delete left part
of it deleted; a refund marked Failed could never complete; an occurrence was saved before its tags
were checked; re-sent sessions left file rows counting against storage; held booking places could be
edited away; expired holds blocked a new request; any account could be named as a booking guest; a
re-joined removed admin came back as admin; text longer than its column answered 500 (now a sentence,
and the anonymous request form checks its address before making an account); impossible coordinates
and cursors answered 500; polls were readable and votable by id alone; cancellation notices and the tour
door used the server's clock.

**Phase 3 — screens.** The section banner's white text on white while the photograph loads; a failed save
ending the whole session (the API client now answers an unreachable server as an ordinary failure);
failures shown as success in nine places; outages shown as "not found" or as a permissions problem;
British and technical wording (Programme, Postcode, URL slug, raw status names, C/R/U/D, internal
history in role descriptions, raw device codes, MIME types, account-system fields); a dead link; the
public case list's dates; fact tiles overflowing on phones; tables too wide for phones; squashed filter
boxes; place feed titled "A place"; "Back to paranormal365".

**Renewal grace** (Ben, 10/09/2026: "add a one time 2-week grace period for lapsed renewals", then
"once per user per year"). A Stripe plan that wasn't cancelled and reaches its period end unpaid keeps
going for 14 days while the renewal job retries daily (Stripe forgets the idempotency key after a day,
so each retry is real). One grace per paying person (the plan's setter-up, who renewals are charged to)
in any 365 days, across their groups; never extended. The billing people get a message and the billing
page shows the date. Migration `RenewalGracePeriod` adds four nullable columns to
OrganizationSubscriptions. Seats are not covered.

**Browser pass.** The hero placeholders were dark because app.css colors every placeholder from
`--ben-placeholder-color` with `!important`; the hero now sets the variable. The phone logo's white disc
was an old under-992px rule giving the bare mark a backing, which the tile's white filter whitened.
`PageProbe` (BEN_PROBE=1) settled the "blank" screenshots: most were caught mid-load.

**Docs.** Help: organization-administration (roles, applications, join link, grace), working-a-case,
your-case, your-files, getting-started, going-to-an-event, the-mobile-apps. Changelog 2.19.0. Product
documentation PDF and the seat guides rebuilt; getting-started and member/viewer/owner screenshots
re-captured.

## Left for Ben
- The calendar's "MultiDay" button label is Telerik's own text; changing it means a Telerik localization
  file.
