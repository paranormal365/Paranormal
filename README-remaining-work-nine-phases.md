# The remaining-work plan — phases 1 to 5

One branch carrying the first five phases of the nine-phase plan agreed on 2026-08-20. Each phase is
its own commit and closes its own backlog item; the branch exists so they land together after being
exercised against one another.

| Phase | What it is | Backlog |
| --- | --- | --- |
| 1 | Sitewide feature switches, and a gate that takes URLs down with the links | #108 |
| 2 | Charts — a themed ApexCharts wrapper, and stat cards | #98 |
| 3 | Sign-in events, the administrator dashboard, and per-group stats | #101 |
| 4 | The profile page, in the template's hero-and-tabs layout | #99 |
| 5 | Internal messages, in the template's mail layout | #100 |

Phases 6 to 9 — the background scheduler, the small editor items, the public feed and publications —
are not on this branch.

## The shape of the plan

Feature switches came first because everything after them has to be born flagged: a SuperAdmin can
turn any of ten sections off sitewide, and `FeatureGate` renders the not-found body during SSR so a
disabled section's **URLs die with its navigation links** rather than lingering as things you can
still reach by typing. The two new concepts in later phases — the public feed and publications —
default **off**.

Charts before dashboards, dashboards before the two layout phases, and messaging last because the
feed in phase 8 reuses its row styling and its one message renderer.

**ApexCharts is pinned at 4.7.0**, the last MIT release; 5.x is dual-licensed with a revenue cap.
Peity, which Ben named, needs jQuery — its job is done by ApexCharts' own sparkline mode instead, so
the site carries one charting library rather than two.

## What each phase found

The pattern across all five is the same, and it is worth stating once: **almost every real bug on
this branch was found by looking at the running page or by signing in as somebody else, not by
reading the code.**

- **Phase 3** — `SignInEvent` is deliberately not an audited entity and records no IP address or
  user agent. It answers "how many people signed in" and nothing else; a guard test fails if a
  column is added, because the temptation later will be "just an IP address".
- **Phase 3** — two EF queries ordered by a property of the record they were projecting into, which
  does not fail until it runs.
- **Phase 4** — `/api/my-investigations/attended` had been returning 500 **for every caller** since
  it was written. Both callers catch and fall back to an empty list, so total failure read as the
  reassuring "you haven't attended an investigation yet". The investigation map had been empty for
  everyone. That is the third swallowed exception in this codebase to hide a working-looking
  failure.
- **Phase 2** — charts rendered twice: two async creates both passed the destroy check before
  either registered. Serialised per container; the regression test reports "8 containers produced
  16 charts" against the unfixed code.
- **Phase 5** — three faults in group messaging that an owner account cannot see. Detailed below.

## Phase 5, and the account you sign in as

The Messages tab is now the template's mail idiom — a folder rail with unread counts, rows in the
template's own `<ul class="notification">`, and a reading pane under the list instead of a modal.
`MessageBody` is the point of it beyond appearance: three surfaces each rendered a message body
their own way, and phase 8's @mention and #hashtag linkification needs one place to land.

Building it turned up three separate faults, **all of them invisible to an owner account and total
for everyone else**:

1. `GET /api/organizations/{id}` required Read access through the org security service, which
   returns true for Owners and Administrators and otherwise falls through to explicit grants. A
   plain member had none, so the organisation hub — whose first call this is — told **three of
   BenCo's four members** "Organization not found or you do not have access" about a group they
   belong to and can post messages in.
2. The recipient picker fetched its list from the membership-**administration** endpoint, which
   refuses anyone who is not an org admin — that is, exactly the person most likely to be sending a
   direct message. The catch around it reported "this group has no other active members to write
   to."
3. `ChannelChangedAsync` was written, correct, and never called: the channel dropdown had
   `@bind-Value` but no `OnChange`, so the picker sat on "Loading members…" for ever.

Every test in the new `Messaging` category signs in as **James, an ordinary member**, rather than
Sarah, who owns BenCo. That is the whole reason those tests catch anything. The rest of the suite
authenticates as Sarah or the SuperAdmin, which means the product is currently exercised from its
two most privileged seats — logged as backlog item **#109**.

The direct-message bug this item was partly about — compose sending `RecipientUserIds: []` while
offering Direct Message and Case Team — is fixed, and is covered by a test that goes and reads the
message **as the recipient** rather than trusting the composer.

## Deviations from the plan as written

- **Compose stayed a dialog** rather than becoming a route. A composer with bold, italic and lists
  is the "small formatting" exception to pages-over-modals, and every mail client overlays it. The
  read view did leave its modal, which is the half that mattered.
- **No US choropleth.** The geographic cuts are top-N bars. The query behind a choropleth is the
  same `GROUP BY`; the map is a build of its own, and bars answer "which states lead" without one.
- **Anonymous visitor counts are absent, not deferred.** "New versus returning visitors" means
  traffic from people who are not signed in, and this platform records nothing about them anywhere.
  Answering it means a page-view table, which is a decision about privacy and retention rather than
  a chart. The dashboard says so in place rather than showing a number it cannot honestly produce.

## Also on this branch

`Ben.Web.Website/wwwroot/css/app.css` had never been committed, while `App.razor` has always linked
it. It is here now.

## Verifying it

The stack is the API on :5252 and the site on :5079, both run from their own project directories —
launching from the repository root serves every static file as 200 with zero bytes, which reads as
a Blazor binding bug and is not one.

```
dotnet test Ben.Web.Tests
dotnet test Ben.Web.Playwright -p:IsTestProject=true \
  --filter "TestCategory=Messaging|TestCategory=Organizations|TestCategory=Charts|TestCategory=Profile|TestCategory=FeatureFlags" \
  -e BEN_BASE_URL=http://localhost:5079
```
