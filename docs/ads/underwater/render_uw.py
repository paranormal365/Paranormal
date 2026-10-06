"""
The underwater ad, picture:
  1. the green tablet on the diver's wrist becomes the IsHaunted EVP analyzer (perspective-tracked, keyed under
     the glove) and finds five EVPs: "Aargh", "Here", "Be", "Ye", "Booty";
  2. at the end of the scuba dance, a wall of bubbles wipes up to the site's dark background, lit purple and blue;
  3. the logo builds in the middle, then the name and the tagline.
    python3 render_uw.py out.mp4        |   python3 render_uw.py still 36.0 40.2 ...
"""
import sys, os, json, math, random, subprocess
import numpy as np, cv2, cairo
HERE = os.path.dirname(os.path.abspath(__file__)); BUILD = os.path.join(HERE, 'build')
AD = os.environ.get('AD_DIR', os.path.join(os.path.dirname(HERE), 'ishaunted-ad'))
sys.path.insert(0, AD); os.environ.setdefault('AD_BUILD', os.path.join(AD, 'build'))   # logo layers + drawing helpers
from common import draw_logo_piece, wordmark, hx, ease, ease_out, ease_out_back, ease_in, seg, clamp, lerp, glow, rrect, circle, font, text_w, setc

SRC = os.environ.get('UW_SRC', os.path.expanduser('~/Downloads/Underwater-Ad.mp4'))
TRACKF = os.environ.get('UW_TRACK', os.path.join(BUILD, 'track.json'))
FPS = 30; W, H = 1280, 720
TRACK = {int(k): v for k, v in json.load(open(TRACKF))['track'].items()}
VEND = 1419                       # last frame of the clip (47.3 s)
T_WIPE0, T_WIPE1 = 46.55, 47.75   # bubbles rise and cover
T_LOGO = 47.9
T_END = 52.5

INK = hx('#0C1017'); MIST = hx('#151C28'); ECTO = hx('#7C5CFF'); HAUNT = hx('#22D3EE'); BONE = hx('#E9EDF4')
FOG = hx('#98A3B4'); SUCCESS = hx('#5ACD8B'); LINK = hx('#A78BFA')

# ── the analyzer, landscape (1000 x 700) ────────────────────────────────────
CW, CH = 1000, 700
WORDS = ['Aargh', 'Here', 'Be', 'Ye', 'Booty']
T_TAP, T_SCAN0, T_SCAN1, T_DONE, T_PHRASE = 34.55, 34.7, 39.55, 39.7, 40.05
MARKS = [(35.35, .12), (36.15, .33), (36.95, .52), (37.75, .70), (38.55, .88)]
rnd = random.Random(8)
WAVE = np.array([rnd.random() * .8 + .2 for _ in range(150)])
for _, pos in MARKS:
    i = int(pos * 149)
    for d in range(-3, 4): WAVE[max(0, min(149, i + d))] += (1.8 - abs(d) * .45)

