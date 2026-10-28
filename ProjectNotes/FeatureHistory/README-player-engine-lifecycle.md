# Session player, phase 1: the engine never loses its transport, and a pause is a pause (10/28/2026)

Branch `fix/player-engine-lifecycle`. Phase 1 of the plan in `~/.claude/plans/okay-so-lets-go-splendid-spring.md`
("Session Player: up to date with the latest Paranormal Platform, and never flaky").

## Why

Ben, 10/28/2026: on his 10/23 7:48 AM session in Safari on the Mac, Pause did nothing; later it worked.
That session is a photo and the whole-session video, with no sound file (the iOS 1.1.5 microphone bug).
Two faults made Pause do nothing on exactly that kind of session:

1. **A pause from outside the page** (Now Playing, a media key, AirPods) pauses the `<video>`. The engine
   listened for that only on the covering *sound* recording, kept its clock running, and played the
   video straight back on the next frame.
2. **The engine started twice on one page.** The host set "started" only after two awaits, so a render in
   that window started a second engine on the same element. The visible buttons stayed on the first
   (stopped) engine; both listened to the video. Pause stopped the stopped one.

## What changed

`Ben.Web.Website.Library/wwwroot/js/fieldKitPlayer.js`
- One `AbortController` per engine: every listener is taken back when it stops; `stop()` also clears the
  transport and the motion sign, so a second start rebuilds a working transport.
- A pause from outside counts for the covering video when no sound recording covers the moment.
- The engine's own pauses are counted (one per `pause` event), not timed: at 4× and above the session no
  longer pauses itself.
- The Play button's icon and word change in place; a press mid-swap can't be lost.
- The slider lets go on a release anywhere (it stayed "dragged" after a release off it).
- A click inside the player gives it the keyboard (`tabindex="-1"` on the root).
- "Following the recording" no longer flickers at a hand-over (shows after 400 ms, holds 1 s).
- Safari: the first Play silently starts and stops every other recording, so a later hand-over may sound.
- How long a move takes to sound again is learned only from moves made while playing.
- Recordings this browser can't play never hide one it can (`frameAt(tl, ms, { broken })`).

`fieldKitTimeline.js`, `SessionReplay.swift`, `ProjectNotes/specs/replay-semantics.md` (rule T4)
- Next/Previous step from the playhead unless it is parked before a mark. While playing, Next used to skip
  a mark under 3 s ahead and Previous could pick one still to come. Two new cases in the shared fixture
  `media.json`; both the web (Jint) and the phone (`swift test`) failed them on the old rule.

`FieldKitEnginePlayer.razor`
- `_starting` guard (as `BenMap` has); a start superseded by a reload disposes itself.
- A `JSException` shows "The player couldn't start" with Try again instead of ending the connection.

`BenMap.razor.js`
- `isMap` asks for a map, not an entry; the deferred player set-up catches a MapKit failure; a missing
  bearing is forgotten so the next one snaps.

## Tests

Run against the old player (with only the button's test id, `dragging` and `timelineJson` added), five of
the eight new browser tests failed: the outside pause, the second start, the slider, Space and Next while
moving. The two Pause-button tests and 4× passed on the old code too — the button by itself worked, which
fits Ben's "must have been a glitch", and the 4× timer race did not show in Chromium. Those three stay as
guards.

- `Ben.Web.Playwright/Tests/FieldSessionTransportTests.cs` (new): Pause with sound and with video only,
  a pause from outside the page, a second engine start, 4×, the slider let go off it, Space after a
  click, Next while moving. New fixture `Fixtures/video-only-vp9.mp4` (see `Fixtures/README.md`).
- `ReplayConformanceTests` (web and phone): the two new jump cases.

No migration. The iOS change rides the next phone build.
