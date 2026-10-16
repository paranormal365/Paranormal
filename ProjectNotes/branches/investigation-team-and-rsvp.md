# fix/investigation-team-and-rsvp: two "the server allows it, no screen does it" gaps

Found on production 10/16/2026 while setting up App Review's demo data as the group's Owner.
Branched from `develop` at 50c4395d.

## Gap 1: a visit with no case could never have a team

**Schedule an investigation** on the group's Investigations page makes a visit with no case. Its
Team panel (`InvestigationRoster.razor`) said "Nobody has been added to this investigation yet." and
offered nothing. The only add-attendee endpoint was the case-nested one
(`InvestigationController.AddAttendee`, `.../cases/{caseId}/investigations/{id}/attendees`), and
`OrgInvestigationsController` had check-in, attendance, lead and duties, all of which need a team row
first. Check-in returns Forbid with no row, so nobody could ever arrive at a case-less visit.

**Fix**
- `POST api/organizations/{orgId}/investigations/{id}/attendees` (`AddTeamMember`) and
  `DELETE .../attendees/{attendeeId}` (`RemoveTeamMember`) on `OrgInvestigationsController`.
  Keyed on the investigation's own `OrganizationId`, so they serve visits with a case too.
- Gate: Investigation Read in the group, not a Viewer, then `InvestigationAccess.CanManageAsync`.
- Add refuses, with a sentence: somebody who isn't an active member with an open account; a Viewer
  (they can't check in or file an account, so the row would do nothing); somebody already on the
  team (409; the unique index would otherwise be a 500 in SQL Server, and InMemory doesn't enforce it).
- Remove refuses somebody recorded as attended, or who has filed a finding (who was there and what
  they saw are evidence), and deletes their duty assignments first (that key is NoAction).
- Both audit (`LogCreateAsync` / `LogDeleteAsync`) and return the whole roster.
- Adapter: `AddInvestigationTeamMemberAsync` / `RemoveInvestigationTeamMemberAsync`, the
  reason-carrying shape, so a refusal is shown, not swallowed.
- `InvestigationRoster.razor`: new `CanChangeTeam` parameter. When it and `CanManage` are on, an
  **Add to the team** picker (BenSelect, the site's native select) lists active, non-Viewer members
  not already on the team, and the manage table gets a ✕ per row. Off by default, because the case
  page's `InvestigationPanel` already has its own attendee table with roles. `OrgInvestigations.razor`
  turns it on, and updates its Team count from `OnTeamChanged`.
- Kept away from the roster's time-conversion lines (~360, ~528) that
  `fix/viewer-time-zone-saves` edits.

**Checked: what the case-bound add did.** It checked the caller manages the visit and the case is
the group's, but not that the person added is a member, not duplicates (a second add was a 500 from
the unique index), and it wrote no audit row. Its remove didn't clear duty assignments, so removing
somebody with a duty failed in SQL Server. Fixed in the second commit (below).

## Second commit: the case page's door, held to the same rules

Ben asked that everything found along the way be fixed.

- `Services/Investigations/InvestigationTeam.cs` holds the rules once: `WhyCannotJoinAsync`
  (active member, open account, not a Viewer, not already on the team), `WhyCannotLeaveAsync`
  (not recorded as attended, no filed account), and `RemoveAsync` (duty assignments first, then
  the row; doesn't save). A refusal carries its kind (400 or 409) and its sentence. Both
  `OrgInvestigationsController` and `InvestigationController` call it. The gate on the caller stays
  in each controller, because the two prove the visit belongs to the group differently.
- `InvestigationController.AddAttendee` / `RemoveAttendee` apply those rules and now write
  `LogCreateAsync` / `LogDeleteAsync`. The constructor takes `IAuditLogService` (already registered
  for DI); five test builders were updated.
- **Was the old remove's behavior deliberate?** No. Since 20668270 (07/24/2026, the original
  investigation workflow), every change to it has been a permission or scoping gate (5b9dfa48,
  056647eb, 2f5876ad, 9899f875, f1106706). None of them says anything about removing somebody who
  attended or filed an account. So the "recorded as there or wrote it up" rule now applies
  on the case page too.
- **The case page now shows the refusal.** `AddInvestigationAttendeeAsync` returns
  `(Attendee, Error)` and `RemoveInvestigationAttendeeAsync` returns `(Removed, Error)`. Before,
  `InvestigationPanel.razor` ignored both results, so a refused add cleared the picker and looked like
  it had worked. The sentence shows under the attendee table, and the **Add member** picker no longer
  offers inactive members or Viewers.

**Checked: the Guest code.** Redeeming one (`PublicInvestigationCodeController.Redeem` →
`GuestCodes.RedeemAsync`, and the Field Kit launch path in `FieldLaunchService`) creates an
`InvestigationGuestPass` only, never an `InvestigationAttendee`. That is deliberate and documented on
`GuestCodes`: an attendee row is what `MayContributeAsync` reads, and that is also the read door for
every session on the visit, so a walk-up with a team row could see everything the team sees. A guest
can send recordings to that visit and nothing else, and does not appear on the Team panel.

## Gap 2: a host could not mark an invited person as coming

`PUT api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}/rsvp` allowed "the attendee
themselves or an org admin", and `RsvpCalendarEventAsync` existed in the adapter, but no .razor called
it. So a member invited to a private event stayed "Invited" for good. Coming (Accepted) is what the
event reminder, the head count, capacity and an outside guest's Field Kit attach read.

**Where attendees were listed for a host:** the tour date sign-ups page (`OrgTourDateSeats.razor`,
tour dates only, which has its own approve / turn down), and the calendar editor's invite checkboxes
(`OrgScheduler.razor`), which read the attendee list to pre-tick who is invited but showed no answer,
and are hidden on public events.

**Fix**
- The calendar editor shows a **Who's coming** list for an existing event with anybody on it, public
  or not: name, answer, and for whoever may change the calendar (`May(Calendar, Update)`, the same
  rule the endpoint applies) **Mark as coming** / **Mark as not answered**. Saved at once; a refusal
  is shown under the list. A tour date links to its sign-ups page instead, since approving a seat
  sends the welcome email and marking it "coming" here would skip that.
- `RsvpCalendarEventAsync` now returns `(Attendee, Error)` via `SendExpectingReasonAsync`, so the
  server's sentence (a hosted event's list is answered from its Bookings page) reaches the screen.
- The RSVP and remove-attendee endpoints now match the attendee to the group in the address. Before,
  somebody who runs one group's calendar could pass their own group's id with another group's event
  and attendee ids.

**Is hiding Invite on public events deliberate?** Yes. The comment above the block in
`OrgScheduler.razor`, from bde4e03f (2026-08-18, "wave C — the Manage area"), says a public event is
open to the group by definition, so an invitee list would be "a second, quieter permission model
sitting on top of the obvious one". The help page says the same. Left alone; the new list is
separate from the invite boxes and shows on public events too.

## Tests

- `InvestigationTeamTests` (16): add as manager, audited, then the person can check in; refused for a
  non-manager, a member adding themselves, an outsider, a non-member target, a former member, a
  Viewer, a duplicate, another group's visit; remove with duties, refused for a non-manager, for
  somebody recorded as there, for somebody who wrote it up, and an unknown row.
- `CalendarHostRsvpTests` (4): an owner marks coming and back; a plain member can't answer for
  somebody else; another group's owner can't answer for, or remove, this group's guest.
- `Phase4AdapterTests`: the RSVP adapter's new shape, and that it keeps the refusal.
- Each guard was checked to discriminate: with the guards disabled, 10 of the 16 team tests fail;
  with `develop`'s `OrgCalendarController`, the 2 cross-group tests fail.
- Second commit: `CaseInvestigationTeamTests` (12) covers the same refusals through the case route:
  - outsider, former member, closed account, unknown id, Viewer, duplicate;
  - attended and wrote-it-up stay;
  - duties go with the row;
  - both add and remove are audited;
  - non-managers are refused.

  With the case controller's old add/remove logic put back, 10 of the 12 fail. The 2 that pass are
  the non-manager refusals, which were already enforced. `InvestigationTeamTests` gained a
  closed-account case (17), and `Phase5AdapterTests` checks that both case adapters keep the refusal.

## Not done

- Help screenshots and the product PDF were not refreshed (this branch starts no hosts).