def analyzer(t):
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, CW, CH); c = cairo.Context(s)
    g = cairo.LinearGradient(0, 0, 0, CH); g.add_color_stop_rgba(0, *MIST); g.add_color_stop_rgba(1, *INK); c.set_source(g); c.paint()
    glow(c, 160, 120, 520, ECTO, .20); glow(c, 860, 600, 480, HAUNT, .12)
    # header: the logo, the product, where we are
    draw_logo_piece  # (imported; the full logo below)
    for nme in ('arc', 'moon', 'win0', 'win1', 'win2', 'win3', 'ghost'):
        draw_logo_piece(c, nme, 70, 66, 84)
    font(c, 'Public Sans', 30, True); setc(c, BONE); c.move_to(126, 62); c.show_text('EVP Analyzer')
    font(c, 'Public Sans', 19); setc(c, FOG); c.move_to(126, 92); c.show_text('Shipwreck · 60 ft · 0:41 recorded')
    font(c, 'Public Sans', 16, True); setc(c, LINK); c.move_to(850, 62); c.show_text('Field Kit')
    # waveform panel
    px, py, pw, ph = 36, 132, 928, 330
    rrect(c, px, py, pw, ph, 22); setc(c, hx('#1B2433')); c.fill_preserve(); c.set_line_width(1.5); setc(c, (1, 1, 1, .09)); c.stroke()
    prog = ease(seg(t, T_SCAN0, T_SCAN1))
    mid = py + ph * .62
    n = len(WAVE); bw = (pw - 40) / n
    for i, a in enumerate(WAVE):
        x = px + 20 + i * bw; hgt = 10 + a * 46
        done = (i / n) <= prog
        setc(c, HAUNT if done else FOG, .95 if done else .3)
        rrect(c, x, mid - hgt / 2, max(bw - 1.6, 1.2), hgt, 1.5); c.fill()
    if T_SCAN0 <= t < T_DONE:
        hxp = px + 20 + prog * (pw - 40)
        glow(c, hxp, mid, 70, HAUNT, .5)
        c.set_line_width(4); setc(c, BONE); c.move_to(hxp, py + 70); c.line_to(hxp, py + ph - 18); c.stroke()
    # the five finds: a marker, its number, the word the ghost said
    for k, ((tm, pos), word) in enumerate(zip(MARKS, WORDS)):
        e0 = seg(t, tm, tm + .3)
        if e0 <= 0: continue
        e = ease_out_back(e0, 2.4); x = px + 20 + pos * (pw - 40)
        glow(c, x, mid, 110 * e, ECTO, .55 * (1 - seg(t, tm + .3, tm + 1.3)) + .14)
        c.set_line_width(5); setc(c, ECTO); c.move_to(x, mid + 56); c.line_to(x, py + 112 + (1 - e) * 40); c.stroke()
        circle(c, x, py + 108 + (1 - e) * 40, 10 * e); setc(c, ECTO); c.fill()
        font(c, 'Irish Grover', 34); tw = text_w(c, word); lab = f'EVP {k + 1}'
        c.save(); c.translate(x, py + 66 + (1 - e) * 40); c.scale(lerp(.4, 1, e), lerp(.4, 1, e))
        rrect(c, -tw / 2 - 14, -34, tw + 28, 46, 14); setc(c, ECTO, clamp(e0 * 3)); c.fill()
        setc(c, (1, 1, 1, clamp(e0 * 3))); c.move_to(-tw / 2, 0); c.show_text(word)
        c.restore()
        font(c, 'Public Sans', 14, True); lw = text_w(c, lab); setc(c, FOG, clamp(e0 * 3)); c.move_to(x - lw / 2, mid + 82); c.show_text(lab)
    # status, bottom
    found = sum(1 for m_ in MARKS if t >= m_[0])
    if t < T_SCAN0:
        rrect(c, 36, 500, 330, 92, 22)
        g2 = cairo.LinearGradient(36, 500, 366, 592); g2.add_color_stop_rgba(0, *ECTO); g2.add_color_stop_rgba(1, *HAUNT); c.set_source(g2); c.fill()
        font(c, 'Public Sans', 30, True); setc(c, (1, 1, 1, 1)); tw = text_w(c, 'Scan for EVPs'); c.move_to(201 - tw / 2, 557); c.show_text('Scan for EVPs')
        if t > T_TAP:
            r = seg(t, T_TAP, T_TAP + .4); c.set_line_width(5); setc(c, (1, 1, 1, .7 * (1 - r))); circle(c, 201, 546, 30 + r * 200); c.stroke()
    elif t < T_DONE:
        font(c, 'Public Sans', 32, True); setc(c, BONE); c.move_to(40, 540); c.show_text(f'Listening… {int(prog * 100)}%')
        rrect(c, 40, 560, 560, 12, 6); setc(c, (1, 1, 1, .1)); c.fill()
        rrect(c, 40, 560, 560 * prog, 12, 6); g3 = cairo.LinearGradient(40, 0, 600, 0); g3.add_color_stop_rgba(0, *ECTO); g3.add_color_stop_rgba(1, *HAUNT); c.set_source(g3); c.fill()
        font(c, 'Public Sans', 22); setc(c, FOG); c.move_to(40, 612); c.show_text(f'{found} found so far')
    else:
        k = ease_out_back(seg(t, T_DONE, T_DONE + .35), 2.0)
        glow(c, 76, 540, 110, SUCCESS, .45 * k)
        circle(c, 76, 540, 32 * k); setc(c, SUCCESS); c.fill()
        c.set_line_width(7); setc(c, (1, 1, 1, k)); c.set_line_cap(cairo.LINE_CAP_ROUND); c.set_line_join(cairo.LINE_JOIN_ROUND)
        c.move_to(62, 541); c.line_to(72, 552); c.line_to(92, 528); c.stroke()
        a = clamp(seg(t, T_DONE, T_DONE + .25))
        font(c, 'Public Sans', 40, True); setc(c, BONE, a); c.move_to(126, 556); c.show_text('5 EVPs found')
        p2 = ease(seg(t, T_PHRASE, T_PHRASE + .5))
        if p2 > 0:
            font(c, 'Irish Grover', 40); phrase = '“Aargh… here be ye booty.”'
            setc(c, HAUNT, p2); c.move_to(40, 626 + (1 - p2) * 12); c.show_text(phrase)
    s.flush()
    return np.frombuffer(s.get_data(), np.uint8).reshape(CH, CW, 4).copy()

