#!/usr/bin/env python3
"""Builds the App Store's product-page header and search-results images (10/13/2026).

Apple added these to App Store Connect in October 2026 (Product Page Information → Header and Search
Results). They are optional, shown on iOS 27 and later, and reviewed apart from the app.

    python3 Ben.iOS/creative-assets/build.py

writes, next to this file:

    header.png          3840 × 1646 (21:9), the banner across the top of the product page
    search-results.png  3840 × 2560 (3:2), the picture beside the app in App Store search

**Matching the site.** The home page's own photograph (Unsplash License, see
ProjectNotes/FeatureHistory/README-hosted-events-235-media.md), the Signal night color and the
violet-to-cyan gradient (Ben.Web.Website/wwwroot/css/themes/signal-tokens.css), the home page's kicker
and headline, set in the site's Public Sans, the violet app icon, and real app screens from the 1.1.2
App Store set.

**The safe areas.** Apple crops these differently on each device. The header's must-see area is a
centered 1646 × 661, and the search image's a centered 2168 × 1030 (from Apple's templates). Everything
that has to be seen is inside them; the phones and the atmosphere run to the edges for wide screens.
`--guides` draws both rectangles, for checking.

**No transparency.** Apple refuses an image with an alpha channel, so the PNGs are saved as RGB.
"""
import pathlib, subprocess, sys, tempfile
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
GUIDES = "--guides" in sys.argv

PHOTO = ROOT / "docs/media/stock/i3-walking-to-house.jpg"
LOGO = ROOT / "Ben.iOS/IsHaunted/Resources/IsHaunted-iOS26-Button.icon/Assets/is-haunted-logo-white.svg"
FONTS = ROOT / "Ben.Web.Website/wwwroot/fonts"
SHOTS = ROOT / "Ben.iOS/screenshots-1.1.2/iphone-6.5-dark"

KICKER = "One home for the paranormal"
HEADLINE = "Find out what happened here."
LEDE = "Haunted places near you, the groups that investigate them, and the nights you can book."


def font_faces():
    """The site's own @font-face rules, pointed at the files on disk."""
    css = (FONTS / "fonts.css").read_text()
    return css.replace("url(/fonts/", f"url({FONTS.as_uri()}/")


BASE_CSS = """
* { box-sizing: border-box; margin: 0; padding: 0; }
html, body { width: %(w)dpx; height: %(h)dpx; overflow: hidden; background: #0C1017; }
body { position: relative; font-family: 'Public Sans', -apple-system, sans-serif; color: #E9EDF4; }

/* The home page's photograph, softened into the night: it is a backdrop, never the subject. */
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

/* An iPhone, drawn: a near-black body, a hairline edge, and the real screen inside. */
.phone { position: absolute; aspect-ratio: 1242 / 2688; border-radius: 15.5%% / 7.2%%; background: #05070B;
         box-shadow: 0 60px 140px rgba(0, 0, 0, .65), 0 0 0 3px rgba(255, 255, 255, .10),
         0 0 120px rgba(124, 92, 255, .22); }
.phone img { width: 100%%; height: 100%%; display: block; border-radius: 13.5%% / 6.25%%; }
.phone.dim { filter: brightness(.55) saturate(.8); }

.guide { position: absolute; outline: 4px dashed #FF3B6B; z-index: 99; }
"""


def trace_svg(width, height, seed):
    """A faint magnetometer trace: flat, then the spikes a session marks."""
    import random
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
            f"<img src='{(SHOTS / shot).as_uri()}'></div>")


def page(w, h, body, safe):
    css = BASE_CSS % {"w": w, "h": h, "photo": PHOTO.as_uri()}
    guide = ""
    if GUIDES:
        sw, sh = safe
        guide = f"<div class='guide' style='left:{(w - sw) / 2}px;top:{(h - sh) / 2}px;width:{sw}px;height:{sh}px'></div>"
    return (f"<!doctype html><html><head><meta charset='utf-8'><style>{font_faces()}{css}</style></head>"
            f"<body><div class='photo'></div><div class='night'></div><div class='grain'></div>{body}{guide}</body></html>")


def header():
    w, h = 3840, 1646
    body = f"""
    <div class='trace' style='top:{h - 520}px'>{trace_svg(w, 360, 7)}</div>
    {phone('01-home.png', -240, 300, 1500, rotate=-9, dim=True)}
    {phone('13-fieldkit-review.png', 300, 190, 1560, rotate=-4)}
    {phone('04-field-kit.png', 3400, 300, 1500, rotate=9, dim=True)}
    {phone('11-fieldkit-live-excursion.png', 2840, 190, 1560, rotate=4)}
    <div style='position:absolute;left:0;right:0;top:0;bottom:0;display:flex;flex-direction:column;
                align-items:center;justify-content:center;text-align:center;z-index:5'>
      <div class='icon' style='width:250px;height:250px;margin-bottom:46px'><img src='{LOGO.as_uri()}'></div>
      <div class='kicker' style='font-size:40px;margin-bottom:22px'>{KICKER}</div>
      <div class='headline' style='font-size:92px'>Find out what <em>happened here.</em></div>
    </div>"""
    return w, h, page(w, h, body, (1646, 661))


def search_results():
    w, h = 3840, 2560
    # The safe area is a centered 2168 × 1030: x 836–3004, y 765–1795.
    body = f"""
    <div class='trace' style='top:{h - 760}px'>{trace_svg(w, 420, 11)}</div>
    {phone('01-home.png', -60, 620, 1460, rotate=-8, dim=True)}
    {phone('05-events.png', 3380, 620, 1460, rotate=8, dim=True)}
    {phone('13-fieldkit-review.png', 2090, 700, 1160, rotate=-6, z=2)}
    {phone('11-fieldkit-live-excursion.png', 2420, 660, 1240, rotate=4, z=3)}
    <div style='position:absolute;left:900px;top:765px;width:1260px;height:1030px;display:flex;
                flex-direction:column;justify-content:center;z-index:5'>
      <div class='icon' style='width:210px;height:210px;margin-bottom:44px'><img src='{LOGO.as_uri()}'></div>
      <div class='kicker' style='font-size:38px;margin-bottom:24px'>{KICKER}</div>
      <div class='headline' style='font-size:120px;margin-bottom:34px'>Find out what<br><em>happened here.</em></div>
      <div class='lede' style='font-size:46px;max-width:1080px'>{LEDE}</div>
    </div>"""
    return w, h, page(w, h, body, (2168, 1030))


def render(name, w, h, html):
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
        sys.exit(f"{name}: Chrome drew {image.size}, not {(w, h)}")
    out = HERE / (f"{name}-guides.png" if GUIDES else f"{name}.png")
    image.save(out, optimize=True)
    print(f"wrote {out.relative_to(ROOT)} {image.size[0]} × {image.size[1]}, {image.mode}")


if __name__ == "__main__":
    for name, build in (("header", header), ("search-results", search_results)):
        render(name, *build())
