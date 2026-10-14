# Session player: the urgent fixes (10/14/2026)

Branch `fix/session-player-urgent`, from master (cd1b8ac0). This is Phase 0 of the session-player audit and rebuild. The whole plan, from the smallest pieces up, is in `session-player-engine.md` once that branch exists.

Ben asked for an audit of the online session player against the Field Kit on the phone. That covers every optional channel, several people watching at once, scrubbing, and a map that follows. He also asked whether the map should be rendered to a video at upload. That answer is in the engine branch's README.

Phase 0 fixes what was wrong on the player as it stands, before the rebuild replaces how it keeps time.

## Privacy and counting (API)

- **A photo's location leaked through share links.**
  - Both the share-link route and the signed-in file route served the ORIGINAL upload, so a link made with positions withheld still handed out the phone's location in the photo's EXIF.
  - Both now serve `IMediaIngestService.ServingPathFor`, the cleaned or stripped copy. The original stays on disk as the evidence.
  - Bundle photos were already stripped on the phone (`DeviceDataExporter.swift:209-212`). Bundle videos carry no location: the recorder sets no movie metadata (`CameraSession.swift:96`).
- **One person scrubbing a video counted as dozens of views.**
  - A player fetches a recording in many byte ranges, and each one wrote a share-view row and moved the counter.
  - A recording now counts once per visitor per 30 minutes. Opening the link still always counts.
  - The counter moves in the database (`ExecuteUpdate`) rather than being written back from a stale read, which lost views when two people watched at once.
- **The API couldn't tell viewers apart.** The website's media relay now forwards the visitor's address and browser, so the API can separate viewers. It only believes the address from the local machine, as it does for `WebApiClient`.

## The player (website)

- **Scrubbing after a pause snapped back.** A paused recording kept its place as the clock, so the `timeupdate` its own seek fired moved the playhead. If the drag left the recording, the playhead landed on the recording's first or last second. Pausing now hands the clock back.
- **The map froze after a replay or a jump back.** "Too soon to move the map" was measured one way only, so moving backwards froze the map until playback passed the old spot; a second play-through never moved it. Mark jumps and the restart now move it at once.
- **The accuracy circle trailed the marker.** It was drawn around the map's centre instead of the marker.
- **The map re-zoomed on every update.** Its zoom was worked out again from each update's accuracy, so it zoomed in and out and threw away the viewer's own zoom. It is now chosen once, from the first fix.
- **A clip recorded on its own sat one clip-length late.** Its reference carries its length and no offset, because the phone writes it when the clip ends. It now begins at "at − length", as the phone reads it.
- **The session ran from first reading to last.**
  - It now runs from the phone's `started_at` to `ended_at`.
  - A session the phone never closed now ends at the last thing it recorded, sound included, so the tail of a recording past the last reading can be reached.
- **Two overlapping recordings both played aloud.** Only one sound is heard now: the clock, or the latest to begin.
- **One malformed measurement ended the viewer's connection.** For example, `"emf": 48` instead of `{ "value": 48 }`. Every read now checks the shape first, and a bad reading is skipped. The upload guard now refuses such a document and names the reading.
- **"Try again" read the document a second time on top of the first.**
- **An expired link was reported as a broken file.** A recording's link carries a one-hour ticket. Played past that hour, it failed and was marked "won't play in this browser". A recording that already reported its length is now treated as an expired link: renewed once, with a one-line notice.

## The phone

- **A captured photo or clip took the reading number of the reading before it**, and the count came out one short per capture.

## Tests

- **xUnit:**
  - Shared photo is the cleaned copy; signed-in recording is the cleaned copy.
  - Scrubbing is one view; two visitors are two views.
  - Bare-value measurement refused; non-object reading refused.
  - All checked to fail on the old code, except "two visitors", which guards against the dedupe collapsing different people.
- **BenKit:** every reading has its own sequence number (failed on the old engine).
- **Playwright (`FieldSessionPlayerFixesTests`, via `scripts/run-e2e.sh`):**
  - Scrubbing after a pause stays put, inside and outside the recording.
  - A clip recorded on its own starts at 0:04, not 0:20.
  - A recording past the last reading can be reached (the total reads 0:16).
