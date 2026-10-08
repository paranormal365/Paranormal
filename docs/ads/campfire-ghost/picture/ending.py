"""IsHaunted logo ending, after render_uw.py's ending(): the site's dark background, the logo built piece
by piece (the ghost piece floats in last), the wordmark, then a letter-by-letter tagline. 1280x720."""
import os, sys, math
os.environ.setdefault('AD_BUILD', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'build'))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cv2, cairo
from common import draw_logo_piece, wordmark, hx, ease, ease_out, ease_out_back, seg, clamp, lerp, glow, font, text_w, setc
W, H = 1280, 720
INK = hx('#0C1017'); ECTO = hx('#7C5CFF'); HAUNT = hx('#22D3EE'); BONE = hx('#E9EDF4'); FOG = hx('#98A3B4')
TAG = os.environ.get("TAGLINE", "Even ghosts need a good video editor.")
SUB = os.environ.get('SUBLINE', "Upload it. Mark it. Cut it. Share it.")

def dark_bg(c, t):
    setc(c, INK); c.paint()
    glow(c, 380 + 40 * math.sin(t * .6), 250, 760, ECTO, .38)
    glow(c, 930 + 30 * math.cos(t * .5), 520, 660, HAUNT, .24)
    for k in range(5):
        x0 = 120 + k * 260 + 40 * math.sin(t * .4 + k)
        g = cairo.LinearGradient(0, 0, 0, H); g.add_color_stop_rgba(0, HAUNT[0], HAUNT[1], HAUNT[2], .06); g.add_color_stop_rgba(1, HAUNT[0], HAUNT[1], HAUNT[2], 0)
        c.move_to(x0 - 30, 0); c.line_to(x0 + 30, 0); c.line_to(x0 + 140, H); c.line_to(x0 + 40, H); c.close_path(); c.set_source(g); c.fill()

def bg_frame(t):
    bgra = np.zeros((H, W, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    dark_bg(c, t); s.flush()
    return cv2.cvtColor(bgra, cv2.COLOR_BGRA2BGR)

def ending(t, tt):
    """t: absolute time (for the drifting light); tt: seconds since the logo build started"""
    bgra = np.zeros((H, W, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    dark_bg(c, t)
    LX, LY, LS = 640, 250, 290
    if tt > 1.9: glow(c, LX, LY, 280, ECTO, .3 * ease(seg(tt, 1.9, 2.5)))
    p = seg(tt, 0, .55)
    if p > 0: e = ease_out_back(p, 1.3); draw_logo_piece(c, 'arc', LX, LY, LS, dy=lerp(420, 0, e), rot=lerp(-1.2, 0, e), a=clamp(p * 3))
    p = seg(tt, .2, .75)
    if p > 0: e = ease_out_back(p, 1.3); draw_logo_piece(c, 'moon', LX, LY, LS, dy=lerp(460, 0, e), rot=lerp(1.0, 0, e), sc=lerp(1.4, 1, e), a=clamp(p * 3))
    for i in range(4):
        p = seg(tt, .75 + i * .09, 1.02 + i * .09)
        if p > 0: draw_logo_piece(c, f'win{i}', LX, LY, LS, sc=ease_out_back(p, 2.8), pivot=(.565, .615), a=clamp(p * 4))
    p = seg(tt, 1.05, 2.05)
    if p > 0:   # the ghost swoops in from the left -- the same ghost that escaped the videos
        e = ease_out(p); wob = 1 - e
        draw_logo_piece(c, 'ghost', LX, LY, LS, dx=lerp(-560, 0, e) + math.sin(p * 9) * 26 * wob,
                        dy=lerp(260, 0, e) + math.sin(p * 7 + 1) * 60 * wob - math.sin(p * math.pi) * 40,
                        rot=math.sin(p * 8) * .26 * wob, sx=1 + math.sin(p * 11) * .07 * wob, sy=1 - math.sin(p * 11) * .07 * wob,
                        a=clamp(p * 4), pivot=(.5, .4))
    p = seg(tt, 1.95, 2.35)
    if p > 0:
        e = ease_out_back(p, 1.6)
        c.save(); c.translate(W / 2, 474 + (1 - e) * 24); c.scale(lerp(.85, 1, e), lerp(.85, 1, e)); wordmark(c, 0, 0, 60, align='c', a=clamp(p * 2)); c.restore()
    def letters(txt, size, y, t0, col, bold=True, step=.016):
        font(c, 'Public Sans', size, bold); total = text_w(c, txt); x = W / 2 - total / 2
        for i, ch in enumerate(txt):
            cw = text_w(c, ch); p = seg(tt, t0 + i * step, t0 + i * step + .24)
            if p > 0:
                e = ease_out_back(p, 2.2)
                c.save(); c.translate(x + cw / 2, y - size * .35 + (1 - e) * 12); c.scale(lerp(.4, 1, e), lerp(.4, 1, e))
                setc(c, col, clamp(p * 3)); c.move_to(-cw / 2, size * .35); font(c, 'Public Sans', size, bold); c.show_text(ch); c.restore()
            x += cw
    letters(TAG, 30, 538, 2.35, BONE)
    letters(SUB, 20, 584, 2.35 + len(TAG) * .016 + .35, FOG, bold=False, step=.012)
    s.flush()
    return cv2.cvtColor(bgra, cv2.COLOR_BGRA2BGR)

if __name__ == '__main__':
    for tt in (0.4, 1.4, 2.2, 4.2):
        cv2.imwrite(f'end_{tt}.png', ending(10 + tt, tt))
