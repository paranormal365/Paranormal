"""Shared drawing for the IsHaunted ad: easing, shapes, text, the cast, the car, the phones and the
IsHaunted screens (Signal dark tokens). See build.py."""
import math, random
import cairo
import numpy as np
from PIL import Image, ImageFilter

W, H, FPS = 1920, 1080, 30
import os
BUILD = os.environ.get('AD_BUILD', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build'))
ASSETS = os.path.join(BUILD, 'assets')
# The cast (figure.py, dragon.py) lives in docs/ads/actors, which git does not track: it is kept for reuse
# in future videos. The ad needs that folder present to rebuild; the rendered videos in wwwroot do not.
import sys as _sys
ACTORS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'actors')
if ACTORS not in _sys.path: _sys.path.insert(1, ACTORS)

# ── Signal palette (dark) from wwwroot/css/themes/signal-tokens.css ─────────
def hx(h, a=1.0):
    h = h.lstrip('#')
    if len(h) == 3: h = ''.join(ch * 2 for ch in h)
    return (int(h[0:2], 16) / 255, int(h[2:4], 16) / 255, int(h[4:6], 16) / 255, a)

BG = hx('#0C1017'); BG_SUNK = hx('#090D13'); SURF = hx('#151C28'); SURF2 = hx('#1B2433')
INK = hx('#E9EDF4'); MUTED = hx('#98A3B4'); FAINT = hx('#6C7889')
ACC = hx('#7C5CFF'); ACC2 = hx('#22D3EE'); LINK = hx('#A78BFA')
LIME0 = hx('#68993B'); LIME1 = hx('#C3EF52'); WIN = hx('#FED46E')

# ── easing / timing ─────────────────────────────────────────────────────────
def clamp(v, a=0.0, b=1.0): return max(a, min(b, v))
def lerp(a, b, t): return a + (b - a) * t
def seg(t, a, b): return clamp((t - a) / (b - a)) if b > a else (1.0 if t >= b else 0.0)
def ease(t): t = clamp(t); return t * t * (3 - 2 * t)
def ease_in(t): t = clamp(t); return t * t * t
def ease_out(t): t = clamp(t); return 1 - (1 - t) ** 3
def ease_out_back(t, s=1.70158):
    t = clamp(t) - 1
    return t * t * ((s + 1) * t + s) + 1
def ease_out_elastic(t):
    t = clamp(t)
    if t in (0, 1): return t
    return 2 ** (-10 * t) * math.sin((t * 10 - 0.75) * (2 * math.pi) / 3) + 1
def noise1(t, seed=0):
    # cheap smooth noise from a few sines
    return (math.sin(t * 1.7 + seed) * .5 + math.sin(t * 3.1 + seed * 2.3) * .3 + math.sin(t * 7.3 + seed * .7) * .2)

def setc(ctx, c, a=None):
    if a is None: ctx.set_source_rgba(*c)
    else: ctx.set_source_rgba(c[0], c[1], c[2], c[3] * a)

def shade(c, k):
    return (clamp(c[0] * k), clamp(c[1] * k), clamp(c[2] * k), c[3])

def mix(c1, c2, t):
    return tuple(lerp(c1[i], c2[i], t) for i in range(4))

# ── shapes ──────────────────────────────────────────────────────────────────
def rrect(ctx, x, y, w, h, r):
    r = min(r, w / 2, h / 2)
    ctx.new_sub_path()
    ctx.arc(x + w - r, y + r, r, -math.pi / 2, 0)
    ctx.arc(x + w - r, y + h - r, r, 0, math.pi / 2)
    ctx.arc(x + r, y + h - r, r, math.pi / 2, math.pi)
    ctx.arc(x + r, y + r, r, math.pi, 3 * math.pi / 2)
    ctx.close_path()

def ellipse(ctx, cx, cy, rx, ry):
    ctx.save(); ctx.translate(cx, cy); ctx.scale(max(rx, .01), max(ry, .01))
    ctx.new_sub_path(); ctx.arc(0, 0, 1, 0, 2 * math.pi); ctx.restore()

def circle(ctx, cx, cy, r):
    ctx.new_sub_path(); ctx.arc(cx, cy, r, 0, 2 * math.pi)

def line(ctx, x1, y1, x2, y2, w, c, cap=cairo.LINE_CAP_ROUND):
    ctx.set_line_cap(cap); ctx.set_line_width(w); setc(ctx, c)
    ctx.move_to(x1, y1); ctx.line_to(x2, y2); ctx.stroke()

def glow(ctx, cx, cy, r, c, a=1.0):
    g = cairo.RadialGradient(cx, cy, 0, cx, cy, r)
    g.add_color_stop_rgba(0, c[0], c[1], c[2], a)
    g.add_color_stop_rgba(.4, c[0], c[1], c[2], a * .35)
    g.add_color_stop_rgba(1, c[0], c[1], c[2], 0)
    ctx.set_source(g); circle(ctx, cx, cy, r); ctx.fill()

def lingrad(x0, y0, x1, y1, stops):
    g = cairo.LinearGradient(x0, y0, x1, y1)
    for o, c in stops: g.add_color_stop_rgba(o, *c)
    return g

def acc_grad(x0, y0, x1, y1):
    return lingrad(x0, y0, x1, y1, [(0, ACC), (1, ACC2)])

# ── text ────────────────────────────────────────────────────────────────────
def font(ctx, family='Public Sans', size=20, bold=False):
    ctx.select_font_face(family, cairo.FONT_SLANT_NORMAL,
                         cairo.FONT_WEIGHT_BOLD if bold else cairo.FONT_WEIGHT_NORMAL)
    ctx.set_font_size(size)

def text_w(ctx, s):
    return ctx.text_extents(s).x_advance

def text(ctx, s, x, y, c=INK, align='l', family='Public Sans', size=20, bold=False, a=1.0):
    font(ctx, family, size, bold)
    w = text_w(ctx, s)
    if align == 'c': x -= w / 2
    elif align == 'r': x -= w
    setc(ctx, c, a); ctx.move_to(x, y); ctx.show_text(s)
    return w

def wordmark(ctx, x, y, size, align='l', a=1.0, ink=INK):
    """IsHaunted + gradient .com, Irish Grover — as in BenHeader.razor/.ben-brand__dot"""
    font(ctx, 'Irish Grover', size)
    w1 = text_w(ctx, 'IsHaunted'); w2 = text_w(ctx, '.com')
    if align == 'c': x -= (w1 + w2) / 2
    elif align == 'r': x -= (w1 + w2)
    setc(ctx, ink, a); ctx.move_to(x, y); ctx.show_text('IsHaunted')
    ctx.move_to(x + w1, y); ctx.text_path('.com')
    g = acc_grad(x + w1, y - size, x + w1 + w2, y)
    ctx.save(); ctx.push_group(); ctx.set_source(g); ctx.fill(); ctx.pop_group_to_source(); ctx.paint_with_alpha(a); ctx.restore()
    return w1 + w2

# ── logo layers ─────────────────────────────────────────────────────────────
_logo_cache = {}
def logo_layer(name):
    if name not in _logo_cache:
        _logo_cache[name] = cairo.ImageSurface.create_from_png(f'{ASSETS}/logo_{name}.png')
    return _logo_cache[name]
LOGO_PX = 1254

def draw_logo(ctx, cx, cy, size, a=1.0, layers=('arc', 'moon', 'win0', 'win1', 'win2', 'win3', 'ghost')):
    for n in layers:
        draw_logo_piece(ctx, n, cx, cy, size, a=a)

