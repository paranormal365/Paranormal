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
    # from the cut at 7.50 the glove covers the tablet's right-hand corner while he taps: that corner is
    # completed from the other three, so the page holds still on a tablet that holds still
    # the glove covers the tablet while he taps, then his shoulder does: the outline is carried by planar
    # tracking from each shot's clearest frame, so the page holds still on a tablet that holds still
    'soccer':     dict(a=212, b=280, t=80, up='landscape', win=2, cuts=[225], edges=True, planar=True, from_=225),
    # the iPad thrown up as he leaps: tracked afresh, the same page
    'soccer2':    dict(a=281, b=332, t=80, gmin=130, up='landscape', win=3, screen='soccer'),   # bright: not the grass
    'sistine':    dict(planar=True, a=420, b=556, t=40, up='landscape', win=5),
    'rubberball': dict(planar=True, a=551, b=724, t=40, up='portrait', win=5),
    'liberty':    dict(planar=True, a=767, b=893, t=40, up='portrait', win=5),
    'iss':        dict(planar=True, cuts=[1039], a=1030, b=1079, t=35, up='landscape', win=5),
    # not a green screen: the iPad shows a mint page of text, keyed as mint (text strokes closed over)
    'hancock':    dict(planar=True, a=1141, b=1206, t=25, mode='mint', up='portrait', win=5, para=2),
    'mars':       dict(planar=True, a=1250, b=1422, t=24, ft=22, up='landscape', win=7),
    'sphinx':     dict(planar=True, a=1540, b=1596, t=30, up='portrait', win=5),
    'prairie':    dict(planar=True, a=1660, b=1766, t=70, up='portrait', win=5),
    'surgery':    dict(planar=True, a=1767, b=1943, t=35, up='portrait', win=15),
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


def _line(p, q):
    d = q - p; n = np.array([-d[1], d[0]]) / (np.hypot(*d) + 1e-9); return np.array([n[0], n[1], -(n @ p)])

def _meet(l1, l2):
    a1, b1, c1 = l1; a2, b2, c2 = l2; dt = a1 * b2 - a2 * b1
    if abs(dt) < 1e-9: return None
    return np.array([(b1 * c2 - b2 * c1) / dt, (c1 * a2 - c2 * a1) / dt])


