#!/usr/bin/env python3
"""Builds the whole-platform flyer: one sheet of US Letter, front and back, for emailing.

WHY A SEVENTH FLYER

docs/ads/build-ads.py makes six advertisements, one per AUDIENCE — enthusiasts, groups, ghost
walks, venues, event hosts, and a general one. Each is photograph-led and carries a single
screenshot, because each is answering "is this for me?".

Ben asked (2026-10-01) for something different: one flyer to email to a person who has never seen
the site, that shows what it actually DOES — the editors, the EVP detector, investigations, the
group, the billing page, the tours, the events, the apps, and how the apps join the website — and
summarises the lot with the best of everything. That is a capability showcase, not an audience
pitch, so it gets a layout of its own rather than contorting the six.

The difference is the screens. This flyer carries nineteen real ones, every pixel of it captured
from the running site by the help-media harness or the persona walks. Nothing here is a mockup,
and nothing is a stock photograph of a laptop with a made-up interface on it.

EMAIL

The point of this one is that it is attached to an email, so the PDF is kept under about 8 MB:
the screenshots are drawn into tiles roughly 1.8 inches wide, and Chrome embeds them at the size
it draws them.

Run from the repository root:

    python3 docs/ads/build-flyer.py            # writes docs/ads/flyer-everything.html -> IsHaunted-Flyer.pdf
    python3 docs/ads/build-flyer.py --png      # also photographs both pages into docs/ads/preview/

Photographs: docs/media/stock (Unsplash License, credited in
ProjectNotes/FeatureHistory/README-hosted-events-235-media.md).
Screens: the help captures, the admin help captures, and the persona walks under docs/web-media.
"""
import html, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

STOCK = "../media/stock/"
PUB   = "../../Ben.Web.Website/wwwroot/help/media/"
ADMIN = "../../Ben.Web.Services/Help/Media/"
WALK  = "../web-media/"
LOGO  = "../assets/is-haunted-logo.svg"

ICONS = {
    "globe":    '<circle cx="12" cy="12" r="10"/><path d="M2 12h20M12 2a15 15 0 0 1 0 20M12 2a15 15 0 0 0 0 20"/>',
    "pin":      '<path d="M12 22s7-6.2 7-12a7 7 0 0 0-14 0c0 5.8 7 12 7 12z"/><circle cx="12" cy="10" r="2.5"/>',
    "folder":   '<path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>',
    "calendar": '<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>',
    "wave":     '<path d="M2 12h2l2-6 3 12 3-9 2 5 2-2h6"/>',
    "chart":    '<path d="M3 3v18h18"/><path d="m7 15 4-4 3 3 5-6"/>',
    "camera":   '<path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/>',
    "sparkle":  '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6"/>',
    "board":    '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M9 21V9"/>',
    "users":    '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/>',
    "lantern":  '<path d="M9 3h6M12 3V1M8 6h8l1 3v9l-1 3H8l-1-3V9z"/><path d="M12 10c1.5 1.5 1.5 3.5 0 5-1.5-1.5-1.5-3.5 0-5z"/>',
    "ticket":   '<path d="M3 8a2 2 0 0 0 2-2h14a2 2 0 0 0 2 2v2a2 2 0 0 0 0 4v2a2 2 0 0 0-2 2H5a2 2 0 0 0-2-2v-2a2 2 0 0 0 0-4z"/><path d="M13 6v12" stroke-dasharray="2 2"/>',
    "star":     '<path d="m12 2 3.1 6.3 6.9 1-5 4.9 1.2 6.8L12 17.8 5.8 21l1.2-6.8-5-4.9 6.9-1z"/>',
    "phone":    '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M11 18h2"/>',
    "shield":   '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="m9 12 2 2 4-4"/>',
    "card":     '<rect x="2" y="5" width="20" height="14" rx="2"/><path d="M2 10h20"/>',
}



