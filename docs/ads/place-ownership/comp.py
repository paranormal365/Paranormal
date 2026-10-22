"""Put the site on each scene's green screen (track.py's corners, screens.py's pages).

    comp_frame(n, frame) -> frame          used by render.py for every frame of the ad
    ~/.cache/ishaunted-ad/bin/python comp.py --check scene [step]   # build/comp/<scene>-check.jpg

The page is warped onto the screen's corners and shown wherever the screen is green (the key), so the
fingers, the bezel and any glare stay in front of it. The key's edge is despilled (no green fringe on a
fingertip), and any green left over — a screen blurred past recognition as it is thrown, a reflection —
is turned the page's own dark violet rather than left green.

Which page shows when is SCHEDULE: the taps in the footage switch it (page -> claim -> claimed -> done).
"""
import os, sys
import cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import screens as SC
import track as TR

OUT = os.path.join(HERE, 'build', 'comp')
FPS = TR.FPS

# scene: [(from second, state)], read off the taps in the footage
SCHEDULE = {
    'soccer':     [(0, 'page'), (8.05, 'claim'), (8.45, 'claimed'), (8.7, 'done')],
    'sistine':    [(0, 'page'), (15.1, 'claim'), (15.6, 'claimed'), (15.9, 'done')],
    'rubberball': [(0, 'page'), (20.9, 'claim'), (21.3, 'claimed'), (21.7, 'done')],
    'liberty':    [(0, 'page'), (27.1, 'claim'), (27.5, 'claimed'), (27.9, 'done')],
    'iss':        [(0, 'claim'), (35.0, 'claimed'), (35.3, 'done')],
    'hancock':    [(0, 'page'), (38.6, 'claim'), (38.95, 'claimed'), (39.25, 'done')],
    'mars':       [(0, 'page'), (44.6, 'claim'), (45.0, 'claimed'), (45.4, 'done')],
    'sphinx':     [(0, 'claimed'), (52.2, 'done')],
    'prairie':    [(0, 'page'), (57.0, 'claim'), (57.5, 'claimed'), (57.9, 'done')],
    'surgery':    [(0, 'done')],
}
BRIGHT = {'surgery': 1.05}
# which tracked corner is the page's top-left (quarter turns), read off each scene's device
ROT = {'soccer': 3, 'soccer2': 0, 'liberty': 1, 'prairie': 1}
# where the old screen's green light is turned violet: the whole frame where nothing else is green
# screens whose page should reach a little past the fitted edge (the fit stops short of the glass)
GROW = {'hancock': 1.03, 'mars': 1.045}
# scenes where no other thing in frame is the screen's color: leftover screen color is recolored anywhere
LEFT_ALL = {'hancock', 'soccer', 'soccer2'}
LIME = {'mars'}
RELIGHT_ALL = {'prairie', 'sistine', 'liberty', 'iss', 'sphinx', 'surgery'}                  # how bright the page is against the scene (1 = as rendered)

_tracks, _pages = {}, {}
def track_of(scene):
    if scene not in _tracks:
        d = np.load(os.path.join(TR.OUT, f'{scene}.npz')); _tracks[scene] = (d['frames'], d['quad'])
    return _tracks[scene]

def page_of(scene, state):
    k = (scene, state)
    if k not in _pages: _pages[k] = SC.screen(scene, state)
    return _pages[k]

def state_at(scene, t):
    st = SCHEDULE[scene][0][1]
    for t0, s in SCHEDULE[scene]:
        if t >= t0: st = s
    return st

def scenes_at(n):
    return [s for s, cf in TR.CFG.items() if cf['a'] <= n <= cf['b']]


def comp_frame(n, frame, M=None, src=None):
    """Every scene whose window covers frame n — two of them through a dissolve. M: a 2x3 affine the
    frame has already been through (the surgery push-in), so the page is drawn sharp at the new size."""
    for scene in scenes_at(n): frame = comp_scene(scene, n, frame, M, src)
    return frame


def screen_quad(scene, n):
    """The screen's corners in frame n, or None."""
    N, Q = track_of(scene); i = n - int(N[0])
    return Q[i] if 0 <= i < len(Q) else None


# reference brightness of a screen's green when it is lit like the page should look (per key mode)
LIT = {'green': 175.0, 'mint': 170.0}
_last_center = {}

