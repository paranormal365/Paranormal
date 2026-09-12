# Trim a session to the evidence, on the phone (item 210)

An hour of recording usually matters for ten seconds. Before a session is sent, the investigator
drags an in point and an out point on the Send screen, and only what is between them is uploaded.

## The decision that shaped it

The backlog item described trimming **on the server after upload**. Ben asked whether it could be
done on the phone instead, and it can, and it is better on every axis:

- **The original never leaves the device.** "The full recording stays on this phone" is a fact by
  construction rather than a promise the server has to keep.
- **Nothing on the server is ever destroyed.** There is no irreversible operation, no "are you
  sure", and no conflict with a session that is already published, cited in a report, or reachable
  by a share link (item 207).
- **No server ffmpeg.** The API has none configured; iOS has AVFoundation.
- **It saves the upload as well as the storage.** Ten seconds go over a home connection instead of
  an hour.

What it does not cover: a session already on the server. That is a separate piece of work if it is
ever wanted, and it would carry every warning the phone-side version gets to skip.

## Ben's spec for the control

*"In point, out point. Initially the in point is the start and the out point is the end. Scrolling
in point adjusts where it starts and same with end point. Show progress of in point when scrolling
it and of end point when scrolling it. The part to be exported should be between them and obvious
— the line between is bolder and a slightly different colour, the start point is a green dot and
the end point is a red dot."* That is `SessionTrimSlider`, exactly.

Then: *"couldn't they preview what they have while trimming?"* — `TrimPreview` is the review
screen's own replay pointed at the trimmer. Dragging either handle parks the playhead there and
shows the field and sound at that moment; **Play what will be sent** runs from the in point and
stops at the out point with the recording following, so what is heard is what will be sent.

And clip naming: of the two shapes offered, the times won — **back bedroom (20:00–30:00)** — because
they say what the clip *is* on the server's list, in the player and in the report, where a counter
would only say it is the third of something.

## Shape

All the decisions live in BenKit, where `swift test` runs them in under a second without a
simulator:

- `SessionWindow`, `TrimmableMedia`, `SessionTrimPlan` — what a window sends: each recording is
  **sent whole**, **cut** (offsets into the *original* file, which is what AVFoundation wants and
  what somebody can check against the copy on the phone), or **left out**. Photos are moments,
  recordings are spans. **A recording of unknown length is sent whole rather than guessed at** —
  dropping evidence on a guess is the one failure worth ruling out.
- `SessionTrimRange` — the in/out drag rules: neither handle passes the other, a finger off the
  end of the track stops at the end, an interrupted session uses its last reading as its end.
- `ReadingLog.rawLines(within:)` — filters by timestamp while keeping every line **verbatim**; a
  line whose timestamp will not decode is kept, not dropped.
- `DeviceDataExporter.Request.window` — readings outside it are not written and the document
  declares the window as its own span, so the server does not record an hour-long session holding
  three readings.
- `DeviceDataExporter.rebaseAudioOffsets` — **the one nobody would think of.** A reading's
  `start_offset_seconds` counts into the recording, and the player reconstructs where the recording
  begins by subtracting it. Cut sixty minutes to ten and every offset still counts from a beginning
  no longer in the file, so the audio lands an hour from its readings. Rewritten by a targeted text
  edit, never a re-encode, so the reading lines stay the bytes the device wrote.
- `SessionMediaTrimmer` — `AVAssetExportPresetPassthrough` into a scratch directory the upload
  clears. **The original is never opened for writing**, and every failure sends the whole file.

The app side is `SessionTrimSlider`, `TrimPreview`, and a section in `UploadSessionView` that
builds the plan from `replayData` (durations and markers were already there — the first version
probed every file with AVFoundation for a question the store had answered).

## What the UI harness found that a screenshot never could

The resting slider screenshotted perfectly. The UI test's hierarchy dump then showed a drag to the
**middle** of a 9 s track landing the in point at **0:08**, and the out point unable to move at
all. Two real bugs in the view:

1. **The drag double-counted the handle's offset.** A dragged view reports its gesture relative to
   where it sat *before* the offset, and the arithmetic added the handle's position back in — so
   the in point ran away rightward with every pixel of movement, and the out point's fraction
   clamped past the end. Fixed with a named coordinate space on the track.
2. **The Form claimed the drag.** A leftward drag from a row's trailing edge is a swipe; the out
   point lives at that edge. `highPriorityGesture` outranks it.

Plus two harness findings worth keeping: an `accessibilityIdentifier` on a `GeometryReader` or a
`VStack` is **inherited by every child** and names no element itself (both handles surfaced as
`trim-track`); and a pre-existing upload test had been querying the investigation picker as an
`Other` when it has always been a `Button` — it passed only through the Send button, which the
new section pushed below the fold of a lazy `Form`.

The drag test now asserts the in point **lands between 0:02 and 0:07** for a drag to the middle,
not merely that something changed.

## Verification

- **BenKit: 314 tests, 43 new**, each guard proven against broken code (unknown-duration drop,
  original-span-kept, offset-rebase off).
- **UI: 3 new (`SessionTrimUITests`) plus the two existing upload/export tests**, all passing on
  the iPhone 17 Pro simulator against a local API, signed in. They skip without
  `TEST_RUNNER_BEN_API_BASE_URL`, and say so.
- The screenshot attachment from the passing drag test shows In 0:04 / Out 0:09, the band over
  the kept region, the preview at 0:04, and "audio-001.m4a — cut to 0:04".

**Not verified here:** a real cut through `AVAssetExportSession` on a real recording, and the
server accepting a trimmed document end to end. The unit tests prove the document and the plan;
the export session runs only on a device or simulator with a real m4a, which the fake-sensor
session does produce but the tests stop short of uploading. Worth one manual upload from a phone.

## Documentation

`the-mobile-apps.md` gains **Sending only the part that mattered**.
