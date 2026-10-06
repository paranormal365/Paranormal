"""Track the green phone screen: per frame, fit the left edge (L), the right edge above the thumb (R, kept
parallel to L), and the bottom edge (B). Their geometry gives an affine map from a portrait app canvas
(390 x 844 pt) onto the frame, with the canvas's bottom-right corner on the screen's bottom-right corner."""
import cv2, numpy as np, json, sys
import os
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(HERE, 'build'); os.makedirs(BUILD, exist_ok=True)
SRC = os.environ.get('WIZ_SRC', os.path.expanduser('~/Downloads/6a63ddcf-57b1-496b-9772-5e197287f52a-2026-10-06.mp4'))

def green_mask(bgr):
    b, g, r = [bgr[..., i].astype(np.int16) for i in range(3)]
    gr = g - np.maximum(r, b)
    return gr

def fit(frame):
    gr = green_mask(frame); m = gr > 60
    H, Wd = m.shape
    if m.mean() < .05: return None
    ys, lx = [], []
    for y in range(0, H, 3):
        xs = np.flatnonzero(m[y])
        if len(xs) and xs[0] > 3: ys.append(y); lx.append(xs[0])
    a, b = np.polyfit(ys, lx, 1)                       # L: x = a*y + b
    ry, rx = [], []
    for y in range(0, 90, 2):
        xs = np.flatnonzero(m[y])
        if len(xs): ry.append(y); rx.append(xs[-1])
    bR = np.median(np.array(rx) - a * np.array(ry))   # R parallel to L
    bx, by = [], []
    for x in range(800, 1040, 3):
        ys_ = np.flatnonzero(m[:, x])
        if len(ys_) and ys_[-1] < H - 3: bx.append(x); by.append(ys_[-1])
    c, d = np.polyfit(bx, by, 1)                       # B: y = c*x + d
    # corner = R ∩ B : x = a*y + bR, y = c*x + d
    y0 = (c * bR + d) / (1 - c * a); x0 = a * y0 + bR
    ex = np.array([1.0, -a]) / np.hypot(1, a)          # screen-right direction (perpendicular to L)
    if ex[0] < 0: ex = -ex
    ey = np.array([a, 1.0]) / np.hypot(1, a)           # screen-down direction (along L)
    width = abs((bR - b)) / np.hypot(1, a)             # perpendicular distance L..R
    return dict(a=a, bL=b, bR=bR, c=c, d=d, cx=x0, cy=y0, ex=ex.tolist(), ey=ey.tolist(), w=width)

if __name__ == '__main__':
    cap = cv2.VideoCapture(SRC); n = 0; out = {}
    while True:
        ok, fr = cap.read()
        if not ok: break
        if n >= 760:
            g = fit(fr)
            if g: out[n] = g
        n += 1
    print('frames', n, 'green frames', len(out), min(out), max(out))
    # smooth
    keys = sorted(out); P = np.array([[out[k]['cx'], out[k]['cy'], out[k]['w'], out[k]['a']] for k in keys])
    from scipy.ndimage import uniform_filter1d
    S = uniform_filter1d(P, 5, axis=0, mode='nearest')
    sm = {}
    for i, k in enumerate(keys):
        cx, cy, w, a = S[i]
        ex = [1 / np.hypot(1, a), -a / np.hypot(1, a)]; ey = [a / np.hypot(1, a), 1 / np.hypot(1, a)]
        sm[k] = dict(cx=cx, cy=cy, w=w, ex=ex, ey=ey)
    json.dump({'n': n, 'track': sm}, open(os.path.join(BUILD, 'track.json'), 'w'))
    for k in keys[::8]: print(k, {kk: (round(v, 1) if not isinstance(v, list) else [round(x, 3) for x in v]) for kk, v in sm[k].items()})
