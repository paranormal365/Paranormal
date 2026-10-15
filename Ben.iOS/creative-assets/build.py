#!/usr/bin/env python3
"""Builds the App Store's product-page headers and search-results images (10/13/2026, pages 10/15).

Apple added these to App Store Connect in October 2026: a header across the top of a product page and
a picture beside the app in search, kept in the Asset Library and usable on the main product page and on
each custom product page. They are optional, shown on iOS 27 and later, and reviewed apart from the app.

    python3 Ben.iOS/creative-assets/build.py            # every page
    python3 Ben.iOS/creative-assets/build.py events     # one page
    python3 Ben.iOS/creative-assets/build.py --guides   # draw the safe areas, for checking

writes, for each page in PAGES, `<page>/header.png` (3840 × 1646, 21:9) and `<page>/search-results.png`
(3840 × 2560, 3:2) next to this file. What each page is for, and the words that go with it in App Store
Connect, are in README.md.

**Matching the site.** Unsplash photographs already licensed for the site (see
ProjectNotes/FeatureHistory/README-hosted-events-235-media.md), the Signal night color and violet-to-cyan
gradient (Ben.Web.Website/wwwroot/css/themes/signal-tokens.css), the site's Public Sans, the violet app
icon, and real app screens.

**The safe areas.** Apple crops these differently on each device. The header's must-see area is a
centered 1646 × 661, and the search image's a centered 2168 × 1030 (from Apple's templates). The icon
and the words are always inside them; the phones run to the edges for wide screens.

**The phones fade out before their tab bar.** A tab's name changes with the app (Field Kit became
Paranormal in 1.1.4), and a picture in the store should not go out of date when it does, so no tab bar is
ever shown. The screens themselves are untouched.

**No transparency.** Apple refuses an image with an alpha channel, so the PNGs are saved as RGB.
"""
import pathlib, random, subprocess, sys, tempfile
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
GUIDES = "--guides" in sys.argv

STOCK = ROOT / "docs/media/stock"
LOGO = ROOT / "Ben.iOS/IsHaunted/Resources/IsHaunted-iOS26-Button.icon/Assets/is-haunted-logo-white.svg"
FONTS = ROOT / "Ben.Web.Website/wwwroot/fonts"
STORE = ROOT / "Ben.iOS/screenshots-1.1.4/iphone-6.5-dark"   # the App Store set, 1242 × 2688
GUIDE = ROOT / "docs/ios-media/iphone"                       # the app guide's captures, 1206 × 2622
HELP = ROOT / "Ben.Web.Website/wwwroot/help/media"           # the website's help captures, 2880 × 1800
WORK = pathlib.Path(tempfile.mkdtemp(prefix="creative-"))     # cropped website captures, for this run


# Every page places its phones the same way, so a page only names its four screens.
# A header's phones sit outside its safe area: two dimmed at the far edges, two beside the words.
HEADER_AT = [(-240, 300, 1500, -9, True), (300, 230, 1560, -4, False),
             (3400, 300, 1500, 9, True), (2840, 230, 1560, 4, False)]
# A search image's: two dimmed at the edges, then the featured pair beside the words, the front one last.
SEARCH_AT = [(-60, 620, 1460, -8, True), (3380, 620, 1460, 8, True),
             (2090, 700, 1160, -6, False), (2420, 660, 1240, 4, False)]


def S(name):
    """A screen from the App Store set if it has one, otherwise from the app guide's captures."""
    return STORE / name if (STORE / name).exists() else GUIDE / name