def edge_quad(frame, m, prev, ref=None, min_pts=25, dark=80, tol=.08):
    """The screen's corners from its EDGES, and only from edges that meet the device's dark bezel.

    Where the green ends at a glove, a finger or a shoulder, that boundary says where the hand is, not
    where the screen is; following it makes the page squeeze and slide as the hand moves over a device
    that is holding still. So each boundary point looks a few pixels outward: dark there is bezel, and
    only those points place the four sides. A side with too few (hidden behind a shoulder) keeps the
    previous frame's line, moved as the opposite side moved. Returns (quad, which sides were seen)."""
    mb = (m > .5).astype(np.uint8)
    cs, _ = cv2.findContours(mb, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    if not cs: return None, None
    pts = max(cs, key=cv2.contourArea).reshape(-1, 2).astype(np.float64)
    blur = cv2.GaussianBlur(mb.astype(np.float32), (9, 9), 0)
    gx = cv2.Sobel(blur, cv2.CV_32F, 1, 0, ksize=5); gy = cv2.Sobel(blur, cv2.CV_32F, 0, 1, ksize=5)
    xi = np.clip(pts[:, 0].astype(int), 0, mb.shape[1] - 1); yi = np.clip(pts[:, 1].astype(int), 0, mb.shape[0] - 1)
    nrm = -np.stack([gx[yi, xi], gy[yi, xi]], 1); nrm /= (np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9)
    v = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY).astype(np.float32)   # a navy bezel is dark too
    outs = []
    for step in (5, 8):
        o = np.round(pts + nrm * step).astype(int)
        o[:, 0] = np.clip(o[:, 0], 0, mb.shape[1] - 1); o[:, 1] = np.clip(o[:, 1], 0, mb.shape[0] - 1)
        outs.append(v[o[:, 1], o[:, 0]])
    bezel = (np.maximum(*outs) < dark)
    # the frame's own edge is not bezel either
    bezel &= (pts[:, 0] > 3) & (pts[:, 0] < mb.shape[1] - 4) & (pts[:, 1] > 3) & (pts[:, 1] < mb.shape[0] - 4)
    lines, seen = [], []
    for k in range(4):
        a, b = prev[k], prev[(k + 1) % 4]; L = _line(a, b)
        d = np.abs(pts @ L[:2] + L[2]); seg = b - a
        t = ((pts - a) @ seg) / (seg @ seg + 1e-9)
        sel = bezel & (d < max(10, tol * np.hypot(*seg))) & (t > .05) & (t < .95)
        if sel.sum() >= min_pts:
            vx, vy, x0, y0 = cv2.fitLine(pts[sel].astype(np.float32), cv2.DIST_HUBER, 0, .01, .01).ravel()
            nl = _line(np.array([x0, y0]), np.array([x0 + vx, y0 + vy]))
            if nl[:2] @ L[:2] < 0: nl = -nl
            lines.append(nl); seen.append(True)
        else:
            lines.append(None); seen.append(False)
    hidden = [k for k in range(4) if lines[k] is None]
    if len(hidden) == 1 and ref is not None:
        # one side hidden (behind a glove, a shoulder): the other three sides are real, so the two corners
        # away from it are exact; the two on it sit along their sides at the screen's own proportions,
        # measured the last time all four sides were seen
        k = hidden[0]; a, b, c, d = k, (k + 1) % 4, (k + 2) % 4, (k + 3) % 4
        Cc = _meet(lines[b], lines[c]); Cd = _meet(lines[c], lines[d])
        if Cc is not None and Cd is not None:
            opp = np.linalg.norm(Cc - Cd); ropp = np.linalg.norm(ref[c] - ref[d]) + 1e-9
            def along(line, frm, toward, length):
                v = np.array([line[1], -line[0]])
                if v @ (toward - frm) < 0: v = -v
                return frm + v * length
            Cb = along(lines[b], Cc, prev[b], opp * np.linalg.norm(ref[b] - ref[c]) / ropp)
            Ca = along(lines[d], Cd, prev[a], opp * np.linalg.norm(ref[a] - ref[d]) / ropp)
            q = np.zeros((4, 2)); q[a], q[b], q[c], q[d] = Ca, Cb, Cc, Cd
            if cv2.isContourConvex(q.astype(np.float32)): return q, seen
    for k in range(4):                       # a hidden side moves as the side opposite it moved
        if lines[k] is None:
            L = _line(prev[k], prev[(k + 1) % 4]); o = lines[(k + 2) % 4]
            if o is not None:
                po = _line(prev[(k + 2) % 4], prev[(k + 3) % 4])
                # the opposite side's shift along this side's normal, from its old line to its new one
                mid = (prev[(k + 2) % 4] + prev[(k + 3) % 4]) / 2
                shift = -(o @ np.array([mid[0], mid[1], 1]))   # signed distance from old midpoint to new line
                L = L.copy(); L[2] -= shift * (L[:2] @ o[:2])
            lines[k] = L
    q = [_meet(lines[(k - 1) % 4], lines[k]) for k in range(4)]
    if any(c is None for c in q): return None, None
    q = np.array(q)
    if not cv2.isContourConvex(q.astype(np.float32)) or np.abs(q - prev).max() > 120: return None, None
    return q, seen


def settle(frame, m, q, steps=(.25, .18, .12, .08, .08, .08)):
    """An outline settled onto the device's bezel: each side refitted from bezel points only, the
    search narrowing. A hand over a corner bends the green's outline, never the bezel's."""
    for tol in steps:
        qe, _ = edge_quad(frame, m, q, None, tol=tol)
        if qe is not None and np.abs(qe - q).max() < 120: q = qe
    return q


