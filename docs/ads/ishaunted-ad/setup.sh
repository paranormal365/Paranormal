#!/bin/sh
# One-time setup for build.py on a Mac with Homebrew (Ben, 10/05/2026).
#   - ffmpeg + cairo (+ pkgconf to build pycairo) from Homebrew
#   - a throwaway venv at ~/.cache/ishaunted-ad; the system Python is not touched
#   - Irish Grover and Public Sans (Regular + Bold) into ~/Library/Fonts, from the site's own woff2 files
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$HERE/../../.." && pwd)
VENV="$HOME/.cache/ishaunted-ad"
export PATH="/opt/homebrew/bin:$PATH"

brew list ffmpeg >/dev/null 2>&1 && brew list cairo >/dev/null 2>&1 && brew list pkgconf >/dev/null 2>&1 \
    || brew install ffmpeg cairo pkgconf

[ -x "$VENV/bin/python" ] || /usr/bin/python3 -m venv "$VENV"
"$VENV/bin/pip" install -q --upgrade pip
PKG_CONFIG_PATH="/opt/homebrew/lib/pkgconfig:$PKG_CONFIG_PATH" \
    "$VENV/bin/pip" install -q numpy scipy pillow pycairo qrcode fonttools brotli

# Quartz (cairo's font backend on macOS) ignores a weight request on a variable font and hands back
# its default instance, Thin. So Public Sans goes in as two static instances, Regular and Bold.
"$VENV/bin/python" - "$REPO/Ben.Web.Website/wwwroot/fonts" <<'PY'
import os, sys
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
src, dst = sys.argv[1], os.path.expanduser('~/Library/Fonts')
out = os.path.join(dst, 'IrishGrover-Regular.ttf')
if not os.path.exists(out):
    f = TTFont(os.path.join(src, 'ig-10fc9b91.woff2')); f.flavor = None; f.save(out)
for wght, style in ((400, 'Regular'), (700, 'Bold')):
    out = os.path.join(dst, f'PublicSans-{style}.ttf')
    if os.path.exists(out): continue
    f = TTFont(os.path.join(src, 'ps-5fc5c7b6.woff2')); f.flavor = None
    f = instancer.instantiateVariableFont(f, {'wght': wght})
    n = f['name']
    for rec in list(n.names):
        if rec.nameID in (16, 17, 21, 22, 25) or rec.nameID >= 256: n.removeNames(nameID=rec.nameID)
    for nid, val in ((1, 'Public Sans'), (2, style), (4, f'Public Sans {style}'), (6, f'PublicSans-{style}'), (3, f'PublicSans-{style};ishaunted-ad')):
        n.setName(val, nid, 3, 1, 0x409); n.setName(val, nid, 1, 0, 0)
    f['OS/2'].usWeightClass = wght
    f['OS/2'].fsSelection = (f['OS/2'].fsSelection & ~0b1100001) | ((1 << 5) if style == 'Bold' else (1 << 6))
    f['head'].macStyle = 1 if style == 'Bold' else 0
    f.save(out)
print('fonts: Irish Grover, Public Sans Regular + Bold')
PYecho "ready: $VENV/bin/python $HERE/build.py"