def composite(frame, t, quad):
    ui = analyzer(t)
    srcq = np.float32([[0, 0], [CW, 0], [CW, CH], [0, CH]]); dstq = np.float32(quad)
    M = cv2.getPerspectiveTransform(srcq, dstq)
    warped = cv2.warpPerspective(ui, M, (W, H), flags=cv2.INTER_LINEAR, borderValue=(0, 0, 0, 0)).astype(np.float32)
    f = frame.astype(np.float32)
    b, g, r = f[..., 0], f[..., 1], f[..., 2]
    gr = g - np.maximum(r, b)
    key = cv2.GaussianBlur(np.clip((gr - 18) / 45.0, 0, 1), (3, 3), 0)
    wa = warped[..., 3:4] / 255.0
    fill = np.array([INK[2], INK[1], INK[0]], np.float32) * 255
    ui_rgb = warped[..., :3] + fill * (1 - wa)
    # sit it underwater: the screen's own falloff, a blue-green cast, a touch of softness, moving caustics
    lum = cv2.GaussianBlur(g, (0, 0), 20); med = np.median(g[gr > 50]) if (gr > 50).any() else 180
    ui_rgb *= np.clip(lum / med, .8, 1.12)[..., None]
    ui_rgb *= np.array([1.04, 1.0, .86], np.float32)          # BGR: a little more blue, a little less red
    ui_rgb = cv2.GaussianBlur(ui_rgb, (0, 0), .9)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    caust = (np.sin(xx * .045 + t * 2.1 + np.sin(yy * .03 + t)) * np.sin(yy * .05 - t * 1.7)) * 7
    ui_rgb += caust[..., None] * key[..., None]
    a = key[..., None]
    out = f * (1 - a) + ui_rgb * a
    near = (cv2.dilate((gr > 25).astype(np.uint8), np.ones((15, 15), np.uint8)) > 0) & (key < .5)
    gch = out[..., 1]; lim = np.maximum(out[..., 0], out[..., 2]) * 1.03
    gch[near] = np.minimum(gch[near], lim[near]); out[..., 1] = gch
    spill = cv2.GaussianBlur(ui_rgb * key[..., None], (0, 0), 22)
    out += spill * .16 * (1 - key[..., None]) * near[..., None]
    return np.clip(out, 0, 255).astype(np.uint8)

# ── the bubble wipe and the dark, lit background ────────────────────────────
brng = np.random.default_rng(5)
FRONT_B = [(brng.uniform(-40, W + 40), brng.uniform(-60, 60), brng.uniform(10, 46)) for _ in range(150)]
LOOSE_B = [(brng.uniform(0, W), brng.uniform(0, 1), brng.uniform(3, 18), brng.uniform(.6, 1.6)) for _ in range(140)]
AFTER_B = [(brng.uniform(0, W), brng.uniform(0, 3), brng.uniform(2, 9), brng.uniform(60, 140)) for _ in range(40)]

