"""The green-and-blue dragon the hero rides in on. Facing +x, origin at the chest; about 600 units
nose to tail. Back and flanks green, belly plates and wing membranes blue, glowing cyan eyes."""
import math
import cairo
from common import *

GREEN = hx('#2FA36B'); GREEN_D = hx('#1C6E4C'); BLUE = hx('#3A8BE0'); BLUE_D = hx('#1E4E9A'); SPIKE = hx('#7FF0D8')

def _spine(t, swoop=0.0):
    pts = []
    # head end -> neck -> body -> tail tip
    P0, P1, P2, P3 = (160, -92 + swoop * 20), (118, -86), (96, -24), (42, -8)
    for i in range(10):
        q = i / 9
        x = (1 - q) ** 3 * P0[0] + 3 * (1 - q) ** 2 * q * P1[0] + 3 * (1 - q) * q * q * P2[0] + q ** 3 * P3[0]
        y = (1 - q) ** 3 * P0[1] + 3 * (1 - q) ** 2 * q * P1[1] + 3 * (1 - q) * q * q * P2[1] + q ** 3 * P3[1]
        pts.append((x, y))
    for i in range(1, 9):
        q = i / 8
        pts.append((lerp(42, -130, q), -8 + math.sin(q * math.pi) * -10 + q * 8))
    for i in range(1, 16):
        q = i / 15
        pts.append((-130 - q * 360, 2 + q * 40 + math.sin(t * 3 - q * 5) * 34 * q))
    return pts

def _radii(n):
    r = []
    for i in range(n):
        q = i / (n - 1)
        if q < .28: r.append(lerp(17, 27, q / .28))
        elif q < .5: r.append(27 + 18 * math.sin((q - .28) / .22 * math.pi / 2))
        else: r.append(lerp(45, 3, ((q - .5) / .5) ** .8))
    return r

