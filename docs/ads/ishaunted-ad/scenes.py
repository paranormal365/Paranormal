import math, random
import cairo
from PIL import Image, ImageFilter
from common import *
from common import _limb
from figure import figure, POSES, OUTFITS, P, walk, conga, blend, weapon, scroll
from dragon import dragon

# ── global timeline (seconds) ───────────────────────────────────────────────
T1, T2, T3, T4, T5, T6, T7, TEND = 0.0, 5.5, 10.0, 14.0, 17.0, 21.0, 25.5, 30.0
T2B = 7.8   # inside the foyer: the window, and only now the ghosts

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

    # ── the dragon's flight, and the hero's leap ──────────────────────────────
    DS = .72                      # dragon scale
    LAND = (1000, 985)            # where the hero lands, in front of the walkway
    HS = .62                      # the hero's scale on the ground

    def dragon_state(self, t):
        if t < 1.7:
            p = ease_out(seg(t, 0, 1.7))
            x = lerp(-520, 820, p); y = lerp(150, 470, p) + math.sin(p * math.pi) * -40
            return x, y, lerp(14, -4, p), 0.0
        if t < 2.62:
            return 820 + math.sin(t * 2) * 8, 470 + math.sin(t * 4.5) * 12, -4, 0.0
        p = ease_in(seg(t, 2.62, 4.2))
        return lerp(820, 2500, p), lerp(470, -320, p), lerp(-4, -24, seg(t, 2.62, 3.2)), ease(seg(t, 2.62, 2.8)) * (1 - ease(seg(t, 3.1, 3.4)))

    def seat(self, t):
        x, y, h, _ = self.dragon_state(min(t, 2.62))
        sx, sy = 30, -52 + 150 * .89 - 150 * .89
        r = math.radians(h)
        return x + self.DS * (math.cos(r) * sx - math.sin(r) * sy), y + self.DS * (math.sin(r) * sx + math.cos(r) * sy)

    def leap_p(self, t):
        if t < 2.85: return .45 * ease_out(seg(t, 2.62, 2.85))
        if t < 3.3: return lerp(.45, .55, seg(t, 2.85, 3.3))          # the slow-motion hang
        return lerp(.55, 1.0, ease_in(seg(t, 3.3, 3.45)))

    def hero_state(self, t):
        """x, y (feet), scale, pose, view, expr, mode"""
        if t < 2.62:
            return None
        if t < 3.45:
            sx, sy = self.seat(2.62)
            p = self.leap_p(t)
            x = lerp(sx, self.LAND[0], p); y = lerp(sy + 40, self.LAND[1], p) - 330 * 4 * p * (1 - p)
            mode = 'hero_air' if 2.85 <= t < 3.3 else 'leap'
            return x, y, self.HS, None, 'front' if mode == 'hero_air' else 'side', 'hero', mode
        if t < 4.9:
            k = ease(seg(t, 3.9, 4.35))
            pose = blend(POSES['landing'], POSES['guard'], k)
            return self.LAND[0], self.LAND[1], self.HS, pose, 'front', 'hero', 'ground'
        p = ease_in(seg(t, 4.9, 5.5)) * .7 + seg(t, 4.9, 5.5) * .3
        return lerp(self.LAND[0], 1140, p), lerp(self.LAND[1], 862, p), lerp(self.HS, .42, p), walk(t * 20, run=True, lean=8), 'side', 'hero', 'dash'

    def camera(self, t):
        keys = [(0, 1.0, 960, 560), (1.55, 1.0, 960, 560), (1.95, 2.8, 0, 0), (2.55, 2.9, 0, 0),
                (2.9, 2.4, 0, 0), (3.3, 2.3, 0, 0), (3.5, 1.85, 0, 0), (4.6, 1.45, 0, 0), (5.2, 1.0, 960, 560), (5.5, 1.0, 960, 560)]
        # focus on whatever matters at that moment
        def focus(tt):
            if tt < 2.62:
                sx, sy = self.seat(tt); return sx, sy - 40
            hs = self.hero_state(tt)
            return hs[0], hs[1] - 150 * hs[2]
        for i in range(len(keys) - 1):
            t0, z0, _, _ = keys[i]; t1, z1, _, _ = keys[i + 1]
            if t0 <= t <= t1:
                q = ease(seg(t, t0, t1))
                z = lerp(z0, z1, q)
                fx, fy = focus(t)
                w = clamp((z - 1) / 1.0)
                return z, lerp(960, fx, w), lerp(560, fy, w), w
        return 1.0, 960, 560, 0

    def draw(self, ctx, t, gt):
        z, fx, fy, w = self.camera(t)
        shake = 0.0
        if 3.45 <= t < 3.7: shake = (1 - seg(t, 3.45, 3.7)) * 14
        if 2.85 <= t < 2.95: shake = (1 - seg(t, 2.85, 2.95)) * 10
        ctx.save()
        ctx.translate(math.sin(gt * 97) * shake, math.cos(gt * 83) * shake * .6)
        cam(ctx, z, fx, fy, k=w * .97)
        paint_cached(ctx, self.bg, self.k)
        for i, (x, y, ww, hh) in enumerate(S1_LIT):
            a = .55 + .25 * noise1(gt * 2.2, i * 3.1) + (.25 if i == 0 else 0)
            window(ctx, x, y, ww, hh, clamp(a), arch=True)
            glow(ctx, x + ww / 2, y + hh / 2, 90, hx('#FFC266'), .18 * a)
        ga = ease(seg(t, .6, 1.2)) * (1 - ease(seg(t, 3.0, 3.5)))
        if ga > 0:
            ctx.save(); circle(ctx, 1140, 448, 31); ctx.clip()
            glow(ctx, 1140, 448, 60, hx('#B8F5C8'), .5 * ga)
            sheet_ghost(ctx, 1140 + math.sin(gt * 2) * 4, 470, .45, gt, alpha=ga * .9, expr='o')
            ctx.restore()
        for (ph, y0, s_) in self.bats:
            bx = ((t * .22 + ph) % 1.4) * 2200 - 200
            by = y0 + math.sin(t * 2 + ph * 9) * 30
            flap = math.sin(gt * 22 + ph * 5)
            ctx.save(); ctx.translate(bx, by); ctx.scale(s_, s_)
            ctx.move_to(0, 0); ctx.curve_to(-14, -10 - flap * 12, -30, -6 - flap * 16, -40, -2 - flap * 18)
            ctx.curve_to(-30, 2, -22, 4, -14, 2); ctx.curve_to(-8, 6, -4, 4, 0, 8)
            ctx.curve_to(4, 4, 8, 6, 14, 2); ctx.curve_to(22, 4, 30, 2, 40, -2 - flap * 18)
            ctx.curve_to(30, -6 - flap * 16, 14, -10 - flap * 12, 0, 0); setc(ctx, hx('#05060A')); ctx.fill(); ctx.restore()
        self.fog(ctx, self.fogA, 560, gt * 26, .85)

        N = OUTFITS['ninja']
        # the dragon, with the hero aboard until he leaps
        dx, dy, dh, roar = self.dragon_state(t)
        if dx < 2700:
            flap = gt * (9 if t < 1.7 else (6 if t < 2.62 else 11))
            if t < 1.7:
                motion_lines(ctx, dx - 760, dy - 140, dx - 260, dy + 80, 14, gt, (1, 1, 1, 1), .35)
            def rider(c, sx, sy):
                if t >= 2.62: return
                if t < 1.95:
                    figure(c, sx, sy + 150 * .89, .89, P(lt=-80, lk=10, rt=-70, rk=20, la=60, lf=80, ra=70, rf=90, lean=14, air=True),
                           N, gt, 'side', 'neutral', look=(1, .3), wind=2)
                else:
                    sc = t > 2.05 and t < 2.48
                    pose = P(lt=-70, lk=-10, rt=70, rk=10, la=-30, lf=-160, ra=30, rf=160, lean=-4, air=True) if sc else \
                           P(lt=-70, lk=-10, rt=70, rk=10, la=-60, lf=-30, ra=60, rf=30, air=True, tilt=-6)
                    figure(c, sx + math.sin(gt * 70) * (2 if sc else 0), sy + 150 * .89, .89, pose, N, gt, 'front',
                           'fear' if sc else 'hero', wind=1.5)
            dragon(ctx, dx, dy, self.DS, gt, flap, heading=dh, roar=roar, rider=rider)
        # the hero after the leap
        hs = self.hero_state(t)
        if hs and hs[6] != 'hero_air':
            hx_, hy_, hsc, pose, view, expr, mode = hs
            if mode == 'leap':
                motion_lines(ctx, hx_ - 220, hy_ - 200 * hsc, hx_ - 30, hy_ - 20, 10, gt, hx('#C7B8FF'), .6)
                figure(ctx, hx_, hy_, hsc, dict(POSES['leap'], air=True), N, gt, 'side', expr, wind=2.4)
            elif mode == 'ground':
                land = seg(t, 3.45, 4.1)
                if land < 1:
                    # the shockwave and dust where he hit
                    for k in range(3):
                        rr = (land * 260 + k * 30) * hsc * 1.6
                        ellipse(ctx, hx_, hy_ + 4, rr, rr * .22); ctx.set_line_width(6 * (1 - land)); setc(ctx, (1, 1, 1, .5 * (1 - land))); ctx.stroke()
                    for k in range(10):
                        ang = k / 10 * math.pi
                        d = land * 180 * hsc * 1.6
                        circle(ctx, hx_ + math.cos(ang) * d * 1.6, hy_ - math.sin(ang) * d * .35, 18 * (1 - land) + 4); setc(ctx, hx('#9AA0AE', .5 * (1 - land))); ctx.fill()
                figure(ctx, hx_, hy_, hsc, pose, N, gt, view, expr, wind=1.6, glint=math.sin(clamp(seg(t, 4.35, 4.65)) * math.pi))
            elif mode == 'dash':
                motion_lines(ctx, hx_ - 320 * hsc * 2, hy_ - 300 * hsc, hx_ - 40 * hsc, hy_ - 30 * hsc, 14, gt, hx('#C7B8FF'), .7, -1)
                for k, aa in ((3, .16), (2, .26), (1, .4)):
                    figure(ctx, hx_ - k * 40 * hsc * 2, hy_ + k * 6, hsc, pose, N, gt, view, expr, wind=2.4, alpha=aa)
                figure(ctx, hx_, hy_, hsc, pose, N, gt, view, expr, wind=2.4)
        self.fog(ctx, self.fogB, 770, -gt * 40, .75)
        # the hero moment, mid-air: impact frame, slow motion, blade drawn
        ia = ease(seg(t, 2.85, 2.88)) * (1 - ease(seg(t, 3.22, 3.3)))
        if hs and ia > 0:
            hx_, hy_, hsc = hs[0], hs[1], hs[2]
            impact_frame(ctx, hx_, hy_ - 150 * hsc, gt, ia)
            figure(ctx, hx_, hy_, hsc, dict(POSES['hero'], air=True), N, gt, 'front', 'hero', wind=2.6,
                   glint=math.sin(clamp(seg(t, 2.95, 3.2)) * math.pi))
        if 2.85 <= t < 2.9:
            setc(ctx, (1, 1, 1, .85 * (1 - seg(t, 2.85, 2.9)))); ctx.paint()
        ctx.restore()
        g = cairo.RadialGradient(W / 2, H / 2, H * .4, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .65); ctx.set_source(g); ctx.paint()

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
        """porch: walk up (0-1.25), wait while the bellhop opens up, walk in (1.5-2.2)"""
        if t < 1.25:
            p = ease_out(seg(t, 0, 1.25))
            return lerp(-220, 900, p), 995, 1.3, 'right', t * 9
        if t < 1.5:
            return 900, 995, 1.3, 'back', None
        p = ease(seg(t, 1.5, 2.2))
        return lerp(900, 950, p), lerp(995, 880, p), lerp(1.3, 1.0, p), 'back', t * 9

    def draw(self, ctx, t, gt):
        ctx.save()
        z = lerp(1.0, 1.05, ease(seg(t, 0, 2.3)))
        cam(ctx, z, 960, 600, k=.0)
        # the dark window: just trees and the moon. Nothing in it yet.
        paint_cached(ctx, self.behind, self.k)
        x, y, w, h = WIN_R
        setc(ctx, hx('#1E3A6A', .18)); arch_path(ctx, x, y, w, h); ctx.fill()
        # candle window (left)
        x, y, w, h = WIN_L
        ctx.save(); arch_path(ctx, x, y, w, h); ctx.clip()
        ctx.set_source(lingrad(0, y, 0, y + h, [(0, hx('#3A1A12')), (1, hx('#5A2A16'))])); ctx.paint()
        glow(ctx, x + w / 2, y + h - 90, 260, hx('#FF9C3A'), .45 + .1 * noise1(gt * 6, 1))
        for sx in (x, x + w):
            ctx.move_to(sx, y); ctx.curve_to(sx + (60 if sx == x else -60), y + h * .4, sx + (20 if sx == x else -20), y + h * .7, sx, y + h)
            ctx.line_to(sx, y); setc(ctx, hx('#6A1420')); ctx.fill()
        cx_ = x + w / 2; base = y + h - 30
        rrect(ctx, cx_ - 30, base - 10, 60, 14, 4); setc(ctx, hx('#B88A3A')); ctx.fill()
        line(ctx, cx_, base - 10, cx_, base - 90, 8, hx('#B88A3A'))
        ctx.set_line_width(6); setc(ctx, hx('#B88A3A')); ctx.arc(cx_, base - 120, 55, .15 * math.pi, .85 * math.pi); ctx.stroke()
        for i, dx_ in enumerate((-55, 0, 55)):
            candle(ctx, cx_ + dx_, base - (96 if dx_ == 0 else 80), 54 if dx_ == 0 else 44, gt, i * 2.7)
        ctx.restore()
        setc(ctx, (1, 1, 1, .05)); arch_path(ctx, x, y, w, h); ctx.fill()
        # the doorway: warm light inside, the bellhop opening up
        dx, dy, dw, dh = DOOR
        op = ease(seg(t, 1.0, 1.45))
        ctx.save()
        ctx.rectangle(dx, dy, dw, dh); ctx.move_to(dx, dy); ctx.arc(dx + dw / 2, dy, dw / 2, math.pi, 0); ctx.close_path(); ctx.clip()
        ctx.set_source(lingrad(0, dy - 130, 0, dy + dh, [(0, hx('#FFE9B8')), (1, hx('#E7A055'))])); ctx.paint()
        glow(ctx, dx + dw / 2, dy + 120, 260, hx('#FFFFFF'), .6)
        ctx.set_line_width(5); setc(ctx, hx('#6E6458'))
        for k in range(7):
            a = math.pi + k / 6 * math.pi
            ctx.move_to(dx + dw / 2, dy); ctx.line_to(dx + dw / 2 + math.cos(a) * dw, dy + math.sin(a) * dw); ctx.stroke()
        line(ctx, dx, dy, dx + dw, dy, 8, hx('#6E6458'))
        if op > 0:
            # the unicorn bellhop, a sweep of the arm: this way
            sweep = ease_out_back(seg(t, 1.2, 1.55), 1.6)
            ba = clamp(op * 1.4)
            figure(ctx, dx + dw - 76, dy + dh - 2, .92, blend(POSES['attention'], POSES['flourish'], sweep), OUTFITS['unicorn'], gt,
                   'front', 'smile', look=(-1, .2), alpha=ba, glint=math.sin(clamp(seg(t, 1.45, 1.75)) * math.pi))
            for i in range(6):
                q = (gt * .7 + i / 6) % 1
                sparkle(ctx, dx + dw - 150 + math.sin(i * 2.3) * 70, dy + dh - 260 - q * 160, 9 * (1 - q) + 3, hx('#FFF6C8'), (1 - q) * sweep)
        px, py, ps, facing, wph = self.guest(t)
        inside = t > 1.85
        if inside:
            a = 1 - ease(seg(t, 2.0, 2.3))
            figure(ctx, px, py, ps * .96, walk(wph or 0), OUTFITS['ninja'], gt, 'back', alpha=a)
        # door panel swinging inward (hinge on the left)
        dwv = dw * math.cos(op * math.pi * .46)
        persp = op * 26
        ctx.move_to(dx, dy); ctx.line_to(dx + dwv, dy + persp); ctx.line_to(dx + dwv, dy + dh - persp * .4); ctx.line_to(dx, dy + dh); ctx.close_path()
        ctx.set_source(lingrad(dx, 0, dx + dw, 0, [(0, hx('#5A2626')), (1, shade(hx('#5A2626'), lerp(1, .55, op)))])); ctx.fill()
        ctx.save(); ctx.translate(dx, dy); ctx.scale(max(dwv, 1) / dw, 1)
        for (px_, py_, pw_, ph_) in ((28, 40, 88, 140), (144, 40, 88, 140), (28, 220, 88, 140), (144, 220, 88, 140), (28, 400, 88, 120), (144, 400, 88, 120)):
            rrect(ctx, px_, py_, pw_, ph_, 6); setc(ctx, hx('#3E1818')); ctx.fill()
            rrect(ctx, px_ + 8, py_ + 8, pw_ - 16, ph_ - 16, 4); setc(ctx, hx('#6A2E2E')); ctx.fill()
        circle(ctx, 230, 300, 11); setc(ctx, hx('#D9B25A')); ctx.fill()
        ctx.set_line_width(6); setc(ctx, hx('#D9B25A')); circle(ctx, 130, 200, 22); ctx.stroke()
        circle(ctx, 130, 176, 9); setc(ctx, hx('#D9B25A')); ctx.fill()
        ctx.restore()
        ctx.restore()
        paint_cached(ctx, self.wall, self.k)
        jack(ctx, 650, 935, .9, gt, 1); jack(ctx, 1300, 945, .75, gt, 2)
        if not inside:
            if facing == 'right':
                figure(ctx, px, py, ps * .96, walk(wph), OUTFITS['ninja'], gt, 'side', 'hero', look=(1, 0), wind=1.4)
            else:
                figure(ctx, px, py, ps * .96, POSES['guard'], OUTFITS['ninja'], gt, 'back', wind=1.2)
        bl = ease(seg(t, 1.9, 2.3))
        if bl > 0:
            glow(ctx, 960, 600, 300 + bl * 1500, hx('#FFF1D0'), bl)
        ctx.restore()
        g = cairo.RadialGradient(W / 2, H / 2, H * .45, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .55); ctx.set_source(g); ctx.paint()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 2b — inside the foyer. Only now, looking out the window, the ghost kids.
