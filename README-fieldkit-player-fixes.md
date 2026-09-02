# Field Kit player — correcting what the sweep found

Branch: `feature/fieldkit-player-fixes`, cut from `develop` at `5065e9e`.

The 2026-09-02 production sweep drove ten sessions through `FieldKitPlayer` — nine hand-built to
the shape `FieldSessionEngine` writes, plus one recorded by the real app on a simulator and
uploaded through the app's own two requests. The transport, markers, map, audio and video all held
up. Four things did not, and all four share a cause: **the player was written against a session
that has everything, so a session missing one channel loses more than that channel.**

## What is wrong

### 1. A photo is never shown

`FieldKitPlayer.razor` branches on `IsAudio` and `IsVideo` in the Recordings card and has no third
case, so a photo appears as a bare filename with nothing to click. The bytes are fine — the file
downloads, the digest matches, the content type is `image/jpeg`. The public place page renders the
same file as a thumbnail, which makes this worse than an omission: **the one person who cannot see
the photo is the member who took it**, while a stranger reading the archive can.

Variants A and E in the matrix. Fix: an `IsImage` branch rendering a thumbnail that opens the full
file, matching what `PlaceView` already does.

### 2. A room disappears when there is no GPS fix

The "Room: …" badge is nested inside the map card's *has-a-track* branch. Indoors a phone routinely
never gets a fix, so the card says "No position was recorded" and the room the investigator typed —
present on every reading — is never shown in the live view. It survives only on individual marker
rows.

This is the most user-visible of the four: naming the room is a deliberate act during a session,
and the reading is that the app discarded it. Variants B, G and I. Fix: the badge belongs to the
session, not to the map, so it moves out of that branch.

### 3. Only the magnetic field is charted

One series, `Microtesla`. Sound level is captured per reading and shown as a number in the readout,
but never plotted. Turn the magnetometer off — which the app allows mid-session — and the player
draws an empty chart with axes and no line, while the only channel that *has* data is invisible.

Variant I. Fix: chart what the session actually recorded. Magnetic when there is magnetic data,
sound when there is sound, both when there are both, and an honest sentence when there is neither —
not an empty grid, which reads as broken rather than absent.

### 4. The axis contradicts the readout

`ChartValueAxisTitle` is the literal `"mG from base"`. When no base level was taken the readout
above it says `540 mG abs` and the card header already drops its "(from base)" suffix — so the axis
is the only thing still claiming a baseline that does not exist. Seen on the real-app upload, which
never took a base level.

Fix: derive the title the same way the header and the readout already do.

### 5. Nothing on the website lists what the phone sent up

Added to the branch at Ben's request. `GetFieldSessionsAsync` had **no Razor consumer at all**, so
the only routes to `/field-sessions/{id}` were Report Builder's "Play back" button and a URL you
already held. A member who recorded a session, uploaded it, and then opened the website had no way
to find it: the upload appeared to go nowhere.

New page **`/my-field-sessions`** (Media → My Field Sessions): the account's own uploads, newest
first, each with a Play back button — and a map.

**The map**, Ben's idea, given the same treatment as the groups on the home page. Its coordinate is
not on the row; it lives inside the session document, so it costs a file read per session. That is
why it is its own endpoint, `api/field-sessions/mine/map`, rather than more fields on `mine`: the
phone calls `mine` on every Field Kit visit and must not pay for a map it is not drawing. The first
fix in a session is the whole answer — a session is one visit to one building, and the track's own
extent is smaller than the accuracy circle around any point in it.

**Who may see a pin** is a permission question, and the answer is the caller's own sessions: this
endpoint returns nothing else, and a person is entitled to see where their own work happened. There
is deliberately no public-only filter, which would leave a solo investigator working at private
addresses looking at an empty map of their own work. The rule that does bite is for any *shared*
map later: somebody else's session is readable only through `MayReadAsync` — attendee, org member,
public investigation or public case — and such a map must go through it rather than reusing this
query. A coordinate is the most sensitive thing a session carries.