def _outline(pts, rad):
    up, dn = [], []
    for i, (x, y) in enumerate(pts):
        a = pts[max(0, i - 1)]; b = pts[min(len(pts) - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]; L = math.hypot(dx, dy) or 1
        nx, ny = -dy / L, dx / L
        up.append((x + nx * rad[i], y + ny * rad[i])); dn.append((x - nx * rad[i], y - ny * rad[i]))
    return up, dn

def _wing(ctx, sx, sy, t, flap, near=True):
    """bat wing from the shoulder: arm, wrist, four finger bones, a scalloped membrane"""
    elev = math.sin(flap) * 62          # degrees up (+) / down (-)
    fold = .55 + .45 * abs(math.cos(flap))
    ctx.save(); ctx.translate(sx, sy); ctx.rotate(math.radians(-elev - 8)); ctx.scale(1, fold)
    wr = (-40, -150)
    fingers = [(-40, -350), (-150, -340), (-250, -280), (-320, -180)]
    mem = cairo.LinearGradient(0, 0, -300, -300)
    c0, c1 = (BLUE, hx('#2FB8A0')) if near else (BLUE_D, GREEN_D)
    mem.add_color_stop_rgba(0, c0[0], c0[1], c0[2], .96); mem.add_color_stop_rgba(1, c1[0], c1[1], c1[2], .9)
    ctx.move_to(0, 0); ctx.line_to(*wr); ctx.line_to(*fingers[0])
    for i in range(1, len(fingers)):
        a, b = fingers[i - 1], fingers[i]
        ctx.curve_to(lerp(a[0], b[0], .3) + 30, lerp(a[1], b[1], .3) + 40, lerp(a[0], b[0], .7) + 30, lerp(a[1], b[1], .7) + 40, *b)
    ctx.curve_to(-280, -80, -200, -20, -150, 14); ctx.close_path()
    ctx.set_source(mem); ctx.fill_preserve(); ctx.set_line_width(4); setc(ctx, OUTLINE); ctx.stroke()
    bone = shade(GREEN, .8 if near else .6)
    line(ctx, 0, 0, wr[0], wr[1], 14, OUTLINE); line(ctx, 0, 0, wr[0], wr[1], 9, bone)
    for f in fingers:
        line(ctx, wr[0], wr[1], f[0], f[1], 7, OUTLINE); line(ctx, wr[0], wr[1], f[0], f[1], 4, bone)
    ctx.move_to(wr[0], wr[1]); ctx.line_to(wr[0] + 10, wr[1] - 30); ctx.line_to(wr[0] + 14, wr[1]); ctx.close_path(); setc(ctx, hx('#E9F6EE')); ctx.fill()
    ctx.restore()

def _head(ctx, x, y, ang, t, roar=0.0):
    ctx.save(); ctx.translate(x, y); ctx.rotate(ang)
    jaw = 6 + roar * 26
    # horns swept back
    for k, (hx_, hy_) in enumerate(((-70, -62), (-56, -40))):
        ctx.move_to(14, -20 - k * 6); ctx.curve_to(-10, -40, hx_ + 20, hy_ - 4, hx_, hy_); ctx.curve_to(hx_ + 30, hy_ + 10, 0, -10, 24, -10 - k * 4)
        ctx.close_path(); setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve()
        ctx.set_source(lingrad(0, -60, 0, 0, [(0, hx('#F2EAD8')), (1, hx('#B8AE96'))])); ctx.fill()
    # lower jaw
    ctx.move_to(30, 8); ctx.line_to(112, 12 + jaw); ctx.curve_to(100, 24 + jaw, 60, 26 + jaw * .6, 26, 22); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, BLUE); ctx.fill()
    if roar > .05:
        ctx.move_to(40, 12); ctx.line_to(108, 12); ctx.line_to(104, 10 + jaw); ctx.line_to(40, 18 + jaw * .5); ctx.close_path(); setc(ctx, hx('#5A1A2A')); ctx.fill()
        glow(ctx, 112, 12 + jaw / 2, 60, ACC2, .5 * roar)
    for i in range(6):
        tx_ = 50 + i * 10
        ctx.move_to(tx_, 10); ctx.line_to(tx_ + 4, 18); ctx.line_to(tx_ + 8, 10); ctx.close_path(); setc(ctx, (1, 1, 1, 1)); ctx.fill()
    # skull + snout
    ctx.move_to(-4, -22); ctx.curve_to(20, -36, 50, -34, 70, -22); ctx.curve_to(96, -18, 118, -10, 126, 0)
    ctx.curve_to(124, 8, 112, 12, 100, 12); ctx.line_to(30, 12); ctx.curve_to(10, 16, -6, 10, -10, -4); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve()
    ctx.set_source(lingrad(0, -36, 0, 14, [(0, shade(GREEN, 1.15)), (1, GREEN_D)])); ctx.fill()
    # brow ridge, nostril, the glowing eye
    ctx.move_to(30, -26); ctx.curve_to(46, -34, 62, -30, 70, -22); ctx.set_line_width(5); setc(ctx, GREEN_D); ctx.stroke()
    ellipse(ctx, 116, -6, 5, 3); setc(ctx, OUTLINE); ctx.fill()
    glow(ctx, 56, -16, 34, ACC2, .7)
    ctx.move_to(44, -16); ctx.curve_to(52, -24, 64, -22, 68, -14); ctx.curve_to(60, -10, 50, -10, 44, -16); ctx.close_path()
    setc(ctx, hx('#BFFBFF')); ctx.fill(); ellipse(ctx, 57, -16, 1.8, 4.5); setc(ctx, OUTLINE); ctx.fill()
    # frill spikes behind the jaw
    for i in range(4):
        ctx.move_to(-2 - i * 6, -10 + i * 8); ctx.line_to(-26 - i * 8, -4 + i * 12); ctx.line_to(-4 - i * 6, -2 + i * 8); ctx.close_path(); setc(ctx, SPIKE); ctx.fill()
    ctx.restore()

