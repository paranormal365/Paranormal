# Audit Phase 5 — Background Pass

Branch: `feature/audit-phase-5-background-pass` (from `develop`, phases 1–4 and C3 merged)
Source doc: [`ProjectNotes/Code-Audit-2026-08-16.md`](ProjectNotes/Code-Audit-2026-08-16.md)

Findings covered: **D2**, **A5**, and a first slice of **C5**. **C2**, **C4**, **C7** and **E2** are
deliberately *not* closed here — see below for why, and what each actually needs.

This is the phase the audit itself described as "incremental/ongoing", so the honest job is to do
the parts that finish cleanly and leave the rest as accurate, actionable notes rather than
half-finished work.

## 1. Dependency updates (D2)

Split by risk, and only the safe half is taken:

**Taken — patch/minor within .NET 10.0.x.** The framework family moves 10.0.9/10.0.10 → 10.0.11
(EF Core, Identity, JwtBearer, OpenIdConnect, Components.WebAssembly, Extensions.*), plus
`Telerik.Documents.Fixed`, NUnit, NUnit.Analyzers, Playwright and Test.Sdk within their majors.
CI now exists to catch what these break, which is the reason to do them at all.

**Deliberately not taken — every major.** Each has a specific reason, not just caution:

| Package | Jump | Why it waits |
|---|---|---|
| `NAudio` | 2.2.1 → 3.0.0 | EVP detection, mixing, clipping and the macOS MP3/ACM decoder fix all sit on it. Needs an audio regression pass with real files, not a version bump. |
| `Telerik.UI.for.Blazor` | 14.1.0 → 15.0.0 | Pinned identically in the vendored video editor; both move together or neither does. A major here touches every screen. |
| `Swashbuckle.AspNetCore` | 7.2.0 → 10.2.3 | Three majors, and the schema-id and filter behaviour just changed in phases 3–4. Worth pairing with a look at .NET 10's built-in OpenAPI instead. |
| `Markdig` | 0.37.0 → 1.3.2 | Renders help documentation; a rendering change is user-visible content. |
| `xunit.runner.visualstudio` | 3.1.x → 4.0.0, `coverlet` 6 → 10, `NUnit3TestAdapter` 5 → 6, `Test.Sdk` 17 → 18 | Test *infrastructure*. Breaking these makes every other result untrustworthy, so they move on their own, deliberately. |

Also noted: `Ben.Web.Playwright` pins `Microsoft.NET.Test.Sdk` a whole major behind the other test
projects (17.14 vs 18.7) — drift worth removing when the test infrastructure is upgraded together.

## 2. HomeSvg (A5)

`Ben.Web.WebApp/Components/Pages/HomeSvg.razor` is **9,596 lines** — a raw Adobe Illustrator export
with an embedded base64 PNG, compiled into the render tree as a component. It is 22% of all razor
lines in the solution and nothing in it is dynamic.

Deferred out of phase 1 because it changes what the app serves; taken here with live verification.

## 3. String column bounds (C5) — first slice only

257 string properties, 82 `HasMaxLength` configurations. The rest are `nvarchar(max)`: unindexable,
inflating memory grants, and accepting 2 GB of input on fields that should be short.

Doing all of it in one migration would be a large, hard-to-review schema change. This phase bounds
the most-queried tables only, as one migration, and leaves the rest as a known, sized job.

## Not closed here, and why

**C2 — oversized files.** The audit's own guidance is "no big-bang rewrite — split opportunistically
when next touching each file", so closing it as a task would contradict the finding. Current state,
for visibility: `MyCaseController` 1,312 lines / 28 endpoints, `OrgInvestigationsController` 672,
`MyContactInfoController` 656, `CaseController` 650; `AudioFilePreview.razor` 2,091,
`MyCaseDetail.razor` 1,346, `OrgCmsEditor.razor` 1,105.

**C4 — `IEntityTypeConfiguration`.** `OnModelCreating` is 1,685 lines and there are zero
configuration classes. The *content* is good (332 explicit delete behaviours, all 15 decimals with
precision); it is the monolith shape that hurts. But every line of it is live schema mapping, and a
mechanical move is exactly where a silent behaviour change hides. It deserves its own branch with a
model-snapshot diff proving the built model is byte-identical — the same "prove nothing moved"
discipline C3 used.

**C7 — help documentation.** 18 of 62 `@page` components reference `HelpLink`. This is content
work, not code, and needs a decision per page about which are genuinely user-facing (some are
redirects and admin shells that need nothing). The standing rule — a feature isn't done until the
in-app help covers it — makes this worth a dedicated pass rather than a rushed one.

**E2 — SQL-backed tests.** All 4,146 tests use EF InMemory, which ignores unique indexes (including
the ones added to fix the Phase-C races), cascade rules, transactions and string truncation. The
fix is a handful of relational-behaviour tests against a real SQL Server — which the CI added in
phase 3 could host as a service container. Worth doing; not a five-minute job.

## Verification

Clean rebuild with the compiler server shut down (phases 1 and 4 both had incremental builds report
stale results), all tests green, and for A5 a live check that the home page still renders.

---
*Part of the audit remediation tracked in `ProjectNotes/Code-Audit-2026-08-16.md`.*