def bubble(c, x, y, r, a=1.0):
    if r <= .5: return
    circle(c, x, y, r); setc(c, hx('#BFE9FF'), .10 * a); c.fill()
    c.set_line_width(max(1.2, r * .09)); setc(c, hx('#D9F4FF'), .6 * a); circle(c, x, y, r); c.stroke()
    c.set_line_width(max(1.0, r * .12)); setc(c, (1, 1, 1, .8 * a)); c.arc(x - r * .1, y - r * .1, r * .62, math.pi * 1.05, math.pi * 1.45); c.stroke()

def dark_bg(c, t):
    setc(c, INK); c.paint()
    glow(c, 380 + 40 * math.sin(t * .6), 250, 760, ECTO, .38)
    glow(c, 930 + 30 * math.cos(t * .5), 520, 660, HAUNT, .24)
    for k in range(5):                                   # slow light shafts from above
        x0 = 120 + k * 260 + 40 * math.sin(t * .4 + k)
        g = cairo.LinearGradient(0, 0, 0, H); g.add_color_stop_rgba(0, HAUNT[0], HAUNT[1], HAUNT[2], .06); g.add_color_stop_rgba(1, HAUNT[0], HAUNT[1], HAUNT[2], 0)
        c.move_to(x0 - 30, 0); c.line_to(x0 + 30, 0); c.line_to(x0 + 140, H); c.line_to(x0 + 40, H); c.close_path(); c.set_source(g); c.fill()

def wipe(base_bgr, t):
    """bubbles rise from the bottom; below the froth, the site's dark background"""
    bgra = cv2.cvtColor(base_bgr, cv2.COLOR_BGR2BGRA); bgra[..., 3] = 255
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    p = ease_in(seg(t, T_WIPE0, T_WIPE1)) * .55 + ease(seg(t, T_WIPE0, T_WIPE1)) * .45
    fy = lerp(H + 90, -160, p)
    # the dark, revealed below a wobbling front
    c.save(); c.move_to(0, H)
    for i in range(33):
        x = i * W / 32; c.line_to(x, fy + 26 * math.sin(x * .021 + t * 6) + 14 * math.sin(x * .053 - t * 4))
    c.line_to(W, H); c.close_path(); c.clip()
    dark_bg(c, t); c.restore()
    # froth along the front, loose bubbles racing ahead of it
    for (x, dy, r) in FRONT_B:
        y = fy + dy + 26 * math.sin(x * .021 + t * 6)
        bubble(c, x + 8 * math.sin(t * 5 + r), y, r * (.8 + .2 * math.sin(t * 7 + x)))
    for (x, ph, r, sp) in LOOSE_B:
        y = fy - (ph * 520 + (t - T_WIPE0) * 260 * sp) % 760
        if -40 < y < H + 40: bubble(c, x + 10 * math.sin(t * 4 + ph * 9), y, r, .9)
    s.flush()
    return cv2.cvtColor(bgra, cv2.COLOR_BGRA2BGR)

