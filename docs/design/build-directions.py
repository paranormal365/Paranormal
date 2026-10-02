#!/usr/bin/env python3
"""Three design directions for the site, each in light and dark, for Ben to choose from.

Ben, 2026-10-01: *"could you spice up the template like you did those flyers? They look great and
the website feels dated. Give me some previews to choose from."* Then: *"It will need a light and
dark version."*

WHAT THESE ARE AND ARE NOT

They are mockups: standalone HTML, no Blazor, no data, nothing wired to anything. They exist to be
looked at and chosen between, and the losers get deleted. Nothing here ships as it stands.

**Every direction renders the SAME page and the same words**, so what is being compared is the
design and only the design. The page is the home page as a visitor meets it: the bar, the hero with
its location search, what is near you, what is on, the flyer strip, the footer. The current site's
own screenshot (docs/design/preview/00-today-*.png) is captured beside them for the comparison to
mean anything.

Each direction is a full set of decisions, not a palette swap — type family and scale, hero
treatment, card shape, button shape, the rhythm between sections, and where the accent is allowed
to appear. A recolour of the same layout would not answer the question Ben actually asked, which is
that the site feels dated.

Run from the repository root:

    python3 docs/design/build-directions.py          # writes the HTML
    python3 docs/design/build-directions.py --png    # and photographs each one

Photographs: docs/media/stock (Unsplash License).
"""
import os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
STOCK = "../media/stock/"

# ── The content, written once ───────────────────────────────────────────────
PLACES = [
    ("The Hermitage Hotel", "Nashville, TN", "v3-hotel-lobby.jpg", "14 reports", "Public place"),
    ("Bell Witch Cave", "Adams, TN", "w3-foggy-graveyard.jpg", "9 reports", "Public place"),
    ("Printers Alley", "Nashville, TN", "w1-cobblestone-streetlights.jpg", "6 reports", "Ghost walk"),
]
EVENTS = [
    ("An Evening of Evidence", "Fri 17 Oct · 7:00 pm", "v6-ballroom.jpg", "Paranormal365", "32 of 60 places left"),
    ("Printers Alley Ghost Walk", "Sat 18 Oct · 8:30 pm", "w2-cobblestone-night.jpg", "Printers Alley Walks", "Books by the hour"),
]
# The real thumbnails the home page already serves, so the strip in a mock-up is the strip.
THUMBS = "../../Ben.Web.Website/wwwroot/flyers/thumbs/"
FLYERS = [("Everyone", "IsHaunted-Flyer.png"), ("Just curious", "IsHaunted-Ad-IsHaunted.png"),
          ("Enthusiasts", "IsHaunted-Ad-Individuals.png"), ("Investigation groups", "IsHaunted-Ad-Groups.png"),
          ("Ghost walk tours", "IsHaunted-Ad-Ghost-Walks.png"), ("Haunted venues", "IsHaunted-Ad-Venues.png"),
          ("Event hosts", "IsHaunted-Ad-Event-Hosts.png")]
NAV = ["Home", "Join a Group", "Feed", "What's On", "Equipment", "Store"]

ICONS = {
    "search": '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
    "pin": '<path d="M12 22s7-6.2 7-12a7 7 0 0 0-14 0c0 5.8 7 12 7 12z"/><circle cx="12" cy="10" r="2.5"/>',
    "cal": '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
    "sun": '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
    "cart": '<circle cx="9" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/><path d="M2 3h3l2.3 11.3a2 2 0 0 0 2 1.7h7.5a2 2 0 0 0 2-1.6L21 7H6"/>',
}


def ic(name, size=18, w=1.8):
    return (f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            f'stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round">{ICONS[name]}</svg>')


# ── The three directions ────────────────────────────────────────────────────
#
# Each is a set of decisions, not a palette. The tokens below cover colour; the `css` block under
# each one covers the rest — type family and scale, hero treatment, card shape, button shape, and
# the rhythm between sections.

