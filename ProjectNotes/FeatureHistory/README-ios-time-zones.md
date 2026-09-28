# Time zones on the iPhone and iPad (09/28/2026)

Branch `feature/ios-time-zones`, from develop after the website's time zones (`cd6f3be6`,
`README-time-zones.md`). Ben:

> now do the timezones in the iphone and ipad apps
>
> saves in utc displays in current time zone
>
> Make sure this tracks with the .ben file here and on the server.
>
> ensure the time displayed in the app is local time for their current location.

Ships in **1.1.0** with the bug fixes since 1.0.3 and item 252's launch button (Ben: "The next push
of the iOS app is going to be 1.1.0").

## Where things stood

- The API already sent every zone the app needed (website T1); the app decoded none of them.
- The app formatted with `.formatted(date:time:)`: the device's zone, never the person's choice.
  Setting `NSTimeZone.default` does not help — modern Foundation formatting ignores it (checked).
- `EventClock` fell back to UTC for an event with no zone; the website falls back to Chicago.
- **A client's case never showed its visits.** `MyCaseInvestigation` expected `investigationId` and
  `scheduledStart`; the server sends `id` and `scheduledDateTime`, so nothing decoded.

## What shipped

- **`ReaderClock`** (BenKit): the phone's own zone, `.autoupdatingCurrent`, which iOS sets from the
  phone's location (Date & Time → Set Automatically). `Date.readerFormatted` and
  `ReaderClock.dateTime` replace 31 `.formatted(date:time:)` and 7 `.dateTime` formats in 19 files;
  they name the zone explicitly, which is what lets a test pin it.
- **The account's saved zone does not move the app's clock** (Ben's second instruction). The first
  cut adopted it from `api/me` and had a Settings → Time zone screen; both were taken out the same
  day. The saved zone is the website's; sign-up still sends the phone's zone as its starting value.
- **`PlaceTime` and `TimeZoneSwitch`**: an event, tour, investigation, booking or visit reads where
  the phone is (**My time**, the default) or on its own clock (**Local time**); the choice
  (`TimeView`, `time.inMyTime`) is remembered. Equal zones say so rather than offering a switch that
  changes nothing. Both redraw on `NSSystemTimeZoneDidChange`.
- **`EventClock`** falls back to America/Chicago, with the zone abbreviation on every time.
- Seat reminders keep the walk's own clock: their text is written when the seat is booked, and at
  the walk the phone is where the walk is.
- **A client's visits** decode (model rebuilt from a fixture captured from the real API) and show on
  the case with the switch and the cancel-by deadline.

## The .ben file

Unchanged, and now tested on both sides: the file carries UTC (`Z`) instants and the recording
phone's zone in `session.timezone`. BenKit's test pins the `Z`; the server's
(`DeviceDataSummaryTests`) shows a `Z` or an offset timestamp is stored as the same UTC instant.
Both the phone and the website display those instants on the reader's clock.

## Tests

- BenKit 540/540 (new `ReaderClockTests`, serialized because they pin the shared clock; the visits
  fixture test). Mutation-checked: a clock snapshotted at launch (`.current`) and a switch that starts
  on the place's clock each fail a test. Server help/changelog/device-data tests green.
- UI: `HelpMediaCaptureTests.testCaptureCaseVisits` (help screenshot) plus the local suites.
- Simulator by hand (first cut): a Chicago visit read 3:13 PM CDT and, on a Pacific clock, 1:13 PM PDT.

## Not in this branch

- The Xcode project's `MARKETING_VERSION` — set it to 1.1.0 for the next build.
- Item 252 (a lead starts everybody's Field Kit), next, in the same 1.1.0.
