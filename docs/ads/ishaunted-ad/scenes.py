import math, random
import cairo
from PIL import Image, ImageFilter
from common import *
from common import _limb

# ── global timeline (seconds) ───────────────────────────────────────────────
T1, T2, T3, T4, T5, T6, T7, TEND = 0.0, 5.5, 9.0, 14.0, 17.0, 21.0, 25.5, 30.0

def cam(ctx, z, fx, fy, k=None):
    """zoom by z keeping world point (fx,fy) moving toward screen centre by k (default tied to z)"""
    if k is None: k = clamp((z - 1) / max(z, 1e-6) * 1.6)
    sx, sy = lerp(fx, W / 2, k), lerp(fy, H / 2, k)
    ctx.translate(sx, sy); ctx.scale(z, z); ctx.translate(-fx, -fy)

def paint_cached(ctx, surf, k):
    ctx.save(); ctx.scale(1 / k, 1 / k); ctx.set_source_surface(surf, 0, 0)
    ctx.get_source().set_filter(cairo.FILTER_BILINEAR); ctx.paint(); ctx.restore()

def make_cache(fn, k=1.5, w=W, h=H):
    s, c = new_surface(int(w * k), int(h * k)); c.scale(k, k); fn(c); return s

# ════════════════════════════════════════════════════════════════════════════
# SCENE 1 — the house at night
# ════════════════════════════════════════════════════════════════════════════
def pine(ctx, x, base, h, w, c):
    setc(ctx, c)
    for i in range(4):
        y0 = base - h * i / 4.6
        ww = w * (1 - i / 5)
        ctx.move_to(x - ww, y0); ctx.line_to(x, y0 - h * .42); ctx.line_to(x + ww, y0); ctx.close_path()
    ctx.fill()
    ctx.rectangle(x - w * .08, base - 4, w * .16, 20); ctx.fill()

def forest(ctx, base, hmin, hmax, step, c, seed, x0=-80, x1=2000):
    rnd = random.Random(seed)
    x = x0
    while x < x1:
        h = rnd.uniform(hmin, hmax)
        pine(ctx, x, base + rnd.uniform(-10, 10), h, h * rnd.uniform(.22, .3), c)
        x += step * rnd.uniform(.6, 1.2)

def window(ctx, x, y, w, h, lit, arch=True, shutters=False, crooked=0.0, broken=False):
    # frame
    if arch:
        ctx.move_to(x, y + h); ctx.line_to(x, y + w / 2); ctx.arc(x + w / 2, y + w / 2, w / 2, math.pi, 0); ctx.line_to(x + w, y + h); ctx.close_path()
    else:
        ctx.rectangle(x, y, w, h)
    path = ctx.copy_path()
    if lit > 0:
        ctx.set_source(lingrad(0, y, 0, y + h, [(0, hx('#FFE29A', lit)), (1, hx('#E8913A', lit))]))
    else:
        setc(ctx, hx('#0D0B18'))
    ctx.fill()
    ctx.append_path(path); ctx.set_line_width(5); setc(ctx, hx('#4A4366')); ctx.stroke()
    # muntins
    line(ctx, x + w / 2, y + 4, x + w / 2, y + h, 3, hx('#4A4366'))
    line(ctx, x, y + h * .55, x + w, y + h * .55, 3, hx('#4A4366'))
    if broken:
        ctx.move_to(x + w * .55, y + h * .58); ctx.line_to(x + w * .8, y + h * .7); ctx.line_to(x + w * .62, y + h * .8); ctx.line_to(x + w * .95, y + h * .92)
        ctx.set_line_width(2); setc(ctx, hx('#8A84A6')); ctx.stroke()
    if shutters:
        for sx, rot in ((x - w * .42, -crooked), (x + w + 2, crooked * .3)):
            ctx.save(); ctx.translate(sx, y + 6); ctx.rotate(rot)
            ctx.rectangle(0, 0, w * .4, h - 6); setc(ctx, hx('#1E1A2E')); ctx.fill()
            for k in range(6):
                line(ctx, 3, 8 + k * (h - 16) / 6, w * .4 - 3, 8 + k * (h - 16) / 6, 2, hx('#2F2944'))
            ctx.restore()

S1_LIT = [  # (x,y,w,h) windows that flicker
    (950, 570, 60, 90), (1310, 570, 60, 90), (920, 740, 55, 90), (825, 620, 40, 75), (1420, 680, 55, 90)]

def s1_bg(c):
    c.set_source(lingrad(0, 0, 0, 760, [(0, hx('#05070F')), (.6, hx('#121A35')), (1, hx('#2B2D52'))])); c.paint()
    rnd = random.Random(3)
    for _ in range(220):
        x, y = rnd.uniform(0, W), rnd.uniform(0, 600); r = rnd.uniform(.6, 1.8)
        circle(c, x, y, r); setc(c, (1, 1, 1, rnd.uniform(.25, .8))); c.fill()
    # moon
    glow(c, 1460, 250, 520, hx('#BFD3FF'), .35)
    glow(c, 1460, 250, 230, hx('#FFF6D8'), .55)
    circle(c, 1460, 250, 130)
    c.set_source(cairo.RadialGradient(1420, 210, 10, 1460, 250, 130)); g = c.get_source()
    g.add_color_stop_rgba(0, *hx('#FFFBEA')); g.add_color_stop_rgba(1, *hx('#E6DDB8')); c.fill()
    for (dx, dy, r) in ((-40, -30, 24), (30, 20, 30), (-10, 55, 16), (55, -45, 12), (-60, 40, 10)):
        circle(c, 1460 + dx, 250 + dy, r); setc(c, hx('#CFC49C', .45)); c.fill()
    # cloud wisps
    for (x, y, rx, ry, a) in ((520, 180, 300, 22, .18), (300, 260, 220, 16, .15)):
        ellipse(c, x, y, rx, ry); setc(c, hx('#0E1328', a)); c.fill()
    # forests (surround the house)
    forest(c, 690, 160, 260, 46, hx('#18203D'), 1)
    forest(c, 735, 200, 330, 52, hx('#0F152B'), 2)
    # ground
    c.set_source(lingrad(0, 700, 0, H, [(0, hx('#141C26')), (1, hx('#080B10'))])); c.rectangle(0, 720, W, H); c.fill()
    ellipse(c, 960, 735, 1200, 40); setc(c, hx('#141C26')); c.fill()
    # driveway + parking
    c.move_to(-50, 875); c.line_to(400, 862); c.line_to(400, 960); c.line_to(-50, 990); c.close_path()
    setc(c, hx('#2E323C')); c.fill()
    rrect(c, 360, 845, 560, 120, 40); setc(c, hx('#343844')); c.fill()
    rnd = random.Random(9)
    for _ in range(900):
        x, y = rnd.uniform(-40, 910), rnd.uniform(850, 980)
        if x < 380 and not (862 - (x + 50) * .03 < y < 990 - (x + 50) * .067): continue
        if x >= 380 and not (850 < y < 960): continue
        circle(c, x, y, rnd.uniform(.8, 2.2)); setc(c, hx('#4C5260', rnd.uniform(.3, .8))); c.fill()
    # parking lines
    for x in (460, 760):
        line(c, x, 860, x - 20, 950, 4, hx('#9AA0AE', .35))
    # walkway stones to the porch
    for i, (x, y) in enumerate(((930, 905), (990, 893), (1045, 884), (1100, 876))):
        ellipse(c, x, y, 34, 11); setc(c, hx('#3E4250')); c.fill()

    # ── the house ──
    sid = hx('#2C2542'); roof = hx('#181427'); trim = hx('#3D3558')
    # tower
    c.rectangle(780, 400, 130, 450); setc(c, shade(sid, .9)); c.fill()
    c.move_to(762, 412); c.line_to(845, 190); c.line_to(928, 412); c.close_path(); setc(c, roof); c.fill_preserve()
    c.set_line_width(4); setc(c, trim); c.stroke()
    line(c, 845, 190, 845, 150, 4, hx('#3D3558')); c.move_to(845, 158); c.line_to(872, 150); c.line_to(845, 144); c.close_path(); setc(c, hx('#3D3558')); c.fill()
    # main block
    c.rectangle(900, 520, 480, 330); setc(c, sid); c.fill()
    for y in range(530, 850, 16):
        line(c, 900, y, 1380, y, 1.5, hx('#000000', .18), cap=cairo.LINE_CAP_BUTT)
    # right wing
    c.rectangle(1380, 620, 140, 230); setc(c, shade(sid, .85)); c.fill()
    c.move_to(1362, 632); c.line_to(1450, 538); c.line_to(1538, 632); c.close_path(); setc(c, roof); c.fill_preserve(); c.set_line_width(4); setc(c, trim); c.stroke()
    # chimney
    c.rectangle(1286, 370, 44, 110); setc(c, hx('#3A2630')); c.fill()
    c.rectangle(1280, 362, 56, 14); setc(c, hx('#2A1A22')); c.fill()
    # main roof (slightly sagging ridge)
    c.move_to(868, 534); c.line_to(1138, 328); c.curve_to(1180, 352, 1260, 420, 1412, 534); c.close_path(); setc(c, roof); c.fill_preserve()
    c.set_line_width(5); setc(c, trim); c.stroke()
    for k in range(1, 9):
        yy = 328 + k * 23
        x_l = 1138 - (yy - 328) * 1.31; x_r = 1138 + (yy - 328) * 1.33
        line(c, x_l + 6, yy, x_r - 6, yy, 1.5, hx('#000', .35), cap=cairo.LINE_CAP_BUTT)
    # gable trim gingerbread
    for k in range(10):
        a = k / 9
        x = lerp(890, 1130, a); y = lerp(520, 340, a)
        circle(c, x, y + 12, 6); setc(c, hx('#4A4366')); c.fill()
    # attic round window (ghost face appears here — animated)
    circle(c, 1140, 448, 34); setc(c, hx('#0D0B18')); c.fill_preserve(); c.set_line_width(6); setc(c, hx('#4A4366')); c.stroke()
    line(c, 1106, 448, 1174, 448, 3, hx('#4A4366')); line(c, 1140, 414, 1140, 482, 3, hx('#4A4366'))
    # windows
    for (x, y, w, h) in S1_LIT:
        window(c, x, y, w, h, 0, arch=True)
    window(c, 1060, 570, 60, 90, 0, shutters=True, crooked=.25, broken=True)
    window(c, 1210, 570, 60, 90, 0, broken=False, shutters=True, crooked=.05)
    window(c, 825, 470, 40, 70, 0)
    # porch
    c.rectangle(985, 698, 310, 22); setc(c, roof); c.fill()
    c.move_to(980, 700); c.line_to(1140, 650); c.line_to(1300, 700); c.close_path(); setc(c, roof); c.fill_preserve(); c.set_line_width(4); setc(c, trim); c.stroke()
    c.rectangle(1105, 730, 70, 118); setc(c, hx('#3A1C1C')); c.fill()
    rrect(c, 1112, 738, 24, 44, 3); setc(c, hx('#2A1414')); c.fill(); rrect(c, 1144, 738, 24, 44, 3); c.fill()
    circle(c, 1164, 792, 4); setc(c, hx('#C9A04A')); c.fill()
    c.move_to(1100, 732); c.arc(1140, 732, 40, math.pi, 0); c.close_path(); setc(c, hx('#3D3558')); c.fill()
    for x in (1005, 1275):
        c.rectangle(x - 7, 718, 14, 128); setc(c, hx('#4A4366')); c.fill()
    c.rectangle(985, 836, 310, 14); setc(c, hx('#211B33')); c.fill()
    for i in range(3):
        c.rectangle(1092 - i * 6, 850 + i * 9, 96 + i * 12, 9); setc(c, hx('#2B2540')); c.fill()
    # foundation
    c.rectangle(895, 846, 90, 10); c.rectangle(1295, 846, 230, 10); setc(c, hx('#1E1A28')); c.fill()
    # dead tree
    c.set_line_cap(cairo.LINE_CAP_ROUND); setc(c, hx('#07080D'))
    def branch(x, y, ang, ln, w, d):
        if d == 0 or ln < 8: return
        x2, y2 = x + math.cos(ang) * ln, y + math.sin(ang) * ln
        c.set_line_width(w); c.move_to(x, y); c.line_to(x2, y2); c.stroke()
        branch(x2, y2, ang - .45 + (d % 2) * .1, ln * .72, w * .66, d - 1)
        branch(x2, y2, ang + .38, ln * .68, w * .62, d - 1)
    branch(640, 870, -math.pi / 2 - .08, 150, 26, 7)
    # gravestones
    for (x, y, rot, w, h) in ((1640, 905, -.08, 56, 80), (1740, 895, .1, 46, 64), (1830, 912, -.15, 60, 86), (1560, 925, .05, 40, 54)):
        c.save(); c.translate(x, y); c.rotate(rot)
        c.move_to(-w / 2, 0); c.line_to(-w / 2, -h + w / 2); c.arc(0, -h + w / 2, w / 2, math.pi, 0); c.line_to(w / 2, 0); c.close_path()
        setc(c, hx('#3B4152')); c.fill_preserve(); c.set_line_width(3); setc(c, hx('#22262F')); c.stroke()
        if w > 50:
            font(c, 'Public Sans', 16, True); setc(c, hx('#22262F')); tw = text_w(c, 'RIP'); c.move_to(-tw / 2, -h * .45); c.show_text('RIP')
        c.restore()
    # broken fence
    for i in range(14):
        x = 1300 + i * 22
        if i in (5, 9): continue
        c.save(); c.translate(x, 905); c.rotate((math.sin(i * 7.3) * .12))
        c.move_to(-6, 0); c.line_to(-6, -44); c.line_to(0, -54); c.line_to(6, -44); c.line_to(6, 0); c.close_path()
        setc(c, hx('#1A1D26')); c.fill(); c.restore()
    line(c, 1295, 876, 1600, 870, 5, hx('#1A1D26'))