DIRECTIONS = {
"lantern": dict(
    hero_accent="#F5A524",   # reads on a photograph in either mode
    name="Lantern",
    blurb="The flyer, on the web. Warm amber on deep navy, a photographic hero, "
          "confident display type and generous room between things.",
    modes=dict(
        dark=dict(bg="#0A1322", bg2="#0E1B2E", panel="#14263E", ink="#FFFFFF",
                  muted="rgba(255,255,255,.72)", accent="#F5A524", accent2="#FFD27A",
                  line="rgba(255,255,255,.12)", shadow="0 18px 40px rgba(0,0,0,.45)",
                  scrim="10,19,34"),
        light=dict(bg="#F7F2E8", bg2="#FFFFFF", panel="#FFFFFF", ink="#16202E",
                   muted="#5A6473", accent="#B06A0C", accent2="#D8911F",
                   line="rgba(20,30,45,.13)", shadow="0 12px 30px rgba(20,30,45,.13)",
                   scrim="247,242,232"),
    ),
    css="""
  body { font-family: "Avenir Next", Avenir, system-ui, sans-serif; }
  h1, h2, h3, .display { font-family: Futura, "Avenir Next", sans-serif; font-weight: 800; letter-spacing: -.015em; }
  .kicker { font-size: 11px; font-weight: 700; letter-spacing: .22em; text-transform: uppercase; color: var(--accent); }
  .hero { height: 460px; }
  .hero h1 { font-size: 58px; line-height: 1.0; max-width: 11ch; }
  .hero p { font-size: 19px; max-width: 46ch; }
  .btn { border-radius: 9px; font-weight: 700; padding: 13px 22px; font-size: 15px; }
  .btn-primary { background: linear-gradient(135deg, var(--accent), var(--accent2)); color: #0A1322; }
  .btn-ghost { background: transparent; border: 1.5px solid var(--line); color: var(--ink); }
  .field { border-radius: 9px; border: 1.5px solid var(--line); background: var(--panel); }
  .card { border-radius: 15px; border: 1px solid var(--line); background: var(--panel); box-shadow: var(--shadow); overflow: hidden; }
  .card .shot { height: 148px; }
  .card .body { padding: 16px 18px 18px; }
  .card h3 { font-size: 17px; }
  .sec { margin-top: 68px; }
  .sec h2 { font-size: 30px; margin: 6px 0 22px; }
  .badge { border-radius: 999px; background: color-mix(in srgb, var(--accent) 18%, transparent); color: var(--accent); font-weight: 700; }
"""),

"nightfall": dict(
    hero_accent="#F08A5D",   # reads on a photograph in either mode
    name="Nightfall",
    blurb="Cinematic and editorial. Near-black, one ember accent, a serif headline doing the work, "
          "square photography and hairline rules. Least like an admin panel.",
    modes=dict(
        dark=dict(bg="#08090C", bg2="#0C0D11", panel="#101116", ink="#F2F2F0",
                  muted="#9A9A95", accent="#E4572E", accent2="#F08A5D",
                  line="rgba(255,255,255,.10)", shadow="none", scrim="8,9,12"),
        light=dict(bg="#F4F2EE", bg2="#FFFFFF", panel="#FFFFFF", ink="#121212",
                   muted="#5E5E58", accent="#C03A14", accent2="#DD6336",
                   line="rgba(0,0,0,.13)", shadow="none", scrim="244,242,238"),
    ),
    css="""
  body { font-family: "Avenir Next", Avenir, system-ui, sans-serif; }
  h1, h2, h3, .display { font-family: Didot, "Playfair Display", Georgia, serif; font-weight: 400; letter-spacing: -.01em; }
  .kicker { font-size: 10px; font-weight: 600; letter-spacing: .34em; text-transform: uppercase; color: var(--muted); }
  .hero { height: 540px; text-align: center; }
  .hero .inner { align-items: center; }
  .hero h1 { font-size: 66px; line-height: 1.04; max-width: 15ch; }
  .hero p { font-size: 17px; max-width: 48ch; }
  .hero .rule { width: 56px; height: 1px; background: var(--accent); margin: 22px auto; }
  .btn { border-radius: 0; font-weight: 600; padding: 14px 26px; font-size: 11px;
         letter-spacing: .2em; text-transform: uppercase; }
  .btn-primary { background: var(--accent); color: #fff; }
  .btn-ghost { background: transparent; border: 1px solid var(--line); color: var(--ink); }
  .field { border-radius: 0; border: 0; border-bottom: 1px solid var(--line); background: transparent; }
  .card { border-radius: 0; border: 0; background: transparent; overflow: hidden; }
  .card .shot { height: 210px; }
  .card .body { padding: 16px 0 0; border-top: 1px solid var(--line); margin-top: 14px; }
  .card h3 { font-size: 21px; }
  .sec { margin-top: 92px; }
  .sec h2 { font-size: 38px; margin: 10px 0 30px; }
  .badge { border-radius: 0; background: transparent; color: var(--accent); font-weight: 600;
           letter-spacing: .14em; text-transform: uppercase; font-size: 9.5px !important; }
"""),

"signal": dict(
    hero_accent="#A98CFF",   # reads on a photograph in either mode
    name="Signal",
    blurb="A 2026 product site. Soft gradients and glow, pill buttons, big rounded cards and a "
          "floating glass search. The most obviously modern, the least spooky.",
    modes=dict(
        dark=dict(bg="#0C1017", bg2="#111722", panel="#151C28", ink="#E9EDF4",
                  muted="#98A3B4", accent="#7C5CFF", accent2="#22D3EE",
                  line="rgba(255,255,255,.09)", shadow="0 20px 45px rgba(0,0,0,.5)",
                  scrim="12,16,23"),
        light=dict(bg="#FFFFFF", bg2="#F4F7FC", panel="#FFFFFF", ink="#0E1726",
                   muted="#5A6679", accent="#5B3DF5", accent2="#0891A6",
                   line="rgba(14,23,38,.11)", shadow="0 14px 34px rgba(14,23,38,.10)",
                   scrim="255,255,255"),
    ),
    css="""
  body { font-family: "SF Pro Display", "Avenir Next", system-ui, sans-serif; }
  h1, h2, h3, .display { font-family: inherit; font-weight: 700; letter-spacing: -.028em; }
  .kicker { font-size: 11px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase;
            color: var(--accent); }
  .hero { height: 480px; }
  .hero h1 { font-size: 54px; line-height: 1.06; max-width: 13ch; }
  .hero p { font-size: 18px; max-width: 46ch; }
  .btn { border-radius: 999px; font-weight: 650; padding: 13px 26px; font-size: 15px; }
  .btn-primary { background: linear-gradient(120deg, var(--accent), var(--accent2)); color: #fff;
                 box-shadow: 0 8px 22px color-mix(in srgb, var(--accent) 45%, transparent); }
  .btn-ghost { background: color-mix(in srgb, var(--ink) 7%, transparent); border: 1px solid var(--line); color: var(--ink); }
  .field { border-radius: 999px; border: 1px solid var(--line); background: var(--panel); }
  .card { border-radius: 20px; border: 1px solid var(--line); background: var(--panel);
          box-shadow: var(--shadow); overflow: hidden; }
  .card .shot { height: 152px; }
  .card .body { padding: 16px 18px 20px; }
  .card h3 { font-size: 17px; }
  .sec { margin-top: 74px; }
  .sec h2 { font-size: 32px; margin: 8px 0 24px; }
  .badge { border-radius: 999px; font-weight: 650;
           background: color-mix(in srgb, var(--accent) 16%, transparent); color: var(--accent); }
  .glass { backdrop-filter: blur(18px); background: color-mix(in srgb, var(--panel) 72%, transparent); }
"""),
}