# ════════════════════════════════════════════════════════════════════════════
FWIN = (700, 170, 520, 560)   # the big arched window, x y w h

def s2b_room(c):
    c.set_source(lingrad(0, 0, 0, 880, [(0, hx('#24141F')), (1, hx('#3A2030'))])); c.rectangle(0, 0, W, 880); c.fill()
    # panelling
    for x in range(0, W, 160):
        rrect(c, x + 14, 520, 132, 330, 6); c.set_line_width(4); setc(c, hx('#4A2A38')); c.stroke()
    line(c, 0, 500, W, 500, 10, hx('#4A2A38'))
    for x in (380, 1540):
        glow(c, x, 360, 520, hx('#FFC777'), .32)
    # floor
    c.set_source(lingrad(0, 880, 0, H, [(0, hx('#3A2418')), (1, hx('#1A0F0A'))])); c.rectangle(0, 880, W, 200); c.fill()
    for i in range(-16, 17):
        line(c, 960 + i * 80, 880, 960 + i * 190, H, 2, hx('#000', .35))
    c.move_to(-50, H); c.line_to(600, 900); c.line_to(1320, 900); c.line_to(1970, H); c.close_path(); setc(c, hx('#5A1626', .55)); c.fill()
    # cut the window
    x, y, w, h = FWIN
    c.save(); c.set_operator(cairo.OPERATOR_CLEAR); arch_path(c, x, y, w, h); c.fill(); c.restore()
    arch_path(c, x - 26, y - 26, w + 52, h + 52); arch_path(c, x, y, w, h)
    c.set_fill_rule(cairo.FILL_RULE_EVEN_ODD); setc(c, hx('#5E4A40')); c.fill(); c.set_fill_rule(cairo.FILL_RULE_WINDING)
    c.rectangle(x - 50, y + h + 18, w + 100, 26); setc(c, hx('#6E584A')); c.fill()
    for k in (1, 2):
        line(c, x + w * k / 3, y + 20, x + w * k / 3, y + h, 6, hx('#5E4A40'))
    line(c, x, y + h * .45, x + w, y + h * .45, 6, hx('#5E4A40'))
    # heavy curtains, tied back
    for sx, sg in ((x - 26, 1), (x + w + 26, -1)):
        c.move_to(sx - sg * 90, y - 60); c.line_to(sx + sg * 40, y - 60)
        c.curve_to(sx + sg * 10, y + h * .45, sx + sg * 70, y + h * .6, sx + sg * 30, y + h + 40)
        c.line_to(sx - sg * 90, y + h + 40); c.close_path(); setc(c, hx('#6A1420')); c.fill()
        rrect(c, sx + sg * 10 - 18, y + h * .55, 36, 12, 5); setc(c, hx('#C9A04A')); c.fill()
    # sconces
    for sx in (380, 1540):
        rrect(c, sx - 8, 330, 16, 70, 5); setc(c, hx('#2A2016')); c.fill()
        candle(c, sx, 330, 40, 0, sx)
    cobweb(c, 0, 0, 200, 1, 1, .35); cobweb(c, W, 0, 200, -1, 1, .35)
    # a coat stand and a little table with a lamp, for depth
    line(c, 200, 880, 200, 560, 10, hx('#2A1810')); line(c, 170, 590, 230, 590, 8, hx('#2A1810'))
    rrect(c, 1600, 760, 160, 18, 4); setc(c, hx('#2A1810')); c.fill(); line(c, 1620, 778, 1620, 880, 8, hx('#2A1810')); line(c, 1740, 778, 1740, 880, 8, hx('#2A1810'))