_FOG = []
def fog_texture(w, h, seed, n, alpha, color=(185, 195, 220)):
    rnd = random.Random(seed)
    im = Image.new('L', (w, h), 0)
    from PIL import ImageDraw
    d = ImageDraw.Draw(im)
    for _ in range(n):
        x = rnd.uniform(0, w); y = rnd.uniform(h * .3, h * .85)
        rx = rnd.uniform(120, 320); ry = rnd.uniform(30, 80)
        for dx in (-w, 0, w):  # tileable
            d.ellipse((x + dx - rx, y - ry, x + dx + rx, y + ry), fill=int(rnd.uniform(.5, 1) * alpha))
    im = im.filter(ImageFilter.GaussianBlur(40))
    rgba = Image.new('RGBA', (w, h), color + (0,)); rgba.putalpha(im)
    return pil_to_surf(rgba, premultiplied=False)

class Scene1:
    def __init__(self):
        self.k = 1.5
        self.bg = make_cache(s1_bg, self.k)
        self.fogA = fog_texture(2400, 420, 1, 26, 120)
        self.fogB = fog_texture(2400, 360, 2, 22, 90)
        self.bats = [(-.2, 120, 1.0), (.3, 190, .8), (.9, 90, .7)]

    def car_x(self, t):
        return lerp(-320, 600, ease_out(seg(t, .2, 2.4)))

    def draw(self, ctx, t, gt):
        # camera: gentle push + snap zoom on the fear look
        px, py, ps, facing, walk, expr = self.guest_state(t)
        z = lerp(1.0, 1.06, ease(seg(t, 0, 5.5)))
        zk = ease(seg(t, 3.95, 4.18)) * (1 - ease(seg(t, 4.75, 5.0)))
        head = (px, py - 258 * ps)
        ctx.save()
        cam(ctx, z + zk * 1.7, lerp(960, head[0], ease(zk)), lerp(560, head[1], ease(zk)), k=ease(zk) * .97 + (1 - ease(zk)) * .05)
        paint_cached(ctx, self.bg, self.k)
        # window flicker
        for i, (x, y, w, h) in enumerate(S1_LIT):
            a = .55 + .25 * noise1(gt * 2.2, i * 3.1) + (.25 if i == 0 else 0)
            window(ctx, x, y, w, h, clamp(a), arch=True)
            glow(ctx, x + w / 2, y + h / 2, 90, hx('#FFC266'), .18 * a)
        # ghost in attic window
        ga = ease(seg(t, 1.2, 1.8)) * (1 - ease(seg(t, 3.4, 3.9)))
        if ga > 0:
            ctx.save(); circle(ctx, 1140, 448, 31); ctx.clip()
            glow(ctx, 1140, 448, 60, hx('#B8F5C8'), .5 * ga)
            sheet_ghost(ctx, 1140 + math.sin(gt * 2) * 4, 470, .45, gt, alpha=ga * .9, expr='o')
            ctx.restore()
        # bats across the moon
        for (ph, y0, s) in self.bats:
            bx = ((t * .22 + ph) % 1.4) * 2200 - 200
            by = y0 + math.sin(t * 2 + ph * 9) * 30
            flap = math.sin(gt * 22 + ph * 5)
            ctx.save(); ctx.translate(bx, by); ctx.scale(s, s)
            ctx.move_to(0, 0); ctx.curve_to(-14, -10 - flap * 12, -30, -6 - flap * 16, -40, -2 - flap * 18)
            ctx.curve_to(-30, 2, -22, 4, -14, 2); ctx.curve_to(-8, 6, -4, 4, 0, 8)
            ctx.curve_to(4, 4, 8, 6, 14, 2); ctx.curve_to(22, 4, 30, 2, 40, -2 - flap * 18)
            ctx.curve_to(30, -6 - flap * 16, 14, -10 - flap * 12, 0, 0); setc(ctx, hx('#05060A')); ctx.fill(); ctx.restore()
        # fog behind car
        self.fog(ctx, self.fogA, 560, gt * 26, .85)
        # car
        cx = self.car_x(t)
        moving = 0 < seg(t, .2, 2.4) < 1
        bounce = (math.sin(gt * 24) * 1.5 if moving else 0) + 4 * math.sin(seg(t, 2.4, 2.8) * math.pi * 2) * (1 - seg(t, 2.4, 2.8))
        cs = .78
        # headlight beam
        hlx, hly = cx + 160 * cs, 915 - 52 * cs - bounce * cs
        ctx.save(); ctx.set_operator(cairo.OPERATOR_ADD)
        ctx.move_to(hlx, hly - 6); ctx.line_to(hlx + 760, hly - 120); ctx.line_to(hlx + 760, hly + 110); ctx.close_path()
        ctx.set_source(lingrad(hlx, 0, hlx + 760, 0, [(0, hx('#FFF2C4', .35)), (1, hx('#FFF2C4', 0))])); ctx.fill()
        glow(ctx, hlx, hly, 40, hx('#FFF6D0'), .7)
        ctx.restore()
        door = ease(seg(t, 2.6, 2.85)) * (1 - ease(seg(t, 3.1, 3.25)))
        car(ctx, cx, 915, cs, (cx + 320) / (34 * cs), bounce=bounce, driver=t < 2.75)
        # guest
        if t >= 2.7:
            a = ease(seg(t, 2.7, 2.95))
            arms = None; phone = 'near'
            if facing == 'front':
                shake = math.sin(gt * 70) * 2.5 * (1 if expr == 'fear' else 0)
                arms = {'l': (-25, -15), 'r': (35, 150)}
                person(ctx, px + shake, py, ps, 'front', expr=expr, t=gt, phone='r', arms=arms, alpha=a)
            else:
                person(ctx, px, py, ps, facing, walk=walk, expr='neutral', t=gt, phone=phone if facing == 'right' else None,
                       arms={'near': (40 + (walk and math.sin(walk) * 8 or 0), 140)} if facing == 'right' else None, alpha=a)
        car_door(ctx, cx, 915, cs, door)
        # foreground fog
        self.fog(ctx, self.fogB, 770, -gt * 40, .75)
        ctx.restore()
        # vignette
        g = cairo.RadialGradient(W / 2, H / 2, H * .4, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .65); ctx.set_source(g); ctx.paint()

    def guest_state(self, t):
        cx = self.car_x(t)
        ps = .5
        if t < 3.0:
            return cx + 26, 930 - 18 * (1 - ease(seg(t, 2.7, 2.95))), ps, 'right', None, 'neutral'
        if t < 3.9:
            p = ease(seg(t, 3.0, 3.9))
            return lerp(cx + 26, 900, p), lerp(930, 905, p), ps, 'right', t * 9, 'neutral'
        if t < 4.85:
            return 900, 905, ps, 'front', None, 'fear' if t > 3.98 else 'neutral'
        p = seg(t, 4.85, 5.6)
        return lerp(900, 1140, p), lerp(905, 862, p), lerp(ps, .42, p), 'back', t * 9, 'neutral'

    def fog(self, ctx, tex, y, off, a):
        w = tex.get_width()
        x = -(off % w)
        ctx.save()
        for dx in (x - w, x, x + w):
            ctx.set_source_surface(tex, dx, y); ctx.paint_with_alpha(a)
        ctx.restore()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 2 — the front door, candles, ghost kids through the dark window
