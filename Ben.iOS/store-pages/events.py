#!/usr/bin/env python3
"""The App Store In-App Events for a version, and their artwork (10/15/2026).

    python3 Ben.iOS/store-pages/events.py 1.1.4

writes `screenshots-<version>/in-app-events/<event>/card.png` (1920 × 1080, the card on the Today tab
and in search), `details.png` (1080 × 1920, the event's own page) and `README.md` with every field App
Store Connect asks for. The artwork carries no headline: Apple prints the event's name, badge and
description with it, so words in the picture would say everything twice.
"""
import pathlib, shutil, sys
from build import CSS, chrome, font_faces, IOS, ROOT
from PIL import Image

STOCK = ROOT / "docs/media/stock"
LOGO = IOS / "IsHaunted/Resources/IsHaunted-iOS26-Button.icon/Assets/is-haunted-logo-white.svg"

EVENTS = [
    dict(
        key="halloween-ghost-walks",
        name="Halloween Ghost Walks",
        badge="Special Event",
        short="Find a haunted walk or night near you",
        long="Ghost walks, haunted dinners and investigation nights near you this Halloween. Reserve your place in a tap.",
        start="10/17/2026 12:00 AM", end="10/31/2026 11:59 PM", publish="10/17/2026",
        link="https://ishaunted.com/events",
        note="ON HOLD (10/15/2026): the live site lists no public events. Submit once a group lists real Halloween walks or nights.",
        photo="w2-cobblestone-night.jpg",
        screens=["05-events.png", "iphone-event-pass.png"],
    ),
    dict(
        key="investigate-on-iphone-duo",
        name="Investigate on iPhone Duo",
        badge="Major Update",
        short="Your investigation controls, down the side",
        long="Open your iPhone Duo: the gauge fills the screen, and every control for the night sits in a column beside it.",
        start="10/23/2026 12:00 AM", end="11/21/2026 11:00 PM", publish="10/23/2026",
        link="ishaunted://paranormal",
        note="Saved as a draft 10/15/2026 (Apple caps an event at 31 days, so it ends 11/21). The link opens the Paranormal tab only in 1.1.4: press Add for Review once 1.1.4 is approved.",
        photo="i1-flashlight-silhouette.jpg",
        duo="duo-10-listening-landscape.png",
        duo2="duo-07-experience-list-landscape.png",
    ),
    dict(
        key="halloween-night-investigation",
        name="Record Halloween Night",
        badge="Special Event",
        short="Investigate a haunted place on Halloween",
        long="Record the magnetic field, sound and video on Halloween night, then add your session to the place for others to see.",
        start="10/31/2026 6:00 PM", end="11/01/2026 6:00 AM", publish="10/24/2026",
        link="ishaunted://paranormal",
        note="Submitted 10/15/2026 (Apple requires a deep link). In 1.1.3 the link just opens the app; from 1.1.4 it opens the Paranormal tab.",
        photo="w3-foggy-graveyard.jpg",
        screens=["11-fieldkit-live-excursion.png", "13-fieldkit-review.png"],
    ),
]

LIMITS = {"name": 30, "short": 50, "long": 120}

ART_CSS = """
.photo { position:absolute; inset:-30px; background:url('%(photo)s') center/cover; filter:blur(3px) brightness(.55) saturate(.85); }
.night { position:absolute; inset:0; background:
  radial-gradient(ellipse 50%% 60%% at 25%% 30%%, rgba(124,92,255,.45), transparent 70%%),
  radial-gradient(ellipse 45%% 55%% at 80%% 75%%, rgba(34,211,238,.25), transparent 70%%),
  linear-gradient(180deg, rgba(12,16,23,.2), rgba(12,16,23,.75)); }
.icon { position:absolute; border-radius:22.37%%; background:linear-gradient(170deg,#8A70FF,#5B3DF5 55%%,#4A2EE0);
        box-shadow:0 30px 80px rgba(91,61,245,.55); display:grid; place-items:center; }
.icon img { width:82%%; height:82%%; }
.phone { position:absolute; border-radius:15.5%% / 7.2%%; background:#05070B; overflow:hidden;
         box-shadow:0 40px 110px rgba(0,0,0,.65), 0 0 0 3px rgba(255,255,255,.1), 0 0 120px rgba(124,92,255,.25);
         -webkit-mask-image:linear-gradient(180deg,#000 0%%,#000 80%%,transparent 92%%); }
.phone img { width:100%%; height:100%%; object-fit:cover; object-position:top; display:block; }
.duo { position:absolute; border-radius:36px; overflow:hidden; background:#05070B;
       box-shadow:0 40px 110px rgba(0,0,0,.65), 0 0 0 3px rgba(255,255,255,.1), 0 0 140px rgba(124,92,255,.3); }
.duo img { width:100%%; height:100%%; object-fit:cover; display:block; }
"""