def look_of_the_shot(scene, n, f, warped, matte, inside, q):
    """The page as the camera would have seen it on that screen, in that light.

    - light: the green records how light fell across the glass — a gradient, a shadowed end, a duller
      screen in a dark hall — so the page is shaded by the green's own brightness, smoothed (the
      green's noise is not light), relative to a screen lit full on;
    - color: a little of the scene's own cast;
    - softness and grain: the footage is soft and grainy, a rendered page is neither;
    - motion: a device moving fast is smeared along its motion, as the green around it is."""
    cf = TR.CFG[scene]; mode = cf.get('mode', 'green')
    sel = (matte > .6) & (inside > .5)
    if sel.sum() < 80: return warped
    lum = (f[..., 1] if mode == 'green' else f.mean(2)).astype(np.float32)
    # the green's brightness, filled across fingers and glare by normalized smoothing
    rad = max(9, int(.12 * np.sqrt(abs(cv2.contourArea(q)) + 1))) | 1
    num = cv2.GaussianBlur(np.where(sel, lum, 0).astype(np.float32), (0, 0), rad)
    den = cv2.GaussianBlur(sel.astype(np.float32), (0, 0), rad)
    shade = np.where(den > 1e-3, num / np.maximum(den, 1e-3), np.median(lum[sel])) / LIT[mode]
    shade = np.clip(shade, .35, 1.25)
    out = warped * shade[..., None]
    # the scene's cast: the frame's average color around the device, a tenth of the way
    ring = (cv2.dilate(inside, np.ones((61, 61), np.uint8)) > .5) & (inside < .5)
    if ring.sum() > 200:
        cast = f[ring].mean(0); cast = cast / (cast.mean() + 1e-6)
        out = out * (1 + .12 * (cast - 1))[None, None, :]
    # motion: smear along the screen's own travel since the last frame
    N_, Q_ = track_of(scene); i_ = n - int(N_[0])
    v = (Q_[min(i_ + 2, len(Q_) - 1)].mean(0) - Q_[max(i_ - 2, 0)].mean(0)) / 4 if 0 <= i_ < len(Q_) else np.zeros(2)
    if True:
        L = int(min(40, np.hypot(*v) * .5))
        if L >= 3:
            ker = np.zeros((2 * L + 1, 2 * L + 1), np.float32)
            d = v / (np.hypot(*v) + 1e-6)
            for t in np.linspace(-L, L, 4 * L + 1):
                ker[int(round(L + t * d[1])), int(round(L + t * d[0]))] = 1
            out = cv2.filter2D(out, -1, ker / ker.sum())
    # the footage's softness and grain
    out = cv2.GaussianBlur(out, (0, 0), .7)
    rng = np.random.default_rng(n * 7919 + hash(scene) % 1000)
    out = out + rng.normal(0, 2.2, out.shape[:2]).astype(np.float32)[..., None]
    return out


REFIT = []    # the tracked frames refitted from their own green, for checking

# A device rising into the frame shows only its top edge at first; there the tracking has nothing to hold.
# Those frames take the device's shape from the first frame it is whole (ref) and set it on this frame's
# own top edge of green: tilted to its slope, at its height.
ENTER = {'sistine': (427, 433, 434)}
# Thrown or tossed, a device turns faster than the tracking follows; there each frame's own outline,
# settled onto the bezel, is the better fit
OWN = {'sistine': (494, 513)}


def entering(scene, n, frame, Q, N):
    a, b, ref = ENTER[scene]
    R = Q[ref - int(N[0])].astype(np.float64)
    cf = TR.CFG[scene]
    k = TR.key(frame, cf['t'], cf.get('mode', 'green'), cf.get('gmin', 0)) > .5
    cols = np.where(k.sum(0) > 3)[0]
    if len(cols) < 50: return None
    xs = cols[(cols > np.percentile(cols, 10)) & (cols < np.percentile(cols, 90))]
    ys = np.array([np.argmax(k[:, x]) for x in xs], np.float64)
    m, c0 = np.polyfit(xs, ys, 1)
    for _ in range(2):                                  # a hand across the top edge is not the edge
        keep = np.abs(ys - (m * xs + c0)) < 6
        if keep.sum() > 20: m, c0 = np.polyfit(xs[keep], ys[keep], 1)
    tm = (R[0] + R[1]) / 2
    da = np.arctan(m) - np.arctan2(R[1, 1] - R[0, 1], R[1, 0] - R[0, 0])
    rot = np.array([[np.cos(da), -np.sin(da)], [np.sin(da), np.cos(da)]])
    q = (R - tm) @ rot.T + tm
    q[:, 1] += (m * tm[0] + c0) - tm[1]
    return q.astype(np.float32)



