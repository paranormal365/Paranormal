"""The robot's hologram: a walking tour check-in, in the site's colors, on the green screen it holds.

The source's green screen is a see-through hologram that powers on from the bottom up (frames ~545-560)
and glows green onto the robot. Here:
  - every strong green in the shot from frame 540 on is turned to the site's cyan, so the light it throws
    matches what it now shows;
  - the check-in screen is warped onto the panel's corners (read off a gridded crop at six key frames and
    interpolated — the panel moves smoothly), and shown only where the source was green, so it powers on
    exactly as the green did, see-through and scan-lined like the original.
"""
import math
import cairo, cv2, numpy as np
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'ishaunted-ad'))
from common import draw_logo, hx, ease, ease_out, ease_out_back, seg, clamp, lerp, glow, font, text_w, setc, rrect

CW, CH = 960, 640            # the screen's own canvas
INK = hx('#0C1017'); DEEP = hx('#1E1650'); ECTO = hx('#7C5CFF'); HAUNT = hx('#22D3EE')
LILAC = hx('#A78BFA'); BONE = hx('#E9EDF4'); FOG = hx('#98A3B4')

# Panel corners (TL, TR, BR, BL) in the 1280x720 frame, by frame number.
KEYS = {
    550: [(565, 395), (825, 405), (815, 570), (550, 545)],
    555: [(605, 305), (928, 335), (920, 550), (585, 565)],
    560: [(658, 240), (1005, 275), (985, 535), (625, 535)],
    566: [(688, 220), (1050, 280), (1035, 560), (665, 565)],
    572: [(685, 225), (1055, 285), (1035, 565), (645, 560)],
    579: [(685, 240), (1055, 285), (1035, 565), (650, 560)],
}
FIRST = 540   # from here the green is the hologram's


def corners(i):
    ks = sorted(KEYS)
    if i <= ks[0]: return np.float32(KEYS[ks[0]])
    if i >= ks[-1]: return np.float32(KEYS[ks[-1]])
    for a, b in zip(ks, ks[1:]):
        if a <= i <= b:
            t = (i - a) / (b - a)
            return np.float32(KEYS[a]) * (1 - t) + np.float32(KEYS[b]) * t


