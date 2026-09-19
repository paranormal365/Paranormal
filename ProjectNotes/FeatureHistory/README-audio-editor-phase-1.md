# Audio editor phase 1 — the server stops accepting the impossible

Branch: `feature/audio-editor-phase-1-server-safety`, cut from `master` after the memory fix merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (server 2–17, plus F, Q, R).

## What already shipped from this phase

Finding 1 and 1b — the memory figures — were fixed first, out of order, because the long-file probe
measured 8.6 GB for one Normalize and nothing was released between requests. `AudioSourceReader`
now reads through one buffer, the detector reads mono at 16 kHz, and edits have a stated 30-minute
ceiling refused as a 400. That is merged; this branch is the rest.

## What this phase does

Two of these are security fixes and were always going to ship in phase 1 whatever the walk found:

- **Privacy laundering (6).** Anyone who can *see* a private recording can derive a copy from it,
  and the derived copy's `IsPublic` came straight from the request. A viewer could therefore
  publish somebody else's private audio by "editing" it. The source's own visibility is a ceiling
  now, on both the edit and the clip endpoint.
- **Audio config is not the viewer's to change (9).** PUT and DELETE asked only whether the caller
  could *view* the file, so any viewer could overwrite or delete the owner's saved player settings.
  They ask `CanManageFileAsync` now; GET, which had no per-file check at all, asks `CanViewFileAsync`.

The rest is the server refusing what it cannot do, in a sentence, instead of allocating or throwing:

- Numeric bounds that were missing entirely: `SpeedRatio` had no lower bound (0.001 allocates a
  thousand times the samples and then phase-vocodes them), `GainDb` and the fades accepted NaN
  (which writes a silent file and answers 201), mixer offsets were unbounded (2–3, 13).
- Corrupt or mislabelled audio was a 500 on edit and clip and a 400 on scan; it is a 400
  everywhere now (5).
- The row is validated before the bytes are written, and the file is removed if the insert fails,
  so a rejected `Label` no longer leaves an orphan on disk (7).
- Marker Create and Update accepted inverted spans, negative starts and over-long labels that
  Review and Candidates already rejected — one shared check now (8).
- A clip wholly past the end of the recording persisted a 44-byte WAV with a 201 and a false
  duration (10).
- Derived files never carried `DurationSeconds`, and an edited file showed "0:00–0:00" in Saved
  Clips because the edit endpoint never set `RegionStart`/`RegionEnd` (11, F).
- The mixer dereferenced `StoragePath!` with no `FileData` fallback, never checked the user-id
  claim before writing bytes, and recorded no `ParentFileId` (4, 14, 15).
- `GET /clips` had no visibility check (16).
- The heavy synchronous endpoints — edit, clip, scan, mix — get a rate-limit policy of their own
  (17).
- `AudioEditRequest.Operation` accepts its own name as well as its number (R). The audit's note
  that "every other enum accepts its name" was wrong: this API binds every enum as an integer on
  purpose. Accepting both here is additive and breaks no caller.

Finding Q (the 128 MB multipart ceiling on the classic upload endpoint) is not audio and is not in
this branch; it is recorded in the audit for its own fix.

## Verification

`dotnet build Ben.slnx` clean; `Ben.Web.Tests` green. Every new test run once against the un-fixed
code and seen to fail. Then the long-file probe again on the isolated stack, and the walk's edit
cases by hand as Sarah.
