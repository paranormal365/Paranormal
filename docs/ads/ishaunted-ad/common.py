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

GUEST = dict(skin=SKIN, hair=HAIR, top=HOODIE, pants=PANTS, shoe=SHOE, hair_style='short', costume=None)

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

def face(ctx, cx, cy, view, expr, t, look=(0, 0), blink=0.0, skin=SKIN):
    """cx,cy = head centre (local). view 'front' | 'side' (facing +x)."""
    ink = OUTLINE
    if view == 'front':
        ex = [(-21, -6), (21, -6)]; scale = [1, 1]
        mx = 0
    else:
        ex = [(14, -6), (40, -6)]; scale = [.8, 1]
        mx = 30
    lx, ly = look
    if expr == 'fear':
        jit = math.sin(t * 60) * 1.2
        for (x, y), k in zip(ex, scale):
            ellipse(ctx, cx + x, cy + y, 15 * k, 20); setc(ctx, (1, 1, 1, 1)); ctx.fill_preserve()
            ctx.set_line_width(3); setc(ctx, ink); ctx.stroke()
            circle(ctx, cx + x + lx * 4 + jit, cy + y + 3 + ly * 3, 4.5); setc(ctx, ink); ctx.fill()
        # brows up in the middle
        ctx.set_line_width(5); setc(ctx, HAIR); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
        (x0, y0), (x1, y1) = ex
        ctx.move_to(cx + x0 - 13, cy + y0 - 22); ctx.line_to(cx + x0 + 10, cy + y0 - 32); ctx.stroke()
        ctx.move_to(cx + x1 + 13, cy + y1 - 22); ctx.line_to(cx + x1 - 10, cy + y1 - 32); ctx.stroke()
        # wobbly open mouth
        ctx.save(); ctx.translate(cx + mx, cy + 30)
        ellipse(ctx, 0, 0, 13, 15 + math.sin(t * 40) * 1.5); setc(ctx, hx('#4A1620')); ctx.fill_preserve()
        ctx.set_line_width(3); setc(ctx, ink); ctx.stroke()
        rrect(ctx, -8, -12, 16, 6, 2); setc(ctx, (1, 1, 1, 1)); ctx.fill()
        ctx.restore()
        # sweat drop
        dx = cx + (48 if view == 'front' else -30); dy = cy - 30 + (t * 30 % 18)
        ctx.move_to(dx, dy - 14); ctx.curve_to(dx + 9, dy, dx + 7, dy + 9, dx, dy + 9)
        ctx.curve_to(dx - 7, dy + 9, dx - 9, dy, dx, dy - 14); setc(ctx, hx('#9ED8FF')); ctx.fill_preserve()
        ctx.set_line_width(2); setc(ctx, hx('#3B7FB5')); ctx.stroke()
        return
    if expr == 'huge':
        # happy closed eyes ^ ^
        for (x, y), k in zip(ex, scale):
            ctx.set_line_width(5); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            ctx.arc(cx + x, cy + y + 6, 11 * k, math.pi * 1.1, math.pi * 1.9); ctx.stroke()
        # cheeks
        for x in ((-38, 38) if view == 'front' else (8, 52)):
            ellipse(ctx, cx + x, cy + 16, 11, 7); setc(ctx, hx('#FF7A8A', .55)); ctx.fill()
        # huge D mouth
        ctx.save(); ctx.translate(cx + mx, cy + 14)
        w = 32 if view == 'front' else 24
        ctx.move_to(-w, 0); ctx.line_to(w, 0); ctx.curve_to(w, 34, -w, 34, -w, 0); ctx.close_path()
        setc(ctx, hx('#5A1A22')); ctx.fill_preserve(); ctx.set_line_width(3.5); setc(ctx, ink); ctx.stroke()
        ctx.save(); ctx.move_to(-w, 0); ctx.line_to(w, 0); ctx.curve_to(w, 34, -w, 34, -w, 0); ctx.close_path(); ctx.clip()
        ctx.rectangle(-w, 0, 2 * w, 8); setc(ctx, (1, 1, 1, 1)); ctx.fill()
        ellipse(ctx, 0, 26, w * .55, 10); setc(ctx, hx('#FF7A8A')); ctx.fill()
        ctx.restore(); ctx.restore()
        return
    # neutral / smile / focus
    for (x, y), k in zip(ex, scale):
        if blink > .5:
            ctx.set_line_width(4); setc(ctx, ink); ctx.move_to(cx + x - 10 * k, cy + y); ctx.line_to(cx + x + 10 * k, cy + y); ctx.stroke()
            continue
        ellipse(ctx, cx + x, cy + y, 11 * k, 14); setc(ctx, (1, 1, 1, 1)); ctx.fill_preserve()
        ctx.set_line_width(3); setc(ctx, ink); ctx.stroke()
        circle(ctx, cx + x + lx * 4 + (2 if view == 'side' else 0), cy + y + 1 + ly * 4, 6); setc(ctx, ink); ctx.fill()
        circle(ctx, cx + x + lx * 4 + 3, cy + y - 2 + ly * 4, 2); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    ctx.set_line_width(4); setc(ctx, ink); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    if expr == 'smile':
        ctx.arc(cx + mx, cy + 14, 14, math.pi * .15, math.pi * .85); ctx.stroke()
    else:
        ctx.arc(cx + mx, cy + 18, 9, math.pi * .25, math.pi * .75); ctx.stroke()

