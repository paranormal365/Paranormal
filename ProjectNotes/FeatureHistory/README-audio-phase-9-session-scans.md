# Audio phase 9: scanning a committed .ben session for EVPs (10/27/2026)

Branch `feature/audio-phase-9-session-scans`, off develop after phase 8 (`283cddc8`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 9. Ben's decisions (planning, 10/26):

- **Who may scan:** the person who recorded the session; the group behind its investigation or
  calendar event; a hosted event's team (active members and confirmed staff) when it was sent to
  that event; and the verified venue of a public place it was published to. Never public visitors
  or share-link viewers.
- **Separate copies:** each scanner works on their own copy, or their group's, with their own
  markers. Nobody sees anybody else's. The `.ben` is never opened for writing.
- **A group's copy belongs to the group:** in the group's files, seen by its active members and the
  event's confirmed staff.

## Who may scan (`Services/Access/FieldSessionAnalysisAccess`)

The session read rules moved unchanged out of `FieldSessionUploadController` into
`Services/Access/FieldSessionAccess`; the controller's private methods now forward to it. The scan
rules are built from the same pieces.

| Person | Gets |
|---|---|
| The person who sent it | Their own copy |
| An attendee of its investigation, or an active member of that group | The group's copy |
| The team of a calendar event or hosted event it was sent to (members, guides, confirmed staff) | The group's copy |
| An active member of a verified venue for the public place it was published to, once its media is approved | The venue's copy |
| Anybody else who may read it (an open investigation) | Refused: "Only the person who recorded this session and the team it was sent to can scan it." |
| Anybody, on a published session | Refused: "Only the person who recorded this, their group, or the location's venue can scan it." |
| Somebody who may not read it | 404, as every other session route answers |

**Narrower than reading, on purpose.** An open investigation lets any signed-in person read what was
sent to it. Scanning makes copies and markers, so only somebody who was there, or belongs, may.
`Who_may_scan_which_session` has that row, and it fails when the read rule is used instead (checked).

`FileAudienceAccess.CanViewFileAsync` gained one clause: a group-owned file carrying a session's
provenance is visible to whoever may scan that session for that group. That is how confirmed staff
who are not members open the group's copy and its EVPs.

## The copy (`Services/FieldSessions/FieldSessionAnalysisCopies`)

- The bytes come from the bundle through `IBenBundleStore.OpenEntryAsync` (or an older session's
  cleaned recording) and are written to `users/{id}/session-copies/` or `orgs/{id}/session-copies/`.
  Nothing writes to the session's path; a test hashes the bundle before and after and checks every
  write.
- Verbatim, about 30 MB an hour, hashed as it is written and compared with the phone's digest. A
  mismatch is deleted and refused: "The copy did not match the recording on file…".
- A person's copy counts against their own allowance, asked first. A group's copy answers to the
  group's plan: a lapsed subscription adds nothing new. (Groups have no storage limit of their own
  today, so that is all there is to check.)
- One copy per recording per person, and one per group, held by filtered unique indexes. Two opens
  at once: the loser deletes its bytes and uses the winner's.
- The file is named `Session 10-12-2026 audio-001.m4a`, and carries `SourceFieldSessionUploadId` and
  `SourceClipPath`.

## The questions (`Services/Audio/Scanning/EvpQuestionWindows`)

The phone marks a question (`evp_question`, the question as its note) and the end of each wait
(`evp_wait_end`). A question's window runs to the end of waiting, or to the next question if that
comes first, or a minute. A candidate starting inside one gets the note `Q: Is anyone here with us
tonight?` (or "After the question at 4:10" when no text was written). The recording's place in the
session comes from the timeline's own clip placement (rules M4/M5), read through the shared
timeline cache; a recording it could not place gets no notes rather than wrong ones.

This is in `EvpScanService`, so it applies to every scan of a copy: the session-wide job and a scan
started from the editor.

## API (`FieldSessionUploadController.Analysis.cs`)

| | |
|---|---|
| `GET api/field-sessions/{id}/analysis` | May this person scan, whose copies, the sound recordings with what the chosen copy holds, a running scan, and whether this server can read .m4a. The one call every entrance makes |
| `POST api/field-sessions/{id}/clips/{fileId}/analysis-copy` | The copy, made if needed: 201 new, 200 existing; 409/413 with the reason |
| `POST api/field-sessions/{id}/scan-jobs` | Every sound recording (or those named), as one background job. Refused before any copy is made when this server cannot read them (400 `ScanRefusalRecord`). 409 while one runs; 429 at three per person |
| `GET api/field-sessions/{id}/evps` | EVPs saved from the session's recordings that the caller can open |