def planar(frames_bgr, raw, keys, start, start_q, fill=None):
    """Carry the screen's outline through a shot, frame to frame, following the device.

    Each frame is predicted from the last by optical flow on a thin band round the screen — its bezel
    and the hands holding it, never the background behind and never the flat green — with a robust
    fit (RANSAC drops a tapping finger, which moves on its own). The prediction then snaps to the dark
    bezel wherever the bezel shows, and gives way to the frame's own fit wherever that fit is plainly
    reliable (every corner sits in clean green). Forward and backward from the shot's best frame."""
    n = len(frames_bgr); out = [None] * n; out[start] = start_q.copy(); ref = [start_q.copy()]
    gray = [cv2.cvtColor(f, cv2.COLOR_BGR2GRAY) for f in frames_bgr]
    clean = [raw[i] is not None and not np.isnan(raw[i][0, 0]) and (fill is None or fill[i] > .9)
             and (corner_support(keys[i], raw[i]) > .7).all() for i in range(n)]
    def step(i, j):
        q = out[i]
        band = np.zeros(gray[i].shape, np.uint8)
        side = max(12, int(.06 * np.sqrt(abs(cv2.contourArea(q.astype(np.float32))) + 1)))
        cv2.polylines(band, [np.round(q).astype(np.int32)], True, 255, 2 * side)
        band[keys[i] > .4] = 0
        p0 = cv2.goodFeaturesToTrack(gray[i], 300, .01, 5, mask=band)
        pred = q.copy()
        if p0 is not None and len(p0) >= 8:
            p1, st, _ = cv2.calcOpticalFlowPyrLK(gray[i], gray[j], p0, None, winSize=(21, 21), maxLevel=3)
            good = st.ravel() == 1
            if good.sum() >= 8:
                A, _ = cv2.estimateAffine2D(p0[good], p1[good], method=cv2.RANSAC, ransacReprojThreshold=2.0)
                if A is not None: pred = q @ A[:, :2].T + A[:, 2]
        qe, seen = edge_quad(frames_bgr[j], keys[j], pred, ref[0], tol=.12)
        if qe is not None and (np.abs(qe - pred).max() < 30 or (all(seen) and np.abs(qe - pred).max() < 90)):
            if all(seen): ref[0] = qe.copy()
            pred = qe
        if clean[j]:
            r = order_like(raw[j], pred)
            if np.abs(r - pred).max() < 60: pred = .25 * pred + .75 * r
        # lost (a fast move the flow could not follow): most of this frame's green is outside the
        # outline, so take the frame's own fit and carry on from there
        kb = keys[j] > .5; area = kb.sum()
        if area > 300 and raw[j] is not None and not np.isnan(raw[j][0, 0]):
            m = np.zeros(kb.shape, np.uint8); cv2.fillConvexPoly(m, np.round(pred).astype(np.int32), 1)
            qa = abs(cv2.contourArea(pred.astype(np.float32)))
            if (kb & ~m.astype(bool)).sum() > .25 * area:
                pred = settle(frames_bgr[j], keys[j], order_like(raw[j], pred)); ref[0] = pred.copy()
        out[j] = pred
    for i in range(start, n - 1): step(i, i + 1)
    for i in range(start, 0, -1): step(i, i - 1)
    return np.array(out)


def corner_support(m, q, inset=.1, r=.035):
    """How much clean green is just inside each corner (0..1). Screens have rounded corners, so the
    probe sits a little in from the corner, toward the middle; a corner under a glove or a shoulder has
    little green there, and its fitted position is a guess."""
    H, W = m.shape; c = q.mean(0); out = []
    rad = max(3, int(r * np.sqrt(abs(cv2.contourArea(q.astype(np.float32))) + 1)))
    for k in range(4):
        p = q[k] + (c - q[k]) * inset
        mask = np.zeros((H, W), np.uint8); cv2.circle(mask, (int(round(p[0])), int(round(p[1]))), rad, 1, -1)
        a = mask.sum(); out.append(float((m * mask).sum() / a) if a else 0.0)
    return np.array(out)