# ════════════════════════════════════════════════════════════════════════════
DOOR = (830, 330, 260, 560)          # x, y, w, h
WIN_L = (360, 330, 240, 430)
WIN_R = (1320, 330, 240, 430)

def arch_path(ctx, x, y, w, h):
    ctx.move_to(x, y + h); ctx.line_to(x, y + w / 2); ctx.arc(x + w / 2, y + w / 2, w / 2, math.pi, 0); ctx.line_to(x + w, y + h); ctx.close_path()

def cobweb(ctx, x, y, r, sx=1, sy=1, a=.45):
    ctx.save(); ctx.translate(x, y); ctx.scale(sx, sy)
    setc(ctx, (.85, .88, .95, a)); ctx.set_line_width(1.4)
    for k in range(6):
        ang = k / 5 * math.pi / 2
        ctx.move_to(0, 0); ctx.line_to(math.cos(ang) * r, math.sin(ang) * r); ctx.stroke()
    for ring in (.3, .55, .8):
        for k in range(5):
            a0 = k / 5 * math.pi / 2; a1 = (k + 1) / 5 * math.pi / 2
            p0 = (math.cos(a0) * r * ring, math.sin(a0) * r * ring); p1 = (math.cos(a1) * r * ring, math.sin(a1) * r * ring)
            m = (math.cos((a0 + a1) / 2) * r * ring * .85, math.sin((a0 + a1) / 2) * r * ring * .85)
            ctx.move_to(*p0); ctx.curve_to(m[0], m[1], m[0], m[1], *p1); ctx.stroke()
    ctx.restore()

def jack(ctx, x, y, s, t, seed=0):
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    glow(ctx, 0, -30, 120, hx('#FF9A2E'), .25 + .08 * noise1(t * 4, seed))
    ellipse(ctx, 0, -40, 62, 46); setc(ctx, OUTLINE); ctx.fill()
    ellipse(ctx, 0, -40, 58, 42); setc(ctx, hx('#E9771C')); ctx.fill()
    for k in (-26, 0, 26):
        ctx.set_line_width(3); setc(ctx, hx('#B5560E')); ctx.move_to(k, -80); ctx.curve_to(k * 1.5, -50, k * 1.5, -30, k, -2); ctx.stroke()
    rrect(ctx, -6, -96, 12, 18, 3); setc(ctx, hx('#3E6B2A')); ctx.fill()
    fl = hx('#FFD25A', .85 + .15 * noise1(t * 9, seed))
    for ex_ in (-20, 20):
        ctx.move_to(ex_ - 11, -40); ctx.line_to(ex_, -60); ctx.line_to(ex_ + 11, -40); ctx.close_path(); setc(ctx, fl); ctx.fill()
    ctx.move_to(-30, -26); ctx.line_to(-18, -16); ctx.line_to(-8, -26); ctx.line_to(4, -16); ctx.line_to(16, -26); ctx.line_to(30, -26)
    ctx.curve_to(20, -4, -20, -4, -30, -26); setc(ctx, fl); ctx.fill()
    ctx.restore()

def candle(ctx, x, y, h, t, seed):
    rrect(ctx, x - 9, y - h, 18, h, 4); setc(ctx, hx('#F2E8D2')); ctx.fill()
    ctx.set_source(lingrad(x - 9, 0, x + 9, 0, [(0, hx('#000', .0)), (1, hx('#000', .25))])); rrect(ctx, x - 9, y - h, 18, h, 4); ctx.fill()
    f = noise1(t * 11, seed); f2 = noise1(t * 7, seed + 4)
    glow(ctx, x, y - h - 14, 110, hx('#FFB54A'), .45 + .15 * f)
    ctx.save(); ctx.translate(x + f2 * 2, y - h - 4)
    fh = 26 + f * 5
    ctx.move_to(0, 0); ctx.curve_to(10, -6, 7, -fh * .6, f2 * 2, -fh); ctx.curve_to(-7, -fh * .6, -10, -6, 0, 0)
    setc(ctx, hx('#FFB23A')); ctx.fill()
    ctx.move_to(0, -2); ctx.curve_to(5, -5, 3, -fh * .45, f2, -fh * .7); ctx.curve_to(-3, -fh * .45, -5, -5, 0, -2)
    setc(ctx, hx('#FFF4C8')); ctx.fill(); ctx.restore()
    line(ctx, x, y - h, x, y - h - 4, 2, hx('#222'))

def s2_wall(c):
    # wall, warmly lit by the sconces
    c.set_source(lingrad(0, 0, 0, 900, [(0, hx('#2A2140')), (1, hx('#3B2D4F'))])); c.rectangle(0, 0, W, 900); c.fill()
    for y in range(0, 900, 26):
        line(c, 0, y, W, y, 2, hx('#000', .22), cap=cairo.LINE_CAP_BUTT)
    for x in (720, 1200):
        glow(c, x, 430, 620, hx('#FFC777'), .38)
    # porch floor
    c.set_source(lingrad(0, 890, 0, H, [(0, hx('#4A3226')), (1, hx('#22160F'))])); c.rectangle(0, 890, W, 190); c.fill()
    for i in range(-14, 15):
        x0 = 960 + i * 90; x1 = 960 + i * 180
        line(c, x0, 890, x1, H, 2, hx('#000', .35))
    line(c, 0, 892, W, 892, 6, hx('#1C120C'))
    # window frames: cut holes
    c.save(); c.set_operator(cairo.OPERATOR_CLEAR)
    for (x, y, w, h) in (WIN_L, WIN_R):
        arch_path(c, x, y, w, h); c.fill()
    c.rectangle(*DOOR); c.fill()
    c.move_to(DOOR[0], DOOR[1]); c.arc(DOOR[0] + DOOR[2] / 2, DOOR[1], DOOR[2] / 2, math.pi, 0); c.close_path(); c.fill()
    c.restore()
    # window trim
    for (x, y, w, h) in (WIN_L, WIN_R):
        arch_path(c, x - 22, y - 22, w + 44, h + 44)
        arch_path(c, x, y, w, h)
        c.set_fill_rule(cairo.FILL_RULE_EVEN_ODD); setc(c, hx('#6E6458')); c.fill(); c.set_fill_rule(cairo.FILL_RULE_WINDING)
        arch_path(c, x - 22, y - 22, w + 44, h + 44); c.set_line_width(3); setc(c, hx('#2A2420')); c.stroke()
        c.rectangle(x - 40, y + h + 14, w + 80, 22); setc(c, hx('#7A6F62')); c.fill()
        line(c, x + w / 2, y + 6, x + w / 2, y + h, 6, hx('#6E6458'))
        line(c, x, y + h * .5, x + w, y + h * .5, 6, hx('#6E6458'))
        cobweb(c, x - 22, y + h * .3, 70, 1, 1, .35)
    # door surround: pilasters + pediment
    dx, dy, dw, dh = DOOR
    for px in (dx - 58, dx + dw + 8):
        c.rectangle(px, dy - 20, 50, dh + 20); setc(c, hx('#8C806E')); c.fill()
        for k in range(5):
            line(c, px + 10 + k * 8, dy, px + 10 + k * 8, dy + dh - 30, 2, hx('#5E5446'))
        c.rectangle(px - 6, dy - 34, 62, 18); setc(c, hx('#A39784')); c.fill()
        c.rectangle(px - 6, dy + dh - 30, 62, 30); setc(c, hx('#7A6F62')); c.fill()
    # fanlight frame
    c.set_line_width(26); setc(c, hx('#8C806E'))
    c.arc(dx + dw / 2, dy, dw / 2 + 13, math.pi, 0); c.stroke()
    # broken pediment
    c.move_to(dx - 80, dy - 150); c.line_to(dx + dw / 2 - 40, dy - 250); c.line_to(dx + dw / 2 - 40, dy - 222); c.line_to(dx - 60, dy - 134); c.close_path()
    c.move_to(dx + dw + 80, dy - 150); c.line_to(dx + dw / 2 + 40, dy - 250); c.line_to(dx + dw / 2 + 40, dy - 222); c.line_to(dx + dw + 60, dy - 134); c.close_path()
    setc(c, hx('#A39784')); c.fill()
    # urn finial
    ellipse(c, dx + dw / 2, dy - 238, 20, 26); setc(c, hx('#A39784')); c.fill()
    # cobwebs on the door frame
    cobweb(c, dx - 58, dy - 20, 110, 1, 1, .5); cobweb(c, dx + dw + 58, dy - 20, 110, -1, 1, .5)
    # sconces
    for x in (720, 1200):
        rrect(c, x - 10, 470, 20, 60, 6); setc(c, hx('#2A2016')); c.fill()
        c.move_to(x - 34, 470); c.line_to(x + 34, 470); c.line_to(x + 22, 380); c.line_to(x - 22, 380); c.close_path()
        c.set_source(lingrad(0, 380, 0, 470, [(0, hx('#FFF1C6')), (1, hx('#FFC56B'))])); c.fill_preserve()
        c.set_line_width(4); setc(c, hx('#2A2016')); c.stroke()
        line(c, x, 380, x, 470, 3, hx('#2A2016')); line(c, x - 28, 425, x + 28, 425, 3, hx('#2A2016'))
        c.move_to(x - 26, 380); c.line_to(x, 352); c.line_to(x + 26, 380); c.close_path(); setc(c, hx('#2A2016')); c.fill()
        glow(c, x, 425, 140, hx('#FFE3A0'), .55)
    # welcome mat
    c.move_to(800, 912); c.line_to(1120, 912); c.line_to(1160, 980); c.line_to(760, 980); c.close_path(); setc(c, hx('#5A3A28')); c.fill()
    font(c, 'Irish Grover', 34); setc(c, hx('#C9A877')); tw = text_w(c, 'Welcome'); c.move_to(960 - tw / 2, 962); c.show_text('Welcome')

