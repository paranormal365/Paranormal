"""The place ownership ad, finished (Ben, 10/22/2026).

    ~/.cache/ishaunted-ad/bin/python render.py [out.mp4] [--from S] [--to S] [--stills S,S,...]

1. Ben's "Advertisement Place Ownership.mp4" (64.8 s, ten scenes), with every device's green screen
   showing the site's venue claim for that scene's place (screens.py, track.py, comp.py) — the
   Declaration iPad's page of text included — and the running man's name tag reading "John Hancock"
   (tag.py).
2. The surgery: the doctor never taps; the phone already says "Congratulations! You own Operating Room 3
   now." The camera eases in on it over the scene's last seconds, the page drawn sharp at the new size
   while the room goes soft, and fades into the site's dark background.
3. The new icon (10/15/2026) built piece by piece — the violet tile, the arcs, the four windows lighting,
   the ghost swooping in and then its face — the wordmark in Irish Grover, and the tagline.

Writes build/events.json: when each piece of the ending lands, for the score (score.py).
"""
import json, math, os, subprocess, sys
import cairo, cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'ishaunted-ad'))
import comp as CP, tag as TG, track as TR, pieces as PC
from common import hx, ease, ease_out, ease_in, ease_out_back, seg, clamp, lerp, glow, font, text_w, setc

W, H, FPS = 1280, 720, 30
SRC_FRAMES = 1944                          # 64.8 s
INK = hx('#0C1017'); ECTO = hx('#7C5CFF'); HAUNT = hx('#22D3EE'); LILAC = hx('#A78BFA')
BONE = hx('#E9EDF4'); FOG = hx('#98A3B4')

PUSH = (61.0, 64.0, 5.2)       # the surgery push-in: from, to, how far
FADE = (64.15, 64.8)           # the surgery fades into the dark
END_T0, END_LEN = 64.8, 7.9    # the ending
TOTAL = END_T0 + END_LEN

TAG = 'Claim it before the ghosts do.'
SUB = 'Find the place on IsHaunted.com and tap Claim it.'

# the ending's beats, seconds into it (the score lands on these)
EV = dict(tile=.0, tile_land=.42, arc=.3, arc_land=.75, moon=.5, moon_land=1.05,
          win=[1.15, 1.32, 1.49, 1.66], ghost=1.75, ghost_land=2.6, face=2.75, word=3.1, tag=3.75)


