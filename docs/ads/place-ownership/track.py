"""Track each scene's green screen: per frame, the screen's four corners and a soft key matte.

    ~/.cache/ishaunted-ad/bin/python track.py [scene ...]      # build/track/<scene>.npz
    ~/.cache/ishaunted-ad/bin/python track.py --check scene     # build/track/<scene>-check.jpg

The corners come from the green region's convex hull (a finger biting into the green is bridged by the
hull), fitted to four points. They are kept in the DEVICE's order (its top-left first, whatever way it is
turned) by matching each frame's corners to the previous frame's, so the page stays upright on the
device as hands turn it. A frame whose fit is implausible (a hand across a whole edge, motion blur) is
filled from its neighbors; then the corners are smoothed.

The matte is the key itself: wherever the screen is green, the page shows; fingers, glare and the
bezel stay in front.
"""
import os, sys, json
import cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.environ.get('PO_SRC', os.path.expanduser('~/_Fun/IsHaunted/Advertisement Place Ownership.mp4'))
OUT = os.path.join(HERE, 'build', 'track')
FPS = 30

# scene: first frame, last frame, key threshold (green above max(red, blue)), which way is up at the
# first frame ('portrait' = the longer side vertical), smoothing window
CFG = {
    'soccer':     dict(a=222, b=268, t=40, up='landscape', win=5),
    # the iPad thrown up as he leaps: tracked afresh, the same page
    'soccer2':    dict(a=281, b=318, t=60, gmin=150, up='landscape', win=3, screen='soccer'),   # bright: not the grass
    'sistine':    dict(a=432, b=556, t=40, up='landscape', win=5),
    'rubberball': dict(a=551, b=716, t=40, up='portrait', win=5),
    'liberty':    dict(a=767, b=893, t=40, up='portrait', win=5),
    'iss':        dict(a=1030, b=1079, t=35, up='landscape', win=5),
    # not a green screen: the iPad shows a mint page of text, keyed as mint (text strokes closed over)
    'hancock':    dict(a=1141, b=1206, t=25, mode='mint', up='portrait', win=5, para=2),
    'mars':       dict(a=1258, b=1422, t=24, ft=22, up='landscape', win=7),
    'sphinx':     dict(a=1540, b=1596, t=30, up='portrait', win=5),
    'prairie':    dict(a=1660, b=1766, t=70, up='portrait', win=5),
    'surgery':    dict(a=1767, b=1943, t=35, up='portrait', win=15),
}


def frames(a, b):
    cap = cv2.VideoCapture(SRC); cap.set(cv2.CAP_PROP_POS_FRAMES, a)
    for n in range(a, b + 1):
        ok, f = cap.read()
        if not ok: break
        yield n, f


def key(f, t, mode='green', gmin=0):
    """Soft matte: 0 off-screen, 1 on clean green (or, for 'mint', on the Declaration iPad's page)."""
    b, g, r = [f[..., i].astype(np.float32) for i in range(3)]
    if mode == 'mint':
        d = np.minimum(g - r, (b - r) * 1.6) * (g > 90)
        m = np.clip((d - t * .5) / (t * .5), 0, 1)
        return m
    d = g - np.maximum(r, b)
    k = np.clip((d - t * .5) / (t * .5), 0, 1)
    if gmin: k *= np.clip((g - gmin * .85) / (gmin * .15), 0, 1)
    return k