TAG = "Pirate ghosts at 60 feet? Anything's possible."
def ending(t):
    """after the wipe: the lit dark, a few bubbles still rising, the logo, the name, the tagline"""
    bgra = np.zeros((H, W, 4), np.uint8)
    s = cairo.ImageSurface.create_for_data(bgra, cairo.FORMAT_ARGB32, W, H, W * 4); c = cairo.Context(s)
    dark_bg(c, t)
    for (x, ph, r, sp) in AFTER_B:
        y = H + 40 - ((t - T_WIPE1 + ph) * sp) % (H + 80)
        bubble(c, x + 8 * math.sin(t * 2 + ph * 5), y, r, .55)
    LX, LY, LS = 640, 262, 300
    tt = t - T_LOGO
    if tt > 1.9: glow(c, LX, LY, 280, ECTO, .3 * ease(seg(tt, 1.9, 2.5)))
    p = seg(tt, 0, .55)
    if p > 0: e = ease_out_back(p, 1.3); draw_logo_piece(c, 'arc', LX, LY, LS, dy=lerp(420, 0, e), rot=lerp(-1.2, 0, e), a=clamp(p * 3))
    p = seg(tt, .2, .75)
    if p > 0: e = ease_out_back(p, 1.3); draw_logo_piece(c, 'moon', LX, LY, LS, dy=lerp(460, 0, e), rot=lerp(1.0, 0, e), sc=lerp(1.4, 1, e), a=clamp(p * 3))
    for i in range(4):
        p = seg(tt, .75 + i * .09, 1.02 + i * .09)
        if p > 0: draw_logo_piece(c, f'win{i}', LX, LY, LS, sc=ease_out_back(p, 2.8), pivot=(.565, .615), a=clamp(p * 4))
    p = seg(tt, 1.05, 2.05)
    if p > 0:
        e = ease_out(p); wob = 1 - e
        draw_logo_piece(c, 'ghost', LX, LY, LS, dx=lerp(-560, 0, e) + math.sin(p * 9) * 26 * wob,
                        dy=lerp(260, 0, e) + math.sin(p * 7 + 1) * 60 * wob - math.sin(p * math.pi) * 40,
                        rot=math.sin(p * 8) * .26 * wob, sx=1 + math.sin(p * 11) * .07 * wob, sy=1 - math.sin(p * 11) * .07 * wob,
                        a=clamp(p * 4), pivot=(.5, .4))
    p = seg(tt, 1.95, 2.35)
    if p > 0:
        e = ease_out_back(p, 1.6)
        c.save(); c.translate(W / 2, 492 + (1 - e) * 24); c.scale(lerp(.85, 1, e), lerp(.85, 1, e)); wordmark(c, 0, 0, 60, align='c', a=clamp(p * 2)); c.restore()
    size = 28; font(c, 'Public Sans', size, True); total = text_w(c, TAG); x = W / 2 - total / 2; y = 548
    for i, ch in enumerate(TAG):
        cw = text_w(c, ch); p = seg(tt, 2.35 + i * .016, 2.35 + i * .016 + .24)
        if p > 0:
            e = ease_out_back(p, 2.2)
            c.save(); c.translate(x + cw / 2, y - size * .35 + (1 - e) * 12); c.scale(lerp(.4, 1, e), lerp(.4, 1, e))
            setc(c, BONE, clamp(p * 3)); c.move_to(-cw / 2, size * .35); font(c, 'Public Sans', size, True); c.show_text(ch); c.restore()
        x += cw
    s.flush()
    return cv2.cvtColor(bgra, cv2.COLOR_BGRA2BGR)

def frame_at(cap, n, last):
    t = n / FPS
    if n <= VEND:
        ok, fr = cap.read()
        if not ok: fr = last
        if n in TRACK: fr = composite(fr, t, TRACK[n])
    else:
        fr = last
    if t >= T_WIPE1 + .05: return ending(t), fr if n <= VEND else last
    if t >= T_WIPE0: return wipe(fr, t), fr if n <= VEND else last
    return fr, fr

def main(outp):
    cap = cv2.VideoCapture(SRC)
    p = subprocess.Popen([os.environ.get('FFMPEG', 'ffmpeg'), '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', f'{W}x{H}', '-r', str(FPS),
                          '-i', '-', '-c:v', 'libx264', '-preset', 'medium', '-crf', '17', '-pix_fmt', 'yuv420p', outp], stdin=subprocess.PIPE)
    last = None; total = int(round(T_END * FPS))
    for n in range(total):
        out, last = frame_at(cap, n, last)
        p.stdin.write(out.tobytes())
    p.stdin.close(); p.wait(); print('frames', total, 'seconds', total / FPS)

if __name__ == '__main__':
    if sys.argv[1] == 'still':
        cap = cv2.VideoCapture(SRC)
        for ts in sys.argv[2:]:
            t = float(ts); k = min(int(round(t * FPS)), VEND)
            cap.set(cv2.CAP_PROP_POS_FRAMES, k); ok, fr = cap.read()
            if k in TRACK and t <= VEND / FPS: fr = composite(fr, t, TRACK[k])
            if t >= T_WIPE1 + .05: fr = ending(t)
            elif t >= T_WIPE0: fr = wipe(fr, t)
            cv2.imwrite(os.path.join(BUILD, f'still_{ts}.png'), fr)
    else:
        main(sys.argv[1])