def steady(Q, M, N, cuts, smooth=3.0):
    """Corners that follow the device, not the hands on it: untrusted corners (little green around
    them) are dropped, a single dropped corner is completed from the other three, and each corner's
    path is a stiff smoothing spline through what is left, shot by shot."""
    from scipy.interpolate import UnivariateSpline
    T = np.full(Q.shape[:2], False)
    for i, (q, m) in enumerate(zip(Q, M)):
        if np.isnan(q[0, 0]): continue
        sup = corner_support(m.astype(np.float32), q * (m.shape[1] / 1280))
        T[i] = sup > .55
    P = np.where(T[..., None], Q, np.nan)
    for i in range(len(P)):                       # one corner covered: it is where the other three say
        if T[i].sum() == 3:
            k = int(np.flatnonzero(~T[i])[0])
            P[i, k] = P[i, (k - 1) % 4] + P[i, (k + 1) % 4] - P[i, (k + 2) % 4]
    S = Q.copy()
    bounds = [0] + [c - N[0] for c in cuts if N[0] < c <= N[-1]] + [len(Q)]
    for s0, s1 in zip(bounds[:-1], bounds[1:]):
        t = np.arange(s0, s1)
        for k in range(4):
            for j in range(2):
                v = P[s0:s1, k, j]; ok = ~np.isnan(v)
                if ok.sum() >= 6:
                    sp = UnivariateSpline(t[ok], v[ok], k=3, s=ok.sum() * smooth ** 2)
                    S[s0:s1, k, j] = sp(t)
                elif ok.sum() >= 1:
                    S[s0:s1, k, j] = np.interp(t, t[ok], v[ok])
    return S, T