def dragon(ctx, x, y, s, t, flap, heading=0.0, roar=0.0, rider=None, swoop=0.0):
    """rider(ctx, sx, sy): called in dragon space where the rider sits (base of the neck)"""
    ctx.save(); ctx.translate(x, y); ctx.rotate(math.radians(heading)); ctx.scale(s, s)
    pts = _spine(t, swoop); rad = _radii(len(pts))
    up, dn = _outline(pts, rad)
    sh = (20, -34)
    _wing(ctx, sh[0] + 14, sh[1] - 4, t, flap + .35, near=False)
    # hind and fore legs, tucked in flight
    for (lx, ly, k) in ((-110, 24, 1.0), (20, 28, .8)):
        line(ctx, lx, ly, lx - 30 * k, ly + 50 * k, 22 * k, OUTLINE); line(ctx, lx, ly, lx - 30 * k, ly + 50 * k, 16 * k, GREEN_D)
        line(ctx, lx - 30 * k, ly + 50 * k, lx - 6, ly + 74 * k, 14 * k, OUTLINE); line(ctx, lx - 30 * k, ly + 50 * k, lx - 6, ly + 74 * k, 9 * k, GREEN_D)
        for c in range(3):
            ctx.move_to(lx - 6 + c * 6, ly + 74 * k); ctx.line_to(lx + 2 + c * 7, ly + 86 * k); ctx.line_to(lx + c * 6, ly + 74 * k); ctx.close_path(); setc(ctx, hx('#F2EAD8')); ctx.fill()
    # body tube
    ctx.move_to(*up[0])
    for p in up[1:]: ctx.line_to(*p)
    for p in reversed(dn): ctx.line_to(*p)
    ctx.close_path()
    body = ctx.copy_path()
    setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve()
    ctx.set_source(lingrad(0, -70, 0, 50, [(0, shade(GREEN, 1.15)), (.5, GREEN), (1, GREEN_D)])); ctx.fill()
    ctx.save(); ctx.append_path(body); ctx.clip()
    # belly plates (blue) along the lower edge
    for i in range(3, len(pts) - 4):
        (x0, y0), (x1, y1) = dn[i], dn[i + 1]
        mx, my = (pts[i][0] + dn[i][0]) / 2, (pts[i][1] + dn[i][1]) / 2
        ctx.move_to(x0, y0); ctx.line_to(x1, y1); ctx.line_to((pts[i + 1][0] + x1) / 2, (pts[i + 1][1] + y1) / 2); ctx.line_to(mx, my); ctx.close_path()
        setc(ctx, BLUE if i % 2 else shade(BLUE, .88)); ctx.fill()
    # scales: rows of small arcs
    ctx.set_line_width(1.6); setc(ctx, (0, 0, 0, .18))
    for i in range(1, len(pts) - 3):
        x0, y0 = pts[i]; r_ = rad[i]
        for k in (-.55, -.15):
            circle(ctx, x0, y0 + k * r_, max(2, r_ * .22)); ctx.stroke()
    ctx.restore()
    # back spikes
    for i in range(2, len(up) - 3, 1):
        a = up[i]; b = up[i + 1]; p = pts[i]
        dx, dy = a[0] - p[0], a[1] - p[1]; L = math.hypot(dx, dy) or 1
        hgt = 10 + rad[i] * .45
        ctx.move_to(*a); ctx.line_to(a[0] + dx / L * hgt + (b[0] - a[0]) * .2, a[1] + dy / L * hgt); ctx.line_to(*b); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(2.5); ctx.stroke_preserve(); setc(ctx, SPIKE); ctx.fill()
    # tail fin
    tip = pts[-1]
    ctx.move_to(tip[0] + 20, tip[1] - 4); ctx.line_to(tip[0] - 40, tip[1] - 30); ctx.line_to(tip[0] - 26, tip[1]); ctx.line_to(tip[0] - 40, tip[1] + 26); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve(); setc(ctx, BLUE); ctx.fill()
    # head
    hx0, hy0 = pts[0]; hx1, hy1 = pts[1]
    _head(ctx, hx0 - 6, hy0, math.atan2(hy0 - hy1, hx0 - hx1) * .5 - .1 * swoop, t, roar)
    _wing(ctx, sh[0], sh[1], t, flap, near=True)
    if rider: rider(ctx, 30, -52)
    ctx.restore()
