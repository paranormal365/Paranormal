#!/usr/bin/env python3
"""Builds the one-page, front-and-back advertisements (item 235 phase 17e).

Each ad is one sheet of US Letter: a front that stops somebody walking past, and a back that answers what they ask
next. One layout, six audiences, each with its own palette chosen from its lead photograph:

  a deep base colour taken from the shadows of the photograph, and a complementary accent taken from its light —
  indigo and candle amber, night blue and lamplight, oxblood and champagne — so the headline's accent word and the
  call to action read as part of the picture rather than pasted on it.

Run from the repository root:

    python3 docs/ads/build-ads.py            # writes docs/ads/*.html and prints each to docs/ads/*.pdf
    python3 docs/ads/build-ads.py --png      # also photographs each page to docs/ads/preview/ for checking

Photographs: docs/media/stock (Unsplash License, credited in ProjectNotes/FeatureHistory/README-hosted-events-235-media.md).
Screens: the help captures and the phase 17d persona walk.
"""
import html, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

STOCK = "../media/stock/"
HELP_ADMIN = "../../Ben.Web.Services/Help/Media/"
HELP_PUBLIC = "../../Ben.Web.Website/wwwroot/help/media/"
WALK = "../media/hosted-events/walk/"
IOS = HELP_PUBLIC + "the-mobile-apps/"

ICONS = {
    "calendar": '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
    "ticket": '<path d="M3 8a2 2 0 0 0 2-2h14a2 2 0 0 0 2 2v2a2 2 0 0 0 0 4v2a2 2 0 0 0-2 2H5a2 2 0 0 0-2-2v-2a2 2 0 0 0 0-4z"/><path d="M13 6v12" stroke-dasharray="2 2"/>',
    "users": '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
    "door": '<path d="M13 4h3a2 2 0 0 1 2 2v14M2 20h20M13 20V3.5a1 1 0 0 0-1.2-1l-6 1.3A1 1 0 0 0 5 4.8V20"/><circle cx="10" cy="12" r=".6" fill="currentColor"/>',
    "mail": '<rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 6-10 7L2 6"/>',
    "phone": '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M11 18h2"/>',
    "pin": '<path d="M12 22s7-6.2 7-12a7 7 0 0 0-14 0c0 5.8 7 12 7 12z"/><circle cx="12" cy="10" r="2.5"/>',
    "camera": '<path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/>',
    "star": '<path d="m12 2 3.1 6.3 6.9 1-5 4.9 1.2 6.8L12 17.8 5.8 21l1.2-6.8-5-4.9 6.9-1z"/>',
    "shield": '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="m9 12 2 2 4-4"/>',
    "home": '<path d="m3 10 9-7 9 7v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M9 22V12h6v10"/>',
    "chart": '<path d="M3 3v18h18"/><path d="m7 15 4-4 3 3 5-6"/>',
    "wave": '<path d="M2 12h2l2-6 3 12 3-9 2 5 2-2h6"/>',
    "folder": '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>',
    "lock": '<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
    "globe": '<circle cx="12" cy="12" r="10"/><path d="M2 12h20M12 2a15 15 0 0 1 0 20M12 2a15 15 0 0 0 0 20"/>',
    "megaphone": '<path d="m3 11 15-6v14L3 13z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/>',
    "utensils": '<path d="M3 2v7a3 3 0 0 0 3 3v10M9 2v7M6 2v4M18 22V2c-2.5 1.5-4 4-4 8h4"/>',
    "lantern": '<path d="M9 3h6M12 3V1M8 6h8l1 3v9l-1 3H8l-1-3V9z"/><path d="M12 10c1.5 1.5 1.5 3.5 0 5-1.5-1.5-1.5-3.5 0-5z"/>',
    "moon": '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
    "sparkle": '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6"/>',
}


