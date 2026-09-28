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

## What shipped

- **T1 — data and API** (`90730110`): `TimeZoneId` on AppUsers, Organizations (default Chicago),
  Cases and Investigations; `Zones` (Ben.Data.Common) checks, resolves and inherits; `ZoneChain`
  fills a record's `EffectiveTimeZoneId`. Sign-up, the request wizard, `/api/me`, the profile, group
  settings, cases, investigations and the investigation lists carry zones; tours, events and calendar
  dates default to the group's zone.
- **T2 — the viewer's zone** (`0cba5aed`): `IBenUserState.ViewerTimeZone` (chosen → browser →
  Chicago) behind every viewer helper; sign-up, profile and wizard; 63 `.ToLocalTime()` calls and
  four raw-UTC displays fixed; `ServerClockGuardTests`.
- **T3 — the item's clock** (`c84334f8`): `BenTime`, `TimeZoneSwitch`, `TimeView`; case, visit,
  group, event and tour pages; the visit dialog and the calendar's event form take times on the
  item's own clock.
- **T4 — help, change logs, screenshots, PDF**, and versions on the change log (below).

## What testing found (and fixed)

- Visit emails to clients printed times in UTC ("at 12:00 AM UTC" for a 7 PM visit).
- The calendar loaded an event's sign-up deadline as raw UTC and saved it back through the viewer's
  zone, moving it by the viewer's offset on every edit.
- The group settings save dropped `StripMediaMetadataCanChoose`, greying the switch after each save.
- The public change log printed its `**bold**` markers literally, in 28 places.
- A mocking library does not run an interface's default members, so the viewer helpers now fall back
  to the browser's zone rather than throwing.
- Browser test: a New York case read from a Chicago browser, then on a Pacific profile; it fails
  when the switch is broken (checked).

## Versions on the change log (Ben, 2026-09-28)

> Can we track releases in versioning? Go back to when it went live and call that 1.0.0. Minor
> releases are like 1.0.1. Larger releases are 1.1.0. Huge and major releases are 2.0.0.

- The date line carries the release: `## 2026-09-28 · 2.11.0`. `ChangelogService` reads it and
  `/changes` shows "Version 2.11.0" beside the date.
- **Website** went live as 1.0.0 on 08/23; hosted events (09/12) is 2.0.0; this branch is 2.11.0.
- **Service** went live as 1.0.0 with the 08/22 release; hosted events is 2.0.0; this branch 2.8.0.
- **iPhone and iPad** use the App Store numbers (1.0.0 → 1.0.3); changes since 1.0.3 are under
  **1.1.0**, which is the number the next build should carry.
- `ChangelogServiceTests` holds it: every release since go-live is numbered, 1.0.0 first, each one
  exactly one patch, minor or major step after the last (apps: never backwards).

## Not in this branch

- The iPhone/iPad app (next): the API already returns every zone it needs; the app ignores the new
  fields until then.
- Entra and Apple sign-ups do not ask for a zone; the browser's stands in until the profile says.
- Guessing a zone from an address: named on purpose, as `HouseClock` explains.
