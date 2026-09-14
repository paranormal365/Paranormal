# Item 235 — SuperAdmin oversight of hosted events, finished

Branch: `feature/events-superadmin-oversight` (off develop, after hosted events merged).

## Why

Ben, 2026-09-14: "Have you built out superadmin oversight and access to all hosted event screens and the graphs and
charts to help with development?" The honest answer was *partly*: the business dashboard, the list, removal and appeals
existed (phase 17b), and the server lets a SuperAdmin through every hosted endpoint, but nothing had proved a SuperAdmin
can open every event screen of a group they are **not** a member of — every walk had used an account that owns
Paranormal365 — there was no way into those screens from the list, and nothing charted whether the feature is
*working*. Ben said yes to all three.

## The three parts

1. **Proof of access.** A Playwright fixture discovers every event screen from the `@page` directives under
   `Manage/Events` — so a screen added later is covered without anybody remembering — and opens each as the SuperAdmin
   for an event of Music City Spirit Seekers, a group the SuperAdmin does not belong to. Each must show the event and no
   refusal. Anything that refuses is fixed, not skipped.
2. **The screens from the list.** `/admin/events` rows open a detail row linking straight to every screen of that
   event. The links come from one list in the library, `EventScreens`, and a guard fails when an event page exists that
   the list does not name.
3. **An Event health tab** on `/admin/dashboard` (`?tab=event-health`), for development rather than business:
   - holds live now and lapsing in the next day; requests still waiting and the oldest wait;
   - bookings made, answered and holds lapsed, by day; how long answers took;
   - letters queued, sent and failed, by day, and the queue now;
   - errors the server logged on event addresses, by day and by address (SQL Server only; says so elsewhere);
   - rate-limit refusals on the booking and attendance policies;
   - the scheduled jobs: last run, how long it took, whether it failed, failures since the server started.

## Decisions

- **Job runs are held in memory**, not in a table: no migration to carry to production for a development panel, and
  the panel says "since this server started". One process runs the jobs, so one ledger is the truth.
- **Counts and addresses, never people**, as on the business tab. Addresses have ids replaced by `{id}`.
- Letters are not tagged by event in the outbox, so the letters panel is all mail; it is labelled that way.
