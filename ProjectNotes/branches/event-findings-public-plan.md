# Event findings: private to the people who were there, public when the organizer says, and on the location's page (plan, 10/25/2026)

Branch: `feature/event-findings` (from develop).

## Ben's rules

1. "The organizer can choose if the findings can be public after the event. They can link it to the event
   location and make them public there as well."
2. Answers, 10/25:
   - **Private means the people who were there**: guests with a confirmed place, plus the organizers and staff.
   - **New events start private** until the organizer publishes their findings.
   - **On the location's page, the organizer picks pieces**, "but there is also the option for the organizer to
     select to share everything public."
   - **Guests' pieces:** "The organizer, but the guest is given a week to opt out or can choose to opt out
     before or during the event."

"Findings" = an event's accepted guest evidence, the field sessions shown with its results, and its results
link/letter. Votes ride along with what they vote on.

## What exists today (mapped 10/24)

- Accepting evidence sets `UploadFile.IsPublic = true` (EventEvidenceController.Review): the file is public
  the moment it is accepted, anonymous route and all.
- The accepted list and file route are anonymous, gated only by the calendar row being public
  (`OrgCalendarEvent.IsPublic`, which copies `HostedEvent.IsOnThePublicSite`).
- Sessions shown with the results: sender's yes + organizer's per-session yes + `CollectsEvidence` + a
  public-location place (`EventSessionPublication`).
- The place page shows event material only when a guest publishes their own piece there
  (`EventEvidenceSubmission.PublishedToPlaceAtUtc`, `ArchiveEvidencePublication`).
- **Leak:** accepted evidence at a private-residence event is public on the event page (no place-kind check
  in `Accepted` / the file route). Sessions do check.
- Archive mismatch: the results endpoint allows Archived; its evidence lookup doesn't.

## The model

`HostedEvent` gains:
- `FindingsPublishedUtc` (null = private to the people who were there) and `FindingsPublishedByAppUserId`.
- `FindingsOnPlace` (enum: `None`, `Chosen`, `AllPublic`) — what the location's page shows.

