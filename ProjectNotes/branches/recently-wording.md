# fix/recently-wording

Ben, 10/16/2026, after checking the live site: under **Recently** on What I'm going to, things that
are over still said "You're coming" or "You're invited", e.g. "Bell Witch Cave — autumn survey ·
Over · You're coming".

- `NextUpCard.PastStandingWords(kind, standing)` gives the past tense: "You were on the team"
  (investigation), "You said you'd come" (calendar RSVP), "You had a place" (tour seat or booking),
  "You were invited", "You asked for a place", "A place was held for you". It never says "You
  went": a place held is not proof somebody turned up.
- `MyOtherEvents` uses it for Recently rows, with a muted badge; rows still ahead are unchanged.
- Test: `MyEventsPageTests.What_is_over_is_said_in_the_past_tense` (fails on the old code).
- Help (`going-to-an-event.md`) and the Unreleased changelog line updated; PDF reprinted.