def s2b_outside(c):
    x, y, w, h = FWIN
    c.set_source(lingrad(0, y, 0, y + h, [(0, hx('#0A1030')), (1, hx('#1E2E52'))])); c.rectangle(x - 40, y - 40, w + 80, h + 80); c.fill()
    glow(c, x + w * .72, y + 110, 220, hx('#CFE0FF'), .5)
    circle(c, x + w * .72, y + 110, 40); setc(c, hx('#F6F1DA')); c.fill()
    rnd = random.Random(11)
    for i in range(9):
        pine(c, x - 30 + i * 70 + rnd.uniform(-14, 14), y + h - 90 - rnd.uniform(0, 50), rnd.uniform(220, 360), 60, hx('#0A1124'))
    c.rectangle(x - 40, y + h - 100, w + 80, 160); setc(c, hx('#0E1A2C')); c.fill()

class Scene2b:
    TREE = (FWIN[0] + FWIN[2] * .52, FWIN[1] + 300)
    def __init__(self):
        self.k = 1.25
        self.room = make_cache(s2b_room, self.k)
        self.out = make_cache(s2b_outside, self.k)

    def draw(self, ctx, t, gt):
        x, y, w, h = FWIN
        ctx.save()
        cam(ctx, lerp(1.0, 1.06, ease(seg(t, 0, 2.2))), 960, 520, k=0)
        paint_cached(ctx, self.out, self.k)
        # the ghost kids: they appear once he is inside and we can see out
        ga = ease(seg(t, .35, .8))
        ctx.save(); arch_path(ctx, x, y, w, h); ctx.clip()
        tx, ty = self.TREE
        for i in range(3):
            ph = gt * 1.7 + i * 2.1
            gx = tx + math.cos(ph) * 120
            gy = ty + math.sin(ph * 2) * 14 - abs(math.sin(gt * 4 + i)) * 22
            gs = .62 + .06 * math.sin(ph)
            if ga > .05:
                ctx.save(); ctx.push_group()
                weapon(ctx, 'toysword', gx + (54 if i % 2 else -54) * gs, gy - 4 * gs, (-40 if i % 2 else 220) + math.sin(gt * 9 + i) * 35, gt, s=gs * 1.4)
                ctx.pop_group_to_source(); ctx.paint_with_alpha(ga); ctx.restore()
            sheet_ghost(ctx, gx, gy, gs, gt, seed=i * 3, look=-math.sin(ph), arms_up=.6 + .4 * math.sin(gt * 5 + i), alpha=.92 * ga)
        # one peeks out from a trunk and waves at the camera
        pk = ease(seg(t, .9, 1.2))
        sheet_ghost(ctx, x + 70 + pk * 34, y + 260, .46, gt, seed=9, look=1, arms_up=.4 + .6 * abs(math.sin(gt * 9)) * pk, alpha=.9 * ga)
        glow(ctx, tx, ty, 260, hx('#B8F5C8'), .12 * ga)
        ctx.restore()
        setc(ctx, hx('#3A5A9A', .14)); arch_path(ctx, x, y, w, h); ctx.fill()
        # a streak of reflection on the glass
        ctx.save(); arch_path(ctx, x, y, w, h); ctx.clip()
        ctx.move_to(x + 40, y + h); ctx.line_to(x + 140, y); ctx.line_to(x + 190, y); ctx.line_to(x + 90, y + h); ctx.close_path(); setc(ctx, (1, 1, 1, .05)); ctx.fill()
        ctx.restore()
        paint_cached(ctx, self.room, self.k)
        # candle flames on the sconces
        for sx in (380, 1540):
            candle(ctx, sx, 330, 40, gt, sx)
        # the bellhop leads, the hero follows; he stops for a double take at the window
        stop = 1.0 <= t < 1.6
        bx = lerp(560, 1520, ease(seg(t, 0, 1.0)) * .45 + ease(seg(t, 1.55, 2.2)) * .55)
        hxp = lerp(180, 1180, ease(seg(t, 0, 1.0)) * .45 + ease(seg(t, 1.6, 2.2)) * .55)
        U = OUTFITS['unicorn']; Nj = OUTFITS['ninja']
        if stop:
            figure(ctx, bx, 990, 1.12, POSES['attention'], U, gt, 'side', 'smile', mirror=True)
        else:
            figure(ctx, bx, 990, 1.12, walk(gt * 8.5), U, gt, 'side', 'smile', wind=1.2)
        if stop:
            # startled, then the blade comes half out: guard
            k = ease_out_back(seg(t, 1.22, 1.38), 1.8)
            pose = blend(POSES['scared'], dict(POSES['guard'], weapon_ang=-30), k) if t >= 1.22 else POSES['scared']
            figure(ctx, hxp, 1000, 1.2, pose, Nj, gt, 'front', 'fear' if t < 1.25 else 'hero', look=(.4, -1), wind=1.6,
                   glint=math.sin(clamp(seg(t, 1.35, 1.6)) * math.pi))
            exclaim(ctx, hxp + 60, 1000 - 390 * 1.2, 1.1, ease_out_back(seg(t, 1.02, 1.15), 2.5) * (1 - seg(t, 1.35, 1.5)))
        else:
            figure(ctx, hxp, 1000, 1.2, walk(gt * 9), Nj, gt, 'side', 'hero', wind=1.4)
        ctx.restore()
        g = cairo.RadialGradient(W / 2, H / 2, H * .45, W / 2, H / 2, H * 1.05)
        g.add_color_stop_rgba(0, 0, 0, 0, 0); g.add_color_stop_rgba(1, 0, 0, 0, .55); ctx.set_source(g); ctx.paint()

