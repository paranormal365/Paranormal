"""
Finish the Sora wizard ad:
  1. replace the green phone screen with the IsHaunted EVP analyzer (tracked, keyed under the thumb),
  2. hold the last frame (slow push-in) so the scan has time to find its EVPs,
  3. a dark liquid in the site colours floods the screen, the logo builds piece by piece, tagline below.
Video only; the sound is made by score.py.   python3 render_evp.py out.mp4
"""
import sys, os, json, math, random, subprocess
import numpy as np, cv2, cairo
HERE = os.path.dirname(os.path.abspath(__file__)); BUILD = os.path.join(HERE, 'build')
AD = os.path.join(os.path.dirname(HERE), 'ishaunted-ad')
sys.path.insert(0, AD); os.environ.setdefault('AD_BUILD', os.path.join(AD, 'build'))   # logo layers + drawing helpers
from common import draw_logo_piece, wordmark, hx, ease, ease_out, ease_out_back, seg, clamp, lerp, glow, rrect, circle, font, text_w, setc

SRC = os.environ.get('WIZ_SRC', os.path.expanduser('~/Downloads/6a63ddcf-57b1-496b-9772-5e197287f52a-2026-10-06.mp4'))
FPS = 24; W, H = 1344, 768
TRACK = {int(k): v for k, v in json.load(open(os.path.join(BUILD, 'track.json')))['track'].items()}
G0, G1 = min(TRACK), max(TRACK)                 # green frames 787..847
T_HOLD_END = 38.4                               # the held last frame runs to here
T_LIQ = 38.25                                   # liquid starts
T_END = 43.0

INK = hx('#0C1017'); MIST = hx('#151C28'); ECTO = hx('#7C5CFF'); HAUNT = hx('#22D3EE'); BONE = hx('#E9EDF4')
FOG = hx('#98A3B4'); SUCCESS = hx('#5ACD8B')

# ── the EVP analyzer (portrait canvas 390 x 844 pt, drawn at 2x) ────────────
MARKS = [(34.00, .17, 'EVP 1', '0:07'), (34.80, .41, 'EVP 2', '0:19'), (35.60, .63, 'EVP 3', '0:31'), (36.30, .86, 'EVP 4', '0:43')]
T_TAP, T_SCAN0, T_SCAN1, T_DONE = 33.10, 33.25, 36.45, 36.60
rnd = random.Random(4)
WAVE = np.array([rnd.random() for _ in range(120)])
for _, pos, _, _ in MARKS:
    i = int(pos * 119)
    for d in range(-3, 4): WAVE[max(0, min(119, i + d))] += (1.6 - abs(d) * .4)

