# Geo-fed ads, interleaved, counted — item 186 F8

The last structural piece of the feed's economics: the promoted-group cards (item 166 W3's
review-gated machinery, reused whole) now reach the feed itself, ordered by distance to the
viewer, and counted.

## Geo feeding

`GET api/public/promoted-groups` gains optional `lat`/`lon`. Distance = the group's nearest
PUBLIC, searchable `OrganizationAddress` — the same rows the nearby search shows anyone.
**An AreaOfOperation contributes NOTHING**: its centre exists to hide where a home-based group
is, and "12.3 miles from you" derived from it would publish exactly what it conceals (pinned by
the suite's privacy probe, alongside the non-searchable-address one). Located groups order
nearest-first; unlocated ones still rotate after them — unlocatable, not unseen.

The viewer's coordinates come from an explicit **Show groups near you** click on a card in the
feed (the browser's own permission prompt, via the same colocated module NearbyDiscovery uses),
live only in the circuit, ride as query values, and are stored nowhere.

## The weave

Main feed only (tag and type pages stay clean): one promoted slot per eight posts — positions
8, 16, 24, so nobody meets an ad before they've met the feed — alternating a real Approved card
with the site's own house card (find-a-group CTA, and it says whose it is). Failure to fetch
cards renders no slot at all: promotion is garnish, same doctrine as the placements card.

## Counting

`Impressions`/`Clicks` on `OrganizationAd` (migration `AddOrganizationAdCounters`). Impressions
bump in ONE batched update per serve — a count of serves, and the promote page says so
("times shown", not "people reached"). Clicks go through the website's new **`/go/{adId}`**
minimal endpoint: it asks the API to count (`POST …/{adId}/click`, which answers the closed-set
target) and issues the redirect itself — the API cannot 302 to a website whose origin it doesn't
know. Every failure path lands on `/find`, never a dead end (item 149's rule). The existing
PromotedGroupsCard on Home/find switches to `/go` links, so counters move on every placement.
PromoteGroupPage shows both numbers to the group.

## Money

Stays free while the feed is dark. When ads become billable it hooks into items 143/144's
ledger — recorded as a follow-on, nothing built now.

## Verifying

- 6 new tests (`PromotedGroupsGeoTests`): nearest-first with unlocated after; the two privacy
  probes (non-public address and no-coords cases yield null distance); impression batching per
  serve; click counting + closed-set answer; unapproved click counts nothing and 404s.
- The weave's rendering and the /go redirect land in F10's Playwright batch (the weave is
  Razor-side markup; bUnit isn't in this repo's toolbox and the e2e walk is the honest test).
- Live: flag on → feed page fetch confirms cards with distances when coords are supplied;
  /go/{id} on the running website counts and 302s; flag restored.