## The website

- **The session's page** (`/field-sessions/{id}`) has a "Scan for EVPs" card (`SessionEvpScanPanel`):
  whose copy (a chooser when there are several), "Scan all n recordings" with progress and Stop,
  each recording with what its copy holds and **Open in editor**, and "EVPs pulled from this
  session". It picks up a scan already running. Never on a share link. On the public archive page
  it shows only to somebody who may scan, which is the place's venue.
- **The group's session list** (the event's and the group's) has "Scan for EVPs" on each row with sound.
- **The editor's session address** (`/field-sessions/{s}/audio/{recording}?group=…|mine=true`)
  makes or finds the copy and opens it, with "From the session on 10/12/2026 · audio-001.m4a ·
  Hollow Creek Paranormal's copy" beside the name. Every tool but the destructive edits.
- **Saving an EVP** from a group's copy saves it to the group, carrying the session and recording
  it came from. From a person's copy it is theirs, as before.

## Changed from the plan

- **Migration M4, not M2.** Phase 6 left these tables out of M2 rather than guess their shape. The
  plan's later migrations move up one: 9b is M5, phase 11 is M6.
- **No foreign keys to the session.** `FieldSessionAnalysisCopies.FieldSessionUploadId` and
  `EvpScanJobs.FieldSessionUploadId` are plain indexed columns. A session can be deleted three ways
  (its owner, its investigation going, a purge), and a key from the copies would either block those
  or, cascading, add a second delete path from `UploadFiles` that SQL Server refuses. The copy is
  the scanner's file and stays. Only `CopyUploadFileId` has a key: deleting the copy removes its row.
  `OrphanedSessionPurgeCoverageTests` lists both as having no key, with the reason, and fails if
  either gains one.
- **The editor opens the copy straight away**, rather than playing from the bundle and copying on
  the first marker. One path, and the copy is what every tool works on anyway.
- **No `clips/by-path` endpoint**: nothing needed it.
- **"EVPs pulled from this session" lists only what you can open.** The plan had others' EVPs shown
  as "In {name}'s files"; that would announce what a separate copy is meant to keep to itself.
- **No per-row button on the place page's archive**: the venue reaches the scan from the archived
  session's own page, where the card appears for them and nobody else.

## Fixed on the way

- **A published or event-linked session sent as one .ben was charged to its owner's 2 GB in full.**
  The exemption only looked at per-recording file rows, and a bundle's recordings have none: they
  are inside the session's own file. `AccountStorageGuard.UsedBytesAsync` now exempts the session's
  document by the same rule. `A_bundled_session_counts_only_while_it_is_kept_private` fails on the
  old code (checked). This was the plan's step 8 follow-up; it is fixed instead of recorded.

## Tests

- `FieldSessionAnalysisAccessTests` (23): nine people against six sessions, sign-in, asking for a
  copy you may not have, and who sees a group's copy (members and confirmed staff; not unconfirmed
  staff, strangers, or the guest who recorded it).
- `SessionEvpScanTests` (10): the copy is the recording's own bytes and the bundle is never
  written; opening again finds the same copy; the guest's own copy is apart from the group's; a
  digest mismatch keeps nothing; the organizer's analysis, scan job (linked to the session, 409 on
  a second) and live job; a stranger gets 404 everywhere and nothing is copied; no ffmpeg refuses
  before copying; a candidate after a question carries it (placed by the timeline's audio_ref); the
  windows (wait end, next question, a minute); an unplaced recording gets no notes, a recording
  placed by its noted end does.
- `AccountStorageGuardTests`: the bundle row above.
- e2e `FieldSessionEvpScanTests` (categories FieldKit, AudioEditor): the guest is given a seat at
  the seeded seats event (held, then confirmed by the organizer; a fresh database has none) and
  sends the real device bundle to it; the organizer scans it from the session page and sees it
  finish, opens the group's copy in the editor, saves an EVP that belongs to the group and appears
  on the session page; the guest's recording is byte for byte the phone's, and the guest can't
  open the group's copy. Everything made, and the seat, is deleted.
- Results: 9,088 unit tests pass. e2e audio + mixer + Field Kit + LookAndTruths: 40 passed, 2 skipped
  (the capture and the walk, both opt-in); the new test on its own run: 1 of 1. Its first run skipped
  because the seeded guest held no place at the rooms weekend, which is why it books its own seat.

## Deploying

Run `ProjectNotes/sql/M4-FieldSessionAnalysisCopies.sql` on production **before** the API, after
M2 and M3 if they have not been run yet. Safe to run twice. `MediaTools:FfmpegPath` must be set for
.m4a scans (phase 4).