def draw_logo_piece(ctx, name, cx, cy, size, dx=0, dy=0, rot=0, sc=1.0, a=1.0, pivot=(0.5, 0.5), sx=1.0, sy=1.0):
    s = logo_layer(name)
    k = size / LOGO_PX
    ctx.save()
    px, py = pivot
    ctx.translate(cx - size / 2 + px * size + dx, cy - size / 2 + py * size + dy)
    ctx.rotate(rot); ctx.scale(sc * sx, sc * sy)
    ctx.translate(-px * size, -py * size)
    ctx.scale(k, k)
    ctx.set_source_surface(s, 0, 0)
    ctx.get_source().set_filter(cairo.FILTER_GOOD)
    ctx.paint_with_alpha(a)
    ctx.restore()

# ── surfaces <-> PIL ────────────────────────────────────────────────────────
def new_surface(w=W, h=H):
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, w, h)
    return s, cairo.Context(s)

def surf_to_pil(s):
    s.flush()
    return Image.frombuffer('RGBA', (s.get_width(), s.get_height()), bytes(s.get_data()), 'raw', 'BGRA', s.get_stride(), 1).copy()

def pil_to_surf(im, premultiplied=True):
    """premultiplied=True for images that came from cairo surfaces; False for PIL-made art"""
    im = im.convert('RGBA')
    if not premultiplied:
        a = np.asarray(im).astype(np.float32)
        a[..., :3] *= a[..., 3:4] / 255.0
        im = Image.fromarray(a.astype(np.uint8), 'RGBA')
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, im.width, im.height)
    st = s.get_stride(); raw = im.tobytes('raw', 'BGRA')
    buf = s.get_data()
    if st == im.width * 4:
        buf[:] = raw
    else:
        for y in range(im.height):
            buf[y * st:y * st + im.width * 4] = raw[y * im.width * 4:(y + 1) * im.width * 4]
    s.mark_dirty()
    return s