BASE_CSS = """
* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
html { background: var(--bg); }
body { margin: 0; padding: 0; background: var(--bg); color: var(--ink);
       min-height: 100vh; display: flex; flex-direction: column; }
.wrap-grow { flex: 1; }
img { display: block; }
a { color: inherit; text-decoration: none; }
.wrap { max-width: 1120px; margin: 0 auto; padding: 0 32px; }

/* the bar */
.bar { height: 68px; display: flex; align-items: center; justify-content: space-between;
       padding: 0 32px; border-bottom: 1px solid var(--line); background: var(--bg2);
       position: relative; z-index: 5; }
.bar .left { display: flex; align-items: center; gap: 34px; }
.logo { display: flex; align-items: center; gap: 10px; font-weight: 800; font-size: 17px; }
.logo img { width: 30px; height: 30px; }
.nav { display: flex; gap: 22px; font-size: 14px; color: var(--muted); }
.nav .on { color: var(--ink); font-weight: 650; }
.bar .right { display: flex; align-items: center; gap: 18px; color: var(--muted); }
.bar .right .signin { color: var(--ink); font-weight: 650; font-size: 14px; }

/* the hero */
.hero { position: relative; overflow: hidden; display: flex; align-items: flex-end; }
.hero .photo { position: absolute; inset: 0; background-size: cover; background-position: center 42%; }
.hero .photo::after { content: ""; position: absolute; inset: 0; background:
  linear-gradient(180deg, rgba(var(--scrim),.55) 0%, rgba(var(--scrim),0) 30%,
                          rgba(var(--scrim),.72) 72%, var(--bg) 100%); }

.hero .inner { position: relative; z-index: 2; width: 100%; max-width: 1120px; margin: 0 auto;
               padding: 0 32px 46px; display: flex; flex-direction: column; }
.hero h1 { margin: 10px 0 14px; }
.hero p { margin: 0 0 26px; color: var(--muted); line-height: 1.45; }
.search { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.field { display: flex; align-items: center; gap: 10px; padding: 12px 16px; min-width: 330px; color: var(--muted); font-size: 15px; }
.btn { border: 0; cursor: pointer; display: inline-flex; align-items: center; gap: 8px; }
.btns { display: flex; gap: 12px; margin-top: 16px; }

/* sections */
.sec h2 { line-height: 1.1; }
.sec .head { display: flex; align-items: baseline; justify-content: space-between; gap: 24px; }
.sec .head > div { flex: 1; }
.sec .more { font-size: 14px; color: var(--accent); font-weight: 650; white-space: nowrap; }
.grid3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 22px; }
.grid2 { display: grid; grid-template-columns: repeat(2, 1fr); gap: 22px; }
/* A grid stretches every card to the tallest in its row, and a block body leaves the slack at
   the bottom as an unexplained gap. Pin the bottom row on purpose instead. */
.card { display: flex; flex-direction: column; }
.card .body { flex: 1; display: flex; flex-direction: column; }
.card .foot { margin-top: auto; }
.card .shot { background-size: cover; background-position: center; position: relative; flex: none; }
.card h3 { margin: 0 0 5px; }
.card .meta { font-size: 13px; color: var(--muted); display: flex; align-items: center; gap: 6px; }
.card .foot { margin-top: 12px; display: flex; align-items: center; justify-content: space-between; }
.badge { font-size: 11px; padding: 4px 10px; display: inline-block; }
.card .shot .pin { position: absolute; top: 12px; left: 12px; }

/* the flyer strip, as it is on the page today */
.flyers { display: flex; flex-wrap: wrap; gap: 20px 26px; margin-top: 18px; }
.flyers a { width: 86px; text-align: center; }
.flyers .sheet { width: 64px; height: 83px; margin: 0 auto; border-radius: 3px;
  box-shadow: 0 2px 8px rgba(0,0,0,.35), 0 0 0 1px var(--line); object-fit: cover; }
.flyers span { display: block; margin-top: 9px; font-size: 11.5px; line-height: 1.25; color: var(--muted); }

/* footer */
.sitefoot { margin-top: auto; padding-top: 84px; border-top: 1px solid var(--line); background: var(--bg2); }
.sitefoot .wrap { display: flex; justify-content: space-between; padding-top: 26px; padding-bottom: 26px;
              font-size: 13px; color: var(--muted); }
.sitefoot .links { display: flex; gap: 26px; }

/* the label strip at the very top, so a screenshot says what it is */
/* Text over a PHOTOGRAPH is light in both modes. The light palettes put a dark amber kicker on a
   night photograph and it vanished entirely: a hero is a picture, not a surface, so it keeps its
   own ink and its own accent whichever mode the page is in — and gets a scrim behind the words
   rather than trusting the picture to be dark wherever the text happens to land. */
.hero .photo::before { content: ""; position: absolute; inset: 0; z-index: 1; background:
  linear-gradient(100deg, rgba(6,10,18,.80) 0%, rgba(6,10,18,.55) 40%, rgba(6,10,18,0) 74%); }
.hero.centred .photo::before { background: radial-gradient(72% 62% at 50% 44%,
  rgba(6,10,18,.76) 0%, rgba(6,10,18,.44) 56%, rgba(6,10,18,0) 100%); }
.hero h1 { color: #fff; }
.hero p { color: rgba(255,255,255,.87); }
.hero .kicker { color: var(--hero-accent); }
.hero .field { color: rgba(255,255,255,.8); border-color: rgba(255,255,255,.30);
               background: rgba(10,14,22,.48); }
.hero .btn-ghost { border-color: rgba(255,255,255,.42); color: #fff; background: rgba(10,14,22,.34); }

.tag { padding: 9px 32px; font-size: 12px; letter-spacing: .14em; text-transform: uppercase;
       font-weight: 700; background: var(--accent); color: var(--bg); }
"""


