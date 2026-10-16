# fix/visual-polish: three things seen on a hosted event's page

Branched from `integrate/oct16-fixes` at b70758f9 ("Record the App Review demo data made for
1.1.4's review"), 10/16/2026. Found while reviewing the site locally on
/o/paranormal365/events/thomas-house-seance-weekend, on a phone and a desktop.

## 1. "Coming along?" twice for a signed-out visitor

`HostedEventPlaces` printed "Coming along?" at the top of its signed-out branch, then rendered the
page's `AttendByEmail` fragment (passed in as `SignedOut`), which printed it again.

Now `SignedOut` is a `RenderFragment<bool>`: the bool says whether the form draws its own heading.

| Signed-out path | Heading | Form |
|---|---|---|
| Plan can be picked | "Coming along?" once, above the plan | under "Just for the day?", no heading; once sent, "**Check your email.**" inline |
| Plan can be picked, no day passes | "Coming along?" once | none |
| Asked for in words (no plan to pick) | the form's own "Coming along?" / "Check your email" | `AttendByEmail(true)` |
| Closed (`ClosedSentence`) | "Coming along?" once, then the sentence | none |
| Not taking bookings | none (the "Closed" badge) | none |
| Ordinary (non-hosted) event | the form's own, unchanged | `AttendByEmail(true)` |

The signed-in branches (`EventBookingCard`, `HostedEventPicker`, `AskForPlaceForm`) were checked
and print one heading each. `HostedEventPlacesHeadingTests` renders each signed-out path through
`HtmlRenderer` and counts the heading; with the old top-of-branch heading put back, four of its
five tests fail.

## 2. The phone's sticky "Ask for a place" button covered the form

`.hosted-sticky-ask` is fixed at the bottom of a phone's screen and jumps to `#event-act`; once
there it sat over the form's last line and its send button.

- `HostedEventShowcase.razor.js` (new, collocated module) watches `#event-act` with an
  IntersectionObserver (`rootMargin: 0 0 -25% 0`, so the form counts as on screen once it is above
  the bottom quarter) and toggles `hosted-sticky-ask--away` on the button. If the form is not in
  the DOM yet it waits for it with a MutationObserver. Every callback checks `isConnected`.
- The class hides the button with `opacity` + `visibility` (so it also leaves the tab order), with a
  short transition that `prefers-reduced-motion` turns off.
- `HostedEventShowcase.razor` imports the module from `/_content/Ben.Web.Website.Library/...`,
  starts the watch after render, and swallows `JSException`, `JSDisconnectedException`,
  `TaskCanceledException`, `ObjectDisposedException` and `InvalidOperationException`, so the
  element vanishing cannot end the circuit. It disposes the watch and the module.
- `kit/ben-kit.css`: on phones, `body:has(.hosted-sticky-ask)` gets `padding-bottom: 5.5rem`, so
  the footer and the page's last line can always be scrolled clear of the button.
- No existing IntersectionObserver helper fitted: `FeedPage.razor.js` and `HomeAdPlayer.razor.js`
  each own theirs for their own purpose.
- e2e: `EventGalleryTests.On_a_phone_the_ask_button_steps_aside_for_the_form_and_comes_back`
  replaces the old "stays on screen" test, which scrolled 2000px blind and could land on the form.

## 3. Times with a leading zero

The shared patterns on `DateTimeViewerExtensions` were `hh:mm tt`, so `BenTime`, every grid column
and every `ToDisplayTime` / `ToDisplayDateTime` printed "07:00 PM", while hand-typed `h:mm tt`
places printed "7:00 PM".

Changed to the unpadded hour (the date stays `MM/dd/yyyy`):

| Constant | Was | Now |
|---|---|---|
| `DateTimePattern` (and `DateTimeNoSecondsPattern`, `GridDateTimeFormat`) | `MM/dd/yyyy hh:mm tt` | `MM/dd/yyyy h:mm tt` |
| `DateTimeWithSecondsPattern` | `MM/dd/yyyy hh:mm:ss tt` | `MM/dd/yyyy h:mm:ss tt` |
| `TimePattern` | `hh:mm tt` | `h:mm tt` |
| `TimeWithSecondsPattern` | `hh:mm:ss tt` | `h:mm:ss tt` |
| `ToDisplayTimeNoSeconds` | its own `"hh:mm tt"` | `TimePattern` |

Display sites that typed their own padded hour:

| File | Was | Now |
|---|---|---|
| Manage/Tours/OrgTourDateSeats.razor | `{…:MM/dd/yyyy hh:mm tt}` | `.ToDisplayDateTime()` |
| Publications/OrgPublicationPosts.razor | `MMM d, yyyy, HH:mm` | `MMM d, yyyy, h:mm tt` (invariant) |
| SuperAdmin/AdminTestPosts.razor | `MM/dd/yyyy HH:mm` | `.ToDisplayDateTime()` |
| Kit/FieldKitPlayer.razor (×2), Kit/FieldKitEnginePlayer.razor | `HH:mm:ss` chart labels | `.ToDisplayTimeWithSeconds()` ("7:41:05 PM") |

One shared formatter: 26 hand-typed `.ToString("h:mm tt")` / `.ToString("MM/dd/yyyy h:mm tt")`
calls in 19 Library files now call `ToDisplayTime()` / `ToDisplayDateTime()`. They already read
right; this keeps them reading the same as everything else, and in the invariant culture.

`DateEntry` (BenDateField) shows typed times with the same pattern, so a time field now reads
"8:00 PM"; its placeholder and its "Write the time like …" sentences say "8:00 PM". It still
reads "08:00 PM" and "20:00" when typed.

Kept, because a machine reads them (each is in the guard's allow-list with its reason):

| File | Format | Why |
|---|---|---|
| Ben.Web.Website/Program.cs | `{Timestamp:HH:mm:ss}` | Serilog console line template |
| SuperAdmin/AdminAuditLog.razor (×2) | `yyyy-MM-dd HH:mm[:ss] UTC` | the audit record copied into an email, matching the UTC AuditLogs table |
| Organization/NewInvestigationWindow.razor | `HH:mm` | value of an `<input type="time">` |
| Manage/Events/OrgEventPage.razor | TimeSpan `hh\:mm` | value of an `<input type="time">` (a TimeSpan's `hh` is 24-hour) |

Left alone, not padded hours: TimeSpan durations (`h\:mm\:ss`, `m\:ss`), `EventLength`'s `h:mm`,
the interpolated `{x:h:mm tt}` sites, `ShortDayDate` patterns. Outside the website projects,
`Ben.Data.WebApi` has no padded display hour.

### The guard

`ClockHourSourceGuardTests` scans every .cs/.razor/.cshtml in Ben.Web.Website,
Ben.Web.Website.Library and Ben.Web.Services for `hh:mm` / `hh\:mm` / `HH:mm`, after stripping
`@* *@`, `/* */` (anchored, so `accept="image/*"` is safe) and `//` (not the one in `https://`).
The allow-list has an exact count per file; one more use, or an entry that matches nothing, fails.
Run before the fix it failed with 11 offences (the five patterns in DateTimeViewerExtensions,
OrgTourDateSeats, OrgPublicationPosts, AdminTestPosts, the three FieldKit labels).

Tests updated for the new output: DisplayDateFormatTests (the morning test pinned the leading zero;
it now pins its absence), DateTimeViewerExtensionsTests, DateEntryTests,
DateFormatSourceGuardTests; e2e DateFieldTests, TimeZoneTests, LookAndTruthsTests (which also now
fails on a leading zero anywhere on /events).