Sessions with no fix are counted and named under the map rather than silently dropped, because a
shorter map than list otherwise reads as a bug.

## What is deliberately not in scope

- **Undecodable media.** A file whose digest matches but whose bytes will not play gets a silent,
  dead `<audio>`. Detecting that needs the browser to report a media error back, which is a
  different shape of change (JS interop) than these; noted for its own branch.

## How each fix is proved

`FieldSessionVariantCaptureTests` (opt-in, `BEN_VARIANT_MANIFEST`) already renders a matrix of
sessions and records what each one drew. It is extended here to assert rather than only describe:
a photo variant must produce an `img`, a room-without-fix variant must show the room badge, a
sound-only variant must produce a line. Each assertion fails against the code on `develop`, which
is the point — the matrix noticed all four of these and none of them broke a test.

The new page has `MyFieldSessionsTests`: a member sees their uploads and every row can be opened,
and a visitor sees none of them.

**Verified against a real render**, not only in tests: nine variants re-uploaded and walked, with
B showing "Room: Cellar" above "No position was recorded" (the fix), A and E rendering their
photos, the chart card reading "Magnetic field and sound over the session", and the new page
drawing three pins over Adams with "6 of your 9 sessions had no position to plot" beneath.

## Scale: loading pins as the map moves

Ben asked whether the sessions map could load more as it moves, staggered so the server is not
overloaded. The honest answer was that the first cut could not: the coordinate lived only inside
the session document, so every pin was a file read, the answer was hard-capped at 200, and the
shared map raised no movement event at all. Panning on top of that would have re-read every file.
So this was built in the order that makes each step cheaper than the last:

1. **The fix lives on the row.** `FieldSessionUpload.Latitude`/`Longitude`/`PositionResolved`
   (migration `20260902202426_AddFieldSessionFirstFix`, precision 18,10 like every other
   coordinate column — `CoordinatePrecisionTests` refuses less). Set at upload from the first
   positioned reading. Rows from before the column are resolved lazily, 25 per map request,
   opened once and never again; a document this server cannot read stays unresolved rather than
   being recorded as "no fix", because that would be a claim about a file nobody opened.
2. **The query is SQL.** `api/field-sessions/mine/map` takes optional `north/south/east/west`
   (all four or none), is capped at 500 pins, and says how many matched and how many old rows are
   still to be inspected — so a client can tell "500 of 500" from "500 of 4,000".
3. **The shared map reports movement.** `InvestigationsMap` raises `OnViewportChanged` from
   Telerik's `OnPanEnd`/`OnZoomEnd`, converting the `[nwLat, nwLng, seLat, seLng]` extent into a
   named `MapViewport` once so no caller has to remember which index is which. One event per
   gesture; debouncing beyond that is the caller's business.
4. **The page paces itself.** 350 ms after the last gesture, one bounded request; a newer gesture
   cancels the earlier timer and any request still in flight, so five pans in three seconds cost
   one request, not five, and a late answer for a viewport the map has left is never drawn.

Verified on the side database: the first map call resolved nine old rows (3 with a fix, 6 indoors
without, 1 unreadable left alone), Tennessee bounds returned 3, New York 0, half a set of bounds
was refused with a sentence.

**Production prerequisite:** the migration must be applied to the live database before this
branch is deployed — `dotnet ef database update` against it, which is Ben's call on a shared
database. The API tolerates the column being absent until then only in the sense that it will
fail loudly, not quietly.

## Playback map cadence

Ben's observation: during playback the map does not need to follow every reading. A session is one
visit to one building, walked room to room, so the map is loaded once and the person barely moves
on it. The tick loop was re-rendering the map component every 250 ms with whatever the latest fix
was, and a fix drifts a metre or two between readings even for somebody standing still — so the map
re-centred and rebuilt its marker constantly for nobody going anywhere.

Now the map follows a throttled point: at most once per second of playback, and only when the
person has actually moved at least five metres. The readout and the room badge still update every
tick, because they are cheap and the room is the finer answer anyway. A scrub is the person's own
gesture and moves the map at once.