def analyzer(t):
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, 780, 1688); c = cairo.Context(s); c.scale(2, 2)
    c.set_source(cairo.LinearGradient(0, 0, 0, 844)); g = c.get_source()
    g.add_color_stop_rgba(0, *MIST); g.add_color_stop_rgba(1, *INK); c.paint()
    glow(c, 120, 560, 320, ECTO, .18); glow(c, 330, 760, 260, HAUNT, .10)
    # title
    font(c, 'Public Sans', 13, True); setc(c, hx('#A78BFA')); c.move_to(20, 368); c.show_text('Field Kit')
    font(c, 'Public Sans', 27, True); setc(c, BONE); c.move_to(20, 400); c.show_text('EVP Analyzer')
    font(c, 'Public Sans', 14); setc(c, FOG); c.move_to(20, 424); c.show_text('Great Hall · 0:52 recorded')
    # waveform panel
    px, py, pw, ph = 16, 448, 286, 176
    rrect(c, px, py, pw, ph, 16); setc(c, hx('#1B2433')); c.fill_preserve(); c.set_line_width(1); setc(c, (1, 1, 1, .09)); c.stroke()
    prog = ease(seg(t, T_SCAN0, T_SCAN1))
    mid = py + ph / 2 + 6
    n = len(WAVE); bw = (pw - 24) / n
    for i, a in enumerate(WAVE):
        x = px + 12 + i * bw; hgt = 8 + a * 22
        done = (i / n) <= prog
        setc(c, HAUNT if done else FOG, .95 if done else .35)
        rrect(c, x, mid - hgt / 2, max(bw - 1.2, 1), hgt, 1); c.fill()
    # playhead
    if T_SCAN0 <= t < T_DONE:
        hxp = px + 12 + prog * (pw - 24)
        c.set_line_width(2.5); setc(c, BONE); c.move_to(hxp, py + 18); c.line_to(hxp, py + ph - 14); c.stroke()
        glow(c, hxp, mid, 40, HAUNT, .5)
    # EVP markers pop as the scan passes them
    for (tm, pos, lab, at) in MARKS:
        k = seg(t, tm, tm + .28)
        if k <= 0: continue
        x = px + 12 + pos * (pw - 24); e = ease_out_back(k, 2.4)
        glow(c, x, mid, 60 * e, ECTO, .55 * (1 - seg(t, tm + .3, tm + 1.2)) + .15)
        c.set_line_width(3); setc(c, ECTO); c.move_to(x, mid + 34); c.line_to(x, py + 34 + (1 - e) * 30); c.stroke()
        circle(c, x, py + 30 + (1 - e) * 30, 7 * e); setc(c, ECTO); c.fill()
        font(c, 'Public Sans', 11, True); tw = text_w(c, lab)
        rrect(c, x - tw / 2 - 6, py + 6 + (1 - e) * 30, tw + 12, 17, 8); setc(c, ECTO, e); c.fill()
        setc(c, (1, 1, 1, e)); c.move_to(x - tw / 2, py + 18.5 + (1 - e) * 30); c.show_text(lab)
    # status / button (the button sits under his thumb: his touch starts the scan)
    bx, by, bwid, bh = 196, 560, 178, 64
    if t < T_SCAN0:
        rrect(c, bx, by, bwid, bh, 16)
        g2 = cairo.LinearGradient(bx, by, bx + bwid, by + bh); g2.add_color_stop_rgba(0, *ECTO); g2.add_color_stop_rgba(1, *HAUNT); c.set_source(g2); c.fill()
        font(c, 'Public Sans', 17, True); setc(c, (1, 1, 1, 1)); tw = text_w(c, 'Scan for EVPs'); c.move_to(bx + bwid / 2 - tw / 2, by + 39); c.show_text('Scan for EVPs')
        if t > T_TAP:
            r = seg(t, T_TAP, T_TAP + .35)
            c.set_line_width(3); setc(c, (1, 1, 1, .7 * (1 - r))); circle(c, 330, 600, 20 + r * 120); c.stroke()
    found = sum(1 for m in MARKS if t >= m[0])
    if T_SCAN0 <= t < T_DONE:
        font(c, 'Public Sans', 20, True); setc(c, BONE); c.move_to(20, 660); c.show_text(f'Analyzing… {int(prog * 100)}%')
        rrect(c, 20, 672, 270, 8, 4); setc(c, (1, 1, 1, .1)); c.fill()
        rrect(c, 20, 672, 270 * prog, 8, 4); g3 = cairo.LinearGradient(20, 0, 290, 0); g3.add_color_stop_rgba(0, *ECTO); g3.add_color_stop_rgba(1, *HAUNT); c.set_source(g3); c.fill()
        font(c, 'Public Sans', 15); setc(c, FOG); c.move_to(20, 704); c.show_text(f'{found} found so far')
    if t >= T_DONE:
        k = ease_out_back(seg(t, T_DONE, T_DONE + .35), 2.0)
        cx_, cy_ = 42, 668
        glow(c, cx_, cy_, 70, SUCCESS, .45 * k)
        circle(c, cx_, cy_, 20 * k); setc(c, SUCCESS); c.fill()
        c.set_line_width(4.5); setc(c, (1, 1, 1, k)); c.set_line_cap(cairo.LINE_CAP_ROUND); c.set_line_join(cairo.LINE_JOIN_ROUND)
        c.move_to(cx_ - 9, cy_); c.line_to(cx_ - 3, cy_ + 7); c.line_to(cx_ + 10, cy_ - 7); c.stroke()
        a = clamp(seg(t, T_DONE, T_DONE + .25))
        font(c, 'Public Sans', 26, True); setc(c, BONE, a); c.move_to(74, 678); c.show_text('4 EVPs found')
        font(c, 'Public Sans', 14); setc(c, FOG, a); c.move_to(74, 700); c.show_text('Scan complete · tap one to listen')
        # result rows slide in along the bottom (the visible part of the screen there)
        for i, (tm, pos, lab, at) in enumerate(MARKS):
            r = ease_out(seg(t, T_DONE + .12 + i * .1, T_DONE + .45 + i * .1))
            if r <= 0: continue
            yy = 728 + i * 30; xx = 20 + i * 52 + (1 - r) * 60
            rrect(c, xx, yy, 250, 24, 12); setc(c, hx('#1B2433'), r); c.fill()
            circle(c, xx + 14, yy + 12, 5); setc(c, ECTO, r); c.fill()
            font(c, 'Public Sans', 13, True); setc(c, BONE, r); c.move_to(xx + 26, yy + 17); c.show_text(lab)
            font(c, 'Public Sans', 13); setc(c, FOG, r); c.move_to(xx + 90, yy + 17); c.show_text(at)
            c.move_to(xx + 224, yy + 6); c.line_to(xx + 234, yy + 12); c.line_to(xx + 224, yy + 18); c.close_path(); setc(c, HAUNT, r); c.fill()
    s.flush()
    a = np.frombuffer(s.get_data(), np.uint8).reshape(1688, 780, 4).copy()
    return a  # BGRA premultiplied

