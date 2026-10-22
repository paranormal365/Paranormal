# feature/pricing-findable: prices can be found

Branched from `develop` at 4eb819dc, 10/22/2026.

## What Ben said

1. "There is no direct menu link to prices on the home page. The way I am finding it is going to
   Community then Pricing. There should be a more direct path."
2. "I don't see, as an administrator, how to configure event settings like pricing."

## What changed

### Pricing for everybody

| Where | Was | Now |
|---|---|---|
| Top bar, signed out | no Pricing at all | **Pricing** after Join a Group |
| Top bar, signed in | only inside Community (Community → Pricing) | **Pricing** on the bar after Community, and **not** in Community any more |
| Mobile drawer | same as the bar (one model, `BenNav.Sections`) | same as the bar |
| Home page | the "Investigate properly" pillar linked to /pricing as "See what a group gets" | plus a **See pricing** button under "Three ways in. All of them start free." |
| Footer | no link | **Pricing** after Help |

**Why Community → Pricing went rather than stayed:** BenNav lights every entry whose address is the
current page (`MostSpecificMatch` + `IsActive`), so the same /pricing in two places lit both as "you
are here", the bug the store's Browse/Favorites entries had on 09/24. And Pricing isn't a community
thing; a person who learned to look in Community would keep looking there.

**When it hides:** only when `billing.purchases-enabled` and `events.credits-enabled` are both off.
One owner, `SiteFeaturesProvider.PricingIsShown`, used by the bar, the drawer, the home page and
the footer. The website couldn't see the event-credit switch before: `SiteFeaturesInfo` gained
`EventCreditsEnabled` (defaults true, like the API's reading of an unset switch), filled by
`PublicSiteFeaturesController`.

### Administration → Pricing (/admin/pricing)

- A top-level Administration entry beside the Dashboard (not inside a group, so it's one click).
- One page, `SuperAdmin/AdminPricing.razor`, SuperAdmin only and refused while impersonating, the
  same guard as Site Settings (AuthReady first):
  - **Plans:** the live bands with members, monthly and yearly price, and groups on each; the
    **Sell plans and seats** switch; **Edit plans and prices** → /admin/subscription-tiers.
  - **Events:** Sell event credits, the credit price, the paid-subscriber percentage, the bundles.
  - **Ghost Walk Tour subscription:** the switch and the monthly price per route.
  - A sentence under each (`PricingPreview`), e.g. "A group with a paid plan pays $19.80 per
    event (20% of the full price)." Read from the public price list (`/api/public/pricing/extras`)
    and the tier rows, the same numbers the Pricing page and checkout use, and re-read after every
    save. Nothing is computed from the boxes on the page.
  - **See what visitors see** → /pricing.
- One source of truth: the settings are the same `SiteSetting` rows, saved through the same
  `PUT /api/admin/site-settings/{key}`, so `AdminSiteSettingController.WhyNotValid` checks them
  and its sentence is shown. Site Settings is unchanged apart from a line on its Selling plans,
  Events and Ghost Walk Tour subscription tabs: "Also on Administration → Pricing".
- The Administration dashboard has stat cards that count accounts and cases, not a grid of tools,
  so no Pricing tile was added; the rail entry sits directly under Dashboard instead.

### Phone widths

Below 992px every new control is 44px each way: the page's buttons, boxes and switch rows, the
home page's See pricing button and the footer links. The footer now wraps below 992px (it did below
576px): with Pricing added the row was wider than a tablet.

## Tests

- `Ben.Web.Tests/Website/PricingFindableTests.cs`: renders the bar, the drawer, the home page and
  the footer, signed out and signed in, with each switch combination; renders /admin/pricing
  through `InteractiveTestRenderer` (a renderer that says it is interactive, so the AuthReady guard
  runs) to show a member, a visitor and an impersonating SuperAdmin are refused before anything is
  fetched, that a SuperAdmin sees the three sections and their sentences, that a refused save
  shows the server's sentence, that a save refreshes the sentence, and that a failed load says why.
  Each was run against the change taken back out (nav, home, footer, provider, guard, refusal,
  refresh, load failure, the Site Settings line) and failed.
- `SiteSettingConsumerGuardTests` counts AdminPricing.razor as an editor, not a reader.
- `Ben.Web.Playwright/Tests/PricingFindableTests.cs` (category PricingFindable): all ten widths,
  both themes, `LookAtAsync`/`TouchTargetAsync`. Written, not run.
- `ProductWalk` walks /admin/pricing.

## Owed

- A help screenshot of the new page (`BEN_CAPTURE=1 … --filter TestCategory=Capture`) on a fresh
  BEN_E2E_DB.
- No migration, no new setting.
