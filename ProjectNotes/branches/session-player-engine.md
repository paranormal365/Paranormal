# Session player: the browser keeps time (10/14/2026)

Branch `feature/session-player-engine`, on top of `fix/session-player-urgent` (Phase 0, its own README).

## What Ben asked

> Go through and perform an audit of the online session player and ensure everything works and matches visually and operationally as the field kit with every possible optional scenario. Ensure it is able to hold up being used by more than one person at a time. Also, ensure scrubbing follows and tracks correctly and the mapping follows as well. I think when recorded and submitted from the field kit, we could record it as a video instead of hitting the mapping and drawing it every time it is requested… It is to track the direction and location and move smoothly each reading.

Then: "make a plan to do this from the most atomic level to implementation".

His answers to four choices:
1. **The browser keeps time.**
2. **The map follows smoothly**: a viewer's own pan stops it following until they press Recenter.
3. **Place pages get the player.**
4. **Include the phone.**

## The map as a video: not done, and why

Today nothing calls a mapping API per reading:
- The map is Apple MapKit JS, drawn in the viewer's browser.
- A page load is one map view against a daily allowance of 250,000.
- The building's tiles come from Apple to the browser once and are cached there.
- Moving the marker, turning the arrow and panning within those tiles cost nothing.

A pre-rendered video would need all of the following:
- **A map source that allows storing pictures.** Apple serves no raster tiles, and its terms forbid keeping map imagery (`MapKitTokenService.cs:84-86`), so this means a paid provider.
- **ffmpeg on the production server.** It is not configured there.
- **A render queue.** None exists.
- **Three videos per session**, one per audience: exact, withheld, and the place's public point. Each would be redone whenever privacy or publication changed.
- **Storage** for all of those videos.

It would also lose pan, zoom, the pins and the accuracy circle. Smooth movement is done in the browser instead (below).

## The pieces, smallest first

1. **The rules** (`ProjectNotes/specs/replay-semantics.md`). One row per rule for what a moment of a session shows. The website and the phone both obey it.
2. **The fixtures** (`Ben.iOS/BenKit/Tests/BenKitTests/Fixtures/replay-conformance/`):
   - a walk, media, and an interrupted session;
   - each is a real document plus the frame expected at chosen moments;
   - both players are tested against them.
   - **Phone:** `ReplayConformanceTests.swift`, through the phone's own import and replay.
   - **Website:** `Ben.Web.Tests/Services/ReplayConformanceTests.cs`, which runs document → server builder → JSON → `fieldKitTimeline.js` in Jint.
3. **The timeline** (`Ben.Data.WebApi/Services/FieldSessions/FieldSessionTimelineBuilder.cs`, record `Ben.Service.Models/FieldSessions/SessionTimeline.cs`). The document is read once on the server by those rules:
   - times are in ms from the start, and readings are columns;
   - each reading carries the base in force;
   - clips are placed by the rules;
   - traces are thinned in time order;
   - the walked path is included.
   - **Recording lengths** are read from the files' own headers (`MediaDuration.cs`, the `mvhd` box or a WAV header, no ffmpeg), so a video is placed on the server rather than in each viewer's browser.
4. **The endpoints and cache** (`FieldSessionTimelineService.cs`):
   - `GET api/field-sessions/{id}/timeline` for signed-in viewers;
   - `GET api/shared-sessions/{token}/timeline` for share links, which strips positions unless the link includes them and gives only the one recording for a single-file link;
   - `GET api/public/field-sessions/{id}/timeline` for the public archive, with positions at the place's public point and recordings only once approved;
   - **The cache:** each timeline is built once per session per audience and kept in its own size-limited cache. The key covers the document, every file, the policy and the audience. Access is always checked before the cache.
5. **The frame in the browser** (`wwwroot/js/fieldKitTimeline.js`). Pure functions of the timeline and a moment, held to the fixtures.
6. **The engine** (`wwwroot/js/fieldKitPlayer.js`):
   - the clock, the transport (1–32×, previous and next mark), the scrubber, the keyboard, the media, the readouts, the stage, the photo glow, the marked row, the chart's playhead and the map, all at screen rate;
   - the sounding recording is the clock at 2× or slower;
   - it never sends anything to the server while it plays.
7. **The page** (`Kit/FieldKitEnginePlayer.razor`). Drawn once from the timeline. The elements the engine fills are rendered empty, so Blazor and the engine never fight over the page.
8. **The map** (`Kit/Maps/BenMap.razor.js`, `player*`):
   - the marker glides every frame;
   - the arrow turns the short way round, and snaps on a seek;
   - the accuracy circle moves in place;
   - the walked path is drawn;
   - a viewer's pan stops the following until they press Recenter.
9. **The chart** (`Kit/FieldTraceChart.razor`). SVG drawn on the server, on a time axis, with the playhead, dashed marks and the stretches the app was put away.
10. **Place pages**: `/places/{id}/sessions/{session}`. Each archive row now has a "Play it back" link.
11. **The phone**:
    - the base in force per reading;
    - a "Moving" facing from the track;
    - the gap rule on Play;
    - tapping a photo goes to its moment;
    - the marked row lights;
    - the arrow is relative to the map's heading, eases its turns and snaps on a seek;
    - the follow camera with Recenter.

The first player stayed one setting away for a release (`FieldKit:Player = legacy`) and was retired on 10/28/2026 (`feature/player-retire-legacy`).

## Proof

- **The rules:**
  - The phone's previous replay fails the fixtures with 12 issues: the base and the Moving facing.
  - Breaking the lead, the accuracy rule or the Moving facing in the JS fails the website's run.
- **Many viewers:**
  - **Nothing crosses the server connection while a session plays.** In 8 seconds of playback, the old player took 152 frames from the server and sent 166. The engine took 0 and sent 0 (`FieldSessionEngineTests`).
  - Ten viewers on one session: `MultiViewerPlaybackTests` (opt-in, `BEN_MULTI_VIEWER=1`).
- **Scrubbing:** a drag during playback lands where it was let go and plays on from there (sampled every 250 ms). The sound goes with it.
- **The map:** the marker sits where the walk was between fixes; the arrow points the way they faced; the circle is the fix's accuracy; a viewer's pan stops the following and Recenter restores it.
- **Privacy:**
  - A share link without positions carries no coordinate in its timeline.
  - The public archive's timeline carries no true coordinate. Without the sweep, the test fails.
- **Existing tests:** the existing field-session browser tests pass unchanged against the engine, apart from the chart selector, now `[data-testid=trace-magnetic] path.fk-trace`.

## Still to do in this arc

- Help text, screenshots and the product PDF.
- `/changes` entries.
- A simulator pass on the phone.