def person(ctx, x, y, s=1.0, facing='right', walk=None, expr='neutral', t=0.0, look=(0, 0),
           arms=None, phone=None, look_cfg=GUEST, dance=None, bob=0.0, lean=0.0, blink=0.0, alpha=1.0):
    """
    Cartoon person, feet at (x,y), ~300px tall at s=1.
    facing: 'front' | 'back' | 'right' | 'left'
    walk: phase (radians) or None. dance: phase or None.
    arms: dict {'near': (a1,a2), 'far': (a1,a2)} angles, 0=down, +90=forward(+x).
          front view: 'l','r' with + meaning outward is handled by caller.
    phone: None | 'near' | 'far' | 'l' | 'r' — which hand holds a phone. 'pocket' hides it.
    """
    L = look_cfg
    if alpha < 1: ctx.push_group()
    ctx.save(); ctx.translate(x, y)
    mirror = facing == 'left'
    if mirror: ctx.scale(-s, s)
    else: ctx.scale(s, s)
    view = 'side' if facing in ('left', 'right') else facing
    sw = math.sin(walk) if walk is not None else 0.0
    by = -abs(math.sin(walk)) * 7 if walk is not None else 0.0
    dsw = 0.0
    if dance is not None:
        by = -abs(math.sin(dance)) * 16
        dsw = math.sin(dance * .5) * 8
    by -= bob
    ctx.translate(0, by); ctx.rotate(math.radians(lean + dsw * .4))
    cost = L.get('costume')
    top = L['top']; pants = L['pants']; skin = L['skin']

    hip_y, sh_y, head_y = -95, -183, -258
    # cape behind everything
    if cost == 'vampire':
        ctx.move_to(-40, sh_y - 10); ctx.curve_to(-75, -60, -70, -20, -60, -18)
        ctx.line_to(60, -18); ctx.curve_to(70, -20, 75, -60, 40, sh_y - 10); ctx.close_path()
        setc(ctx, hx('#9E1B32')); ctx.fill_preserve(); ctx.set_line_width(4); setc(ctx, OUTLINE); ctx.stroke()

    # legs
    if cost == 'witch':
        legs_len = 60
    else:
        legs_len = 85
    def leg(ox, ang, c):
        r = math.radians(ang)
        y0 = -legs_len - 10
        fx, fy = ox + math.sin(r) * legs_len, y0 + math.cos(r) * legs_len
        ctx.set_line_cap(cairo.LINE_CAP_ROUND)
        ctx.set_line_width(29); setc(ctx, OUTLINE); ctx.move_to(ox, y0); ctx.line_to(fx, fy); ctx.stroke()
        ctx.set_line_width(24); setc(ctx, c); ctx.move_to(ox, y0); ctx.line_to(fx, fy); ctx.stroke()
        fwd = 14 if view == 'side' else 0
        ellipse(ctx, fx + fwd, fy + 6, 22 if view == 'side' else 17, 11); setc(ctx, OUTLINE); ctx.fill()
        ellipse(ctx, fx + fwd, fy + 5, 19 if view == 'side' else 14, 8.5); setc(ctx, L['shoe']); ctx.fill()
    if dance is not None:
        la, ra = math.sin(dance) * 14, -math.sin(dance) * 14
    elif walk is not None:
        la, ra = sw * 28, -sw * 28
    else:
        la, ra = 8, -8
    if view == 'side':
        leg(0, ra, shade(pants, .75)); leg(0, la, pants)
    else:
        if walk is not None:
            # walking toward/away: legs shorten alternately
            k1 = 1 - max(0, sw) * .18; k2 = 1 - max(0, -sw) * .18
            for ox, k in ((-19, k1), (19, k2)):
                fy = hip_y + 95 * k
                ctx.set_line_cap(cairo.LINE_CAP_ROUND)
                ctx.set_line_width(29); setc(ctx, OUTLINE); ctx.move_to(ox, hip_y); ctx.line_to(ox, fy - 6); ctx.stroke()
                ctx.set_line_width(24); setc(ctx, pants); ctx.move_to(ox, hip_y); ctx.line_to(ox, fy - 6); ctx.stroke()
                ellipse(ctx, ox, fy, 17, 11); setc(ctx, OUTLINE); ctx.fill()
                ellipse(ctx, ox, fy - 1, 14, 8.5); setc(ctx, L['shoe']); ctx.fill()
        else:
            leg(-19, la * .5 + 3, pants); leg(19, ra * .5 - 3, pants)

    # arms (far side first in side view)
    def arm_angles(name, default):
        if arms and name in arms: return arms[name]
        return default
    hand_pos = {}
    if view == 'side':
        a_far = arm_angles('far', (-sw * 25, -sw * 25 + 10))
        if dance is not None: a_far = (170 + math.sin(dance) * 25, 160 + math.sin(dance) * 25)
        hand_pos['far'] = _limb(ctx, -4, sh_y + 6, a_far[0], a_far[1], 50, 46, 22, shade(top, .72), hand=shade(skin, .85))
        if phone == 'far': draw_phone_small(ctx, hand_pos['far'][0] + 6, hand_pos['far'][1] - 10, 10)

    # torso
    tw = 40 if view == 'side' else 50
    if cost == 'witch':
        ctx.move_to(-tw + 6, sh_y - 8); ctx.line_to(tw - 6, sh_y - 8); ctx.line_to(tw + 22, -30); ctx.line_to(-tw - 22, -30); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve(); setc(ctx, top); ctx.fill()
        # jagged hem
        ctx.move_to(-tw - 22, -30)
        for i in range(9):
            xx = -tw - 22 + (2 * tw + 44) * (i + .5) / 9; ctx.line_to(xx, -18); ctx.line_to(-tw - 22 + (2 * tw + 44) * (i + 1) / 9, -30)
        ctx.close_path(); setc(ctx, shade(top, .8)); ctx.fill()
    else:
        rrect(ctx, -tw - 2.5, sh_y - 14 - 2.5, 2 * tw + 5, (hip_y + 10) - (sh_y - 14) + 5, 24); setc(ctx, OUTLINE); ctx.fill()
        rrect(ctx, -tw, sh_y - 14, 2 * tw, (hip_y + 10) - (sh_y - 14), 22); setc(ctx, top); ctx.fill()
        if cost is None:
            # hoodie details
            if view == 'front':
                rrect(ctx, -26, -128, 52, 26, 8); setc(ctx, shade(top, .82)); ctx.fill()
                line(ctx, -9, sh_y - 4, -11, sh_y + 30, 3, (1, 1, 1, .85)); line(ctx, 9, sh_y - 4, 11, sh_y + 30, 3, (1, 1, 1, .85))
            elif view == 'side':
                rrect(ctx, -6, -128, 34, 24, 8); setc(ctx, shade(top, .82)); ctx.fill()
            # hood behind neck
            if view != 'back':
                ellipse(ctx, -8 if view == 'side' else 0, sh_y - 10, 34, 12); setc(ctx, shade(top, .7)); ctx.fill()
            else:
                ellipse(ctx, 0, sh_y + 8, 36, 26); setc(ctx, shade(top, .8)); ctx.fill()
        elif cost == 'vampire':
            ctx.move_to(-14, sh_y - 12); ctx.line_to(0, sh_y + 40); ctx.line_to(14, sh_y - 12); ctx.close_path(); setc(ctx, (1, 1, 1, 1)); ctx.fill()
            ctx.move_to(-10, sh_y - 4); ctx.line_to(0, sh_y + 4); ctx.line_to(10, sh_y - 4); ctx.line_to(0, sh_y + 12); ctx.close_path(); setc(ctx, hx('#C21E3A')); ctx.fill()
            # collar
            ctx.move_to(-tw, sh_y - 8); ctx.line_to(-tw - 22, sh_y - 52); ctx.line_to(-12, sh_y - 14); ctx.close_path()
            ctx.move_to(tw, sh_y - 8); ctx.line_to(tw + 22, sh_y - 52); ctx.line_to(12, sh_y - 14); ctx.close_path()
            setc(ctx, hx('#1A1020')); ctx.fill()
        elif cost == 'mummy':
            ctx.save(); rrect(ctx, -tw, sh_y - 14, 2 * tw, (hip_y + 10) - (sh_y - 14), 22); ctx.clip()
            for i in range(9):
                yy = sh_y - 14 + i * 14
                line(ctx, -tw, yy + (i % 2) * 6, tw, yy + 8 - (i % 2) * 6, 3, hx('#B8AE96'))
            ctx.restore()
        elif cost == 'skeleton':
            for i in range(4):
                yy = sh_y + 14 + i * 18
                line(ctx, -26, yy, 26, yy, 7, hx('#F2F2EA'))
            line(ctx, 0, sh_y + 4, 0, hip_y, 8, hx('#F2F2EA'))

    # head
    hcx = 6 if view == 'side' else 0
    hr = 58
    # neck
    rrect(ctx, -14, sh_y - 28, 28, 24, 8); setc(ctx, shade(skin, .85)); ctx.fill()
    if cost == 'pumpkin':
        ellipse(ctx, hcx, head_y, hr + 12 + 2.5, hr + 2.5); setc(ctx, OUTLINE); ctx.fill()
        ellipse(ctx, hcx, head_y, hr + 12, hr); setc(ctx, hx('#F28A1C')); ctx.fill()
        for k in (-28, 0, 28):
            ctx.set_line_width(3); setc(ctx, hx('#C25E0C')); ctx.move_to(hcx + k, head_y - hr + 6)
            ctx.curve_to(hcx + k * 1.5, head_y - 20, hcx + k * 1.5, head_y + 20, hcx + k, head_y + hr - 6); ctx.stroke()
        rrect(ctx, hcx - 6, head_y - hr - 16, 12, 20, 4); setc(ctx, hx('#3E7A2A')); ctx.fill()
        # jack face
        for ex_ in (-22, 22):
            ctx.move_to(hcx + ex_ - 12, head_y - 2); ctx.line_to(hcx + ex_, head_y - 22); ctx.line_to(hcx + ex_ + 12, head_y - 2); ctx.close_path()
        setc(ctx, hx('#3A1600')); ctx.fill()
        ctx.move_to(hcx - 34, head_y + 14)
        for i in range(7):
            xx = hcx - 34 + 68 * (i + .5) / 7; ctx.line_to(xx, head_y + (30 if i % 2 else 20))
        ctx.line_to(hcx + 34, head_y + 14); ctx.curve_to(hcx + 20, head_y + 44, hcx - 20, head_y + 44, hcx - 34, head_y + 14)
        setc(ctx, hx('#3A1600')); ctx.fill()
        glow(ctx, hcx, head_y + 10, 40, hx('#FFB347'), .25)
    else:
        circle(ctx, hcx, head_y, hr + 2.5); setc(ctx, OUTLINE); ctx.fill()
        circle(ctx, hcx, head_y, hr); setc(ctx, skin if cost != 'skeleton' else hx('#F2F2EA')); ctx.fill()
        # ears
        if view == 'front':
            for ex_ in (-hr + 2, hr - 2):
                ellipse(ctx, hcx + ex_, head_y + 4, 10, 14); setc(ctx, shade(skin, .9)); ctx.fill()
        elif view == 'side':
            ellipse(ctx, hcx - 10, head_y + 4, 10, 14); setc(ctx, shade(skin, .9)); ctx.fill()
        if cost == 'mummy':
            ctx.save(); circle(ctx, hcx, head_y, hr); ctx.clip()
            for i in range(8):
                yy = head_y - hr + i * 16
                line(ctx, hcx - hr, yy + (i % 2) * 8, hcx + hr, yy + 10 - (i % 2) * 8, 4, hx('#B8AE96'))
            ctx.restore()
        # hair
        hs = L.get('hair_style', 'short')
        if hs == 'short' and cost not in ('mummy', 'skeleton'):
            ctx.save(); circle(ctx, hcx, head_y, hr); ctx.clip()
            if view == 'back':
                circle(ctx, hcx, head_y - 6, hr + 4)
            else:
                off = -10 if view == 'side' else 0
                ctx.move_to(hcx - hr - 5, head_y + (10 if view == 'side' else -8))
                ctx.curve_to(hcx - hr, head_y - hr - 30, hcx + hr + 10, head_y - hr - 30, hcx + hr + 5, head_y - 14 + (6 if view == 'side' else 0))
                for i in range(5):
                    xx = hcx + hr - (2 * hr) * (i + 1) / 5 + off
                    ctx.line_to(xx + 10, head_y - 26 - (i % 2) * 10)
                ctx.close_path()
            setc(ctx, L['hair']); ctx.fill(); ctx.restore()
            # tuft
            ctx.move_to(hcx - 6, head_y - hr + 2); ctx.curve_to(hcx, head_y - hr - 22, hcx + 18, head_y - hr - 18, hcx + 14, head_y - hr - 4)
            setc(ctx, L['hair']); ctx.fill()
        elif hs == 'long':
            ctx.save()
            ctx.move_to(hcx - hr - 6, head_y + 60); ctx.curve_to(hcx - hr - 20, head_y - hr - 30, hcx + hr + 20, head_y - hr - 30, hcx + hr + 6, head_y + 60)
            ctx.line_to(hcx + hr - 14, head_y + 60); ctx.curve_to(hcx + hr - 4, head_y - 10, hcx + 20, head_y - 34, hcx - 10, head_y - 30)
            ctx.curve_to(hcx - 30, head_y - 20, hcx - hr + 10, head_y + 10, hcx - hr + 14, head_y + 60); ctx.close_path()
            setc(ctx, L['hair']); ctx.fill_preserve(); ctx.set_line_width(3); setc(ctx, OUTLINE); ctx.stroke(); ctx.restore()
        elif hs == 'slick':
            ctx.save(); circle(ctx, hcx, head_y, hr); ctx.clip()
            ctx.move_to(hcx - hr - 4, head_y - 4); ctx.curve_to(hcx - hr, head_y - hr - 20, hcx + hr, head_y - hr - 20, hcx + hr + 4, head_y - 4)
            ctx.line_to(hcx + 14, head_y - 22); ctx.line_to(hcx, head_y - 6); ctx.line_to(hcx - 14, head_y - 22); ctx.close_path()
            setc(ctx, L['hair']); ctx.fill(); ctx.restore()
        if view != 'back':
            if cost == 'skeleton':
                for ex_ in (-20, 20):
                    ellipse(ctx, hcx + ex_, head_y - 4, 15, 17); setc(ctx, OUTLINE); ctx.fill()
                ctx.move_to(hcx, head_y + 10); ctx.line_to(hcx - 6, head_y + 22); ctx.line_to(hcx + 6, head_y + 22); ctx.close_path(); ctx.fill()
                for i in range(6):
                    line(ctx, hcx - 22 + i * 9, head_y + 32, hcx - 22 + i * 9, head_y + 42, 2.5, OUTLINE)
                line(ctx, hcx - 26, head_y + 37, hcx + 26, head_y + 37, 2.5, OUTLINE)
            else:
                face(ctx, hcx, head_y, view, expr, t, look=look, blink=blink, skin=skin)
                if cost == 'vampire':
                    for fx_ in (-7, 7):
                        ctx.move_to(hcx + (30 if view == 'side' else 0) + fx_ - 3, head_y + 22); ctx.line_to(hcx + (30 if view == 'side' else 0) + fx_, head_y + 32)
                        ctx.line_to(hcx + (30 if view == 'side' else 0) + fx_ + 3, head_y + 22); ctx.close_path(); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    if cost == 'witch':
        ctx.save(); ctx.translate(hcx, head_y - hr + 14); ctx.rotate(math.radians(-8))
        ellipse(ctx, 0, 0, 82, 16); setc(ctx, OUTLINE); ctx.fill()
        ellipse(ctx, 0, -2, 78, 12); setc(ctx, hx('#2A1638')); ctx.fill()
        ctx.move_to(-42, -4); ctx.curve_to(-20, -60, 10, -110, 34, -128); ctx.curve_to(26, -90, 34, -40, 42, -4); ctx.close_path()
        setc(ctx, hx('#2A1638')); ctx.fill_preserve(); ctx.set_line_width(4); setc(ctx, OUTLINE); ctx.stroke()
        ctx.rectangle(-40, -22, 82, 14); setc(ctx, hx('#E07B1F')); ctx.fill()
        ctx.restore()
    if cost == 'cat':
        for sgn in (-1, 1):
            ctx.move_to(hcx + sgn * 20, head_y - hr + 8); ctx.line_to(hcx + sgn * 48, head_y - hr - 30); ctx.line_to(hcx + sgn * 52, head_y - hr + 26); ctx.close_path()
            setc(ctx, hx('#1A1620')); ctx.fill()
        for sgn in (-1, 1):
            for k in (-6, 4):
                line(ctx, hcx + sgn * 18, head_y + 16, hcx + sgn * 50, head_y + 12 + k, 2, OUTLINE)

    # near arm(s)
    if view == 'side':
        a_near = arm_angles('near', (sw * 25, sw * 25 + 10))
        if dance is not None: a_near = (160 - math.sin(dance) * 25, 150 - math.sin(dance) * 25)
        hand_pos['near'] = _limb(ctx, 4, sh_y + 6, a_near[0], a_near[1], 50, 46, 22, top if cost != 'vampire' else hx('#1A1020'), hand=skin)
        if phone == 'near': draw_phone_small(ctx, hand_pos['near'][0] + 8, hand_pos['near'][1] - 12, 8)
    else:
        if dance is not None:
            al = arm_angles('l', (-160 + math.sin(dance) * 25, -150 + math.sin(dance) * 30))
            ar = arm_angles('r', (160 + math.sin(dance) * 25, 150 + math.sin(dance) * 30))
        else:
            al = arm_angles('l', (-12 - sw * 8, -6))
            ar = arm_angles('r', (12 + sw * 8, 6))
        armc = top if cost != 'vampire' else hx('#1A1020')
        hand_pos['l'] = _limb(ctx, -tw + 6, sh_y + 6, al[0], al[1], 50, 46, 22, armc, hand=skin)
        hand_pos['r'] = _limb(ctx, tw - 6, sh_y + 6, ar[0], ar[1], 50, 46, 22, armc, hand=skin)
        if phone in ('l', 'r'):
            hp = hand_pos[phone]; draw_phone_small(ctx, hp[0], hp[1] - 14, 0, back=(view == 'front'))
    ctx.restore()
    if alpha < 1:
        ctx.pop_group_to_source(); ctx.paint_with_alpha(alpha)
    return hand_pos

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

def screen_scanner(ctx, t, scan=0.0, confirmed=0.0, show_check=False, check_k=1.0, label=None):
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
    rrect(ctx, -84, -150, 168, 300, 24); setc(ctx, hx('#2A2E38')); ctx.fill()
    rrect(ctx, -78, -144, 156, 288, 20); setc(ctx, BG); ctx.fill()
    rrect(ctx, -62, -76, 124, 124, 8); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    draw_qr(ctx, -54, -68, 108)
    draw_logo(ctx, -52, -118, 34); font(ctx, 'Irish Grover', 15); setc(ctx, INK); ctx.move_to(-32, -112); ctx.show_text('IsHaunted')
    ctx.restore()
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
