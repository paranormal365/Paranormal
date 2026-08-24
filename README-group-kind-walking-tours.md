# Ghost walking tours as a kind of group (Ben, 2026-08-24)

Ben: *"Could we also include the possibility that a group where a user starts and joins is a
ghost walking tour. A ghost walking tour is going to be public information and public by default."*

## The axis this adds — and the one it doesn't

Backlog item #78 deferred **group types** — a many-to-many subject taxonomy (Ghost, UFO,
Cryptid…) describing *what a group investigates*. That stays deferred. This is a different
axis: what an organization **does**. An investigation group and a tour operator want opposite
defaults, and no amount of subject tagging expresses that.

`OrganizationKind`: `InvestigationGroup = 0` (every pre-existing row, correctly) and
`GhostWalkingTour = 1`.

## Decided with Ben

- **Kind sets defaults; tours are a capability.** The kind chosen at creation decides what the
  group STARTS as. `Organization.RunsPublicTours` is separate and defaults true for a tour —
  so an investigation group that also runs paid tours (plenty do) turns it on and is found
  under tours, without registering a second group or lying about what it is. The finder
  filters on the CAPABILITY; the badge shows the KIND. Both facts, both true.
- **Public by default means**: address shown + searchable, new events public, and prominent in
  Find/nearby with a badge and a filter.
- **RSVP only** — the existing public-events machinery, including the no-account email RSVP.
  No ticketing or payment; that would need the processor decision first.
- Deliberately NOT changed: client intake. A tour may take investigation requests like anyone
  else — nothing is withheld from either kind.

## Where the defaults live

`Ben.Data.Common.Enums.OrganizationKindDefaults` — in the COMMON assembly, because two callers
need it: the creation wizard (website) fills its form from it, and the registration service
(server) applies it to the entity. If they disagreed about what "a new tour" means, the
difference would surface as a tour whose address is quietly private.

## What this touches

- Migration `AddOrganizationKindAndTours` (additive: two columns + an index on the capability,
  which is what the finder's filter queries).
- Self-service registration (`RegisterOrganizationAsync`) and admin create/update carry the
  kind; update treats null as "leave as-is" so an older caller can't silently reclassify.
- Public surfaces: org page and browse results carry Kind + RunsPublicTours; `?toursOnly=true`
  filters the browse.
- UI: the wizard's first question (with copy that changes per kind), badges on the public page
  and finder cards, the finder's Everyone/Walking-tours toggle, org Settings controls for both
  fields, and new calendar events defaulting public for a tour.

## Verifying

- 8 unit tests: the defaults both ways, InvestigationGroup == 0 (so the existing table is not
  silently reclassified), the tours filter finding BOTH the tour company and the
  investigation-group-that-runs-tours, and the badge staying honest for the latter.
- Live: registered a real tour through the wizard's own endpoint — kind 1, tours on, public
  page badged, filter isolating it; flipped an investigation group's capability on and watched
  it join the tour filter with its badge unchanged; deleted both afterwards.
- Found and fixed while looking: filtering to tours with no matches said "No groups have
  registered yet" — false when 21 have. An empty FILTER is not an empty site.
