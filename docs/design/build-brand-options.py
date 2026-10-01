#!/usr/bin/env python3
"""Three ways to carry the logo and the wordmark in Signal, for Ben to choose between.

Ben, 2026-10-01, having picked Signal: *"I think I would like the logo appearing somewhere and the
IsHaunted.com may need to pop more. If you don't think we need to point to the logo and feature it,
then dont."*

THE PROBLEM WORTH SEEING RATHER THAN READING

The mark is a white ghost inside a GREEN ring with amber windows (#72A73F, #27602E, #FED46E).
Signal's accents are violet and cyan. Dropped in at full colour it is the only green thing on the
page and it reads as a sticker rather than as the brand — which is the opposite of what Ben asked
for. So the question is not "where does the logo go" but "which logo goes where".

    A — Wordmark forward.  The mark in white in the chrome, the wordmark heavy with .com in the
                           accent. No logo in the hero. Quietest, and the wordmark does the work.
    B — A logo moment.     A's header, plus the mark at full colour and full size over the hero,
                           the way the site does it today. Keeps the real logo, in the one place
                           where it is the subject rather than furniture.
    C — Badge lockup.      The mark white inside a rounded gradient tile, app-icon style, wordmark
                           beside it. Most "product", furthest from the drawn logo.

In A and C the mark is turned white with a CSS filter rather than by making a second asset: it is
one line, it is reversible, and nothing about the brand file changes. The full-colour mark stays
exactly as it is for the favicon, the app icon, the hero in B, and the flyers.

Run from the repository root:

    python3 docs/design/build-brand-options.py --png
"""
import importlib.util, os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

spec = importlib.util.spec_from_file_location("directions", os.path.join(HERE, "build-directions.py"))
D = importlib.util.module_from_spec(spec)
spec.loader.exec_module(D)

BRAND_CSS = """
/* the mark, turned white so it belongs to the chrome rather than fighting it */
.logo img { filter: brightness(0) invert(1); }
.logo { gap: 11px; font-size: 21px; letter-spacing: -.02em; }
.logo img { width: 34px; height: 34px; }
.logo .dot { background: linear-gradient(120deg, var(--accent), var(--accent2));
             -webkit-background-clip: text; background-clip: text; color: transparent; }
.sitefoot .brand { display: flex; align-items: center; gap: 10px; font-weight: 700; color: var(--ink); }
.sitefoot .brand img { width: 24px; height: 24px; filter: brightness(0) invert(1); opacity: .9; }

/* B — the logo as the subject, over the hero */
.herologo { display: block; width: 132px; height: 132px; margin: 0 0 14px;
            filter: drop-shadow(0 10px 34px rgba(0,0,0,.6)); }

/* C — the mark in an app-icon tile */
.logo .tile { width: 38px; height: 38px; border-radius: 11px; display: grid; place-items: center;
              background: linear-gradient(135deg, var(--accent), var(--accent2));
              box-shadow: 0 6px 18px color-mix(in srgb, var(--accent) 50%, transparent); }
.logo .tile img { width: 25px; height: 25px; }
"""

LIGHT_FIX = """
/* in light mode the chrome is pale, so the white mark has to come back to ink */
.logo img { filter: none; }
.sitefoot .brand img { filter: none; }
.logo .tile img { filter: brightness(0) invert(1); }
"""


def wordmark(kind):
    if kind == "c":
        return ('<span class="logo"><span class="tile">'
                '<img src="../assets/is-haunted-logo.svg" alt=""></span>'
                '<span>IsHaunted<span class="dot">.com</span></span></span>')
    return ('<span class="logo"><img src="../assets/is-haunted-logo.svg" alt="">'
            '<span>IsHaunted<span class="dot">.com</span></span></span>')


def build(kind, mode):
    html = D.page("signal", mode)
    html = html.replace(
        '<span class="logo"><img src="../assets/is-haunted-logo.svg" alt="">IsHaunted.com</span>',
        wordmark(kind))
    extra = BRAND_CSS + (LIGHT_FIX if mode == "light" else "")
    html = html.replace("</style>", extra + "</style>", 1)
    if kind == "b":
        html = html.replace('<div class="kicker">One home for the paranormal</div>',
                            '<img class="herologo" src="../assets/is-haunted-logo.svg" alt="">'
                            '<div class="kicker">One home for the paranormal</div>', 1)
    label = {"a": "A · wordmark forward", "b": "B · a logo moment", "c": "C · badge lockup"}[kind]
    return html.replace("Signal · " + mode + " · mock-up, not the live site",
                        f"Signal · {label} · {mode}")


def main():
    made = []
    for kind in ("a", "b", "c"):
        for mode in ("dark", "light"):
            if mode == "light" and kind != "b":
                continue                      # one light sheet is enough to show the mode flips
            path = os.path.join(HERE, f"signal-brand-{kind}-{mode}.html")
            with open(path, "w", encoding="utf-8") as f:
                f.write(build(kind, mode))
            made.append((kind, mode, path))
            print("wrote", os.path.relpath(path, ROOT))

    if "--png" not in sys.argv or not os.path.exists(CHROME):
        return 0
    prev = os.path.join(HERE, "preview")
    os.makedirs(prev, exist_ok=True)
    for kind, mode, path in made:
        shot = os.path.join(prev, f"1{'abc'.index(kind)}-signal-brand-{kind}-{mode}.png")
        subprocess.run([CHROME, "--headless", f"--screenshot={shot}", "--window-size=1280,1000",
                        "--hide-scrollbars", "--virtual-time-budget=4000", f"file://{path}"],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        print("wrote", os.path.relpath(shot, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
