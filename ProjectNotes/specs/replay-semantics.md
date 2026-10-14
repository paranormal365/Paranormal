# What a moment of a field session shows

The rules for replaying an uploaded Field Kit session. **One rule per row, obeyed by both the website's player and the phone's review.** Each row names the code that implements it on each side.

The conformance fixtures in `Ben.iOS/BenKit/Tests/BenKitTests/Fixtures/replay-conformance/` hold these rules to account on both sides. Each fixture is a session document plus the expected frame at chosen moments.

Times in fixtures are whole milliseconds from the session's start. Numbers compare within a tolerance (1e-6 for coordinates, 0.5 for degrees, 1 ms for times), never exactly: the phone, the browser and .NET disagree in the last decimal place, and that is not a difference in the rules.

Written 10/14/2026, from the audit of both players. Where the two disagreed, the row says which way it was settled and why.

## The session

| # | Rule | Was |
|---|---|---|
| S1 | **Start** = `session.started_at`, or the first reading if that is earlier. | Web: first reading. Phone: `started_at`. |
| S2 | **End** = `session.ended_at`, or the last reading if that is later. With no `ended_at` (an interrupted session), it is the latest of: last reading, last marker, last recording's end. | Web: last reading, so a recording past it could not be reached. Phone: this rule. |
| S3 | Readings are sorted by `at`. A reading with no readable `at`, or that is not an object, is skipped and counted, never fatal. | Web threw and ended the viewer's connection. |

## What the instruments read

| # | Rule | Was |
|---|---|---|
| I1 | **Held, not interpolated.** The instruments show the last reading at or before the moment (binary search). Before the first reading, every instrument reads "—". | Both. |
| I2 | **Magnetic field** shows as mG from base: (µT − base µT) × 10. With no base in force it shows the absolute value, labelled "abs" on the web and "no base" on the phone. | |
| I3 | **The base in force** is the `emf.baseline` on the held reading, or the last one written before it. A re-baseline partway through applies from that reading on. | Web: the first base seen. Phone: the last base seen. Both were wrong for a session that re-zeroed partway. |
| I4 | **Sound** shows as absolute dBFS, held. | |
| I5 | **Room** is the held reading's `room`. It is not carried past a reading that has none: readings carry the room for as long as one is set, so its absence means they stopped saying. | Both. |

## Where they were

| # | Rule | Was |
|---|---|---|
| P1 | **The track** is every reading with both latitude and longitude, at the reading's own `at`. | |
| P2 | **Position is interpolated** between the fixes on either side of the moment. | Both. |
| P3 | **Before the first fix** it is the first fix; **after the last fix** it is the last fix. | Web drew nothing before the first fix. |
| P4 | **Holds at the earlier fix** when the two fixes are more than **30 s** apart, or at the same moment. A gap of minutes is a lost fix, not a walk. | Both. |
| P5 | **Accuracy** is the worse (larger) of the two fixes when interpolating, otherwise the held fix's own. | Web: the last accuracy seen. |
| P6 | **Facing** (the arrow), from the held reading: `heading_degrees` is **Looking**; otherwise `course_degrees` is **Walking**; otherwise the bearing between the fixes either side, if they are at least **5 m** apart and no more than 30 s apart, is **Moving**; otherwise **none**, and a dot is drawn instead of an arrow. | Web: scanned back without limit and held a bearing from minutes earlier. Phone: had no "Moving". |

## What was recorded

| # | Rule | Was |
|---|---|---|
| M1 | **A marker** is a reading whose `marker` names a marker kind (manual_marker, sentry_emf, sentry_sound, evp_question, evp_wait_end, device_moved, scene_motion, app_backgrounded, app_returned). The website also shows any other label that is not a capture kind, as a manual marker titled by its label, so a kind added by a later app is not lost. The phone only writes kinds it knows, so it drops a label it doesn't know (a session from a newer app opened on an older one). This is not in the shared fixtures. | |
| M2 | **A capture** is a reading whose `marker` is photo, video or audio, with the path in `audio_ref.filename` or in the note `"<kind>: media/…"`. | |
| M3 | **A photo** is an instant at its reading's `at`. | |
| M4 | **A clip recorded on its own** (audio or video capture) began at `at − length`. The length comes from `audio_ref.duration_seconds`, otherwise from the file itself. The reading is written when the clip ends. | Web placed a capture's audio at its end. |
| M5 | **The session's own sound recording** is referenced by `audio_ref` with `start_offset_seconds` on readings taken while it ran. It began at `at − offset`. The first such reference fixes it. | |
| M6 | **Covering:** for each of audio and video, the clip at a moment is the **latest-starting** one with start − **1 s** ≤ moment < end. Its offset is max(0, moment − start). | Web: one clock across both kinds, with no lead tolerance when choosing it. |
| M7 | **Heard:** one sound at a time, the covering audio clip. A video is muted while an audio clip covers the moment. | Web played two overlapping sounds together. |
| M8 | **"Motion detected"** shows from a scene_motion marker until 3 s after it (the latest such marker within 3 s). | Both. |
| M9 | **A photo glows** from its moment until 3 s after. | Both. |
| M10 | **The nearest marker** is the first within 1 s of the moment. It highlights its row. | Phone computed it and showed nothing. |

## Moving around

| # | Rule | Was |
|---|---|---|
| T1 | **The clock** is measured, not counted: playhead = anchor + elapsed real time × speed, off a monotonic clock. It is re-anchored on play, on a seek, and on a speed change (from the measured position). | Both (see `feedback_measure_time_dont_count_ticks`). |
| T2 | **Speeds:** 1, 2, 4, 8, 16, 32×. | Web stopped at 16×. |
| T3 | **At the end** playback pauses there; Play at the end starts again from the start. There is no loop. | Both. |
| T4 | **Jumping to a marker** lands **3 s before it** (not before the start). Next and previous step past the marker the playhead is parked before. | Both. |
| T5 | **Scrubbing** moves the playhead and everything with it at once. It does not pause playback that is running (the phone's slider). Dragging on the chart pauses, then seeks. | Web paused on every scrub, and a paused recording snapped the playhead back (fixed 10/14). |
| T6 | **Play in a gap:** when nothing covers the moment and a recording begins later, Play moves the playhead to that recording's start (Ben, 09/16/2026). | Web only; the phone adopts it. |
| T7 | **Tapping a photo** moves the playhead to its moment, pauses and shows it full size. Closing it resumes if it was playing. | Phone did not seek, despite its own comment. |

## Presentation, not rules

These are deliberately **not** in the fixtures: they are animation, not the meaning of a moment.

- **Easing the arrow.** It turns the short way round toward its target and snaps on a seek.
- **The follow camera.** It keeps the marker in view and lets go when the viewer pans.
- **How often the screen redraws.**
