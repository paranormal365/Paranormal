# A session does not record the moment it is created (item 215)

Ben, 2026-09-04: *"When you start a Field Kit session, I think it shouldn't start recording
immediately. They may want to set everything up first and then start. So make the Stop button be a
Start to actually start the recording itself, and then the Stop be a stop button to end the
session. They can delete the session if nothing happens and they want to free up space."*

## What changed

A new session opens **pending**. The live screen is up, the gauge runs, the room can be named, the
channels switched, the base level set — and **nothing is logged**. The pinned bar reads *not
started* with **Discard** beside it and a green **Start**. Start begins the clock and opens the log;
the bar becomes **Stop**, and Mark, Note, EVP and the capture buttons appear. Stop ends the session
exactly as before.

The naming sheet's button is now **Open the session** rather than "Start recording", because it no
longer starts anything.

## The three rules underneath

**A fourth state, on purpose.** `FieldSessionOutcome` gains `pending` beside `recording`, `ended`
and `interrupted`. It has to be its own state: a pending session the phone dies on lost nothing,
so reporting it as *interrupted* would be a lie about a night that never began; and it holds no
readings, so listing it as a *recording* would be one too. Recovery at launch only touches
`"recording"`, so a pending session comes back exactly as it was, ready to start or discard.

**The engine's one gate.** Sensors run from `start()`; readings reach the log only after
`beginLogging()`. Every path to the log — interval samples, marks, notes, sentry events — goes
through one `append`, so that is the single place the gate lives. A mark before Start has no
session time to belong to; the screen hides the control, and the gate is the guarantee behind it.

**Start moves the clock.** `startedAt` is rewritten to the moment Start is pressed, not kept from
creation. Everything downstream measures from it — the trimmer's track (item 210), the export's
span, the media clock, the report's readout — and a session set up for ten minutes before Start
would otherwise carry ten minutes of empty timeline in front of its first reading. The audio
recording, if that channel is on, starts at Start too, so its first second is the session's first
second rather than sitting in the past on the media timeline.

## Shape

- BenKit: `FieldSessionOutcome.pending`; `FieldSessionSummary.isPending` / `isOpen`;
  `FieldSessionStore.beginRecording(_:)` (idempotent — a double tap is one start);
  `FieldSessionEngine.beginLogging()` / `isLogging`; `ActiveFieldSession.startSession(at:)` and
  `isRecording`. `load()` resumes into any *open* session, pending included.
- App: `LiveSessionView` bar (Start / Discard vs Stop), controls gated on `isRecording`, a set-up
  hint, `discard()`; `SessionClock` gains `isPending` (holds at 00:00:00, says *not started*
  rather than *stopped*); `FieldKitHomeView` lists pending sessions under *Set up, not started*.

## What the screenshot exposed

The first pending screenshot showed the clock corner reading **"00:00:02 stopped"** — counting
set-up time as if it were the session, and claiming something had run and ended. `SessionClock`
had two states and needed three. Fixed and re-verified; the second screenshot reads
*00:00:00 not started*.

## Verification

- **BenKit: 317 tests, 3 new** — Start moves the clock and is idempotent; a never-started session
  survives a relaunch as pending, not interrupted; **nothing reaches the log before Start** (proven
  to discriminate: with the gate removed, it fails). Three existing tests encoded the old
  "created = recording" rule and were brought to the new one; the engine fixtures open the log
  because those tests are about a session that has begun.
- **UI: 25 of 25** on the iPhone 17 Pro simulator — all 18 Field Kit tests (every one drives
  start/stop and every one gained the Start step), 4 new `SessionStartUITests` (pending offers
  Start and no Mark; Start brings Stop and the controls and readings actually land; Discard removes
  the session; a pending session survives a relaunch as pending), and the 3 trim tests.
- **The App Store demo-video script** (`FieldKitDemoDriveUITests`) now sets the base level while
  pending and then presses Start, so the footage shows the gauge before the clock begins. Run and
  passing.
- One test-side lesson: the first discard test asserted that no "not started" row existed
  afterwards, which was about the run's history — earlier tests leave their own pending sessions —
  not about this discard. It now counts rows before creating the session and after discarding it.

**Not touched:** `FieldKitUploadProbeUITests.swift` is Ben's own untracked file. It drives a session
through `stop-field-session` and will need the same `start-recording` tap before it runs again.

## Documentation

*The mobile apps* → *Your first session, start to finish* is rewritten for the eight steps, and
says what Discard does and why a never-started session is not "interrupted".
