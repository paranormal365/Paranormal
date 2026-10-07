"""
Key out the green screen but keep the shadows, as neutral (black) semi-transparent shadows.

  1. clean plate: the backdrop's unshadowed green, a smooth fit to its upper envelope (per channel)
  2. per pixel: "is this the backdrop?" by colour balance (R/G, B/G), not brightness, so shadows count as backdrop
  3. backdrop pixels: opacity = how much darker than the clean plate they are; colour = black
  4. foreground: soft matte at edges, green spill removed; the static treadmill held solid from the median frame
Outputs straight-alpha RGBA frames.
"""
import sys, os, numpy as np, cv2

SRC = sys.argv[1]
cap = cv2.VideoCapture(SRC); frames = []
while True:
    ok, f = cap.read()
    if not ok: break
    frames.append(f.astype(np.float32))
N = len(frames); H, W = frames[0].shape[:2]
med = np.median(np.stack(frames[::2]), 0)
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
xn, yn = xx / W - .5, yy / H - .5

def greenness(img):
    b, g, r = img[..., 0], img[..., 1], img[..., 2]
    return (g - np.maximum(r, b)) / (g + 8)              # 0 grey .. ~1 pure green, brightness-independent

# ── 1) the clean plate ──────────────────────────────────────────────────────
gm = greenness(med) > .28
def basis(x, y):
    return np.stack([np.ones_like(x), x, y, x * x, x * y, y * y, x ** 3, x * x * y, x * y * y, y ** 3], -1)
A = basis(xn[gm], yn[gm])
plate = np.zeros_like(med)
sel = np.ones(gm.sum(), bool)
for it in range(6):                                       # fit, then keep only the pixels at or above it: the lit envelope
    lum = med[gm][:, 1]
    coef, *_ = np.linalg.lstsq(A[sel], lum[sel], rcond=None)
    fit = A @ coef
    sel = lum >= fit * (.985 if it < 5 else .97)
for c in range(3):
    coef, *_ = np.linalg.lstsq(A[sel], med[gm][:, c][sel], rcond=None)
    plate[..., c] = basis(xn, yn) @ coef
plate = np.clip(plate, 1, 255)
pratio = np.stack([plate[..., 2] / plate[..., 1], plate[..., 0] / plate[..., 1]], -1)   # R/G, B/G of the backdrop

# ── 4a) the treadmill does not move: a solid hold-out from the median frame ──
def fg_alpha(img):
    g = np.maximum(img[..., 1], 1)
    ratio = np.stack([img[..., 2] / g, img[..., 0] / g], -1)
    d = np.sqrt(((ratio - pratio) ** 2).sum(-1))          # colour-balance distance from the backdrop
    gn = greenness(img)
    a1 = np.clip((d - .10) / .22, 0, 1)                   # away from backdrop colour -> foreground
    a2 = np.clip((.30 - gn) / .22, 0, 1)                  # not green-dominant -> foreground
    a = np.maximum(a1, a2)
    a[img[..., 1] < 18] = 1.0                             # too dark to judge: only the treadmill's blacks are this dark
    return a
mx_, mn_ = med.max(-1), med.min(-1)
sat_ = (mx_ - mn_) / np.maximum(mx_, 1)
hold = ((fg_alpha(med) > .5) & (greenness(med) < .2) & ~((sat_ >= .35) & (mx_ >= 90))).astype(np.uint8)   # the treadmill is grey, black or
                                                                                       # blue-grey: never green, never shrimp-orange
