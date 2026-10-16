# Public event page: in-page links, the silent grey button, "352 hr" (10/16/2026)

Branch `fix/public-event-page`, from `develop`. Three bugs seen on production on 10/16/2026, on
https://ishaunted.com/o/apple-beta/events/app-review-night and its calendar twin.

## 1. "Ask for a place" went to the home page

**Cause.** The phone's sticky button (`HostedEventShowcase.razor`) was `<a href="#event-act">`.
The site has `<base href="/">`, so a browser resolves a bare fragment against the base:
`https://ishaunted.com/#event-act`, the home page. Blazor sees a different path and navigates there.

**The established pattern.** Nothing relies on `<base>` to find its way (App.razor's assets, the
Safari base-href fix of 2026-08-11): a link names its page. Blazor treats a link whose path and
query match the current page, with a fragment, as "same page, different spot" and scrolls without
navigating (checked in `blazor.web.js` 10.0.9: origin + pathname + search equal and a non-empty
hash → `pushState` and `scrollIntoView`).

**The fix, used everywhere.** `Kit/JumpLink.cs`: `JumpLink.To(nav, "event-act")` returns
`/path?query#event-act` for the page being shown. A plain `<a href="@JumpLink.To(Nav, "x")">`,
not a wrapping component, so scoped CSS still reaches the link (the sticky button is styled that
way) and string `Href` parameters (fact tiles, stat cards) can be given one.

**Every same-page fragment link found (32 in 10 files), all now `JumpLink.To`:**

| File | Links |
| --- | --- |
| `Organization/Public/HostedEventShowcase.razor` | `#event-act` (the reported bug) |
| `Organization/OrgSettingsManager.razor` | the settings index: `#settings-billing`, `-tours`, `-events`, `-promote`, `-attribution`, `-group`, `-event-types`, `-member-levels`, `-duties`, `-duty-matrix` |
| `Organization/Public/OrgPublicNav.razor` | `#org-tours` (shown only on the group's home page) |
| `Organization/Public/PublicTourPage.razor` | "Pick a night" `#tour-dates`; fact tiles `Href: "#tour-dates"`, `Href: "#tour-slideshow"` |
| `Manage/Tours/OrgTourPage.razor` | index `#tour-details`, `-guides`, `-mail`, `-gallery`, `-dates`, `-reviews` |
| `Manage/Events/OrgEventPage.razor` | index `#event-details`, `-dates`, `-venue`, `-guests`, `#group-field-sessions`, `#event-state`; "See the removal and appeal" `#event-removed` |
| `Shared/CaseVoteWidget.razor` | comments button `#case-comments` |
| `SuperAdmin/AdminDashboard.razor` | stat card `Href="#sign-ins"` |
| `Shared/HomeHero.razor`, `Shared/HomePitch.razor` | `#the-app` (worked only because home is `/`) |

Also: `PublicEventDetail.razor`'s menu links were `o/{org}/events/{slug}#menu-{id}`, relative and
resolved through `<base>`; now `JumpLink.To`.

**Arriving with a fragment.** Links from other pages already named their page
(`OrgPublicSection`'s "Ask for a place" → `{event url}#event-act`, the event manage page's
`/o/{org}/events/{slug}#hosted-room`). They did not go home, but Blazor scrolls to the top after a
page change and never to the fragment, so they landed at the top. `PublicEventDetail` now calls the
help guides' `scrollToHashWhenSettled` once the event has drawn. Other cross-page fragment links
(`/help/...#x`, `/store/p/{slug}#reviews`, `/organizations/{id}/billing#event-credits`,
`/admin/events#appeals`, `/#whats-near-you`, `pricing#events` via sign-in) were left as they are.

**`href="#"` placeholders** (`BenNav` group toggles, `OrganizationView` ×3, `OrgInvestigations`,
`InvestigationRoster` ×2, the CMS "Button link" snippet). Once the page is live Blazor honors
`@onclick:preventDefault` (its link interception returns on `defaultPrevented`), so they do
nothing, as meant. Before the circuit connects a click went to the home page. App.razor now
cancels the default of any `href="#"` click in the capture phase; Blazor still delivers the click
to the component. No change to those files.

**Guard.** `Ben.Web.Tests/Website/SamePageLinkGuardTests.cs` scans every `.razor`, `.cshtml`,
`.html`, `.js` and `.cs` in Ben.Web.Website and Ben.Web.Website.Library (vendored folders with a
`VENDORED.md` and `*.min.js` skipped), comments stripped first, for `href=`/`Href=`/`Href:` followed
by a quoted `#something`. Empty allow-list; an allow-list entry that matches nothing fails. Run
against develop's markup it fails with all 32 links listed above.

## 2. The grey button with no reason

`AskForPlaceForm` disabled "Ask for a place" unless a night was ticked, the contact was enough
(first name, last name, a phone with 7–15 digits) and no dietary note lacked a name. Only the
night said why. A signed-in guest whose account had a name and no phone saw a filled-in form and a
dead button.

- `BookingContactDraft.WhatIsMissing(withEmail, organizationName)`: the one next thing, in
  screen order: "Add your first and last name." / "Add your first name." / "Add your last name." /
  "Add your email address so we can send you the link." / "Check your email address. It needs an
  @." / "Add a phone number so Apple-Beta can reach you." / "Check your phone number. It needs 7
  to 15 digits." `IsEnough` is now `WhatIsMissing(...) is null`, so the two cannot disagree.
- `BookingPartyDraft.WhatIsMissing`: "Give each dietary note a name."
- Shown beside the button (and linked with `aria-describedby`) on: the ask form (`#ask-missing`,
  replaces `#ask-needs-a-night`), the seat/room picker (`#picker-missing`), the event page's
  sign-up by email for a hosted event (`#attend-missing`) and for an ordinary event
  (`#attend-email-missing`), and the party panel's Save (`#{prefix}-party-missing`).
- `BookingContactFields`: each field's label says "(required)", with `aria-required`.
- Checked and left: tour seats on `PublicEventDetail` (gated on busy only), the door's walk-up
  "Let them in" (gated on no places left, which the door's room line already says in red).
- The buttons stay disabled rather than refusing on click, so the Playwright tests that expect
  `#ask-send`/`#picker-hold` to be disabled still hold.

## 3. "352 hr"

`Ben.Web.Services/EventLength.cs`, used by `PublicEventDetail` and `PublicTourPage`:

- Up to 24 hours on the wall clock: real elapsed time, "45 min", "4 hr", "2 hr 30 min".
- Longer, on the event's own clock: ends earlier in the day than it started → nights (the dates
  it crosses); otherwise days (the dates it touches). A multiple of 7 → "1 week"/"N weeks"; over
  21 → "about N weeks"; 60 or more → "about N months".
- A hosted event with more than one date: "15 nights, 6:00–11:00 PM" when every date keeps the
  same hours, else "15 nights"; never rounded to weeks. The old "over 15 nights" note is gone.
- Over the 11/1/2026 fall-back in America/Chicago: 10 PM to 2 AM is "5 hr" (the hour that
  really passes); 6 PM to 6 PM is "25 hr"; a run of nights counts its dates.

`Ben.Web.Tests/Services/EventLengthTests.cs` covers each.

## Not done

- Help screenshots of the ask form and the event page are unchanged (they now lack "(required)"
  on the labels); no hosts were started for this branch. One help bullet was added to
  `going-to-an-event.md`.
- CMS pages an organizer writes by hand could still contain `href="#x"` in stored content.