def comp_scene(scene, n, frame, M=None, src=None):
    N, Q = track_of(scene)
    i = n - int(N[0])
    if i < 0 or i >= len(Q): return frame
    q = Q[i]
    if scene in ENTER and ENTER[scene][0] <= n <= ENTER[scene][1]:
        e = entering(scene, n, src if src is not None else frame, Q, N)
        if e is not None: q = e
    q = np.roll(q, -ROT.get(scene, 0), 0).astype(np.float32)
    if scene in GROW: c = q.mean(0); q = (c + (q - c) * GROW[scene]).astype(np.float32)
    if M is not None: q = (q @ M[:, :2].T + M[:, 2]).astype(np.float32)
    cf = TR.CFG[scene]
    if src is not None and M is not None:   # zoomed: key the sharp original, then zoom the key with it
        key = cv2.warpAffine(cv2.GaussianBlur(TR.key(src, cf['t'], cf.get('mode', 'green'), cf.get('gmin', 0)), (0, 0), .8), M,
                             (frame.shape[1], frame.shape[0]), flags=cv2.INTER_LINEAR)
    else:
        key = TR.key(frame, cf['t'], cf.get('mode', 'green'), cf.get('gmin', 0))
    # A safeguard for every scene: where the tracked corners leave much of THIS frame's screen uncovered —
    # a screen half out of frame, half behind a shoulder, smeared by being thrown, filled in from
    # neighbors that were wrong — fit the corners from this frame's own green instead.
    if scene in OWN and OWN[scene][0] <= n <= OWN[scene][1] and M is None:
        q0 = np.roll(q, ROT.get(scene, 0), 0).astype(np.float64)
        q2, _ = TR.fit_quad(key, q0.mean(0))
        if q2 is not None:
            q2 = TR.settle(frame, key, TR.order_like(q2, q0))
            q = np.roll(q2, -ROT.get(scene, 0), 0).astype(np.float32)
    # A tracked device is refitted only when the tracking has plainly lost it (most of the green outside
    # the outline, as when the goalie throws his tablet), never from a hand merely bending the green
    if cf.get('mode') != 'mint':
        kb = key > .5; area = int(kb.sum())
        if area > 250:
            covered = np.zeros(kb.shape, np.uint8); cv2.fillConvexPoly(covered, q.astype(np.int32), 1)
            if (kb & ~covered.astype(bool)).sum() > (.5 if cf.get('planar') else .2) * area:
                if cf.get('planar') and n not in REFIT: REFIT.append(n)
                q2, _ = TR.fit_quad(key, q.mean(0))
                if q2 is not None: q = TR.order_like(q2, q).astype(np.float32)
    sc = cf.get('screen', scene)
    page = page_of(sc, state_at(sc, n / FPS)) * BRIGHT.get(sc, 1.0)
    h, w = page.shape[:2]      # which corner is the page's top-left is ROT's, never a guess from the shape
    src = np.float32([[0, 0], [w, 0], [w, h], [0, h]])
    P = cv2.getPerspectiveTransform(src, q)
    H, W = frame.shape[:2]
    # supersampled from the 2x page: blur the page in proportion to how small it is drawn
    scale = np.sqrt(abs(cv2.contourArea(q)) / (w * h))
    if scale < .5:
        k = int(.5 / max(scale, .05)) | 1
        page = cv2.GaussianBlur(page, (k, k), 0)
    # the page's own dark edge carried a little past its outline: green just beyond the outline (a fast
    # swing, a soft key edge) becomes page background rather than a colored band
    warped = cv2.warpPerspective(page, P, (W, H), flags=cv2.INTER_AREA, borderMode=cv2.BORDER_REPLICATE)
    inside = cv2.warpPerspective(np.ones((h, w), np.float32), P, (W, H), flags=cv2.INTER_LINEAR)
    grow = max(5, int(.07 * np.sqrt(abs(cv2.contourArea(q)) + 1))) | 1
    inside = cv2.dilate(inside, np.ones((grow, grow), np.uint8)) if cf.get('mode') != 'mint' else cv2.dilate(inside, np.ones((5, 5), np.uint8))
    near = cv2.dilate(inside, np.ones((15, 15), np.uint8))
    f = frame.astype(np.float32)
    k = key
    if TR.CFG[scene].get('mode') == 'mint':
        # the old page's own area (its text closed over), less the hands and cuff in front of it
        page_area = cv2.dilate(cv2.morphologyEx((k > .3).astype(np.uint8), cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8)), np.ones((3, 3), np.uint8))
        page_area = np.maximum(page_area, cv2.erode((inside > .5).astype(np.uint8), np.ones((9, 9), np.uint8)))
        matte = (1 - TR.in_front(frame, inside)) * cv2.GaussianBlur(page_area.astype(np.float32), (5, 5), 0)
    else:
        matte = k
    # the whole screen: every patch of its green that touches the device's outline gets the page (its
    # dark edge carried outward), so an outline a few pixels short never leaves a band of old screen
    rr = max(9, int(.25 * np.sqrt(abs(cv2.contourArea(q)) + 1))) | 1
    reach = cv2.dilate(inside, np.ones((rr, rr), np.uint8)) > .5
    if cf.get('mode') != 'mint':
        nl, lab = cv2.connectedComponents(((k > .3) & reach).astype(np.uint8))
        touch = np.unique(lab[(inside > .5) & (lab > 0)])
        screen = np.isin(lab, touch) & (lab > 0)
        screen = cv2.dilate(screen.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.float32)
        a = np.maximum(matte * inside, k * screen)[..., None]
    else:
        a = (matte * inside)[..., None]
    warped = look_of_the_shot(scene, n, f, warped, matte, inside, q)
    out = f * (1 - a) + warped * a
    # what green is left — the key's edge, a blurred or reflected screen — is the page's dark violet now
    lm = ((np.clip(k * (1 - inside), 0, 1) + np.clip(k * inside - a[..., 0], 0, 1)) * (1.0 if scene in LEFT_ALL else near))[..., None]
    out = out * (1 - lm) + (out * .35 + np.array([60, 30, 40], np.float32) * 1.4) * lm
    # relight: the old screen threw green light on hands, fur and faces; the new one throws violet
    if scene in RELIGHT_ALL: zone = 1.0
    else:
        r = int(.5 * np.sqrt(abs(cv2.contourArea(q)))) | 1
        zone = cv2.GaussianBlur(cv2.dilate(inside, np.ones((r, r), np.uint8)), (r | 1, r | 1), 0)
    b_, g_, r_ = out[..., 0], out[..., 1], out[..., 2]
    spill = np.clip(g_ - np.maximum(r_, b_) * .96, 0, None) * zone
    out[..., 1] = g_ - spill
    out[..., 2] = r_ + spill * .22
    out[..., 0] = b_ + spill * .38
    if scene in LIME:     # a lime screen lights its surround yellow-green: that glow goes too
        ring = cv2.dilate(inside, np.ones((25, 25), np.uint8)) > .5
        hsv = cv2.cvtColor(np.clip(out, 0, 255).astype(np.uint8), cv2.COLOR_BGR2HSV).astype(np.float32)
        lime = ring & (hsv[..., 0] >= 22) & (hsv[..., 0] <= 50) & (hsv[..., 1] > 110) & (hsv[..., 2] > 60)
        w = cv2.GaussianBlur(lime.astype(np.float32), (7, 7), 0)[..., None]
        grey = out.mean(2, keepdims=True) * .55 + np.array([40, 20, 28], np.float32) * .5
        out = out * (1 - w) + grey * w
    return np.clip(out, 0, 255).astype(np.uint8)


