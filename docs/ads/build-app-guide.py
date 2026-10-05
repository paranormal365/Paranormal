#!/usr/bin/env python3
"""Builds the customer's guide to the iPhone and iPad app, in the flyers' style (10/05/2026).

Ben: "include a link to the documentation on the website for people to read more about what is
available in the app ... rework them to use [Signal] and make them flashy and professional like the
flyers." The guide that was on the website (/guides/IsHaunted-iOS-iPhone.pdf) is the DEVELOPER
handover document — how the app is built and how to run it — which is the wrong thing to hand
somebody deciding whether to install it. This is the one for them: six pages of what the app does,
said the way the help article says it, on the app's own screens.

    python3 docs/ads/build-app-guide.py          # writes docs/IsHaunted-App-Guide.pdf and the site's copy
    python3 docs/ads/build-app-guide.py --png    # also photographs each page to docs/ads/preview/

Every claim comes from Ben.Web.Services/Help/Content/the-mobile-apps.md; keep the two in step.
Screens: docs/ios-media/iphone (the guide captures). Photographs: docs/media/stock (Unsplash).
Apple's badge and QR code: Ben.Web.Website/wwwroot/static/images/app, used as Apple supplies them.
"""
import html, os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

PHONE = "../ios-media/iphone/"
STOCK = "../media/stock/"
APP = "../../Ben.Web.Website/wwwroot/static/images/app/"
HELP = "../../Ben.Web.Website/wwwroot/help/media/the-mobile-apps/"

ICONS = {
    "gauge": '<path d="M12 14l4-4"/><path d="M3.3 19a10 10 0 1 1 17.4 0"/>',
    "wave": '<path d="M2 12h2l2-6 3 12 3-9 2 5 2-2h6"/>',
    "pin": '<path d="M12 22s7-6.2 7-12a7 7 0 0 0-14 0c0 5.8 7 12 7 12z"/><circle cx="12" cy="10" r="2.5"/>',
    "door": '<path d="M13 4h3a2 2 0 0 1 2 2v14M2 20h20M13 20V3.5a1 1 0 0 0-1.2-1l-6 1.3A1 1 0 0 0 5 4.8V20"/><circle cx="10" cy="12" r=".6" fill="currentColor"/>',
    "signal": '<path d="M2 20h.01M7 20v-4M12 20v-8M17 20V8M22 4v16"/><path d="m3 3 18 18"/>',
    "flag": '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/><path d="M4 22v-7"/>',
    "mic": '<rect x="9" y="2" width="6" height="12" rx="3"/><path d="M19 10v2a7 7 0 0 1-14 0v-2M12 19v3"/>',
    "eye": '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8S1 12 1 12z"/><circle cx="12" cy="12" r="3"/>',
    "moon": '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
    "camera": '<path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/>',
    "lock": '<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
    "play": '<polygon points="6 3 20 12 6 21 6 3"/>',
    "image": '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/>',
    "upload": '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12"/>',
    "scissors": '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M20 4 8.1 15.9M14.5 14.5 20 20M8.1 8.1 12 12"/>',
    "globe": '<circle cx="12" cy="12" r="10"/><path d="M2 12h20M12 2a15 15 0 0 1 0 20M12 2a15 15 0 0 0 0 20"/>',
    "share": '<circle cx="18" cy="5" r="3"/><circle cx="6" cy="12" r="3"/><circle cx="18" cy="19" r="3"/><path d="m8.6 13.5 6.8 4M15.4 6.5l-6.8 4"/>',
    "walk": '<circle cx="13" cy="4" r="2"/><path d="m9 20 3-7 2 3v4M7 11l3-3 3 1 2 3h3"/>',
    "calendar": '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
    "ticket": '<path d="M3 8a2 2 0 0 0 2-2h14a2 2 0 0 0 2 2v2a2 2 0 0 0 0 4v2a2 2 0 0 0-2 2H5a2 2 0 0 0-2-2v-2a2 2 0 0 0 0-4z"/><path d="M13 6v12" stroke-dasharray="2 2"/>',
    "utensils": '<path d="M3 2v7a3 3 0 0 0 3 3v10M9 2v7M6 2v4M18 22V2c-2.5 1.5-4 4-4 8h4"/>',
    "scan": '<path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2"/><path d="M7 12h10"/>',
    "folder": '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>',
    "users": '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
    "shield": '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="m9 12 2 2 4-4"/>',
    "phone": '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M11 18h2"/>',
}