def blur_surf(s, r, down=2):
    im = surf_to_pil(s)
    if r <= 0: return pil_to_surf(im)
    w, h = im.size
    small = im.resize((w // down, h // down), Image.BILINEAR).filter(ImageFilter.GaussianBlur(r / down))
    return pil_to_surf(small.resize((w, h), Image.BILINEAR))

def motion_blur(s, k, axis=1):
    """box blur along one axis (axis=1 horizontal, 0 vertical) — whip pans"""
    k = int(k)
    if k < 2: return s
    s.flush()
    arr = np.frombuffer(bytes(s.get_data()), np.uint8).reshape(s.get_height(), s.get_stride() // 4, 4)[:, :s.get_width()].astype(np.float32)
    arr = arr[::2, ::2]
    k2 = max(2, k // 2)
    c = np.cumsum(arr, axis=axis)
    pad = [(0, 0)] * 3; pad[axis] = (k2, 0)
    c = np.pad(c, pad, mode='edge')
    if axis == 1: out = (c[:, k2:] - c[:, :-k2]) / k2
    else: out = (c[k2:] - c[:-k2]) / k2
    out = np.clip(out, 0, 255).astype(np.uint8)
    im = Image.fromarray(out[:, :, [2, 1, 0, 3]], 'RGBA').resize((s.get_width(), s.get_height()), Image.BILINEAR)
    return pil_to_surf(im)

# ── QR code (real, points at ishaunted.com) ─────────────────────────────────
import qrcode
_qr = qrcode.QRCode(border=0, error_correction=qrcode.constants.ERROR_CORRECT_M)
_qr.add_data('https://ishaunted.com/pass/HM-31OCT-0047'); _qr.make(fit=True)
QR = _qr.get_matrix()

def draw_qr(ctx, x, y, size, fg=(0.05, 0.06, 0.09, 1)):
    n = len(QR); m = size / n
    setc(ctx, fg)
    for r in range(n):
        for c in range(n):
            if QR[r][c]:
                ctx.rectangle(x + c * m, y + r * m, m + .6, m + .6)
    ctx.fill()

# ════════════════════════════════════════════════════════════════════════════
# CHARACTERS
# ════════════════════════════════════════════════════════════════════════════
SKIN = hx('#F2C7A5'); HAIR = hx('#3B2A20'); HOODIE = hx('#7C5CFF'); PANTS = hx('#2C3550'); SHOE = hx('#EDEDED')
OUTLINE = (0.07, 0.06, 0.1, 1)

NINJA_TOP = hx('#232A4A'); NINJA_HAIR = hx('#1C2340')
GUEST = dict(skin=hx('#F2C9A8'), hair=NINJA_HAIR, top=NINJA_TOP, pants=NINJA_TOP, shoe=hx('#14182A'),
             hair_style='spiky', costume='ninja', iris=hx('#7C5CFF'))
NINJA = GUEST
UNICORN = dict(skin=hx('#F7D6C4'), hair=hx('#FF8FD1'), top=hx('#FFF4FB'), pants=hx('#FFF4FB'), shoe=hx('#B7A3E0'),
               hair_style='none', costume='unicorn', iris=hx('#E05FB0'), hand=hx('#EADCF5'))
RAINBOW = [hx('#FF7AA8'), hx('#FFB86B'), hx('#FFE37A'), hx('#8FE3A8'), hx('#7FC8FF'), hx('#B79BFF')]

def _limb(ctx, x, y, a1, a2, l1, l2, w, c, hand=None, hand_r=None):
    """two-segment limb from (x,y). angles in degrees, 0 = straight down, +90 = +x."""
    r1, r2 = math.radians(a1), math.radians(a2)
    ex, ey = x + math.sin(r1) * l1, y + math.cos(r1) * l1
    hx_, hy_ = ex + math.sin(r2) * l2, ey + math.cos(r2) * l2
    ctx.set_line_cap(cairo.LINE_CAP_ROUND); ctx.set_line_join(cairo.LINE_JOIN_ROUND)
    # outline pass
    ctx.set_line_width(w + 5); setc(ctx, OUTLINE)
    ctx.move_to(x, y); ctx.line_to(ex, ey); ctx.line_to(hx_, hy_); ctx.stroke()
    ctx.set_line_width(w); setc(ctx, c)
    ctx.move_to(x, y); ctx.line_to(ex, ey); ctx.line_to(hx_, hy_); ctx.stroke()
    if hand is not None:
        hr = hand_r or w * .62
        circle(ctx, hx_, hy_, hr + 2.5); setc(ctx, OUTLINE); ctx.fill()
        circle(ctx, hx_, hy_, hr); setc(ctx, hand); ctx.fill()
    return hx_, hy_

def draw_phone_small(ctx, x, y, ang, s=1.0, glow_on=True, back=False):
    ctx.save(); ctx.translate(x, y); ctx.rotate(math.radians(ang))
    w, h = 26 * s, 48 * s
    rrect(ctx, -w / 2 - 2, -h / 2 - 2, w + 4, h + 4, 7 * s); setc(ctx, OUTLINE); ctx.fill()
    rrect(ctx, -w / 2, -h / 2, w, h, 6 * s); setc(ctx, hx('#1E2230')); ctx.fill()
    if not back:
        rrect(ctx, -w / 2 + 2.5 * s, -h / 2 + 3 * s, w - 5 * s, h - 6 * s, 4 * s); setc(ctx, hx('#0C1017')); ctx.fill()
        # tiny brand bits on screen
        circle(ctx, 0, -h / 2 + 12 * s, 4 * s); setc(ctx, hx('#F8F9F9')); ctx.fill()
        rrect(ctx, -w / 2 + 5 * s, -2 * s, w - 10 * s, w - 10 * s, 2 * s); setc(ctx, hx('#FFFFFF')); ctx.fill()
    else:
        circle(ctx, -w / 2 + 7 * s, -h / 2 + 8 * s, 3.5 * s); setc(ctx, hx('#05070A')); ctx.fill()
    ctx.restore()

def face(ctx, cx, cy, view, expr, t, look=(0, 0), blink=0.0, skin=SKIN, iris=None, eye_scale=1.0):
    """Anime faces: tall eyes with a coloured iris and two highlights, a lash line, small nose and mouth.
    expr: neutral | smile | fear | hero | huge.  view 'front' | 'side' (facing +x)."""
    ink = OUTLINE
    iris = iris or hx('#5B3A29')
    if view == 'front':
        ex = [(-22, -2, 1.0), (22, -2, 1.0)]; mx = 0
    else:
        ex = [(16, -2, .72), (42, -2, 1.0)]; mx = 32
    lx, ly = look

    def lash(x, y, k, slant=0.0, sgn=1):
        ctx.set_line_width(5); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
        ctx.move_to(x - 14 * k, y - 15 + slant * sgn * -6); ctx.curve_to(x - 6 * k, y - 22, x + 6 * k, y - 22, x + 15 * k, y - 14 - slant * sgn * -6)
        ctx.stroke()
        # the little flick at the outer corner
        ox = x + (15 * k if sgn > 0 else -14 * k)
        ctx.set_line_width(3.5); ctx.move_to(ox, y - 14); ctx.line_to(ox + 6 * sgn, y - 18); ctx.stroke()

    def anime_eye(x, y, k, sgn, narrow=0.0, pupil_scale=1.0, shrink=False):
        ctx.save(); ctx.translate(x, y); ctx.scale(eye_scale, eye_scale); ctx.translate(-x, -y)
        _anime_eye(x, y, k, sgn, narrow, pupil_scale, shrink)
        ctx.restore()

    def _anime_eye(x, y, k, sgn, narrow=0.0, pupil_scale=1.0, shrink=False):
        ry = 19 * (1 - narrow * .45)
        ctx.save()
        ellipse(ctx, x, y + narrow * 4, 13 * k, ry)
        setc(ctx, (1, 1, 1, 1)); ctx.fill_preserve(); ctx.set_line_width(2.5); setc(ctx, ink); ctx.stroke()
        ellipse(ctx, x, y + narrow * 4, 13 * k, ry); ctx.clip()
        if shrink:
            circle(ctx, x + lx * 3 + math.sin(t * 60) * 1.2, y + 2, 3.6); setc(ctx, ink); ctx.fill()
        else:
            ix, iy = x + lx * 4 * k, y + 2 + ly * 4
            ctx.set_source(lingrad(0, iy - 15, 0, iy + 15, [(0, shade(iris, .45)), (.55, iris), (1, shade(iris, 1.35))]))
            ellipse(ctx, ix, iy, 10 * k, 15 * pupil_scale); ctx.fill()
            ellipse(ctx, ix, iy + 1, 4.2 * k, 7.5 * pupil_scale); setc(ctx, hx('#100A18')); ctx.fill()
            circle(ctx, ix + 4 * k, iy - 6, 4.2); setc(ctx, (1, 1, 1, 1)); ctx.fill()
            circle(ctx, ix - 3.5 * k, iy + 6, 2); setc(ctx, (1, 1, 1, .9)); ctx.fill()
        ctx.restore()
        if narrow > 0:
            # heavy lid across the top, slanted
            ctx.set_line_width(6); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            inner = x - 13 * k * sgn; outer = x + 13 * k * sgn
            ctx.move_to(inner, y - 6); ctx.line_to(outer, y - 15); ctx.stroke()
        else:
            lash(x, y, k, sgn=sgn)

    sgns = (-1, 1)
    if expr == 'fear':
        for (x, y, k), sg in zip(ex, sgns):
            anime_eye(cx + x, cy + y, k * 1.12, sg, shrink=True)
        # brows up in the middle
        ctx.set_line_width(4); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
        for (x, y, k), sg in zip(ex, sgns):
            ctx.move_to(cx + x + 14 * k * sg, cy + y - 26); ctx.line_to(cx + x - 9 * k * sg, cy + y - 36); ctx.stroke()
        # the gloom lines (anime shock)
        for i in range(6):
            xx = cx - 30 + i * 12 + (8 if view == 'side' else 0)
            line(ctx, xx, cy - 54, xx, cy - 30 - (i % 2) * 6, 2.5, hx('#5468C8', .75))
        # wobbly open mouth
        ctx.save(); ctx.translate(cx + mx, cy + 30)
        ctx.move_to(-12, 0)
        for i in range(7):
            ctx.line_to(-12 + i * 4, -3 + (3 if i % 2 else -2) + math.sin(t * 40 + i) * 1.2)
        ctx.curve_to(12, 14, -12, 14, -12, 0); ctx.close_path()
        setc(ctx, hx('#4A1620')); ctx.fill_preserve(); ctx.set_line_width(2.5); setc(ctx, ink); ctx.stroke()
        ctx.restore()
        # two sweat drops
        for (ddx, ph) in ((50, 0), (40, .45)):
            dx = cx + (ddx if view == 'front' else -28 - ddx * .2); dy = cy - 34 + ((t + ph) * 30 % 22)
            ctx.move_to(dx, dy - 14); ctx.curve_to(dx + 9, dy, dx + 7, dy + 9, dx, dy + 9)
            ctx.curve_to(dx - 7, dy + 9, dx - 9, dy, dx, dy - 14); setc(ctx, hx('#9ED8FF')); ctx.fill_preserve()
            ctx.set_line_width(2); setc(ctx, hx('#3B7FB5')); ctx.stroke()
        return
    if expr == 'huge':
        for (x, y, k) in ex:
            ctx.set_line_width(5); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            ctx.arc(cx + x, cy + y + 8, 12 * k, math.pi * 1.1, math.pi * 1.9); ctx.stroke()
        for x in ((-38, 38) if view == 'front' else (8, 54)):
            for k in range(3):
                line(ctx, cx + x - 8 + k * 6, cy + 20, cx + x - 12 + k * 6, cy + 26, 2.5, hx('#FF6F86', .8))
        ctx.save(); ctx.translate(cx + mx, cy + 16)
        w = 30 if view == 'front' else 22
        ctx.move_to(-w, 0); ctx.line_to(w, 0); ctx.curve_to(w, 32, -w, 32, -w, 0); ctx.close_path()
        setc(ctx, hx('#5A1A22')); ctx.fill_preserve(); ctx.set_line_width(3); setc(ctx, ink); ctx.stroke()
        ctx.save(); ctx.move_to(-w, 0); ctx.line_to(w, 0); ctx.curve_to(w, 32, -w, 32, -w, 0); ctx.close_path(); ctx.clip()
        ctx.rectangle(-w, 0, 2 * w, 7); setc(ctx, (1, 1, 1, 1)); ctx.fill()
        ellipse(ctx, 0, 24, w * .55, 9); setc(ctx, hx('#FF7A8A')); ctx.fill()
        ctx.restore(); ctx.restore()
        return
    if expr == 'evil':
        # narrow glowing eyes, hard brows, a thin cruel smile with fangs
        for (x, y, k), sg in zip(ex, sgns):
            ex_, ey_ = cx + x, cy + y
            ctx.save(); ctx.translate(ex_, ey_); ctx.scale(eye_scale, eye_scale)
            glow(ctx, 0, 0, 30, LIME1, .55)
            ctx.move_to(-15 * k * sg, -2); ctx.curve_to(-6 * k * sg, -12, 8 * k * sg, -12, 16 * k * sg, -8)
            ctx.curve_to(8 * k * sg, 6, -6 * k * sg, 6, -15 * k * sg, -2); ctx.close_path()
            setc(ctx, hx('#F4FFD6')); ctx.fill_preserve(); ctx.set_line_width(3); setc(ctx, OUTLINE); ctx.stroke()
            ellipse(ctx, 2 * sg, -2, 5, 7); setc(ctx, LIME1); ctx.fill()
            ellipse(ctx, 2 * sg, -2, 1.6, 6); setc(ctx, OUTLINE); ctx.fill()
            ctx.restore()
            ctx.set_line_width(6); setc(ctx, OUTLINE); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            ctx.move_to(ex_ - 8 * k * sg, ey_ - 14); ctx.line_to(ex_ + 20 * k * sg, ey_ - 30); ctx.stroke()
        ctx.set_line_width(3.5); setc(ctx, OUTLINE)
        ctx.move_to(cx + mx - 24, cy + 26); ctx.curve_to(cx + mx - 8, cy + 36, cx + mx + 10, cy + 34, cx + mx + 26, cy + 20); ctx.stroke()
        for fx_ in (-10, 8):
            ctx.move_to(cx + mx + fx_ - 3, cy + 31); ctx.line_to(cx + mx + fx_, cy + 40); ctx.line_to(cx + mx + fx_ + 3, cy + 31); ctx.close_path()
            setc(ctx, (1, 1, 1, 1)); ctx.fill()
        return
    if expr == 'hero':
        for (x, y, k), sg in zip(ex, sgns):
            anime_eye(cx + x, cy + y, k, sg, narrow=1.0)
        # hard brows angled down to the nose
        ctx.set_line_width(6); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
        for (x, y, k), sg in zip(ex, sgns):
            ctx.move_to(cx + x - 6 * k * sg, cy + y - 22); ctx.line_to(cx + x + 16 * k * sg, cy + y - 32); ctx.stroke()
        # confident half grin
        ctx.set_line_width(3.5); ctx.move_to(cx + mx - 14, cy + 30); ctx.curve_to(cx + mx - 4, cy + 36, cx + mx + 8, cy + 34, cx + mx + 16, cy + 24); ctx.stroke()
        ctx.move_to(cx + mx + 2, cy + 33); ctx.line_to(cx + mx + 12, cy + 29); ctx.line_to(cx + mx + 10, cy + 33); ctx.close_path(); setc(ctx, (1, 1, 1, 1)); ctx.fill()
        return
    # neutral / smile
    for (x, y, k), sg in zip(ex, sgns):
        if blink > .5:
            ctx.set_line_width(4); setc(ctx, ink); ctx.move_to(cx + x - 12 * k, cy + y + 2); ctx.curve_to(cx + x - 4, cy + y + 7, cx + x + 4, cy + y + 7, cx + x + 12 * k, cy + y + 2); ctx.stroke()
            continue
        anime_eye(cx + x, cy + y, k, sg)
    ctx.set_line_width(3); setc(ctx, ink)
    for (x, y, k), sg in zip(ex, sgns):
        ctx.move_to(cx + x - 10 * k, cy + y - 30); ctx.line_to(cx + x + 10 * k, cy + y - 32); ctx.stroke()
    line(ctx, cx + mx + (4 if view == 'side' else 0), cy + 14, cx + mx + (7 if view == 'side' else 2), cy + 17, 2.5, shade(skin, .6))
    ctx.set_line_width(3.5); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    if expr == 'smile':
        ctx.arc(cx + mx, cy + 22, 11, math.pi * .15, math.pi * .85); ctx.stroke()
    else:
        ctx.arc(cx + mx, cy + 26, 6, math.pi * .25, math.pi * .75); ctx.stroke()

def ninja_scarf_tail(ctx, x, y, t, wind=1.0, side=False, back=False):
    """the long purple scarf streaming behind the hero; wind 0..2"""
    n = 9; L_ = 150 + 70 * wind
    pts = []
    for i in range(n + 1):
        q = i / n
        if back:
            px = x + math.sin(t * 6 + q * 5) * 14 * q * wind
            py = y + q * L_ * .75
        else:
            px = x - q * L_ * (1.0 if side else .55)
            py = y + q * (30 if side else 70) + math.sin(t * 7 - q * 6) * 16 * q * (0.4 + wind * .6)
        w = 13 * (1 - q * .55)
        pts.append((px, py, w))
    ctx.move_to(pts[0][0], pts[0][1] - pts[0][2])
    for (px, py, w) in pts: ctx.line_to(px, py - w)
    for (px, py, w) in reversed(pts): ctx.line_to(px, py + w)
    ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve(); setc(ctx, ACC); ctx.fill()
    ctx.set_line_width(3); setc(ctx, shade(ACC, 1.3))
    ctx.move_to(pts[0][0], pts[0][1] - pts[0][2] * .4)
    for (px, py, w) in pts: ctx.line_to(px, py - w * .4)
    ctx.stroke()

def spiky_hair(ctx, hcx, head_y, hr, view, col, t, wind=1.0):
    """anime hero hair: swept-back spikes, a fringe that stops above the eyes, one shine band"""
    back = -1 if view == 'side' else 0
    ctx.save()
    # crown spikes
    n = 9
    ctx.move_to(hcx - hr * .95, head_y - 4)
    for i in range(n + 1):
        th = math.radians(200 - i * (220 / n))
        r = hr * (1.55 if i % 2 else .98) + (8 * math.sin(t * 9 + i) * .15 * wind if i % 2 else 0)
        sweep = (14 if i % 2 else 0) * (1 if back == 0 else 1.6)
        px = hcx + math.cos(th) * r - (sweep if back else (sweep * .2 * (1 if i < n / 2 else -1)))
        py = head_y - math.sin(th) * r
        ctx.line_to(px, py)
    ctx.line_to(hcx + hr * .95, head_y - 4)
    ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, col); ctx.fill()
    if view != 'back':
        # fringe over the forehead, clear of the eyes
        ox = 8 if view == 'side' else 0
        ctx.move_to(hcx - hr + 2 + ox, head_y - 30)
        tips = [(-40, -12), (-24, -22), (-10, -6), (6, -20), (20, -10), (36, -22), (48, -14)]
        for (tx, ty) in tips:
            ctx.line_to(hcx + tx + ox, head_y + ty)
            ctx.line_to(hcx + tx + 7 + ox, head_y - 34)
        ctx.line_to(hcx + hr - 2 + ox, head_y - 30); ctx.line_to(hcx + hr, head_y - 50); ctx.line_to(hcx - hr, head_y - 50); ctx.close_path()
        setc(ctx, col); ctx.fill()
    else:
        circle(ctx, hcx, head_y - 4, hr + 3); setc(ctx, col); ctx.fill()
    # shine band
    ctx.set_line_width(7); setc(ctx, hx('#4D5EA8', .9)); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    ctx.arc(hcx - 4, head_y - 8, hr * .8, math.radians(205), math.radians(245)); ctx.stroke()
    ctx.arc(hcx - 4, head_y - 8, hr * .8, math.radians(262), math.radians(282)); ctx.stroke()
    ctx.restore()

def ninja_headband(ctx, hcx, head_y, hr, view, t, wind=1.0):
    by = head_y - 36
    ctx.save(); circle(ctx, hcx, head_y, hr + 4); ctx.clip()
    ctx.rectangle(hcx - hr - 6, by - 8, 2 * hr + 12, 16); setc(ctx, hx('#2E3760')); ctx.fill()
    line(ctx, hcx - hr, by - 7, hcx + hr, by - 7, 2, hx('#4A568A'))
    ctx.restore()
    if view != 'back':
        px = hcx + (20 if view == 'side' else 0)
        rrect(ctx, px - 19, by - 10, 38, 20, 4); setc(ctx, OUTLINE); ctx.fill()
        ctx.set_source(lingrad(0, by - 9, 0, by + 9, [(0, hx('#E8ECF4')), (1, hx('#9AA2B8'))])); rrect(ctx, px - 17, by - 8, 34, 16, 3); ctx.fill()
        # the ghost from the logo, engraved
        ctx.move_to(px - 6, by + 5); ctx.curve_to(px - 7, by - 6, px + 7, by - 6, px + 6, by + 5); ctx.line_to(px + 3, by + 3); ctx.line_to(px, by + 5); ctx.line_to(px - 3, by + 3); ctx.close_path()
        setc(ctx, hx('#5A6278')); ctx.fill()
        circle(ctx, px - 2, by - 1, 1.2); circle(ctx, px + 2, by - 1, 1.2); setc(ctx, hx('#E8ECF4')); ctx.fill()
    # the two tails behind the head
    tx = hcx - hr + (2 if view != 'side' else -2)
    for k, off in enumerate((-4, 6)):
        ctx.move_to(tx, by + off)
        for i in range(1, 7):
            q = i / 6
            ctx.line_to(tx - q * (60 + 30 * wind), by + off + q * (22 + k * 12) + math.sin(t * 8 - q * 5 + k) * 7 * q * wind)
        ctx.set_line_width(9 - k * 2); setc(ctx, hx('#2E3760')); ctx.stroke()

def unicorn_hood(ctx, hcx, head_y, hr, view, t, layer='front'):
    hood = hx('#FFF4FB')
    if layer == 'back':
        # the hood behind the head, and the mane down the back
        circle(ctx, hcx - (6 if view == 'side' else 0), head_y - 4, hr + 12); setc(ctx, OUTLINE); ctx.fill()
        circle(ctx, hcx - (6 if view == 'side' else 0), head_y - 4, hr + 9); setc(ctx, hood); ctx.fill()
        if view != 'front':
            for i, c in enumerate(RAINBOW):
                ctx.save(); ctx.translate(hcx - (hr * .7 if view == 'side' else (i - 2.5) * 14), head_y - hr * .6 + i * 14)
                ctx.rotate(math.radians(-40 + math.sin(t * 6 + i) * 6))
                ellipse(ctx, -14, 0, 22, 9); setc(ctx, c); ctx.fill(); ctx.restore()
        return
    # front layer: hood brim around the face, ears, horn, mane fringe, bellhop cap
    ox = 6 if view == 'side' else 0
    ctx.save()
    ctx.arc(hcx + ox, head_y + 6, hr + 9, math.pi * 1.0, math.pi * 2.0)
    ctx.arc_negative(hcx + ox, head_y + 10, hr - 6, math.pi * 2.0, math.pi * 1.0); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, hood); ctx.fill()
    ctx.restore()
    # mane fringe
    for i, c in enumerate(RAINBOW[:4]):
        ellipse(ctx, hcx + ox - 26 + i * 14, head_y - hr + 14, 11, 9); setc(ctx, c); ctx.fill()
    # ears
    for sgn in ((-1, 1) if view == 'front' else (-1,)):
        ex_ = hcx + ox + sgn * (hr * .78)
        ctx.move_to(ex_ - 12, head_y - hr + 8); ctx.line_to(ex_ + sgn * 8, head_y - hr - 30); ctx.line_to(ex_ + 14, head_y - hr + 10); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, hood); ctx.fill()
        ctx.move_to(ex_ - 5, head_y - hr + 4); ctx.line_to(ex_ + sgn * 6, head_y - hr - 18); ctx.line_to(ex_ + 7, head_y - hr + 6); ctx.close_path()
        setc(ctx, hx('#FFB3D6')); ctx.fill()
    # golden spiral horn
    hb = (hcx + ox + (10 if view == 'side' else 0), head_y - hr - 2)
    ctx.move_to(hb[0] - 13, hb[1]); ctx.line_to(hb[0] + (14 if view == 'side' else 4), hb[1] - 66); ctx.line_to(hb[0] + 13, hb[1]); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve()
    ctx.set_source(lingrad(hb[0] - 13, 0, hb[0] + 13, 0, [(0, hx('#FFE9A8')), (.5, hx('#F2C24A')), (1, hx('#C9952A'))])); ctx.fill()
    tip = (hb[0] + (14 if view == 'side' else 4), hb[1] - 66)
    for k in range(1, 5):
        q = k / 5
        x0 = lerp(hb[0] - 13, tip[0], q); x1 = lerp(hb[0] + 13, tip[0], q); yy = lerp(hb[1], tip[1], q)
        line(ctx, x0, yy + 4, x1, yy - 3, 2.5, hx('#B07E1E'))
    sp = .5 + .5 * math.sin(t * 4)
    sparkle(ctx, tip[0] + 4, tip[1] - 2, 10 * sp + 3, hx('#FFFFFF'), .9)
    # bellhop pillbox cap, tipped to one side
    ctx.save(); ctx.translate(hcx + ox - hr * .55, head_y - hr - 4); ctx.rotate(math.radians(-14))
    rrect(ctx, -22, -24, 44, 26, 6); setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, hx('#C8233C')); ctx.fill()
    ctx.rectangle(-22, -6, 44, 7); setc(ctx, hx('#E2B23C')); ctx.fill()
    ellipse(ctx, 0, -24, 22, 5); setc(ctx, hx('#E0384F')); ctx.fill()
    ctx.restore()
    # bow tie
    if view == 'front':
        by = head_y + hr + 14
        ctx.move_to(hcx, by); ctx.line_to(hcx - 18, by - 10); ctx.line_to(hcx - 18, by + 10); ctx.close_path()
        ctx.move_to(hcx, by); ctx.line_to(hcx + 18, by - 10); ctx.line_to(hcx + 18, by + 10); ctx.close_path()
        setc(ctx, hx('#C8233C')); ctx.fill(); circle(ctx, hcx, by, 5); ctx.fill()