# Each page: its photograph and words, its header screens (far left, beside the words on the left, far
# right, beside the words on the right), and its search screens (far left, far right, back, front).
# Only what the app itself does: where the work goes on, the website is named as the place it does.
PAGES = {
    # The main product page: the site's own front door.
    "main": dict(
        photo="i3-walking-to-house.jpg", kicker="One home for the paranormal",
        headline=("Find out what", "happened here."),
        lede="Haunted places near you, the groups that investigate them, and the nights you can book.",
        header=["01-home.png", "13-fieldkit-review.png", "04-field-kit.png", "11-fieldkit-live-excursion.png"],
        search=["01-home.png", "05-events.png", "13-fieldkit-review.png", "11-fieldkit-live-excursion.png"],
    ),
    # Anyone starting out, on their own. (Not called free: the page is about what they can do.)
    "explorers": dict(
        photo="w3-foggy-graveyard.jpg", kicker="Start investigating",
        headline=("Go see for", "yourself."),
        lede="Record a night at a haunted place, share what you found, and follow the people and groups near you.",
        header=["71-my-evidence.png", "11-fieldkit-live-excursion.png", "04-field-kit.png", "01-home.png"],
        search=["71-my-evidence.png", "04-field-kit.png", "11-fieldkit-live-excursion.png", "01-home.png"],
    ),
    # Investigators: the Paranormal Platform, recording and reviewing a night.
    "investigators": dict(
        photo="i1-flashlight-silhouette.jpg", kicker="The Paranormal Platform",
        headline=("Record the", "whole night."),
        lede="Magnetic field, sound, video and location on one timeline, with every moment you mark.",
        header=["12-fieldkit-marks.png", "11-fieldkit-live-excursion.png", "10-fieldkit-pending-base-set.png", "13-fieldkit-review.png"],
        search=["12-fieldkit-marks.png", "14-fieldkit-trim.png", "13-fieldkit-review.png", "11-fieldkit-live-excursion.png"],
    ),
    # Investigation groups: cases, visits, and the team's sessions.
    "groups": dict(
        photo="v2-victorian-mansion.jpg", kicker="For investigation groups",
        headline=("Every case,", "in your pocket."),
        lede="Your group's cases and visits, the people to contact, and the sessions your team records there.",
        header=["02-cases.png", "21-case-detail.png", "57-join-ask.png", "13-fieldkit-review.png"],
        search=["02-cases.png", "57-join-ask.png", "21-case-detail.png", "13-fieldkit-review.png"],
    ),
    # Researchers: what the phone keeps for the research board on the website.
    "researchers": dict(
        photo="i2-dark-hallway.jpg", kicker="For researchers",
        web=("working-a-case/board-family-tree.png", None),
        headline=("Keep every", "detail."),
        lede="Notes, photos, marks and readings stamped with the time and place, ready for your research on IsHaunted.com.",
        header=["46-note-typed.png", "44-marked.png", "21-case-detail.png", "13-fieldkit-review.png"],
        search=["46-note-typed.png", "21-case-detail.png", "44-marked.png", "13-fieldkit-review.png"],
    ),
    # Audio and EVP work.
    "audio": dict(
        photo="s1-candles.jpg", kicker="For audio and EVP",
        web=("using-the-audio-editor/editor-overview.png", None),
        headline=("Ask a question.", "Then listen."),
        lede="EVP sessions that record every question, sound measured against your base, and playback marked where you asked.",
        header=["42b-meter-with-needle.png", "47-evp-mode.png", "44-marked.png", "13-fieldkit-review.png"],
        search=["42b-meter-with-needle.png", "44-marked.png", "47-evp-mode.png", "13-fieldkit-review.png"],
    ),
    # Video: recorded with the readings, trimmed on the phone, finished on the website.
    "video": dict(
        photo="p1-phone-camera.jpg", kicker="For video",
        # Cropped below two notices the capture happened to catch; the editor itself is untouched.
        web=("using-the-video-editor/timeline-two-clips.png", (0, 300, 2880, 1800)),
        headline=("Film it. Trim it.", "Send it."),
        lede="Video recorded with every reading, trimmed on the phone before it goes, and finished on IsHaunted.com.",
        header=["12-fieldkit-marks.png", "14-fieldkit-trim.png", "50-sentry-panel.png", "56-launch-send-to.png"],
        search=["12-fieldkit-marks.png", "50-sentry-panel.png", "56-launch-send-to.png", "14-fieldkit-trim.png"],
    ),
    # Haunted places open to the public, and the sessions people publish there.
    "public-places": dict(
        photo="v1-venue-exterior.jpg", kicker="Haunted places",
        headline=("See what others", "recorded here."),
        lede="Find the sessions people published at public places near you, then add your own night to the place.",
        header=["01-home.png", "04-field-kit.png", "71-my-evidence.png", "13-fieldkit-review.png"],
        search=["01-home.png", "71-my-evidence.png", "13-fieldkit-review.png", "04-field-kit.png"],
    ),
    # People going to a ghost walk or a hosted event.
    "events": dict(
        photo="d1-candlelit-dinner.jpg", kicker="Haunted nights out",
        headline=("Your night,", "in your pocket."),
        lede="Reserve a ghost walk, carry your pass with no signal, and see the program and the menus.",
        header=["64-event-programme.png", "62-event-pass.png", "63-event-hub.png", "65-event-menus.png"],
        search=["64-event-programme.png", "63-event-hub.png", "05-events.png", "62-event-pass.png"],
    ),
    # The people running a hosted event: the door, and what guests carry.
    "event-hosts": dict(
        photo="v6-ballroom.jpg", kicker="For event hosts",
        headline=("Run the door", "from your phone."),
        lede="Scan passes and check in parties, even with no signal, while guests carry their program and menus.",
        header=["68-door-reservation.png", "66-door.png", "64-event-programme.png", "65-event-menus.png"],
        search=["65-event-menus.png", "64-event-programme.png", "68-door-reservation.png", "66-door.png"],
    ),
    # Ghost walk tours: guests reserve, the guide starts a session, everyone joins it.
    "ghost-walks": dict(
        photo="w2-cobblestone-night.jpg", kicker="For ghost walk tours",
        headline=("Lead the walk.", "Everyone records."),
        lede="Guests reserve a walk, join the session the guide starts, and send what they recorded to the group.",
        header=["05-events.png", "04-field-kit.png", "57-join-ask.png", "56-launch-send-to.png"],
        search=["05-events.png", "57-join-ask.png", "56-launch-send-to.png", "04-field-kit.png"],
    ),
}


