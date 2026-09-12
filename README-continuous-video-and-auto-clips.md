# Continuous video, and clips around what was detected

Branch: `feature/fieldkit-uploads-and-changelog`.

Ben, 2026-09-12: *"What if they can record continuous video, but have the option to record clips
when activity is detected which get logged in the timeline of their field kit session."* And on
how the clips are produced: *"Let the end user choose either to derive afterwards or cut live
during session and explain how each impact battery."*

## The shape

Three modes for a session, chosen where the channels are chosen, each stating its battery cost
before it is picked. `VideoRecordingMode` holds them and the wording.

| Mode | The camera | The clips | What it costs |
|---|---|---|---|
| Clips I record myself | Off between clips | You press Video | Cheapest by a long way |
| Record everything, clip it later | Runs all session | Marks now, clips at review | The camera and encoder all night; the clipping is free, and happens when you are plugged in |
| Record everything, clip as it happens | Runs all session | Written as each trigger fires | The above, plus a second encode at each detection while the camera still runs |

The default is the cheap one. Somebody who turned video on without reading anything must not get
the mode that flattens a phone before the night is over.

## What is built and tested

- `VideoRecordingMode` — the three modes, their summaries and their battery notes.
- `ClipPolicy` and `ClipPlanner` — how a moment becomes a clip. Eight seconds of lead-in, fifteen
  after, triggers within reach of one another merged into a single clip, a ninety-second cap so a
  busy night cannot merge into one clip the length of the session, and everything clamped inside
  the recording that actually exists.
- 13 tests in `ClipPlannerTests`, covering the lead-in, both clamps, the merge, the cap (and that
  the cap drops no triggers), out-of-order input and triggers with no footage behind them.

These are pure arithmetic and policy, and they are finished.

## What is NOT built yet

The capture pipeline. One `AVCaptureSession`, one video data output feeding three consumers:

1. an `AVAssetWriter` writing the continuous recording,
2. a ring buffer holding the last few seconds, so a live clip can start BEFORE its trigger,
3. the scene-motion analysis that Sentry mode already does.

Plus: a marker that records which video file was running and how far into it (mirroring the
`audioFilename` / `audioOffsetSeconds` a marker already carries for sound), the mode on the start
sheet and the live screen, and "make a clip of this" on a mark at review.

## The thing to be careful about

**None of the capture pipeline can be verified in the simulator — it has no camera.** The lesson
is one this branch already paid for once: Sign in with Apple shipped broken in 1.0.2 build 4
because nobody had ever run it end to end, and the App Review tester was the first person past the
button. The same trap is open here and it is wider, because a recorder that fails does so at 3am
in somebody's cellar with the night's evidence in it.

So, before any of this ships:

- Run it on a real iPhone, for at least one session long enough to matter, in the dark.
- Watch what happens when the phone locks, when a call arrives, and when the app is backgrounded.
  iOS suspends camera capture in the background; continuous recording only survives while the app
  is in front, which the blackout overlay keeps it. That limitation needs saying on screen, not
  discovering.
- Check the phone's temperature and what the charge actually did, and correct the battery notes
  in `VideoRecordingMode` if they are wrong. They are honest estimates, not measurements.
