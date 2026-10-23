# feature/ask-organizers-before-event

**Ben, 10/23/2026:** "People can write questions to the event organizers before the event." Then: "They can
ask questions to organizers, but that is about all I would think should be allowed. I would also make a
note if you are able to determine if they are signed up for the event or confirmed for the event you
might notate it for the organizer to see."

## What changes

- **Who may write** (`EventGuestThreads.WhyClosedFor`): a guest (any booking, lead or named) as before;
  anybody signed in until the event's first night starts (`StartsAtUtcAsync`, the event's own window
  rule). After the start, a person without a booking can still read their conversation and can't write
  ("Questions from people without a booking close when the event starts."); one who never wrote is told
  nothing (204). Nothing else is opened to them: no room, no downloads, no results beyond what's public.
- **Organizers see where each writer stands** (`StandingsAsync`): *Confirmed*, *Signed up, places held*,
  *Signed up, not confirmed yet*, *Signed up, the hold ran out*, *Turned down*, *Booking cancelled*, *Not
  signed up*. On the Guest messages list and the open conversation. The guest never sees it.
- Website: the event page draws the conversation for whoever the server offers it to (no change needed
  beyond wording). App: the event hub offers the row to a non-booked reader the server offers a
  conversation to (ships in the next app version).
- Help: going-to-an-event, organization-administration; changelogs webapp + ios Unreleased; product PDF.

## Tests

`WritingToTheOrganizersTests`: a stranger asks ten days out, Jim answers, the event starts, the stranger
reads and can't write, Jim and a guest still can; standings on the list and the thread. The new test and
the updated first test fail on the old controller.

## Follow-up (feature/ask-organizers-signed-out)

Signed out, an upcoming hosted event's page shows a short "Write to the organizers" section: sign in
(returning to `#event-organizer-thread`) and write, no booking needed. Hidden once the event starts.
Playwright `Signed_out_the_event_page_says_you_can_sign_in_and_ask_the_organizers`, not yet run (test
database unreachable 10/23).