# ── action-anime effects ───────────────────────────────────────────────────
def sparkle(ctx, x, y, r, c=(1, 1, 1, 1), a=1.0, rot=0.0):
    if r <= 0 or a <= 0: return
    ctx.save(); ctx.translate(x, y); ctx.rotate(rot)
    ctx.move_to(0, -r)
    for k in range(4):
        ang = k * math.pi / 2
        ctx.curve_to(math.sin(ang) * r * .12, -math.cos(ang) * r * .12, math.sin(ang + math.pi / 4) * r * .12, -math.cos(ang + math.pi / 4) * r * .12,
                     math.sin(ang + math.pi / 2) * r, -math.cos(ang + math.pi / 2) * r)
    ctx.close_path(); setc(ctx, c, a); ctx.fill()
    ctx.restore()

def radial_lines(ctx, cx, cy, r_in, r_out, n, seed, c, a=1.0, wmax=16):
    """anime focus lines: thin wedges from outside the frame toward a centre; re-randomised every frame"""
    rnd = random.Random(int(seed * 30))
    setc(ctx, c, a)
    for _ in range(n):
        ang = rnd.uniform(0, math.tau)
        ri = r_in * rnd.uniform(.85, 1.5)
        w = rnd.uniform(.004, .022) * (wmax / 16)
        ctx.move_to(cx + math.cos(ang) * ri, cy + math.sin(ang) * ri)
        ctx.line_to(cx + math.cos(ang - w) * r_out, cy + math.sin(ang - w) * r_out)
        ctx.line_to(cx + math.cos(ang + w) * r_out, cy + math.sin(ang + w) * r_out)
        ctx.close_path()
    ctx.fill()

