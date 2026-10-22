"""The new app icon (10/15/2026, "with the ghost's face") cut into the pieces the ending builds it from.

    ~/.cache/ishaunted-ad/bin/python pieces.py        # build/logo/piece-<name>.png, 1254 px square

The mark is the site's own SVG (wwwroot/static/images/is-haunted-mark.svg); its paths are grouped by what
they draw and each group is rendered alone, white (the face near-black), on a transparent square in the
mark's own coordinates, so the pieces land exactly on one another. The violet tile under them is drawn by
the ending itself (render.py), in the icon's color (icon.json: #5B3DF5, lighter at the top).

  arc      the left arc and the short piece over the ghost's head
  moon     the crescent: the long sweep round the right and under the house
  win0-3   the house's four panes, one at a time (top left, top right, bottom left, bottom right)
  ghost    the ghost, roof line and tail strokes included
  face     its eyes and mouth — the part the new icon brought back
"""
import json, os, re, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
MARK = os.path.join(REPO, 'Ben.Web.Website', 'wwwroot', 'static', 'images', 'is-haunted-mark.svg')
OUT = os.path.join(HERE, 'build', 'logo')
PW = os.environ.get('PW_DRIVER', os.path.join(REPO, 'Ben.Web.Playwright', 'bin', 'Release', 'net10.0', '.playwright'))
SIZE = 1254

GROUPS = {   # piece -> the SVG's path indices, in file order
    'arc':     [10, 11, 16],
    'moon':    [6, 7, 8, 9],
    'win0':    [14], 'win1': [13], 'win2': [15], 'win3': [12],     # the panes, in the order they light
    'ghost':   [0, 1, 5, 17],
    'face':    [2, 3, 4],
}
FACE = '#120E22'          # the icon's eyes and mouth: near-black, a touch of the tile's violet


def paths():
    return re.findall(r'<path[^>]*?d="([^"]+)"[^>]*/>', open(MARK).read(), re.S)


def render():
    os.makedirs(OUT, exist_ok=True)
    ds = paths()
    jobs = []
    for name, idx in GROUPS.items():
        fill = FACE if name == 'face' else '#FFFFFF'
        body = ''.join(f'<path fill="{fill}" d="{ds[i]}"/>' for i in idx)
        svg = os.path.join(OUT, f'piece-{name}.svg')
        open(svg, 'w').write(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 627 627" width="{SIZE}" height="{SIZE}">{body}</svg>')
        jobs.append((svg, svg[:-4] + '.png'))
    js = os.path.join(OUT, '_pieces.js')
    open(js, 'w').write("""
const { chromium } = require(process.argv[2] + '/package');
(async () => { const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: %d, height: %d } });
  for (const [svg, png] of JSON.parse(process.argv[3])) {
    await p.goto('file://' + svg); await p.screenshot({ path: png, omitBackground: true });
  }
  await b.close(); })();""" % (SIZE, SIZE))
    subprocess.run([os.path.join(PW, 'node', 'darwin-arm64', 'node'), js, PW, json.dumps(jobs)], check=True)
    return [p for _, p in jobs]


WORD_PX = 144          # the wordmark's size in the image (twice what the ending shows)


def wordmark():
    """'IsHaunted.com' as the site's header sets it (Irish Grover, kerned by the browser — Cairo's simple
    text API drops the font's kerning and opened a gap in "Haunted"), with each letter's box, so the
    ending can bring the letters in one by one. build/logo/word.png + word.json"""
    fonts = os.path.expanduser('~/Library/Fonts')
    doc = os.path.join(OUT, 'word.html')
    open(doc, 'w').write(f"""<!doctype html><html><head><meta charset="utf-8"><style>
@font-face {{ font-family: 'Irish Grover'; src: url('file://{fonts}/IrishGrover-Regular.ttf'); }}
html, body {{ margin: 0; background: transparent; }}
#w {{ font-family: 'Irish Grover'; font-size: {WORD_PX}px; line-height: 1.3; padding: 0 24px; display: inline-block; white-space: pre; color: #E9EDF4; }}
#w .dot {{ color: #A78BFA; }}
</style></head><body><span id="w">IsHaunted<span class="dot">.com</span></span></body></html>""")
    js = os.path.join(OUT, '_word.js')
    open(js, 'w').write("""
const { chromium } = require(process.argv[2] + '/package'); const fs = require('fs');
(async () => { const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1400, height: 300 } });
  await p.goto('file://' + process.argv[3]); await p.evaluate(() => document.fonts.ready);
  const r = await p.evaluate(() => {
    const el = document.getElementById('w'), box = el.getBoundingClientRect(), out = [];
    const walk = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    while (walk.nextNode()) { const t = walk.currentNode;
      for (let i = 0; i < t.length; i++) { const rg = document.createRange(); rg.setStart(t, i); rg.setEnd(t, i + 1);
        const b2 = rg.getBoundingClientRect(); out.push([t.data[i], b2.left - box.left, b2.right - box.left]); } }
    return { w: box.width, h: box.height, letters: out }; });
  await (await p.$('#w')).screenshot({ path: process.argv[4], omitBackground: true });
  fs.writeFileSync(process.argv[5], JSON.stringify(r)); await b.close(); })();""")
    subprocess.run([os.path.join(PW, 'node', 'darwin-arm64', 'node'), js, PW, doc,
                    os.path.join(OUT, 'word.png'), os.path.join(OUT, 'word.json')], check=True)


if __name__ == '__main__':
    print('\n'.join(render()))
    wordmark()
