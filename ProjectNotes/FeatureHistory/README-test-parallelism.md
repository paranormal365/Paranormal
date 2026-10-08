# Branch: feature/test-parallelism

Ben, 2026-10-01: *"For the tests in ishaunted.com, use parallelism to run them in order to cut down
on how long it takes to run them."*

## Measured before anything changed

| suite | tests | time | state |
|---|---|---|---|
| `Ben.Web.Tests` | 7,786 | 1m41s | **already parallel.** One thread takes 2m54s, so xunit's default collection parallelism is already giving 1.7× |
| the other six xunit projects | 4,570 | 14s together | already parallel, too small to matter |
| whole `dotnet test Ben.slnx` | 12,356 | **1m45s** | — |
| `Ben.Web.Playwright` | 983 run, 106 skipped | **53m12s** | **one test at a time, start to finish** |

Pushing xunit harder was tried and rejected: 16 threads is 1m53s and 24 is 2m15s — both slower than
the default ten, and both fail one load-sensitive test. More threads than cores is a loss here, so
the unit side is left exactly as it was. **Everything below is about the browser suite.**

That the browser suite had no slack to recover was already on record: `proof-run.trx` (2026-08-26)
is 400 tests in 17m37s against 17.6 minutes of measured test time, and `clean-run.trx` is 21m26s
against 21.4 minutes. Wall clock *was* the sum of the parts. One browser on one of ten cores.

## After

**53m12s → 22m16s at four workers**, on the same code, measured end to end around
`scripts/run-e2e.sh` both times.

## What changed

- **`Ben.Web.Playwright/Parallelism.cs`** (new) — `[assembly: Parallelizable(ParallelScope.Fixtures)]`
  and `[assembly: LevelOfParallelism(4)]`, with the whole argument in the file: why fixtures and not
  tests, what `[NonParallelizable]` actually guarantees, and why four rather than ten.
- **`scripts/run-e2e.sh`** — `--workers N` (default 4, `BEN_E2E_WORKERS` honoured), passed to NUnit
  as `NumberOfTestWorkers`; and the API's per-address rate limits raised for the length of a run.
- **32 fixtures** given `[NonParallelizable]`, each with a sentence naming what it shares.
- `Ben.Web.Playwright/README.md` and `ProjectNotes/Running-Playwright-Tests.md`.

## The two findings worth keeping

**Forty fixtures were already annotated for a parallelism that never happened.** They carry
`[NonParallelizable]` with a sentence each — the one seeded case, a site-wide switch, the WASM
editor host — and several were written *after* a full run failed a test that passed alone. Every
one of them was a no-op, because NUnit runs nothing in parallel until an assembly says so, and no
assembly ever did. That pre-existing discipline is what made throwing the switch a day's work
rather than a month's.

**`/login` is the ceiling, not the CPU.** It is anonymous, so the limiter keys it by address, and
the whole suite is one address — at twenty a minute. A serial run of 400 tests over 18 minutes
already signs in about twenty times a minute, which is where *"a 429 reads as Invalid email or
password"* came from. Four workers go straight through it, and it would have surfaced as a
credentials problem in four unrelated fixtures at once. The script raises it for the run only,
through configuration rather than the database; no seeder writes those rows, so a fresh e2e
database takes the raised values and the shipped defaults are untouched.

## The failures, sorted

The first parallel run failed 25. The serial baseline failed 8. Three are in both lists and are
therefore **pre-existing, not caused by this change**, and deliberately have NOT been marked:

- `PublicCaseTests.CaseDetail_AuthenticatedUser_SeesVoteButtons`
- `StoreFavouritesTests.Seeded_favourites_are_listed`
- `FirstClickAfterTypingTests.Navigating_the_site_does_not_open_a_second_circuit` — which already
  carried `[NonParallelizable]`, so it ran with nothing else in flight and its failure cannot be an
  artefact of parallelism

Five more fail only serially, on a freshly seeded database: `The_bands_are_designed_cards…`,
`Shop_pages_fit_and_the_product_stacks_where_it_should` (two viewports),
`Guest_FindsTheirOrder_ByTheEmailedLink`, `A_scheduled_time_prints_no_seconds`. Unrelated to this
work; worth a look of their own.

The remaining 22 were concurrency, and two of the three clusters said so in their own error text:

| cluster | what it said | fixtures marked |
|---|---|---|
| the one seeded hosted event | *"Somebody took one of those places a moment ago"* | 22 — `EventDoorTests` clears the whole house in `[SetUp]`, and 22 of the 25 fixtures that touch event `40000002-…-0003` write to it |
| the store's site-wide switches | *"The store isn't taking orders at the moment"* | 3 — `StoreCartTests` turns checkout off and back on; `AdminStoreCatalogTests` writes the one settings row |
| other site-wide switches | — | 4 — self-registration, the announcement banner, a feature flag, the default avatar |

**Only the writers needed marking.** A `[NonParallelizable]` fixture runs in a shift of its own
with nothing else in flight, so marking `StoreCartTests` is enough and `StoreCheckoutTests` — at
224 seconds the most expensive fixture in the suite — stays in the parallel pool. Marking the
victims instead would have put ten minutes back into the serial block.

## Still to do

1. **A verification run at `--workers 4` with the 32 markers in place.** Expect the three
   pre-existing failures and nothing else. Not yet run; this branch is not finished without it.
2. `Ben.Canvas.Playwright` has the same latent trap — two `[NonParallelizable]` markers that are
   no-ops — and has not been touched, because its suite has no runner script and proving it green
   in parallel needs one.

## The floor, and why it is where it is

About 15 minutes of the suite is now a serial block, and **8 of those minutes are the 27 fixtures
that were already marked before this work started.** More workers cannot touch that: six workers
model out about two minutes better than four, which is not worth the extra contention. Going below
roughly twenty minutes means giving the hosted-event fixtures events of their own instead of
sharing one seeded event — real work, well beyond what was asked for here, and recorded rather than
quietly started.