def composite(frame, t, tr):
    """warp the analyzer onto the tracked screen and key it under the thumb"""
    ui = analyzer(t)
    s = tr['w'] / 390.0; ex = np.array(tr['ex']); ey = np.array(tr['ey']); C = np.array([tr['cx'], tr['cy']])
    o = C - 390 * s * ex - 844 * s * ey
    M = np.array([[ex[0] * s / 2, ey[0] * s / 2, o[0]], [ex[1] * s / 2, ey[1] * s / 2, o[1]]], np.float32)
    warped = cv2.warpAffine(ui, M, (W, H), flags=cv2.INTER_LINEAR, borderValue=(0, 0, 0, 0)).astype(np.float32)
    f = frame.astype(np.float32)
    b, g, r = f[..., 0], f[..., 1], f[..., 2]
    gr = g - np.maximum(r, b)
    key = np.clip((gr - 22) / 50.0, 0, 1)
    key = cv2.GaussianBlur(key, (3, 3), 0)
    # light matching: keep the real screen's falloff (it is brighter top-left)
    lum = cv2.GaussianBlur(g, (0, 0), 25); med = np.median(g[gr > 60]) if (gr > 60).any() else 200
    shade_ = np.clip(lum / med, .82, 1.12)[..., None]
    wa = warped[..., 3:4] / 255.0
    fill = np.array([INK[2], INK[1], INK[0]], np.float32) * 255
    ui_rgb = (warped[..., :3] + fill * (1 - wa)) * shade_
    a = key[..., None]
    out = f * (1 - a) + ui_rgb * a
    # despill the thumb and bezel edges, and let a little of the screen's light fall on the thumb
    near = (cv2.dilate((gr > 30).astype(np.uint8), np.ones((15, 15), np.uint8)) > 0) & (key < .5)
    gch = out[..., 1]; lim = np.maximum(out[..., 0], out[..., 2]) * 1.02
    gch[near] = np.minimum(gch[near], lim[near]); out[..., 1] = gch
    spill = cv2.GaussianBlur(ui_rgb * key[..., None], (0, 0), 18)
    out += spill * .18 * (1 - key[..., None]) * near[..., None]
    return np.clip(out, 0, 255).astype(np.uint8)

