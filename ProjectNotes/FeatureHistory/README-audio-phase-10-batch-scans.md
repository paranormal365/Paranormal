# Audio phase 10: batch scans and the Scans page (10/27/2026)

Branch `feature/audio-phase-10-batch-scans`, off develop after phase 9b (`39381ab2`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 10. No migration.

## API (`EvpScanJobController`)

| | |
|---|---|
| `POST api/evp-scans {fileIds, sensitivity, options?}` | One background job for every recording that can be scanned, in the order given; each recording comes back accepted or with its reason ("Not a recording.", "Not found, or not yours to scan.", "Already being scanned.", or this server can't read it). 202 with the job; 200 with no job when none could be taken. Up to 100 per batch; 429 at three unfinished scans |
| `GET api/evp-scans[?status=live]` | The caller's scans, newest first (50 at most): the job, which settings (Low/Medium/High/Custom), the session it came from, and each recording by name |

Cancel is the existing `DELETE api/scan-jobs/{id}`.

## The website

- **Media Library:** **Scan several for EVP…** turns on choosing; only recordings can be chosen
  (others are dimmed), in the grid and the list. **Scan for EVP** opens `BatchScanDialog`: the same
  settings editor as Fine-tune (starting from the person's default preset), then the outcome, with
  each refused recording and its reason, and a link to the Scans page.
- **`/audio/scans`** (`AudioScansPage`), linked as "EVP scans" from the Media Library: one card per
  scan with its settings, status and progress bar, Stop while it runs, and a row per recording with
  its own progress and what it found, and **Open in editor** (Back returns here). Read every 3 s
  while anything is waiting or running, and not at all otherwise.
- **Editor:** **Rescan with these settings** at the foot of Fine-tune.

## Changed from the plan

- **A batch is one job, not one job per recording.** The limit is three unfinished scans per
  person, so ten recordings as ten jobs would mostly be refused. One job with a part per recording
  keeps the limit meaningful, and each recording still gets its own answer and its own row.
- **Rescan sits in Fine-tune**, beside the settings it uses, rather than relabeling the Scan button
  (twelve tests find that button by name, and its meaning hasn't changed).
- **The case Files tab** only uses the library as a picker, so it has no scan bar.

## Tests

- `EvpScanJobTests` (+5): a batch is one job in the order asked, and runs to Done; each refusal says
  why (photo, someone else's, already running, unknown); nothing scannable starts nothing; the
  batch limits (none, over 100, three live); the list shows only mine, by name, Custom vs Medium,
  and `live` filters.
- e2e `Several_recordings_scan_as_a_batch_and_the_scans_page_follows_it`: two uploads chosen in the
  library, scanned from the dialog, followed on /audio/scans to "candidates to review" with three
  per recording, and one opened in the editor with Back to the Scans page.
- Results: 9,104 unit tests pass; e2e audio + mixer + Field Kit + LookAndTruths 43 passed, 1 opt-in capture skipped.
