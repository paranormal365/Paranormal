# Audit Phase 3 — Production Readiness

Branch: `feature/audit-phase-3-production-readiness` (from `develop`, phases 1–2 merged)
Source doc: [`ProjectNotes/Code-Audit-2026-08-16.md`](ProjectNotes/Code-Audit-2026-08-16.md)

Findings covered: **B5, B6, B7**. (**B4**, CORS, already shipped — it was pulled forward because
`Ben.Wasm.Video` needed it to talk to the API at all.)

> The site is still in development and deployed nowhere, so none of this is live exposure. It is
> the batch that has to be true *before* a first deployment, which is why it is grouped.

## 1. Audit pipeline hardening (B5)

133 call sites do `_ = TryAuditAsync(...)`, and the pattern has three compounding weaknesses:

| Problem | Consequence |
|---|---|
| Fire-and-forget **and** handed the request's `CancellationToken` | Client disconnects right after a mutation commits → the audit write is cancelled and swallowed |
| `TryAuditAsync` swallows every exception with no logging | A systemically broken audit table (bad migration, full disk) is invisible |
| `AdminEntityControllerBase.Delete` audits *before* `SaveChangesAsync` | A failed delete still writes a "deleted" audit row |

**Plan:** pass `CancellationToken.None` to audit tasks, log in the catch (the metadata-extraction
path already learned this lesson), and move the Delete audit after the save. Decide whether audit
rows are compliance-relevant enough to await — they are a single INSERT, so the latency argument
for fire-and-forget is weaker than it looks.

## 2. Logging and log hygiene (B6)

- Serilog pins `Microsoft.AspNetCore.Authentication`, `Authorization`, and `IdentityModel` to
  **Debug in code**, so configuration cannot lower it. Auth debug logging can capture token and
  claims detail.
- The rolling file sink writes **inside the working tree** (`.vscode/webapi-.log`), a repo-relative
  path that follows the code to any deployment.
- `.vscode/webapp.log` is **committed to the repo**.
- Committed `appsettings.json` defaults `Logging:LogLevel:Default` to `Debug`.

**Plan:** move the level overrides into `appsettings.Development.json`, point file sinks at a real
log directory (or drop the file sink — Serilog already writes to SQL), `git rm --cached` the
committed log, gitignore `*.log`, and default committed config to `Information`.

## 3. Continuous integration (B7)

2,219 tests at audit time — now **4,133** with the vendored video suites — run only when someone
remembers. With the repo self-contained after vendoring, a workflow no longer needs a second
checkout.

**Plan:** GitHub Actions on push to `develop`/`master` and on PRs: restore, build, test. Telerik
NuGet feed credentials via repo secrets. Note the solution now includes a Blazor WASM project and
the sidecar, so CI wants the same `dotnet build Ben.slnx` the local flow uses — the sidecar's
ffmpeg binaries are gitignored and fetched by script, so tests must not depend on them being
present (`Ben.Video.Sidecar.FakeFfmpeg` exists precisely for that).

## Also in scope if cheap

**D3** — committed `appsettings.json` still names the **retired** Entra app registration
(`e75f71ef…`, superseded 2026-07-18); anyone running without the Development override wires auth
to a dead app.

## Verification plan

Build clean, all 4,133 tests green, and for B5 specifically a test that a cancelled request still
produces its audit row — the current behaviour would pass a naive "audit row exists" test, so the
test has to cancel.

---
*Part of the audit remediation tracked in `ProjectNotes/Code-Audit-2026-08-16.md`.*
