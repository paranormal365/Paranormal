using NUnit.Framework;

// ─────────────────────────────────────────────────────────────────────────────
//  WHY THIS FILE EXISTS
//
//  The suite ran one test at a time, start to finish, and the wall clock was simply the sum of
//  every test's own duration: 400 tests took 17m37s on 2026-08-26 against 17.6 minutes of
//  measured test time, and 21m26s against 21.4 minutes on the run before it. There was no
//  scheduling overhead to recover and no idle time to fill — the machine ran one browser on one
//  of ten cores while the other nine waited. At 790 tests that is most of an hour for a gate
//  that has to be passed before every merge.
//
//  Nothing in NUnit was stopping it. NUnit runs nothing in parallel unless it is told to, at the
//  assembly level, by exactly the two attributes below — and they had never been added. What HAD
//  been added, over months, was the other half of the job: forty fixtures carry
//  [NonParallelizable] with a sentence each saying what they share (the one seeded case, a
//  site-wide switch, the WASM editor host), several of them written after a full run failed a test
//  that passed alone. Every one of those was a no-op. The suite had been annotated for parallel
//  execution it never did.
//
//  So this file is the switch, and the annotations are what makes throwing it safe.
//
//  ── Fixtures, not tests ───────────────────────────────────────────────────
//
//  ParallelScope.Fixtures: different fixtures run at the same time, the tests INSIDE a fixture
//  stay in order, one after another. That is the only scope this suite can take, and the reason
//  is in the fixtures themselves — most of them seed in [OneTimeSetUp] and hold what they made in
//  an instance field (_orgId, _caseId, _eventId) that every test then reads, and a good many
//  depend on the test before (EventDiningTests lays tables, then clears them). ParallelScope.All
//  would run those against each other's state. ParallelScope.Fixtures leaves each fixture exactly
//  the sequence it was written for and takes its parallelism between them, where no promise was
//  ever made.
//
//  ── What [NonParallelizable] buys, precisely ──────────────────────────────
//
//  More than "this one does not run in parallel". NUnit dispatches work in SHIFTS, and only one
//  shift is ever active: the parallel shift drains, then the non-parallel shift runs on the main
//  thread, alone. So a fixture that turns the public feed off and back on is not merely
//  single-threaded — nothing else in the suite is running while it does it. That is the exact
//  guarantee the site-wide switches need, and it is why the forty existing markers are enough to
//  make this change safe rather than a source of new flakes. A fixture found to interfere later
//  gets the same marker and the same sentence.
//
//  ── The number ────────────────────────────────────────────────────────────
//
//  Four, as the default compiled in here, and the run can say otherwise:
//
//      dotnet test … -- NUnit.NumberOfTestWorkers=6     (overrides this attribute)
//      scripts/run-e2e.sh --workers 1                   (back to one at a time, to reproduce)
//
//  Four and not ten. Each worker is a Chromium of its own, and this machine is already running
//  the four hosts the suite tests — the API, the website, and the two WASM hosts — so the cores
//  are not free to begin with. The ceiling that matters is not the CPU anyway: it is the API's
//  rate limiter, which partitions per client IP and sees the whole suite as one caller.
//  scripts/run-e2e.sh raises those limits for the length of a run; a run pointed at a host that
//  has not had them raised will start collecting 429s, and a 429 on this suite reads as a login
//  failure or an empty page rather than as what it is.
// ─────────────────────────────────────────────────────────────────────────────

[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
