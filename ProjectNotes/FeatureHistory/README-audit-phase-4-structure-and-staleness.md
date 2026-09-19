# Audit Phase 4 — Structure & Staleness

Branch: `feature/audit-phase-4-structure-and-staleness` (from `develop`, phases 1–3 merged)
Source doc: [`ProjectNotes/Code-Audit-2026-08-16.md`](ProjectNotes/Code-Audit-2026-08-16.md)

Findings covered: **C1, C6, D4, D5, D6**. (**D3** shipped in phase 3.)

Nothing here changes what the app does. It changes where things live, what the API documents, and
what the app depends on at runtime.

## 1. The Swagger filter hides real properties (D6)

`CircularReferenceSchemaFilter` strips any property whose name `EndsWith("s")`, intending to drop
collections. It also drops `Status`, `Address`, `Notes`, `Radius` — scalars whose names happen to
end in the letter.

This has been invisible because the document did not generate at all (fixed in phase 3, B7). Now
that `/swagger/v1/swagger.json` returns 377 paths again, the docs are being read, and they are
lying about the shape of the API.

**Plan:** filter on actual navigation/collection types from `context.Type` rather than on the
spelling of the name.

## 2. wavesurfer.js comes from a CDN (D4)

Two hosts pull it at runtime from `unpkg.com` on a floating `@7` tag:

- `Ben.Web.WebApp/Components/App.razor`
- `Ben.Wasm.Video/wwwroot/index.html`

A core feature — audio waveforms in the editor — therefore depends on a third party being up, works
differently offline, and takes whatever unpkg decides `@7` means today.

Worth noting the repo is already half-way there and inconsistent with itself: `WaveSurferPlayer`
imports **self-hosted ESM** bundles from `/js/wavesurfer/`, while the video editor's
`VideoTimeline`/`AudioWaveform` need the **UMD global** `window.WaveSurfer`, which is what the CDN
tag supplies.

**Plan:** self-host a pinned UMD build and serve it to both hosts. Related, deliberately out of
scope: **A4**, the 174 MB vendored wavesurfer *fork* under `wwwroot/ts/` — that is a separate
decision about where build sources live.

## 3. Silent catches (C6)

Seven bare `catch { }` blocks in the web layer, the same class Phase D hunted:
`CmsSectionEditor`, `CaseList` (pending-count badge), `OrgCmsPageEdit` ×2, `OrgCmsEditor`,
`OrgMessages`, and `UserMenu`'s avatar load.

**Plan:** surface to the user or log with context. Where quiet genuinely is right — the badge count
is a fair candidate — say so in a comment, so the next reader knows it was a decision. `UserMenu`
matters because a failing photo fetch is currently indistinguishable from having no photo.

## 4. Root clutter (D5)

- Four `README-*.md` describing **merged** feature branches → move into `ProjectNotes/`.
- `PHASE.md` describes `feature/self-service-contact-info`, still unmerged, yet sits on develop.
- `Ben.sln.DotSettings.user` — user-specific IDE settings, tracked.
- `scripts/ensure-docker-running.sh` — Docker is gone; the dev database is a dedicated SQL Server.

The phase READMEs for this audit follow the same rule and move once their branch merges.

## 5. Non-controller classes under Controllers/ (C1)

Twelve classes in `Controllers/Entities/` derive from nothing and are the real domain layer —
including the access-control family that decides what every caller may see:

| Group | Classes |
|---|---|
| Audio/DSP | `AudioSourceReader`, `AudioMixer`, `AudioEditor`, `SmbPitchShifter`, `EvpDetector` |
| Access control | `FileAudienceAccess` (14 files), `CaseOrgAccess` (7), `InvestigationAccess`, `InvestigationVisibilityFilter`, `PrivatePhotoConsent` |
| Places | `PlaceMatcher`, `InvestigationPlacement` |

**Plan:** move to `Services/Audio/`, `Services/Access/`, `Services/Places/`. Namespace-only; the
compiler proves completeness.

## Explicitly deferred

**C3** — splitting the 384-method `IBenAdminClient`. The audit puts it in this phase, but it is a
large mechanical change across the whole Blazor layer and reads better as its own branch than
bundled with unrelated cleanups.

## Verification

Build clean and all 4,137 tests green throughout. For D6, assert the generated document actually
contains a scalar that used to be stripped — a test that only checks "the document generates" would
pass against the bug.

---
*Part of the audit remediation tracked in `ProjectNotes/Code-Audit-2026-08-16.md`.*
