# Pages that blink as they come alive (branch `fix/prerender-blink`, from develop beeb32a3, 09/24/2026)

## Goal
Blazor Server draws a page on the server, then draws it again when the live connection starts,
as a brand-new component with none of the first one's data. A page that loads in
OnInitializedAsync/OnParametersSetAsync therefore showed its content, its "Loading…" spinner, and
its content again: a visible blink on every visit. The storefront branch fixed its own pages
(ef1a87cb, [PersistentState] + a 128 KB circuit message limit); this branch covers the rest of
the visitor-facing site.

## Survey (PagesDoNotBlinkTests, unfixed develop)
| Page | Server copy | Blinked |
|---|---|---|
| `/` | spinner | no — draws a spinner, then content (not a blink; see "Not done") |
| `/events` | drawn | **yes** — "Loading…" |
| `/publications` | drawn | no |
| `/find` | drawn | **yes** — "Loading groups…" |
| `/equipment-catalog` | spinner | no |
| `/pricing` | drawn | **yes** — "Loading…" |
| `/o/{group}` | drawn | **yes** — "Loading…" |
| `/o/{group}/cases` | drawn | **yes** — "Loading…" |
| `/places/{id}` (seeded 40000001-…-0001) | spinner | no |

## The fix
- The five blinking pages carry what the server fetched into the live page with [PersistentState].
  The carried data is used once, for the address it was fetched for. It is recorded only during
  the server render, so Retry, a later visit or a changed filter always asks the API.
  - PublicEventList (`/events`, keyed by the group or "*")
  - OrgDiscovery (the first unfiltered page of `/find`)
  - PricingPage (the tiers)
  - OrgPublicHome (the org plus its tours, keyed by UrlName)
  - OrgPublicCaseList (the org plus its cases, keyed by UrlName)
- **PrerenderCarry.Fits: a 12 KB budget, not a raised limit.**
  - The carried state is sent back in the circuit's first message, and SignalR hangs up on a
    message over its limit (32 KB, kept here). The page is then drawn but DEAD, which is worse than
    a blink; the storefront met this at 35 KB.
  - Events and published cases have no length limit, and a long-running group's may be far bigger
    than the test database's. So over 12 KB of JSON (~16 KB encoded) a page carries nothing and
    simply loads again. The worst case is the old blink, never a dead page.
  - develop's Program.cs is untouched. The storefront branch raises the limit to 128 KB, and both
    changes stand when it merges.

## Proof
- PagesDoNotBlinkTests.The_page_does_not_blink_when_it_comes_alive (9 pages):
  - Failed on the 5 blinking pages before the fix; all 9 pass after.
  - Each case also fails on a refused connection and proves the page is live by typing into the
    sidebar's menu filter. The storefront's first blink test passed against dead pages because
    a page that never comes alive never blinks.
- PagesDoNotBlinkTests.The_carried_state_fits_the_connection: the 5 pages carry 2.8–10.2 KB, and
  each must stay under 16 KB. Broken once on purpose (a 2 KB limit): 5/5 failed.
- PrerenderCarryTests: 4 facts; 3 deliberate breaks of the budget, all caught.
- Full e2e on IsHauntedDb_e2e: 866 tests, 771 passed, 3 failed, 92 skipped.
  - Two of the failures pass on a re-run: the users grid's delete button, and the share dialog
    picker.
  - The_sidebar_the_bell_and_the_page_report_the_same_number (the bell says 0, the page lists 102)
    fails on plain develop as well. It is not from this branch.
- Unit suite 6,859/0.

## Not done
- `/`, `/equipment-catalog` and a place page draw a SPINNER on the server, then the content. That
  is not a blink, but those pages show no content until the connection is live. Worth doing with
  the same pattern if their first paint matters (the home page is the front door).
- Signed-in pages were not surveyed; most wait for sign-in (AuthReady) and so draw a spinner on the
  server anyway.
