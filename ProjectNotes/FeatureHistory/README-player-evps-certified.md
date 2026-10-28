# Session player, phase 5: EVPs and enhanced clips on the timeline, with a certificate (10/28/2026)

Branch `feature/player-evps-certified`. Phase 5 of the session-player plan. **Migration `FileProvenances`
(20261028173717) — Ben applies it on MonsterASP.** It only adds a table; nothing is dropped or changed.

## Ben's direction (10/28/2026)

Audio is recorded. When the .ben is submitted, its owner — or an organizer with a copy — runs the audio
through our analyzer, marks any EVPs and what they believe each says, and may enhance it. The .ben is never
edited. An enhanced recording is another clip attached to the .ben externally: it keeps the timecode,
appears in the timeline, is noted as enhanced, and keeps verification that it came from the original —
enhanced by us, not AI, not edited elsewhere: a certificate of authenticity.

## What changed

Data
- `FileProvenances` (one row per certified file, FK cascade on the file only): the session and the copy
  the chain starts from, whose copy it was (person or group), the recording's path, the phone's sealed
  SHA-256 for it, the copy's SHA-256, the steps as JSON (op, settings, who, when, result hash), this
  file's SHA-256, where it starts in the recording (null once a step moves time), and the site's seal.
- "What it says" needed no column: the editor's keep dialog already asks **What did you hear?** and saves
  it as the marker's label. The add-a-marker dialog now asks the same question.

Server
- `FileProvenanceService.RecordAsync`, called by the clip and edit endpoints as the file is saved.
  Enhanced = only copy, clip (incl. normalize), normalize, gain, fade. Cut, silence, reverse, speed,
  pitch and a volume curve are certified as altered. Cut, reverse and speed take it off the timeline.
- `ProvenanceSigner`: the API's own data-protection keys (the persisted ring), so nothing new to configure.
- `SessionSeal`: reads the phone's `seal.json` from inside the unchanged .ben. A scanning copy is now checked
  against it when made (`FieldSessionAnalysisCopies.EnsureAsync`), and every certificate says whether the
  copy matched it. Sessions sent before today are covered too: the seal was always in their .ben.
- `GET api/field-sessions/{id}/findings` (what this person can open) and the public twin
  `GET api/public/field-sessions/{id}/findings` (the sender's and the showing group's or venue's, certified
  clips only, once media is approved), `…/findings/{fileId}/media`, certificates at
  `GET/POST api/upload-files/{id}/certificate[/check]` and `…/findings/{fileId}/certificate[/check]`.
- `UploadFileController.Replace` refuses session recordings and .ben files, scanning copies and certified
  clips (409, said in a sentence). Save-as-version is unchanged: it makes a new file.
- Edits of a group's copy now belong to the group and carry the session link, as clips already did.

Site
- The player lays EVPs on the timeline as marks ("EVP “Get out” · Probable · by …") and on the chart in
  green; lists clips under their recording (Enhanced / Altered, summary, certificate link, player); and
  with **Play the enhanced clips in their place** the engine plays an enhanced clip instead of the
  original while the playhead is inside it.
- `/files/{id}/certificate` and `/places/{placeId}/sessions/{sessionId}/certificates/{fileId}`: the steps
  in sentences, the hashes, the no-AI statement, **Check it now**.

## A bug the real database caught

The first browser run on SQL Server showed every certificate as "changed". SQL Server hands `IssuedUtc` back
with no zone, and the sealed text wrote it with `"O"`, which then lost its `Z`. The in-memory database keeps
the zone, so the unit tests passed. The sealed text now writes the date the same however it comes back, and
`A_certificate_read_back_without_its_time_zone_still_checks` failed on the old text before the fix.

## Tests
- `Ben.Web.Tests/Services/FileProvenanceTests.cs` (7): the chain back to the seal and the placement; gain
  stays enhanced and in place, speed doesn't; a file not from a session has no certificate; a copy that
  doesn't match the seal says so; changed bytes and a changed certificate both fail the check; the public
  sees the sender's and the showing group's clips and not another member's; a date read back without its
  zone still checks.
- `Ben.Web.Playwright/Tests/FieldSessionFindingsTests.cs`: a copy, an EVP kept with what was heard, a
  normalized clip; the page shows both; the switch plays the clip in the original's place; the
  certificate checks "verified"; replacing the clip is refused.

## Later, noted not built
- The phone signing `seal.json` (a `signature` field is planned in `SessionSeal.swift`), so "copied
  unchanged" chains to a device key.
- The phone's own review showing web-made EVPs and enhanced clips.
- A resent .ben replaces the session's file rows; a copy's row id then points at a row that is gone. The
  copy and its certificates still read correctly (by path); nothing depends on that id today.
