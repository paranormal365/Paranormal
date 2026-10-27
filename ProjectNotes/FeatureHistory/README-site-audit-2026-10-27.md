# Site audit for flakiness and gaps (10/27/2026)

Branch `fix/site-audit-2026-10-27`. Ben asked: "Audit the entire site and look for flakiness and gaps. When
found, fix it."

## How the site was audited

1. A full e2e run on a fresh database (`IsHauntedDb_audit1_0904`): 1,227 tests, 1,097 passed, 0 failed,
   130 skipped. Every skip was read and sorted into two groups:
   - **Skips caused by missing data.** These were fixed by having the test arrange its own data, or by
     seeding it.
   - **Skips caused by configuration** (real Stripe, real mail, the owner's password). These were left
     alone.
2. SiteSweep (`BEN_SWEEP=1`): 1,201 screens as every seat, light theme. It found no console errors and no
   failed requests. It found one real clipping fault and two contrast faults. The 1.00:1 contrast rows over
   hero photographs are false positives.
3. ProductWalk (`BEN_PRODUCT_WALK=1`): 8 personas. Four of them failed (visitor, member, viewer, owner).
   Each failure was traced to its screenshot.

## What was wrong in the product, and the fix

| Fault | Fix |
|---|---|
| Any page could drop its circuit with `ObjectDisposedException: SemaphoreSlim.Release`. `NotificationState` disposed its gate and token source while a refresh was still running. BenNav uses it on every page. | `Dispose` now only cancels. `Publish` returns once disposed. A new test, `A_refresh_in_flight_when_the_circuit_goes_away_ends_quietly`, failed before the fix and passes after it. |
| `StoreCheckoutCompletePage` and `Subscribed` had the same dispose-while-polling race. | Their `Dispose` methods now only cancel. |
| The audio player showed WaveSurfer's raw error ("Failed to fetch"), had no way to retry, and failed for good on a single network blip. | It now shows a plain sentence and a **Try again** button (`ws-player-retry`). After a network failure it retries once by itself. The raw error is kept in the tooltip. |
| The event menu's gallery frame was clipped (SiteSweep's only hard failure). | The frame is marked `aria-hidden` and fades out with a mask. The audit no longer flags clipping inside `aria-hidden` decoration. |
| `.btn-danger` failed 4.5:1 contrast. | It now uses `--bs-danger` (#a85593), with hover and active colors mixed from it. |
| Calendar day headings, the caption and other-month days failed contrast. | They now use `--ben-ink-muted`, in all three copies of `telerik-signal.css`. |
| **Evidence from a group's public events never reached an ordinary member.** The Calendar tab is open to every member, but listing the evidence review queue needs the Calendar area's Read grant. A member without it got a 403, and the queue rendered that as "nothing waiting". This was found once the evidence tests stopped skipping. | The queue is shown only to people with Calendar Read. Accept and Decline appear only with Calendar Update; without it, a row reads "Waiting for someone who can publish it". A queue that fails to load now says so with the server's reason (`evidence-queue-failed`). The test now has the owner review, and checks that a member without the grant sees no refusal. |
| **A group page could show the wrong tab.** A rail link clicked while the group was still loading changed the address (`?tab=calendar`) and lit the rail, but the page kept showing Details, because `OnParametersSetAsync` ignored `?tab=` until loading had finished. | `?tab=` is followed whether or not loading has finished. |
| The evidence review test clicked the first Accept in the queue, which could be the seeded submission. It also looked for the old heading, "Evidence from attendees". | It accepts its own row and confirms the change, then looks for `event-evidence-list`. |
| The two map-panning tests started their drags in the middle of the map, which is where the pin sits, so the drag grabbed the pin instead of moving the map. Whether they passed depended on where the page had scrolled. | The drags start away from the middle. |
| Admin users "former members" test counted rows on one page of 25. | It now reads the pager's total. |
| The home page pricing test failed when the page redrew the button mid-scroll ("not attached to the DOM"). | `Oct16TestBase.ScrollToAsync` retries the scroll. |
| Three nullable warnings in the site (`BenAdminClientAdapter.EventScenario`, `MyCaseDetail`, `PlaceView`). | Fixed; the site builds with no warnings. |
| The seeded hosted-event venue had no coordinates, so the weekend never appeared on the map. | The seeder gives it coordinates, and fills them in on a place that has none. |
| No seeded closed account and no seeded place with a public contact, so the tests for both always skipped. | `SeedAClosedAccountAsync` and `SeedAProvingPlaceAsync` were added to `DevelopmentDataSeeder`. |

## What was wrong in the tests and the walk, and the fix

- **LookAndTruths.** The test clicked the "work waiting" banner instead of the group grid. It now targets
  `.k-grid a[href^='/organizations/']`.
- **EventEvidence.** The test looked for a past event in the public list, which shows only upcoming
  events, and used the old org name `tgh`. It now reads the group's calendar through the API.
- **MyFieldSessions and MediaLibrary.** These tests now upload their own session or photo instead of
  skipping on a fresh database. The shared helpers are `ApiSignedInAsync` and `SendAFieldSessionAsync`,
  both in `BenTestBase`.
- **MyEvidenceArchive.** The retract test depended on another test that runs later. It now publishes
  first if nothing is published.
- **EventGallery.** The skip message now includes the API's own reason that the event isn't taking
  bookings.
- **ProductWalk:**
  - The group steps now use the left rail (`rail-context`). The tab strip they used steps aside wherever
    the rail shows.
  - The search step waited for old wording. It now waits for "found near".
  - The walk now brings its own feed post and its own field session. A fresh database has neither.
- **Warnings.** The test project's 7 warnings are cleared:
  - the obsolete `FrameLocator().First`;
  - three null checks in `CmsEditorUxTests`;
  - a hidden `SignUpAsync`.

## Results

| Run | Passed | Failed | Skipped |
|---|---|---|---|
| Before (`audit1`) | 1,097 | 0 | 130 |
| After the first fixes (`audit2`) | 1,103 | 3 (tests that used to skip, now running) | 122 |

The 3 `audit2` failures were fixed and rerun on fresh databases.

## Left alone, on purpose

- Skips that need real Stripe, real mail delivery or the owner's password. They depend on configuration
  and cannot be fixed by data.
- The SiteSweep contrast rows over hero photographs. They measure against the image's fallback color, not
  against what is actually behind the text.

## Deploy

There is no migration. The seeder changes only affect development and e2e databases.
