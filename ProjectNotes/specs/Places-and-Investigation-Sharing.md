# Places, Investigation Maps, and Location-Based Sharing

**Status:** spec, not built. Next roadmap piece.
**Date:** 2026-08-15

---

## What this is for

Three requests that turn out to be one feature:

1. An organization's page should show a **map of where its investigations happened**, plus grids of
   past and future ones. Members can view the past ones; editing needs permission, except that
   someone who was actually there may finish their own findings.
2. A user's own page should show **the same map and grids, for investigations they took part in**.
   They edit what they own; for everything else they follow the owning group's rules.
3. Investigations should be possible **without a client case** — a group visiting a famous location.
   Over time many groups accumulate visits to the same address, and their findings could be shared
   publicly, or only with others who have investigated that address.

They share one shape: *a map and a list of investigations, filtered by scope, with per-row
permissions*. Build that once.

---

## The central decision: Place, not pseudo-case

**Introduce a `Place`** — a named, geocoded address that both cases and investigations point at.
Do **not** model case-less investigations as cases with the client fields left empty.

A `Case` carries a client, an originating request, a case number, organization ownership, and a
privacy model built entirely around protecting somebody's home. A visit to a public landmark has
none of that. Folding them together means every existing case query grows an implicit "…and not one
of those other ones", and the queries that miss it become bugs — some of them privacy bugs, in the
part of the system least able to afford them.

With a Place the model reads plainly:

- **Case** = a client's problem *at a place*
- **Public investigation** = a visit *to a place*

The aggregation the user wants then falls out for free: *14 investigations here, by 6 groups, since
2024*. And an investigation always has coordinates, because a Place always does.

```
Place  ──<  Case          ──<  Investigation
   └────────────────────────<  Investigation   (no case)
```

### Place

| Field | Notes |
|---|---|
| `Id` | |
| `Name` | "Waverly Hills Sanatorium", or null for a private residence |
| `StreetAddress1/2`, `City`, `State`, `ZipCode`, `Country` | |
| `Latitude`, `Longitude` | resolved on save |
| `GeocodeNote` | why it has no coordinates, or null — see below |
| `Kind` | `PrivateResidence` \| `PublicLocation` — drives the sharing default |
| `IsApproved` | for user-created public places, if curation proves necessary |

**Deduplication is the hard part.** Two groups typing the same landmark must land on the same Place
or the aggregation is worthless. Suggested rule: match on rounded coordinates (≈4 decimal places,
about 11 m) plus a normalised name, and offer "did you mean this place?" rather than silently
merging. Deliberately not solved in this spec — it needs its own pass.

---

## Geocoding, and saying so when it fails

Every investigation resolves to coordinates: from its Place, or from an address the investigator
enters directly, which is then geocoded like a case is.

`AddressGeocodingService` already exists, is configured in `Ben.Data.WebApi/Program.cs`, and offers
both `TryResolveCoordinatesAsync` (structured) and `TryResolveFromQueryAsync` (free text).

**When lookup fails, record why.** A missing dot is otherwise indistinguishable from an
investigation nobody has written up, and somebody needs to be able to see that an address simply
could not be found, and fix it. That is what `GeocodeNote` is for — it is never silently null-null.

> **Already applied:** migration `AddInvestigationCoordinates` added `Latitude`, `Longitude`,
> `GeocodeNote` and `DateGeocoded` to `Investigation` on the dev database. It belongs to this work;
> nothing populates or reads those columns yet.

---

## One view, three scopes

The map-and-grid component is written once and fed a scope:

| Scope | Shows | Lives on |
|---|---|---|
| **Organization** | investigations belonging to this group | a new Investigations tab on `OrganizationView` |
| **Personal** | investigations this user *attended* | the user management page, beside the profile |
| **Place** | every investigation at this address, across groups | a place page |

There is no org-wide investigations endpoint today — the existing controller is case-scoped
(`api/organizations/{orgId}/cases/{caseId}/investigations`) and `MyInvestigations` is user-scoped.
All three scopes need new read endpoints.

### An invitation's life: notification → calendar → map

An invitation surfaces in three places, at different times, and the map is deliberately last:

| Stage | Where it shows | When |
|---|---|---|
| Invited | internal messages, and the unread count on sign-in | immediately |
| Invited | the user's calendar | immediately |
| Attended | the personal map and its grids | only once the investigation is in the past |

The map is a record of *where you have been*, not where you intend to go. A dot that appears the
moment you are invited would assert a visit that has not happened, and this is a record people may
eventually cite — quiet inaccuracy is the kind that survives.

**Both feeder halves already exist.** `NotificationSummaryResponse.InvestigationInvites` drives the
bell (Area 5 C4), and `InvestigationController` already creates an `OrgCalendarEvent` and stores its
id on `Investigation.OrgCalendarEventId`. This spec adds only the third stage.