class Scene2:
    def __init__(self):
        self.k = 1.25
        self.wall = make_cache(s2_wall, self.k)
        # behind-the-dark-window forest (static part)
        def bw(c):
            x, y, w, h = WIN_R
            c.set_source(lingrad(0, y, 0, y + h, [(0, hx('#0B1230')), (1, hx('#1A2A4A'))])); c.rectangle(x - 30, y - 30, w + 60, h + 60); c.fill()
            glow(c, x + w * .75, y + 70, 160, hx('#CFE0FF'), .45)
            circle(c, x + w * .75, y + 70, 26); setc(c, hx('#F6F1DA')); c.fill()
            rnd = random.Random(5)
            for i in range(7):
                pine(c, x - 20 + i * 45 + rnd.uniform(-10, 10), y + h - 70 - rnd.uniform(0, 40), rnd.uniform(160, 260), 46, hx('#0A1124'))
            c.rectangle(x - 30, y + h - 80, w + 60, 120); setc(c, hx('#0E1A2C')); c.fill()
        self.behind = make_cache(bw, self.k)

    def guest(self, t):
        if t < 2.0:
            p = ease_out(seg(t, 0, 2.0)) if t > 0 else 0
            x = lerp(-220, 960, seg(t, 0, 2.0) * .55 + p * .45)
            return x, 995, 1.3, 'right', t * 8.5
        if t < 2.3:
            return 960, 995, 1.3, 'back', None
        p = ease(seg(t, 2.3, 3.2))
        return 960, lerp(995, 880, p), lerp(1.3, 1.0, p), 'back', t * 8.5

    def draw(self, ctx, t, gt):
        ctx.save()
        z = lerp(1.0, 1.05, ease(seg(t, 0, 3.5)))
        cam(ctx, z, 960, 600, k=.0)
        # behind the dark window: forest + kids playing
        paint_cached(ctx, self.behind, self.k)
        x, y, w, h = WIN_R
        ctx.save(); arch_path(ctx, x, y, w, h); ctx.clip()
        for i in range(3):
            ph = gt * 1.6 + i * 2.1
            gx = x + w / 2 + math.cos(ph) * 70
            gy = y + h - 120 + math.sin(ph * 2) * 18 - abs(math.sin(gt * 4 + i)) * 20
            sheet_ghost(ctx, gx, gy, .42 + .06 * math.sin(ph), gt, seed=i * 3, look=-math.sin(ph), arms_up=.6 + .4 * math.sin(gt * 5 + i), alpha=.92)
        # one peeks from behind a tree
        pk = .5 + .5 * math.sin(gt * 1.3)
        sheet_ghost(ctx, x + 50 + pk * 26, y + h - 170, .3, gt, seed=9, look=1, alpha=.85)
        ctx.restore()
        setc(ctx, hx('#1E3A6A', .18)); arch_path(ctx, x, y, w, h); ctx.fill()
        # candle window (left)
        x, y, w, h = WIN_L
        ctx.save(); arch_path(ctx, x, y, w, h); ctx.clip()
        ctx.set_source(lingrad(0, y, 0, y + h, [(0, hx('#3A1A12')), (1, hx('#5A2A16'))])); ctx.paint()
        glow(ctx, x + w / 2, y + h - 90, 260, hx('#FF9C3A'), .45 + .1 * noise1(gt * 6, 1))
        # curtains
        for sx in (x, x + w):
            ctx.move_to(sx, y); ctx.curve_to(sx + (60 if sx == x else -60), y + h * .4, sx + (20 if sx == x else -20), y + h * .7, sx, y + h)
            ctx.line_to(sx, y); setc(ctx, hx('#6A1420')); ctx.fill()
        # candelabra
        cx_ = x + w / 2; base = y + h - 30
        rrect(ctx, cx_ - 30, base - 10, 60, 14, 4); setc(ctx, hx('#B88A3A')); ctx.fill()
        line(ctx, cx_, base - 10, cx_, base - 90, 8, hx('#B88A3A'))
        ctx.set_line_width(6); setc(ctx, hx('#B88A3A')); ctx.arc(cx_, base - 120, 55, .15 * math.pi, .85 * math.pi); ctx.stroke()
        for i, dx_ in enumerate((-55, 0, 55)):
            candle(ctx, cx_ + dx_, base - (96 if dx_ == 0 else 80), 54 if dx_ == 0 else 44, gt, i * 2.7)
        ctx.restore()
        setc(ctx, (1, 1, 1, .05)); arch_path(ctx, x, y, w, h); ctx.fill()
        # door opening + interior light
        dx, dy, dw, dh = DOOR
        op = ease(seg(t, 1.75, 2.35))
        ctx.save()
        ctx.rectangle(dx, dy, dw, dh); ctx.move_to(dx, dy); ctx.arc(dx + dw / 2, dy, dw / 2, math.pi, 0); ctx.close_path(); ctx.clip()
        ctx.set_source(lingrad(0, dy - 130, 0, dy + dh, [(0, hx('#FFE9B8')), (1, hx('#E7A055'))])); ctx.paint()
        glow(ctx, dx + dw / 2, dy + 120, 260, hx('#FFFFFF'), .6)
        # fanlight muntins
        ctx.set_line_width(5); setc(ctx, hx('#6E6458'))
        for k in range(7):
            a = math.pi + k / 6 * math.pi
            ctx.move_to(dx + dw / 2, dy); ctx.line_to(dx + dw / 2 + math.cos(a) * dw, dy + math.sin(a) * dw); ctx.stroke()
        line(ctx, dx, dy, dx + dw, dy, 8, hx('#6E6458'))
        # guest inside the doorway
        px, py, ps, facing, walk = self.guest(t)
        inside = t > 2.55
        if inside:
            a = 1 - ease(seg(t, 2.8, 3.3))
            person(ctx, px, py, ps, 'back', walk=walk, t=gt, alpha=a)
        # door panel swinging inward (hinge on the left)
        dwv = dw * math.cos(op * math.pi * .46)
        persp = op * 26
        ctx.move_to(dx, dy); ctx.line_to(dx + dwv, dy + persp); ctx.line_to(dx + dwv, dy + dh - persp * .4); ctx.line_to(dx, dy + dh); ctx.close_path()
        ctx.set_source(lingrad(dx, 0, dx + dw, 0, [(0, hx('#5A2626')), (1, shade(hx('#5A2626'), lerp(1, .55, op)))])); ctx.fill()
        ctx.save(); ctx.translate(dx, dy); ctx.scale(dwv / dw, 1)
        for (px_, py_, pw_, ph_) in ((28, 40, 88, 140), (144, 40, 88, 140), (28, 220, 88, 140), (144, 220, 88, 140), (28, 400, 88, 120), (144, 400, 88, 120)):
            rrect(ctx, px_, py_, pw_, ph_, 6); setc(ctx, hx('#3E1818')); ctx.fill()
            rrect(ctx, px_ + 8, py_ + 8, pw_ - 16, ph_ - 16, 4); setc(ctx, hx('#6A2E2E')); ctx.fill()
        circle(ctx, 230, 300, 11); setc(ctx, hx('#D9B25A')); ctx.fill()
        # brass knocker ring
        ctx.set_line_width(6); setc(ctx, hx('#D9B25A')); circle(ctx, 130, 200, 22); ctx.stroke()
        circle(ctx, 130, 176, 9); setc(ctx, hx('#D9B25A')); ctx.fill()
        ctx.restore()
        ctx.restore()
        paint_cached(ctx, self.wall, self.k)
        # jack-o-lanterns on the porch
        jack(ctx, 650, 935, .9, gt, 1); jack(ctx, 1300, 945, .75, gt, 2)
        # guest on the porch
        if not inside:
            ph_ = 'near' if facing == 'right' else None
            person(ctx, px, py, ps, facing, walk=walk, t=gt, expr='neutral', look=(1, 1),
                   arms={'near': (25, 140)} if facing == 'right' else None, phone=ph_)
            if facing == 'right':
                # phone glow on the face
                glow(ctx, px + 80 * ps, py - 175 * ps, 110 * ps, hx('#8E7CFF'), .3)
        # bloom into the house
        bl = ease(seg(t, 2.9, 3.5))
        if bl > 0:
            glow(ctx, 960, 600, 300 + bl * 1500, hx('#FFF1D0'), bl)
        ctx.restore()
        g = cairo.RadialGradient(W / 2, H / 2, H * .45, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .55); ctx.set_source(g); ctx.paint()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 3 — reception
# ════════════════════════════════════════════════════════════════════════════
DESK = (640, 620, 680, 280)
FR_SKIN = hx('#8DB36A')