def icon(name, size=22):
    return (f'<svg class="ic" width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            f'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{ICONS[name]}</svg>')


CSS = """
@page { size: 8.5in 11in; margin: 0; }
* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
html, body { margin: 0; padding: 0; background: #555; }
body { font-family: "Avenir Next", Avenir, "Helvetica Neue", sans-serif; color: var(--ink); }
.page { width: 8.5in; height: 11in; position: relative; overflow: hidden; background: var(--base);
        page-break-after: always; break-after: page; }
.page:last-child { page-break-after: auto; break-after: auto; }
@media screen { .page { margin: 0 auto 24px; } }
body.only-front .back, body.only-back .front { display: none; }
body.only-front .page, body.only-back .page { margin: 0; }

/* ── front ─────────────────────────────────────────────────── */
.hero { position: absolute; inset: 0 0 auto 0; height: 7.05in; background-size: cover; background-position: var(--focus, center); }
.hero::after { content: ""; position: absolute; inset: 0;
  background: linear-gradient(180deg, rgba(0,0,0,.45) 0%, rgba(0,0,0,0) 22%, var(--base-a0) 42%, var(--base-a70) 72%, var(--base) 100%); }
.brandbar { position: absolute; top: .42in; left: .55in; right: .55in; display: flex; align-items: center; justify-content: space-between; z-index: 3; }
.brand { display: flex; align-items: center; gap: .12in; color: #fff; font-weight: 700; letter-spacing: .02em; font-size: 15pt; }
.brand img { width: .46in; height: .46in; filter: drop-shadow(0 2px 6px rgba(0,0,0,.5)); }
.brand small { display: block; font-weight: 500; font-size: 8pt; letter-spacing: .22em; text-transform: uppercase; opacity: .8; }
.pill { font-size: 8.5pt; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: var(--base);
        background: var(--accent); padding: .07in .16in; border-radius: 99px; box-shadow: 0 4px 14px rgba(0,0,0,.35); }
.headline { position: absolute; left: .55in; right: 3.1in; top: 4.05in; z-index: 3; color: #fff; }
.kicker { font-size: 9pt; font-weight: 700; letter-spacing: .24em; text-transform: uppercase; color: var(--accent); margin-bottom: .1in; }
.headline h1 { font-family: Futura, "Avenir Next", sans-serif; font-weight: 800; font-size: 40pt; line-height: 1.02;
               margin: 0 0 .16in; letter-spacing: -.01em; text-shadow: 0 3px 18px rgba(0,0,0,.45); }
.headline h1 em { font-style: normal; color: var(--accent); }
.headline p { font-size: 13.5pt; line-height: 1.38; margin: 0; color: rgba(255,255,255,.9); max-width: 4.6in; }
.phone { position: absolute; z-index: 4; right: .6in; top: 3.25in; width: 2.3in; height: 4.95in; border-radius: .34in;
         background: #050505; padding: .075in; box-shadow: 0 30px 60px rgba(0,0,0,.6), 0 0 0 1.5px rgba(255,255,255,.14), 0 0 60px var(--glow); transform: rotate(3deg); }
.phone img { width: 100%; height: 100%; object-fit: cover; object-position: top; border-radius: .27in; display: block; }
.pillars { position: absolute; left: .55in; right: 3.2in; top: 7.2in; display: grid; gap: .16in; z-index: 3; }
.pillar { display: grid; grid-template-columns: .5in 1fr; gap: .14in; align-items: start; }
.pillar .badge { width: .5in; height: .5in; border-radius: .14in; display: grid; place-items: center; color: var(--base);
                 background: linear-gradient(135deg, var(--accent), var(--accent2)); box-shadow: 0 6px 16px var(--glow); }
.pillar h3 { margin: .02in 0 .03in; font-size: 12.5pt; color: #fff; font-weight: 700; }
.pillar p { margin: 0; font-size: 9.6pt; line-height: 1.38; color: var(--muted); }
.cta { position: absolute; left: 0; right: 0; bottom: 0; height: 1.02in; display: flex; align-items: center; justify-content: space-between;
       padding: 0 .55in; background: linear-gradient(90deg, var(--accent), var(--accent2)); color: var(--base); z-index: 5; }
.cta .go { font-family: Futura, sans-serif; font-weight: 800; font-size: 19pt; letter-spacing: -.01em; }
.cta .go span { display: block; font-family: "Avenir Next", sans-serif; font-size: 9pt; font-weight: 600; letter-spacing: .12em; text-transform: uppercase; opacity: .78; }
.cta .price { text-align: right; font-weight: 700; font-size: 10pt; line-height: 1.25; }
.cta .price b { display: block; font-family: Futura, sans-serif; font-size: 22pt; font-weight: 800; }

/* ── back ──────────────────────────────────────────────────── */
.back { background: radial-gradient(120% 60% at 100% 0%, var(--base2) 0%, var(--base) 60%); color: #fff; }
.back-top { height: 2.55in; display: grid; grid-template-columns: 1.55fr 1fr 1fr; gap: .07in; }
.back-top div { background-size: cover; background-position: center; position: relative; }
.back-top div::after { content: ""; position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0,0,0,0) 45%, var(--base-a70) 100%); }
.back-head { position: absolute; top: 1.72in; left: .55in; right: .55in; z-index: 2; }
.back-head h2 { font-family: Futura, sans-serif; font-weight: 800; font-size: 25pt; margin: 0; line-height: 1.05; text-shadow: 0 3px 14px rgba(0,0,0,.6); }
.back-head h2 em { font-style: normal; color: var(--accent); }
.grid { position: absolute; top: 2.95in; left: .55in; right: .55in; display: grid; grid-template-columns: repeat(3, 1fr); gap: .14in; }
.card { background: var(--card); border: 1px solid var(--line); border-radius: .14in; padding: .15in .16in .14in; }
.card .ic { color: var(--accent); }
.card h4 { margin: .06in 0 .04in; font-size: 10.6pt; font-weight: 700; }
.card p { margin: 0; font-size: 8.5pt; line-height: 1.4; color: var(--muted); }
.shots { position: absolute; top: 6.05in; left: .55in; right: .55in; height: 2.45in; display: grid; grid-template-columns: 1.9fr 1fr; gap: .2in; align-items: stretch; }
.screen { border-radius: .12in; overflow: hidden; background: #111; box-shadow: 0 18px 40px rgba(0,0,0,.5), 0 0 0 1px rgba(255,255,255,.1); position: relative; }
.screen::before { content: ""; position: absolute; top: 0; left: 0; right: 0; height: .17in; background: #2a2a2e; z-index: 1; }
.screen::after { content: "● ● ●"; position: absolute; top: .012in; left: .1in; font-size: 6pt; letter-spacing: .02in; color: #6d6d74; z-index: 2; }
.screen img { position: absolute; top: .17in; left: 0; width: 100%; height: calc(100% - .17in); object-fit: cover; object-position: var(--shot-focus, top left); }
.side { display: flex; flex-direction: column; justify-content: center; gap: .1in; }
.side .quote { font-family: Didot, Georgia, serif; font-style: italic; font-size: 14pt; line-height: 1.25; color: #fff; }
.side .who { font-size: 8pt; letter-spacing: .14em; text-transform: uppercase; color: var(--accent); font-weight: 700; }
.steps { position: absolute; top: 8.72in; left: .55in; right: .55in; display: grid; grid-template-columns: repeat(3, 1fr); gap: .2in; }
.step { display: grid; grid-template-columns: .38in 1fr; gap: .08in; align-items: start; }
.step b { font-family: Futura, sans-serif; font-weight: 800; font-size: 20pt; color: var(--accent); line-height: 1; }
.step h5 { margin: 0 0 .02in; font-size: 10pt; }
.step p { margin: 0; font-size: 8.4pt; color: var(--muted); line-height: 1.35; }
.offer { position: absolute; left: .55in; right: .55in; bottom: .4in; height: 1.1in; border-radius: .16in; display: grid;
         grid-template-columns: 1.6fr 1fr; overflow: hidden; background: var(--card-solid); box-shadow: 0 0 0 1.5px var(--accent), 0 16px 40px rgba(0,0,0,.45); }
.offer > div:first-child { padding: .16in .28in; display: flex; flex-direction: column; justify-content: center; }
.offer .big { font-family: Futura, sans-serif; font-weight: 800; font-size: 16.5pt; color: #fff; line-height: 1.08; }
.offer .big em { font-style: normal; color: var(--accent); }
.offer .small { font-size: 8.4pt; color: var(--muted); margin-top: .05in; line-height: 1.35; }
.offer .url { display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center;
              color: var(--base); background: linear-gradient(135deg, var(--accent), var(--accent2)); }
.offer .url b { display: block; font-family: Futura, sans-serif; font-weight: 800; font-size: 18pt; }
.offer .url span { font-size: 8pt; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; margin-top: .02in; }
.fine { position: absolute; bottom: .14in; left: .55in; right: .55in; font-size: 5.8pt; white-space: nowrap; overflow: hidden; color: rgba(255,255,255,.42); text-align: center; }
"""


def rgba(hex_colour, alpha):
    h = hex_colour.lstrip("#")
    r, g, b = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    return f"rgba({r},{g},{b},{alpha})"


def page(ad):
    e = html.escape
    base = ad["base"]
    style = (f"--base:{base};--base2:{ad['base2']};--accent:{ad['accent']};--accent2:{ad['accent2']};"
             f"--base-a0:{rgba(base, 0)};--base-a70:{rgba(base, .72)};--glow:{rgba(ad['accent'], .28)};"
             f"--card:{rgba('#ffffff', .055)};--card-solid:{ad['base2']};--line:{rgba('#ffffff', .09)};--muted:rgba(255,255,255,.72);--ink:#fff;"
             f"--focus:{ad.get('focus', 'center')};")
    pillars = "".join(
        f'<div class="pillar"><div class="badge">{icon(i, 24)}</div><div><h3>{e(t)}</h3><p>{e(d)}</p></div></div>'
        for i, t, d in ad["pillars"])
    cards = "".join(f'<div class="card">{icon(i)}<h4>{e(t)}</h4><p>{e(d)}</p></div>' for i, t, d in ad["cards"])
    steps = "".join(
        f'<div class="step"><b>{n}</b><div><h5>{e(t)}</h5><p>{e(d)}</p></div></div>'
        for n, (t, d) in enumerate(ad["steps"], 1))
    tops = "".join(f'<div style="background-image:url({STOCK}{p})"></div>' for p in ad["strip"])
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>{e(ad['title'])}</title><style>{CSS}</style>
<script>document.addEventListener('DOMContentLoaded',()=>{{if(location.hash==='#front')document.body.classList.add('only-front');if(location.hash==='#back')document.body.classList.add('only-back');}});</script>
</head><body style="{style}">
<section class="page front">
  <div class="hero" style="background-image:url({STOCK}{ad['hero']})"></div>
  <div class="brandbar">
    <div class="brand"><img src="../assets/is-haunted-logo.svg" alt=""><div>IsHaunted.com<small>Web &amp; iPhone</small></div></div>
    <div class="pill">{e(ad['pill'])}</div>
  </div>
  <div class="headline">
    <div class="kicker">{e(ad['kicker'])}</div>
    <h1>{ad['headline']}</h1>
    <p>{e(ad['sub'])}</p>
  </div>
  <div class="phone"><img src="{ad['phone']}" alt=""></div>
  <div class="pillars">{pillars}</div>
  <div class="cta"><div class="go">{e(ad['cta'])}<span>{e(ad['cta_small'])}</span></div>
    <div class="price"><b>{e(ad['price'])}</b>{e(ad['price_small'])}</div></div>
</section>
<section class="page back">
  <div class="back-top">{tops}</div>
  <div class="back-head"><h2>{ad['back_headline']}</h2></div>
  <div class="grid">{cards}</div>
  <div class="shots">
    <div class="screen" style="--shot-focus:{ad.get('shot_focus', 'top left')}"><img src="{ad['screen']}" alt=""></div>
    <div class="side"><div class="quote">{e(ad['quote'])}</div><div class="who">{e(ad['quote_who'])}</div></div>
  </div>
  <div class="steps">{steps}</div>
  <div class="offer"><div><div class="big">{ad['offer']}</div><div class="small">{e(ad['offer_small'])}</div></div>
    <div class="url"><b>ishaunted.com</b><span>{e(ad['url_small'])}</span></div></div>
  <div class="fine">{e(ad['fine'])}</div>
</section>
</body></html>"""


FINE = "IsHaunted.com · Nashville, Tennessee · Photographs: Unsplash · Features shown are live on the website; the iPhone app is on its way to the App Store · Prices in USD, subject to change"

ADS = [
    dict(
        slug="IsHaunted", title="IsHaunted.com", pill="For everyone who wonders",
        base="#0B1624", base2="#16324A", accent="#F5A524", accent2="#FFCF73", hero="i3-walking-to-house.jpg", focus="center 40%",
        kicker="One home for the paranormal",
        headline="Where the unexplained gets <em>organized</em>.",
        sub="Explore haunted places, investigate with your group, run ghost walks and sell out haunted weekends — on the web and on your iPhone.",
        phone=IOS + "iphone-event-hub.png",
        pillars=[
            ("globe", "Explore, free", "The feed, a map of haunted places, and the events and ghost walks near you."),
            ("folder", "Investigate together", "Cases, evidence tools, a phone that records in the field, and reports clients can read."),
            ("ticket", "Host and grow", "Ghost walks, venues and hosted events — bookings, passes and the door, handled."),
        ],
        cta="Start free at ishaunted.com", cta_small="No card needed to join",
        price="Free", price_small="to join, explore and post",
        back_headline="One platform. <em>Every seat</em> at the table.",
        strip=["v6-ballroom.jpg", "w1-cobblestone-streetlights.jpg", "s1-candles.jpg"],
        cards=[
            ("wave", "Enthusiasts", "Field Kit turns a phone into a magnetometer, sound meter and recorder — no signal needed."),
            ("users", "Investigation groups", "Cases, members and roles, evidence review and privacy for private-home clients."),
            ("lantern", "Ghost walk tours", "A page per tour, dates people book, reminders with your guide, reviews that sell."),
            ("home", "Venues", "Tell your building's story, show your rooms, and say yes to who hosts there."),
            ("calendar", "Event hosts", "Rooms or seats, confirmations with passes, and check-in at the door."),
            ("phone", "The iPhone app", "Passes that open with no signal, the programme, the menus and the room."),
        ],
        screen=WALK + "45-public-event.png", shot_focus="top center",
        quote="“A weekend at a haunted hotel should feel like one — from the first email to the last photo.”",
        quote_who="Built for the people who run the night",
        steps=[("Join free", "Make an account in a minute, or look around without one."),
               ("Find your people", "Follow the feed, join a group or book an event near you."),
               ("Bring your night", "Start a group, list a tour, or host an event of your own.")],
        offer="Free to <em>explore</em>.",
        offer_small="Groups from $19.99 a month · ghost walks $29 a month per tour · hosted events $99 each",
        url_small="Web · iPhone coming soon", fine=FINE,
    ),
    dict(
        slug="Individuals", title="IsHaunted for enthusiasts", pill="For enthusiasts",
        base="#0A1C22", base2="#123B45", accent="#FF7A59", accent2="#FFB199", hero="i1-flashlight-silhouette.jpg", focus="center 30%",
        kicker="Free for the curious",
        headline="Your phone is the <em>instrument</em>.",
        sub="Record the magnetic field, the sound and the question you asked — then share what you found with people who get it.",
        phone=IOS + "iphone-meter.png",
        pillars=[
            ("wave", "Field Kit", "Magnetometer, sound level, audio with EVP marks and a sentry mode. Works with no signal."),
            ("camera", "The feed", "Post photos and video, follow investigators, and find the stories behind #hashtags."),
            ("pin", "Go and see", "Haunted places on a map, and events and ghost walks near you with passes on your phone."),
        ],
        cta="Join free at ishaunted.com", cta_small="Account, feed and Field Kit",
        price="$0", price_small="no card, no trial clock",
        back_headline="Everything a <em>night out</em> needs.",
        strip=["i2-dark-hallway.jpg", "w3-foggy-graveyard.jpg", "p1-phone-camera.jpg"],
        cards=[
            ("moon", "Record offline", "Sessions stay on your phone until you choose to upload — basement, cellar or field."),
            ("chart", "Replay it together", "Readings, photos and audio line up on one timeline, on the phone and the web."),
            ("pin", "Name the room", "Every capture carries the room you were in, not just a dot on a map."),
            ("users", "Join a group", "Find investigation groups by where you live, and ask to join in a tap."),
            ("ticket", "Book the night", "Ask for a room, pick your seats, and keep your pass for the door."),
            ("shield", "Yours, privately", "Location is stripped from what's shown. You decide what goes public."),
        ],
        screen=HELP_PUBLIC + "going-to-an-event/choosing-seats.png",
        quote="“The best evidence is the kind you can replay — the reading, the sound and the moment, together.”",
        quote_who="Field Kit, on every iPhone",
        steps=[("Sign up", "Free account with your own @name."),
               ("Record", "Open Field Kit and start a session — no signal needed."),
               ("Share", "Post to the feed or send it to the group you're out with.")],
        offer="Free. <em>Really.</em>",
        offer_small="The feed, Field Kit, events and 2 GB for your own files — no subscription for enthusiasts.",
        url_small="Web · iPhone coming soon", fine=FINE,
    ),
    dict(
        slug="Groups", title="IsHaunted for investigation groups", pill="For investigation groups",
        base="#1A0D24", base2="#3A1D4D", accent="#E9B949", accent2="#F7DC8C", hero="v4-hallway-chandelier.jpg", focus="center 55%",
        kicker="Run your team like a pro",
        headline="From first call to <em>final report</em>.",
        sub="Take requests, work cases, review evidence and publish findings — with the privacy a family's home deserves.",
        phone=IOS + "iphone-review.png",
        pillars=[
            ("folder", "Cases that stay organized", "Requests, timelines, investigations and findings, in one place per case."),
            ("wave", "Evidence tools built in", "EVP detection, spectrograms, a video editor, and Field Kit sessions cited in reports."),
            ("users", "Your people, your rules", "Roles and permissions, duties, member titles and equipment you lend."),
        ],
        cta="Start your group at ishaunted.com", cta_small="Public places stay free",
        price="$19.99", price_small="a month, 1–3 members",
        back_headline="The whole operation. <em>One login.</em>",
        strip=["i2-dark-hallway.jpg", "s2-ouija.jpg", "v5-hotel-bedroom.jpg"],
        cards=[
            ("lock", "Private-home privacy", "Write with real names; the public sees pseudonyms and a general area."),
            ("globe", "Your own website", "Pages, galleries and published cases under your group's name."),
            ("calendar", "Events and RSVPs", "A group calendar with sign-ups, reminders and evidence from guests."),
            ("megaphone", "Get found", "Discovery by location and group ads reviewed and served near viewers."),
            ("shield", "Every change recorded", "An audit trail, receipts and a ledger that never rewrites history."),
            ("phone", "In the field", "The iPhone app logs what happened, with photos, from the scene."),
        ],
        screen="../investor-media/org-hub.png",
        quote="“A client's home is not content. The privacy tools are the product.”",
        quote_who="Why groups pay",
        steps=[("Start a group", "Pick a name and invite your team."),
               ("Take a case", "Requests land in one queue with the client's details."),
               ("Publish safely", "Share findings with redaction done for you.")],
        offer="Plans <em>from $19.99</em> a month.",
        offer_small="Pay yearly and get two months free. Investigations of public places stay free on every plan.",
        url_small="Start today", fine=FINE,
    ),
    dict(
        slug="Ghost-Walks", title="IsHaunted for ghost walk tours", pill="For ghost walk tours",
        base="#08142A", base2="#17305A", accent="#FFB547", accent2="#FFDB8E", hero="w1-cobblestone-streetlights.jpg", focus="center 35%",
        kicker="Tours that book themselves",
        headline="Fill every <em>lantern-lit</em> walk.",
        sub="A page for each tour, dates people book in a tap, and a reminder with your guide's face the night before.",
        phone=WALK + "42-public-tour-phone.png",
        pillars=[
            ("pin", "A page per tour", "Meeting point on a map, who's guiding, photos, dates and reviews."),
            ("mail", "Emails that sell the night", "Written once in your voice: time, place, guide and a calendar file."),
            ("camera", "Guests become marketing", "Last night's best photo in your slideshow by breakfast — you approve it."),
        ],
        cta="List your tour at ishaunted.com", cta_small="Unlimited dates and guides",
        price="$29", price_small="a month per tour",
        back_headline="More walkers. <em>Fewer</em> no-shows.",
        strip=["w2-cobblestone-night.jpg", "w3-foggy-graveyard.jpg", "i1-flashlight-silhouette.jpg"],
        cards=[
            ("calendar", "Dates and capacity", "Open a date, cap it, and close it when the street is full."),
            ("users", "Walk-ups welcome", "Sign somebody up on the pavement — no account needed."),
            ("star", "Reviews that count", "Only people who came can rate the walk."),
            ("ticket", "Seat plans", "Trolleys and theatres get a seat map guests pick from."),
            ("globe", "Found by visitors", "Tours show on the home map and in “near you” searches."),
            ("phone", "Free app for guests", "Their phone becomes an instrument on your walk."),
        ],
        screen=WALK + "41-public-tour.png", shot_focus="top center",
        quote="“One extra ticket a month pays for the listing.”",
        quote_who="Priced per tour, not per guide",
        steps=[("Create your tour", "Choose ghost walking tour when you start your group."),
               ("Open dates", "Run it twice a year or four nights a week."),
               ("Welcome guests", "They arrive knowing where, when and who.")],
        offer="<em>$29</em> a month per tour.",
        offer_small="Or $290 a year. Every guide and every date included — ask about three months free for new tour businesses.",
        url_small="Your page is public from day one", fine=FINE,
    ),
    dict(
        slug="Venues", title="IsHaunted for venues", pill="For haunted venues",
        base="#240A10", base2="#4A1622", accent="#E8C987", accent2="#F6E3B4", hero="v6-ballroom.jpg", focus="center 50%",
        kicker="Hotels · theatres · historic homes",
        headline="Your building has a <em>story</em>. Book it.",
        sub="Show the history, the rooms and the house rules — and decide which organizers may host events under your roof.",
        phone=WALK + "44-public-venue-phone.png",
        pillars=[
            ("home", "A venue page worth sharing", "History, house rules, a photo library and every event coming up."),
            ("shield", "You say who hosts", "Organizers ask first. Approve the nights, lend your rooms, photos and staff."),
            ("door", "Rooms, seats and the door", "Describe what each room sleeps; the plan, bookings and check-in follow."),
        ],
        cta="Claim your venue at ishaunted.com", cta_small="Confirmed as the venue for your building",
        price="Free", price_small="venue page for your group",
        back_headline="Weekends that <em>sell out</em>. On your terms.",
        strip=["v1-venue-exterior.jpg", "v7-hotel-bar.jpg", "d1-candlelit-dinner.jpg"],
        cards=[
            ("lock", "Claim the building", "Confirm you run the place, so nobody publishes there without you."),
            ("calendar", "Host your own", "Séance weekends, dinners and hunts under your own name."),
            ("utensils", "Menus and dining", "Meals by night, dietary sheets for the kitchen, tables by party."),
            ("camera", "Photos offered to you", "Organizers offer their best shots; you choose what joins your page."),
            ("users", "Lend your staff", "Your people can run the door at events you've approved."),
            ("star", "Withdraw fairly", "Pull out with a reason and the organizer's credit goes back."),
        ],
        screen=WALK + "43-public-venue.png", shot_focus="top center",
        quote="“The Thomas House has had guests on the second-floor landing since the 1970s. Now they book.”",
        quote_who="Demo venue, the Thomas House Hotel",
        steps=[("Describe the place", "History, rooms and what each one sleeps."),
               ("Approve organizers", "Say yes to the nights and what you'll lend."),
               ("Open the doors", "Guests arrive with a pass; your staff check them in.")],
        offer="Your venue page, <em>free</em>.",
        offer_small="Publish your own event with a $99 credit, good for a year — or host as many as you like on a business plan.",
        url_small="Claim your building", fine=FINE,
    ),
    dict(
        slug="Event-Hosts", title="IsHaunted Hosted Events", pill="For event hosts",
        base="#150B22", base2="#35194D", accent="#F7B84B", accent2="#FFDD9A", hero="s3-candle-chandelier.jpg", focus="center 30%",
        kicker="Hosted events · web + iPhone",
        headline="Sell out the <em>séance</em>. Skip the spreadsheet.",
        sub="Rooms or seats, confirmations with passes, and a door that opens with no signal — for haunted weekends of every size.",
        phone=IOS + "iphone-event-pass.png",
        pillars=[
            ("ticket", "Rooms or seats", "Draw the plan once. Guests ask for a room or pick their seats; holds lapse on their own."),
            ("mail", "Every letter, written for you", "Asked, held, confirmed with a QR pass, reminders — and one note to all guests."),
            ("door", "The night itself", "Check in by scan or name, menus and dietary sheets, a programme and a photo room."),
        ],
        cta="Host your event at ishaunted.com", cta_small="Guests pay you directly — no ticket fees",
        price="$99", price_small="per event, however many nights",
        back_headline="Everything a <em>haunted weekend</em> needs.",
        strip=["d1-candlelit-dinner.jpg", "v6-ballroom.jpg", "t1-theatre-seats.jpg"],
        cards=[
            ("calendar", "The booking board", "Who's waiting first, with the clock on every hold. Confirm from your phone."),
            ("users", "Helpers, not admins", "Invite a steward to run the door without handing over the keys."),
            ("utensils", "Menus and the kitchen", "Meals by night, dietary notes tallied, tables seated by party."),
            ("phone", "Passes on the iPhone", "The pass, programme, menus and downloads — offline when the cellar is."),
            ("camera", "The room and the wall", "Guests post photos; you approve what the public sees."),
            ("chart", "At a glance", "Coming, waiting, places left, arrivals and reviews on one card."),
        ],
        screen=WALK + "10-organizer-writing-to-guests.png", shot_focus="top left",
        quote="“The car park moved. Everybody knew by lunchtime.”",
        quote_who="Write to every guest at once",
        steps=[("Create the event", "Nights, venue, rooms or seats — a draft costs nothing."),
               ("Publish", "One credit, or your business plan. It's live in minutes."),
               ("Open the door", "Scan passes, even with no signal, from any phone.")],
        offer="<em>$99</em> an event.",
        offer_small="However many nights. No ticket fees: guests pay you. Credits last a year and come back if you cancel 48+ hours ahead.",
        url_small="iPhone app coming soon", fine=FINE,
    ),
]


# ── the Hosted Events brochure ───────────────────────────────────────────────

BROCHURE_CSS = """
@page { size: 8.5in 11in; margin: 0; }
* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
html, body { margin: 0; background: #444; }
body { font-family: "Avenir Next", Avenir, "Helvetica Neue", sans-serif; color: #fff;
       --base: #150B22; --base2: #2E1745; --accent: #F7B84B; --accent2: #FFDD9A; --muted: rgba(255,255,255,.74);
       --card: rgba(255,255,255,.055); --line: rgba(255,255,255,.1); }
.page { width: 8.5in; height: 11in; position: relative; overflow: hidden; page-break-after: always; break-after: page;
        background: radial-gradient(110% 55% at 100% 0%, var(--base2) 0%, var(--base) 62%); }
.page:last-child { page-break-after: auto; break-after: auto; }
@media screen { .page { margin: 0 auto 24px; } }
body.one .page { display: none; margin: 0; }
body.one .page.show { display: block; }
.pad { position: absolute; inset: .6in .6in .55in; }
.kicker { font-size: 8.5pt; font-weight: 700; letter-spacing: .24em; text-transform: uppercase; color: var(--accent); }
h1, h2 { font-family: Futura, "Avenir Next", sans-serif; font-weight: 800; margin: 0; letter-spacing: -.01em; }
h1 em, h2 em { font-style: normal; color: var(--accent); }
h2 { font-size: 27pt; line-height: 1.04; margin: .06in 0 .1in; }
.lede { font-size: 11.5pt; line-height: 1.45; color: var(--muted); max-width: 6.2in; margin: 0; }
.folio { position: absolute; bottom: .28in; left: .6in; right: .6in; display: flex; justify-content: space-between;
         font-size: 7.5pt; letter-spacing: .14em; text-transform: uppercase; color: rgba(255,255,255,.42); }
.shot { border-radius: .1in; overflow: hidden; background: #0d0d10; box-shadow: 0 16px 36px rgba(0,0,0,.5), 0 0 0 1px rgba(255,255,255,.1); }
.shot img { display: block; width: 100%; height: auto; }
.laptop { border-radius: .12in; overflow: hidden; background: #0d0d10; box-shadow: 0 22px 50px rgba(0,0,0,.55), 0 0 0 1px rgba(255,255,255,.12); position: relative; }
.laptop::before { content: "●  ●  ●"; display: block; height: .19in; line-height: .19in; padding-left: .1in; font-size: 6pt; color: #6b6b73; background: #26262c; }
.laptop img { display: block; width: 100%; height: auto; }
.phone { border-radius: .3in; background: #040404; padding: .065in; box-shadow: 0 24px 50px rgba(0,0,0,.6), 0 0 0 1.4px rgba(255,255,255,.14), 0 0 50px rgba(247,184,75,.18); }
.phone img { display: block; width: 100%; height: auto; border-radius: .24in; }
.cap { display: grid; grid-template-columns: .3in 1fr; gap: .08in; align-items: start; }
.cap b { width: .3in; height: .3in; border-radius: 50%; display: grid; place-items: center; font-size: 10pt; color: var(--base);
         background: linear-gradient(135deg, var(--accent), var(--accent2)); font-family: Futura, sans-serif; }
.cap h4 { margin: .02in 0 .02in; font-size: 10.5pt; }
.cap p { margin: 0; font-size: 8.6pt; line-height: 1.4; color: var(--muted); }
.abs { position: absolute; }
.note { border-left: 3px solid var(--accent); background: var(--card); padding: .1in .14in; border-radius: 0 .08in .08in 0; font-size: 9pt; line-height: 1.42; color: var(--muted); }
.note strong { color: #fff; }
.person { background: var(--card); border: 1px solid var(--line); border-radius: .16in; overflow: hidden; display: grid; grid-template-columns: 1.55in 1fr; }
.person .ph { background-size: cover; background-position: center; min-height: 1.28in; position: relative; }
.person .ph::after { content: ""; position: absolute; inset: 0; background: linear-gradient(90deg, rgba(0,0,0,0) 55%, var(--base) 100%); opacity: .55; }
.person .tx { padding: .14in .18in; }
.person h3 { margin: .03in 0 .04in; font-size: 13pt; }
.person p { margin: 0; font-size: 8.8pt; line-height: 1.42; color: var(--muted); }
.person .role { font-size: 7.5pt; font-weight: 700; letter-spacing: .18em; text-transform: uppercase; color: var(--accent); }
.mail { border-radius: .08in; overflow: hidden; background: #fff; box-shadow: 0 12px 30px rgba(0,0,0,.45); }
.mail img { display: block; width: 100%; height: auto; }
.mails { display: grid; grid-template-columns: repeat(3, 1fr); gap: .22in .2in; margin: .14in 0 .3in; align-items: start; }
.mails .mail { height: 2.3in; position: relative; }
.mails .mail img { height: 100%; object-fit: cover; object-position: top left; }
.mails .mail::after { content: ""; position: absolute; left: 0; right: 0; bottom: 0; height: .45in; background: linear-gradient(180deg, rgba(255,255,255,0), #fff); }
.pad { display: flex; flex-direction: column; gap: .17in; }
.pad > h2 { margin-bottom: 0; }
.row { display: grid; gap: .22in; align-items: start; }
.stack { display: flex; flex-direction: column; gap: .14in; }
.mailcap { font-size: 7.8pt; color: var(--muted); margin-top: .05in; line-height: 1.3; }
.mailcap strong { color: #fff; display: block; font-size: 8.4pt; }
.price { margin-top: auto; border-radius: .18in; overflow: hidden; display: grid; grid-template-columns: 1.4fr 1fr; background: var(--base2); box-shadow: 0 0 0 1.5px var(--accent), 0 18px 40px rgba(0,0,0,.45); }
.price > div:first-child { padding: .2in .26in; }
.price .big { font-family: Futura, sans-serif; font-weight: 800; font-size: 30pt; line-height: 1; }
.price .big em { font-style: normal; color: var(--accent); }
.price ul { margin: .1in 0 0; padding-left: 1.1em; font-size: 9pt; line-height: 1.5; color: var(--muted); }
.price .go { display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center; color: var(--base);
             background: linear-gradient(135deg, var(--accent), var(--accent2)); padding: .2in; }
.price .go b { font-family: Futura, sans-serif; font-size: 20pt; font-weight: 800; }
.price .go span { font-size: 8.5pt; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; margin-top: .04in; }
.price .go .soon { opacity: .75; margin-top: .12in; }
/* cover */
.cover-hero { position: absolute; inset: 0 0 auto 0; height: 7.3in; background-size: cover; background-position: center 35%; }
.cover-hero::after { content: ""; position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0,0,0,.5) 0%, rgba(0,0,0,.05) 25%, rgba(21,11,34,.35) 50%, rgba(21,11,34,.85) 78%, var(--base) 100%); }
.brand { position: absolute; top: .45in; left: .6in; display: flex; gap: .12in; align-items: center; font-weight: 700; font-size: 14pt; z-index: 2; }
.brand img { width: .44in; height: .44in; }
.brand small { display: block; font-size: 7.5pt; letter-spacing: .22em; text-transform: uppercase; font-weight: 600; opacity: .8; }
.cover-title { position: absolute; left: .6in; right: .6in; top: 2.35in; z-index: 2; }
.cover-title h1 { font-size: 44pt; line-height: 1; margin: .1in 0 .16in; text-shadow: 0 4px 20px rgba(0,0,0,.5); }
.cover-devices { position: absolute; left: 1.2in; right: .55in; top: 5.55in; height: 3.6in; z-index: 3; }
.cover-title p { font-size: 13.5pt; line-height: 1.4; color: rgba(255,255,255,.9); max-width: 4.9in; margin: 0; }
.facts { position: absolute; left: .6in; right: .6in; bottom: .5in; z-index: 4; display: grid; grid-template-columns: repeat(3, 1fr); gap: .16in; }
.fact { background: rgba(21,11,34,.82); backdrop-filter: blur(4px); border: 1px solid var(--line); border-radius: .14in; padding: .14in .16in; }
.fact b { display: block; font-family: Futura, sans-serif; font-size: 14pt; color: var(--accent); margin-bottom: .03in; }
.fact span { font-size: 8.6pt; color: var(--muted); line-height: 1.35; }
"""


def _frame(cls, src, width=None, extra=""):
    style = f"width:{width}in;" if width else ""
    return f'<div class="{cls}" style="{style}{extra}"><img src="{src}" alt=""></div>'


def _cap(n, title, text):
    return f'<div class="cap"><b>{n}</b><div><h4>{html.escape(title)}</h4><p>{html.escape(text)}</p></div></div>'


def _mail(src, title, text):
    return (f'<div><div class="mail"><img src="{src}" alt=""></div>'
            f'<div class="mailcap"><strong>{html.escape(title)}</strong>{html.escape(text)}</div></div>')


def brochure(mails):
    W, S, I = WALK, STOCK, IOS
    folio = lambda n: f'<div class="folio"><span>IsHaunted.com · Hosted Events</span><span>{n}</span></div>'
    head = lambda kicker, title, lede=None: (f'<div class="kicker">{kicker}</div><h2>{title}</h2>'
                                            + (f'<p class="lede">{html.escape(lede)}</p>' if lede else ""))
    pages = []

    pages.append(f"""<section class="page cover">
  <div class="cover-hero" style="background-image:url({S}s3-candle-chandelier.jpg)"></div>
  <div class="brand"><img src="../assets/is-haunted-logo.svg" alt=""><div>IsHaunted.com<small>Web &amp; iPhone</small></div></div>
  <div class="cover-title"><div class="kicker">Hosted Events</div>
    <h1>Haunted weekends,<br><em>sold out</em> and run<br>from your phone.</h1>
    <p>Rooms or seats, confirmations with passes, a door that works with no signal, and every letter written for you.</p></div>
  <div class="cover-devices">
    {_frame('laptop', W + '45-public-event.png', 4.7, 'position:absolute;left:0;top:.55in;transform:rotate(-2deg)')}
    {_frame('phone', W + '16-guest-pass-phone.png', 1.62, 'position:absolute;right:.05in;top:0;transform:rotate(4deg)')}
  </div>
  <div class="facts">
    <div class="fact"><b>Rooms or seats</b><span>Guests ask for a room or pick their seats on your plan.</span></div>
    <div class="fact"><b>No signal, no problem</b><span>Passes and the door keep working in the cellar.</span></div>
    <div class="fact"><b>$99 an event</b><span>However many nights. Guests pay you directly.</span></div>
  </div>
</section>""")

    people = [
        ("d1-candlelit-dinner.jpg", "The guest", "Finds it, asks, gets a pass",
         "Browses the event with its photographs and access notes, asks for a room or picks seats, and keeps a QR pass on the web and the iPhone."),
        ("v3-hotel-lobby.jpg", "The organizer", "Runs the weekend",
         "Confirms parties into rooms, writes to everybody at once, invites helpers, and sees the numbers at a glance."),
        ("i1-flashlight-silhouette.jpg", "The helper on the door", "Lets people in",
         "Accepts an invitation that grants the door and nothing else, then checks parties in by scan or by name."),
        ("v1-venue-exterior.jpg", "The venue", "Says who hosts there",
         "Shows the building's history and photographs, and approves the organizers who want to run nights under its roof."),
        ("p1-phone-camera.jpg", "IsHaunted", "Keeps it trustworthy",
         "Watches every event on one dashboard, removes what breaks the rules, returns the credit, and hears appeals."),
    ]
    cards = "".join(
        f'<div class="person"><div class="ph" style="background-image:url({S}{img})"></div><div class="tx"><div class="role">{html.escape(role)}</div>'
        f'<h3>{html.escape(t)}</h3><p>{html.escape(d)}</p></div></div>' for img, t, role, d in people)
    pages.append(f"""<section class="page"><div class="pad">
  {head('How it works', 'Five people.<br>One <em>unforgettable</em> weekend.', "Every hosted event has the same cast. IsHaunted gives each of them exactly the screen they need, and nothing they don't.")}
  <div class="stack" style="margin-top:.12in;gap:.12in">{cards}</div>
</div>{folio(2)}</section>""")

    pages.append(f"""<section class="page"><div class="pad">
  {head('The guest', 'From “that looks amazing”<br>to <em>a pass in their pocket</em>.')}
  <div class="row" style="grid-template-columns:4.1in 1fr">
    {_frame('laptop', W + '02-guest-event-page.png')}
    <div class="stack">{_cap(1, 'The event page', "Photographs from the venue, the nights, and what's left.")}
      {_cap(2, 'Getting in and around', 'Stairs, parking and low light, said before anybody books.')}</div>
  </div>
  {_frame('shot', W + '03-guest-getting-in.png')}
  <div class="row" style="grid-template-columns:3.55in 1fr">
    {_frame('shot', W + '04-guest-asking-for-a-room.png')}
    <div class="stack">{_frame('shot', W + '05-guest-asked.png')}
      {_cap(3, 'Ask for a room', 'Nights, party size and a note. Nothing is held or paid here.')}
      {_cap(4, 'Told straight away', 'The booking card says it is with the venue, and so does the letter.')}
      <div class="note"><strong>Guests pay you, not us.</strong> Prices are shown so nobody is surprised; money is settled between the guest and the venue.</div></div>
  </div>
  <div class="row" style="grid-template-columns:1.55in 1fr;align-items:center">
    {_frame('shot', W + '15-guest-pass.png')}
    {_cap(5, 'A pass for the door', "The QR pass arrives with the confirmation, waits under What I'm going to, and opens on the iPhone with no signal.")}
  </div>
</div>{folio(3)}</section>""")

    pages.append(f"""<section class="page"><div class="pad">
  {head('The organizer', 'The whole weekend<br>on <em>one board</em>.')}
  {_frame('shot', W + '06-organizer-event-at-a-glance.png')}
  {_cap(1, 'At a glance', 'People coming, parties waiting, places left night by night, arrivals and reviews.')}
  <div class="row" style="grid-template-columns:4.2in 1fr">
    {_frame('laptop', W + '08-organizer-confirming-a-party.png')}
    <div class="stack">{_cap(2, 'Put them where it suits', 'Confirm a party into the Blue Room with a line they will read. The pass goes with it.')}
      {_cap(3, 'Waiting on you first', 'Requests and holds sit at the top, soonest deadline first.')}
      {_cap(4, 'Helpers, not admins', 'Invite somebody to run the door without handing over the guest list.')}</div>
  </div>
  <div class="row" style="grid-template-columns:3.6in 1fr">
    {_frame('shot', W + '10-organizer-writing-to-guests.png')}
    <div class="stack">{_cap(5, 'Write to everybody', 'Parking moved? Doors at eight? One letter by email and in their messages, with the count shown before you send.')}
      {_frame('mail', MAIL + 'thomas-house-s-ance-weekend-parking-for-friday-night.png')}</div>
  </div>
</div>{folio(4)}</section>""")

    pages.append(f"""<section class="page"><div class="pad">
  {head('The night itself', 'A door that opens<br><em>with no signal</em>.', "Old buildings eat phone signal. The pass, the door list and check-in keep working in the cellar, and catch up when the bars come back.")}
  <div class="row" style="grid-template-columns:repeat(3,1fr);justify-items:center;margin-top:.1in">
    <div class="stack">{_frame('phone', W + '18-door-on-a-phone.png', 1.95)}{_cap(1, 'The list for tonight', 'Who is expected, who is in, and walk-ups added on the spot.')}</div>
    <div class="stack">{_frame('phone', W + '19-door-finding-a-party.png', 1.95)}{_cap(2, 'Find them by name', 'No pass to hand? Type part of a name and check the party in.')}</div>
    <div class="stack">{_frame('phone', I + 'iphone-door-scanned.png', 1.95)}{_cap(3, 'Or scan on the iPhone', 'The camera reads the pass; tap the reservation to check them in.')}</div>
  </div>
  <div class="row" style="grid-template-columns:3.9in 1fr;align-items:center">
    {_frame('shot', W + '13-organizer-kitchen.png')}
    <div class="stack">{_cap(4, 'What the kitchen needs', 'Dietary notes tallied for each night and printed a page per night, so the kitchen never scrolls a phone.')}
      <div class="note"><strong>Offline on the iPhone.</strong> Arrivals recorded with no signal keep the time they happened and send when the phone reconnects.</div></div>
  </div>
</div>{folio(5)}</section>""")

    pages.append(f"""<section class="page"><div class="pad">
  {head('Seats and strangers', 'Two hundred seats.<br><em>No account</em> needed.', "For an evening in the ballroom, guests pick their own seats on your plan. Somebody without an account picks too, and we email them a link to hold the seats.")}
  <div class="row" style="grid-template-columns:4.6in 1fr">
    {_frame('shot', W + '21-stranger-picking-seats.png')}
    <div class="stack">{_frame('phone', W + '48-public-evening-phone.png', 1.7)}</div>
  </div>
  <div class="row" style="grid-template-columns:1fr 1fr">
    {_cap(1, 'Pick on the plan', 'Sections and prices in a legend; taken and held seats cannot be picked.')}
    {_cap(2, 'On a phone, too', 'The plan scrolls in its own frame with a summary that stays in view.')}
  </div>
  <div class="row" style="grid-template-columns:2.6in 1fr;align-items:center">
    {_frame('mail', MAIL + 'hold-your-places-at-an-evening-of-evidence-within-15-minutes.png')}
    {_cap(3, 'A hold that lapses by itself', "Picked seats are held for the venue's chosen time, shown to everybody else as pending, and let go if nobody confirms.")}
  </div>
</div>{folio(6)}</section>""")

    pages.append(f"""<section class="page"><div class="pad">
  {head('Venues, and the people who run IsHaunted', 'The building says yes.<br>We keep it <em>honest</em>.')}
  <div class="row" style="grid-template-columns:5.2in 1fr">
    {_frame('laptop', W + '43-public-venue.png')}
    <div class="stack">{_cap(1, 'A venue page worth sharing', "History, house rules, a photo library and what's on next.")}
      {_cap(2, 'Nobody hosts without the venue', 'Organizers ask first; the venue approves the nights and what it lends.')}</div>
  </div>
  {_frame('shot', W + '25-superadmin-events-dashboard.png')}
  <div class="row" style="grid-template-columns:1fr 1fr">
    {_frame('shot', W + '29-organizer-appealing.png')}
    <div class="stack">{_cap(3, 'Removed, fairly', 'If an event breaks the rules it comes off the site, guests are told, and the credit goes back.')}
      {_cap(4, 'And a person reads the appeal', 'Upheld appeals bring the event back as a draft, ready to publish again.')}</div>
  </div>
</div>{folio(7)}</section>""")

    pages.append(f"""<section class="page"><div class="pad">
  {head('Every letter, written for you', 'Nobody is left <em>wondering</em>.')}
  <div class="mails">{''.join(_mail(src, t, d) for src, t, d in mails)}</div>
  <div class="price"><div>
    <div class="big"><em>$99</em> an event</div>
    <ul><li>However many nights: one credit, good for a year</li><li>The credit comes back if you call it off 48+ hours ahead</li>
    <li>Business plans host as many events as they like</li><li>Guests pay you directly, with no ticket fees</li></ul></div>
    <div class="go"><b>ishaunted.com</b><span>Host your first event</span><span class="soon">iPhone app coming soon</span></div></div>
</div>{folio(8)}</section>""")

    return f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><title>IsHaunted Hosted Events</title><style>{BROCHURE_CSS}</style>
<script>document.addEventListener('DOMContentLoaded',()=>{{const m=location.hash.match(/^#p(\d+)$/);if(m){{document.body.classList.add('one');document.querySelectorAll('.page')[+m[1]-1]?.classList.add('show');}}}});</script>
</head><body>{''.join(pages)}</body></html>"""


def build_brochure(want_png):
    page_html = os.path.join(HERE, "hosted-events-brochure.html")
    with open(page_html, "w") as f:
        f.write(brochure(MAILS))
    pdf = os.path.join(ROOT, "docs", "IsHaunted-Hosted-Events.pdf")
    subprocess.run([CHROME, "--headless", "--no-pdf-header-footer", "--allow-file-access-from-files",
                    f"--print-to-pdf={pdf}", "file://" + page_html], capture_output=True, timeout=180)
    print("wrote", os.path.relpath(pdf, ROOT))
    if want_png:
        for n in range(1, 9):
            png = os.path.join(HERE, "preview", f"brochure-p{n}.png")
            subprocess.run([CHROME, "--headless", "--hide-scrollbars", "--allow-file-access-from-files",
                            "--window-size=816,1056", "--force-device-scale-factor=1.5",
                            f"--screenshot={png}", f"file://{page_html}#p{n}"], capture_output=True, timeout=120)


def main():
    want_png = "--png" in sys.argv
    if "--brochure-only" in sys.argv:
        build_brochure(want_png)
        return
    build_brochure(want_png)
    os.makedirs(os.path.join(HERE, "preview"), exist_ok=True)
    for ad in ADS:
        page_html = os.path.join(HERE, f"ad-{ad['slug'].lower()}.html")
        with open(page_html, "w") as f:
            f.write(page(ad))
        pdf = os.path.join(HERE, f"IsHaunted-Ad-{ad['slug']}.pdf")
        subprocess.run([CHROME, "--headless", "--no-pdf-header-footer", "--allow-file-access-from-files",
                        f"--print-to-pdf={pdf}", "file://" + page_html], capture_output=True, timeout=120)
        print("wrote", os.path.relpath(pdf, ROOT))
        if want_png:
            for side in ("front", "back"):
                png = os.path.join(HERE, "preview", f"{ad['slug'].lower()}-{side}.png")
                subprocess.run([CHROME, "--headless", "--hide-scrollbars", "--allow-file-access-from-files",
                                "--window-size=816,1056", "--force-device-scale-factor=1.5",
                                f"--screenshot={png}", f"file://{page_html}#{side}"], capture_output=True, timeout=120)


MAIL = "../media/hosted-events/emails/"
MAILS = [
    (MAIL + "we-ve-passed-your-request-for-thomas-house-s-ance-weekend-on.png", "Your request is with them", "Sent the moment a guest asks."),
    (MAIL + "your-place-at-thomas-house-s-ance-weekend-is-confirmed.png", "You're confirmed", "With the rooms, access notes and the QR pass."),
    (MAIL + "hold-your-places-at-an-evening-of-evidence-within-15-minutes.png", "Hold your seats", "The link for somebody without an account."),
    (MAIL + "thomas-house-s-ance-weekend-parking-for-friday-night.png", "A note to every guest", "Written once on the booking board."),
    (MAIL + "can-you-help-at-thomas-house-s-ance-weekend.png", "Can you help?", "A helper's invitation to run the door."),
    (MAIL + "a-party-of-2-asked-for-a-place-at-thomas-house-s-ance-weeken.png", "Somebody asked", "The organizer hears about every request."),
]


if __name__ == "__main__":
    main()
