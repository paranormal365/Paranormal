"""The site on the devices' screens: a place's page, the claim, and the confirmation.

Each scene's device shows the venue-claim flow for that scene's place (item 235 phase 9, the place
page's "Is this your venue? Claim it for ..."), drawn as the site's dark Signal theme draws it, in the
site's own fonts (Public Sans, and Irish Grover for the wordmark) and with the new app icon.

    ~/.cache/ishaunted-ad/bin/python screens.py [scene ...]     # into build/screens/

States, switched by the taps in the footage:
  page     the place's page, with the claim line and its button
  claim    "Claim a venue": what claiming means, the role, and the Claim button
  claimed  the same, the button pressed
  done     the confirmation: congratulations, the group now owns the place

The surgery scene uses only "done" ("Congratulations, you own Operating Room 3 now."): the doctor holds
the phone and never taps it.

Rendered with the Chromium that Playwright installs (the e2e project's bundled node driver), at 2x.
"""
import json, os, random, subprocess, sys, html

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(HERE, 'build', 'screens')
FONTS = os.path.expanduser('~/Library/Fonts')
ICON = os.path.join(REPO, 'Ben.Web.Website', 'wwwroot', 'static', 'images', 'app', 'app-icon.png')
PW = os.environ.get('PW_DRIVER', os.path.join(REPO, 'Ben.Web.Playwright', 'bin', 'Release', 'net10.0', '.playwright'))

# scene: (place, where, group, device, landscape)
SCENES = {
    'soccer':     ('Championship Final Stadium', 'Goal Mouth, North End', 'Keepers of the Keep', 'tablet', True),
    'sistine':    ('The Sistine Chapel', 'Vatican City', 'Buonarroti Paranormal', 'tablet', True),
    'rubberball': ("World's Largest Ball of Rubber Bands", 'Rural Route 9, Kansas', 'Overall Investigators', 'tablet', False),
    'liberty':    ('The Statue of Liberty', "Bedloe's Island, New York", 'Société Bartholdi', 'tablet', False),
    'iss':        ('The Great Wall of China', 'Badaling, Beijing', 'Low Earth Orbit Paranormal', 'tablet', True),
    'hancock':    ('Independence Hall', 'Philadelphia, Pennsylvania', 'The Hancock Society', 'tablet', False),
    'mars':       ('Jezero Crater', 'Mars', 'Rover Paranormal Unit', 'tablet', True),
    'sphinx':     ('The Great Sphinx of Giza', 'Giza, Egypt', 'Pharaoh Tours', 'phone', False),
    'prairie':    ('Prairie Dog Town', 'Burrow 4,212, South Dakota', 'Burrow Busters', 'phone', False),
    'surgery':    ('Operating Room 3', 'Hollow Creek General Hospital', 'Night Shift Investigators', 'phone', False),
}
STATES = ('page', 'claim', 'claimed', 'done')
SIZES = {('tablet', True): (1024, 768), ('tablet', False): (768, 1024), ('phone', False): (390, 844)}