def check(scene, step=4):
    cf = TR.CFG[scene]; os.makedirs(OUT, exist_ok=True); tiles = []
    for n, f in TR.frames(cf['a'], cf['b']):
        if (n - cf['a']) % step: continue
        g = comp_frame(n, f)
        cv2.putText(g, f'{n / FPS:.2f}', (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 255), 3)
        tiles.append(cv2.resize(g, (426, 240), interpolation=cv2.INTER_AREA))
    while len(tiles) % 5: tiles.append(np.zeros_like(tiles[0]))
    cv2.imwrite(os.path.join(OUT, f'{scene}-check.jpg'),
                np.vstack([np.hstack(tiles[k:k + 5]) for k in range(0, len(tiles), 5)]), [cv2.IMWRITE_JPEG_QUALITY, 82])


def still(scene, t, path=None):
    n = int(round(t * FPS))
    for _, f in TR.frames(n, n):
        g = comp_frame(n, f)
    path = path or os.path.join(OUT, f'{scene}-{t:.2f}.png'); os.makedirs(OUT, exist_ok=True)
    cv2.imwrite(path, g); return path


if __name__ == '__main__':
    if sys.argv[1] == '--check':
        step = int(sys.argv[3]) if len(sys.argv) > 3 else 4
        check(sys.argv[2], step)
    elif sys.argv[1] == '--still':
        print(still(sys.argv[2], float(sys.argv[3])))
