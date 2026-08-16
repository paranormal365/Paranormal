# Vendored Ben.Video Source

The `Ben.Video.*` projects in this repo are a **vendored copy** of the separate Ben.Video
repository (`github.com/VandyBen/Ben.Video`, local checkout `~/Source/Github-BenVideo`), taken so
the WASM editor work can proceed here without touching that repo.

| Fact | Value |
|---|---|
| Vendored from | `develop` @ `abd3bfda3e3fa72313afee0c7d098c6631da0c1e` (2026-08-14) |
| Why develop, not main | develop was strictly ahead by 3 commits, one of which fixes the editor crashing on every load ("comment inside a component tag"). Vendoring main would have vendored the crash. |
| Method | `git archive origin/develop` per project — tracked content only, no build artifacts |
| Projects taken | Core, Editor, RenderService, Sidecar, Sidecar.FakeFfmpeg, Ben.Video.Tests, Ben.Video.Sidecar.Tests |
| Deliberately not taken | `Ben.Video.Playground` (dropped per direction), `Ben.Video.App` (1,983 of its 1,999 tracked files are committed Kendo-theme `node_modules` — same anti-pattern as audit finding A4; the new `Ben.Wasm.Video` host replaces it), `docs/`, `scripts/`, `logs/` |

**From this point, the copies diverge on purpose.** Fixes for the WASM-hosted editor happen here;
the Ben.Video repo remains as-is with its own history. If a change here needs to flow back, it is
a deliberate port, not a merge — there is no git relationship between the copies.

This supersedes the previous arrangement where `Ben.slnx` referenced the sibling checkout at
`../Github-BenVideo/` — the solution now builds self-contained from this repo alone.
