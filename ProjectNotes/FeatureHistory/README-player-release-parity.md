# Session player, phase 4: what the latest release records (10/28/2026)

Branch `feature/player-release-parity` (off `fix/player-media-links`). Phase 4 of the session-player plan.

## Why

The phone and the site had grown things the session page never showed: where a session was sent, the
zone it was recorded in, an EVP session's questions and the listening after them, the date details were
added, a photo's room, a recording the phone named and never sent (Ben's 10/23 session), and the actions
2.22.0 added — which lived only on My field sessions and the event's list.

## What changed

Server
- `SessionTimeline` (additive; version stays 1): `TimeZoneId` (from `session.timezone`, only a zone the
  site knows), `BatteryAtStart`, `Missing[]` (paths the document names that the session never brought),
  `TimelinePhoto.Room`, `TimelineMarker.ListenUntilMs` for `evp_question` (the rule `EvpQuestionWindows`
  already used for the scanner).
- `TimelineRequest.Arrived`: every file the session brought, so the public archive (approved files only)
  and a one-file share link never call an unapproved or unshared file "missing". Part of the cache key.
- `FieldSessionBelonging` on the single-session read: investigation, hosted event or tour date (with its
  public link when it has one), group, place, started by a launch; plus `SentByViewer` and
  `ViewerRunsTheEvent`. The website's record also gains `PlaceId` and `IsBundle`, which the API always
  sent and the record dropped.
- `PublicSessionSummary`: `EventTitle`, `EventUrl`, `GroupName` when it is public through an event's results.

Phone
- An upload sends the zone the session was recorded in, not `TimeZone.current` at upload time
  (`UploadSessionView.swift`; `FieldSessionSummary.timezoneIdentifier`). Rides the next build.

Site (`FieldKitEnginePlayer.razor`)
- Heading: the start in the session's zone with its letters, the site's Local time / My time switch, a
  line saying where it belongs (linked), facts (ran for, readings, device, battery), "never closed".
- "Magnetic field (no base set)" when no base was set, as the phone says.
- Marks: questions read "Asked: “…” · listened 15 s"; the chart shades the listening; details say when.
- Photos carry their room (thumb title, full-size caption).
- Recordings: a row per file named and never sent.
- `FieldSessionActionsPanel` (new): delete, "May be shown with the results", "Keep it off the place's page"
  for the sender; "Show with the results" / "Stop showing" for the event's team. Server sentences shown.
- `LatestChoiceSaver` (new): the 10/27 one-request-at-a-time rule for switches, now shared by My field
  sessions and the panel.
- The public page's banner names the event and links it when that is how the session is public.

## Tests
- `Ben.Web.Tests/Services/FieldSessionTimelineReleaseTests.cs` (new).
- `Ben.Web.Playwright/Tests/FieldSessionPageTests.cs` (new): missing sound file, question row and
  shading, the start where it was recorded and the switch, delete from the page, an event session's
  belongs line with the sender's and organizer's controls. `EnsureAConfirmedSeatAsync` moved to
  `Oct16TestBase` from the EVP scan tests to be shared.

No migration.