hold = cv2.morphologyEx(hold, cv2.MORPH_OPEN, np.ones((15, 15), np.uint8))     # strip antennae and head spikes
# the shrimp walks on the belt: below the console's top edge it is part of the solid treadmill, so the belt it
# stands on is enclosed (and so solid); above the console, its head and antennae keep their soft per-frame key
cols = [np.flatnonzero(hold[250:, x])[:1] for x in range(600, 741)]
CONSOLE_TOP = 250 + int(np.median([c[0] for c in cols if len(c)])) if any(len(c) for c in cols) else 330
on_belt = (fg_alpha(med) > .5) & (yy > CONSOLE_TOP + 4) & (greenness(med) < .3)
hold = (hold | on_belt.astype(np.uint8))
# bridge thin channels (the green line between belt and rail) below the console, so reflections on the rails count as enclosed
hold = hold | (cv2.morphologyEx(hold, cv2.MORPH_CLOSE, np.ones((17, 17), np.uint8)) & (yy > CONSOLE_TOP).astype(np.uint8))
print('console top at y', CONSOLE_TOP)
hold = cv2.morphologyEx(hold, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
n_, lab, stats, _ = cv2.connectedComponentsWithStats(hold)
keep = np.zeros_like(hold)
for i in range(1, n_):
    if stats[i, cv2.CC_STAT_AREA] > 4000: keep[lab == i] = 1   # the treadmill, not specks
# fill holes (the green-tinted rail reflections): background pockets that do not reach the frame's edge
nb, blab, bstats, _ = cv2.connectedComponentsWithStats((keep == 0).astype(np.uint8))
for i in range(1, nb):
    x, y, w, h, area = bstats[i]
    if x > 0 and y > 0 and x + w < W and y + h < H and area < 250000: keep[blab == i] = 1   # incl. the shrimp's patch of belt
hold_in = cv2.erode(keep, np.ones((7, 7), np.uint8)).astype(np.float32)

# the shrimp's own colour, for unmixing thin strands: sampled from clearly-shrimp pixels above the treadmill
def excess(img): return img[..., 1] - (img[..., 2] + img[..., 0]) / 2       # green excess, linear under mixing
# sampled from the shrimp's body over the dark belt (no green shows through it there): clearly orange pixels
b_, g_, r_ = med[..., 0], med[..., 1], med[..., 2]
body = (yy > 340) & (yy < 620) & (xx > 520) & (xx < 820) & (r_ > 150) & (r_ > g_ * 1.15) & (g_ > b_)
F_ANT = np.median(med[body], 0) if body.sum() > 50 else np.array([70., 120., 200.])
print('shrimp colour (B,G,R):', F_ANT.round(0), 'from', int(body.sum()), 'px')
E_F = float(excess(F_ANT[None, None])[0, 0])
E_B = excess(plate)
# leftmost treadmill column per row: shadows left of it are dropped
# the treadmill's true left edge, from its solid body only (no shadow core, no thin bits), and nothing left of it
body_t = cv2.morphologyEx(keep, cv2.MORPH_OPEN, np.ones((31, 31), np.uint8))
nb2, lb2, st2, _ = cv2.connectedComponentsWithStats(body_t)
if nb2 > 1: body_t = np.isin(lb2, 1 + np.flatnonzero(st2[1:, cv2.CC_STAT_AREA] > 20000)).astype(np.uint8)
lefts = np.array([np.flatnonzero(body_t[y])[0] if body_t[y].any() else W for y in range(H)])
for y in range(1, H):                                                         # rows with no solid body: carry the edge down
    if lefts[y] >= W and lefts[y - 1] < W: lefts[y] = lefts[y - 1]
for y in range(401, H):                                                       # the edge cannot jump: hold it where it does
    if abs(int(lefts[y]) - int(lefts[y - 1])) > 12: lefts[y] = lefts[y - 1]
LEFT_OF_TREADMILL = (xx < (lefts[:, None] - 1)) & (yy >= 400) & (keep == 0)   # and never clear the treadmill itself
print('treadmill left edge at rows 450/550/650/750:', [int(lefts[r]) for r in (450, 550, 650, 750)])

def key(img):
    g = np.maximum(img[..., 1], 1)
    ratio = np.stack([img[..., 2] / g, img[..., 0] / g], -1)
    d = np.sqrt(((ratio - pratio) ** 2).sum(-1))                             # colour-balance distance from the backdrop
    # coverage from green excess: C = a*F + (1-a)*B  ->  a = (E_B - E_C) / (E_B - E_F)
    a_lin = np.clip((E_B - excess(img)) / np.maximum(E_B - E_F, 1), 0, 1)
    gate = np.clip((d - .035) / .055, 0, 1)                                  # backdrop colour (lit or shadowed) is never foreground
    af = a_lin * gate
    af[img[..., 1] < 18] = 1.0
    af = np.clip((af - .04) / .96, 0, 1)                                     # a dead zone for compression noise
    # drop isolated specks of noise on the backdrop (keep anything with a few pixels of structure)
    spk = ((af > .08) & (hold_in < .5)).astype(np.uint8)
    nc, lab, st, _ = cv2.connectedComponentsWithStats(spk, connectivity=8)
    small = np.isin(lab, np.flatnonzero(st[:, cv2.CC_STAT_AREA] < 6)) & (lab > 0)
    af[small] = 0
    # the shrimp's body and head are solid; only thin strands (antennae, spines) keep soft edges
    solid = cv2.morphologyEx((af > .5).astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))   # thick parts only
    nh, hl, hs, _ = cv2.connectedComponentsWithStats((solid == 0).astype(np.uint8))          # fill holes that are fully
    for i in range(1, nh):                                                                  # enclosed and small: never
        x, y, w, h, area = hs[i]                                                            # the gaps between spines
        if area < 40 and x > 0 and y > 0 and x + w < W and y + h < H: solid[hl == i] = 1
    solid = cv2.erode(solid, np.ones((3, 3), np.uint8))
    solid = cv2.GaussianBlur(cv2.dilate(solid, np.ones((3, 3), np.uint8)).astype(np.float32), (5, 5), 1.0)
    af = np.maximum(af, solid)
    af = np.maximum(af, hold_in)
    af = cv2.GaussianBlur(af, (3, 3), .6)
    # shadows: how much darker than the lit backdrop, as black; none to the left of the treadmill
    lum = img[..., 1] * .7 + img[..., 2] * .2 + img[..., 0] * .1
    plum = plate[..., 1] * .7 + plate[..., 2] * .2 + plate[..., 0] * .1
    sh = np.clip(1 - lum / plum, 0, 1)
    sh = np.clip((sh - .03) / .97, 0, 1)
    sh[LEFT_OF_TREADMILL] = 0
    sh = cv2.GaussianBlur(sh, (0, 0), 1.5)
    # foreground colour: unmix the backdrop out; faint strands lean on the shrimp's own colour so noise is not amplified
    bg = plate * (1 - sh[..., None])
    a = np.clip(af, 1e-3, 1)[..., None]
    F = np.where(af[..., None] > .98, img, (img - (1 - a) * bg) / a)
    conf = np.clip(af / .45, 0, 1)[..., None]
    F = F * conf + F_ANT * (1 - conf)
    F = np.clip(F, 0, 255)
    b, g2, r = F[..., 0], F[..., 1], F[..., 2]
    F[..., 1] = np.minimum(g2, (r + b) / 2 + 8)                              # no green: greys and blue-greys are untouched
    af[LEFT_OF_TREADMILL] = 0                                                 # nothing lives left of the treadmill
    alpha = af + (1 - af) * sh
    col = np.where(alpha[..., None] > 1e-4, (af[..., None] * F) / np.maximum(alpha, 1e-4)[..., None], 0)
    return np.clip(col, 0, 255).astype(np.uint8), np.clip(alpha * 255, 0, 255).astype(np.uint8), sh

if __name__ == '__main__':
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for i, f in enumerate(frames):
        col, al, _ = key(f)
        cv2.imwrite(os.path.join(out, f'{i:04d}.png'), np.dstack([col, al]))
    print('frames', N, 'size', W, H)