`EventEvidenceSubmission` and the session's event link gain:
- `OnPlaceChosenUtc` (the organizer picked this piece for the location's page),
- `PlaceOptOutUtc` (the guest said no), and the row's `PlaceShowFromUtc` = chosen + 7 days.

`HostedEventBooking` gains `FindingsOffPlaceUtc`: the guest's standing "keep my findings off the location's
page" for this event, set before or during the event (or any time), which acts as an opt-out for every piece.

**Who sees a finding** (one rule, `EventFindings.WhoMaySee`):
- Unpublished: organizers/staff + confirmed guests of the event (signed in).
- Published: anyone — **unless the place is a private residence**, which is never public (rule 0, the leak fix).

**On the location's page** a piece shows when: findings are published, the place is a public location,
the piece is chosen (or `AllPublic`), the guest hasn't opted out, and `now >= PlaceShowFromUtc`.

## Phase 0 — the leak, first and on its own (shippable alone)

- [ ] 0.1 `Accepted`, the evidence file route and the results endpoint refuse a private-residence event's
      evidence to anybody but the organizers and the people who were there.
- [ ] 0.2 Accepting at a private-residence event does not set `UploadFile.IsPublic`; already-public files
      at such events are set private by a one-off data fix in the migration.
- [ ] 0.3 Tests: anonymous refused; a confirmed guest and an organizer allowed; a public-location event
      unchanged. Each fails on today's code.
- [ ] 0.4 Merge and ship on its own; deploy SQL for 0.2.

## Phase 1 — data (migration owed at deploy)

- [ ] 1.1 `HostedEvent.FindingsPublishedUtc`, `FindingsPublishedByAppUserId`, `FindingsOnPlace`.
- [ ] 1.2 `EventEvidenceSubmission.OnPlaceChosenUtc`, `PlaceOptOutUtc`; the same on the session's event link.
- [ ] 1.3 `HostedEventBooking.FindingsOffPlaceUtc`.
- [ ] 1.4 **Existing events** (gap G1): events already showing findings keep showing them — the migration sets
      `FindingsPublishedUtc` for hosted events on a public location with any accepted evidence or shown
      session, so nothing live disappears. New events start private.
- [ ] 1.5 Tests: the migration's backfill rule as a unit (which events get published).

## Phase 2 — who may see (the gate everything uses)

- [ ] 2.1 `EventFindings.MaySeeAsync(db, hostedEvent, userId?)`: organizers/staff (existing access checks),
      confirmed guests (lead or named, any confirmed booking), or anyone when published and not a residence.
- [ ] 2.2 Accepting no longer makes the file public by itself; the file is public exactly when the event's
      findings are published (and not a residence). Publishing/unpublishing flips the event's accepted files.
- [ ] 2.3 Evidence list, file route, results endpoint, sessions shown with results, votes, the event page's
      "What people caught": all through 2.1. Signed-out visitors of an unpublished event see "Findings are
      shared with the people who were there" (no list).
- [ ] 2.4 Archive: findings stay readable after archive by whoever 2.1 allows (fixes the mismatch).
- [ ] 2.5 The results letter links as today; for unpublished findings the link asks the guest to sign in.
- [ ] 2.6 Tests: each surface × (anonymous, signed-in stranger, confirmed guest, organizer) × (published,
      unpublished, residence).

## Phase 3 — the organizer's choice

- [ ] 3.1 `POST …/events/{id}/findings/publish` and `/unpublish` (organizer permission). Allowed once the
      event has ended (the first night has started? — gap G2: "after the event").
- [ ] 3.2 After the event page: **Who can see the findings** — "The people who were there" / "Everyone",
      with what each means; residence events say they stay with the people who were there.
- [ ] 3.3 Audit entries for publish/unpublish.
- [ ] 3.4 Tests: publish/unpublish flips files and visibility; refused before the event; residence refused.

## Phase 4 — the location's page

- [ ] 4.1 After the event page: **Show on {place}'s page** — off / pieces I choose / everything public.
      Only for a public-location place, only once findings are published.
- [ ] 4.2 Choosing a piece (evidence or session) stamps `OnPlaceChosenUtc` and `PlaceShowFromUtc = +7 days`,
      and tells the guest: "{Organizer} would like to show your {piece} on {place}'s page from {date}.
      Keep it off" — one tap, any time. Choosing "everything public" does the same for every piece, and for
      pieces accepted later as they're accepted.
- [ ] 4.3 `PublicPlaceController` lists the event's pieces that pass the place rule (model above), credited to
      the event and the guest, beside what guests published themselves (no duplicates when both).
- [ ] 4.4 Unpublishing findings, or switching the place off, takes them off the place page at once.
- [ ] 4.5 Tests: shown only after 7 days; opt-out (per piece and standing) wins at any time; residence never;
      unpublish removes; no duplicate with a guest's own publication.

## Phase 5 — the guest's say

- [ ] 5.1 Booking card / pass page / app's event screen (gap G3 — app needs a version): "Keep my findings off
      {place}'s page" switch, before, during or after the event.
- [ ] 5.2 The notice letter and bell row with the one-tap "Keep it off".
- [ ] 5.3 The guest's own evidence/session list shows each piece's state: with the people who were there /
      public / on {place}'s page from {date} / kept off.
- [ ] 5.4 Sending evidence says, plainly, what the organizer may do with it (help-backed sentence).
- [ ] 5.5 Tests: standing opt-out before the event blocks later choices; per-piece opt-out after showing
      removes it; the letter goes once.

## Phase 6 — words and records

- [ ] 6.1 Help: going-to-an-event (who sees findings, the week, keeping off), organization-administration
      (publishing findings, the location's page), the mobile apps (if 5.1 reaches the app).
- [ ] 6.2 Change log; product PDF; brochure lines if any claim "public record" (they do: "you approve what the
      public sees" stays true).
- [ ] 6.3 Consent sentences updated where they promise less (EventSessionPublication's "only the group").

## Phase 7 — proving it

- [ ] 7.1 Unit/controller tests per phase, each run against the old code first.
- [ ] 7.2 Browser test: a guest sends evidence; the organizer accepts; a stranger can't see it; the guest
      can; publish; the stranger can; choose it for the place; the guest keeps it off; the place doesn't show
      it.
- [ ] 7.3 Full suite on a fresh database; deploy SQL; merge; push on Ben's word.

## Gaps found

| # | Gap | Proposed answer |
|---|---|---|
| G1 | Events already showing findings publicly today | Keep them public (migration backfill), so nothing live disappears; only new events start private. **Confirm with Ben** |
| G2 | "After the event": when may the organizer publish? | Once the first night has started (findings arrive during the night), or any time after |
| G3 | The app shows results from an anonymous endpoint | It already sends the guest's sign-in, so guests keep seeing private findings; the opt-out switch in the app needs a new version (1.1.8) — the web works without it |
| G4 | Ordinary public calendar events (not hosted) also collect evidence | Unchanged: they keep today's "accepted = public". Only hosted events get the choice |
| G5 | A guest opts out after a piece already showed on the place page | Removed at once; nothing cached publicly beyond the page |
| G6 | A guest's own "publish to the place" (exists today) vs the organizer's choice | Both lead to the same listing, never twice; the guest's own act needs no week |
| G7 | Votes on unpublished findings | Only the people who can see them can vote |
| G8 | Staff who weren't "guests" (door helpers) | Count as organizers/staff for seeing |
| G9 | Walk-ups (no account) | Can't see private findings (no sign-in); published ones like anyone |
| G10 | Unpublish after the place page showed them | Off the place page at once (4.4) |
| G11 | A residence event's organizer asks to publish | Refused in words: findings from a private home stay with the people who were there |
