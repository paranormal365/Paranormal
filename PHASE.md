# Viewer-Correct Date/Time Display

Branch: `feature/webapp-viewer-timezone`

## Why

All dates are stored as UTC. Many Razor components display them via `.ToLocalTime()` — but this is
server-side Blazor (Interactive Server render mode), so that call converts to the **server's** OS
timezone, not the viewing browser's. Anyone not in the server's timezone sees wrong times with no
indication anything is off. An Explore pass confirmed 23 files / 51 call sites, plus two related
bugs from the same root cause: `DateTime.Now` used for scheduling defaults/comparisons (server-clock
biased), and several Telerik grid columns bound directly to raw UTC with zero conversion attempted
at all.

The fix: detect each viewer's actual browser timezone via JS interop once per circuit, and convert
every displayed/edited date consistently, per the standing principle that date/time should always
be shown in the format and timezone the viewer actually expects.

## Approach

- **Resolve once per circuit, reusing the existing `AuthReady` bootstrap** in `MainLayout.razor`
  rather than inventing a second readiness gate — a new `window.benGetBrowserTimeZone()` JS function
  (`Intl.DateTimeFormat().resolvedOptions().timeZone`) is called during the same first-render
  sequence that already signals `AuthReady`, storing the result on `IBenUserState.BrowserTimeZone`
  (default `TimeZoneInfo.Utc` until resolved or on failure).
- **One reusable extension helper**, `DateTimeViewerExtensions` (`ToViewerLocalTime`,
  `ToUtcFromViewerLocal`, `NowInViewerTimeZone`), used as a mechanical drop-in replacement for
  `.ToLocalTime()` at display sites.
- **`DateTime.Now` misuse** splits into two different fixes depending on whether the value round-trips
  through an editable field (needs the full local↔UTC round trip) or is a pure comparison against a
  stored UTC value (needs `DateTime.UtcNow`, not a viewer-local conversion).
- **Raw-UTC grid columns** (`GridColumn DisplayFormat=...`) can't have a conversion injected into a
  bound format string, so those become `<Template>` blocks, matching the pattern already used for
  `OrganizationList.razor`'s "Applications" column.

Explicitly out of scope: `OrganizationView.razor`'s intentional "(server)"-labeled Central-time audit
timestamps, and `AdminUserDetail.razor`'s editable raw-UTC pickers (SuperAdmin-only raw-data tool —
left as a documented follow-up rather than risking a round-trip bug there).

## Verification

- New `DateTimeViewerExtensionsTests` — round-trip conversion across several IANA zones (including a
  non-hour-aligned offset and a UTC+14 zone), DST gap/overlap cases, `Kind=Unspecified` input.
- `dotnet build` clean and the full test suite green after each commit.
- Live-verify a 6-page sample spanning every bug pattern, including the two riskiest cases:
  `OrgScheduler.razor`'s full picker+grid round trip, and `AdminUserDetail.razor` as a negative
  control (confirm it's unchanged).