#### Arrival check-in — how `DidAttend` actually gets set

People check themselves in when they arrive. Their chip on the investigation's roster changes
colour as each person turns up, so anyone looking at the page can see who is on site. Checking in
sets `DidAttend`.

This is worth having for its own sake, not only for the map: on a night visit to an unfamiliar
building, *who has arrived* is operational information, and at times a safety one.

**A manual override stays.** The case manager, the investigation's lead, or organization management
can set `DidAttend` afterwards — for the location with no signal, or the person who simply forgot.

##### Record which way it was set

Two paths to the same flag means the flag alone no longer says much. Store the provenance:

| Field | Meaning |
|---|---|
| `DateArrived` | when they say they arrived — not necessarily when the row was written |
| `AttendanceRecordedByAppUserId` | null when self-checked-in; the organizer when overridden |

"I checked in from the site at 21:04" and "the manager ticked a box the following Tuesday" are
different grades of evidence for the same claim, and this is a record people may eventually cite.

##### No signal is the normal case, not the edge case

Rural properties, basements, steel-framed buildings — the moment check-in matters most is exactly
when it is least likely to work. Two consequences:

- **Let check-in be late.** Capture `DateArrived` as a *stated* time rather than implicitly "now",
  so someone can record a 21:00 arrival at 01:00 when signal returns.
- **The roster stays first-class.** The live board is a convenience layered on it, never the only
  route to a correct record.

##### Deliberate non-goal: verifying arrival by location

The temptation, now that investigations have coordinates, is to check the device is actually there.
This spec says no. It is surveillance of volunteers, indoor GPS is unreliable in precisely these
buildings, and the failure mode — an honest investigator unable to check in from a basement — is
worse than the dishonesty it would prevent.

##### Live updates: poll, do not reach for SignalR

There is **no real-time infrastructure in this application at all**. Area 1 looked at it and
deliberately deferred SignalR to a phase that was never built, because the WebApi is a separate
process from the Blazor app and a hub would need `HubConnection` plus bearer plumbing.

A roster refreshing every 10–15 seconds while the page is open is entirely adequate for watching a
handful of people arrive, and costs nothing new. Revisit only if this page proves it needs better.

##### This settles open question 2

Attendance is self-reported by the person who was there, with an organizer override for reality.
The earlier "presume present when unrecorded" fudge is no longer needed: the common path is now
self-service, and the fallback is explicit rather than a guess. **The map filter becomes *past* and
`DidAttend == true`**, which is the honest rule.

##### One gap: "lead investigator" does not exist

`Case.CaseManagerAppUserId` is real and checkable. Organization Owner/Administrator is real and
checkable. **There is no lead-investigator concept** — `InvestigationAttendee.AssignedRole` is free
text, so "Lead" is a string somebody typed and nothing can be authorised against it.

Smallest honest fix: add `IsLead` to `InvestigationAttendee`. It earns its place beyond attendance —
it is also the answer to "who is running this visit?", which the roster cannot currently express.

#### Attended, not merely invited — and the trap in it

`InvestigationAttendee` carries both `Rsvp` and `DidAttend`. The map filter needs *past* **and**
*attended*.

The trap: `DidAttend` is set by the organizer afterwards, and if nobody does that housekeeping,
a strict `DidAttend == true` leaves every personal map permanently empty — a feature silently
dependent on admin hygiene that may never happen.

**Superseded by arrival check-in, above.** With people checking themselves in, the flag is set on
the common path by the person best placed to know, and the organizer override covers the rest. The
filter is *past* and `DidAttend == true`, with no presumption needed.

Either way this also grounds the "finish your findings" rule: you may complete your write-up
because you were present, which is the same fact that put the dot on your map.

---

## Permissions: decided on the server, carried on the row

The same investigation now appears in three lists. If each view works out "can I edit this?" for
itself, they will drift, and the failure mode is someone editing another group's record.

**Each row arrives carrying its own verdict**, computed once where memberships and org rules live:

```
CanEditRecord         — title, schedule, attendees
CanCompleteMyFindings — my own binder entries on this investigation
```

The UI decides whether to render a button. It never decides who you are.

### The rules those flags encode

| Situation | Record | Own findings |
|---|---|---|
| I own it / created it | ✅ | ✅ |
| I attended, no edit permission | ❌ | ✅ — including after it is past |
| Org member, granted permission | ✅ | ✅ |
| Org member, no permission | ❌ | ❌ |
| Another group's, visible to me | ❌ | ❌ |

"My own findings" means the `CaseTimelineEntry` rows I authored against that investigation — the
binder from Area 5 C3 — not the investigation record. Attendees finish their own contributions;
they do not edit the visit.

**This is a tightening.** Today `InvestigationController` checks only `IsOrgMemberAsync`, so any
member can edit any investigation. Expect existing behaviour to change, and say so in the release
note.