# ════════════════════════════════════════════════════════════════════════════
# SCENE 3 — reception
# ════════════════════════════════════════════════════════════════════════════
DESK = (640, 620, 680, 280)
FR_SKIN = hx('#B9A9D8')   # the sorcerer's pale violet hands (long dark nails)
ROBE = hx('#3A1A58')
NINJA_GLOVE = hx('#2A2F48')

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
    """reception: the sorcerer's reveal, the couple leaves en garde, the hero presents his scroll"""
    HX, HY, HSC = 540, 990, 1.22
    def __init__(self):
        self.k = 1.25
        self.room = make_cache(s3_room, self.k)
        self.desk = make_cache(s3_desk, self.k)

    def hero(self, t):
        if t < 2.0:
            p = seg(t, .55, 2.0)
            return lerp(-220, self.HX, ease_out(p) * .5 + p * .5), walk(t * 9), p < 1
        k = ease_out_back(seg(t, 2.0, 2.35), 1.4)
        return self.HX, blend(POSES['stand'], POSES['present'], k), False

    def scroll_at(self, J):
        """centre of the scroll between the hands, in figure space"""
        a, b = J['lwr'], J['rwr']
        return (a[0] + b[0]) / 2 + 14, (a[1] + b[1]) / 2 - 20

    def scroll_world(self, t):
        x, pose, _ = self.hero(t)
        from figure import solve
        J = solve(pose, 'side')
        cx, cy = self.scroll_at(J)
        return x + cx * self.HSC, self.HY + cy * self.HSC

    def draw(self, ctx, t, gt):
        ctx.save()
        # camera: open tight on the villain, pull back, later push into the scroll
        rev = 1 - ease(seg(t, .55, 1.05))
        zk = ease(seg(t, 2.95, 4.0))
        fx, fy = self.scroll_world(t)
        if zk > 0:
            cam(ctx, lerp(1.0, 4.2, ease_in(zk)), lerp(960, fx, zk), lerp(540, fy, zk), k=zk)
        else:
            cam(ctx, 1.0 + rev * 1.6, lerp(960, 985, rev), lerp(540, 300, rev), k=rev * .95)
        paint_cached(ctx, self.room, self.k)
        hxp, hpose, walking = self.hero(t)
        lk = clamp((hxp - 340) / 600, -1, 1)
        for ex_ in (318, 362):
            ellipse(ctx, ex_, 322, 9, 6); setc(ctx, (1, 1, 1, .9)); ctx.fill()
            circle(ctx, ex_ + lk * 4, 323, 3.5); setc(ctx, OUTLINE); ctx.fill()
        cx_, cy_ = 960, 70
        line(ctx, cx_, 0, cx_, cy_, 4, hx('#2A2016'))
        ctx.set_line_width(6); setc(ctx, hx('#B88A3A')); ellipse(ctx, cx_, cy_ + 30, 160, 26); ctx.stroke()
        for i in range(7):
            candle(ctx, cx_ - 150 + i * 50, cy_ + 30 - (6 if i % 2 else 0), 30, gt, i * 1.9)
        # the villain reveal: dark focus lines and the orb flaring
        ra = ease(seg(t, 0, .08)) * (1 - ease(seg(t, .45, .7)))
        if ra > 0:
            g = cairo.RadialGradient(985, 330, 60, 985, 330, 1100)
            g.add_color_stop_rgba(0, .55, .9, .3, .5 * ra); g.add_color_stop_rgba(.25, .2, .05, .35, .85 * ra); g.add_color_stop_rgba(1, .02, .0, .05, .95 * ra)
            ctx.set_source(g); ctx.paint()
            radial_lines(ctx, 985, 330, 220, 1500, 80, gt, (0, 0, 0, 1), .7 * ra)
        So = OUTFITS['sorcerer']
        So = dict(So, weapon_hand='r')
        if t < .6:
            spose = dict(POSES['villain'], weapon_ang=-62)
        elif t < 2.2:
            spose = blend(dict(POSES['villain'], weapon_ang=-62), dict(POSES['villain_lean'], weapon_ang=-92, ra=12, rf=-6), ease(seg(t, .6, 1.0)))
        else:
            aim = P(la=-58, lf=-82, ra=12, rf=-6, lean=-8, tilt=-10, weapon_ang=-92)
            spose = blend(dict(POSES['villain_lean'], weapon_ang=-92, ra=12, rf=-6), aim, ease_out_back(seg(t, 2.2, 2.75), 1.3))
        slook = (1.0, .2) if t < 1.6 else (-1.0, .4)
        def phone_in_hand(c, J):
            if t < 2.25: return
            w = J['lwr']
            c.save(); c.translate(w[0] - 8, w[1] + 4); c.rotate(math.radians(-30))
            rrect(c, -16, -30, 32, 60, 7); setc(c, OUTLINE); c.fill(); rrect(c, -13, -27, 26, 54, 6); setc(c, hx('#2A3040')); c.fill()
            circle(c, -5, -18, 4); setc(c, hx('#05070A')); c.fill()
            c.restore()
            circle(c, w[0], w[1], 10); setc(c, OUTLINE); c.fill(); circle(c, w[0], w[1], 7.5); setc(c, So['hand']); c.fill()
        figure(ctx, 985, 830, 1.72, spose, So, gt, 'front', 'evil', look=slook, held=phone_in_hand, wind=.8,
               glint=math.sin(clamp(seg(t, .1, .5)) * math.pi))
        if ra > 0:
            # green lightning off the orb
            rnd = random.Random(int(gt * 30))
            ctx.set_line_width(3); setc(ctx, LIME1, .9 * ra)
            for k in range(4):
                x0, y0 = 1180, 150
                ctx.move_to(x0, y0)
                for j in range(6):
                    x0 += rnd.uniform(-60, 60); y0 += rnd.uniform(-40, 50); ctx.line_to(x0, y0)
                ctx.stroke()
        paint_cached(ctx, self.desk, self.k)
        candle(ctx, 1260, 618, 40, gt, 7)
        # the bellhop at attention, lance up
        figure(ctx, 1700, 975, 1.12, POSES['attention'], OUTFITS['unicorn'], gt, 'front', 'smile', look=(-1, 0))
        # the couple: en garde for a beat, then off to the party
        for i, name in enumerate(('vampire', 'witch')):
            bx = 1370 + i * 130
            if t < .5:
                pose = dict(POSES['guard'], weapon_ang=-40) if name == 'vampire' else POSES['flourish']
                figure(ctx, bx, 985, 1.15, pose, OUTFITS[name], gt, 'front', 'evil' if name == 'vampire' else 'smile', look=(-1, 0))
            else:
                cx2 = bx + (t - .5) * 560
                if cx2 < 2300:
                    figure(ctx, cx2, 985, 1.15, walk(gt * 8.5 + i), OUTFITS[name], gt, 'side', 'smile')
        # the hero, presenting the scroll with both hands
        def held_scroll(c, J):
            if walking: return
            sx_, sy_ = self.scroll_at(J)
            unroll = ease(seg(t, 2.05, 2.45))
            scroll(c, sx_, sy_, 70, 190, gt, unroll=unroll, detail=False)
        figure(ctx, hxp, self.HY, self.HSC, hpose, OUTFITS['ninja'], gt, 'side', 'hero', look=(1, -.3), wind=1.2, held=held_scroll)
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
        grip_back(c, x, y, w, h, FR_SKIN, ROBE, ang=a, side="right", scale=1.3, stitches=False, sleeve_dir=(1, -.2))
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
        # the papyrus pass, held up in both gloved fists
        sw_, sh_ = 420, 616
        ninja_fists(ctx, cx, cy, sw_, sh_, ang, behind=True)
        scroll(ctx, cx, cy, sw_, sh_, gt, ang=ang)
        ninja_fists(ctx, cx, cy, sw_, sh_, ang, behind=False)
        # autofocus box over the QR
        if 1.1 < t < 2.1:
            a = .8 * (1 - seg(t, 1.6, 2.1))
            pulse = 1 + .08 * (1 - seg(t, 1.1, 1.35))
            ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang); ctx.translate(-cx, -cy)
            x0, y0 = cx, cy + 102; kx = 1.2
            bw = 245 * pulse
            ctx.set_line_width(4); setc(ctx, hx('#FFD25A', a)); ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            for (dx, dy) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
                px, py = x0 + dx * bw / 2, y0 + dy * bw / 2
                ctx.move_to(px, py - dy * 28); ctx.line_to(px, py); ctx.line_to(px - dx * 28, py); ctx.stroke()
            ctx.restore()
        ctx.restore()
        dx = lerp(300, 0, ease_out(seg(t, 0, .9))); dy = lerp(-240, 0, ease_out(seg(t, 0, .9)))
        ox, oy = dx + math.sin(gt * 2) * 4, dy + math.cos(gt * 1.7) * 3
        ctx.save(); ctx.translate(ox, oy)
        ctx.set_source_surface(self.rphone, 0, 0); ctx.paint()
        ctx.restore()
        # the sorcerer's scanning spell: a magic circle on the lens, a beam onto the QR
        ma = ease(seg(t, .7, 1.1))
        lx, ly = 1375 + ox, 74 + oy
        if ma > 0:
            qx, qy = self.P[0], self.P[1] + 102
            ctx.save(); ctx.set_operator(cairo.OPERATOR_ADD)
            ctx.move_to(lx - 30, ly + 10); ctx.line_to(qx - 110, qy - 110); ctx.line_to(qx + 110, qy + 110); ctx.line_to(lx + 30, ly - 10); ctx.close_path()
            ctx.set_source(lingrad(lx, ly, qx, qy, [(0, hx('#C3EF52', .35 * ma)), (1, hx('#C3EF52', .08 * ma))])); ctx.fill()
            ctx.restore()
            magic_circle(ctx, lx, ly, 70 + 8 * math.sin(gt * 4), gt, LIME1, ma)