def s3_room(c):
    c.set_source(lingrad(0, 0, 0, 860, [(0, hx('#2A1428')), (1, hx('#4A2440'))])); c.rectangle(0, 0, W, 860); c.fill()
    # damask
    for row in range(14):
        for col in range(22):
            x = col * 92 + (46 if row % 2 else 0); y = row * 64
            c.save(); c.translate(x, y)
            ellipse(c, 0, 0, 14, 22); ellipse(c, 0, -30, 5, 8); ellipse(c, 0, 30, 5, 8)
            setc(c, hx('#E0A8D8', .07)); c.fill(); c.restore()
    glow(c, 960, 150, 900, hx('#FFC98A'), .3)
    # wainscot
    c.rectangle(0, 640, W, 220); setc(c, hx('#24131F')); c.fill()
    for x in range(20, W, 180):
        rrect(c, x, 670, 150, 160, 6); c.set_line_width(4); setc(c, hx('#3A2030')); c.stroke()
    line(c, 0, 642, W, 642, 10, hx('#3A2030'))
    # floor
    c.set_source(lingrad(0, 860, 0, H, [(0, hx('#3A2418')), (1, hx('#1A0F0A'))])); c.rectangle(0, 860, W, 220); c.fill()
    for i in range(-16, 17):
        line(c, 960 + i * 80, 860, 960 + i * 190, H, 2, hx('#000', .35))
    for y in (900, 950, 1010):
        line(c, 0, y, W, y, 1.5, hx('#000', .25))
    # runner rug
    c.move_to(-50, 1080); c.line_to(560, 880); c.line_to(1400, 880); c.line_to(1980, 1080); c.close_path(); setc(c, hx('#6A1626', .55)); c.fill()
    # key cabinet behind desk
    rrect(c, 800, 270, 340, 300, 8); setc(c, hx('#3A2216')); c.fill()
    for r in range(4):
        for k in range(7):
            x = 820 + k * 45; y = 290 + r * 68
            c.rectangle(x, y, 38, 58); setc(c, hx('#1E120C')); c.fill()
            if (r * 7 + k) % 3:
                line(c, x + 19, y + 10, x + 19, y + 30, 2, hx('#C9A04A'))
                rrect(c, x + 12, y + 30, 14, 18, 2); setc(c, hx('#E8DCC0')); c.fill()
    # portrait (eyes animated)
    rrect(c, 200, 200, 280, 340, 6); setc(c, hx('#B8902E')); c.fill()
    rrect(c, 220, 220, 240, 300, 4); setc(c, hx('#1E2A22')); c.fill()
    ellipse(c, 340, 440, 100, 90); setc(c, hx('#2E2A3A')); c.fill()
    ellipse(c, 340, 330, 56, 70); setc(c, hx('#B9C4A8')); c.fill()
    c.move_to(280, 300); c.curve_to(290, 230, 390, 230, 400, 300); c.curve_to(380, 270, 300, 270, 280, 300); setc(c, hx('#2A1A14')); c.fill()
    line(c, 320, 380, 360, 380, 3, hx('#5A3A3A'))
    # grandfather clock
    rrect(c, 1640, 360, 120, 500, 10); setc(c, hx('#3A2216')); c.fill()
    circle(c, 1700, 430, 42); setc(c, hx('#E8DCC0')); c.fill()
    line(c, 1700, 430, 1700, 400, 4, OUTLINE); line(c, 1700, 430, 1720, 444, 4, OUTLINE)
    rrect(c, 1668, 520, 64, 220, 6); setc(c, hx('#1E120C')); c.fill()
    # cobwebs
    cobweb(c, 0, 0, 220, 1, 1, .35); cobweb(c, W, 0, 220, -1, 1, .35)
    # entrance arch at left
    c.rectangle(0, 300, 120, 560); setc(c, hx('#140A12')); c.fill()

def s3_desk(c):
    x, y, w, h = DESK
    c.rectangle(x, y + 24, w, h - 24); c.set_source(lingrad(0, y, 0, y + h, [(0, hx('#5A3020')), (1, hx('#2E180E'))])); c.fill()
    for k in range(4):
        rrect(c, x + 30 + k * 165, y + 70, 140, 170, 8); c.set_line_width(5); setc(c, hx('#3A1E12')); c.stroke()
    rrect(c, x - 20, y, w + 40, 30, 6); setc(c, hx('#6E3E26')); c.fill()
    line(c, x - 20, y + 30, x + w + 20, y + 30, 4, hx('#C9A04A'))
    # brass plate
    rrect(c, x + w / 2 - 110, y + 46, 220, 40, 6); setc(c, hx('#C9A04A')); c.fill()
    font(c, 'Irish Grover', 26); setc(c, hx('#3A2010')); tw = text_w(c, 'Reception'); c.move_to(x + w / 2 - tw / 2, y + 75); c.show_text('Reception')
    # guest book
    c.move_to(1040, y + 4); c.line_to(1180, y + 4); c.line_to(1196, y - 10); c.line_to(1024, y - 10); c.close_path(); setc(c, hx('#EDE3CC')); c.fill()
    line(c, 1110, y - 10, 1110, y + 4, 2, hx('#8A7A5A'))
    # bell
    ellipse(c, 720, y + 2, 30, 6); setc(c, hx('#8A6A2A')); c.fill()
    c.move_to(694, y); c.curve_to(694, y - 36, 746, y - 36, 746, y); c.close_path(); setc(c, hx('#D9B25A')); c.fill()
    circle(c, 720, y - 32, 5); c.fill()

def frank(ctx, x, y, s, t, look=0.0, phone_k=0.0, wave=0.0, blink=0.0):
    """big friendly Frankenstein-ish receptionist. (x,y) = waist centre (hidden by the desk)."""
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    ctx.translate(0, math.sin(t * 1.6) * 3)
    suit = hx('#1C1A26')
    # left arm (his left = screen right) resting on desk, unless waving
    def arm(side, a1, a2, hand=True, phone=False):
        sx_ = side * 108
        hp = _limb(ctx, sx_, -230, a1, a2, 100, 96, 50, suit, hand=FR_SKIN, hand_r=30)
        return hp
    # torso
    ctx.move_to(-120, 0); ctx.line_to(-128, -230); ctx.curve_to(-120, -280, 120, -280, 128, -230); ctx.line_to(120, 0); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, suit); ctx.fill()
    ctx.move_to(-40, -262); ctx.line_to(0, -140); ctx.line_to(40, -262); ctx.close_path(); setc(ctx, hx('#F2F2EA')); ctx.fill()
    ctx.move_to(-40, -262); ctx.line_to(-70, -150); ctx.line_to(-10, -170); ctx.close_path(); ctx.move_to(40, -262); ctx.line_to(70, -150); ctx.line_to(10, -170); ctx.close_path()
    setc(ctx, hx('#2A2836')); ctx.fill()
    # bow tie
    ctx.move_to(0, -246); ctx.line_to(-28, -262); ctx.line_to(-28, -230); ctx.close_path(); ctx.move_to(0, -246); ctx.line_to(28, -262); ctx.line_to(28, -230); ctx.close_path()
    setc(ctx, hx('#9E1B32')); ctx.fill(); circle(ctx, 0, -246, 7); ctx.fill()
    # name badge
    rrect(ctx, 60, -200, 44, 18, 3); setc(ctx, hx('#C9A04A')); ctx.fill()
    # neck + bolts
    rrect(ctx, -46, -300, 92, 50, 12); setc(ctx, shade(FR_SKIN, .85)); ctx.fill()
    for sgn in (-1, 1):
        rrect(ctx, sgn * 46 + (0 if sgn > 0 else -28), -290, 28, 20, 4); setc(ctx, hx('#8A909C')); ctx.fill()
        rrect(ctx, sgn * 74 + (0 if sgn > 0 else -10), -294, 10, 28, 3); setc(ctx, hx('#5A606C')); ctx.fill()
    # head: tall, flat-topped
    ctx.save(); ctx.translate(look * 6, 0)
    rrect(ctx, -92, -496, 184, 210, 40); setc(ctx, OUTLINE); ctx.fill()
    rrect(ctx, -88, -492, 176, 202, 36); setc(ctx, FR_SKIN); ctx.fill()
    ctx.set_source(lingrad(-88, 0, 88, 0, [(0, hx('#000', 0)), (1, hx('#000', .15))])); rrect(ctx, -88, -492, 176, 202, 36); ctx.fill()
    # flat hair with jagged fringe
    ctx.move_to(-92, -440); ctx.line_to(-92, -500); ctx.line_to(92, -500); ctx.line_to(92, -440)
    for i in range(9):
        xx = 92 - (184 * (i + 1) / 9); ctx.line_to(xx + 10, -428 - (i % 2) * 14); ctx.line_to(xx, -444)
    ctx.close_path(); setc(ctx, hx('#141018')); ctx.fill()
    # forehead stitches
    line(ctx, -50, -418, 30, -414, 3, hx('#2C3A22'))
    for k in range(5):
        xx = -44 + k * 18; line(ctx, xx, -424, xx + 2, -408, 3, hx('#2C3A22'))
    # heavy brow
    rrect(ctx, -70, -404, 140, 16, 8); setc(ctx, hx('#141018')); ctx.fill()
    # eyes
    for ex_ in (-34, 34):
        if blink > .5:
            line(ctx, ex_ - 14, -374, ex_ + 14, -374, 4, OUTLINE)
        else:
            ellipse(ctx, ex_, -374, 16, 13); setc(ctx, (1, 1, 1, 1)); ctx.fill()
            circle(ctx, ex_ + look * 7, -372, 7); setc(ctx, OUTLINE); ctx.fill()
            ctx.rectangle(ex_ - 17, -390, 34, 10); setc(ctx, FR_SKIN); ctx.fill()  # heavy lids
    # nose + scar
    ctx.move_to(-4, -366); ctx.line_to(-16, -330); ctx.line_to(8, -330); setc(ctx, shade(FR_SKIN, .8)); ctx.fill()
    line(ctx, 50, -350, 66, -316, 3, hx('#2C3A22')); line(ctx, 52, -332, 64, -336, 3, hx('#2C3A22'))
    # friendly crooked grin
    ctx.move_to(-50, -318); ctx.curve_to(-20, -292, 24, -292, 54, -322)
    ctx.curve_to(26, -306, -20, -304, -50, -318); setc(ctx, hx('#3A1A1A')); ctx.fill()
    for (tx, tw_) in ((-26, 12), (-8, 12), (14, 10), (32, 9)):
        ctx.rectangle(tx, -312, tw_, 9); setc(ctx, hx('#F2F0D8')); ctx.fill()
    ctx.restore()
    # arms
    # right arm (screen left, toward guest): from desk -> chest -> raised with phone
    pk = phone_k
    if pk < .4:
        e = ease(seg(pk, 0, .4)); a1 = lerp(14, -10, e); a2 = lerp(60, -150, e)
    else:
        e = ease(seg(pk, .4, 1)); a1 = lerp(-10, -62, e); a2 = lerp(-150, -84, e)
    hp = _limb(ctx, -108, -230, a1, a2, 100, 96, 50, suit, hand=FR_SKIN, hand_r=30)
    if pk > .25:
        # phone aimed down-left at the guest's pass: we see its back + camera
        ctx.save(); ctx.translate(hp[0] - 22, hp[1] + 6); ctx.rotate(math.radians(lerp(-10, 28, ease(seg(pk, .4, 1)))))
        rrect(ctx, -34, -62, 68, 124, 14); setc(ctx, OUTLINE); ctx.fill()
        rrect(ctx, -30, -58, 60, 116, 12); setc(ctx, hx('#2A3040')); ctx.fill()
        rrect(ctx, -26, -54, 24, 24, 6); setc(ctx, hx('#141820')); ctx.fill()
        circle(ctx, -14, -42, 6); setc(ctx, hx('#05070A')); ctx.fill()
        ctx.restore()
        circle(ctx, hp[0], hp[1], 30); setc(ctx, FR_SKIN); ctx.fill_preserve(); ctx.set_line_width(4); setc(ctx, OUTLINE); ctx.stroke()
    _limb(ctx, 108, -230, 20 - wave * 150, 50 - wave * (190 + math.sin(t * 14) * 25), 100, 96, 50, suit, hand=FR_SKIN, hand_r=30)
    ctx.restore()