# ── the site's dark background, as the other ads' endings paint it ───────────
def dark_bg(t):
    bgra = np.zeros((H, W, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    setc(c, INK); c.paint()
    glow(c, 380 + 40 * math.sin(t * .6), 250, 760, ECTO, .38)
    glow(c, 930 + 30 * math.cos(t * .5), 520, 660, HAUNT, .24)
    for k in range(5):
        x0 = 120 + k * 260 + 40 * math.sin(t * .4 + k)
        g = cairo.LinearGradient(0, 0, 0, H)
        g.add_color_stop_rgba(0, *HAUNT[:3], .06); g.add_color_stop_rgba(1, *HAUNT[:3], 0)
        c.move_to(x0 - 30, 0); c.line_to(x0 + 30, 0); c.line_to(x0 + 140, H); c.line_to(x0 + 40, H); c.close_path()
        c.set_source(g); c.fill()
    s.flush()
    return bgra[..., :3].astype(np.float32)


def over(out, bgra):
    """Premultiplied BGRA float (0..1) over a BGR float frame."""
    return out * (1 - bgra[..., 3:4]) + bgra[..., :3] * 255


# ── the icon, built ───────────────────────────────────────────────────────────
TILE = 300                       # the tile's side on screen
CX, CY = 640, 238                # its center
MARK = TILE * 1.1 / PC.SIZE      # the mark's 1254-px pieces onto the tile (the icon draws it slightly larger)

_pc = {}
def piece(name):
    if name not in _pc:
        im = cv2.imread(os.path.join(PC.OUT, f'piece-{name}.png'), cv2.IMREAD_UNCHANGED).astype(np.float32) / 255
        im[..., :3] *= im[..., 3:4]                                   # premultiply
        _pc[name] = im
    return _pc[name]

def put(out, name, dx=0, dy=0, rot=0, sc=1, a=1, pivot=(627, 627), sx=1, sy=1, shadow=True):
    """A piece, turned and scaled about pivot (in the piece's pixels), placed relative to the tile."""
    if a <= 0: return out
    img = piece(name); k = MARK * sc
    px, py = pivot
    fx = CX + (px - PC.SIZE / 2) * MARK + dx; fy = CY + (py - PC.SIZE / 2) * MARK + dy
    cs, sn = math.cos(rot), math.sin(rot)
    M = np.float32([[k * sx * cs, -k * sy * sn, 0], [k * sx * sn, k * sy * cs, 0]])
    M[0, 2] = fx - (M[0, 0] * px + M[0, 1] * py); M[1, 2] = fy - (M[1, 0] * px + M[1, 1] * py)
    layer = cv2.warpAffine(img, M, (W, H), flags=cv2.INTER_LINEAR, borderValue=(0, 0, 0, 0)) * a
    if shadow:   # the icon's soft neutral shadow under the white mark
        sh = cv2.GaussianBlur(layer[..., 3], (0, 0), 5)
        sh = np.roll(sh, 4, 0) * .35
        out = out * (1 - sh[..., None]) + np.array([60, 20, 30], np.float32) * sh[..., None]
    return over(out, layer)

def tile(out, sc, a, t):
    """The icon's violet tile: rounded, lighter at the top, a glassy rim, a glow behind."""
    if a <= 0 or sc <= 0: return out
    bgra = np.zeros((H, W, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    glow(c, CX, CY + 10, TILE * 1.15 * sc, ECTO, .55 * a)
    S = TILE * sc; x, y, r = CX - S / 2, CY - S / 2, S * .225
    def shape():
        c.new_path(); c.move_to(x + r, y); c.line_to(x + S - r, y); c.curve_to(x + S, y, x + S, y, x + S, y + r)
        c.line_to(x + S, y + S - r); c.curve_to(x + S, y + S, x + S, y + S, x + S - r, y + S)
        c.line_to(x + r, y + S); c.curve_to(x, y + S, x, y + S, x, y + S - r)
        c.line_to(x, y + r); c.curve_to(x, y, x, y, x + r, y); c.close_path()
    g = cairo.LinearGradient(0, y, 0, y + S)
    g.add_color_stop_rgba(0, *hx('#7A5CFF')[:3], a); g.add_color_stop_rgba(1, *hx('#4F2FE6')[:3], a)
    shape(); c.set_source(g); c.fill()
    # the glass: a light rim, brightest across the top, and a faint sheen sweeping over once
    rim = cairo.LinearGradient(0, y, 0, y + S)
    rim.add_color_stop_rgba(0, 1, 1, 1, .55 * a); rim.add_color_stop_rgba(.5, 1, 1, 1, .08 * a); rim.add_color_stop_rgba(1, 1, 1, 1, .25 * a)
    shape(); c.set_source(rim); c.set_line_width(3 * sc); c.stroke()
    sx = x - S + (2.4 * S) * clamp((t - EV['tile_land']) / 1.2)
    sheen = cairo.LinearGradient(sx, y, sx + S * .5, y + S)
    sheen.add_color_stop_rgba(0, 1, 1, 1, 0); sheen.add_color_stop_rgba(.5, 1, 1, 1, .18 * a); sheen.add_color_stop_rgba(1, 1, 1, 1, 0)
    shape(); c.set_source(sheen); c.fill()
    s.flush()
    return over(out, bgra.astype(np.float32) / 255)


class Sparks:
    """Sparkles in the site's colors, thrown when the face appears."""
    def __init__(self, seed=11):
        self.r = np.random.default_rng(seed); self.p = []
    def burst(self, x, y, n):
        for _ in range(n):
            a = self.r.uniform(0, 2 * math.pi); v = self.r.uniform(80, 360)
            self.p.append([x, y, math.cos(a) * v, math.sin(a) * v - 60, self.r.uniform(.6, 1.3), 0.0,
                           [ECTO, HAUNT, LILAC, BONE][self.r.integers(4)], self.r.uniform(1.5, 3.5)])
    def step(self, dt):
        for q in self.p: q[0] += q[2] * dt; q[1] += q[3] * dt; q[3] += 140 * dt; q[2] *= .985; q[5] += dt
        self.p = [q for q in self.p if q[5] < q[4]]
    def draw(self):
        bgra = np.zeros((H, W, 4), np.uint8)
        s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
        for x, y, _, _, life, age, col, r in self.p:
            k = 1 - age / life; glow(c, x, y, r * 4, col, .5 * k)
            setc(c, col, k); c.arc(x, y, r * (.5 + .5 * k), 0, 2 * math.pi); c.fill()
        s.flush(); return bgra.astype(np.float32) / 255


def ending(tt, t, sparks):
    out = dark_bg(t)
    p = seg(tt, EV['tile'], EV['tile_land'])
    out = tile(out, ease_out_back(p, 1.8), clamp(p * 3), tt)
    p = seg(tt, EV['arc'], EV['arc_land'])
    if p > 0:
        e = ease_out_back(p, 1.2); out = put(out, 'arc', dx=lerp(-260, 0, e), dy=lerp(120, 0, e), rot=lerp(-1.3, 0, e), a=clamp(p * 3))
    p = seg(tt, EV['moon'], EV['moon_land'])
    if p > 0:
        e = ease_out_back(p, 1.2); out = put(out, 'moon', rot=lerp(1.6, 0, e), sc=lerp(1.35, 1, e), a=clamp(p * 3))
    for k, t0 in enumerate(EV['win']):
        p = seg(tt, t0, t0 + .22)
        if p > 0:
            cx, cy = [(670, 740), (735, 740), (680, 800), (735, 800)][k]
            out = put(out, f'win{k}', sc=ease_out_back(p, 3.0), pivot=(cx, cy), a=clamp(p * 4))
    p = seg(tt, EV['ghost'], EV['ghost_land'])
    if p > 0:                                                         # the ghost swoops in last
        e = ease_out(p); wob = 1 - e
        out = put(out, 'ghost', dx=lerp(-700, 0, e) + math.sin(p * 9) * 24 * wob,
                  dy=lerp(200, 0, e) + math.sin(p * 7 + 1) * 50 * wob - math.sin(p * math.pi) * 40,
                  rot=math.sin(p * 8) * .2 * wob, sx=1 + math.sin(p * 11) * .07 * wob,
                  sy=1 - math.sin(p * 11) * .07 * wob, a=clamp(p * 4), pivot=(560, 560))
    p = seg(tt, EV['face'], EV['face'] + .35)
    if p > 0:     # its face: the eyes open, the mouth makes its "oh"
        out = put(out, 'face', sx=1, sy=ease_out_back(p, 2.6), pivot=(600, 400), a=clamp(p * 3), shadow=False)
    out = over(out, sparks.draw())

    # the wordmark, letter by letter, as the site's header sets it
    bgra = np.zeros((H, W, 4), np.uint8); s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4)
    c = cairo.Context(s)
    def letters(parts, face, size, y, t0, step, bold=False):
        # each letter where the whole line puts it (the advance of everything before it, kerning and all)
        font(c, face, size, bold); full = ''.join(txt for txt, _ in parts); x0 = W / 2 - text_w(c, full) / 2
        cols = [col for txt, col in parts for _ in txt]
        for i, ch in enumerate(full):
            font(c, face, size, bold); x = x0 + text_w(c, full[:i]); cw = text_w(c, ch)
            q = seg(tt, t0 + i * step, t0 + i * step + .26)
            if q > 0:
                e = ease_out_back(q, 2.2)
                c.save(); c.translate(x + cw / 2, y - size * .35 + (1 - e) * 14); c.scale(lerp(.4, 1, e), lerp(.4, 1, e))
                setc(c, cols[i], clamp(q * 3)); c.move_to(-cw / 2, size * .35); font(c, face, size, bold); c.show_text(ch); c.restore()
    letters([(TAG, BONE)], 'Public Sans', 34, 566, EV['tag'], .02, bold=True)
    letters([(SUB, FOG)], 'Public Sans', 24, 614, EV['tag'] + len(TAG) * .02 + .35, .012)
    s.flush()
    out = over(out, bgra.astype(np.float32) / 255)
    return wordmark(out, tt)


_word = None
WORD_SCALE, WORD_Y = .5, 478       # the wordmark at 72 px; its middle's height on screen
def wordmark(out, tt, step=.035):
    """'IsHaunted.com' letter by letter, each letter a slice of the browser-set wordmark (pieces.py)."""
    global _word
    if _word is None:
        im = cv2.imread(os.path.join(PC.OUT, 'word.png'), cv2.IMREAD_UNCHANGED).astype(np.float32) / 255
        im[..., :3] *= im[..., 3:4]
        _word = (im, json.load(open(os.path.join(PC.OUT, 'word.json'))))
    im, info = _word
    ih, iw = im.shape[:2]; x0 = W / 2 - iw * WORD_SCALE / 2
    for i, (_, l, r) in enumerate(info['letters']):
        q = seg(tt, EV['word'] + i * step, EV['word'] + i * step + .26)
        if q <= 0: continue
        e = ease_out_back(q, 2.2); k = WORD_SCALE * lerp(.4, 1, e)
        a, b = int(math.floor(l)), int(math.ceil(r))
        sl = np.zeros_like(im); sl[:, a:b] = im[:, a:b]
        cx, cy = (a + b) / 2, ih / 2
        fx, fy = x0 + cx * WORD_SCALE, WORD_Y + (1 - e) * 14
        M = np.float32([[k, 0, fx - k * cx], [0, k, fy - k * cy]])
        out = over(out, cv2.warpAffine(sl, M, (W, H), flags=cv2.INTER_AREA) * clamp(q * 3))
    return out


# ── the surgery push-in ───────────────────────────────────────────────────────
_focus = None
def push_focus():
    """Where the push-in ends up: the phone's average place over the move."""
    global _focus
    if _focus is None:
        qs = [CP.screen_quad('surgery', n) for n in range(int(PUSH[0] * FPS), int(PUSH[1] * FPS))]
        _focus = tuple(np.mean([q.mean(0) for q in qs if q is not None], 0))
    return _focus

def push(n, frame):
    """Ease in on the phone, keeping it where it is in frame, and draw its page sharp at the new size."""
    t = n / FPS
    z = 1 + (PUSH[2] - 1) * ease(seg(t, PUSH[0], PUSH[1]))
    q = CP.screen_quad('surgery', n)
    if z <= 1.001 or q is None: return CP.comp_frame(n, frame)
    fx, fy = push_focus()          # one fixed point for the whole move: a camera does not follow a hand's tremor
    cw, ch = W / z, H / z
    x0 = clamp(fx - cw / 2, 0, W - cw); y0 = clamp(fy - ch / 2, 0, H - ch)
    M = np.float32([[z, 0, -x0 * z], [0, z, -y0 * z]])
    big = cv2.warpAffine(frame, M, (W, H), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
    soft = (z - 1) * .9            # the room goes soft as the phone fills the frame — and hides the upscale
    if soft > .2: big = cv2.GaussianBlur(big, (0, 0), soft)
    return CP.comp_frame(n, big, M)


def frame_at(n, src, sparks):
    t = n / FPS
    if n < SRC_FRAMES:
        f = CP.comp_frame(n, src) if not ('surgery' in CP.scenes_at(n)) else push(n, src)
        f = TG.fix_frame(n, f)
        if t >= FADE[0]:
            k = ease(seg(t, *FADE))
            f = (f.astype(np.float32) * (1 - k) + dark_bg(t) * k)
        return np.clip(f, 0, 255).astype(np.uint8)
    tt = t - END_T0
    if abs(tt - EV['face']) < .5 / FPS: sparks.burst(CX, CY - 20, 70)
    sparks.step(1 / FPS)
    return np.clip(ending(tt, t, sparks), 0, 255).astype(np.uint8)


def main():
    args = sys.argv[1:]
    out = next((a for a in args if a.endswith('.mp4')), os.path.join(HERE, 'build', 'IsHaunted-Place-Ownership-Ad.mp4'))
    t_from = float(args[args.index('--from') + 1]) if '--from' in args else 0
    t_to = float(args[args.index('--to') + 1]) if '--to' in args else TOTAL
    stills = [float(x) for x in args[args.index('--stills') + 1].split(',')] if '--stills' in args else None
    json.dump({'end_t0': END_T0, 'total': TOTAL, **EV}, open(os.path.join(HERE, 'build', 'events.json'), 'w'), indent=1)

    n0, n1 = int(round(t_from * FPS)), int(round(t_to * FPS))
    sparks = Sparks()
    cap = cv2.VideoCapture(TR.SRC); cap.set(cv2.CAP_PROP_POS_FRAMES, min(n0, SRC_FRAMES))
    ff = None
    if not stills:
        ff = subprocess.Popen(['ffmpeg', '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', f'{W}x{H}',
                               '-r', str(FPS), '-i', '-', '-c:v', 'libx264', '-preset', 'slow', '-crf', '16',
                               '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out], stdin=subprocess.PIPE)
    want = {int(round(s * FPS)) for s in stills} if stills else None
    for n in range(n0, n1):
        src = None
        if n < SRC_FRAMES:
            ok, src = cap.read()
            if not ok: break
        if n >= SRC_FRAMES and n * 1.0 / FPS - END_T0 < 0: continue
        if want is not None and n not in want:
            if n >= SRC_FRAMES:      # keep the sparks' state honest between stills
                tt = n / FPS - END_T0
                if abs(tt - EV['face']) < .5 / FPS: sparks.burst(CX, CY - 20, 70)
                sparks.step(1 / FPS)
            continue
        f = frame_at(n, src, sparks)
        if ff: ff.stdin.write(f.tobytes())
        else: cv2.imwrite(os.path.join(HERE, 'build', f'still-{n / FPS:.2f}.png'), f)
        if n % 150 == 0: print(f'{n / FPS:5.1f}s', flush=True)
    if ff: ff.stdin.close(); ff.wait(); print(out)


if __name__ == '__main__':
    main()