def font_faces():
    """The site's own @font-face rules, pointed at the files on disk."""
    css = (FONTS / "fonts.css").read_text()
    return css.replace("url(/fonts/", f"url({FONTS.as_uri()}/")


BASE_CSS = """
* { box-sizing: border-box; margin: 0; padding: 0; }
html, body { width: %(w)dpx; height: %(h)dpx; overflow: hidden; background: #0C1017; }
body { position: relative; font-family: 'Public Sans', -apple-system, sans-serif; color: #E9EDF4; }

/* The page's photograph, softened into the night: a backdrop, never the subject. */
.photo { position: absolute; inset: -40px; background: url('%(photo)s') center 40%% / cover no-repeat;
         filter: blur(5px) brightness(.62) saturate(.85); }
.night { position: absolute; inset: 0;
         background:
           radial-gradient(ellipse 42%% 60%% at 22%% 38%%, rgba(124, 92, 255, .42), transparent 70%%),
           radial-gradient(ellipse 38%% 55%% at 80%% 66%%, rgba(34, 211, 238, .24), transparent 70%%),
           radial-gradient(ellipse 70%% 80%% at 50%% 50%%, transparent 30%%, rgba(12, 16, 23, .78) 100%%),
           linear-gradient(180deg, rgba(12, 16, 23, .35), rgba(12, 16, 23, .55) 60%%, rgba(12, 16, 23, .92)); }
.grain { position: absolute; inset: 0; opacity: .10; mix-blend-mode: overlay;
         background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='400' height='400'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='.9' numOctaves='2' stitchTiles='stitch'/></filter><rect width='100%%' height='100%%' filter='url(%%23n)'/></svg>"); }
.trace { position: absolute; left: 0; width: 100%%; opacity: .55; }

.icon { border-radius: 22.37%%; background: linear-gradient(170deg, #8A70FF 0%%, #5B3DF5 55%%, #4A2EE0 100%%);
        box-shadow: 0 30px 80px rgba(91, 61, 245, .55), inset 0 2px 0 rgba(255, 255, 255, .25);
        display: grid; place-items: center; }
.icon img { width: 82%%; height: 82%%; }
.kicker { font-weight: 700; text-transform: uppercase; letter-spacing: .16em; color: #22D3EE; }
.headline { font-weight: 800; letter-spacing: -.028em; line-height: 1.02; color: #FFFFFF;
            text-shadow: 0 6px 40px rgba(0, 0, 0, .55); }
.headline em { font-style: normal; background: linear-gradient(120deg, #A78BFA, #22D3EE);
               -webkit-background-clip: text; background-clip: text; color: transparent; }
.lede { font-weight: 500; color: #C3CBD8; line-height: 1.32; }

/* An iPhone, drawn: a near-black body, a hairline edge, and the real screen inside. It fades out above
   its tab bar, whose names change with the app. */
.phone { position: absolute; border-radius: 15.5%% / 7.2%%; background: #05070B;
         box-shadow: 0 60px 140px rgba(0, 0, 0, .65), 0 0 0 3px rgba(255, 255, 255, .10),
         0 0 120px rgba(124, 92, 255, .22);
         -webkit-mask-image: linear-gradient(180deg, #000 0%%, #000 78%%, transparent 89%%);
                 mask-image: linear-gradient(180deg, #000 0%%, #000 78%%, transparent 89%%); }
.phone img { width: 100%%; height: 100%%; display: block; object-fit: cover; object-position: top;
             border-radius: 13.5%% / 6.25%%; }
.phone.dim { filter: brightness(.55) saturate(.8); }

.laptop { position: absolute; }
.lid { aspect-ratio: 16 / 10; background: #05070B; box-shadow: 0 60px 140px rgba(0, 0, 0, .65),
       0 0 0 3px rgba(255, 255, 255, .10), 0 0 120px rgba(34, 211, 238, .18); }
.lid img { width: 100%%; height: 100%%; display: block; object-fit: cover; object-position: top left; }
.base { background: linear-gradient(180deg, #3A4150, #151A23); border-radius: 0 0 50%% 50%% / 0 0 100%% 100%%; }
.on-web { display: inline-block; margin-top: 34px; padding: .45em 1.1em; border-radius: 999px; font-weight: 700;
          letter-spacing: .04em; color: #FFFFFF; background: linear-gradient(120deg, rgba(124, 92, 255, .85), rgba(34, 211, 238, .85));
          box-shadow: 0 10px 40px rgba(0, 0, 0, .45); }
.guide { position: absolute; outline: 4px dashed #FF3B6B; z-index: 99; }
"""