# ── the ending: dark liquid, logo, tagline ──────────────────────────────────
rng = np.random.default_rng(7)
def fbm(h, w, octaves=5, seed=1):
    r = np.random.default_rng(seed); acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        sh = (max(2, h // (64 >> o if (64 >> o) else 1)), max(2, w // (64 >> o if (64 >> o) else 1)))
        n = r.random(sh).astype(np.float32)
        acc += cv2.resize(n, (w, h), interpolation=cv2.INTER_CUBIC) * amp; tot += amp; amp *= .5
    return acc / tot
MARBLE = cv2.GaussianBlur(cv2.resize(fbm(H, W // 2, 4, 3), (W * 2, H * 2)), (0, 0), 6)
DRIPS = [(rng.uniform(0, W), rng.uniform(14, 38), rng.uniform(.5, 1.3), rng.uniform(0, .3)) for _ in range(22)]

def liquid(base, t):
    """dark ink pours down from the top; returns the frame with the liquid over it, and the coverage"""
    p = seg(t, T_LIQ, T_LIQ + 1.05)
    xs = np.arange(W, dtype=np.float32)
    front = np.full(W, -80.0, np.float32) + ease(p) * (H + 260)
    for (cx, wd, sp, dl) in DRIPS:
        q = clamp((p - dl) / (1 - dl))
        front += np.exp(-((xs - cx) / wd) ** 2) * (50 + 90 * sp) * ease(q) * (q > 0)
    front += (cv2.resize(MARBLE[:1, :], (W, 1)).ravel() - .5) * 60
    yy = np.arange(H, dtype=np.float32)[:, None]
    d = front[None, :] - yy                          # >0 inside the liquid
    cov = np.clip(d / 6.0 + .5, 0, 1)
    sh = int(t * 30) % (H)
    mar = MARBLE[sh:sh + H, int(t * 20) % W:int(t * 20) % W + W]
    if mar.shape != (H, W): mar = cv2.resize(MARBLE, (W, H))
    col = np.zeros((H, W, 3), np.float32)
    ink = np.array(INK[:3][::-1]) * 255; mist = np.array(MIST[:3][::-1]) * 255
    ecto = np.array(ECTO[:3][::-1]) * 255; haunt = np.array(HAUNT[:3][::-1]) * 255
    v = (yy / H)[..., None]
    col[:] = mist * (1 - v) + ink * v
    swirl = np.clip((mar - .55) * 3, 0, 1)[..., None]
    col += (ecto * .09) * swirl + (haunt * .04) * np.clip((.45 - mar) * 3, 0, 1)[..., None]
    rim = np.exp(-((d - 7) / 6.0) ** 2)[..., None] * (p < 1)
    col += (ecto * .55 * (1 - xs[None, :, None] / W) + haunt * .55 * (xs[None, :, None] / W)) * rim
    out = base.astype(np.float32) * (1 - cov[..., None]) + col * cov[..., None]
    return np.clip(out, 0, 255).astype(np.uint8)

TAG = "It's almost magic."
SUB = 'Automatic EVP analysis. Wand sold separately.'
def endcard(img, t):
    """the logo builds piece by piece, the ghost floats in last, tagline below"""
    s = cairo.ImageSurface.create_for_data(img, cairo.FORMAT_RGB24 if False else cairo.FORMAT_ARGB32, W, H, W * 4)
    c = cairo.Context(s)
    t0 = T_LIQ + 1.0
    LX, LY, LS = 672, 290, 320
    tt = t - t0
    if tt > 1.9:                                   # the glow goes behind the logo, so the ghost stays white
        ga = ease(seg(tt, 1.9, 2.6)); glow(c, LX, LY, 300, ECTO, .25 * ga)
    p = seg(tt, 0, .6)
    if p > 0: e = ease_out_back(p, 1.2); draw_logo_piece(c, 'arc', LX, LY, LS, dx=lerp(-700, 0, e), rot=lerp(-1.4, 0, e), a=clamp(p * 3))
    p = seg(tt, .2, .8)
    if p > 0: e = ease_out_back(p, 1.2); draw_logo_piece(c, 'moon', LX, LY, LS, dx=lerp(700, 0, e), dy=lerp(-160, 0, e), rot=lerp(1.2, 0, e), sc=lerp(1.5, 1, e), a=clamp(p * 3))
    for i in range(4):
        p = seg(tt, .75 + i * .09, 1.05 + i * .09)
        if p > 0: draw_logo_piece(c, f'win{i}', LX, LY, LS, sc=ease_out_back(p, 2.6), pivot=(.565, .615), a=clamp(p * 4))
    p = seg(tt, 1.05, 2.05)
    if p > 0:
        e = ease_out(p); wob = 1 - e
        draw_logo_piece(c, 'ghost', LX, LY, LS, dx=lerp(-620, 0, e) + math.sin(p * 9) * 30 * wob,
                        dy=lerp(240, 0, e) + math.sin(p * 7 + 1) * 70 * wob - math.sin(p * math.pi) * 45,
                        rot=math.sin(p * 8) * .28 * wob, sx=1 + math.sin(p * 11) * .07 * wob, sy=1 - math.sin(p * 11) * .07 * wob,
                        a=clamp(p * 4), pivot=(.5, .4))
    # the name, right under the logo
    p = seg(tt, 1.95, 2.35)
    if p > 0:
        e = ease_out_back(p, 1.6)
        c.save(); c.translate(W / 2, 525 + (1 - e) * 26); c.scale(lerp(.85, 1, e), lerp(.85, 1, e))
        wordmark(c, 0, 0, 64, align='c', a=clamp(p * 2)); c.restore()
    # the tagline under the name, letter by letter
    size = 30; font(c, 'Public Sans', size, True); total = text_w(c, TAG); x = W / 2 - total / 2; y = 585
    for i, ch in enumerate(TAG):
        cw = text_w(c, ch); p = seg(tt, 2.3 + i * .022, 2.3 + i * .022 + .25)
        if p > 0:
            e = ease_out_back(p, 2.2)
            c.save(); c.translate(x + cw / 2, y - size * .35 + (1 - e) * 14); c.scale(lerp(.4, 1, e), lerp(.4, 1, e))
            setc(c, BONE, clamp(p * 3)); c.move_to(-cw / 2, size * .35); font(c, 'Public Sans', size, True); c.show_text(ch); c.restore()
        x += cw
    # and the joke, quietly
    p = ease(seg(tt, 2.8, 3.15))
    if p > 0:
        font(c, 'Public Sans', 21); w1 = text_w(c, SUB)
        setc(c, FOG, p); c.move_to(W / 2 - w1 / 2, 622 + (1 - p) * 8); c.show_text(SUB)
    s.flush()

def main(outp):
    cap = cv2.VideoCapture(SRC)
    p = subprocess.Popen([os.environ.get('FFMPEG', 'ffmpeg'), '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', f'{W}x{H}', '-r', str(FPS),
                          '-i', '-', '-c:v', 'libx264', '-preset', 'medium', '-crf', '17', '-pix_fmt', 'yuv420p', outp], stdin=subprocess.PIPE)
    n = 0; last = None
    while True:
        ok, fr = cap.read()
        if not ok: break
        t = n / FPS
        if n in TRACK: fr = composite(fr, t, TRACK[n]); last_raw = None
        p.stdin.write(fr.tobytes()); last = (n, fr)
        n += 1
    # hold the last frame, with a slow push-in, while the scan finishes
    cap = cv2.VideoCapture(SRC); cap.set(cv2.CAP_PROP_POS_FRAMES, G1); ok, raw = cap.read()
    tr = TRACK[G1]
    total = int(round(T_END * FPS))
    while n < total:
        t = n / FPS
        if t < T_LIQ + 1.1:
            fr = composite(raw, t, tr)
            z = 1 + .07 * ease(seg(t, n and (G1 + 1) / FPS, T_HOLD_END))
            cx, cy = 560, 470
            M = np.array([[z, 0, cx - z * cx], [0, z, cy - z * cy]], np.float32)
            fr = cv2.warpAffine(fr, M, (W, H), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT)
            if t >= T_LIQ: fr = liquid(fr, t)
            held = fr
        else:
            fr = liquid(held, t)
        if t >= T_LIQ + .9:
            bgra = cv2.cvtColor(fr, cv2.COLOR_BGR2BGRA); bgra[..., 3] = 255
            endcard(bgra, t); fr = cv2.cvtColor(bgra, cv2.COLOR_BGRA2BGR)
        p.stdin.write(fr.tobytes()); n += 1
    p.stdin.close(); p.wait(); print('frames', n, 'seconds', n / FPS)

if __name__ == '__main__':
    if sys.argv[1] == 'still':
        cap = cv2.VideoCapture(SRC)
        for ts in sys.argv[2:]:
            t = float(ts); k = int(round(t * FPS))
            if k <= G1:
                cap.set(cv2.CAP_PROP_POS_FRAMES, k); ok, fr = cap.read()
                if k in TRACK: fr = composite(fr, t, TRACK[k])
            else:
                cap.set(cv2.CAP_PROP_POS_FRAMES, G1); ok, raw = cap.read(); fr = composite(raw, t, TRACK[G1])
                if t >= T_LIQ: fr = liquid(fr, t)
                if t >= T_LIQ + .9:
                    bgra = cv2.cvtColor(fr, cv2.COLOR_BGR2BGRA); bgra[..., 3] = 255; endcard(bgra, t); fr = cv2.cvtColor(bgra, cv2.COLOR_BGRA2BGR)
            cv2.imwrite(os.path.join(BUILD, f'still_{ts}.png'), fr)
    else:
        main(sys.argv[1])
