# Embedding cases and investigations in public pages

Backlog item **#80, part 4** — the part with teeth. Appending public work to a page is
straightforward; private investigations are not, and this is the point in the CMS where a mistake
publishes somebody's home address rather than an ugly layout.

## The decision everything follows from

**References are stored; records are resolved.** A section holds ids and switches, never a copy of
the data. The public endpoint expands it on every request against live rows.

That means a client who withdraws their alias next month disappears from pages published today,
without anybody remembering which pages those were. A snapshot taken at embed time would freeze
whatever happened to be true that afternoon and quietly outlive every later decision.

## What a visitor can receive

`EmbeddedInvestigation` and `EmbeddedCase` have **no field** for an exact latitude, a street address
or a real name. Absent, not nulled. Reflection tests assert it — the cheapest guard in the file and
the strongest, because every other test checks what the code *currently* writes, while this checks
what the payload is *able* to hold. A later mapping change cannot reintroduce a field that redaction
then has to remember to blank.

Coordinates that are published are grid-snapped through the existing `PublicCoordinates`, and always
travel with `LocationIsApproximate` — a bare point on a map reads as the place.

## Rules, all enforced at read

| Rule | Why at read time |
| --- | --- |
| Only the group's own records resolve | The picker offers only their own work, but a picker is a convenience and a request can say anything |
| Non-public work needs an explicit acknowledgement | A section saved by an older editor cannot publish something by omission |
| Client names go through `PublicClientName` | It has no branch that returns a real name, and reusing it stops an embed and the case's own page disagreeing about who somebody is |
| Malformed settings publish nothing | Elsewhere a bad section renders an empty box; here it decides whether an address goes out |
| Preview resolves identically to public | A preview that redacted differently would be reassuring about a page that will not look like that |

The editor keeps Ben's stated order — warn about non-public work, **then** ask about the address,
**then** about identities. The warning is what makes the two questions land as decisions rather than
as a settings screen.

## Two things the tests caught

**The resolver emitted PascalCase while the renderer reads camelCase.** Every embedded card would
have rendered blank on a real page. Caught by a test asserting the title *is* published — not by any
of the several asserting an address is not. The JSON is a string carried inside the response, so the
outer serializer never touches it and the casing set here is the only casing there is.

**Breaking the location switch on the investigation branch failed nothing**, because every location
test happened to use a case. Two branches resolve locations and only one was covered. Found by the
discrimination run, not by the tests passing.

Also worth noting: my first fixture gave the client the alias "The Elm Street Family" while the case
was on Elm Street, so the address-leak assertion was failing on a name the client had deliberately
chosen to publish. The fixture was wrong, not the code — but a leak test that trips on its own seed
data is a leak test nobody will trust.

## Verification

- Clean solution build, **0 warnings, 0 errors**; full suite **green (4,651)**.
- Every guard broken deliberately and watched to fail: ownership, the acknowledgement, both location
  switches, the client-name switch, and fail-closed parsing.
- No migration — the section types are enum values and the settings live in existing `ContentJson`.

## Not done

- **No human has click-tested the editor.** The picker, the warning and the two switches are
  Telerik-adjacent Blazor and cannot be exercised here.
- **`CaseRelatedPerson`, witnesses and investigators are not embeddable**, deliberately. There is no
  alias concept for them at all, so there is nothing safe to publish yet — noted in the backlog
  rather than half-built.
- **Part 2b** (Investigation Results page templates) now has its safe projections to build on, which
  was the reason for doing this first.