def trace_svg(width, height, seed):
    """A faint magnetometer trace: flat, then the spikes a session marks."""
    random.seed(seed)
    y0 = height * .5
    points, x = [], 0
    while x <= width:
        jitter = random.uniform(-6, 6)
        spike = 0
        if random.random() < .035:
            spike = random.choice([-1, 1]) * random.uniform(height * .18, height * .42)
        points.append(f"{x:.0f},{y0 + jitter + spike:.0f}")
        x += 26
    return (f"<svg viewBox='0 0 {width} {height}' width='{width}' height='{height}' "
            f"xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g' x1='0' x2='1'>"
            f"<stop offset='0' stop-color='#7C5CFF' stop-opacity='0'/><stop offset='.25' stop-color='#7C5CFF'/>"
            f"<stop offset='.75' stop-color='#22D3EE'/><stop offset='1' stop-color='#22D3EE' stop-opacity='0'/>"
            f"</linearGradient></defs><polyline points='{' '.join(points)}' fill='none' stroke='url(#g)' "
            f"stroke-width='4' stroke-linejoin='round'/></svg>")


def phone(shot, left, top, height, rotate=0, dim=False, z=1):
    width = height * 1242 / 2688
    # Padding in pixels: a percentage would be measured against the PAGE's width, not the phone's.
    bezel = width * .028
    return (f"<div class='phone{' dim' if dim else ''}' style='left:{left}px;top:{top}px;height:{height}px;"
            f"width:{width:.0f}px;padding:{bezel:.0f}px;transform:rotate({rotate}deg);z-index:{z}'>"
            f"<img src='{shot.as_uri()}'></div>")


def phones(spec):
    # Later phones sit on top; the last two of a search image are the featured pair.
    return "".join(phone(*p, z=i + 1) for i, p in enumerate(spec))


def web_screen(p):
    """The page's website capture, cropped if it asks to be (to leave out what isn't the feature)."""
    name, crop = p["web"]
    src = HELP / name
    if not crop:
        return src.as_uri()
    out = WORK / name.replace("/", "-")
    Image.open(src).convert("RGB").crop(crop).save(out)
    return out.as_uri()


def laptop(p, left, top, width, label_size, z=5):
    """A laptop showing the website, labeled, for a page whose work goes on at IsHaunted.com."""
    bezel, base = width * .022, width * .045
    return (f"<div class='laptop' style='left:{left}px;top:{top}px;width:{width}px;z-index:{z};text-align:left'>"
            f"<div class='lid' style='padding:{bezel:.0f}px;border-radius:{width * .03:.0f}px {width * .03:.0f}px 0 0'>"
            f"<img src='{web_screen(p)}' style='border-radius:{width * .012:.0f}px'></div>"
            f"<div class='base' style='height:{base:.0f}px;margin:0 -{width * .07:.0f}px'></div>"
            f"<div class='on-web' style='font-size:{label_size}px'>On IsHaunted.com</div></div>")


def page(w, h, photo, body, safe):
    css = BASE_CSS % {"w": w, "h": h, "photo": (STOCK / photo).as_uri()}
    guide = ""
    if GUIDES:
        sw, sh = safe
        guide = f"<div class='guide' style='left:{(w - sw) / 2}px;top:{(h - sh) / 2}px;width:{sw}px;height:{sh}px'></div>"
    return (f"<!doctype html><html><head><meta charset='utf-8'><style>{font_faces()}{css}</style></head>"
            f"<body><div class='photo'></div><div class='night'></div><div class='grain'></div>{body}{guide}</body></html>")