def icon(name, size=20):
    return (f'<svg class="ic" width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            f'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{ICONS[name]}</svg>')


CSS = """
@page { size: 8.5in 11in; margin: 0; }
* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
:root { --base: #0C1017; --base2: #1E1650; --accent: #B6A2FF; --accent2: #67E8F9;
        --fill: linear-gradient(120deg, #7C5CFF 0%, #22D3EE 100%); --glow: rgba(124,92,255,.38);
        --card: rgba(255,255,255,.055); --line: rgba(255,255,255,.10); --muted: rgba(255,255,255,.72); }
html, body { margin: 0; padding: 0; background: #555; }
body { font-family: "Avenir Next", Avenir, "Helvetica Neue", sans-serif; color: #fff; }
.page { width: 8.5in; height: 11in; position: relative; overflow: hidden; page-break-after: always; break-after: page;
        background: radial-gradient(110% 55% at 100% 0%, var(--base2) 0%, var(--base) 62%); }
.page:last-child { page-break-after: auto; break-after: auto; }
@media screen { .page { margin: 0 auto 24px; } }
h1, h2, .big { font-family: Futura, "Avenir Next", sans-serif; font-weight: 800; letter-spacing: -.01em; }
em { font-style: normal; color: var(--accent); }
.kicker { font-size: 8.5pt; font-weight: 700; letter-spacing: .24em; text-transform: uppercase; color: var(--accent2); }

/* phones */
.phone { position: absolute; width: 2.05in; aspect-ratio: 1206 / 2622; border-radius: .3in; background: #050505; padding: .065in;
         box-shadow: 0 26px 54px rgba(0,0,0,.6), 0 0 0 1.4px rgba(255,255,255,.14), 0 0 50px var(--glow); }
.phone img { width: 100%; height: 100%; object-fit: cover; object-position: top; border-radius: .24in; display: block; }

/* page furniture */
.top { position: absolute; top: .5in; left: .6in; right: .6in; display: flex; align-items: center; justify-content: space-between; }
.brand { display: flex; align-items: center; gap: .12in; font-weight: 700; font-size: 11pt; letter-spacing: .02em; }
.brand img { width: .42in; height: .42in; border-radius: 22%; box-shadow: 0 6px 16px rgba(0,0,0,.45); }
.brand small { display: block; font-size: 7pt; font-weight: 600; letter-spacing: .2em; text-transform: uppercase; opacity: .7; }
.folio { font-size: 8pt; letter-spacing: .2em; text-transform: uppercase; color: var(--muted); }
.head { position: absolute; top: 1.2in; left: .6in; width: 4.1in; }
.head h2 { font-size: 30pt; line-height: 1.02; margin: .08in 0 .14in; }
.head p { font-size: 11pt; line-height: 1.45; color: var(--muted); margin: 0; }
.cards { position: absolute; left: .6in; width: 4.1in; display: grid; gap: .13in; }
.card { display: grid; grid-template-columns: .44in 1fr; gap: .13in; align-items: start; padding: .14in .16in;
        background: var(--card); border: 1px solid var(--line); border-radius: .14in; }
.card .dot { width: .44in; height: .44in; border-radius: .13in; display: grid; place-items: center; color: #fff;
             background: var(--fill); box-shadow: 0 6px 14px var(--glow); }
.card h4 { margin: .01in 0 .03in; font-size: 10.6pt; font-weight: 700; }
.card p { margin: 0; font-size: 8.7pt; line-height: 1.42; color: var(--muted); }
.note { position: absolute; left: .6in; right: .6in; bottom: .55in; padding: .12in .18in; border-radius: .12in;
        border-left: 3px solid #7C5CFF; background: rgba(124,92,255,.10); font-size: 8.6pt; line-height: 1.45; color: var(--muted); }
.note b { color: #fff; }
.fine { position: absolute; bottom: .2in; left: .6in; right: .6in; font-size: 6pt; color: rgba(255,255,255,.4); text-align: center; }
.cap { position: absolute; font-size: 7.6pt; letter-spacing: .14em; text-transform: uppercase; color: var(--muted); text-align: center; width: 2.05in; }

/* cover */
.cover { background: var(--base); }
.cover .hero { position: absolute; inset: 0 0 auto 0; height: 6.4in; background-size: cover; background-position: center 35%; }
.cover .hero::after { content: ""; position: absolute; inset: 0;
  background: linear-gradient(180deg, rgba(12,16,23,.55) 0%, rgba(12,16,23,.1) 25%, rgba(12,16,23,.75) 70%, var(--base) 100%); }
.cover .top { z-index: 3; }
.pill { font-size: 8pt; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: #fff; background: var(--fill);
        padding: .07in .16in; border-radius: 99px; box-shadow: 0 4px 14px rgba(0,0,0,.35); }
.cover .title { position: absolute; top: 1.55in; left: .6in; right: .6in; z-index: 3; }
.cover h1 { font-size: 46pt; line-height: 1; margin: .1in 0 .16in; text-shadow: 0 4px 20px rgba(0,0,0,.5); }
.cover .title p { font-size: 13pt; line-height: 1.4; color: rgba(255,255,255,.9); max-width: 5.4in; margin: 0; }
.cover .fan { position: absolute; top: 4.25in; left: 0; right: 0; height: 4.9in; z-index: 4; }
.cover .fan .phone { width: 2.15in; top: 0; left: 50%; }
.cover .fan .l { transform: translateX(-133%) rotate(-10deg) scale(.88); top: .22in; }
.cover .fan .c { transform: translateX(-50%); z-index: 2; }
.cover .fan .r { transform: translateX(33%) rotate(10deg) scale(.88); top: .22in; }
.get { position: absolute; left: 0; right: 0; bottom: 0; height: 1.55in; background: var(--fill); display: flex; align-items: center;
       justify-content: space-between; padding: 0 .6in; z-index: 5; }
.get .big { font-size: 20pt; line-height: 1.05; }
.get .big span { display: block; font-family: "Avenir Next", sans-serif; font-size: 8.5pt; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; opacity: .85; margin-top: .05in; }
.get .apple { display: flex; align-items: center; gap: .22in; }
.get .badge { height: .62in; }
.get .qr { width: 1.12in; height: 1.12in; border-radius: .1in; box-shadow: 0 8px 20px rgba(0,0,0,.35); }

/* back */
.strip { height: 2.3in; display: grid; grid-template-columns: 1.4fr 1fr 1fr; gap: .06in; }
.strip div { background-size: cover; background-position: center; position: relative; }
.strip div::after { content: ""; position: absolute; inset: 0; background: linear-gradient(180deg, rgba(12,16,23,0) 40%, rgba(12,16,23,.85) 100%); }
.back .head { top: 2.55in; width: auto; right: .6in; }
.back .apple { position: absolute; top: 4.35in; left: .6in; right: .6in; display: grid; grid-template-columns: 1.6in 1fr; gap: .4in; align-items: center;
               padding: .3in; border-radius: .18in; background: var(--card); border: 1px solid var(--line); box-shadow: 0 0 0 1.5px #7C5CFF; }
.back .apple .qr { width: 1.6in; height: 1.6in; border-radius: .14in; }
.back .apple .badge { height: .66in; margin: .12in 0 .1in; display: block; }
.back .apple p { margin: 0; font-size: 9.5pt; line-height: 1.45; color: var(--muted); }
.back .facts { position: absolute; top: 6.85in; left: .6in; right: .6in; display: grid; grid-template-columns: repeat(3, 1fr); gap: .13in; }
.back .facts .card { grid-template-columns: 1fr; }
.back .more { position: absolute; left: .6in; right: .6in; bottom: .55in; height: 1in; border-radius: .16in; overflow: hidden; display: grid;
              grid-template-columns: 1.5fr 1fr; background: #17123a; box-shadow: 0 16px 40px rgba(0,0,0,.45); }
.back .more > div:first-child { padding: .16in .28in; display: flex; flex-direction: column; justify-content: center; }
.back .more .big { font-size: 15pt; }
.back .more small { font-size: 8.4pt; color: var(--muted); margin-top: .04in; }
.back .more .url { background: var(--fill); display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center; }
.back .more .url b { font-family: Futura, sans-serif; font-size: 13pt; }
.back .more .url span { font-size: 7.5pt; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; margin-top: .03in; }
"""

FINE = ("IsHaunted.com · Nashville, Tennessee · Screens are the app as it runs; the readings and places in them are a demonstration · "
        "Photographs: Unsplash · Apple, the Apple logo, iPhone and iPad are trademarks of Apple Inc. App Store is a service mark of Apple Inc.")


def top(folio):
    return (f'<div class="top"><div class="brand"><img src="{APP}app-icon.png" alt=""><div>IsHaunted<small>for iPhone &amp; iPad</small></div></div>'
            f'<div class="folio">{folio}</div></div>')


def cards(items, top_in):
    e = html.escape
    body = "".join(f'<div class="card"><div class="dot">{icon(i)}</div><div><h4>{e(t)}</h4><p>{e(d)}</p></div></div>'
                   for i, t, d in items)
    return f'<div class="cards" style="top:{top_in}in">{body}</div>'


def phones(shots):
    """(src, left_in, top_in, rotate_deg, caption) — placed on the right half of the page."""
    out = []
    for src, left, top_in, rot, cap in shots:
        out.append(f'<div class="phone" style="left:{left}in;top:{top_in}in;transform:rotate({rot}deg)"><img src="{src}" alt=""></div>')
        if cap:
            out.append(f'<div class="cap" style="left:{left}in;top:{top_in + 4.62}in">{html.escape(cap)}</div>')
    return "".join(out)


def build_html():
    p = PHONE
    pages = []

    # 1 — cover
    pages.append(f"""
<section class="page cover">
  <div class="hero" style="background-image:url({STOCK}p2-holding-phone.jpg)"></div>
  {top('<span class="pill">The app guide</span>')}
  <div class="title">
    <div class="kicker">Field Kit, feed, cases and nights out</div>
    <h1>Your phone is<br>the <em>instrument</em>.</h1>
    <p>A meter, a recorder and a camera that keep the same clock — and put the night on your group's case
       once you have signal again. Free, on iPhone and iPad.</p>
  </div>
  <div class="fan">
    <div class="phone l"><img src="{p}53-session-review.png" alt=""></div>
    <div class="phone r"><img src="{p}47-evp-mode.png" alt=""></div>
    <div class="phone c"><img src="{p}42b-meter-with-needle.png" alt=""></div>
  </div>
  <div class="get">
    <div class="big">Free on the App Store<span>iPhone &amp; iPad · iOS 18 or later</span></div>
    <div class="apple"><img class="badge" src="{APP}app-store-badge-white.svg" alt="Download on the App Store">
      <img class="qr" src="{APP}app-store-qr-dark.png" alt="QR code for IsHaunted on the App Store"></div>
  </div>
</section>""")

    # 2 — Field Kit, the instrument
    pages.append(f"""
<section class="page">
  {top('02 · Field Kit')}
  <div class="head"><div class="kicker">The phone as an instrument</div>
    <h2>A meter that knows <em>the room</em>.</h2>
    <p>Field Kit records an investigation on the device. It needs no signal and no account: start in a cellar
       with no bars, and nothing leaves the phone until you send it.</p></div>
  {cards([
      ("gauge", "Measured against the room", "Set a base once the room has settled. The needle then shows how far the magnetic field has moved from it, in milligauss."),
      ("wave", "Sound, alongside", "A sound meter in decibels with your base and report levels marked on it, recording as it goes."),
      ("door", "Say which room you're in", "One tap names the room; every reading, mark and photo after it belongs there. Nothing else can tell rooms apart."),
      ("pin", "Where, honestly", "Your position, with how accurate it is. Indoors that is often the whole building, so it always says so."),
      ("signal", "No signal needed", "Record all night underground. It keeps everything on the phone until you choose to send it."),
  ], 3.45)}
  {phones([(p + "42-live-session.png", 5.0, 1.15, -4, ""), (p + "42b-meter-with-needle.png", 6.05, 5.55, 4, "")])}
  <div class="note"><b>A magnetic field meter, not an EMF meter.</b> The phone's magnetometer reads the steady magnetic field —
    the Earth's, and whatever wiring and iron do to it. It cannot see the AC fields a K-II style detector responds to, and the
    app says so on the screen.</div>
  <div class="fine">{html.escape(FINE)}</div>
</section>""")

    # 3 — marking, asking, watching
    pages.append(f"""
<section class="page">
  {top('03 · During the night')}
  <div class="head"><div class="kicker">Mark it, ask it, leave it</div>
    <h2>Every moment, <em>found again</em>.</h2>
    <p>A week later, reviewing is jumping to a list of moments — not scrubbing hours of tape.</p></div>
  {cards([
      ("flag", "Mark and Note", "Mark drops a marker at this second. Set a report level and it marks the field or the sound for you, once per event."),
      ("mic", "Notes you can speak", "Spoken notes turn into text on the phone itself — nothing sent anywhere, and it works with no signal."),
      ("wave", "EVP question and answer", "Marks the question and the silence after it, each pointing into the recording, so you know where to listen."),
      ("eye", "Leave it watching", "Set it down to watch the field, the sound, the phone being moved, or movement in the camera's view."),
      ("moon", "Black out the screen", "One tap takes the screen to black and keeps the phone awake. Tap anywhere to bring it back."),
      ("lock", "Put it in your pocket", "With sound on, a session keeps recording when the phone is locked. The review shows any gap."),
  ], 3.05)}
  {phones([(p + "44-marked.png", 5.0, 1.15, -4, ""), (p + "47-evp-mode.png", 6.05, 5.55, 4, "")])}
  <div class="fine">{html.escape(FINE)}</div>
</section>""")

    # 4 — play back and send
    pages.append(f"""
<section class="page">
  {top('04 · Afterward')}
  <div class="head"><div class="kicker">Play it back, send it on</div>
    <h2>The night, on <em>one playhead</em>.</h2>
    <p>The trace, the map, the compass, the sound and the video move together, with every mark listed to jump to.</p></div>
  {cards([
      ("play", "Review on the phone", "Marks land a few seconds before the event, so you hear what led up to it. Video plays with the session's sound."),
      ("image", "Photos in the strip", "Each photo glows as playback passes the moment it was taken. Tap one to see it full size."),
      ("upload", "Straight to the case", "Send a session to your group's investigation when there is signal; it plays on the website exactly as here."),
      ("scissors", "Send only what mattered", "Trim to the part that counts before it goes, instead of a whole night of quiet."),
      ("globe", "Public places, in the open", "Recorded somewhere public? Publish it to the place's archive, and play what others found there."),
      ("share", "Hand it to anyone", "Export the session as a file. Another IsHaunted phone opens it and plays it, marked as somebody else's."),
  ], 3.05)}
  {phones([(p + "53-session-review.png", 5.0, 1.15, -4, ""), (HELP + "iphone-public-sessions.png", 6.05, 5.55, 4, "")])}
  <div class="fine">{html.escape(FINE)}</div>
</section>""")

    # 5 — nights out
    pages.append(f"""
<section class="page">
  {top('05 · Nights out')}
  <div class="head"><div class="kicker">Ghost walks, events and the door</div>
    <h2>Book it. <em>Show up.</em> Get in.</h2>
    <p>Tours and events near you, readable without an account — and everything for the night itself in your pocket.</p></div>
  {cards([
      ("walk", "Ghost walks near you", "How far away they are and when they next run. Location is asked for, never taken; type a city instead if you like."),
      ("calendar", "Events, on your clock", "Times read where you are, with the zone beside them. Add a night to your own calendar in one tap."),
      ("ticket", "A pass with no signal", "Your pass, the program and the menus are saved on the phone, so they open in a cellar with no bars."),
      ("camera", "Photos for the night", "Share photos to the event's room; with no signal they wait on the phone and go when you're back in range."),
      ("scan", "Running the door", "Helpers check people in by name or by scanning their pass — with or without a signal."),
      ("users", "Start the night together", "A lead can start everybody's Field Kit from a push. Nobody is taken over; you join when you're ready."),
  ], 3.05)}
  {phones([(p + "63-event-hub.png", 5.0, 1.15, -4, ""), (p + "67-door-scanned.png", 6.05, 5.55, 4, "")])}
  <div class="fine">{html.escape(FINE)}</div>
</section>""")

    # 6 — back: get it
    pages.append(f"""
<section class="page back">
  <div class="strip"><div style="background-image:url({STOCK}i2-dark-hallway.jpg)"></div>
    <div style="background-image:url({STOCK}w3-foggy-graveyard.jpg)"></div><div style="background-image:url({STOCK}s1-candles.jpg)"></div></div>
  <div class="head"><div class="kicker">Get it</div>
    <h2>Free. On your phone <em>tonight</em>.</h2>
    <p>Look around without an account. Sign in — or sign in with Apple — when you want to join in.</p></div>
  <div class="apple">
    <img class="qr" src="{APP}app-store-qr-dark.png" alt="QR code for IsHaunted on the App Store">
    <div><div class="kicker">Point your iPhone's camera here</div>
      <img class="badge" src="{APP}app-store-badge-white.svg" alt="Download on the App Store">
      <p>Or search the App Store for <b>IsHaunted</b>. Works on iPhone and iPad with iOS 18 or later.</p></div>
  </div>
  <div class="facts">
    <div class="card"><div><div class="dot" style="margin-bottom:.08in">{icon('shield')}</div><h4>No tracking</h4>
      <p>No advertising, analytics or data brokers. Location is stamped on readings only while a session runs.</p></div></div>
    <div class="card"><div><div class="dot" style="margin-bottom:.08in">{icon('folder')}</div><h4>One account, two screens</h4>
      <p>Your cases, messages and sessions are the same on the phone and on ishaunted.com.</p></div></div>
    <div class="card"><div><div class="dot" style="margin-bottom:.08in">{icon('phone')}</div><h4>iPad, too</h4>
      <p>On an iPad the instruments sit beside the log, and the trace beside the map.</p></div></div>
  </div>
  <div class="more"><div><div class="big">Everything the app does, <em>in full</em>.</div>
      <small>The help guide, kept up to date with every release.</small></div>
    <div class="url"><b>ishaunted.com/help</b><span>The iPhone &amp; iPad app</span></div></div>
  <div class="fine">{html.escape(FINE)}</div>
</section>""")

    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>IsHaunted for iPhone &amp; iPad — the app guide</title><style>{CSS}</style></head>
<body>{''.join(pages)}</body></html>"""


def main():
    want_png = "--png" in sys.argv
    page_html = os.path.join(HERE, "app-guide.html")
    with open(page_html, "w", encoding="utf-8") as f:
        f.write(build_html())
    pdf = os.path.join(ROOT, "docs", "IsHaunted-App-Guide.pdf")
    subprocess.run([CHROME, "--headless", "--no-pdf-header-footer", f"--print-to-pdf={pdf}", "file://" + page_html],
                   capture_output=True, timeout=180)
    site = os.path.join(ROOT, "Ben.Web.Website", "wwwroot", "guides", "IsHaunted-App-Guide.pdf")
    shutil.copyfile(pdf, site)
    print(f"wrote {os.path.relpath(pdf, ROOT)} and {os.path.relpath(site, ROOT)}")
    if want_png:
        out = os.path.join(HERE, "preview")
        os.makedirs(out, exist_ok=True)
        # One picture of the whole document, each page 816x1056 CSS px (Letter at 96 dpi).
        subprocess.run([CHROME, "--headless", "--hide-scrollbars", "--window-size=816,6480", "--force-device-scale-factor=1",
                        f"--screenshot={os.path.join(out, 'app-guide.png')}", "file://" + page_html],
                       capture_output=True, timeout=180)
        print("wrote docs/ads/preview/app-guide.png")


if __name__ == "__main__":
    main()