VAMP = dict(GUEST, skin=hx('#E8E4F0'), hair=hx('#14101C'), top=hx('#1A1020'), pants=hx('#1A1020'), shoe=hx('#111111'), hair_style='slick', costume='vampire')
WITCH = dict(GUEST, skin=hx('#D9A07E'), hair=hx('#E0662A'), top=hx('#5B2A86'), pants=hx('#222'), shoe=hx('#111'), hair_style='long', costume='witch')

class Scene3:
    def __init__(self):
        self.k = 1.25
        self.room = make_cache(s3_room, self.k)
        self.desk = make_cache(s3_desk, self.k)

    def guest(self, t):
        if t < 2.4:
            p = seg(t, .2, 2.4)
            return lerp(-200, 520, ease_out(p) * .5 + p * .5), 990, 1.25, (t * 8.5 if p < 1 else None)
        return 520, 990, 1.25, None

    def phone_out(self, t):
        return ease(seg(t, 2.4, 2.9))

    def phone_pos(self, t):
        px, py, ps, _ = self.guest(t)
        out = self.phone_out(t)
        a1 = lerp(30, 88, out); a2 = lerp(130, 92, out)
        r1, r2 = math.radians(a1), math.radians(a2)
        hx_ = 4 + math.sin(r1) * 50 + math.sin(r2) * 46
        hy_ = -177 + math.cos(r1) * 50 + math.cos(r2) * 46
        return px + hx_ * ps, py - 7 * 0 + hy_ * ps, (a1, a2)

    def draw(self, ctx, t, gt):
        ctx.save()
        zk = ease(seg(t, 3.7, 5.0))
        fx, fy, _ = self.phone_pos(t)
        cam(ctx, lerp(1.0, 4.2, ease_in(zk)), lerp(960, fx, zk), lerp(540, fy - 30, zk), k=zk)
        paint_cached(ctx, self.room, self.k)
        # portrait eyes follow the guest
        px, py, ps, walk = self.guest(t)
        lk = clamp((px - 340) / 600, -1, 1)
        for ex_ in (318, 362):
            ellipse(ctx, ex_, 322, 9, 6); setc(ctx, (1, 1, 1, .9)); ctx.fill()
            circle(ctx, ex_ + lk * 4, 323, 3.5); setc(ctx, OUTLINE); ctx.fill()
        # chandelier
        cx_, cy_ = 960, 70
        line(ctx, cx_, 0, cx_, cy_, 4, hx('#2A2016'))
        ctx.set_line_width(6); setc(ctx, hx('#B88A3A')); ellipse(ctx, cx_, cy_ + 30, 160, 26); ctx.stroke()
        for i in range(7):
            xx = cx_ - 150 + i * 50
            candle(ctx, xx, cy_ + 30 - (6 if i % 2 else 0), 30, gt, i * 1.9)
        # receptionist
        look = 0.6 if t < 1.6 else lerp(.6, -1.0, ease(seg(t, 1.6, 2.1)))
        blink = 1 if (gt % 3.1) < .12 else 0
        wave = ease(seg(t, .1, .4)) * (1 - ease(seg(t, 1.3, 1.6)))
        frank(ctx, 970, 640, 1.0, gt, look=look, phone_k=ease(seg(t, 2.8, 3.6)), wave=wave, blink=blink)
        paint_cached(ctx, self.desk, self.k)
        candle(ctx, 1260, 618, 40, gt, 7)
        # the couple heads off to the party
        for i, L in enumerate((VAMP, WITCH)):
            bx = 1420 + i * 120
            if t < .3:
                person(ctx, bx, 985, 1.2, 'left', expr='smile', t=gt, look_cfg=L)
            else:
                cx2 = bx + (t - .3) * 520
                if cx2 < 2200:
                    person(ctx, cx2, 985, 1.2, 'right', walk=gt * 8.5 + i, expr='smile', t=gt, look_cfg=L)
        # our guest
        _, _, (a1, a2) = self.phone_pos(t)
        person(ctx, px, py, ps, 'right', walk=walk, t=gt, expr='smile' if t > 2.6 else 'neutral', look=(1, -.4),
               arms={'near': (a1, a2)}, phone='near')
        ctx.restore()
        g = cairo.RadialGradient(W / 2, H / 2, H * .45, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .5); ctx.set_source(g); ctx.paint()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 4 / 5 — the phones
# ════════════════════════════════════════════════════════════════════════════
def grip_back(ctx, cx, cy, w, h, skin, sleeve, ang=0.0, side='left', scale=1.0, stitches=False, sleeve_dir=(0, 1)):
    """drawn BEFORE the phone: sleeve, palm below the phone, fingertips peeking past one edge."""
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang)
    sgn = -1 if side == 'left' else 1
    # sleeve + wrist coming from sleeve_dir
    sx, sy = sleeve_dir
    ctx.save(); ctx.rotate(math.atan2(-sx, sy))
    ctx.move_to(-w * .34, h * .42); ctx.line_to(-w * .5, h * .42 + 900); ctx.line_to(w * .5, h * .42 + 900); ctx.line_to(w * .34, h * .42); ctx.close_path()
    setc(ctx, skin); ctx.fill()
    ctx.move_to(-w * .44, h * .62); ctx.line_to(-w * .6, h * .62 + 900); ctx.line_to(w * .6, h * .62 + 900); ctx.line_to(w * .44, h * .62); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(8); ctx.stroke_preserve(); setc(ctx, sleeve); ctx.fill()
    ctx.restore()
    ellipse(ctx, 0, h * .42, w * .52, w * .34); setc(ctx, OUTLINE); ctx.fill()
    ellipse(ctx, 0, h * .42, w * .49, w * .31); setc(ctx, skin); ctx.fill()
    fw = 50 * scale; fl = 92 * scale
    for i in range(4):
        y = h * (.02 + i * .1)
        x = sgn * (w / 2 + fl * .18)
        ctx.save(); ctx.translate(x, y); ctx.rotate(sgn * -.12)
        rrect(ctx, -fl / 2, -fw / 2, fl, fw, fw / 2); setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, skin); ctx.fill()
        if stitches and i == 1:
            line(ctx, sgn * fl * .25, -fw * .32, sgn * fl * .25, fw * .32, 3, hx('#2C3A22'))
        if skin == FR_SKIN:
            ellipse(ctx, sgn * fl * .36, 0, fl * .1, fw * .3); setc(ctx, hx('#2C3A22')); ctx.fill()
        ctx.restore()
    ctx.restore()

def grip_front(ctx, cx, cy, w, h, skin, ang=0.0, side='left', scale=1.0):
    """drawn AFTER the phone: the thumb over the opposite edge."""
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang)
    sgn = 1 if side == 'left' else -1
    ctx.save(); ctx.translate(sgn * (w / 2 + 6 * scale), h * .26); ctx.rotate(sgn * .42)
    rrect(ctx, -27 * scale, -78 * scale, 54 * scale, 150 * scale, 27 * scale); setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, skin); ctx.fill()
    ellipse(ctx, 0, -56 * scale, 15 * scale, 18 * scale); setc(ctx, (1, 1, 1, .35)); ctx.fill()
    ctx.restore(); ctx.restore()

def screen_to_world(cx, cy, w, sx, sy):
    h = w * SH / SW * 1.02
    pad = w * .035
    kx = (w - 2 * pad) / SW; ky = (h - 2 * pad) / SH
    return cx - w / 2 + pad + sx * kx, cy - h / 2 + pad + sy * ky, kx, ky

class Scene4:
    def __init__(self, s3):
        def bg(c):
            c.translate(W / 2, H / 2); c.scale(2.0, 2.0); c.translate(-640, -620)
            paint_cached(c, s3.room, s3.k); paint_cached(c, s3.desk, s3.k)
        s, c = new_surface(); bg(c)
        self.bg = blur_surf(s, 18)
        # receptionist's phone, back to us, over-the-shoulder top right; closer => softer
        s, c = new_surface()
        x, y, w, a = 1560, 250, 300, -.55
        h = w * SH / SW
        grip_back(c, x, y, w, h, FR_SKIN, hx("#1C1A26"), ang=a, side="right", scale=1.3, stitches=True, sleeve_dir=(1, -.2))
        phone_frame(c, x, y, w, ang=a, back=True)
        grip_front(c, x, y, w, h, FR_SKIN, ang=a, side='right', scale=1.3)
        self.rphone = blur_surf(s, 4)
        self.P = (720, 540, 400)

    def draw(self, ctx, t, gt):
        ctx.set_source_surface(self.bg, 0, 0); ctx.paint()
        z = lerp(1.0, 1.05, ease(seg(t, 0, 3)))
        ctx.save(); cam(ctx, z, 820, 560, k=0)
        cx, cy, w = self.P
        glow(ctx, cx, cy, 700, hx('#7C5CFF'), .22)
        h = w * SH / SW
        ang = -.05 + math.sin(gt * 1.3) * .01
        grip_back(ctx, cx, cy, w, h, SKIN, HOODIE, ang=ang, side='left', sleeve_dir=(-.35, 1))
        phone_frame(ctx, cx, cy, w, ang=ang, screen=screen_pass, t=gt)
        grip_front(ctx, cx, cy, w, h, SKIN, ang=ang, side='left')
        # autofocus box over the QR
        if 1.1 < t < 2.1:
            a = .8 * (1 - seg(t, 1.6, 2.1))
            pulse = 1 + .08 * (1 - seg(t, 1.1, 1.35))
            ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang); ctx.translate(-cx, -cy)
            x0, y0, kx, ky = screen_to_world(cx, cy, w, 195, 444)
            bw = 300 * kx * pulse
            ctx.set_line_width(4); setc(ctx, hx('#FFD25A', a)); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            for (dx, dy) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
                px, py = x0 + dx * bw / 2, y0 + dy * bw / 2
                ctx.move_to(px, py - dy * 28); ctx.line_to(px, py); ctx.line_to(px - dx * 28, py); ctx.stroke()
            ctx.restore()
        ctx.restore()
        dx = lerp(300, 0, ease_out(seg(t, 0, .9))); dy = lerp(-240, 0, ease_out(seg(t, 0, .9)))
        ctx.save(); ctx.translate(dx + math.sin(gt * 2) * 4, dy + math.cos(gt * 1.7) * 3)
        ctx.set_source_surface(self.rphone, 0, 0); ctx.paint()
        ctx.restore()

