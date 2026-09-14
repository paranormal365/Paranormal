# Media retention: the warning query SQL Server could not run

Branch `claude/happy-chandrasekhar-8d0f91`, 2026-09-13. A fix to item 233's retention job.

## What was wrong

`MediaRetentionJob.WarnAsync` filtered on `ExpiryNoticeSentAtUtc < ExpiresAtUtc.Value - LastNotice`.
EF Core cannot translate a column minus a `TimeSpan` for SQL Server, so the query threw
`InvalidOperationException` ("could not be translated") every time it ran. The scheduler logs and
swallows job failures, so the API stayed up and the only trace was an `ERR` line every six hours.

## What that meant, by configuration

| Mail set up? | Before this fix | Notices sent | Files removed |
| --- | --- | --- | --- |
| Yes (production, any host with `Smtp:Host`) | `WarnAsync` threw before sending; `SweepAsync` runs after it, so it never ran | none, ever | none |
| No (dev with `Smtp:Host` null) | `WarnAsync` returned before the query; the sweep ran | none, by design | **any expired file on a plan with a clock, unannounced** |

So since the job shipped on 2026-09-10 **no expiry notice has been sent anywhere**. On a host with
mail, nothing was deleted either. On a host without mail, the sweep could delete files nobody was
warned about — which the class's own remarks say it must never do.

## What changed

1. **The predicate moved the arithmetic onto the notice**: `ExpiryNoticeSentAtUtc.Value.AddDays(LastNotice.TotalDays) < ExpiresAtUtc`.
   Same meaning (`a < b - d` is `a + d < b`); `AddDays` translates to `DATEADD`. Extracted as
   `MediaRetentionJob.DueForNotice(now)` so the test and the job share one expression.
2. **The sweep now enforces "never unannounced".** Fixing (1) alone would have let the first pass
   delete every file that expired during the broken window, because none of them had been warned.
   Now an expired file with no `ExpiryNoticeSentAtUtc` is given `now + LastNotice` instead of being
   deleted: warned on the next pass, taken a day after. With mail not set up those files are not
   even read (`DueForSweep(now, canWarn)`), so they cannot crowd warned files out of the batch.
3. `PassAsync` is the gate-free pass, because `RunAsync`'s six-hour gate is static and would
   silence every other test in the process.

## Rejected

- **`EF.Functions.DateDiffDay`** — counts day *boundaries* crossed, not 24-hour spans, so it
  changes the rule near midnight.
- **Client evaluation (`AsEnumerable`)** — reads every clocked file every pass to filter in memory.
- **Deleting overdue unwarned files anyway** ("they were due") — the warning is the point of the job.

## Tests (`Ben.Web.Tests/Services/MediaRetentionJobTests.cs`)

- `ToQueryString()` on the **SQL Server provider** (no server needed) for both queries. Against the
  old predicate it fails with the production error message; SQLite also refused the old form, but
  cannot vouch for SQL Server.
- A case table checked against the rule as first written, then run through a real SQLite query.
- Sweep: unwarned expired file rescheduled (mail on) / untouched (mail off) / warned expired file
  deleted. Both guard tests fail with the guard removed.
- `Both_queries_run_on_a_real_SQL_Server` — opt-in via `BEN_SQLSERVER_TEST_CONNECTION`, read-only.

## Not done

- No migration; no schema change.
- Not yet run against a live SQL Server from this session (ad-hoc DB access was refused by the
  permission classifier) — the opt-in test is how to do it.
- Not checked: how many files in production are already past their date unwarned. They will each
  get a day's notice on the first pass after deploy.