CSS = """
@font-face { font-family: 'Public Sans'; src: url('file://%(f)s/PublicSans-Regular.ttf'); font-weight: 400; }
@font-face { font-family: 'Public Sans'; src: url('file://%(f)s/PublicSans-Bold.ttf'); font-weight: 700; }
@font-face { font-family: 'Irish Grover'; src: url('file://%(f)s/IrishGrover-Regular.ttf'); }
* { box-sizing: border-box; margin: 0; padding: 0; }
html, body { width: %(w)dpx; height: %(h)dpx; overflow: hidden; }
body { background: #0C1017; color: #E9EDF4; font-family: 'Public Sans', sans-serif; font-size: %(fs)dpx; position: relative; }
.bar { height: %(bar)dpx; background: #090D13; display: flex; align-items: center; gap: .5em; padding: 0 1em;
       border-bottom: 1px solid rgba(255,255,255,.09); }
.bar img { height: 64%%; border-radius: 22%%; }
.word { font-family: 'Irish Grover'; font-size: 1.35em; color: #fff; }
.word span { color: #A78BFA; }
.bar .me { margin-left: auto; width: 1.9em; height: 1.9em; border-radius: 50%%; background: linear-gradient(120deg,#7C5CFF,#22D3EE);
           display: grid; place-items: center; font-weight: 700; font-size: .9em; }
.page { padding: %(pad)dpx; display: flex; flex-direction: column; gap: %(gap)dpx; }
.hero { border-radius: 20px; padding: 1.4em 1.3em; min-height: %(heroh)dpx; display: flex; flex-direction: column; justify-content: flex-end;
        background: radial-gradient(120%% 120%% at 85%% 20%%, rgba(124,92,255,.55), rgba(34,211,238,.18) 45%%, rgba(21,28,40,1) 75%%), #151C28; }
.kicker { font-size: .62em; font-weight: 700; letter-spacing: .14em; color: #22D3EE; text-transform: uppercase; }
.hero h1 { font-size: 1.9em; line-height: 1.08; letter-spacing: -.028em; margin: .25em 0 .2em; }
.hero p { color: #98A3B4; }
.card { background: #151C28; border: 1px solid rgba(255,255,255,.09); border-radius: 20px; padding: 1.1em 1.2em; }
.muted { color: #98A3B4; }
.btn { display: inline-block; border-radius: 999px; padding: .62em 1.3em; font-weight: 700; color: #fff;
       background: linear-gradient(120deg,#7C5CFF,#22D3EE); box-shadow: 0 8px 24px rgba(124,92,255,.40); }
.btn.big { display: block; text-align: center; font-size: 1.1em; padding: .8em 1em; }
.btn.pressed { filter: brightness(1.3); transform: scale(.97); box-shadow: 0 0 0 7px rgba(167,139,250,.40); }
.claimline { display: flex; align-items: center; justify-content: space-between; gap: .8em; flex-wrap: wrap; }
.claimline b { font-size: 1.05em; }
h2 { font-size: 1.5em; letter-spacing: -.02em; }
.radio { display: flex; align-items: center; gap: .55em; margin: .35em 0; }
.radio i { width: 1em; height: 1em; border-radius: 50%%; border: 2px solid rgba(255,255,255,.35); display: inline-block; }
.radio i.on { border: .3em solid #7C5CFF; }
.rows { display: flex; gap: .7em; }
.stat { flex: 1; background: #1B2433; border-radius: 12px; padding: .6em .7em; }
.stat b { display: block; font-size: 1.3em; }
.stat small { color: #98A3B4; font-size: .7em; }
.done { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center; gap: .55em; }
.tick { width: 3.6em; height: 3.6em; border-radius: 50%%; background: linear-gradient(120deg,#7C5CFF,#22D3EE);
        display: grid; place-items: center; font-size: 1.7em; font-weight: 700; box-shadow: 0 0 40px rgba(124,92,255,.65); }
.done h1 { font-size: 2.05em; letter-spacing: -.03em; }
.done .owns { font-size: 1.25em; line-height: 1.3; }
.done .owns strong { color: #A78BFA; }
.confetti i { position: absolute; width: .5em; height: .9em; border-radius: 2px; opacity: .9; }
"""