def header_art(p):
    if "web" not in p:
        return phones([(S(n), *at) for n, at in zip(p["header"], HEADER_AT)])
    # The left two phones as usual; on the right, outside the safe area, the laptop with a phone in front.
    left = [(S(n), *at) for n, at in zip(p["header"][:2], HEADER_AT[:2])]
    return (phones(left) + laptop(p, 2800, 330, 1000, 40)
            + phone(S(p["header"][3]), 3330, 560, 1120, 5, False, z=6))


def search_art(p):
    if "web" not in p:
        return phones([(S(n), *at) for n, at in zip(p["search"], SEARCH_AT)])
    # The dimmed phones at the edges; beside the words, the laptop with a phone in front of it.
    edges = [(S(n), *at) for n, at in zip(p["search"][:2], SEARCH_AT[:2])]
    return (phones(edges) + laptop(p, 2180, 860, 860, 36)
            + phone(S(p["search"][3]), 2720, 1040, 820, 4, False, z=6))


def header(p):
    w, h = 3840, 1646
    first, second = p["headline"]
    body = f"""
    <div class='trace' style='top:{h - 520}px'>{trace_svg(w, 360, 7)}</div>
    {header_art(p)}
    <div style='position:absolute;left:0;right:0;top:0;bottom:0;display:flex;flex-direction:column;
                align-items:center;justify-content:center;text-align:center;z-index:10'>
      <div class='icon' style='width:250px;height:250px;margin-bottom:46px'><img src='{LOGO.as_uri()}'></div>
      <div class='kicker' style='font-size:40px;margin-bottom:22px'>{p["kicker"]}</div>
      <div class='headline' style='font-size:92px'>{first} <em>{second}</em></div>
    </div>"""
    return w, h, page(w, h, p["photo"], body, (1646, 661))


def search_results(p):
    w, h = 3840, 2560
    first, second = p["headline"]
    # The safe area is a centered 2168 × 1030: x 836–3004, y 765–1795.
    body = f"""
    <div class='trace' style='top:{h - 760}px'>{trace_svg(w, 420, 11)}</div>
    {search_art(p)}
    <div style='position:absolute;left:900px;top:765px;width:1260px;height:1030px;display:flex;
                flex-direction:column;justify-content:center;z-index:10'>
      <div class='icon' style='width:210px;height:210px;margin-bottom:44px'><img src='{LOGO.as_uri()}'></div>
      <div class='kicker' style='font-size:38px;margin-bottom:24px'>{p["kicker"]}</div>
      <div class='headline' style='font-size:120px;margin-bottom:34px'>{first}<br><em>{second}</em></div>
      <div class='lede' style='font-size:46px;max-width:1080px'>{p["lede"]}</div>
    </div>"""
    return w, h, page(w, h, p["photo"], body, (2168, 1030))


def render(folder, name, w, h, html):
    with tempfile.TemporaryDirectory() as tmp:
        src = pathlib.Path(tmp) / f"{name}.html"
        src.write_text(html)
        shot = pathlib.Path(tmp) / f"{name}.png"
        subprocess.run([CHROME, "--headless", "--hide-scrollbars", "--force-device-scale-factor=1",
                        "--allow-file-access-from-files", "--virtual-time-budget=4000",
                        f"--window-size={w},{h}", f"--screenshot={shot}", src.as_uri()],
                       check=True, capture_output=True)
        image = Image.open(shot).convert("RGB")
    if image.size != (w, h):
        sys.exit(f"{folder.name}/{name}: Chrome drew {image.size}, not {(w, h)}")
    folder.mkdir(exist_ok=True)
    out = folder / (f"{name}-guides.png" if GUIDES else f"{name}.png")
    image.save(out, optimize=True)
    print(f"wrote {out.relative_to(ROOT)} {image.size[0]} × {image.size[1]}, {image.mode}")


if __name__ == "__main__":
    import atexit, shutil
    atexit.register(shutil.rmtree, WORK, ignore_errors=True)
    wanted = [a for a in sys.argv[1:] if not a.startswith("--")] or list(PAGES)
    for key in wanted:
        p = PAGES[key]
        missing = [n for n in p["header"] + p["search"] if not S(n).exists()] + \
                  ([p["photo"]] if not (STOCK / p["photo"]).exists() else [])
        if missing:
            sys.exit(f"{key}: missing {missing}")
        for name, build in (("header", header), ("search-results", search_results)):
            render(HERE / key, name, *build(p))