def track(scene):
    cf = CFG[scene]; os.makedirs(OUT, exist_ok=True)
    Q, F, N, mattes = [], [], [], []
    prev = ref = None
    for n, f in frames(cf['a'], cf['b']):
        m = key(f, cf['t'], cf.get('mode', 'green'), cf.get('gmin', 0))
        q, fill = fit_quad(key(f, cf.get('ft', cf['t']), cf.get('mode', 'green'), cf.get('gmin', 0)), None if prev is None else prev.mean(0))
        if q is not None:
            q = order_first(q, cf['up']) if prev is None else order_like(q, prev)
            # refined from the bezel: the sides that meet the dark bezel place the screen, and a hidden one
            # is placed by the screen's shape (ref: the last frame all four sides were seen). A cut, or a
            # refinement that jumps, starts again from the plain fit, so a wrong lock never carries on.
            if cf.get('edges') and prev is not None and n not in cf.get('cuts', []) and n >= cf.get('from_', 0):
                qe, seen = edge_quad(f, key(f, cf.get('ft', cf['t']), cf.get('mode', 'green'), cf.get('gmin', 0)), prev, ref)
                if qe is not None and np.abs(qe - prev).max() < 60:
                    q = qe
                    if all(seen): ref = qe.copy()
            elif cf.get('edges'):
                ref = None
            prev = q
        Q.append(q if q is not None else np.full((4, 2), np.nan)); F.append(fill); N.append(n)
        mattes.append(cv2.resize(m, (640, 360), interpolation=cv2.INTER_AREA).astype(np.float16))
    Q = np.array(Q); F = np.array(F)
    Q = unclip(Q, f.shape[1], f.shape[0])
    if cf.get('planar'):
        imgs = [f2 for _, f2 in frames(cf['a'], cf['b'])]
        keys_ = [key(f2, cf['t'], cf.get('mode', 'green'), cf.get('gmin', 0)) for f2 in imgs]
        bounds = [0] + [c - N[0] for c in cf.get('cuts', []) if N[0] < c <= N[-1]] + [len(Q)]
        for s0, s1 in zip(bounds[:-1], bounds[1:]):
            if s0 + N[0] < cf.get('from_', 0): continue        # shots before it fit fine on their own
            st = cf.get('planar_start', {}).get(s0 + N[0])
            if st is None:     # the shot's best frame: the most green, cleanly fitted, wholly in the picture
                H_, W_ = imgs[0].shape[:2]
                inside = lambda q: (q[:, 0] > 6).all() and (q[:, 0] < W_ - 7).all() and (q[:, 1] > 6).all() and (q[:, 1] < H_ - 7).all()
                cand = [i for i in range(s0, s1) if not np.isnan(Q[i, 0, 0]) and inside(Q[i])]
                if not cand: continue
                i0 = max(cand, key=lambda i: F[i] * min(1.0, corner_support(keys_[i], Q[i]).min() / .7) * cv2.contourArea(Q[i].astype(np.float32)))
                q0 = Q[i0]
            else:
                i0, q0 = st[0] - N[0], np.array(st[1], float)
            # settle the starting outline onto the bezel first: a hand over a corner bends the green's
            # outline, never the bezel's
            q0 = settle(imgs[i0], keys_[i0], q0)
            P = planar(imgs[s0:s1], list(Q[s0:s1]), keys_[s0:s1], i0 - s0, q0, F[s0:s1])
            # slow drift taken out where the device's whole screen is plainly visible: the difference from
            # the plain fit there, smoothed over a second, so it corrects drift but carries no jitter
            raw = Q[s0:s1]; D = np.full(P.shape, np.nan)
            for i in range(len(P)):
                if np.isnan(raw[i, 0, 0]) or F[s0 + i] < .9: continue
                if (corner_support(keys_[s0 + i], raw[i]) > .7).all() and np.abs(raw[i] - P[i]).max() < 40:
                    D[i] = raw[i] - P[i]
            ok_ = ~np.isnan(D[:, 0, 0])
            if False and ok_.sum() >= 3:   # superseded: planar() anchors to every reliable frame itself
                t = np.arange(len(P)); w = 15; ker = np.hanning(2 * w + 1)
                for k in range(4):
                    for j in range(2):
                        v = np.interp(t, t[ok_], D[ok_, k, j]); wt = np.convolve(ok_.astype(float), ker, 'same') + 1e-6
                        vv = np.convolve(np.where(ok_, D[:, k, j], 0), ker, 'same') / wt
                        P[:, k, j] += np.where(wt > .5, vv, v)
            Q[s0:s1] = P
        F[max(0, cf.get('from_', 0) - N[0]):] = 1.0
    if 'para' in cf:     # a hand over this corner: complete it as a parallelogram from the other three
        k = cf['para']; j0 = max(0, cf.get('para_from', N[0]) - N[0])
        Q[j0:, k] = Q[j0:, (k - 1) % 4] + Q[j0:, (k + 1) % 4] - Q[j0:, (k + 2) % 4]
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
    if cf.get('steady'):
        M = [cv2.resize(mm.astype(np.float32), (1280, 720)) for mm in mattes]
        S, trusted = steady(Q, M, np.array(N), cf.get('cuts', []))
        print(scene, 'corners trusted', int(trusted.sum()), 'of', trusted.size)
    # smoothed shot by shot: never across a cut, or the page drifts from one shot's tablet to the next
    w = 0 if (cf.get('steady') or cf.get('planar')) else cf['win']
    if w > 1:
        bounds = [0] + [c - N[0] for c in cf.get('cuts', []) if N[0] < c <= N[-1]] + [len(S)]
        ker = np.hanning(2 * w + 1); ker /= ker.sum()
        for s0, s1 in zip(bounds[:-1], bounds[1:]):
            seg = S[s0:s1]
            pad = np.concatenate([np.repeat(seg[:1], w, 0), seg, np.repeat(seg[-1:], w, 0)])
            S[s0:s1] = np.stack([np.convolve(pad[:, k, j], ker, 'same')[w:-w] for k in range(4) for j in range(2)], 1).reshape(-1, 4, 2)
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