class Scene5:
    def __init__(self, s3):
        def bg(c):
            c.translate(W, 0); c.scale(-1, 1)
            c.translate(W / 2, H / 2); c.scale(1.8, 1.8); c.translate(-400, -500)
            paint_cached(c, s3.room, s3.k)
        s, c = new_surface(); bg(c)
        setc(c, hx('#3A1A10', .25)); c.paint()
        self.bg = blur_surf(s, 20)
        # guest phone, far away + blurred (bottom-left)
        s, c = new_surface()
        gw = 230; gh = gw * SH / SW
        grip_back(c, 280, 870, gw, gh, SKIN, HOODIE, ang=.12, side='left', scale=.55, sleeve_dir=(-.3, 1))
        phone_frame(c, 280, 870, gw, ang=.12, screen=screen_pass)
        grip_front(c, 280, 870, gw, gh, SKIN, ang=.12, side='left', scale=.55)
        self.gphone = blur_surf(s, 9)
        self.P = (1010, 520, 420)   # receptionist phone centre + width

    def screen_xy(self, sx, sy):
        cx, cy, w = self.P
        h = w * SH / SW * 1.02
        pad = w * .035
        kx = (w - 2 * pad) / SW; ky = (h - 2 * pad) / SH
        return cx - w / 2 + pad + sx * kx, cy - h / 2 + pad + sy * ky, kx

    def draw(self, ctx, t, gt):
        ctx.set_source_surface(self.bg, 0, 0); ctx.paint()
        ctx.save(); ctx.translate(math.sin(gt * 1.1) * 6, math.cos(gt * .9) * 4)
        ctx.set_source_surface(self.gphone, 0, 0); ctx.paint(); ctx.restore()
        cx, cy, w = self.P
        h = w * SH / SW
        lock = 1.45
        conf = ease(seg(t, lock, lock + .25))
        land = lock + 1.05
        landed = t >= land
        grip_back(ctx, cx, cy, w, h, FR_SKIN, hx('#1C1A26'), side='right', scale=1.0, stitches=True, sleeve_dir=(.3, 1))
        phone_frame(ctx, cx, cy, w, screen=lambda c, tt: screen_scanner(c, tt, confirmed=conf, show_check=landed, label=ease(seg(t, land, land + .35)),
                                                                           check_k=1 + .12 * math.sin(clamp((t - land) / .35) * math.pi) * (1 - clamp((t - land) / .35))), t=gt)
        grip_front(ctx, cx, cy, w, h, FR_SKIN, side='right', scale=1.0)
        # capture flash
        fl = seg(t, lock, lock + .08) * (1 - seg(t, lock + .08, lock + .3))
        if fl > 0:
            vx, vy, k = self.screen_xy(20, 290)
            ctx.save(); rrect(ctx, vx, vy, 350 * k, 330 * k, 20 * k); setc(ctx, (1, 1, 1, .8 * fl)); ctx.fill(); ctx.restore()
        # the checkmark pops OUT of the phone and lands
        if lock + .1 < t < land:
            p = seg(t, lock + .1, land)
            sx0, sy0, k = self.screen_xy(195, 470)
            sx1, sy1, _ = self.screen_xy(195, 690)
            r_land = 48 * k
            up = math.sin(p * math.pi)            # height above the screen
            pos_p = ease(p)
            x = lerp(sx0, sx1, pos_p); y = lerp(sy0, sy1, pos_p) - up * 160
            r = r_land * (lerp(.2, 1, ease_out(seg(p, 0, .25))) + up * 1.5)
            draw_check(ctx, x, y, r, a=clamp(p * 6), shadow=up, shadow_dy=40 + up * 150)
        # landing sparkle
        if landed and t < land + .5:
            q = seg(t, land, land + .5)
            sx1, sy1, k = self.screen_xy(195, 690)
            for i in range(10):
                a = i / 10 * math.tau
                rr = 60 * k + q * 130 * k
                circle(ctx, sx1 + math.cos(a) * rr, sy1 + math.sin(a) * rr, 7 * k * (1 - q)); setc(ctx, LIME1, 1 - q); ctx.fill()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 6 — the party
# ════════════════════════════════════════════════════════════════════════════
PD = (900, 250, 480, 610)   # party doorway hole
DANCERS = None

def bunting(c, x0, y0, x1, y1, sag, n, colors):
    for i in range(n):
        a = (i + .5) / n
        x = lerp(x0, x1, a); y = lerp(y0, y1, a) + sag * math.sin(a * math.pi)
        c.move_to(x - 26, y); c.line_to(x + 26, y); c.line_to(x, y + 46); c.close_path()
        setc(c, colors[i % len(colors)]); c.fill()
    c.set_line_width(3); setc(c, hx('#111')); c.move_to(x0, y0)
    c.curve_to(lerp(x0, x1, .33), y0 + sag * 1.3, lerp(x0, x1, .66), y1 + sag * 1.3, x1, y1); c.stroke()

def paper_bat(c, x, y, s):
    c.save(); c.translate(x, y); c.scale(s, s)
    c.move_to(0, 0); c.curve_to(-14, -10, -30, -6, -40, -12); c.curve_to(-30, 2, -22, 4, -14, 2); c.curve_to(-8, 6, -4, 4, 0, 8)
    c.curve_to(4, 4, 8, 6, 14, 2); c.curve_to(22, 4, 30, 2, 40, -12); c.curve_to(30, -6, 14, -10, 0, 0)
    setc(c, hx('#0A0A10')); c.fill(); c.restore()

def s6_wall(c):
    c.set_source(lingrad(0, 0, 0, 860, [(0, hx('#2A1428')), (1, hx('#4A2440'))])); c.rectangle(0, 0, W, 860); c.fill()
    for row in range(14):
        for col in range(22):
            x = col * 92 + (46 if row % 2 else 0); y = row * 64
            c.save(); c.translate(x, y); ellipse(c, 0, 0, 14, 22); setc(c, hx('#E0A8D8', .07)); c.fill(); c.restore()
    glow(c, 1140, 520, 900, hx('#FF9A3A'), .3)
    c.rectangle(0, 640, W, 220); setc(c, hx('#24131F')); c.fill()
    for x in range(20, W, 180):
        rrect(c, x, 670, 150, 160, 6); c.set_line_width(4); setc(c, hx('#3A2030')); c.stroke()
    line(c, 0, 642, W, 642, 10, hx('#3A2030'))
    c.set_source(lingrad(0, 860, 0, H, [(0, hx('#3A2418')), (1, hx('#1A0F0A'))])); c.rectangle(0, 860, W, 220); c.fill()
    for i in range(-16, 17):
        line(c, 1140 + i * 80, 860, 1140 + i * 190, H, 2, hx('#000', .35))
    # light spill on the floor from the party
    c.move_to(PD[0], 860); c.line_to(PD[0] + PD[2], 860); c.line_to(PD[0] + PD[2] + 260, H); c.line_to(PD[0] - 260, H); c.close_path()
    c.set_source(lingrad(0, 860, 0, H, [(0, hx('#FFB86B', .35)), (1, hx('#FFB86B', 0))])); c.fill()
    # hole
    c.save(); c.set_operator(cairo.OPERATOR_CLEAR); c.rectangle(*PD); c.fill(); c.restore()
    x, y, w, h = PD
    # open door leaves (swung in) + frame
    for sgn, ex in ((-1, x), (1, x + w)):
        c.move_to(ex, y); c.line_to(ex - sgn * 42, y + 30); c.line_to(ex - sgn * 42, y + h - 10); c.line_to(ex, y + h); c.close_path()
        setc(c, hx('#4A2222')); c.fill()
        for k in range(3):
            yy = y + 60 + k * 180
            c.move_to(ex - sgn * 8, yy + 4); c.line_to(ex - sgn * 34, yy + 14); c.line_to(ex - sgn * 34, yy + 140); c.line_to(ex - sgn * 8, yy + 150); c.close_path()
            setc(c, hx('#5E2E2E')); c.fill()
    c.set_line_width(36); setc(c, hx('#8C806E')); c.rectangle(x - 18, y - 18, w + 36, h + 18); c.stroke()
    c.rectangle(x - 60, y - 70, w + 120, 40); setc(c, hx('#A39784')); c.fill()
    cobweb(c, x - 36, y - 36, 120, 1, 1, .45); cobweb(c, x + w + 36, y - 36, 120, -1, 1, .45)
    # decorations
    bunting(c, 0, 90, x - 40, 150, 70, 9, [hx('#F28A1C'), hx('#1A1620'), hx('#7C5CFF')])
    bunting(c, x + w + 40, 150, W, 90, 70, 9, [hx('#7C5CFF'), hx('#F28A1C'), hx('#1A1620')])
    for (bx, by, bs) in ((260, 330, 1.3), (420, 280, 1.0), (1600, 300, 1.2), (1760, 380, .9), (1500, 420, .8)):
        paper_bat(c, bx, by, bs)
    # balloons
    for (bx, by, col) in ((140, 520, '#F28A1C'), (205, 480, '#7C5CFF'), (95, 470, '#1A1620'), (1780, 520, '#F28A1C'), (1840, 470, '#7C5CFF')):
        line(c, bx, by + 50, bx + 10, 860, 2, hx('#DDD', .6))
        ellipse(c, bx, by, 40, 50); setc(c, hx(col)); c.fill()
        ellipse(c, bx - 12, by - 18, 9, 14); setc(c, (1, 1, 1, .3)); c.fill()
    # sign over the door
    rrect(c, x + w / 2 - 230, y - 150, 460, 70, 12); setc(c, hx('#1A1020')); c.fill_preserve(); c.set_line_width(4); setc(c, hx('#F28A1C')); c.stroke()
    font(c, 'Irish Grover', 48); setc(c, hx('#F28A1C')); tw = text_w(c, 'Masquerade Ball'); c.move_to(x + w / 2 - tw / 2, y - 98); c.show_text('Masquerade Ball')