def art(event, raw, w, h, out):
    css = CSS % {"w": w, "h": h} + ART_CSS % {"photo": (STOCK / event["photo"]).as_uri()}
    portrait = h > w
    parts = ["<div class='photo'></div><div class='night'></div>"]
    icon = 150 if not portrait else 170
    parts.append(f"<div class='icon' style='width:{icon}px;height:{icon}px;left:{70 if not portrait else (w - icon) / 2}px;"
                 f"top:{70 if not portrait else 140}px'><img src='{LOGO.as_uri()}'></div>")
    if "duo" in event:
        img = (IOS / "screenshots-1.1.4/raw/duo-walkthrough" / event["duo"]).as_uri()
        if portrait:
            # Upright, the Duo's wide screen is cropped to its right half: the column of controls and the panels.
            # Upright: two whole screens, one above the other, so nothing is cropped or sideways.
            img2 = (IOS / "screenshots-1.1.4/raw/duo-walkthrough" / event.get("duo2", event["duo"])).as_uri()
            dw = 980; dh = dw * 2007 / 2853
            for i, src in enumerate((img, img2)):
                parts.append(f"<div class='duo' style='left:{(w - dw) / 2:.0f}px;top:{420 + i * (dh + 60):.0f}px;"
                             f"width:{dw}px;height:{dh:.0f}px'><img src='{src}'></div>")
        else:
            parts.append(f"<div class='duo' style='left:330px;top:150px;width:1450px;height:{1450 * 2007 / 2853:.0f}px'><img src='{img}'></div>")
    else:
        a, b = (raw / s for s in event["screens"])
        if portrait:
            ph = 1340
            parts.append(f"<div class='phone' style='left:20px;top:440px;height:{ph}px;width:{ph * 1242 / 2688:.0f}px;transform:rotate(-6deg)'><img src='{a.as_uri()}'></div>")
            parts.append(f"<div class='phone' style='right:20px;top:560px;height:{ph}px;width:{ph * 1242 / 2688:.0f}px;transform:rotate(5deg)'><img src='{b.as_uri()}'></div>")
        else:
            ph = 1100
            parts.append(f"<div class='phone' style='left:760px;top:120px;height:{ph}px;width:{ph * 1242 / 2688:.0f}px;transform:rotate(-6deg)'><img src='{a.as_uri()}'></div>")
            parts.append(f"<div class='phone' style='left:1220px;top:200px;height:{ph}px;width:{ph * 1242 / 2688:.0f}px;transform:rotate(5deg)'><img src='{b.as_uri()}'></div>")
    html = f"<!doctype html><html><head><meta charset='utf-8'><style>{font_faces()}{css}</style></head><body>{''.join(parts)}</body></html>"
    chrome(html, w, h, out)
    Image.open(out).convert("RGB").save(out, optimize=True)


def readme(e):
    rows = [f"# {e['name']}", "",
            "| Field in App Store Connect | Value |", "|---|---|",
            f"| Reference name | {e['name']} |",
            f"| Event name ({len(e['name'])}/30) | {e['name']} |",
            f"| Badge | {e['badge']} |",
            f"| Short description ({len(e['short'])}/50) | {e['short']} |",
            f"| Long description ({len(e['long'])}/120) | {e['long']} |",
            f"| Starts | {e['start']} |", f"| Ends | {e['end']} |", f"| Publish on the App Store | {e['publish']} |",
            f"| Deep link | `{e['link']}` |" if e["link"] else "| Deep link | (none: the event opens the app) |",
            "| Event card (16:9) | `card.png`, 1920 × 1080 |",
            "| Event details page (9:16) | `details.png`, 1080 × 1920 |",
            "", f"**Before submitting:** {e['note']}", ""]
    return "\n".join(rows)


def main(version):
    raw = IOS / f"screenshots-{version}/raw/iphone"
    base = IOS / f"screenshots-{version}/in-app-events"
    for e in EVENTS:
        for field, limit in LIMITS.items():
            assert len(e[field]) <= limit, f"{e['key']}: {field} is {len(e[field])} characters, over {limit}"
        folder = base / e["key"]
        shutil.rmtree(folder, ignore_errors=True)
        folder.mkdir(parents=True)
        art(e, raw, 1920, 1080, folder / "card.png")
        art(e, raw, 1080, 1920, folder / "details.png")
        (folder / "README.md").write_text(readme(e))
        print(f"wrote {folder.relative_to(IOS)}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "1.1.4")