def in_front(f):
    """For the mint page: what is in front of it is a hand or the white cuff — skin redder than green,
    the cuff bright and grey — never the page's dark text, which is neither."""
    b, g, r = [f[..., i].astype(np.float32) for i in range(3)]
    front = ((r - g > 6) | ((g - r < 12) & (r > 165))).astype(np.float32)
    front = cv2.morphologyEx(front, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    return cv2.GaussianBlur(cv2.erode(front, np.ones((3, 3), np.uint8)), (5, 5), 0)


def fit_quad(m, prev_center=None):
    mb = (m > .5).astype(np.uint8)
    mb = cv2.morphologyEx(mb, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    mb = cv2.morphologyEx(mb, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
    cs, _ = cv2.findContours(mb, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cs = [c for c in cs if cv2.contourArea(c) > 120]
    if not cs: return None, 0
    if prev_center is not None and len(cs) > 1:   # the screen, not some other green thing
        big = max(cv2.contourArea(c) for c in cs)
        cs = [c for c in cs if cv2.contourArea(c) > .25 * big]
        c = min(cs, key=lambda c: np.hypot(*(c.reshape(-1, 2).mean(0) - prev_center)))
    else:
        c = max(cs, key=cv2.contourArea)
    hull = cv2.convexHull(c)
    ap = None
    for eps in (.015, .02, .03, .04, .05, .07):
        a = cv2.approxPolyDP(hull, eps * cv2.arcLength(hull, True), True)
        if len(a) == 4: ap = a; break
    if ap is None: ap = cv2.boxPoints(cv2.minAreaRect(hull)).reshape(-1, 1, 2)
    q = ap.reshape(4, 2).astype(np.float64)
    if cv2.contourArea(q.astype(np.float32)) < 0: q = q[::-1]
    fill = cv2.contourArea(c) / max(1.0, cv2.contourArea(q.astype(np.float32)))
    return q, fill


def order_first(q, up):
    """Corners as the device's TL, TR, BR, BL at the first frame, by its declared orientation."""
    c = q.mean(0); ang = np.arctan2(q[:, 1] - c[1], q[:, 0] - c[0])
    q = q[np.argsort(ang)]                         # clockwise on screen (y down), starting near -pi (left)
    tl = np.argmin(q.sum(1)); q = np.roll(q, -tl, 0)
    top, side = np.linalg.norm(q[1] - q[0]), np.linalg.norm(q[3] - q[0])
    if (up == 'portrait') != (side > top): pass   # declared orientation disagrees with the shape: keep image order
    return q


def order_like(q, prev):
    """The cyclic order of q that best matches prev (corners move, the device's top stays its top)."""
    c = q.mean(0); ang = np.arctan2(q[:, 1] - c[1], q[:, 0] - c[0]); q = q[np.argsort(ang)]
    best = min(range(4), key=lambda k: np.linalg.norm(np.roll(q, -k, 0) - prev, axis=1).sum())
    return np.roll(q, -best, 0)


def unclip(Q, W, H, edge=4):
    """Corners the frame's edge has cut off, rebuilt from the visible ones and the device's last whole shape."""
    Q = Q.copy(); last = None
    for i, q in enumerate(Q):
        if np.isnan(q[0, 0]): continue
        cut = (q[:, 0] < edge) | (q[:, 0] > W - 1 - edge) | (q[:, 1] < edge) | (q[:, 1] > H - 1 - edge)
        if not cut.any(): last = q.copy(); continue
        if last is None or cut.sum() > 2: continue
        vis = ~cut
        # the similarity that carries the last whole shape's visible corners onto this frame's
        M, _ = cv2.estimateAffinePartial2D(last[vis].astype(np.float32), q[vis].astype(np.float32))
        if M is None: continue
        pred = (last @ M[:, :2].T) + M[:, 2]
        Q[i, cut] = pred[cut]
    return Q


def track(scene):
    cf = CFG[scene]; os.makedirs(OUT, exist_ok=True)
    Q, F, N, mattes = [], [], [], []
    prev = None
    for n, f in frames(cf['a'], cf['b']):
        m = key(f, cf['t'], cf.get('mode', 'green'), cf.get('gmin', 0))
        q, fill = fit_quad(key(f, cf.get('ft', cf['t']), cf.get('mode', 'green'), cf.get('gmin', 0)), None if prev is None else prev.mean(0))
        if q is not None:
            q = order_first(q, cf['up']) if prev is None else order_like(q, prev)
            prev = q
        Q.append(q if q is not None else np.full((4, 2), np.nan)); F.append(fill); N.append(n)
        mattes.append(cv2.resize(m, (640, 360), interpolation=cv2.INTER_AREA).astype(np.float16))
    Q = np.array(Q); F = np.array(F)
    Q = unclip(Q, f.shape[1], f.shape[0])
    if 'para' in cf:     # a hand over this corner: complete it as a parallelogram from the other three
        k = cf['para']
        Q[:, k] = Q[:, (k - 1) % 4] + Q[:, (k + 1) % 4] - Q[:, (k + 2) % 4]
    # implausible fits: low fill (hull far from the green), sudden jumps or area changes
    ok = ~np.isnan(Q[:, 0, 0]) & (F > .55)
    area = np.array([cv2.contourArea(q.astype(np.float32)) if not np.isnan(q[0, 0]) else np.nan for q in Q])
    for i in range(1, len(Q) - 1):
        if ok[i] and ok[i - 1] and ok[i + 1]:
            jump = np.abs(Q[i] - (Q[i - 1] + Q[i + 1]) / 2).max()
            if jump > 25 + .08 * np.sqrt(area[i]): ok[i] = False
    good = np.flatnonzero(ok)
    S = Q.copy()
    if len(good):
        for k in range(4):
            for j in range(2):
                S[:, k, j] = np.interp(np.arange(len(Q)), good, Q[good, k, j])
    w = cf['win']
    if w > 1:
        pad = np.concatenate([np.repeat(S[:1], w, 0), S, np.repeat(S[-1:], w, 0)])
        ker = np.hanning(2 * w + 1); ker /= ker.sum()
        S = np.stack([np.convolve(pad[:, k, j], ker, 'same')[w:-w] for k in range(4) for j in range(2)], 1).reshape(-1, 4, 2)
    np.savez_compressed(os.path.join(OUT, f'{scene}.npz'), frames=np.array(N), raw=Q, quad=S, ok=ok, fill=F)
    print(scene, len(N), 'frames,', int(ok.sum()), 'fitted cleanly')
    return N, S, ok


def check(scene, step=6):
    d = np.load(os.path.join(OUT, f'{scene}.npz')); N, S, ok = d['frames'], d['quad'], d['ok']
    tiles = []
    want = set(N[::step].tolist())
    for n, f in frames(int(N[0]), int(N[-1])):
        if n not in want: continue
        i = n - N[0]; q = S[i].astype(np.int32)
        cv2.polylines(f, [q], True, (0, 0, 255) if ok[i] else (0, 165, 255), 2)
        for k, col in enumerate([(255, 0, 0), (255, 255, 0), (255, 0, 255), (0, 255, 255)]): cv2.circle(f, tuple(q[k]), 6, col, -1)
        cv2.putText(f, f'{n / FPS:.2f}', (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 255), 3)
        tiles.append(cv2.resize(f, (426, 240)))
    while len(tiles) % 5: tiles.append(np.zeros_like(tiles[0]))
    rows = [np.hstack(tiles[k:k + 5]) for k in range(0, len(tiles), 5)]
    cv2.imwrite(os.path.join(OUT, f'{scene}-check.jpg'), np.vstack(rows), [cv2.IMWRITE_JPEG_QUALITY, 80])


if __name__ == '__main__':
    args = sys.argv[1:]
    if args and args[0] == '--check':
        for s in args[1:]: check(s)
    else:
        for s in args or CFG: track(s)
