# Design directions — pick one

Ben, 2026-10-01: *"could you spice up the template like you did those flyers? They look great and
the website feels dated. Give me some previews to choose from."* and *"It will need a light and
dark version."*

**These are mock-ups.** Standalone HTML, no Blazor, no data, nothing wired to anything. They exist
to be looked at and chosen between; the two that lose get deleted. Nothing here ships as it stands.

```bash
python3 docs/design/build-directions.py --png
```

`preview/00-today-dark.png` is the site as it is now, captured the same way, so the comparison
means something.

## Why they can be compared fairly

Every direction renders **the same page and the same words** — the bar, the hero with its location
search, what's near you, what's on, the flyer strip, the footer — so what is being compared is the
design and only the design. Same photographs too.

Each is a full set of decisions rather than a palette swap: type family and scale, hero treatment,
card shape, button shape, the rhythm between sections, and where the accent is allowed to appear. A
recolour of the current layout would not answer what Ben actually said, which is that the site
*feels dated* — and that feeling comes from the layout and the type, not the hue.

| | Direction | What it is | Risk |
|---|---|---|---|
| 01 / 02 | **Lantern** | The flyer, on the web. Amber on deep navy, a photographic hero, Futura display type, generous room. | Closest to what is already approved, so the least surprising — and the least distinctive. |
| 03 / 04 | **Nightfall** | Cinematic and editorial. Near-black, one ember accent, a serif headline doing the work, square photography and hairline rules. | The furthest from an admin panel, and the furthest from the Bootstrap the site is built on — the most work to retrofit. |
| 05 / 06 | **Signal** | A 2026 product site. Soft gradient and glow, pill buttons, big rounded cards, floating glass search. | Modern and credible, but the least haunted; it could be any SaaS. |

## Light and dark

Both for each, because the site has a theme toggle and the current one is used in both.

One decision is worth knowing about because it is not obvious: **text over a photograph stays light
in both modes.** The first pass derived the hero from the palette, so in light mode the kicker was
dark amber on a night photograph and simply vanished. A hero is a picture, not a surface — it keeps
its own ink and its own accent, and gets a scrim behind the words rather than trusting the picture
to be dark wherever the text lands.

## What a real one would cost

The site is SmartAdmin over Bootstrap (`wwwroot/css/smartapp.min.css` plus a theme file), and the
look comes from that template, not from our own CSS. Whichever direction wins, doing it properly
means a token layer of our own — colour, type scale, radius, shadow, spacing — that overrides the
template rather than editing it, so the template can still be updated. That is a real piece of
work, and the honest first step is a single page converted end to end before anything else is
touched.

Worth saying plainly: these mock-ups are hand-written HTML with no Bootstrap underneath. They show
where we could get to; they do not prove how long it takes to get there.
