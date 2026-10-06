"""Track the green tablet on the diver's wrist: per frame, the green region's four corners (a perspective quad),
robust to the gloved finger covering part of it, smoothed over time."""
import cv2, numpy as np, json, os
import os
HERE = os.path.dirname(os.path.abspath(__file__)); BUILD = os.path.join(HERE, 'build'); os.makedirs(BUILD, exist_ok=True)
SRC = os.environ.get('UW_SRC', os.path.expanduser('~/Downloads/Underwater-Ad.mp4'))
OUT = os.environ.get('UW_TRACK', os.path.join(BUILD, 'track.json'))

def quad(frame):
    b, g, r = [frame[..., i].astype(np.int16) for i in range(3)]
    m = ((g - np.maximum(r, b)) > 45).astype(np.uint8)
    if m.mean() < .008: return None
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
    cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    c = max(cs, key=cv2.contourArea)
    if cv2.contourArea(c) < 3000: return None
    hull = cv2.convexHull(c)                      # the finger bites into the green; the hull bridges it
    for eps in (.02, .03, .04, .05, .06):
        ap = cv2.approxPolyDP(hull, eps * cv2.arcLength(hull, True), True)
        if len(ap) == 4: break
    if len(ap) != 4:
        rect = cv2.minAreaRect(hull); ap = cv2.boxPoints(rect).reshape(-1, 1, 2)
    p = ap.reshape(4, 2).astype(float)
    # order: top-left, top-right, bottom-right, bottom-left
    s = p.sum(1); d = p[:, 0] - p[:, 1]
    tl = p[np.argmin(s)]; br = p[np.argmax(s)]; tr = p[np.argmax(d)]; bl = p[np.argmin(d)]
    return [tl.tolist(), tr.tolist(), br.tolist(), bl.tolist()], float(m.mean())

if __name__ == '__main__':
    cap = cv2.VideoCapture(SRC); n = 0; Q = {}
    while True:
        ok, f = cap.read()
        if not ok: break
        if n > 900:
            q = quad(f)
            if q: Q[n] = q
        n += 1
    keys = sorted(Q)
    P = np.array([np.array(Q[k][0]) for k in keys])           # (n, 4, 2): tl tr br bl
    tl, tr, br, bl = P[:, 0], P[:, 1], P[:, 2], P[:, 3]
    # 1) the finger covers the bottom-right corner: rebuild it from the other three plus the perspective
    #    offset measured where it is clean
    D = br - (tr + bl - tl)
    med = np.median(D, 0)
    clean = np.linalg.norm(D - med, axis=1) < 18
    for j in range(2):
        D[:, j] = np.where(clean, D[:, j], np.interp(np.arange(len(D)), np.flatnonzero(clean), D[clean, j]))
    br = tr + bl - tl + D
    # 2) swinging in from below the frame: the lower corners are clipped at the bottom edge; extend them
    #    using the tablet's height (relative to its top edge) from the first clean frames
    H = 720
    clipped = (np.maximum(bl[:, 1], br[:, 1]) > H - 6)
    ok = ~clipped
    first = np.flatnonzero(ok)[:15]
    top_len = np.linalg.norm(tr - tl, axis=1)
    hv_l = np.median((bl[first] - tl[first]) / top_len[first, None], 0)
    hv_r = np.median((br[first] - tr[first]) / top_len[first, None], 0)
    bl = np.where(clipped[:, None], tl + hv_l * top_len[:, None], bl)
    br = np.where(clipped[:, None], tr + hv_r * top_len[:, None], br)
    P = np.stack([tl, tr, br, bl], 1).reshape(len(keys), 8)
    from scipy.ndimage import median_filter, uniform_filter1d
    S = uniform_filter1d(median_filter(P, size=(5, 1), mode='nearest'), 3, axis=0, mode='nearest')
    json.dump({'n': n, 'track': {int(k): S[i].reshape(4, 2).tolist() for i, k in enumerate(keys)}}, open(OUT, 'w'))
    print('frames', n, 'green', len(keys), keys[0], keys[-1], 'finger-fixed', int((~clean).sum()), 'clipped', int(clipped.sum()))
    for i in range(0, len(keys), 25):
        k = keys[i]; print(k, round(k / 30, 2), [[round(v) for v in pt] for pt in S[i].reshape(4, 2)])
