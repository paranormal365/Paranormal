"""The walking tour ad, finished (Ben, 10/09/2026).

    ~/.cache/ishaunted-ad/bin/python render.py "<Walking Tour Ad.mp4>" [out.mp4] [--stills]

1. The source as it is, until the robot's green hologram, which becomes a walking tour check-in
   (hologram.py).
2. A slow push in on the last frame, so the check-in can be read.
3. A flying saucer, like the ones over the city in the future section, zooms by right to left and fills
   the frame. Behind it the site's dark background is painted in, with sparkles in the site's colors
   (#7C5CFF, #22D3EE, #A78BFA) coming off its trail, until it has passed and only the dark is left.
4. The metallic IsHaunted logo is built in the middle piece by piece (pieces.py), the wordmark from the
   same image below it, then the tagline.
"""
import math, os, random, subprocess, sys
import cairo, cv2, numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE); sys.path.insert(0, os.path.join(HERE, '..', 'ishaunted-ad'))
import hologram as HG
import pieces as PC
from common import hx, ease, ease_out, ease_in, ease_out_back, seg, clamp, lerp, glow, font, text_w, setc

W, H, FPS = 1280, 720, 24
INK = hx('#0C1017'); ECTO = hx('#7C5CFF'); HAUNT = hx('#22D3EE'); LILAC = hx('#A78BFA')
BONE = hx('#E9EDF4'); FOG = hx('#98A3B4')
SPARKS = [ECTO, HAUNT, LILAC]

TAG = "Ghost walk check-in so easy, even the robots made it on time."
SUB = "Book on IsHaunted.com. Tap in with the app. Welcome to the future of boo."

PUSH = 30            # frames of push-in on the last frame
PASS = 40            # frames the saucer takes to cross
END = 158            # frames of ending (logo, wordmark, tagline, hold)


# ── the site's dark background (as the campfire ad's ending paints it) ─────────
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