def impact_frame(ctx, cx, cy, seed, a=1.0, c0=None, c1=None, lines=(1, 1, 1, 1)):
    """the hero-moment background: brand gradient burst with white focus lines"""
    if a <= 0: return
    c0 = c0 or ACC; c1 = c1 or ACC2
    g = cairo.RadialGradient(cx, cy, 40, cx, cy, 1300)
    g.add_color_stop_rgba(0, 1, 1, 1, a); g.add_color_stop_rgba(.12, c1[0], c1[1], c1[2], a)
    g.add_color_stop_rgba(.5, c0[0], c0[1], c0[2], a); g.add_color_stop_rgba(1, .05, .03, .14, a)
    ctx.set_source(g); ctx.paint()
    radial_lines(ctx, cx, cy, 260, 1600, 90, seed, lines, .55 * a)

def motion_lines(ctx, x0, y0, x1, y1, n, seed, c=(1, 1, 1, 1), a=.5, direction=1):
    """horizontal speed streaks inside a box; direction -1 trails to the left"""
    rnd = random.Random(int(seed * 30) + 7)
    for _ in range(n):
        y = rnd.uniform(y0, y1); ln = rnd.uniform(.25, 1) * (x1 - x0); x = rnd.uniform(x0, x1 - ln)
        line(ctx, x, y, x + ln, y, rnd.uniform(1.5, 4), c if a >= 1 else (c[0], c[1], c[2], a * rnd.uniform(.4, 1)))

