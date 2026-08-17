# Public Events — the platform's first front door for strangers

Branch: `feature/public-events` · Backlog item **#87**, phase 1. Branched from `develop` after the
CMS work.

Migrations: `AddPublicEventFields`, `AddPublicEventSlug` (both applied to dev SQL;
`scripts/create-database.sql` regenerated).

## Why

Ben's reason, and it shapes every decision here: *"These will benefit the organizations because it
is also an introduction to them by people attending... giving them the ability to create open events
might benefit us as well by increasing their numbers."*

Everything on this platform until now has been a records system for groups that already exist. A
public event listing is the first surface that brings in somebody who has **never heard of any of
them** — they find a ghost walk at a local landmark, sign up, and meet a group. That is how groups
grow, and how the platform grows with them.

## The flag that already existed and did nothing

`OrgCalendarEvent.IsPublic` has been stored, settable in the UI, and read by **nothing** since the
calendar was built. There was no public endpoint for events at all. An organization could tick
"public" and precisely nothing happened.

That is the fifth write-only feature this codebase has turned up. It also meant the substrate was
half built: `MeetingUrl`, an attendee table with RSVP, an optional case link and an address were all
already there.

## What a public event may be

**Never at a private residence, and never attached to a case.** A listing with a date and an address
is an invitation for strangers to turn up somewhere — a sharper version of the rule that already
refuses `InvestigationVisibility.Public` for a residence, and there is still no mechanism for asking
a client to agree.

Enforced on **two signals**, because either alone leaves a gap:

- A **case** is a client engagement, at their address by default, whether or not a place was ever
  recorded against it.
- A **place** states its kind outright (`PlaceKind.PrivateResidence`).

And **restated in the read path's own filter**, not merely trusted from the write path. A row that
became public some other way — a script, a migration, a bug — still never reaches a visitor. The
test proves it by flagging a residence event public directly in storage and watching it stay
invisible.

## The address

`HideExactLocation` shows the town but not the street until somebody says they are coming — the
established pattern for events at a venue that does not want visitors outside the event.

**Withheld at the projection.** A reader who is not attending receives a payload with **no field**
for the address, never the address plus a flag asking the client to be discreet. Attendees and the
organizing group's own members get it; cancelling stops it being served again. The listing says so
up front, so nobody feels tricked into identifying themselves to find out where they are going.

**The map coordinate stays approximate for everybody**, attendee or not, through the same
`PublicCoordinates` grid used for case discovery. One map, one pin — a coordinate that sharpened for
some readers would be a way of working out who is attending.

## Readable URLs, and a correction

Ben's objection killed the scheme I had recommended an hour earlier: *"how can we provide a concrete
link to equipment... '/e' it is going to get crazy eventually."* He is right — `/e` is events or
equipment, and the app already has more than four entity types.

**Full words.** Events ship as `/o/{org}/events/{slug}`, with the slug built from the date and title
the first time an event is published and then never regenerated — a slug that followed the title
would break every link already shared the moment somebody fixed a typo. `UrlSlug` is the shared
helper; cases and investigations can use it next.

The wider scheme is written up as backlog item **#89**, including the two structural points: **two
roots by ownership** (`/o/{org}/…` for what belongs to a group, top level for the cross-organization
equipment catalog), and **investigations are flat rather than nested under a case**, because
`CaseId` is nullable and a URL assuming the case has no form for a landmark visit.

## Shared links carry the site's name

Ben: *"assuming a link is created, I would like for it to show something that is relevant but also
promotes whatever we end up with as a site name... it may not be available then."*

Two pieces:

- **`SocialCard`** emits Open Graph and Twitter tags, so a link pasted into a chat renders as a card
  with the event, the group, **and the site's name**. Every organization sharing their own event is
  also advertising the platform.
- **`SiteIdentity`** puts the name, origin and tagline in configuration. It was hardcoded in seven
  user-facing places including three email bodies — and the ones that survive a rename are always
  the emails, because nobody rereads a template until a customer forwards it back. A test now fails
  if the literal reappears.

**One thing worth knowing about how that nearly shipped broken.** The event was loaded in
`OnAfterRenderAsync`, so during prerender — the only moment a crawler is looking — the card had
nothing to describe. Every shared link would have rendered as a bare URL, and it would have been
invisible to anybody testing by eye, because by the time a human looks the page has loaded. Loading
now happens in `OnInitializedAsync`, which is safe precisely because this page is public and needs no
auth to fetch.

## Endpoints

| Route | Who |
|---|---|
| `GET /api/public/events` | anonymous; all groups or one, upcoming first |
| `GET /api/public/events/{id}` | anonymous; address per entitlement |
| `GET /api/public/organizations/{org}/events/{slug}` | anonymous; the shareable route |
| `POST /api/public/events/{id}/rsvp` | any signed-in user; idempotent |
| `DELETE /api/public/events/{id}/rsvp` | the attendee |

## Verification

Full solution build, **0 warnings**. Full suite **4,443 passing, 0 failing** — 16 new.

Both load-bearing guards verified by deletion: removing the residence filter, and forcing the exact
address on, each fail their tests. There is also a test asserting **every listed event can actually
be opened**, which caught a real gap — `PublicEventListItem` had no slug, so every card in the list
linked nowhere.

**Still to do by hand** (Ben, signed in):
- Create a public event at a place, confirm it appears on `/events` and the group's own tab.
- Confirm one at a residence is refused, with a message that explains what to do instead.
- Tick "hide exact location", confirm the address is absent before RSVP and present after.
- Paste an event link into a chat and check the preview card. Requires `SiteIdentity:BaseUrl` set —
  it is empty by default, which omits `og:url` rather than guessing.

## Next on this item

- The organizer-side UI for the new fields (place, hide-location, capacity, cut-off) — the API takes
  them, the calendar editor does not offer them yet.
- The magic-link RSVP for people without an account (**87b**).
- "Near me" filtering (**87a**/**#88**), which is what turns this list into a reason to come back.