# ── Keeping the attachment small ────────────────────────────────────────────
#
# The help captures are retina: 2880x1800 each, and there are twenty-odd of them. Chrome embeds
# every one at full resolution and the PDF came to 7.2 MB — emailable, but heavier than a flyer has
# any business being. Each screenshot is drawn under two inches wide, so a 1100-pixel copy is still
# more than three hundred dots to the inch at the size it prints.
#
# Resampled with sips (macOS, no dependency to install) into a cache beside this script, and only
# when the source is newer than the copy. The cache is gitignored; delete it and the next build
# rebuilds it.
CACHE = os.path.join(HERE, ".cache")
# width, JPEG quality. Lossless PNG is the wrong format for an attachment: a 1100-pixel screenshot
# is 350-560 KB as PNG and about 90 KB as a quality-82 JPEG, and at under two printed inches the
# difference is not visible. Only the logo stays vector.
_RECIPE = {"stock": (1500, 78), "screen": (1100, 82)}


def small(rel, kind="screen"):
    """A downscaled JPEG copy of rel (a path relative to docs/ads), as a path relative to docs/ads."""
    src = os.path.normpath(os.path.join(HERE, rel))
    if not os.path.exists(src):
        return rel                                   # let the missing-file check below report it
    width, quality = _RECIPE[kind]
    stem = rel.replace("..", "").replace("/", "_").lstrip("_").rsplit(".", 1)[0]
    out = os.path.join(CACHE, f"{width}-{stem}.jpg")
    if not os.path.exists(out) or os.path.getmtime(out) < os.path.getmtime(src):
        os.makedirs(CACHE, exist_ok=True)
        subprocess.run(["sips", "--resampleWidth", str(width),
                        "-s", "format", "jpeg", "-s", "formatOptions", str(quality),
                        src, "--out", out],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return os.path.relpath(out, HERE)


def icon(name, size=16):
    return (f'<svg class="ic" width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" '
            f'stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">'
            f'{ICONS[name]}</svg>')


# ── What the flyer shows ────────────────────────────────────────────────────
# Each tile is (icon, title, one line, screenshot, how to crop it). Twelve of them, four across,
# every one a real screen.
TILES = [
    # icon, title, one line, screenshot, crop anchor, zoom. The zoom matters: a 1400-pixel page
    # drawn an inch wide is a grey smudge, so each tile shows a corner of its screen at a size a
    # reader can actually make out.
    ("globe",    "The feed",               "Follow places, groups and nights out. Polls, photos, links.",
     PUB + "the-feed/feed.png", "center right", 2.1),
    ("pin",      "A map of haunted places", "Every public place, what happened there, what is on near you.",
     PUB + "getting-started/public-map.png", "center", 1.2),
    ("folder",   "Cases and evidence",     "The client's words, the timeline, the files, one thread.",
     PUB + "working-a-case/case-detail.png", "center right", 1.8),
    ("calendar", "Investigations",         "Propose dates, take the roster, and put the visit on the map.",
     PUB + "working-a-case/investigations-map.png", "center", 1.4),
    ("wave",     "The EVP detector",       "Finds voices under the noise and hands you the candidates.",
     PUB + "using-the-audio-editor/evp-candidates.png", "top left", 1.5),
    ("chart",    "Audio tools",            "Spectrogram, filters, a noise gate, silence detection, clips.",
     PUB + "using-the-audio-editor/spectrogram.png", "center", 1.3),
    ("camera",   "A video editor",         "Timeline, callouts, text, transitions and export \u2014 in the browser.",
     PUB + "using-the-video-editor/timeline-two-clips.png", "center", 1.25),
    ("sparkle",  "A photo editor",         "Crop, straighten and mark up a photo without leaving the case.",
     ADMIN + "organization-administration/photo-editor.png", "center", 1.4),
    ("board",    "Research boards",        "Lay out a family tree or a deed trail, then present it full screen.",
     PUB + "working-a-case/board-presenting.png", "center", 1.15),
    ("users",    "Your group",             "Members, roles, titles and duties \u2014 and who sees a private case.",
     ADMIN + "organization-administration/members.png", "top right", 1.9),
    ("lantern",  "Ghost walks and tours",  "A page per tour, dates people book, a door list for the guide.",
     ADMIN + "organization-administration/tours.png", "center right", 2.0),
    ("ticket",   "Hosted events",          "Rooms or seats on a plan, and passes that open with no signal.",
     ADMIN + "organization-administration/event-plan-seats.png", "center right", 1.6),
]

# ── How the phone and the website are one thing ─────────────────────────────
# Ben asked for this specifically, and it is the part a list of features cannot say: the same
# recording travels from a dark hallway to a client's report without anybody exporting a file.
CHAIN = [
    ("Record it in the field", "Field Kit turns the phone into a meter and a recorder. No signal needed.",
     PUB + "the-mobile-apps/iphone-meter.png", "phone"),
    ("It lands on the case",   "The session uploads itself when there is signal again, onto the right case.",
     PUB + "working-a-case/field-session-player.png", "shot"),
    ("Scan it for voices",     "The EVP detector works the whole recording and marks what it heard.",
     PUB + "using-the-audio-editor/evp-candidates.png", "shot"),
    ("Cut the moment out",     "Keep the clip, caption it, and put it in a report or a publication.",
     PUB + "using-the-video-editor/editor-overview.png", "shot"),
    ("The client reads it",    "They see their own case, the evidence you chose, and nothing you did not.",
     WALK + "client/20-my-cases.png", "shot"),
]

CSS = """
@page { size: 8.5in 11in; margin: 0; }
* { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
html, body { margin: 0; padding: 0; background: #4a4a4a; }
body { font-family: "Avenir Next", Avenir, "Helvetica Neue", sans-serif; color: #fff;
  --base: #0C1017; --base2: #1E1650; --accent: #B6A2FF; --accent2: #67E8F9; --fill: linear-gradient(120deg, #7C5CFF 0%, #22D3EE 100%);
  --muted: rgba(255,255,255,.76); --card: rgba(255,255,255,.06); --line: rgba(255,255,255,.11); }

/* Letter, and laid out as a COLUMN rather than with absolute offsets: the first draft positioned
   every block by hand, a tile title wrapped to two lines, and the third row of tiles printed on
   top of the strip below it. A flow column cannot do that. */
.page { width: 8.5in; height: 11in; position: relative; overflow: hidden; background: var(--base);
  display: flex; flex-direction: column; page-break-after: always; break-after: page; }
.page:last-child { page-break-after: auto; break-after: auto; }
@media screen { .page { margin: 0 auto 26px; box-shadow: 0 10px 40px rgba(0,0,0,.55); } }
body.only-front .back, body.only-back .front { display: none; }
body.only-front .page, body.only-back .page { margin: 0; }

h1, h2, .go, .big, .url b, .price b, .glab { font-family: Futura, "Avenir Next", sans-serif; font-weight: 800; }
em { font-style: normal; color: var(--accent); }
.lab { font-size: 7.4pt; font-weight: 700; letter-spacing: .2em; text-transform: uppercase;
  color: rgba(255,255,255,.5); }

/* ── a browser window drawn around a real screenshot ───────── */
.win { border-radius: .09in; overflow: hidden; background: #0e0e12; position: relative; flex: none;
  box-shadow: 0 12px 28px rgba(0,0,0,.5), 0 0 0 1px rgba(255,255,255,.13); }
.win::before { content: ""; position: absolute; top: 0; left: 0; right: 0; height: .12in;
  background: #26262b; z-index: 2; }
.win::after { content: "● ● ●"; position: absolute; top: 0; left: .06in; font-size: 4.6pt;
  letter-spacing: .014in; color: #71717a; z-index: 3; line-height: .12in; }
.win i { position: absolute; top: .12in; left: 0; right: 0; bottom: 0; overflow: hidden; display: block; }
.win img { position: absolute; top: 0; left: 0; width: calc(100% * var(--z, 1));
  height: calc(100% * var(--z, 1)); object-fit: cover; object-position: var(--f, top left); display: block; }

/* ── a phone drawn around a real screenshot ────────────────── */
.ph { border-radius: .2in; background: #050505; padding: .045in; position: relative; flex: none;
  box-shadow: 0 14px 32px rgba(0,0,0,.6), 0 0 0 1.1px rgba(255,255,255,.17); }
.ph img { width: 100%; height: 100%; object-fit: cover; object-position: top center;
  border-radius: .16in; display: block; }

/* ── FRONT ─────────────────────────────────────────────────── */
.herowrap { position: relative; height: 5.55in; flex: none; }
.hero { position: absolute; inset: 0; background-size: cover; background-position: center 42%; }
.hero::after { content: ""; position: absolute; inset: 0; background: linear-gradient(180deg,
  rgba(0,0,0,.55) 0%, rgba(0,0,0,.12) 18%, rgba(12,16,23,0) 32%, rgba(12,16,23,.82) 68%, var(--base) 100%); }
.brandbar { position: absolute; top: .4in; left: .5in; right: .5in; display: flex; align-items: center;
  justify-content: space-between; z-index: 6; }
.brand { display: flex; align-items: center; gap: .13in; font-weight: 700; font-size: 15pt; }
.brand img { width: .5in; height: .5in; filter: drop-shadow(0 2px 7px rgba(0,0,0,.65)); }
.brand small { display: block; font-weight: 500; font-size: 7.4pt; letter-spacing: .2em;
  text-transform: uppercase; opacity: .85; }
.pill { font-size: 8pt; font-weight: 700; letter-spacing: .15em; text-transform: uppercase;
  color: #fff; background: var(--fill); padding: .07in .17in; border-radius: 99px;
  box-shadow: 0 4px 14px rgba(0,0,0,.45); }
.headline { position: absolute; left: .5in; bottom: .34in; width: 4.55in; z-index: 5; }
.kicker { font-size: 8.4pt; font-weight: 700; letter-spacing: .22em; text-transform: uppercase;
  color: var(--accent); margin-bottom: .09in; }
.headline h1 { font-size: 36pt; line-height: 1.0; margin: 0 0 .13in; letter-spacing: -.014em;
  text-shadow: 0 3px 20px rgba(0,0,0,.6); }
.headline p { font-size: 11.6pt; line-height: 1.35; margin: 0; color: rgba(255,255,255,.92); }
.montage { position: absolute; right: .44in; top: 1.15in; width: 3.35in; height: 3.5in; z-index: 4; }
.montage .win { position: absolute; left: 0; top: .44in; width: 2.5in; height: 1.72in; transform: rotate(-2.4deg); }
.montage .ph { position: absolute; right: 0; top: 0; width: 1.6in; height: 3.3in; transform: rotate(3.5deg); z-index: 2; }

.fbody { flex: 1; padding: .24in .5in 0; display: flex; flex-direction: column; min-height: 0; }
.pillars { display: grid; grid-template-columns: repeat(3, 1fr); gap: .2in; }
.pillar { display: grid; grid-template-columns: .4in 1fr; gap: .11in; align-items: start; }
.pillar .badge { width: .4in; height: .4in; border-radius: .11in; display: grid; place-items: center;
  color: #fff; background: var(--fill);
  box-shadow: 0 5px 14px rgba(124,92,255,.38); }
.pillar h3 { margin: 0 0 .03in; font-size: 11pt; font-weight: 700; }
.pillar p { margin: 0; font-size: 8.3pt; line-height: 1.34; color: var(--muted); }
.glimpse { margin-top: .26in; }
.gstrip { display: grid; grid-template-columns: repeat(3, 1fr); gap: .17in; margin-top: .1in; }
.gstrip figure { margin: 0; }
.gstrip .win { height: 1.62in; }
.gstrip figcaption { margin: .08in 0 0; font-size: 8pt; line-height: 1.32; color: var(--muted); }
.gstrip figcaption b { display: block; color: #fff; font-size: 9.4pt; font-weight: 700; margin-bottom: .015in; }
.cta { height: .98in; flex: none; display: flex; align-items: center; justify-content: space-between;
  padding: 0 .5in; margin-top: auto; color: #fff;
  background: var(--fill); }
.cta .go { font-size: 19pt; letter-spacing: -.012em; }
.cta .go span { display: block; font-family: "Avenir Next", sans-serif; font-size: 8.2pt;
  font-weight: 600; letter-spacing: .11em; text-transform: uppercase; opacity: .8; }
.cta .price { text-align: right; font-weight: 700; font-size: 8.8pt; line-height: 1.22; }
.cta .price b { display: block; font-size: 21pt; }

/* ── BACK ──────────────────────────────────────────────────── */
.back { background: radial-gradient(130% 62% at 100% 0%, var(--base2) 0%, var(--base) 56%); }
.bstrip { height: 1.3in; flex: none; display: grid; grid-template-columns: 1.4fr 1fr 1fr; gap: .055in; }
.bstrip div { background-size: cover; background-position: center 45%; position: relative; }
.bstrip div::after { content: ""; position: absolute; inset: 0; background: linear-gradient(180deg,
  rgba(0,0,0,.30) 0%, rgba(12,16,23,.30) 45%, var(--base) 100%); }
.bbody { flex: 1; padding: .15in .5in .26in; display: flex; flex-direction: column; min-height: 0; }
.bhead h2 { font-size: 21pt; margin: 0; line-height: 1.05; }
.bhead p { margin: .06in 0 0; font-size: 8.4pt; line-height: 1.36; color: var(--muted); max-width: 6.6in; }

.tiles { margin-top: .14in; display: grid; grid-template-columns: repeat(4, 1fr); gap: .11in; }
.tile { background: var(--card); border: 1px solid var(--line); border-radius: .1in;
  padding: .085in .085in .1in; display: flex; flex-direction: column; }
.tile .win { height: .86in; margin-bottom: .07in; }
.tile .th { display: flex; align-items: flex-start; gap: .05in; margin: 0 0 .04in; min-height: .26in; }
.tile .th .ic { color: var(--accent); flex: none; margin-top: .012in; }
.tile h4 { margin: 0; font-size: 8.9pt; font-weight: 700; line-height: 1.14; }
.tile p { margin: 0; font-size: 6.8pt; line-height: 1.28; color: var(--muted); }

.chain { margin-top: .16in; }
.chain .lab { color: var(--accent); }
.chain .sub { font-size: 8.2pt; color: var(--muted); margin: .03in 0 .09in; }
.cflow { display: grid; grid-template-columns: 1fr .16in 1fr .16in 1fr .16in 1fr .16in 1fr; align-items: start; }
.cstep .win { height: .70in; margin-bottom: .06in; }
.cstep .ph { width: .46in; height: .70in; margin: 0 auto .06in; }
.cstep b { display: block; font-size: 8pt; line-height: 1.2; margin-bottom: .02in; }
.cstep span { display: block; font-size: 6.8pt; line-height: 1.3; color: var(--muted); }
.arrow { display: grid; place-items: center; height: .70in; color: var(--accent); font-size: 12pt; font-weight: 700; }

.offer { margin-top: auto; height: 1.06in; flex: none; border-radius: .13in; display: grid;
  grid-template-columns: .9in 1.8fr 1fr; overflow: hidden; background: #122742;
  box-shadow: 0 0 0 1.4px #7C5CFF, 0 14px 36px rgba(0,0,0,.45); }
.offer .shotcell { background-size: cover; background-position: top left; border-right: 1px solid var(--line); }
.offer .words { padding: .12in .19in; display: flex; flex-direction: column; justify-content: center; }
.offer .big { font-size: 14.5pt; line-height: 1.06; }
.offer .small { font-size: 7.7pt; color: var(--muted); margin-top: .05in; line-height: 1.33; }
.offer .url { display: flex; flex-direction: column; justify-content: center; align-items: center;
  text-align: center; color: #fff; background: var(--fill); }
.offer .url b { display: block; font-size: 16pt; }
.offer .url span { font-size: 7.2pt; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; margin-top: .02in; }
.fine { margin-top: .07in; flex: none; font-size: 5.6pt; text-align: center;
  color: rgba(255,255,255,.42); white-space: nowrap; overflow: hidden; }
"""

FINE = ("IsHaunted.com · Nashville, Tennessee · Every screen in this flyer is a photograph of the running site · "
        "Photographs: Unsplash · Prices in USD and subject to change · The iPhone and iPad app is on the App Store")


def win(src, focus="top left", zoom=1.0, cls=""):
    """A browser window around a screenshot. zoom>1 crops in, because a whole 1400-pixel page
    drawn an inch wide is a grey smudge; the top-left corner of it, drawn the same inch wide, is
    recognisably the product."""
    return (f'<div class="win {cls}" style="--f:{focus};--z:{zoom}"><i>'
            f'<img src="{small(src)}" alt=""></i></div>')


def phone(src, cls=""):
    return f'<div class="ph {cls}"><img src="{small(src)}" alt=""></div>'


def build():
    e = html.escape

    pillars = "".join(
        f'<div class="pillar"><div class="badge">{icon(i, 20)}</div>'
        f'<div><h3>{e(t)}</h3><p>{e(d)}</p></div></div>'
        for i, t, d in [
            ("globe", "Explore for nothing",
             "The feed, a map of haunted places, and every tour and night out near you. No account needed to look."),
            ("shield", "Investigate properly",
             "Cases, evidence tools, roles that decide who sees a private home, and reports a client can read."),
            ("ticket", "Fill the house",
             "Ghost walks, venues and haunted weekends: bookings, passes, the door, and the money."),
        ])

    glimpse = "".join(
        f'<figure>{win(s, f, z)}<figcaption><b>{e(t)}</b>{e(d)}</figcaption></figure>'
        for t, d, s, f, z in [
            ("A night out, booked", "Every ghost walk and haunted weekend near you, with the dates people can take.",
             PUB + "getting-started/tour-page.png", "top right", 1.5),
            ("A case, worked properly", "The client's own words, the timeline, the evidence and one thread.",
             PUB + "working-a-case/case-detail.png", "top right", 1.55),
            ("Voices under the noise", "The EVP detector marks what it heard and you keep what is real.",
             PUB + "using-the-audio-editor/evp-candidates.png", "top left", 1.3),
        ])

    tiles = "".join(
        f'<div class="tile">{win(s, f, z)}'
        f'<div class="th">{icon(i, 13)}<h4>{e(t)}</h4></div><p>{e(d)}</p></div>'
        for i, t, d, s, f, z in TILES)

    steps = []
    for n, (t, d, s, kind) in enumerate(CHAIN):
        art = phone(s) if kind == "phone" else win(s, "top left", 1.5)
        steps.append(f'<div class="cstep">{art}<b>{e(t)}</b><span>{e(d)}</span></div>')
        if n < len(CHAIN) - 1:
            steps.append('<div class="arrow">&#8594;</div>')
    chain = "".join(steps)

    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>IsHaunted.com — the whole platform</title>
<style>{CSS}</style>
<script>document.addEventListener('DOMContentLoaded',()=>{{
  if(location.hash==='#front')document.body.classList.add('only-front');
  if(location.hash==='#back')document.body.classList.add('only-back');}});</script>
</head><body>

<section class="page front">
  <div class="herowrap">
    <div class="hero" style="background-image:url({small(STOCK + "i3-walking-to-house.jpg", "stock")})"></div>
    <div class="brandbar">
      <div class="brand"><img src="{LOGO}" alt=""><div>IsHaunted.com<small>Web · iPhone · iPad</small></div></div>
      <div class="pill">Everything it does</div>
    </div>
    <div class="montage">
      {win(PUB + "getting-started/public-map.png", "center", 1.0)}
      {phone(PUB + "the-mobile-apps/iphone-home.png")}
    </div>
    <div class="headline">
      <div class="kicker">One home for the paranormal</div>
      <h1>Everything the night needs, in <em>one place</em>.</h1>
      <p>Explore haunted places. Run a group and work real cases. Find a voice in a recording,
         cut the clip, and send the client a report. Sell out a haunted weekend. One site — and
         the phone in your pocket is part of it.</p>
    </div>
  </div>

  <div class="fbody">
    <div class="pillars">{pillars}</div>
    <div class="glimpse">
      <div class="lab">A glimpse of the real thing</div>
      <div class="gstrip">{glimpse}</div>
    </div>
  </div>

  <div class="cta">
    <div class="go">Start free at ishaunted.com<span>No card needed to join</span></div>
    <div class="price"><b>Free</b>to join, explore and post</div>
  </div>
</section>

<section class="page back">
  <div class="bstrip">
    <div style="background-image:url({small(STOCK + "i2-dark-hallway.jpg", "stock")})"></div>
    <div style="background-image:url({small(STOCK + "w1-cobblestone-streetlights.jpg", "stock")})"></div>
    <div style="background-image:url({small(STOCK + "v6-ballroom.jpg", "stock")})"></div>
  </div>
  <div class="bbody">
    <div class="bhead">
      <h2>Twelve things it does. <em>None bolted on.</em></h2>
      <p>The editors, the detector and the apps are part of the site rather than separate products —
         so a recording made in a dark hallway reaches a client's report without anybody emailing a file.</p>
    </div>

    <div class="tiles">{tiles}</div>

    <div class="chain">
      <div class="lab">How the phone and the website are one thing</div>
      <p class="sub">The same recording, start to finish — nothing exported, nothing uploaded by hand.</p>
      <div class="cflow">{chain}</div>
    </div>

    <div class="offer">
      <div class="shotcell" style="background-image:url({small(WALK + "owner/56-org-subscriptions.png")})"></div>
      <div class="words">
        <div class="big">Free to <em>explore</em>. Pay only for what you run.</div>
        <div class="small">Enthusiasts: free, with Field Kit and 2&nbsp;GB of your own files · Groups from
          $19.99 a month · Ghost walks $29 a month per tour · Hosted events $99 each, however many
          nights · Investigations of public places stay free on every plan.</div>
      </div>
      <div class="url"><b>ishaunted.com</b><span>Have a look tonight</span></div>
    </div>
    <div class="fine">{e(FINE)}</div>
  </div>
</section>
</body></html>"""


def main():
    out_html = os.path.join(HERE, "flyer-everything.html")
    with open(out_html, "w", encoding="utf-8") as f:
        f.write(build())
    print("wrote", os.path.relpath(out_html, ROOT))

    # Every src must resolve, or Chrome prints a silent gap where a screenshot should be.
    missing = []
    refs = {r for r in __import__("re").findall(r'(?:src="|url\()([^"\)]+)', open(out_html).read())}
    for src in refs:
        if src.startswith(("http", "data:")):
            continue
        if not os.path.exists(os.path.normpath(os.path.join(HERE, src))):
            missing.append(src)
    if missing:
        print("REFUSING to print — these are referenced and not on disk:")
        for m in sorted(missing):
            print("   ", m)
        return 1
    print(f"   all {len(refs)} image references resolve")

    if not os.path.exists(CHROME):
        print("Chrome not found; HTML written, PDF skipped.")
        return 0

    pdf = os.path.join(HERE, "IsHaunted-Flyer.pdf")
    subprocess.run([CHROME, "--headless", "--no-pdf-header-footer", f"--print-to-pdf={pdf}",
                    f"file://{out_html}"], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f"wrote {os.path.relpath(pdf, ROOT)}  ({os.path.getsize(pdf) / 1e6:.1f} MB)")

    if "--png" in sys.argv:
        prev = os.path.join(HERE, "preview")
        os.makedirs(prev, exist_ok=True)
        for side in ("front", "back"):
            shot = os.path.join(prev, f"flyer-{side}.png")
            subprocess.run([CHROME, "--headless", f"--screenshot={shot}", "--window-size=816,1056",
                            "--force-device-scale-factor=2", f"file://{out_html}#{side}"], check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            print("wrote", os.path.relpath(shot, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