class Scene6:
    def __init__(self):
        self.k = 1.25
        self.wall = make_cache(s6_wall, self.k)
        mummy = dict(GUEST, skin=hx('#D8CFB8'), top=hx('#D8CFB8'), pants=hx('#D8CFB8'), shoe=hx('#B8AE96'), hair_style='none', costume='mummy')
        pump = dict(GUEST, top=hx('#2F6B3A'), pants=hx('#222'), shoe=hx('#111'), costume='pumpkin', hair_style='none')
        skel = dict(GUEST, top=hx('#15151C'), pants=hx('#15151C'), shoe=hx('#111'), costume='skeleton', hair_style='none')
        cat = dict(GUEST, top=hx('#1A1620'), pants=hx('#1A1620'), hair=hx('#1A1620'), costume='cat', skin=hx('#C68E6B'))
        # (x, feet y, scale, look, phase, layer) layer 0 behind guest, 1 in front
        self.dancers = [
            (990, 760, .42, VAMP, 0.0, 0), (1090, 770, .44, mummy, 1.3, 0), (1285, 765, .43, WITCH, 2.2, 0),
            (1205, 800, .5, pump, 0.7, 0), (1330, 815, .48, cat, 3.1, 0),
            (1000, 850, .56, skel, 1.9, 1), (1255, 862, .54, 'ghost', 2.6, 1)]

    def guest(self, t):
        # returns x, feet y, scale, facing, walk, expr, inside, dance
        if t < 1.8:
            return 560, 1016, 2.0, 'front', None, 'huge', False, None
        if t < 3.4:
            p = ease(seg(t, 1.8, 3.4))
            return lerp(560, 1135, p), lerp(1016, 820, p), lerp(2.0, .5, p ** .8), 'back', t * 9, 'huge', p > .93, None
        return 1135, 820, .5, 'front', None, 'huge', True, (t - 3.4) * 7.5

    def party(self, ctx, t, gt, guest_inside_fn=None):
        x, y, w, h = PD
        ctx.save(); ctx.rectangle(x - 80, y, w + 160, h); ctx.clip()
        ctx.set_source(lingrad(0, y, 0, y + h, [(0, hx('#3A1650')), (.6, hx('#7A2A3A')), (1, hx('#C2622A'))])); ctx.paint()
        # roving coloured spots
        for i, col in enumerate(('#7C5CFF', '#22D3EE', '#F28A1C', '#C3EF52')):
            sx = x + w / 2 + math.sin(gt * (1.3 + i * .4) + i * 2) * w * .45
            sy = y + h * .45 + math.cos(gt * (1.1 + i * .3) + i) * h * .3
            glow(ctx, sx, sy, 170, hx(col), .45)
        # string lights
        for row in range(3):
            yy = y + 40 + row * 50
            for k in range(14):
                lx = x - 20 + k * (w + 40) / 13
                ly = yy + 30 * math.sin(k / 13 * math.pi)
                on = .6 + .4 * math.sin(gt * 6 + k * 1.3 + row)
                col = ('#FFD25A', '#F28A1C', '#A78BFA')[(k + row) % 3]
                glow(ctx, lx, ly, 20, hx(col), on * .8); circle(ctx, lx, ly, 4.5); setc(ctx, hx(col)); ctx.fill()
        # floor
        ctx.rectangle(x - 80, y + h - 140, w + 160, 140); ctx.set_source(lingrad(0, y + h - 140, 0, y + h, [(0, hx('#2A1420')), (1, hx('#4A2418'))])); ctx.fill()
        # table of jack-o-lanterns at the back
        rrect(ctx, x + 20, y + h - 210, 160, 16, 4); setc(ctx, hx('#2A1810')); ctx.fill()
        jack(ctx, x + 60, y + h - 210, .45, gt, 3); jack(ctx, x + 130, y + h - 210, .38, gt, 4)
        beat = gt * 2 * math.pi * (124 / 60) / 2
        for (dx_, dy_, s, L, ph, layer) in self.dancers:
            if layer: continue
            self.dancer(ctx, dx_, dy_, s, L, beat + ph, gt)
        if guest_inside_fn: guest_inside_fn()
        for (dx_, dy_, s, L, ph, layer) in self.dancers:
            if not layer: continue
            self.dancer(ctx, dx_, dy_, s, L, beat + ph, gt)
        ctx.restore()

    def dancer(self, ctx, x, y, s, L, ph, gt):
        if L == 'ghost':
            sheet_ghost(ctx, x, y - 70 * s - abs(math.sin(ph)) * 20, s * 1.6, gt, seed=4, arms_up=.5 + .5 * math.sin(ph))
            return
        person(ctx, x + math.sin(ph * .5) * 10, y, s, 'front', expr='smile', t=gt, look_cfg=L, dance=ph)

    def draw(self, ctx, t, gt):
        px, py, ps, facing, walk, expr, inside, dance = self.guest(t)
        def gin():
            if inside:
                person(ctx, px, py, ps, facing, walk=walk, expr=expr, t=gt, dance=dance * 1.0 if dance is not None else None)
        ctx.save()
        z = lerp(1.0, 1.04, ease(seg(t, 0, 4.5)))
        cam(ctx, z, 960, 540, k=0)
        self.party(ctx, t, gt, gin)
        paint_cached(ctx, self.wall, self.k)
        if not inside:
            if facing == 'front':
                jump = abs(math.sin(seg(t, .2, 1.0) * math.pi * 2)) * 18 * (1 - seg(t, .2, 1.0))
                pocket = ease(seg(t, 1.2, 1.7))
                ar = (lerp(30, 8, pocket), lerp(150, 10, pocket))
                person(ctx, px, py - jump, ps, 'front', expr='huge', t=gt, phone='r' if pocket < .8 else None,
                       arms={'r': ar, 'l': (-14, -8)})
            else:
                person(ctx, px, py, ps, 'back', walk=walk, t=gt)
        ctx.restore()
        g = cairo.RadialGradient(W / 2, H / 2, H * .45, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .45); ctx.set_source(g); ctx.paint()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 7 — logo build + tagline
# ════════════════════════════════════════════════════════════════════════════
TAGLINE = "Check-ins so easy, it's scary."

def draw_end(ctx, gt):
    t = gt - T7
    LX, LY, LS = 960, 385, 470
    # soft brand glow
    ga = ease(seg(t, 2.4, 3.2))
    if ga > 0:
        glow(ctx, LX, LY, 420, ACC, .28 * ga); glow(ctx, LX, LY, 300, ACC2, .12 * ga)
    # dark disc so the house (negative space) reads
    da = ease(seg(t, .3, 1.0))
    circle(ctx, LX, LY, LS * .44); setc(ctx, BG, .85 * da); ctx.fill()
    # arc from the left
    p = seg(t, .4, 1.1)
    if p > 0:
        e = ease_out_back(p, 1.2)
        draw_logo_piece(ctx, 'arc', LX, LY, LS, dx=lerp(-900, 0, e), rot=lerp(-1.4, 0, e), a=clamp(p * 3))
    p = seg(t, .6, 1.3)
    if p > 0:
        e = ease_out_back(p, 1.2)
        draw_logo_piece(ctx, 'moon', LX, LY, LS, dx=lerp(900, 0, e), dy=lerp(-200, 0, e), rot=lerp(1.2, 0, e), sc=lerp(1.5, 1, e), a=clamp(p * 3))
    for i in range(4):
        p = seg(t, 1.2 + i * .1, 1.5 + i * .1)
        if p > 0:
            draw_logo_piece(ctx, f'win{i}', LX, LY, LS, sc=ease_out_back(p, 2.6), pivot=(.565, .615), a=clamp(p * 4))
    # the ghost floats in last — loose, wavy, not stiff
    p = seg(t, 1.6, 2.75)
    if p > 0:
        e = ease_out(p)
        wob = (1 - e)
        dx = lerp(-820, 0, e) + math.sin(p * 9) * 40 * wob
        dy = lerp(320, 0, e) + math.sin(p * 7 + 1) * 90 * wob - math.sin(p * math.pi) * 60
        rot = math.sin(p * 8) * .28 * wob
        sq = math.sin(p * 11) * .07 * wob
        draw_logo_piece(ctx, 'ghost', LX, LY, LS, dx=dx, dy=dy, rot=rot, sx=1 + sq, sy=1 - sq, a=clamp(p * 4), pivot=(.5, .4))
    if t > 2.75:
        # settle bob
        q = seg(t, 2.75, 3.2)
        dy = math.sin(q * math.pi) * -6
        draw_logo_piece(ctx, 'ghost', LX, LY, LS, dy=dy * (1 - q))
    # tagline letters
    size = 96
    font(ctx, 'Irish Grover', size)
    total = text_w(ctx, TAGLINE)
    x = W / 2 - total / 2; y = 770
    for i, ch in enumerate(TAGLINE):
        cw = text_w(ctx, ch)
        p = seg(t, 2.6 + i * .028, 2.6 + i * .028 + .35)
        if p > 0:
            e = ease_out_back(p, 2.2)
            ctx.save(); ctx.translate(x + cw / 2, y - size * .3 + (1 - e) * 40); ctx.scale(lerp(.3, 1, e), lerp(.3, 1, e))
            ctx.rotate((1 - e) * .5 * (1 if i % 2 else -1))
            setc(ctx, INK, clamp(p * 3)); ctx.move_to(-cw / 2, size * .3); font(ctx, 'Irish Grover', size); ctx.show_text(ch)
            ctx.restore()
        x += cw
    # subline + wordmark
    p = ease(seg(t, 3.55, 4.0))
    if p > 0:
        ctx.save(); ctx.translate(0, (1 - p) * 20)
        s1 = 'Plan your next event at '
        font(ctx, 'Public Sans', 40, True); w1 = text_w(ctx, s1)
        font(ctx, 'Irish Grover', 52); w2 = text_w(ctx, 'IsHaunted.com')
        x0 = W / 2 - (w1 + w2) / 2
        text(ctx, s1, x0, 875, MUTED, size=40, bold=True, a=p)
        wordmark(ctx, x0 + w1, 878, 52, a=p)
        ctx.restore()
