# Item 120, slice 6 — the investigation area

**8 methods, 10 consumers**, ratchet **34 → 26**.

## Surfaces

Converted with failure rendering: the group's investigation list, a client's own investigations,
scheduling proposals, and the evidence-vote detail panel.

Each of those has a reading that a person will act on:

- **"No investigations"** to a group that has some is what they plan the week around.
- **A client** told they have none stops expecting one.
- **A pending scheduling proposal** that does not render is a date nobody replies to.

`InvestigationPanel`'s attendee cache got the same treatment its binder already had — a refused
attendee list leaves the cache unset and says so, rather than caching an empty roster as fact.

## Decorations, recorded with reasons

- `MyProfile` — attended investigations feed pins on the profile's own map, which has its own
  empty state. Nothing there claims the person attended nothing.
- `EquipmentCheckoutRequestDialog` — the investigation picker is **explicitly optional**: the label
  says so and the default option reads "Not for a specific visit". A refusal leaves an optional
  picker empty and the request still goes through.

## One inverted test

`GetInvestigationsAsync_WhenApiReturnsNull_ReturnsEmpty` asserted that a refusal returns empty — a
green test defending the bug. On a case page that reads as "nobody has scheduled anything", which
is the answer a client is most likely to act on and the hardest for them to check.

## A note on the regex, for the next slice

The block-form converter silently skipped six methods because its body group required at least one
statement between `{` and `var result = …`, and these had none. The count coming out short is the
only reason it was noticed. Worth checking the remaining `?? []` count against the method list
after each mechanical pass rather than trusting the substitution count.
