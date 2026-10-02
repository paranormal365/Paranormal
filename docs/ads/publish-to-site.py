#!/usr/bin/env python3
"""Puts the seven advertisements on the website: the PDFs themselves, and a thumbnail of each.

WHY THIS EXISTS

The site serves what is in Ben.Web.Website/wwwroot, and the advertisements are built into
docs/ads. The one PDF that was already on the site — the iPhone guide, which is the App Store
listing's Marketing URL — got there by hand, and docs/README.md carries a warning about it going
stale: "It is a copy, not a link, so after rebuilding it copy it over the site's one too, or the
listing keeps pointing at the old guide."

Seven files is six more chances to make that mistake, so it is a script rather than a warning. It
takes whatever is in docs/ads now, copies it across, and draws the thumbnails from the SAME build
— so a thumbnail can never show a flyer the site is not serving.

The thumbnails are drawn at 128 pixels wide for a 64-pixel box, because the home page is read on
retina screens and a 64-pixel image on one of those is visibly soft.

Run from the repository root, after building the advertisements:

    python3 docs/ads/build-ads.py          # the six audience sheets
    python3 docs/ads/build-flyer.py --png  # the whole-platform flyer
    python3 docs/ads/publish-to-site.py    # put them on the site

It refuses rather than publishing a partial set: a missing flyer on the home page is a dead link,
and a missing preview is a broken image where a thumbnail should be.
"""
import os, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SITE = os.path.join(ROOT, "Ben.Web.Website", "wwwroot", "flyers")
THUMB_WIDTH = 128          # drawn at 2x for a 64px box

# (pdf in docs/ads, its front-page preview, the name it is served under). The order is the order
# FlyerStrip.razor lists them in; the audience wording lives there, with the markup it belongs to.
FLYERS = [
    ("IsHaunted-Flyer.pdf",          "flyer-front.png",        "IsHaunted-Flyer.pdf"),
    ("IsHaunted-Ad-IsHaunted.pdf",   "ishaunted-front.png",    "IsHaunted-Ad-IsHaunted.pdf"),
    ("IsHaunted-Ad-Individuals.pdf", "individuals-front.png",  "IsHaunted-Ad-Individuals.pdf"),
    ("IsHaunted-Ad-Groups.pdf",      "groups-front.png",       "IsHaunted-Ad-Groups.pdf"),
    ("IsHaunted-Ad-Ghost-Walks.pdf", "ghost-walks-front.png",  "IsHaunted-Ad-Ghost-Walks.pdf"),
    ("IsHaunted-Ad-Venues.pdf",      "venues-front.png",       "IsHaunted-Ad-Venues.pdf"),
    ("IsHaunted-Ad-Event-Hosts.pdf", "event-hosts-front.png",  "IsHaunted-Ad-Event-Hosts.pdf"),
]


def main():
    missing = []
    for pdf, preview, _ in FLYERS:
        if not os.path.exists(os.path.join(HERE, pdf)):
            missing.append(f"docs/ads/{pdf} — run build-ads.py / build-flyer.py")
        if not os.path.exists(os.path.join(HERE, "preview", preview)):
            missing.append(f"docs/ads/preview/{preview} — run the builder with --png")
    if missing:
        print("REFUSING to publish a partial set:")
        for m in missing:
            print("   ", m)
        return 1

    os.makedirs(os.path.join(SITE, "thumbs"), exist_ok=True)
    for pdf, preview, served in FLYERS:
        shutil.copy2(os.path.join(HERE, pdf), os.path.join(SITE, served))
        thumb = os.path.join(SITE, "thumbs", served.replace(".pdf", ".png"))
        subprocess.run(["sips", "--resampleWidth", str(THUMB_WIDTH),
                        os.path.join(HERE, "preview", preview), "--out", thumb],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        size = os.path.getsize(os.path.join(SITE, served)) / 1e6
        print(f"   {served:32} {size:4.1f} MB  + thumbs/{os.path.basename(thumb)}")

    total = sum(os.path.getsize(os.path.join(SITE, f)) for f in os.listdir(SITE)
                if f.endswith(".pdf")) / 1e6
    print(f"\n{len(FLYERS)} flyers on the site, {total:.1f} MB in all")
    print("served at /flyers/<name>.pdf and /flyers/thumbs/<name>.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())