---

## Sharing

### Scopes

| Scope | Who sees it |
|---|---|
| `GroupOnly` | the owning organization |
| `PlaceInvestigators` | the group, plus anyone else who has investigated this place |
| `Public` | everyone, including signed-out visitors |

`PlaceInvestigators` has **dynamic membership**: someone who investigates the place next year gains
access to this year's findings retroactively. That is the point — it is what makes a shared record
of a location worth having — but it must be said in the UI at the moment of choosing, not in a help
page. "I shared this with three people" can quietly become three hundred.

**Open question worth deciding:** is it reciprocal — must you publish your own findings for a place
to see everyone else's? A contribute-to-see rule would feel fairer and discourage lurking, at the
cost of real complexity. Not assumed either way here.

### The default follows the place

Rejected: a global default of public.

The risk is not symmetric. A wrong "private" costs a click. A wrong "public" puts a family's home
address on the internet and cannot be recalled. This application is otherwise built around exactly
that protection — the pseudonym on public case pages, the two-key rule before a member's photo
reaches a client — and a blanket public default would quietly undercut all of it, because most
investigations happen at private residences.

| Investigation at | Default |
|---|---|
| `PrivateResidence` (a case) | `GroupOnly`, and publishable only with client consent |
| `PublicLocation` | `PlaceInvestigators`, with `Public` one click away |

That gives the sociable outcome where it is safe and the careful one where it matters, without
anyone having to remember to change a setting on the one that counts.

---

## Build order

| Phase | Work | Size |
|---|---|---|
| **P1** | `Place` entity + geocoding + backfill from existing case addresses | M |
| **P2** | `Investigation.CaseId` becomes nullable; `PlaceId` added; case-less investigations can be created | M |
| **P3** | Org-scoped investigations endpoint with the server-computed permission flags; tighten `InvestigationController` | M |
| **P4** | The map-and-grid component; Investigations tab on the org page | M |
| **P5** | Personal scope on the user management page (`DidAttend`) | S |
| **P6** | Visibility scopes + defaults + the sharing control | M |
| **P7** | Place pages and cross-group aggregation | M |
| **P8** | Place deduplication / "did you mean" | M — needs its own design pass |
| **P5b** | Arrival check-in: `DateArrived`, `AttendanceRecordedByAppUserId`, `IsLead`, the live roster | M |
| **P9** | **End-user documentation** — required before this is called done | S |

### P9 is not optional

Ben asked for this explicitly, and Area 6's own entry already carries it as a standing rule: a
user-visible feature is not finished until the in-app help explains it. This application has a
repeated history of things that were built and then unreachable; an undocumented feature is the same
failure one layer up.

Docs live in `Ben.Web.Library/Help/Content/*.md` and are audience-gated. Area 9 touches:

| Doc | What changes |
|---|---|
| `working-a-case.md` | investigations map, past/future grids, who may edit what |
| `your-profile.md` | the personal map, and why a dot appears only after you have been |
| `organization-administration.md` | the Investigations tab, granting edit permission |
| *(possibly new)* | sharing findings by location — the visibility scopes and what `PlaceInvestigators` really means |

Add a `<HelpLink>` at each screen the docs explain. `HelpLinkTargetTests` fails the build if a link
points at a document or heading that does not exist, so renames cannot quietly break them.

**Also outstanding from earlier work, and worth folding in:** the contact/support page (#79) and the
calendar changes (address, meeting link, invites, recurrence) both shipped without doc updates.

P1–P3 are the foundation and are worth doing as one branch. P4–P5 are where it starts to look like
the thing the user described.

### Notable existing pieces to reuse

- `AddressGeocodingService` — configured, static, both lookup shapes
- `PublicCaseDiscovery.razor.js` — a working multi-marker map with `init` / `setMapCenter` /
  `resizeMap` / `dispose`; the closest thing to the component P4 needs
- `Case.Latitude` / `Longitude` — proven source for P1's backfill
- `CaseTimelineEntry.InvestigationId` + `CaseTimelineVisibility` — the binder, already the home of
  "my findings"

---

## Open questions

1. **Reciprocity** on `PlaceInvestigators` — contribute-to-see, or not?
2. ~~Who confirms attendance~~ — **settled**: self check-in on arrival, with a case-manager /
   lead / org-management override. See "Arrival check-in" above. Leaves a smaller question: should
   `IsLead` be added to `InvestigationAttendee` now, or deferred?
3. **Curation** of user-created public places — open, or SuperAdmin-approved? (`IsApproved` is
   scaffolded for the latter; leave it unused if the answer is open.)
4. **Client consent** to publish an investigation at their property — reuse the two-key pattern from
   Area 4, or a per-case switch?
5. **Deduplication** (P8) — needs its own pass before P7 is worth much.
