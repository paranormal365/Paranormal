# Session player, phase 7: the tests stop sleeping (10/28/2026)

Branch `fix/player-tests-wait-on-conditions`. Phase 7 of the session-player plan. Tests only; no product change,
no migration.

## Why

A fixed pause before a check passes on a quiet machine and fails on a busy one, and Ben's rule is that the
player must never be flaky ("it needs to be the pinnacle of our site"). Every fixed `WaitForTimeoutAsync` /
`Task.Delay` before an assertion in the player's tests is now a wait on the thing the check is about.

## Two waits that waited for nothing

`FieldSessionTransportTests.PlayheadPastAsync` and `AssertHeldAsync` imported the engine module inside
`WaitForFunctionAsync`. That makes the check async, and a promise is truthy, so both returned at once. They
are now synchronous.

The shared fix is in `BenTestBase`:
- `ExposePlayerAsync` imports the engine once and leaves `window.__fkState()`.
- `WaitForPlayerAsync("s.playing && s.playhead > 1000")` polls that state every 100 ms.

## What each sleep became

| Test | Was | Now |
|---|---|---|
| Engine: drag while playing | 1.5 s after Play | playing and past 0:01 |
| Engine: nothing crosses the connection | 1.5 s "settled"; `playhead > 5000` | the socket quiet for 1 s; the playhead moved > 4 s in the 8 s window |
| Engine: map marker | 400 ms after a scrub | the map's position reaches the expected fix |
| Media clock: Pressing play | 3 s; `if (shown >= 10)` Cellar | audio past 2 s; waits for 0:10.5, then **always** checks the Cellar |
| Player fixes: scrub after pause | 2 s, 1.5 s, 1.5 s | playing past 0:06; the recording's seek finished and 5 frames drawn |
| Stage (all) | 500 / 600 / 1500 / 800 ms | engine has the session's length; seeks finished with a picture; sound past 0:07.5 and video moving |
| Multi-viewer | 1.5 s before each scrub | that viewer playing past 0:01 |

Some waits stay on purpose because they are measuring windows, not waits:
- 8 s of playback while counting frames;
- 5 s at 16×;
- 1.5 s proving nothing restarts a paused session;
- the slow-network delay route.

## Stage tests: WebKit instead of real Chrome

`FieldSessionStageTests` launched the Chrome channel because Playwright's Chromium has no H.264 or AAC. They now
launch Playwright's WebKit:
- WebKit is Safari's engine, where Ben's own sessions misbehaved.
- On the Mac, WebKit plays the phone's formats.

If WebKit isn't installed, the tests are ignored with the install command rather than failing.

## New: `Two_recordings_hand_over_without_a_gap`

The session has `audio-001.wav` from 0:04 to 0:14, then `audio-002.wav` from 0:14. Playing from 0:12, the test
records every frame for 5 s. It checks three things:
- the clock never steps back;
- the second recording is sounding (playing, not muted, time moving) within 250 ms of the playhead reaching 0:14;
- the session plays on past the join.

## Bar

The whole player set five runs in a row in Chromium, plus a run in WebKit, with zero failures.

## What the five-run bar found (10/28/2026)

The first 5× Chromium + WebKit pass failed three times. Two of them were real faults in the player.

1. **The clock froze at Play on a slow connection** (Chromium run 5: the first second moved 0.2 s). After
   `play()`, an element is "not paused" with data long before it sounds, and the engine followed its
   standing-still time. `clockElement` now follows a recording only on frames where its `currentTime`
   moved; other frames run on the page's clock.
2. **A gap at a hand-over in WebKit** (683 ms). Two causes:
   - **Rule M6** let a clip in its lead second (start − 1 s) beat a clip already playing. At a join, the
     first recording lost its last second, and the second was started a second early and pulled back to
     0:00 until the join. The phone had the same rule. Fixed in `fieldKitTimeline.js` and
     `SessionReplay.swift`: a clip that has begun beats one only in its lead. The spec's M6 row and the
     shared `media.json` fixture (the 15 s case) are updated. Both conformance suites fail on the old rule
     at that case and pass on the new one.
   - **The start delay.** The engine learns how long `play()` takes to sound in this browser (`startLead`,
     measured on its own plays) and starts the next recording that much early, muted (`prerolling`). It is
     unmuted when it covers (rule M7).

   - **WebKit doesn't start a never-played recording until it is seeked.** After `play()` it stands at
     0:00, "playing" and fully loaded, until something sets its time; the drift rule did that only at
     0.35 s. A playing recording that shows the same time for 150 ms (`standingStill`, `STILL_KICK_MS`)
     is now given its place, aimed ahead like the drift rule so the clock never waits on it. Setting
     `currentTime` before or right after `play()` made no difference. Neither did warming the recording
     silently 2.5 s ahead, which also corrupted the seek lead (it jumped the recording 1.5 s too far);
     the warm-up was removed.

   After these fixes the hand-over measured 150–165 ms in WebKit and 0–120 ms in Chromium, and the
   session keeps real time across it. The limit is 300 ms in WebKit and 250 ms in Chromium (it was
   500 ms for both).
4. **Sign-in refusals in WebKit.** The API allows 20 sign-ins a minute from one address. The transport
   tests signed in to the API in every test as well as on the page, which came to about 22 a minute,
   and the slower WebKit runs were refused. They now sign in to the API once per class.
5. **`A_manager_can_cite…` skipped itself** as "case not in the seed data" when it ran first on a cold
   host. `OpenOrganizationAsync` read the groups page at NetworkIdle, before the list arrived over the
   circuit. It now waits for the group to appear.
3. **`MyFieldSessionsTests.Panning_the_map…` in WebKit** dragged while the map was still framing its pins.
   Like `MapViewportTests`, it now waits for `describe().framing` to end, and logs the center before and
   after.

Changelog lines: `webapp.md` (the hand-over and the slow start) and `ios.md` (the M6 hand-over, next build).
