# Event scenario gaps (feature/event-scenario-gaps, 10/21/2026)

Jim's overnight conference at The Factory and the menu rules, walked sentence by sentence against the
code. The table, with what was built, partly built or missing and what this branch did, is in
`ProjectNotes/branches/event-scenario-gaps.md`.

> **DEPLOY NEEDS A MIGRATION: `20261021231806_EventScenarioGaps`** (columns and two indexes only;
> `HostedEvents.SendMissedYou` is true for existing rows). It replaced `20261021220236`, which was never
> deployed.

## What this branch adds

- Inviting a pasted list with your own message; **Send somebody a ticket** (confirms on opening); the
  on-the-spot confirmed booking now issues its pass and letter.
- Tickets at the door: a QR code and a printable poster; guests ask on their phone; the door confirms
  once paid.
- **Start the sessions** on the website; a plain "sessions are now being accepted" notice; a helper can be
  given it without the door.
- Class attendance (**There**) on the program roster.
- Role badges in the event room; a room for a ghost walk's checked-in guests and guides.
- After the event: thank-you to who came, "we missed you" to who didn't; the evidence window and deadline;
  confirmed guests can offer evidence; ranking by positive votes then time; votes on the event page; the
  results letter with a link and QR; an **An event's evidence** CMS section; results in the iPhone app.
- Menus: one-tap price kinds, the site mark beside the credit, the planner's title kept from guests, what
  the event covers in the confirmation letter. The decision sheet keeps every seat a party holds.

### Second pass, after Ben's answers

- A party holding several seats or rooms on a night is checked against them together, not whole against
  each one (`EventCapacity.WhyThesePlacesCannotTakeThem`).
- Menus: alcohol has its own **21+** mark and is always priced on its own; the credit reads
  "IsHaunted.com © {year} ISHAUNTED LLC" on the web, the PDF and iOS.
- **Who came** after the event: the organizer's marks override the door's scans for the letters.
- Published menus on the group's calendar entry for each date and in the calendar files; a public
  `/calendar/hosted-events/{id}.ics` linked from the event page.
- Public hosted events on the home map (approximate pins) and a keyword search on **What's on**.
- **Who's at the classes** for door helpers, on the website and in the app.
- Sessions in the results: the sender's yes (app toggle or **My field sessions**), the organizer's
  **Show with the results**, and the event's own standing (public, taking evidence, at a public place).
  Anybody can then play it in the session player from the event's page.
- **Write to the organizers**: a guest with a booking writes from the event's page or the app; one thread
  per guest (`OrgMessageChannel.EventGuestThread`) addressed to whoever decides bookings; the organizers
  answer on **Guest messages**; each side's bell says when there's something new.

## Ben's answers (10/21/2026), now in the help

1. **The hosted room** stays open to confirmed guests from publishing; check-in only applies to walks.
2. **Tickets at the door need an account.** Kept.
3. **Who came**: built; the organizer marks who came before the letters go.
4. **The evidence deadline** covers evidence offered on the event's page only; app sessions are exempt.
5. **Ties** go to the earliest caught.
6. **Only *Confirms* votes** rank.
7. **A ticket opened after the day passes ran out** arrives as a request. Kept.
8. **Map pins and search**: built.
10. **Messaging the organizer**: built.
11. **Sessions in the results**: built, with the sender's consent.
12. **Menus in the calendar**: built.
13. **Alcohol**: its own 21+ mark, always priced on its own.
14. **The credit**: shortened.
15. **Multi-seat capacity**: fixed, with a test.
16. **Door helpers marking classes**: a web page and an app screen.

## For Ben

Still open:

1. **The AAC EVP detector** isn't part of the session player's marks on the public page: the player shows
   the marks the sender's phone made. Running the detector on shown sessions is a separate piece of work.
2. **A newsletter for people looking for this kind of event** (sentence 9, "if we decide"): not built.
3. **Who may write to the organizers.** Anybody with a booking at the event, whatever its status, as its
   lead or named on it. Somebody thinking about booking can't write yet. Should they?
4. **iOS fixtures** for the results, the walk's room, the classes and the organizer thread are written
   from the API's records, not captured. Capture them from a running API on the next end-to-end run.
