# Area 9 — Places, Investigation Maps, and Location-Based Sharing

Branch: `feature/places-and-investigation-maps`

**The design lives in [`ProjectNotes/specs/Places-and-Investigation-Sharing.md`](ProjectNotes/specs/Places-and-Investigation-Sharing.md).**
This file is the working state of the branch; the spec is the argument for why it looks this way.

## In one paragraph

Three requests that turn out to be one feature: a map of where an organization's investigations
happened, the same map scoped to one person's own participation, and investigations that have no
client case — a group visiting a famous location, whose findings can be shared with whoever else has
investigated there.

## The decisions already made

- **Place, not pseudo-case.** A geocoded address that both cases and investigations point at. Cases
  keep their client, request, ownership and privacy model; a visit to a landmark carries none of
  that, and folding them together would put an implicit "and not one of those" into every existing
  case query.
- **Sharing defaults follow the place.** Private residences stay group-only; public locations
  default to sharing with others who investigated there. A global public default was considered and
  rejected — the risk is not symmetric, and most investigations happen at people's homes.
- **Attendance is self-reported on arrival**, with a case-manager / lead / org-management override
  for no signal or forgetfulness. Provenance is recorded, because "checked in from site at 21:04"
  and "ticked a box the following Tuesday" are different grades of evidence.
- **Rank and lead are different mechanisms.** Senior Investigator is standing, and already exists as
  `OrganizationRole`. Lead of one visit is delegated per investigation and expires with it — that is
  `IsLead` on `InvestigationAttendee`, the only piece missing.
- **No verifying arrival by device location.** A deliberate non-goal.
- **Poll, do not reach for SignalR.** No real-time infrastructure exists; a 10–15s refresh is ample.

## Build order

P1 Place + geocoding + backfill · P2 case-less investigations · P3 org endpoint with server-computed
permissions · P4 map-and-grid component + org tab · P5 personal scope · **P5b** arrival check-in ·
P6 visibility scopes · P7 place pages · P8 deduplication · **P9 end-user documentation (required)**

P1–P3 are the foundation and are worth doing together.

## Already on the branch's behalf

`AddInvestigationCoordinates` — `Latitude`, `Longitude`, `GeocodeNote`, `DateGeocoded` on
`Investigation` — is **already applied to the dev database**. Nothing reads those columns yet.

## Settled since this was written

- **Reciprocity: no.** `PlaceInvestigators` is open to anyone who investigated the place — there is
  no contribute-to-see requirement. Revisit only if lurking turns out to be a real problem. Even so,
  the read filter goes in **one predicate function**, so a future change is one place.
- **`IsLead` lands in P3**, with the permission work, rather than waiting for P5b.

## Settled by P8

**Deduplication.** Designed in
[`ProjectNotes/specs/Place-Deduplication-Design.md`](ProjectNotes/specs/Place-Deduplication-Design.md)
and built as far as prevention: a place is offered as a probable match when **the address matches
and it is within a tenth of a mile**. Both, not either — one address can be a block of flats, and
proximity alone matches next door. The admin merge screen is deliberately not built; preventing new
duplicates is the cheap win, and merging three backfilled rows is a lot of machinery for a problem
nobody is looking at yet.

## Still open

1. **Curation** of user-created public places — open, or approved? `Place.IsApproved` exists and is
   inert; nothing reads it.
2. **Client consent** to publish an investigation at a client's property. Until this exists,
   `Public` is refused outright at a private residence.

## Breaking change in P3, worth saying plainly

Editing an investigation used to be open to any member of the group. It now requires being the
person who scheduled it, the case manager, the visit's lead, an owner or administrator, or a holder
of the **Investigation** permission. Reading stays open to members, and answering for yourself —
your own RSVP, your own arrival — stays yours.

The existing tests that asserted any-member-edit were inverted deliberately; that inversion is the
discriminating test for the change. Documented for administrators under **Who can change an
investigation** in the organization-administration help.

## Branch state

Rebased onto `develop` after `feature/self-service-contact-info` landed (self-service contact info,
email validation, calendar event types). That ordering was deliberate: both branches append to
`IBenAdminClient.cs` and `BenAdminClientAdapter.cs`, so landing one first avoids a conflict in two
long files.
