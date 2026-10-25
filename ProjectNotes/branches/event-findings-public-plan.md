# Event findings: the organizer chooses, and the location can show them (TO PLAN — Ben, 10/24/2026)

Ben: "The organizer can choose if the findings can be public after the event. They can link it to the event
location and make them public there as well."

## What exists (mapped 10/24, file:line in the session's notes)

- Accepted guest evidence is public automatically while the event is listed (Published/Live/Ended), and
  disappears when archived (the calendar row goes private). No organizer switch.
- Sessions "shown with the results" need the sender's yes, the organizer's per-session yes, and a public place.
- The place page shows event material only when each guest publishes their own piece there.
- **Privacy gap:** accepted evidence at a private-residence event is still public on the event page (no
  place-kind check in EventEvidenceController's accepted list and file route). Sessions do check.
- Archive mismatch: the results endpoint allows Archived but its evidence lookup doesn't.

## To decide and build (after show-run extensions)

1. An organizer switch: findings public after the event, or kept to the people who were there.
2. "Show on {place}'s page": the organizer's own act, public places only; consent wording for guests'
   pieces; taking it back.
3. Fix the private-residence leak regardless.
4. Findings stay readable after archive when the organizer chose public.
