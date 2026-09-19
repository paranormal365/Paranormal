# Audio editor phase 0 — the screen walk

Branch: `feature/audio-editor-phase-0-walk`, cut from `master`.
Plan: `ProjectNotes/AudioEditor-Audit-2026-09-06-Plan.md` (the whole audit and the eight phases).

## Why this phase exists

Three read-only sweeps of the audio editor produced a long list of findings, and the video editor
arc taught one lesson above every other: what the code says and what the page does are not the
same thing, and several of the worst defects in that arc were things no reading would have found.
So nothing is fixed until the editor has been used, as a person would use it, on a stack that
cannot touch the live database.

## What this phase does

- Teaches `scripts/run-e2e.sh` to hand the suite the seeded passwords it already knows where to
  find, so the only audio browser test the repo has stops being silently skipped.
- Adds `Ben.Web.Playwright/Tests/AudioEditorWalkTests.cs` — one test per use case, each recording
  a verdict and a screenshot rather than stopping at the first failure.
- Probes the server with a 90-minute recording to see where it actually gives out.
- Writes `ProjectNotes/AudioEditor-Audit-2026-09-06.md` from what was seen, and re-ranks the
  phases that follow.

## Rules

Throwaway database (`BEN_E2E_DB=IsHauntedDb_audio_walk`) and its own uploads directory; signed in
as Sarah, an ordinary organisation administrator, with the Viewer persona for the permission
checks; passwords reach the browser through Playwright's `RequiredSecret`, never through a tool
call. Stack torn down at the end.