# ── the flying saucer ─────────────────────────────────────────────────────────
def saucer(cx, cy, w, tilt, t):
    """An RGBA layer: silver hull, a ring of white lights, a cyan neon ring underneath, a glass dome."""
    bgra = np.zeros((H, W, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    c.translate(cx, cy); c.rotate(tilt)
    rx, ry = w / 2, w * .15

    glow(c, 0, ry * 1.4, w * .55, HAUNT, .45)                       # light thrown underneath
    # the hull
    c.save(); c.scale(rx, ry); c.arc(0, 0, 1, 0, 2 * math.pi); c.restore()
    g = cairo.LinearGradient(0, -ry, 0, ry)
    g.add_color_stop_rgb(0, .80, .83, .89); g.add_color_stop_rgb(.4, .52, .56, .64); g.add_color_stop_rgb(.75, .26, .29, .37); g.add_color_stop_rgb(1, .12, .14, .20)
    c.set_source(g); c.fill_preserve()
    hl = cairo.LinearGradient(-rx, 0, rx, 0)
    hl.add_color_stop_rgba(0, 1, 1, 1, 0); hl.add_color_stop_rgba(.35, 1, 1, 1, .22); hl.add_color_stop_rgba(.6, 1, 1, 1, 0)
    c.set_source(hl); c.fill()
    # the rim band and its running lights
    c.save(); c.scale(rx * .97, ry * .32); c.arc(0, 0, 1, 0, math.pi); c.restore()
    c.set_line_width(w * .012); setc(c, (.15, .18, .24, 1)); c.stroke()
    # panel seams on the hull
    for k in range(-5, 6):
        c.move_to(k * rx * .16, -ry * .55); c.line_to(k * rx * .19, ry * .2)
    c.set_line_width(w * .002); setc(c, (.1, .12, .16, .5)); c.stroke()
    n = 18
    for k in range(n):
        a = math.pi * (k + .5) / n
        x, y = math.cos(a) * rx * .9, math.sin(a) * ry * .42
        on = .55 + .45 * math.sin(t * 14 - k * .9)
        glow(c, x, y, w * .045, HAUNT if k % 3 == 0 else BONE, .95 * on); c.arc(x, y, w * .011, 0, 2 * math.pi); setc(c, BONE); c.fill()
    # the neon ring underneath
    c.save(); c.translate(0, ry * .62); c.scale(rx * .46, ry * .3); c.arc(0, 0, 1, 0, 2 * math.pi); c.restore()
    c.set_line_width(w * .022); setc(c, HAUNT, .35); c.stroke_preserve(); c.set_line_width(w * .009); setc(c, BONE, .95); c.stroke()
    # the dome, violet glass
    c.save(); c.translate(0, -ry * .55); c.scale(rx * .36, ry * 1.05); c.arc(0, 0, 1, math.pi, 2 * math.pi); c.close_path(); c.restore()
    d = cairo.LinearGradient(-rx * .36, -ry * 1.6, rx * .36, 0)
    d.add_color_stop_rgba(0, *LILAC[:3], .85); d.add_color_stop_rgba(1, *ECTO[:3], .55)
    c.set_source(d); c.fill_preserve(); c.set_line_width(w * .004); setc(c, BONE, .5); c.stroke()
    glow(c, -rx * .1, -ry * 1.15, w * .08, BONE, .5)
    s.flush()
    out = bgra.astype(np.float32) / 255
    k = max(1, int(w * .012)) | 1                                    # a little motion blur along the flight
    out = cv2.blur(out, (k, 1))
    return out                                                       # premultiplied BGRA, 0..1


class Sparks:
    """Sparkles shed by the saucer's trail; each lives a moment and fades."""
    def __init__(self, seed=7):
        self.r = random.Random(seed); self.p = []

    def emit(self, x, y0, y1, n):
        for _ in range(n):
            self.p.append(dict(x=x + self.r.uniform(-30, 30), y=self.r.uniform(y0, y1),
                               vx=self.r.uniform(60, 420), vy=self.r.uniform(-90, 90),
                               life=self.r.uniform(.5, 1.5), age=0.0, size=self.r.uniform(2.5, 8),
                               col=self.r.choice(SPARKS), tw=self.r.uniform(0, 6)))

    def step(self, dt):
        for q in self.p:
            q['age'] += dt; q['x'] += q['vx'] * dt; q['y'] += q['vy'] * dt; q['vx'] *= .96; q['vy'] *= .96
        self.p = [q for q in self.p if q['age'] < q['life']]

    def draw(self, t):
        bgra = np.zeros((H, W, 4), np.uint8)
        s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
        c.set_operator(cairo.OPERATOR_ADD)
        for q in self.p:
            f = 1 - q['age'] / q['life']; a = f * (.6 + .4 * math.sin(t * 20 + q['tw']))
            sz = q['size'] * (.6 + .6 * f)
            glow(c, q['x'], q['y'], sz * 5, q['col'], .55 * a)
            c.save(); c.translate(q['x'], q['y']); c.rotate(q['tw'])            # a four-point twinkle
            for ang in (0, math.pi / 2):
                c.save(); c.rotate(ang); c.scale(sz * 2.2, sz * .35); c.arc(0, 0, 1, 0, 2 * math.pi); c.restore()
                setc(c, BONE, a * .9); c.fill()
            c.restore()
        s.flush()
        return bgra.astype(np.float32) / 255


# ── the metallic logo, built ──────────────────────────────────────────────────
_pieces = {}
def piece(name):
    if name not in _pieces:
        _pieces[name] = cv2.imread(os.path.join(PC.BUILD, f'piece-{name}.png'), cv2.IMREAD_UNCHANGED).astype(np.float32) / 255
    return _pieces[name]

SCALE = .6                    # the 1254px image at 752px: the symbol about 335px tall
ANCHOR = (652, 482)           # the symbol's middle in the image
CENTER = (640, 250)           # where it sits in the frame

def put(frame, name, dx=0, dy=0, rot=0, sc=1, a=1, pivot=ANCHOR, sx=1, sy=1):
    if a <= 0: return frame
    img = piece(name)
    k = SCALE * sc
    # image point -> frame: around the pivot, scaled and turned, then placed
    px, py = pivot
    fx = CENTER[0] + (px - ANCHOR[0]) * SCALE + dx; fy = CENTER[1] + (py - ANCHOR[1]) * SCALE + dy
    cs, sn = math.cos(rot), math.sin(rot)
    M = np.float32([[k * sx * cs, -k * sy * sn, 0], [k * sx * sn, k * sy * cs, 0]])
    M[0, 2] = fx - (M[0, 0] * px + M[0, 1] * py); M[1, 2] = fy - (M[1, 0] * px + M[1, 1] * py)
    layer = cv2.warpAffine(img, M, (W, H), flags=cv2.INTER_LINEAR, borderValue=(0, 0, 0, 0)) * a
    return frame * (1 - layer[..., 3:4]) + layer[..., :3] * 255


def ending(t, tt, sparks_layer=None):
    out = dark_bg(t)
    if tt > 1.9:
        g = np.zeros((H, W, 4), np.uint8); s = cairo.ImageSurface.create_for_data(g, cairo.FORMAT_ARGB32, W, H, W * 4)
        glow(cairo.Context(s), CENTER[0], CENTER[1], 300, ECTO, .32 * ease(seg(tt, 1.9, 2.5))); s.flush()
        gl = g.astype(np.float32) / 255; out = out * (1 - gl[..., 3:4]) + gl[..., :3] * 255
    p = seg(tt, 0, .55)
    if p > 0:
        e = ease_out_back(p, 1.3); out = put(out, 'arc', dy=lerp(460, 0, e), rot=lerp(-1.2, 0, e), a=clamp(p * 3))
    p = seg(tt, .2, .75)
    if p > 0:
        e = ease_out_back(p, 1.3); out = put(out, 'moon', dy=lerp(480, 0, e), rot=lerp(1.0, 0, e), sc=lerp(1.4, 1, e), a=clamp(p * 3))
    p = seg(tt, .75, 1.05)
    if p > 0:
        out = put(out, 'windows', sc=ease_out_back(p, 2.8), pivot=(690, 628), a=clamp(p * 4))
    p = seg(tt, 1.05, 2.05)
    if p > 0:                                                       # the ghost swoops in last
        e = ease_out(p); wob = 1 - e
        out = put(out, 'ghost', dx=lerp(-640, 0, e) + math.sin(p * 9) * 26 * wob,
                  dy=lerp(240, 0, e) + math.sin(p * 7 + 1) * 60 * wob - math.sin(p * math.pi) * 40,
                  rot=math.sin(p * 8) * .22 * wob, sx=1 + math.sin(p * 11) * .07 * wob,
                  sy=1 - math.sin(p * 11) * .07 * wob, a=clamp(p * 4), pivot=(600, 420))
    p = seg(tt, 1.95, 2.4)
    if p > 0:
        e = ease_out_back(p, 1.6)
        out = put(out, 'word', dy=(1 - e) * 24, sc=lerp(.85, 1, e), pivot=(640, 900), a=clamp(p * 2))
    if sparks_layer is not None:
        out = out + sparks_layer[..., :3] * 255

    # the tagline, letter by letter
    bgra = np.zeros((H, W, 4), np.uint8); s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4)
    c = cairo.Context(s)
    def letters(txt, size, y, t0, col, bold=True, step=.016):
        font(c, 'Public Sans', size, bold); total = text_w(c, txt); x = W / 2 - total / 2
        for i, ch in enumerate(txt):
            cw = text_w(c, ch); q = seg(tt, t0 + i * step, t0 + i * step + .24)
            if q > 0:
                e = ease_out_back(q, 2.2)
                c.save(); c.translate(x + cw / 2, y - size * .35 + (1 - e) * 12); c.scale(lerp(.4, 1, e), lerp(.4, 1, e))
                setc(c, col, clamp(q * 3)); c.move_to(-cw / 2, size * .35); font(c, 'Public Sans', size, bold); c.show_text(ch); c.restore()
            x += cw
    letters(TAG, 30, 600, 2.4, BONE)
    letters(SUB, 20, 646, 2.4 + len(TAG) * .016 + .35, FOG, bold=False, step=.012)
    s.flush()
    tl = bgra.astype(np.float32) / 255
    out = out * (1 - tl[..., 3:4]) + tl[..., :3] * 255
    return out


# ── the whole thing ───────────────────────────────────────────────────────────
def zoom(img, z, focus):
    """Crop toward focus by z and scale back up to the frame."""
    fx, fy = focus; cw, ch = W / z, H / z
    x0 = clamp(fx - cw / 2, 0, W - cw); y0 = clamp(fy - ch / 2, 0, H - ch)
    M = np.float32([[z, 0, -x0 * z], [0, z, -y0 * z]])
    return cv2.warpAffine(img, M, (W, H), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)


def frames(src):
    cap = cv2.VideoCapture(src); i = 0; last = None
    while True:
        ok, f = cap.read()
        if not ok: break
        t = i / FPS
        if i >= HG.FIRST:
            p = clamp((i - 550) / 26); out = HG.composite(f, i, p, t)
        else:
            out = f
        last = (f, i); yield out; i += 1

    f, li = last; n0 = i
    focus = tuple(HG.corners(li).mean(axis=0))
    zmax = 1.32
    held = None
    for k in range(PUSH):
        t = (n0 + k) / FPS
        img = HG.composite(f, li, 1.0, t)
        z = lerp(1.0, zmax, ease(k / (PUSH - 1)))
        held = zoom(img, z, focus); yield held

    sp = Sparks(); n1 = n0 + PUSH
    sw = W * 1.25                                                   # the saucer fills the frame as it passes
    x0, x1 = W + sw * .55, -sw * .62
    for k in range(PASS + 36):
        t = (n1 + k) / FPS
        q = k / (PASS - 1)
        if k < PASS:
            cx = lerp(x0, x1, ease(q)); cy = 370 + math.sin(q * math.pi) * -28
            img = HG.composite(f, li, 1.0, t)
            left = zoom(img, zmax + .06 * q, focus).astype(np.float32)
            # behind the saucer (to its right) the site's background is painted in, softly
            edge = cx + sw * .12
            xs = np.arange(W, dtype=np.float32)
            m = np.clip((xs - edge) / 90.0, 0, 1)[None, :, None]
            base = left * (1 - m) + dark_bg(t) * m
            sp.emit(cx + sw * .38, cy - sw * .2, cy + sw * .2, 26)
            sp.emit(edge, 40, H - 40, 18)
            sp.step(1 / FPS)
            layer = saucer(cx, cy, sw, -.06 + .04 * math.sin(q * math.pi), t)
            out = base * (1 - layer[..., 3:4]) + layer[..., :3] * 255
            out = out + sp.draw(t)[..., :3] * 255
        else:
            sp.step(1 / FPS)
            out = dark_bg(t) + sp.draw(t)[..., :3] * 255
        yield out.clip(0, 255).astype(np.uint8)

    n2 = n1 + PASS + 36
    for k in range(END):
        t = (n2 + k) / FPS
        yield ending(t, k / FPS).clip(0, 255).astype(np.uint8)


def main():
    src = sys.argv[1]
    out = next((a for a in sys.argv[2:] if a.endswith('.mp4')), os.path.join(PC.BUILD, 'walking-tour-ad.mp4'))
    if not os.path.exists(os.path.join(PC.BUILD, 'piece-ghost.png')): PC.cut()
    if '--stills' in sys.argv:
        want = {int(x) for x in os.environ.get('STILLS', '560,595,615,630,640,660,700,740,790').split(',')}
        for n, img in enumerate(frames(src)):
            if n in want: cv2.imwrite(os.path.join(PC.BUILD, f'still-{n}.png'), img)
            if n > max(want): break
        return
    ff = subprocess.Popen(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', f'{W}x{H}',
                           '-r', str(FPS), '-i', '-', '-c:v', 'libx264', '-preset', 'slow', '-crf', '17',
                           '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out], stdin=subprocess.PIPE)
    n = 0
    for img in frames(src):
        ff.stdin.write(img.tobytes()); n += 1
    ff.stdin.close(); ff.wait()
    print(f'wrote {out}: {n} frames, {n / FPS:.1f} s')


if __name__ == '__main__':
    main()
