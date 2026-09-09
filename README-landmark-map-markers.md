# A landmark visit looks different from a visit to somebody's home

Ben, 2026-09-09: *"why don't we give landmarks or public places their own look or color or marker
on the map."* And, a moment later, the constraint that shapes it:

> "I don't think I would add the public locations to an organization's map unless they have done an
> investigation at the location or have some public data related to the public location."

**That already held.** The map plots investigations, never places, so a landmark nobody has worked
puts no pin anywhere. Nothing here adds a pin. It changes how an existing one is drawn.

## What changed

`InvestigationMapPin` gains `bool? IsPublicPlace`, and the marker template draws a **ring** around
the torch when it is `true`.

The three states are the point:

| Value | Means | Marker |
|---|---|---|
| `true` | a landmark anyone can visit | torch in a ringed, tinted circle |
| `false` | somewhere a person lives | unchanged |
| `null` | **the caller does not know** | unchanged |

Only a *known* landmark is drawn differently. Five components build these pins and only two can
answer honestly — `PlaceView`, which is looking at the place, and `OrgInvestigations`, whose row
now carries `PlaceKind` (the query already `Include`d the whole place, so it cost nothing). The
other three leave it null, and their maps look exactly as they did.

Defaulting an unknown to "private" would have been a map quietly asserting something about
somebody's home that nobody checked. That is the mistake the type is shaped to prevent, and it is
what `InvestigationMapPinTests` guards.

## A ring, not just a colour

A difference nobody can see without colour vision is not a difference. The ring carries the meaning
and the tint rides along for the people it helps. The tooltip says it in words too: *"— a place
anyone can visit"*.
