# fix/ios-session-sound-kept

**Ben, 10/23/2026:** "When running the session on 1.1.5, I don't hear any audio at all."

## What his session showed

Session 9e277d48 (10/23 7:48 AM, iPhone 15 Pro Max, 57 s) holds a photo and the whole-session
video, and no sound file. The video has no sound track by design (the session's own recording
carries the sound). The sound readings moved all session (-70 to -45 dBFS), so the meter worked.

## Cause

`FieldSessionEngine.restartSensorTasks()` runs whenever a setting changes mid-session (Watch for
Motion, sentry arm/disarm, any channel). It cancels the meter's `levels()` feed, and that feed's
`onTermination` called `LiveAudioCapture.teardown()`, which closed the open recording. The new feed
started the microphone again, so the meter kept moving; at Stop the session's recording state
asked a writer that was already gone, got 0 seconds, and deleted the file as empty.

The teardown was the same in 1.1.4; 1.1.5 is where sessions began changing settings after Start
often enough to hit it.

## Fix

- `BenKit/Field/MicrophoneDemand.swift`: who still wants the microphone (the current meter feed,
  identified by id, and an open recording). A feed ending removes only itself; the microphone goes
  away only when nothing wants it.
- `LiveAudioCapture`: a meter feed ending calls `meterEnded(feed)`; full teardown only when
  `MicrophoneDemand` says nothing wants the microphone, and after a recording ends with no meter.

## Tests

- `MicrophoneDemandTests` (BenKit).
- `FieldKitRealMicrophoneUITests`: the real microphone code in a simulator (the Mac's microphone):
  Start, switch Magnetic field off, Stop audio, and the recording must be listed. **Fails on the
  1.1.5 code, passes with the fix.** Needs `xcrun simctl privacy <udid> grant microphone
  com.ishaunted.ios` after install.

## Still owed

- Ben checks a real session on his phone with video on and a setting changed mid-session.
- Ships as 1.1.6 (version bump and App Store submission are separate steps).