def page(key, mode):
    d = DIRECTIONS[key]
    t = d["modes"][mode]
    tokens = ";".join(f"--{k}:{v}" for k, v in t.items()) + f";--hero-accent:{d['hero_accent']}"

    places = "".join(f"""
      <a class="card">
        <div class="shot" style="background-image:url({STOCK}{img})"></div>
        <div class="body">
          <h3>{name}</h3>
          <div class="meta">{ic('pin', 14)} {where}</div>
          <div class="foot"><span class="badge">{kind}</span><span class="meta">{reports}</span></div>
        </div>
      </a>""" for name, where, img, reports, kind in PLACES)

    events = "".join(f"""
      <a class="card">
        <div class="shot" style="background-image:url({STOCK}{img})"></div>
        <div class="body">
          <h3>{name}</h3>
          <div class="meta">{ic('cal', 14)} {when} · {host}</div>
          <div class="foot"><span class="badge">{left}</span><span class="meta">Book a place →</span></div>
        </div>
      </a>""" for name, when, img, host, left in EVENTS)

    flyers = "".join(f'<a><img class="sheet" src="{THUMBS}{png}" alt=""><span>{who}</span></a>'
                     for who, png in FLYERS)
    nav = "".join(f'<span class="{"on" if n == "Home" else ""}">{n}</span>' for n in NAV)

    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>{d['name']} — {mode}</title>