def magic_circle(ctx, cx, cy, r, t, c=None, a=1.0):
    c = c or LIME1
    if a <= 0: return
    ctx.save(); ctx.translate(cx, cy)
    glow(ctx, 0, 0, r * 1.5, c, .35 * a)
    for k, (rr, wdt, sp) in enumerate(((1.0, 3, .6), (.82, 2, -1.0), (.55, 2, 1.4))):
        ctx.save(); ctx.rotate(t * sp)
        ctx.set_line_width(wdt); setc(ctx, c, .9 * a); circle(ctx, 0, 0, r * rr); ctx.stroke()
        for j in range(12 if k == 0 else 8):
            ang = j / (12 if k == 0 else 8) * math.tau
            ctx.save(); ctx.rotate(ang); ctx.translate(r * rr * (.9 if k == 0 else 1.0), 0)
            if k == 0: rrect(ctx, -3, -6, 6, 12, 2)
            else: circle(ctx, 0, 0, 3.2)
            setc(ctx, c, .9 * a); ctx.fill(); ctx.restore()
        ctx.restore()
    ctx.save(); ctx.rotate(-t * .4)
    ctx.set_line_width(2); setc(ctx, c, .8 * a)
    for j in range(6):
        a0 = j / 6 * math.tau; a1 = (j + 2) / 6 * math.tau
        ctx.move_to(math.cos(a0) * r * .82, math.sin(a0) * r * .82); ctx.line_to(math.cos(a1) * r * .82, math.sin(a1) * r * .82)
    ctx.stroke(); ctx.restore()
    ctx.restore()

def exclaim(ctx, x, y, s, a=1.0, t=0.0):
    """the anime surprise mark over a head"""
    if a <= 0: return
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s); ctx.rotate(.12)
    for k in range(3):
        ang = math.radians(-60 + k * 30)
        line(ctx, math.cos(ang) * 46, math.sin(ang) * 46 - 40, math.cos(ang) * 64, math.sin(ang) * 64 - 40, 4, (1, .9, .3, a))
    ctx.move_to(-9, -70); ctx.line_to(9, -70); ctx.line_to(5, -18); ctx.line_to(-5, -18); ctx.close_path()
    circle(ctx, 0, -2, 7)
    setc(ctx, OUTLINE, a); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, (1, .86, .25, a)); ctx.fill()
    ctx.restore()

def sheet_ghost(ctx, x, y, s, t, seed=0, expr='happy', alpha=1.0, look=0.0, arms_up=0.0):
    """kawaii child ghost (nod to Ben's Cute Ghost puppet): big eyes, rosy cheeks, flat shading"""
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    ctx.push_group()
    w = 60
    ctx.move_to(-w, 0)
    ctx.curve_to(-w, -95, w, -95, w, 0)
    ctx.line_to(w, 50)
    n = 4
    for i in range(n):
        x0 = w - (2 * w) * i / n; x1 = w - (2 * w) * (i + 1) / n
        wv = math.sin(t * 6 + i * 1.7 + seed) * 6
        ctx.curve_to(x0 - 6, 62 + wv, x1 + 6, 62 - wv, x1, 50 + (wv * .3))
    ctx.close_path()
    setc(ctx, hx('#F8F9F9')); ctx.fill_preserve()
    ctx.set_line_width(3); setc(ctx, hx('#B9C7E0')); ctx.stroke()
    # little sheet arms
    for sgn in (-1, 1):
        a = arms_up * 50 + math.sin(t * 5 + seed + sgn) * 10
        ctx.save(); ctx.translate(sgn * (w - 4), -6); ctx.rotate(math.radians(sgn * (40 + a)))
        ellipse(ctx, 0, 14, 12, 24); setc(ctx, hx('#F8F9F9')); ctx.fill(); ctx.restore()
    # face
    for ex_ in (-20, 20):
        ellipse(ctx, ex_ + look * 6, -26, 11, 15); setc(ctx, hx('#1A1A26')); ctx.fill()
        circle(ctx, ex_ + look * 6 + 4, -31, 4); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    for ex_ in (-34, 34):
        ellipse(ctx, ex_ + look * 6, -8, 9, 5); setc(ctx, hx('#FF8FA0', .7)); ctx.fill()
    if expr == 'happy':
        ctx.set_line_width(3.5); setc(ctx, hx('#1A1A26')); ctx.arc(look * 6, -10, 8, .2, math.pi - .2); ctx.stroke()
    else:
        ellipse(ctx, look * 6, -6, 7, 9); setc(ctx, hx('#1A1A26')); ctx.fill()
    ctx.pop_group_to_source(); ctx.paint_with_alpha(alpha)
    ctx.restore()

# ── car ─────────────────────────────────────────────────────────────────────
CAR = hx('#E8743B')
def car(ctx, x, y, s, wheel_rot, door=0.0, driver=True, bounce=0.0, t=0.0):
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    ctx.translate(0, -bounce)
    # shadow
    ellipse(ctx, 0, 6 + bounce, 175, 14); setc(ctx, (0, 0, 0, .45)); ctx.fill()
    # body
    def body_path():
        ctx.move_to(-170, -30)
        ctx.curve_to(-172, -70, -150, -78, -120, -80)
        ctx.curve_to(-95, -140, 60, -150, 85, -84)
        ctx.curve_to(140, -80, 172, -66, 172, -34)
        ctx.curve_to(172, -16, 160, -12, 150, -12)
        ctx.line_to(-160, -12)
        ctx.curve_to(-168, -12, -170, -20, -170, -30)
        ctx.close_path()
    body_path(); setc(ctx, OUTLINE); ctx.set_line_width(7); ctx.stroke_preserve()
    ctx.set_source(lingrad(0, -150, 0, -12, [(0, shade(CAR, 1.12)), (.6, CAR), (1, shade(CAR, .7))])); ctx.fill()
    # windows
    ctx.move_to(-104, -84); ctx.curve_to(-84, -128, -20, -132, -12, -130); ctx.line_to(-12, -84); ctx.close_path()
    ctx.move_to(0, -130); ctx.curve_to(40, -128, 62, -112, 72, -86); ctx.line_to(0, -84); ctx.close_path()
    setc(ctx, hx('#1C2A44')); ctx.fill_preserve(); ctx.set_line_width(4); setc(ctx, OUTLINE); ctx.stroke()
    # moon sheen on glass
    line(ctx, -80, -100, -60, -122, 5, (1, 1, 1, .25)); line(ctx, 20, -122, 44, -96, 5, (1, 1, 1, .2))
    if driver:
        ctx.save(); ctx.move_to(0, -130); ctx.curve_to(40, -128, 62, -112, 72, -86); ctx.line_to(0, -84); ctx.close_path(); ctx.clip()
        circle(ctx, 34, -98, 20); setc(ctx, hx('#0A0F1C')); ctx.fill()
        ctx.restore()
    # door seam + handle
    ctx.set_line_width(3); setc(ctx, shade(CAR, .6)); ctx.move_to(-6, -84); ctx.line_to(-6, -16); ctx.stroke()
    rrect(ctx, 6, -66, 18, 6, 3); setc(ctx, hx('#2A2A30')); ctx.fill()
    # headlight + tail
    ellipse(ctx, 160, -52, 12, 14); setc(ctx, hx('#FFF6C8')); ctx.fill_preserve(); ctx.set_line_width(3); setc(ctx, OUTLINE); ctx.stroke()
    rrect(ctx, -172, -60, 10, 18, 4); setc(ctx, hx('#FF3B3B')); ctx.fill()
    # bumpers
    rrect(ctx, 130, -26, 48, 12, 6); setc(ctx, hx('#C9CED8')); ctx.fill()
    rrect(ctx, -178, -26, 40, 12, 6); setc(ctx, hx('#C9CED8')); ctx.fill()
    ctx.restore()
    # wheels (no bounce)
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    for wx in (-100, 100):
        circle(ctx, wx, -18, 34); setc(ctx, OUTLINE); ctx.fill()
        circle(ctx, wx, -18, 27); setc(ctx, hx('#24242C')); ctx.fill()
        circle(ctx, wx, -18, 15); setc(ctx, hx('#C9CED8')); ctx.fill()
        for k in range(4):
            a = wheel_rot + k * math.pi / 2
            line(ctx, wx, -18, wx + math.cos(a) * 14, -18 + math.sin(a) * 14, 3.5, hx('#6A6E78'))
    ctx.restore()

