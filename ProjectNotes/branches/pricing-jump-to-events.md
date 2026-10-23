# feature/pricing-jump-to-events: a button at the top of Pricing down to events and ghost walks

Ben, 10/23/2026, with a screenshot of the Pricing page's hero: "Could we put a button that shows up
here if either or both the ghost walk tours or event credits are for sale (enabled) and it just
scrolls to them instead of making someone have to figure out they are below all this?"

Started from `develop` at eacc537a.

## What it does

Beside **How plans work** in the Pricing page's hero, one more button, shown only while something
outside the plans is for sale:

| For sale | Button | Goes to |
|---|---|---|
| Hosted events and the Ghost Walk Tour subscription | Events and ghost walks | `#events` (the ghost walk section follows it) |
| Hosted events only | Hosted events | `#events` |
| Ghost Walk Tour subscription only | Ghost walk tours | `#ghost-walk` |
| Neither | (no button) | |

"For sale" is read the way the sections read it. Events need the Events feature on, plans listed,
and `Events.OnSale` (the credits switch on and a price above zero). The section itself still shows
when credits are off, saying so. The ghost walk offer is only sent by the API while it is on sale.

It is a plain link (`/pricing#events`), so it works in the second or two before the page is live,
and the browser does the scrolling.

**Also fixed:** both hero links were 32px tall on a phone, under the 44px the Pricing page keeps for
every control below 992px (`PricingFindableTests`). They are 44px now (`.pricing-hero-links`).

## Tests

In `EventCreditsAndToursTests` (category EventCreditsAndTours), which turns the offers on and puts
them back:

- `With_events_and_ghost_walks_on_sale_the_top_of_Pricing_takes_you_to_them`: the label, the events
  section out of view before and in view after, still on /pricing, and both links 44px on a phone.
- `With_only_ghost_walks_on_sale_the_link_says_so_and_goes_to_them`
- `With_neither_on_sale_there_is_no_link`

Without the button, the first two failed (the third passes either way, by design). With it, the
EventCreditsAndTours, PricingFindable, PricingWhatsIncluded, PricingBuyPath and PagesDoNotBlink
tests: 56 passed, 11 skipped (the Stripe and capture ones, as always), 0 failed.

## Docs

One sentence in the help ("Administering a Group", Pricing). It names neither offer, because a help
document may not mention the Ghost Walk Tour subscription while it is off
(`EventCreditsAndGhostWalkTests`). Changelog line under Unreleased. Product PDF rebuilt. No help
picture shows the hero, so none changed.
