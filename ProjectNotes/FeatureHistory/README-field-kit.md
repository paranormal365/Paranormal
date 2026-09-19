# Field Kit — the phone as a field instrument (Area 10)

This branch turns the iPhone and iPad app into an instrument you can take into a building with no
signal, and then brings what it records all the way through to the document the client is handed.

## The shape of it

**On the device.** A session records the magnetometer (shown in milligauss, labelled as the
magnetic field it actually is), sound level, position, heading and relative altitude, with photos,
video clips and audio captured into the same session. On top of that: a sentry mode that watches an
unattended device for four kinds of event, EVP question-and-answer marking, a blackout screen so
the phone's own light stays out of the recording, spoken notes transcribed on the device, and
replay on the phone.

**On the wire.** A session exports as a Device Data Format v1 bundle — the published spec from
Area 7, which this app is the first device to implement — and uploads to the server a file at a
time, each with a checksum checked on arrival.

**On the website.** `FieldKitPlayer` replays a session on one playhead: the trace, the map, the
compass and the media moving together, read from the device's own document rather than anything
the server reshaped.

**In the report.** A case report section of type *Field Sessions* cites the sessions recorded for
that case's investigations, and the PDF says what each one holds.

## Two decisions worth knowing about

### Readings go in a file, not the database

A five-hour hybrid session is tens of thousands of readings. They stream to an append-only JSONL
file, one already-spec-shaped object per line, so export is "write the envelope, splice the lines
verbatim" and a crash costs at most one torn trailing line — which recovery truncates. SwiftData
holds only the low-volume relational rows: the session, its marks, its captures.

### The room is a person's word, not a measurement

A fix indoors is 20–50 m wide — the width of the whole building — so nothing the instruments
produce can tell the cellar from the front bedroom. The session carries a **room** the operator
sets, everything recorded inherits it until they change it, and changing it drops a mark of its
own. This is why naming individual photos would have been the weaker feature: it would have left
marks, readings and EVP questions with no room at all.

## What the report citation is, and is not

It is a **reference**. The document, the recordings and their per-file digests stay with the
upload; the section points at them. A citation that copied the readings would create a second
version of the night that could drift from the one the instruments produced, and keeping the
device's `data.json` verbatim exists precisely so there is only ever one.

A session can only be cited through **this case's own investigations**. Without that check a
manager of an org running two cases could put one client's night into the other client's report,
under the same letterhead — which is what `ASessionFromAnotherCase_CannotBeCited` holds in place.

The PDF states, per cited session: where it was, when it ran **or that it was interrupted** (a
phone that died mid-session has no honest end time), who recorded it or plainly that nobody was
signed in, the device model, the reading and mark counts, and every recording — flagged when its
checksum did not match on arrival.

## Verification

- `swift test --package-path BenKit` — 251 tests: engine, log recovery, export golden bytes,
  replay, room labels, dictation.
- `dotnet test Ben.slnx` — 5,631 tests, including the upload controller and the report citations.
  The PDF tests read the words back out of the generated document rather than comparing file
  sizes, because a size comparison passes just as happily when the generator writes the wrong
  words.
- `dotnet test Ben.Web.Playwright` against the three hosts — the citation walk uploads a session
  through the API and then cites it through the UI, which is the only way to catch the two halves
  disagreeing.
- `IsHauntedUITests` on iPhone and iPad, including the room control and the instrument panel in
  the dark.
- Export conformance is checked against the **spec's own** JSON Schema, and the zip against
  `unzip -t` — external authorities, not our decoder agreeing with our encoder.

## Deliberately not built

Feeding a session into the video editor. Recorded in the roadmap under Area 10 at Ben's request,
to be discussed once the apps are finished and proven.