def page(scene, state):
    place, where, group, device, land = SCENES[scene]
    w, h = SIZES[(device, land)]
    phone = device == 'phone'
    p = dict(f=FONTS, w=w, h=h, fs=17 if phone else (21 if land else 22), bar=54 if phone else 64,
             pad=16 if phone else 26, gap=14 if phone else 20, heroh=210 if phone else (220 if land else 300))
    e = html.escape
    bar = (f'<div class="bar"><img src="file://{ICON}"><div class="word">IsHaunted<span>.com</span></div>'
           f'<div class="me">{e(group[0])}</div></div>')
    hero = f'<div class="hero"><div class="kicker">Public location</div><h1>{e(place)}</h1><p>{e(where)}</p></div>'
    if state == 'page':
        body = (hero +
                f'<div class="card claimline"><div><div class="muted">Is this your venue?</div><b>Claim it for {e(group)}</b></div>'
                '<span class="btn">Claim it</span></div>'
                '<div class="rows"><div class="stat"><b>14</b><small>pieces of evidence</small></div>'
                '<div class="stat"><b>37</b><small>visits</small></div><div class="stat"><b>2</b><small>investigations</small></div></div>')
    elif state in ('claim', 'claimed'):
        body = ('<h2>Claim a venue</h2>'
                f'<div class="muted">Claiming <b style="color:#E9EDF4">{e(place)}</b> means other groups ask you before they '
                'publish events there, and you get a venue page.</div>'
                '<div class="card"><div class="muted" style="margin-bottom:.3em">You are its</div>'
                '<div class="radio"><i class="on"></i>Owner</div><div class="radio"><i></i>Manager</div>'
                '<div class="radio"><i></i>Staff</div></div>'
                f'<span class="btn big{" pressed" if state == "claimed" else ""}">Claim it</span>')
    else:
        rnd = random.Random(scene)
        def spot():     # confetti keeps off the words: the sides, or (on the surgery phone) above and below
            if scene == 'surgery':
                return rnd.uniform(4, 94), rnd.choice([rnd.uniform(8, 26), rnd.uniform(80, 95)])
            return rnd.choice([rnd.uniform(2, 22), rnd.uniform(78, 96)]), rnd.uniform(9, 88)
        bits = ''.join(f'<i style="left:{x:.0f}%;top:{y:.0f}%;'
                       f'background:{rnd.choice(["#7C5CFF", "#22D3EE", "#A78BFA", "#FFFFFF"])};'
                       f'transform:rotate({rnd.uniform(-60, 60):.0f}deg)"></i>' for x, y in (spot() for _ in range(26)))
        if scene == 'surgery':     # seen small, held still and pushed in on: few words, very large
            body = (f'<div class="confetti">{bits}</div><div class="done" style="gap:.35em">'
                    '<div class="tick" style="font-size:2.3em">✓</div>'
                    '<h1 style="font-size:2.55em;line-height:1.05">Congratulations!</h1>'
                    '<div class="owns" style="font-size:1.9em;line-height:1.2">You own<br>'
                    f'<strong style="font-size:1.12em">{e(place)}</strong><br>now.</div>'
                    f'<div class="muted" style="font-size:1.05em">{e(where)}</div></div>')
            return (f'<!doctype html><html><head><meta charset="utf-8"><style>{CSS % p}</style></head>'
                    f'<body>{bar}<div class="page" style="height:{h - p["bar"]}px">{body}</div></body></html>'), (w, h)
        who = f'{e(group)} owns'
        body = (f'<div class="confetti">{bits}</div><div class="done"><div class="tick">✓</div><h1>Congratulations!</h1>'
                f'<div class="owns">{who}<br><strong>{e(place)}</strong>{" now." if scene == "surgery" else "."}</div>'
                f'<div class="muted">Confirmed as the venue · {e(where)}</div>'
                '<span class="btn" style="margin-top:.4em">Open your venue</span></div>')
    return (f'<!doctype html><html><head><meta charset="utf-8"><style>{CSS % p}</style></head>'
            f'<body>{bar}<div class="page" style="height:{h - p["bar"]}px">{body}</div></body></html>'), (w, h)


def render_all(scenes=None):
    os.makedirs(OUT, exist_ok=True)
    jobs = []
    for scene in scenes or SCENES:
        for state in STATES:
            doc, (w, h) = page(scene, state)
            src = os.path.join(OUT, f'{scene}-{state}.html')
            open(src, 'w').write(doc)
            jobs.append({'src': src, 'png': src[:-5] + '.png', 'w': w, 'h': h})
    js = os.path.join(OUT, '_shoot.js')
    open(js, 'w').write("""
const { chromium } = require(process.argv[2] + '/package');
(async () => {
  const jobs = JSON.parse(require('fs').readFileSync(process.argv[3], 'utf8'));
  const b = await chromium.launch();
  for (const j of jobs) {
    const p = await b.newPage({ viewport: { width: j.w, height: j.h }, deviceScaleFactor: 2 });
    await p.goto('file://' + j.src); await p.evaluate(() => document.fonts.ready);
    await p.screenshot({ path: j.png }); await p.close();
  }
  await b.close();
})();""")
    jj = os.path.join(OUT, '_jobs.json'); json.dump(jobs, open(jj, 'w'))
    subprocess.run([os.path.join(PW, 'node', 'darwin-arm64', 'node'), js, PW, jj], check=True)
    return [j['png'] for j in jobs]


def screen(scene, state):
    """The rendered state as BGR float32 (0..255)."""
    import cv2, numpy as np
    return cv2.imread(os.path.join(OUT, f'{scene}-{state}.png'), cv2.IMREAD_COLOR).astype(np.float32)


if __name__ == '__main__':
    print('\n'.join(render_all(sys.argv[1:] or None)))
