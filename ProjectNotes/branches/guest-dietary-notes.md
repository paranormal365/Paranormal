# Guests name their party and say what each person can't eat (10/13/2026)

Branch `feature/guest-dietary-notes`, from `feature/event-menus` (that branch has the menus help
sentence this one replaces: *"If there's something you can't eat, tell the venue, so they can note
it on your booking for the kitchen."*). Merge `feature/event-menus` first, or this brings it along.

## The gap

Every booking guest (`HostedEventBookingGuest`) has a name and `DietaryNotes`. The kitchen's sheet
(`Manage/Events/OrgEventDietary.razor`), the dining tables, the door and the iPhone app all read
them, and the API has always taken them: the hold and ask endpoints accept `guests[]`, and
`PUT /api/public/hosted-events/{id}/my-booking` replaces them. But no website form asked. The ask
form sent `Guests: null`, the seat picker sent nothing, and the guest-facing pages showed nothing.
So the kitchen's sheet only had what the host typed on the guest's behalf, and its "nobody named"
count was the whole house.

## What was built

No API or database change. The endpoints already enforce the rules: only the lead's own booking,
no more names than the party has people, the account link kept only for the lead, and only while
the booking is Requested, Held or Confirmed.

1. **`BookingPartyDraft`** (`Organization/Public/BookingPartyDraft.cs`): one row per person in the
   party, kept in step with the party size. The first row gets the lead's name from the account
   unless the guest typed there. Rows with no name aren't sent. A note with no name blocks the
   button, because the server drops a nameless row and the note would be lost without a word.
2. **`BookingPartyFields.razor`**: the rows, with the "a note needs a name" line and who sees them
   (the event's staff: bookings, the kitchen and the door; never public). The wrapper id is
   `{prefix}-guests` because the ask form's party-size box is already `ask-party`.
3. **Asking** (`AskForPlaceForm.razor`): a "Who's coming" section; the party box resizes the rows.
   The note's placeholder no longer suggests allergies, since they have their own box now.
4. **Holding** (`HostedEventPicker.razor`, signed in only): the rows follow the party size the
   picks add up to. Somebody choosing **by email** isn't asked: the email pick
   (`HostedEventEmailPick`) has nowhere to keep names without a migration, so they name the party
   from their booking once signed in. The help says so.
5. **Changing them later** (`BookingPartyPanel.razor`): lists the party and its notes, with
   **Add/Change names and dietary needs** → the rows → **Save**, which sends only `Guests` in the
   edit request. Party size and places are untouched, so a confirmed booking stays confirmed (the
   server re-queues a confirmed booking only when party size or nights change). On:
   - the event's page, inside `EventBookingCard`;
   - **What I'm going to** (`MyEvents.razor`), per live booking; a saved booking is put back in
     the list keeping its sessions and review links, which only that list carries;
   - **the pass** (`MyEventPass.razor`), in its own card, left off the print. The pass page now also
     reads the booking behind it.

## Docs

- `going-to-an-event.md`: new **Who's coming** section with a screenshot, the menus sentence now
  points to it, the held-places and What I'm going to lines mention it.
- `organization-administration.md`: the dietary sheet says guests give the notes themselves and
  that a change doesn't touch their places.
- Changelog 2.20.0: one line.
- `HelpMediaCapture.Capture_HostedEventGuest` shoots `whos-coming.png`.

## Tests

`Ben.Web.Playwright/Tests/HostedEventPartyTests.cs`:

- asking names two people with notes, and the venue's board has both;
- a note with no name keeps Ask from sending, until a name is given;
- holding two seats with a note, and the kitchen's sheet shows it;
- a confirmed party adds a note from What I'm going to, stays "You're coming" (checked through
  the API too), and it is still there after a reload;
- the pass adds a note, and the pass isn't withdrawn.

## Not done

- Names for an email pick before the link is followed (needs storage on `HostedEventEmailPick`).
- The day pass asked for by email (the umbrella's attend form) doesn't ask for names either; the
  booking can be named once signed in.
