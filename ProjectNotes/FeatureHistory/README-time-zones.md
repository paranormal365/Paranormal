# Time zones: yours, and the place's (09/28/2026)

Branch `feature/time-zones`, from develop. Ben:

> When someone signs up for the site, I would like for them to select their timezone. I want the
> site to record UTC, but if someone has configured their timezone, show date and time for their
> timezone. Use the Chicago/America as the default. This is stored on a per user and per
> investigation or case level so end users can see the local time for an investigation or their
> time for the case or investigation. Same with events or tours. You can see the event and tour
> time in the event or tour timezone or the user's timezone.

## Where things stood

- **Storage is already UTC.** Every instant is a `DateTime` holding UTC (`DateTimeViewerExtensions`
  says so; readers `SpecifyKind(Utc)`). Hosted events keep their nights as local wall-clock dates
  and times in their own zone, which is right for something advertised as "7 PM".
- **Zones exist only on tours, hosted events and calendar events** (`TimeZoneId`, IANA, default
  `HouseClock.ZoneId` = America/Chicago). Users, groups, cases and investigations have none.
- **The viewer's zone is the browser's, every session, never saved** (`timezone.js` →
  `WebApiTokenStore.BrowserTimeZone`, UTC when unknown). 179 call sites convert through it.
- **64 `.ToLocalTime()` calls in 37 files** still convert to the SERVER's zone (the 08/11 sweep
  regressed), and a few pages show raw UTC. The same tour date reads in the place's zone on the
  public page and the viewer's zone on the manage page.

## Decisions (Ben, 09/28/2026)

1. **Viewer's zone** = the zone they saved; else their browser's; else America/Chicago. Sign-up
   asks, pre-selected to the browser's zone. Signed-out visitors get their browser's.
2. **A switch, not both at once**: "Local time · My time" beside the times on a case,
   investigation, event or tour; remembered for that person across the site. Every time shown
   carries its zone's abbreviation (7:00 PM CDT).
3. **Groups get a home zone** (America/Chicago by default). New cases, events and tours start from
   it; an investigation starts from its case; each can be changed.
4. **Website and API now; the iPhone/iPad app next** — the API returns every zone the app needs.

## Plan

**T1 — Data and API.**
- `AppUser.TimeZoneId` (null = not chosen), `Organization.TimeZoneId` (Chicago),
  `Case.TimeZoneId` and `Investigation.TimeZoneId` (null = inherit). One migration.
- `Zones` in Ben.Data.Common: the one place that validates an id, resolves one with a fallback, and
  works out a case's or investigation's effective zone.
- Sign-up, `/api/me`, `/api/me/profile`, group settings, case and investigation create/update and
  records carry the zone (records also carry the effective one). Tours, hosted events and calendar
  events default to the group's zone instead of a hard-coded Chicago.

**T2 — The viewer's zone on the website.**
- `IBenUserState.ViewerTimeZone` (saved → browser → Chicago) replaces `BrowserTimeZone` in every
  helper, loaded from `/api/me` at sign-in and on every restored session, and after impersonation.
- Sign-up, the Entra profile completion and My profile get a zone picker (US zones first, every
  zone searchable).
- Every `.ToLocalTime()` and raw-UTC display goes through the viewer helpers; a source guard keeps
  `.ToLocalTime()` out for good.

**T3 — The place's time and the switch.**
- A time component that renders an instant in the item's zone or the viewer's, with the zone
  abbreviation, and the "Local time · My time" switch (remembered per viewer).
- Case, investigation, event and tour pages (public and manage) use it; the zone is editable on the
  group's settings, a case and an investigation. Scheduling a visit takes the time in the
  investigation's own zone.

**T4 — Help, change logs, screenshots, product PDF; tests throughout.**

## Not in this branch

- The iPhone/iPad app (next).
- Guessing a zone from an address: named on purpose, as `HouseClock` explains.