def car_door(ctx, x, y, s, door):
    """open door panel drawn in front of the person stepping out"""
    if door <= 0: return
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    # hinge at x=-6; door swings toward camera: appears as panel widening leftward and lighter
    w = 92 * door
    ctx.move_to(-6, -84); ctx.line_to(-6 - w, -96 - 20 * door); ctx.line_to(-6 - w, -6); ctx.line_to(-6, -16); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, shade(CAR, .95)); ctx.fill()
    ctx.move_to(-10, -82); ctx.line_to(-6 - w * .9, -92 - 18 * door); ctx.line_to(-6 - w * .9, -60); ctx.line_to(-10, -58); ctx.close_path()
    setc(ctx, hx('#1C2A44')); ctx.fill()
    ctx.restore()

# ════════════════════════════════════════════════════════════════════════════
# PHONES + ISHAUNTED SCREENS  (390x844 design points, Signal dark tokens)
# ════════════════════════════════════════════════════════════════════════════
SW, SH = 390, 844

def phone_frame(ctx, cx, cy, w, ang=0.0, screen=None, t=0.0, back=False, tilt_y=1.0):
    h = w * SH / SW * 1.02
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang); ctx.scale(1, tilt_y)
    r = w * .15
    # drop shadow
    rrect(ctx, -w / 2 + 10, -h / 2 + 18, w, h, r); setc(ctx, (0, 0, 0, .45)); ctx.fill()
    rrect(ctx, -w / 2 - 4, -h / 2 - 4, w + 8, h + 8, r + 3); setc(ctx, hx('#05070A')); ctx.fill()
    ctx.set_source(lingrad(-w / 2, 0, w / 2, 0, [(0, hx('#3A3F4C')), (.5, hx('#5A6070')), (1, hx('#2A2E38'))]))
    rrect(ctx, -w / 2, -h / 2, w, h, r); ctx.fill()
    if back:
        rrect(ctx, -w / 2 + 5, -h / 2 + 5, w - 10, h - 10, r - 4); setc(ctx, hx('#1D2230')); ctx.fill()
        # camera bump
        bw = w * .42
        rrect(ctx, -w / 2 + w * .07, -h / 2 + w * .07, bw, bw, bw * .28); setc(ctx, hx('#2B3142')); ctx.fill()
        for (lx, ly) in ((.28, .28), (.72, .28), (.28, .72)):
            cxl, cyl = -w / 2 + w * .07 + bw * lx, -h / 2 + w * .07 + bw * ly
            circle(ctx, cxl, cyl, bw * .2); setc(ctx, hx('#0A0C12')); ctx.fill()
            circle(ctx, cxl, cyl, bw * .12); setc(ctx, hx('#16203A')); ctx.fill()
            circle(ctx, cxl - bw * .04, cyl - bw * .04, bw * .035); setc(ctx, (1, 1, 1, .5)); ctx.fill()
        circle(ctx, -w / 2 + w * .07 + bw * .72, -h / 2 + w * .07 + bw * .72, bw * .07); setc(ctx, hx('#FFF2C0')); ctx.fill()
        # logo on the back (small, embossed)
        ctx.restore(); return
    # screen
    pad = w * .035
    sw_, sh_ = w - 2 * pad, h - 2 * pad
    ctx.save()
    rrect(ctx, -w / 2 + pad, -h / 2 + pad, sw_, sh_, r - pad); ctx.clip()
    ctx.translate(-w / 2 + pad, -h / 2 + pad); ctx.scale(sw_ / SW, sh_ / SH)
    setc(ctx, BG); ctx.paint()
    if screen: screen(ctx, t)
    ctx.restore()
    # dynamic island
    rrect(ctx, -w * .14, -h / 2 + pad + h * .012, w * .28, h * .036, h * .018); setc(ctx, hx('#000000')); ctx.fill()
    # glass sheen
    ctx.save(); rrect(ctx, -w / 2 + pad, -h / 2 + pad, sw_, sh_, r - pad); ctx.clip()
    ctx.move_to(-w / 2, -h / 2); ctx.line_to(w * .1, -h / 2); ctx.line_to(-w / 2, h * .1); ctx.close_path()
    setc(ctx, (1, 1, 1, .045)); ctx.fill(); ctx.restore()
    ctx.restore()

def status_bar(ctx):
    text(ctx, '9:41', 34, 34, INK, size=16, bold=True)
    # signal bars / battery
    for i in range(4):
        rrect(ctx, 282 + i * 6, 30 - i * 3, 4, 4 + i * 3, 1); setc(ctx, INK if i < 3 else FAINT); ctx.fill()
    rrect(ctx, 316, 20, 30, 14, 4); ctx.set_line_width(1.5); setc(ctx, INK); ctx.stroke()
    rrect(ctx, 318.5, 22.5, 20, 9, 2); setc(ctx, INK); ctx.fill()

def app_bar(ctx, big=True):
    if big:
        draw_logo(ctx, 70, 104, 92)
        wordmark(ctx, 122, 118, 36)
    else:
        draw_logo(ctx, 44, 84, 50)
        wordmark(ctx, 74, 92, 24)
    ctx.set_line_width(1); setc(ctx, (1, 1, 1, .09)); ctx.move_to(0, 158 if big else 118); ctx.line_to(SW, 158 if big else 118); ctx.stroke()