def screen(p, t):
    """p: 0..1 how far the check-in has played; t: seconds, for the shimmer."""
    bgra = np.zeros((CH, CW, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, CW, CH, CW * 4); c = cairo.Context(s)
    g = cairo.RadialGradient(CW * .3, CH * .2, 40, CW * .5, CH * .5, CW * .75)
    g.add_color_stop_rgba(0, *DEEP[:3], .96); g.add_color_stop_rgba(1, *INK[:3], .96)
    c.set_source(g); c.paint()
    glow(c, CW * .22, CH * .5, 300, ECTO, .35); glow(c, CW * .85, CH * .85, 260, HAUNT, .18)

    # the top bar: the brand and what this is
    draw_logo(c, 62, 57, 66)
    font(c, 'Public Sans', 30, True); setc(c, BONE); c.move_to(104, 68); c.show_text('IsHaunted')
    pill = 'Walking tour'; font(c, 'Public Sans', 22, True); w = text_w(c, pill)
    rrect(c, CW - 60 - w, 34, w + 34, 44, 22); setc(c, ECTO, .35); c.fill()
    setc(c, LILAC); c.move_to(CW - 43 - w, 64); c.show_text(pill)

    # the check: a gradient disc that pops in, then the tick drawn across it
    cx, cy, r = 220, 340, 128
    e = ease_out_back(seg(p, 0, .3), 1.8)
    if e > 0:
        c.save(); c.translate(cx, cy); c.scale(e, e)
        glow(c, 0, 0, r * 1.9, HAUNT, .45 + .1 * math.sin(t * 6))
        c.arc(0, 0, r, 0, 2 * math.pi); dg = cairo.LinearGradient(-r, -r, r, r)
        dg.add_color_stop_rgb(0, *ECTO[:3]); dg.add_color_stop_rgb(1, *HAUNT[:3]); c.set_source(dg); c.fill()
        k = ease_out(seg(p, .18, .45))
        if k > 0:
            pts = [(-58, 4), (-14, 48), (66, -42)]
            seglen = [math.dist(pts[0], pts[1]), math.dist(pts[1], pts[2])]; total = sum(seglen) * k
            c.set_line_width(26); c.set_line_cap(cairo.LINE_CAP_ROUND); c.set_line_join(cairo.LINE_JOIN_ROUND)
            setc(c, BONE); c.move_to(*pts[0])
            for (a, b), L in zip(zip(pts, pts[1:]), seglen):
                if total <= 0: break
                f = min(1, total / L); c.line_to(a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f); total -= L
            c.stroke()
        c.restore()

    # the words, each sliding up a little as it arrives
    def line(txt, size, y, col, at, bold=True):
        q = ease_out(seg(p, at, at + .22))
        if q <= 0: return
        font(c, 'Public Sans', size, bold); setc(c, col, q); c.move_to(400, y + (1 - q) * 18); c.show_text(txt)
    line('Checked in!', 74, 288, BONE, .3)
    line('Lantern Lane Ghost Walk', 34, 346, HAUNT, .4)
    line('Party of 2  ·  Tonight, 8:00 PM', 30, 396, FOG, .48, bold=False)
    line('Your guide is at the lamp post.', 28, 440, LILAC, .56, bold=False)

    # the pass strip along the bottom
    q = ease_out(seg(p, .62, .85))
    if q > 0:
        rrect(c, 34, CH - 118, CW - 68, 76, 18); setc(c, BONE, .07 * q); c.fill()
        lg = cairo.LinearGradient(34, 0, CW - 34, 0); lg.add_color_stop_rgba(0, *ECTO[:3], q); lg.add_color_stop_rgba(1, *HAUNT[:3], q)
        rrect(c, 34, CH - 46, (CW - 68) * q, 6, 3); c.set_source(lg); c.fill()
        font(c, 'Public Sans', 26, True); setc(c, BONE, q); c.move_to(62, CH - 70); c.show_text('Admitted 2 of 2')
        msg = 'No line. No clipboard. Boo.'; font(c, 'Public Sans', 24, False); setc(c, FOG, q)
        c.move_to(CW - 62 - text_w(c, msg), CH - 70); c.show_text(msg)

    # hologram scan lines, drifting
    off = (t * 40) % 6
    for y in np.arange(-6 + off, CH, 6):
        c.rectangle(0, y, CW, 1.6); setc(c, BONE, .05); c.fill()
    s.flush()
    return bgra


def to_cyan(frame):
    """Every strong green in the shot becomes the site's cyan, keeping its brightness."""
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    h, s, v = cv2.split(hsv)
    m = ((h >= 38) & (h <= 88) & (s > 55)).astype(np.float32)
    m = cv2.GaussianBlur(m, (0, 0), 1.2)
    h2 = h.astype(np.float32) * (1 - m) + 94 * m          # 94 ~ #22D3EE in OpenCV's 0-180 hue
    out = cv2.cvtColor(cv2.merge([h2.astype(np.uint8), s, v]), cv2.COLOR_HSV2BGR)
    return out


def composite(frame, i, p, t):
    """The source frame with its hologram replaced. p: the check-in's progress; t: seconds."""
    src = frame.astype(np.int32)
    b, g, r = src[..., 0], src[..., 1], src[..., 2]
    # where the source was green is where the hologram is lit
    lit = np.clip((g - np.maximum(r, b) - 18) / 95.0, 0, 1).astype(np.float32)
    quad = corners(i)
    mask = np.zeros(frame.shape[:2], np.uint8); cv2.fillConvexPoly(mask, quad.astype(np.int32), 255)
    mask = cv2.GaussianBlur(mask, (0, 0), 2).astype(np.float32) / 255
    # Lit where the source was green while it powers on; once it is fully on (frame ~558), a floor, so
    # the city behind never washes out the smaller lines.
    on = clamp((i - 553) / 5)
    alpha = (np.maximum(np.power(lit, .6) * .93, on * .84) * mask)[..., None]

    base = to_cyan(frame).astype(np.float32)
    content = screen(p, t)
    M = cv2.getPerspectiveTransform(np.float32([(0, 0), (CW, 0), (CW, CH), (0, CH)]), quad)
    warped = cv2.warpPerspective(content, M, (frame.shape[1], frame.shape[0]), flags=cv2.INTER_LINEAR)
    rgb = warped[..., :3].astype(np.float32)
    # the hologram keeps a little of what is behind it, like the original
    behind = cv2.cvtColor(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY), cv2.COLOR_GRAY2BGR).astype(np.float32)
    holo = rgb * .92 + behind * .08
    out = base * (1 - alpha) + holo * alpha
    out += rgb * alpha * .12                                  # a faint glow off the screen
    return out.clip(0, 255).astype(np.uint8)