<style>:root{{{tokens}}}{BASE_CSS}{d['css']}</style></head>
<body>
<div class="tag">{d['name']} · {mode} · mock-up, not the live site</div>

<div class="bar">
  <div class="left">
    <span class="logo"><img src="../assets/is-haunted-logo.svg" alt="">IsHaunted.com</span>
    <nav class="nav">{nav}</nav>
  </div>
  <div class="right">{ic('sun')}{ic('cart')}<span class="signin">Sign in</span></div>
</div>

<section class="hero{' centred' if key == 'nightfall' else ''}">
  <div class="photo" style="background-image:url({STOCK}i3-walking-to-house.jpg)"></div>
  <div class="inner">
    <div class="kicker">One home for the paranormal</div>
    <h1>Find out what happened here.</h1>
    {'<div class="rule"></div>' if key == 'nightfall' else ''}
    <p>Haunted places near you, the groups that investigate them, and the nights you can book —
       all in one place.</p>
    <div class="search">
      <div class="field{' glass' if key == 'signal' else ''}">{ic('search', 17)} Enter your city, address, or zip code</div>
      <button class="btn btn-primary">Find groups</button>
      <button class="btn btn-ghost">Request an investigation</button>
    </div>
  </div>
</section>

<div class="wrap wrap-grow">
  <section class="sec">
    <div class="head"><div><div class="kicker">Within 25 miles</div><h2>What's near you</h2></div>
      <span class="more">See the map →</span></div>
    <div class="grid3">{places}</div>
  </section>

  <section class="sec">
    <div class="head"><div><div class="kicker">This month</div><h2>What's on</h2></div>
      <span class="more">Every date →</span></div>
    <div class="grid2">{events}</div>
  </section>

  <section class="sec">
    <div class="kicker">Pass it on</div>
    <h2>Tell somebody what this is</h2>
    <div class="flyers">{flyers}</div>
  </section>
</div>

<footer class="sitefoot"><div class="wrap"><span>IsHaunted.com © 2026</span>
  <span class="links"><span>Help</span><span>What's new</span><span>Contact</span><span>Privacy</span><span>Terms</span></span>
</div></footer>
</body></html>"""


def main():
    out = []
    for key in DIRECTIONS:
        for mode in ("dark", "light"):
            path = os.path.join(HERE, f"{key}-{mode}.html")
            with open(path, "w", encoding="utf-8") as f:
                f.write(page(key, mode))
            out.append((key, mode, path))
            print("wrote", os.path.relpath(path, ROOT))

    if "--png" not in sys.argv:
        return 0
    if not os.path.exists(CHROME):
        print("Chrome not found; HTML written, screenshots skipped.")
        return 0

    prev = os.path.join(HERE, "preview")
    os.makedirs(prev, exist_ok=True)
    for n, (key, mode, path) in enumerate(out, 1):
        shot = os.path.join(prev, f"{n:02d}-{key}-{mode}.png")
        subprocess.run([CHROME, "--headless", f"--screenshot={shot}",
                        "--window-size=1280,2300", "--hide-scrollbars",
                        "--virtual-time-budget=4000", f"file://{path}"],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("wrote", os.path.relpath(shot, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