def screen_pass(ctx, t):
    """guest's event pass — the Field Kit pass that opens with no signal"""
    status_bar(ctx); app_bar(ctx, big=True)
    text(ctx, 'Your pass', 24, 200, LINK, size=15, bold=True)
    font(ctx, 'Public Sans', 30, True)
    text(ctx, 'Halloween Masquerade', 24, 238, INK, size=30, bold=True)
    text(ctx, 'Ravenwood Manor · Sat 31 Oct · 8 PM', 24, 266, MUTED, size=15)
    # card
    rrect(ctx, 20, 290, 350, 410, 20); setc(ctx, SURF); ctx.fill_preserve(); ctx.set_line_width(1); setc(ctx, (1, 1, 1, .09)); ctx.stroke()
    rrect(ctx, 65, 314, 260, 260, 16); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    draw_qr(ctx, 83, 332, 224)
    # little logo badge in centre of QR (EC level M copes)
    rrect(ctx, 175, 424, 40, 40, 9); setc(ctx, BG); ctx.fill()
    draw_logo(ctx, 195, 444, 36)
    text(ctx, 'Admit one', 195, 614, INK, 'c', size=22, bold=True)
    text(ctx, 'Sam Porter · Guest 47', 195, 640, MUTED, 'c', size=15)
    # pill
    rrect(ctx, 104, 660, 182, 26, 13); setc(ctx, (ACC[0], ACC[1], ACC[2], .16)); ctx.fill()
    text(ctx, 'Opens with no signal', 195, 678, LINK, 'c', size=13, bold=True)
    # button
    rrect(ctx, 20, 724, 350, 54, 14); ctx.set_source(acc_grad(20, 724, 370, 778)); ctx.fill()
    text(ctx, 'Show this at the door', 195, 758, (1, 1, 1, 1), 'c', size=17, bold=True)
    rrect(ctx, 130, 822, 130, 5, 3); setc(ctx, INK, .8); ctx.fill()

def _phone_in_view(ctx):
    rrect(ctx, -84, -150, 168, 300, 24); setc(ctx, hx('#2A2E38')); ctx.fill()
    rrect(ctx, -78, -144, 156, 288, 20); setc(ctx, BG); ctx.fill()
    rrect(ctx, -62, -76, 124, 124, 8); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    draw_qr(ctx, -54, -68, 108)
    draw_logo(ctx, -52, -118, 34); font(ctx, 'Irish Grover', 15); setc(ctx, INK); ctx.move_to(-32, -112); ctx.show_text('IsHaunted')

def screen_scanner(ctx, t, scan=0.0, confirmed=0.0, show_check=False, check_k=1.0, label=None, subject='phone'):
    """door helper's scanner (receptionist). scan: 0..1 sweep progress; confirmed: 0..1 tint"""
    status_bar(ctx); app_bar(ctx, big=True)
    text(ctx, 'Door check-in', 24, 200, LINK, size=15, bold=True)
    text(ctx, 'Halloween Masquerade', 24, 238, INK, size=30, bold=True)
    n_in = 47 if confirmed > .5 else 46
    text(ctx, f'Ravenwood Manor · {n_in} of 120 in', 24, 266, MUTED, size=15)
    # viewfinder
    vx, vy, vw, vh = 20, 290, 350, 330
    ctx.save(); rrect(ctx, vx, vy, vw, vh, 20); ctx.clip()
    ctx.set_source(lingrad(0, vy, 0, vy + vh, [(0, hx('#3A2430')), (1, hx('#1A1218'))])); ctx.paint()
    glow(ctx, vx + vw * .7, vy + 40, 160, hx('#FFB86B'), .25)
    # guest phone seen through camera
    ctx.save(); ctx.translate(vx + vw / 2, vy + vh / 2 + 10); ctx.rotate(-.05)
    if subject == 'scroll':
        from figure import scroll as _scroll
        _scroll(ctx, 0, -53, 260, 382, t)
        ctx.restore()
    else:
        _phone_in_view(ctx)
        ctx.restore()
    if False:
      rrect(ctx, -84, -150, 168, 300, 24); setc(ctx, hx('#2A2E38')); ctx.fill()
      rrect(ctx, -78, -144, 156, 288, 20); setc(ctx, BG); ctx.fill()
      rrect(ctx, -62, -76, 124, 124, 8); setc(ctx, (1, 1, 1, 1)); ctx.fill()
      draw_qr(ctx, -54, -68, 108)
      draw_logo(ctx, -52, -118, 34); font(ctx, 'Irish Grover', 15); setc(ctx, INK); ctx.move_to(-32, -112); ctx.show_text('IsHaunted')
    # scan line
    if confirmed < .5:
        sy = vy + 60 + (vh - 120) * (0.5 + 0.5 * math.sin(t * 5))
        ctx.set_source(lingrad(0, sy - 30, 0, sy + 4, [(0, (ACC2[0], ACC2[1], ACC2[2], 0)), (1, (ACC2[0], ACC2[1], ACC2[2], .5))]))
        ctx.rectangle(vx, sy - 30, vw, 34); ctx.fill()
        line(ctx, vx + 30, sy + 3, vx + vw - 30, sy + 3, 3, ACC2)
    if confirmed > 0:
        setc(ctx, (LIME0[0], LIME0[1], LIME0[2], .35 * confirmed)); ctx.paint()
    ctx.restore()
    # corner brackets
    bc = mix(ACC2, LIME1, confirmed)
    ctx.set_line_width(5); setc(ctx, bc); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    bx0, by0, bx1, by1 = vx + 92, vy + 94, vx + vw - 92, vy + vh - 72
    L_ = 26
    for (px, py, dx, dy) in ((bx0, by0, 1, 1), (bx1, by0, -1, 1), (bx0, by1, 1, -1), (bx1, by1, -1, -1)):
        ctx.move_to(px, py + dy * L_); ctx.line_to(px, py); ctx.line_to(px + dx * L_, py); ctx.stroke()
    # result area
    if label is None: label = confirmed
    if confirmed > 0 and label > 0:
        a = clamp(label)
        text(ctx, 'Confirmed, one guest', 195, 770, INK, 'c', size=26, bold=True, a=a)
        text(ctx, 'Sam Porter · Admit one', 195, 798, MUTED, 'c', size=15, a=a)
    elif confirmed <= 0:
        text(ctx, 'Hold the pass inside the frame', 195, 700, MUTED, 'c', size=16)
    if show_check:
        draw_check(ctx, 195, 690, 48 * check_k, 1.0)
    rrect(ctx, 130, 822, 130, 5, 3); setc(ctx, INK, .8); ctx.fill()

def draw_check(ctx, cx, cy, r, a=1.0, shadow=0.0, shadow_dy=0.0):
    if shadow > 0:
        ctx.save()
        ellipse(ctx, cx + shadow_dy * .35, cy + shadow_dy, r * 1.05, r * 1.05)
        setc(ctx, (0, 0, 0, .45 * shadow * a)); ctx.fill(); ctx.restore()
        glow(ctx, cx + shadow_dy * .35, cy + shadow_dy, r * 1.6, (0, 0, 0, 1), .35 * shadow * a)
    ctx.push_group()
    circle(ctx, cx, cy, r + r * .08); setc(ctx, hx('#0C1017')); ctx.fill()
    ctx.set_source(lingrad(cx - r, cy - r, cx + r, cy + r, [(0, LIME1), (1, LIME0)]))
    circle(ctx, cx, cy, r); ctx.fill()
    # rim highlight
    ctx.set_line_width(r * .08); setc(ctx, (1, 1, 1, .35)); ctx.arc(cx, cy, r * .86, math.pi * 1.05, math.pi * 1.55); ctx.stroke()
    ctx.set_line_cap(cairo.LINE_CAP_ROUND); ctx.set_line_join(cairo.LINE_JOIN_ROUND)
    ctx.set_line_width(r * .22); setc(ctx, (1, 1, 1, 1))
    ctx.move_to(cx - r * .45, cy + r * .02); ctx.line_to(cx - r * .12, cy + r * .34); ctx.line_to(cx + r * .5, cy - r * .32); ctx.stroke()
    ctx.pop_group_to_source(); ctx.paint_with_alpha(a)