def ninja_fists(ctx, cx, cy, w, h, ang, behind=False):
    """the hero's gloved fists on the scroll's lower roller, sleeves rising from the bottom of frame"""
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang)
    for sg in (-1, 1):
        fx, fy = sg * (w / 2 - 30), h / 2 + 4
        if behind:
            ctx.move_to(fx - 46, fy + 10); ctx.line_to(fx - 70 + sg * 40, H); ctx.line_to(fx + 90 + sg * 40, H); ctx.line_to(fx + 46, fy + 10); ctx.close_path()
            setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve(); setc(ctx, hx('#232A4A')); ctx.fill()
            rrect(ctx, fx - 50, fy + 30, 100, 44, 10); setc(ctx, hx('#7E8498')); ctx.fill()
            for k in range(3):
                line(ctx, fx - 46, fy + 38 + k * 12, fx + 46, fy + 42 + k * 12, 2.5, hx('#5A6074'))
        else:
            rrect(ctx, fx - 40, fy - 30, 80, 60, 24); setc(ctx, OUTLINE); ctx.fill()
            rrect(ctx, fx - 36, fy - 26, 72, 52, 21); setc(ctx, NINJA_GLOVE); ctx.fill()
            for k in range(3):
                line(ctx, fx - 24 + k * 18, fy - 24, fx - 24 + k * 18, fy - 8, 3, hx('#14182A'))
            ellipse(ctx, fx - 14, fy - 14, 10, 5); setc(ctx, (1, 1, 1, .15)); ctx.fill()
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
        ninja_fists(c, 280, 820, 210, 308, .12, behind=True)
        scroll(c, 280, 820, 210, 308, 0, ang=.12)
        ninja_fists(c, 280, 820, 210, 308, .12, behind=False)
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
        # the impact burst behind the phone as the check pops
        ib = math.sin(clamp(seg(t, lock + .05, land + .25)) * math.pi)
        if ib > 0:
            radial_lines(ctx, cx, cy - 40, 260, 1500, 70, gt, LIME1, .45 * ib)
            radial_lines(ctx, cx, cy - 40, 300, 1500, 50, gt + .5, (1, 1, 1, 1), .35 * ib)
        grip_back(ctx, cx, cy, w, h, FR_SKIN, ROBE, side='right', scale=1.0, stitches=False, sleeve_dir=(.3, 1))
        phone_frame(ctx, cx, cy, w, screen=lambda c, tt: screen_scanner(c, tt, confirmed=conf, show_check=landed, label=ease(seg(t, land, land + .35)), subject='scroll',
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
    """the party: a conga line looping through the doorway, led by the bellhop; the hero joins the end"""
    LINE = ['unicorn', 'vampire', 'witch', 'knight', 'pirate', 'valkyrie', 'reaper', 'samurai', 'mummy']
    C, RX, RY, DU = (1140, 800), 300, 46, .085
    def __init__(self):
        self.k = 1.25
        self.wall = make_cache(s6_wall, self.k)

    def U(self, gt): return gt * .16

    def slot(self, u):
        ang = 2 * math.pi * u
        x = self.C[0] + self.RX * math.cos(ang); y = self.C[1] + self.RY * math.sin(ang)
        return x, y, .6 + .08 * math.sin(ang), math.sin(ang) > 0, math.sin(ang)

    def conga_pose(self, gt, leader=False):
        ph = gt * 2 * math.pi * (124 / 60) / 4
        p = conga(ph)
        p['la'], p['lf'] = 168, 176          # the far hand holds the weapon high
        p['weapon_ang'] = -96
        if leader:
            p['ra'], p['rf'] = 150, 170
        return p

    def hero(self, t, gt):
        """x, y, scale, pose, view, expr, inside, mirror"""
        if t < 1.8:
            k = ease_out_back(seg(t, .45, .8), 1.8)
            return 560, 1030, 1.75, blend(POSES['stand'], POSES['triumph'], k), 'front', 'huge', False, False
        if t < 3.4:
            p = ease(seg(t, 1.8, 3.4))
            return lerp(560, 1135, p), lerp(1030, 835, p), lerp(1.75, .5, p ** .8), walk(t * 9), 'back', 'huge', p > .95, False
        sx, sy, ss, front, _ = self.slot(self.U(gt) - len(self.LINE) * self.DU)
        k = ease(seg(t, 3.4, 3.9))
        x = lerp(1135, sx, k); y = lerp(835, sy, k); sc = lerp(.55, ss, k)
        return x, y, sc, self.conga_pose(gt), 'side', 'huge', True, front

    def party(self, ctx, t, gt, hero_fn):
        x, y, w, h = PD
        ctx.save(); ctx.rectangle(x - 80, y, w + 160, h); ctx.clip()
        ctx.set_source(lingrad(0, y, 0, y + h, [(0, hx('#3A1650')), (.6, hx('#7A2A3A')), (1, hx('#C2622A'))])); ctx.paint()
        for i, col in enumerate(('#7C5CFF', '#22D3EE', '#F28A1C', '#C3EF52')):
            sx = x + w / 2 + math.sin(gt * (1.3 + i * .4) + i * 2) * w * .45
            sy = y + h * .45 + math.cos(gt * (1.1 + i * .3) + i) * h * .3
            glow(ctx, sx, sy, 170, hx(col), .45)
        for row in range(3):
            yy = y + 40 + row * 50
            for k in range(14):
                lx = x - 20 + k * (w + 40) / 13
                ly = yy + 30 * math.sin(k / 13 * math.pi)
                on = .6 + .4 * math.sin(gt * 6 + k * 1.3 + row)
                col = ('#FFD25A', '#F28A1C', '#A78BFA')[(k + row) % 3]
                glow(ctx, lx, ly, 20, hx(col), on * .8); circle(ctx, lx, ly, 4.5); setc(ctx, hx(col)); ctx.fill()
        ctx.rectangle(x - 80, y + h - 140, w + 160, 140); ctx.set_source(lingrad(0, y + h - 140, 0, y + h, [(0, hx('#2A1420')), (1, hx('#4A2418'))])); ctx.fill()
        rrect(ctx, x + 20, y + h - 230, 160, 16, 4); setc(ctx, hx('#2A1810')); ctx.fill()
        jack(ctx, x + 60, y + h - 230, .45, gt, 3); jack(ctx, x + 130, y + h - 230, .38, gt, 4)
        # the line, back half first, then the front half, by depth
        U = self.U(gt)
        items = []
        for i, name in enumerate(self.LINE):
            sx, sy, ss, front, sn = self.slot(U - i * self.DU)
            items.append((sy, 'd', name, sx, sy, ss, front, i == 0))
        items.append((None, 'h'))
        def depth(it):
            if it[1] == 'h':
                hs = self.hero(t, gt); return hs[1] if hs[6] else -1e9
            return it[0]
        for it in sorted(items, key=depth):
            if it[1] == 'h':
                hero_fn(); continue
            _, _, name, sx, sy, ss, front, leader = it
            Ou = dict(OUTFITS[name], weapon_hand='l', second=None)
            figure(ctx, sx, sy, ss, self.conga_pose(gt, leader), Ou, gt, 'side', 'smile' if name not in ('vampire',) else 'evil',
                   mirror=front, wind=1.2)
        ctx.restore()

    def draw(self, ctx, t, gt):
        hx_, hy_, hs_, hpose, hview, hexpr, inside, hmirror = self.hero(t, gt)
        Nj = OUTFITS['ninja']
        def hero_inside():
            if inside:
                figure(ctx, hx_, hy_, hs_, hpose, Nj, gt, hview, hexpr, mirror=hmirror, wind=1.2)
        ctx.save()
        z = lerp(1.0, 1.04, ease(seg(t, 0, 4.5)))
        cam(ctx, z, 960, 540, k=0)
        self.party(ctx, t, gt, hero_inside)
        paint_cached(ctx, self.wall, self.k)
        if not inside:
            if hview == 'front':
                tri = ease(seg(t, .45, .8))
                if tri > 0 and t < 1.4:
                    radial_lines(ctx, hx_, hy_ - 300 * hs_, 300, 1400, 60, gt, (1, .9, .5, 1), .25 * tri * (1 - seg(t, 1.1, 1.4)))
                figure(ctx, hx_, hy_, hs_, hpose, Nj, gt, 'front', 'huge', wind=1.4,
                       glint=math.sin(clamp(seg(t, .7, 1.1)) * math.pi))
            else:
                figure(ctx, hx_, hy_, hs_, hpose, Nj, gt, 'back', wind=1.2)
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
