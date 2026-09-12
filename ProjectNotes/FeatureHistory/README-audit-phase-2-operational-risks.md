# Audit Phase 2 — Operational Risks

Branch: `feature/audit-phase-2-operational-risks` (stacked on `feature/audit-phase-1-dead-code-removal`)
Source doc: [`ProjectNotes/Code-Audit-2026-08-16.md`](ProjectNotes/Code-Audit-2026-08-16.md)

Findings covered: **B1, B2, B3**.

> **Context:** the site is still in development and is not deployed anywhere, so none of this was
> live exposure. It is pre-launch hardening. The one item with a cost that is real *today* is the
> geocoding limit — geocod.io bills per lookup and the dev app calls the real service.

## 1. Uploads stream instead of buffering (B1)

Every upload path did `CopyToAsync(memoryStream)` then `ToArray()` — two full copies of the file
resident for the whole request, under `[DisableRequestSizeLimit]`. `IFormFile` and
`IFileStorageService` are both stream-based, so the buffer bought nothing.

Seven call sites across five controllers now stream straight through, via
`FormFileStorageExtensions.WriteFormFileAsync`. `OrganizationFileController` already did this
correctly and was left alone.

Three cases needed more than a swap:

| Case | Why | What it does now |
|---|---|---|
| Metadata extraction | Held the bytes until the background task finished | Reads the stored file; `FileMetadataExtractorService` gained a `Stream` overload |
| `Replace` | Fans the same content to every case copy | Writes the source once, then copies file-to-file |
| SVG | Sanitising parses and rewrites the whole document | Still buffered — deliberately, and commented where it happens |

`AdminVideoAssetController` also hashed entire videos into a `byte[]`; it now hashes incrementally.

## 2. Rate limiting (B2)

There was none. Three policies, all per caller (user id when signed in, IP otherwise):

| Policy | Default | Guards |
|---|---|---|
| `geocoding` | 20/min | geocod.io is **billed per lookup** — applied at class level, since authenticated callers spend the same money |
| `auth` | 20/min | `/login` is a password oracle; `/register` creates accounts |
| global | 600/min | ceiling so one client cannot saturate the server |

Rejections return `429` with `Retry-After`. Queueing is off — holding rejected traffic open would
spend server resources on exactly the requests being refused.

### Where the numbers come from

All three are editable by a SuperAdmin at **`/admin/site-settings`**, falling back to
`RateLimits:*` in configuration and then to constants in code.

Two things made that non-trivial, both documented at the code:

- **No database round-trip per request.** The partition factory runs on every request and is
  synchronous; `SiteSettingsService` queries with no caching. `RateLimitSettingsProvider` serves an
  in-memory snapshot and refreshes it in the background, so a read never blocks. Cost: a change
  takes up to 30s to apply.
- **The limit is part of the partition key.** The factory only runs for keys with no limiter yet, so
  without this an edited value would never reach callers already inside a window.

A value that is unset, unparseable, or not positive keeps the previous one — an admin typing
"twenty" into the box must not be able to lock everyone out.

## 3. Registration requires a confirmed address (B3)

`/register` is anonymous, so anyone could create a working account at an address they do not own.
`SignIn.RequireConfirmedAccount` now gates sign-in.

This only works together with a real `IEmailSender<AppUser>`. Identity registers a **silent no-op**
when none is supplied — with confirmation required, that would create every account and then lock it
out with no error anywhere. `IdentityEmailSender` bridges to the existing `IEmailService`.

Per direction during the work, it **always attempts the send**, including while SMTP is unconfigured
(the current state). A failed send is logged with the link rather than raised, so the flow stays
completable and Identity does not report a failure for an account it already created.

Existing sign-in is unaffected: Entra sign-up, invite acceptance, and both seeders already set
`EmailConfirmed`.

## Verification

- Build clean; tests **2,148 → 2,163**, 0 failures.
- **New tests were checked against deliberately broken implementations first.** The upload tests use
  a payload larger than the 81,920-byte copy buffer, so a truncating write fails them (confirmed);
  the limit tests fail if the positive-value guard is dropped (confirmed, exactly the `0` and `-5`
  cases).
- Live, against the running stack: geocoding returns 20×`200` then `429` with `Retry-After: 60`;
  `/login` returns 20×`401` then `429`, confirming the two policies partition independently; normal
  traffic and the public org page unaffected.

### Not verified live

Anything behind authentication — a real file upload through the UI, and the
register → confirm → sign-in sequence. Both need a login, and I don't enter passwords. The upload
path is covered by the byte-fidelity tests above; the registration path is worth one manual pass
before this merges.

---
*Part of the audit remediation tracked in `ProjectNotes/Code-Audit-2026-08-16.md`. Per finding D5,
this README moves to `ProjectNotes/` once the branch merges.*
