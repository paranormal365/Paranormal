# Event scenario gaps (feature/event-scenario-gaps, 10/21/2026)

Jim's overnight conference at The Factory and the menu rules, walked sentence by sentence against the
code. The table, with what was built, partly built or missing and what this branch did, is in
`ProjectNotes/branches/event-scenario-gaps.md`.

> **DEPLOY NEEDS A MIGRATION: `20261021220236_EventScenarioGaps`** (columns and two indexes only;
> `HostedEvents.SendMissedYou` is true for existing rows).

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

## For Ben

These are business decisions or ambiguous, so nothing was guessed:

1. **Who may message in a hosted event's room.** Your note says both people must be checked in. The room
   (item 235) is open to confirmed guests from publishing until a week after, including before anybody
   arrives. I left that as it is and built the check-in rule for ghost walks only. Should a hosted room
   require check-in (and so only open once the doors do)?
2. **Tickets at the door need an account.** The scanner signs in or makes one, because the place and the
   pass live on an account. A no-account door sale (name and email only) would mean making accounts from
   typed addresses, which the site has refused to do so far. Keep it this way?
3. **"We missed you" depends on the door.** When the door checked nobody in, everybody gets the
   thank-you. Fine, or should an organizer be able to tick who came after the event?
4. **The evidence deadline covers evidence offered on the event's page only.** Sessions sent from the
   app to the event still have no deadline (a phone may only get a signal days later). Should the deadline
   apply to them too, by when they were recorded?
5. **Ties in the ranking** go to the **earliest** caught. Or newest first?
6. **"Positive votes"** are the existing *Confirms* votes; *Disputes* and *Inconclusive* don't rank.
7. **Ticket capacity.** A ticket opened after the day passes ran out arrives as a request rather than
   overbooking. Should a ticket the organizer sent always get in?
8. **Events on the home map and keyword search on /events** (story sentences 7 and 8): the nearby list is
   deliberately "list-only, no map". Do you want event pins on the home map and a search box?
9. **A newsletter for people looking for this kind of event** (sentence 9, "if we decide"): not built.
10. **Messaging the organizer from the app** (sentence 10): only the free-text contact line and the
    ask-for-a-place note exist. A "message the organizer" thread would be new.
11. **Sessions in the player for guests, with EVPs** (sentences 33–34): sessions sent to an event play for
    the team only; a guest's session is theirs until they publish it to a place. Should the organizer be
    able to put sent sessions on the results page, with the sender's consent, and should the player show
    the EVP detector's marks?
12. **Menus "in the calendar for the event"**: they're on the event page's dates list. Do you also want them
    on the group's own calendar or in the downloadable .ics?
13. **Extras like alcohol**: "Priced on its own" with a note covers it. Do you want a separate *Extra* mark
    or a 21+ note?
14. **The credit's wording** stays "Menu made with IsHaunted.com · © {year} ISHAUNTED LLC" (approved
    10/13). Your menu note reads "IsHaunted.com © {year} ISHAUNTED LLC"; say if it should be shorter.
15. **Multi-seat parties on the server.** The decision sheet now sends every seat; the server's capacity
    check counts the whole party against each seat. Please try confirming a party of three in seats on the
    seeded evening; if it refuses, the per-seat check needs the party split across the seats.
16. **Door helpers marking classes on the website.** The API lets the door mark class attendance and read
    the roster, but the program page itself is for the group's members; door helpers have no web page for
    it yet (the app has none either).
17. **iOS fixtures** for the results and the walk's room are written from the API's records, not captured.
    Capture them from a running API on the next end-to-end run.
