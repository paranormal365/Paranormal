# Phase D — Blazor Client Correctness

Branch: `feature/webapp-phase-d-client-correctness`

## Why

Phase D closes out the fourth and final tier of this session's WebApi/WebApp audit — client-side
silent-failure UX patterns in `Ben.Web.Library`/`Ben.Web.WebApp`. Unlike Phases A-C, nothing here is
a security hole: the bugs are all "the user did something, it silently failed, and nothing told
them" — a missing-AuthReady race that shows a false "not signed in" screen, mutations whose failure
path is a no-op, double-submit windows, and one latent same-route navigation gap.

## What shipped

**Missing `AuthReady` await (2 instances).** `MyVideosPage.razor` checked `IsAuthenticated` directly
in both its top-level markup gate and `OnInitializedAsync`, racing ahead of the async auth-state
resolution — a hard nav could show "you must be signed in" to a genuinely signed-in user, and even
once past that render, the project list silently never loaded. Fixed by adding a `_authReady` gate
(loading spinner until `AuthReady` resolves, mirroring the same-file pattern already used in
`CaseVideoEditorPage.razor`) and awaiting `AuthReady` in `OnInitializedAsync` behind the established
`RendererInfo.IsInteractive` guard. `ClientRequestWizard.razor`'s `OnAfterRenderAsync` checked
`IsAuthenticated` without ever awaiting `AuthReady` in that method — relying on a separate, earlier
`OnInitializedAsync` await that doesn't always run first — so a hard nav could silently skip loading
an existing draft for good, since `firstRender` only fires once. Fixed by awaiting `AuthReady`
directly in `OnAfterRenderAsync` before the check.

**Silent-failure mutations (9 components).** `WebApiClient`'s helpers return `null`/`false` on any
non-2xx rather than throwing, so "it didn't throw" was never proof of success — several call sites
treated a null/false result as if nothing happened, with the surrounding UI (dialog, form) closing
or navigating away regardless:
- `ClientRequestWizard.razor` — "Save & Exit" navigated away unconditionally even when the save
  failed; `SaveDraftSilent` now returns success/failure, checked by both `SaveDraft` and
  `SubmitRequest` (submitting on top of a silently-failed draft update would have transitioned a
  stale server-side draft instead of what the user actually typed).
- `OrganizationFiles.razor` — publish/edit/delete dialogs all closed silently on failure with zero
  feedback; added error toasts to all three.
- `OrganizationMembershipRequests.razor` — accept/deny silently no-op'd on failure; added error
  toasts.
- `MyVideosPage.razor` / `CaseVideoEditorPage.razor` (twins) — delete-project silently cleared
  `_deleteTarget` with no notification on failure; added error notifications to both copies.
- `EvidenceVoteWidget.razor` / `CaseVoteWidget.razor` (twins) — three empty `catch { }` blocks each,
  and neither component had an error field at all; added `_error` (initial load) and `_actionError`
  (cast/remove vote) fields, both rendered inline.
- `WsRegionExplorer.razor` — reload-notes, save-edit, and delete-note all silently ignored failure;
  reused the existing (already-wired-up) `_noteError` field for all three instead of leaving them
  silent.

**Double-submit gap.** `OrganizationFiles.razor`'s publish/edit/delete dialog buttons had no busy
guard, unlike the file's own upload/copy buttons which already disable + relabel during their
request — added matching `_publishing`/`_savingEdit`/`_deleting` flags to all three.

**Validation.** `MyCaseDetail.razor`'s co-client invite gated only on non-empty; added an
email-format check (`System.Net.Mail.MailAddress` round-trip) as UX polish — server-side validation
was already correct.

**Latent same-route navigation gap.** `CaseDetail.razor` only loaded `_case` in
`OnAfterRenderAsync(firstRender)` and read `InitialTab` in `OnInitialized` — both fire once per
component instance, so a future same-route link that changes `CaseId` in place (Blazor reuses the
instance) would have silently kept showing the previous case. Added `OnParametersSetAsync` tracking
the last-loaded `CaseId`, reloading when it changes, without touching the existing first-render path.

## Verification

Full suite green (1262 total, no change — none of these fixes touch server code covered by unit
tests). Live interactive verification (the AuthReady hard-nav fix in particular needs a real signed-in
session to observe) hit this sandbox's known Blazor Server SignalR/circuit-negotiation limitation —
the page loads and prerenders but the interactive circuit can't connect, so login and true hard-nav
behavior can't be exercised here. Verified instead via: (1) matching the exact fix shape already
proven correct elsewhere in this codebase (`MyCases.razor`'s `OnAfterRenderAsync` + `AuthReady`
pattern, `CaseVideoEditorPage.razor`'s loading-gate pattern), and (2) clean compilation with no new
warnings.
