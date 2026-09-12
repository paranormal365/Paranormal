# Field Kit upload hardening — the door asks whether a Field Kit could have made this

Branch: `feature/fieldkit-upload-hardening`, cut from `develop` at `80270f0`.

Ben's question, 2026-09-03: *can we validate we only accept field kit results from actual field
kits and not from someone trying to inject malware or harmful data?* Two halves: proving the app
sent it (Apple App Attest — its own branch, after 1.0 clears review, because it touches the app),
and never trusting what it sent anyway. This branch is the second half.

## What already held

A hostile upload could not run anything: JSON parsed, never executed; files stored under
server-generated names; path traversal refused; digests checked; images re-encoded; nosniff on
everything the API serves; session files served as downloads; uploads behind the rate limiter.
What it *could* do was lie — invented readings, or a file whose bytes were not what its name said.

## What the door now refuses, each with a sentence

**Documents** (`FieldSessionDocumentGuard`, run after the shape check and before anything is
stored): a reading stamped outside the session's own window (five minutes' slack for clock
drift), readings that run backwards, a magnetic field above 10,000 µT or below zero (a phone's
magnetometer saturates around 2,000), a sound level above 0 dBFS, a latitude past 90, a longitude
past 180, a negative accuracy, a heading past 360, more than 100,000 readings. The refusal names
the first offending reading: *"reading 2 is stamped 2026-08-24T02:06:07Z, outside the session's
own window."*

**Files** (`FieldSessionFileGuard`): only the kinds the Field Kit makes — m4a, mp3, wav, mov,
mp4, jpg, png, heic — with the declared content type agreeing with the name, and **the first
bytes agreeing with both**: an ISO `ftyp` box for m4a/mp4/mov/heic, `RIFF…WAVE`, an ID3 tag or
MPEG frame sync, JPEG and PNG signatures. Two kilobytes of zeros named audio-001.m4a is refused
as *"not a M4A file"*; notes.html is refused as a kind a session cannot carry. Caps: 8 GB a file,
500 files a session.

## What this exposed

- The **simulator's fake-sensor recording** was two kilobytes of zeros — the exact thing the byte
  check exists to refuse. `FakeSensors.swift` now writes a real `ftyp` header followed by nothing:
  accepted at the door, still undecodable, so the web player's "won't play" badge covers it. The
  real-app probe passes against the guarded API.
- The **sessions list reported every recording as 0 bytes** while the detail said 2,048: the list
  loaded file rows without the upload rows their sizes live on. Fixed in all three list queries,
  with a test.

## How it is proved

- `FieldSessionGuardTests` (20): a session the app writes passes; each rule refuses with its
  sentence; every container the app produces passes the byte check; zeros, HTML, an EXE and a
  name/type disagreement are refused; limits are stated.
- `FieldSessionUploadControllerTests`: the list reports real sizes; the serving tests now upload
  bytes with a real header, as the app does.
- `FieldSessionHardeningTests` (browser, +2): the zero-filled m4a and the HTML file are refused
  through the real endpoint; a document with a reading a day early is refused naming the reading.
- Full unit suite 3,908; the session browser tests green on the side database; the real app's
  upload probe green against the guarded API.
