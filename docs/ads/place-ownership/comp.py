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
GROW = {'hancock': 1.03, 'mars': 1.02}
# scenes where no other thing in frame is the screen's color: leftover screen color is recolored anywhere
LEFT_ALL = {'hancock'}
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


def comp_frame(n, frame, M=None):
    """Every scene whose window covers frame n — two of them through a dissolve. M: a 2x3 affine the
    frame has already been through (the surgery push-in), so the page is drawn sharp at the new size."""
    for scene in scenes_at(n): frame = comp_scene(scene, n, frame, M)
    return frame


def screen_quad(scene, n):
    """The screen's corners in frame n, or None."""
    N, Q = track_of(scene); i = n - int(N[0])
    return Q[i] if 0 <= i < len(Q) else None


def comp_scene(scene, n, frame, M=None):
    N, Q = track_of(scene)
    i = n - int(N[0])
    if i < 0 or i >= len(Q): return frame
    q = np.roll(Q[i], -ROT.get(scene, 0), 0).astype(np.float32)
    if scene in GROW: c = q.mean(0); q = (c + (q - c) * GROW[scene]).astype(np.float32)
    if M is not None: q = (q @ M[:, :2].T + M[:, 2]).astype(np.float32)
    sc = TR.CFG[scene].get('screen', scene)
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
    warped = cv2.warpPerspective(page, P, (W, H), flags=cv2.INTER_AREA, borderMode=cv2.BORDER_CONSTANT)
    inside = cv2.warpPerspective(np.ones((h, w), np.float32), P, (W, H), flags=cv2.INTER_LINEAR)
    inside = cv2.dilate(inside, np.ones((3, 3), np.uint8))
    near = cv2.dilate(inside, np.ones((15, 15), np.uint8))
    f = frame.astype(np.float32)
    k = TR.key(frame, TR.CFG[scene]['t'], TR.CFG[scene].get('mode', 'green'), TR.CFG[scene].get('gmin', 0))
    matte = (1 - TR.in_front(frame)) if TR.CFG[scene].get('mode') == 'mint' else k
    a = (matte * inside)[..., None]
    # the screen glows a little onto its own edge
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
