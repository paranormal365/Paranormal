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
- the second recording is sounding (playing, not muted, time moving) within 500 ms of the playhead reaching 0:14;
- the session plays on past the join.

## Bar

The whole player set five runs in a row in Chromium, plus a run in WebKit, with zero failures.
