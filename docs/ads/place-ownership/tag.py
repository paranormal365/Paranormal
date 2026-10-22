"""The name tag on the running man's back says "John Handook"; make it say "John Hancock" (Ben, 10/22/2026).

    ~/.cache/ishaunted-ad/bin/python tag.py             # track the card: build/track/tag.npz
    ~/.cache/ishaunted-ad/bin/python tag.py --check     # build/comp/tag-check.jpg

Per frame the card (cream paper on a dark coat) is found as the largest bright, colorless shape and
fitted to four corners; corners the frame's edge cuts off are rebuilt from the card's shape. Then the
card is unwarped onto a flat canvas, its last line is inpainted away (only the dark strokes in that
band, so the paper's own shading and the border stay), "John Hancock." is set in Palatino where the old
line sat, at the ink contrast the old line had in that very frame — so it fades with the card through
the dissolve into Mars — and the canvas is warped back over the card.
"""
import os, sys
import cairo, cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import track as TR

A, B = 1220, 1262                 # 40.67 s: the card rises into view; 42.07 s: gone in the dissolve
CW, CH = 820, 440                 # the flat canvas, the card's own proportions
LINE = (.07, .6, .945, .87)       # the last line's band on the card (x0, y0, x1, y1 as fractions)
TEXT = 'John Hancock.'
TEXT_X, TEXT_BASE, TEXT_W = .1, .818, .81         # the old line's start, baseline and width on the card
OUT = os.path.join(TR.OUT, 'tag.npz')


def find_card(f):
    hsv = cv2.cvtColor(f, cv2.COLOR_BGR2HSV)
    v, s = hsv[..., 2].astype(np.int16), hsv[..., 1].astype(np.int16)
    m = ((v > 95) & (s < 70)).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
    cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cs = [c for c in cs if cv2.contourArea(c) > 2500]
    if not cs: return None
    c = max(cs, key=cv2.contourArea)
    x, y, w, h = cv2.boundingRect(c)
    if w < 1.2 * h * .8 or w > 3.5 * h: return None
    hull = cv2.convexHull(c)
    for eps in (.02, .03, .04, .06):
        ap = cv2.approxPolyDP(hull, eps * cv2.arcLength(hull, True), True)
        if len(ap) == 4: break
    if len(ap) != 4: ap = cv2.boxPoints(cv2.minAreaRect(hull)).reshape(-1, 1, 2)
    q = ap.reshape(4, 2).astype(np.float64)
    c0 = q.mean(0); q = q[np.argsort(np.arctan2(q[:, 1] - c0[1], q[:, 0] - c0[0]))]
    return np.roll(q, -int(np.argmin(q.sum(1))), 0)          # TL, TR, BR, BL (the card stays upright)


def track():
    Q, N = [], []
    prev = None
    for n, f in TR.frames(A, B):
        q = find_card(f)
        if q is not None:      # the card, not a sheet on the table: big enough, and where it just was
            area = cv2.contourArea(q.astype(np.float32))
            if area < 30000 or (prev is not None and np.linalg.norm(q.mean(0) - prev.mean(0)) > 120): q = None
        if q is not None: prev = q
        N.append(n); Q.append(q if q is not None else np.full((4, 2), np.nan))
    Q = np.array(Q)
    # entering from below, the card's bottom is cut off by the frame: rebuild it from the first whole card
    Q = TR.unclip(Q[::-1], 1280, 720)[::-1]
    ok = ~np.isnan(Q[:, 0, 0]); good = np.flatnonzero(ok)
    S = Q.copy()
    for k in range(4):
        for j in range(2): S[:, k, j] = np.interp(np.arange(len(Q)), good, Q[good, k, j])
    # into the dissolve the card is too faint for the plain search: look again with a threshold of its
    # own around where it was, and failing that hold it there (it has all but stopped by then)
    last = good[-1]
    frames = dict(TR.frames(N[last], N[-1]))
    for i in range(last + 1, len(S)):
        S[i] = S[i - 1]
        f = frames.get(N[i])
        if f is None: continue
        p = S[i - 1]; x0, y0 = np.maximum(p.min(0) - 60, 0).astype(int); x1, y1 = (p.max(0) + 60).astype(int)
        g = cv2.GaussianBlur(cv2.cvtColor(f[y0:y1, x0:x1], cv2.COLOR_BGR2GRAY), (5, 5), 0)
        _, m = cv2.threshold(g, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
        full = np.zeros(f.shape[:2], np.uint8); full[y0:y1, x0:x1] = m
        q = find_card(cv2.merge([full, full, full]))
        a0 = cv2.contourArea(p.astype(np.float32))
        if q is not None and .8 < cv2.contourArea(q.astype(np.float32)) / a0 < 1.2: S[i] = q
    w = 2; pad = np.concatenate([np.repeat(S[:1], w, 0), S, np.repeat(S[-1:], w, 0)])
    ker = np.hanning(2 * w + 1); ker /= ker.sum()
    S = np.stack([np.convolve(pad[:, k, j], ker, 'same')[w:-w] for k in range(4) for j in range(2)], 1).reshape(-1, 4, 2)
    valid = np.zeros(len(S), bool); valid[good[0]:last + 2] = True     # past that, the dissolve leaves rocks for ink
    np.savez_compressed(OUT, frames=np.array(N), quad=S, ok=valid)
    print('tag', len(N), 'frames,', int(ok.sum()), 'found, fixed', int(valid.sum()))


DISSOLVE = (1248, 1257)          # the courtroom fades out over these frames, Mars in

def fade(n):
    """How much of the courtroom is left in frame n of the dissolve."""
    return float(np.clip((DISSOLVE[1] - n) / (DISSOLVE[1] - DISSOLVE[0]), 0, 1))


_t, _delta = None, None
def fix_frame(n, frame):
    global _t, _delta
    if not (A <= n <= B): return frame
    if _t is None: d = np.load(OUT); _t = (d['frames'], d['quad'], d['ok'])
    N, Q, OK = _t
    i = n - int(N[0])
    if not OK[i]:
        # too faint to find in the dissolve: the last fixed frame's correction, fading with the card
        if _delta is None or n < DISSOLVE[0] or n > DISSOLVE[1]: return frame
        dc, n0 = _delta
        q = Q[i].astype(np.float32)
        Mi = cv2.getPerspectiveTransform(np.float32([[0, 0], [CW, 0], [CW, CH], [0, CH]]), q)
        back = cv2.warpPerspective(dc, Mi, (frame.shape[1], frame.shape[0]), flags=cv2.INTER_AREA)
        return np.clip(frame + back * (fade(n) / max(fade(n0), 1e-3)), 0, 255).astype(np.uint8)
    q = Q[i].astype(np.float32)
    src = np.float32([[0, 0], [CW, 0], [CW, CH], [0, CH]])
    M = cv2.getPerspectiveTransform(q, src); Mi = np.linalg.inv(M)
    card = cv2.warpPerspective(frame, M, (CW, CH), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE).astype(np.float32)
    x0, y0, x1, y1 = int(LINE[0] * CW), int(LINE[1] * CH), int(LINE[2] * CW), int(LINE[3] * CH)
    band = card[y0:y1, x0:x1]
    gray = band.mean(2)
    paper = np.percentile(gray, 80); ink = np.percentile(gray, 3)
    contrast = max(0.0, paper - ink)
    if contrast < 6: return frame
    strokes = (gray < paper - contrast * .35).astype(np.uint8)
    strokes = cv2.dilate(strokes, np.ones((9, 9), np.uint8))
    full = np.zeros((CH, CW), np.uint8); full[y0:y1, x0:x1] = strokes
    clean = cv2.inpaint(np.clip(card, 0, 255).astype(np.uint8), full, 7, cv2.INPAINT_TELEA).astype(np.float32)
    # the new line, as coverage, set where the old one sat
    surf = cairo.ImageSurface(cairo.FORMAT_A8, CW, CH); c = cairo.Context(surf)
    c.select_font_face('Palatino', cairo.FONT_SLANT_NORMAL, cairo.FONT_WEIGHT_NORMAL)
    c.set_font_size(100); w100 = c.text_extents(TEXT).x_advance
    c.set_font_size(100 * TEXT_W * CW / w100)                  # as wide as the old line, so it sits the same
    c.move_to(TEXT_X * CW, TEXT_BASE * CH); c.show_text(TEXT); surf.flush()
    cov = np.ndarray((CH, surf.get_stride()), np.uint8, surf.get_data())[:, :CW].astype(np.float32) / 255
    # as soft as the frame draws the card: the canvas is larger than the card is on screen
    scale = np.sqrt(abs(cv2.contourArea(q)) / (CW * CH))
    k = int(.9 / max(scale, .1)) | 1
    cov = cv2.GaussianBlur(cov, (k, k), 0)
    dark = gray < paper - contrast * .6
    inkcol = np.percentile(band[dark], 25, axis=0) if dark.sum() > 20 else np.median(band, 0) - contrast
    new = clean * (1 - cov[..., None]) + inkcol * cov[..., None]
    _delta = (new - card, n)
    # back onto the card, only inside it
    back = cv2.warpPerspective(new, Mi, (frame.shape[1], frame.shape[0]), flags=cv2.INTER_AREA)
    mask = cv2.warpPerspective(np.ones((CH, CW), np.float32), Mi, (frame.shape[1], frame.shape[0]), flags=cv2.INTER_LINEAR)
    mask = cv2.erode(mask, np.ones((5, 5), np.uint8))[..., None]
    return np.clip(frame * (1 - mask) + back * mask, 0, 255).astype(np.uint8)


def check(step=3):
    tiles = []
    for n, f in TR.frames(A, B):
        if (n - A) % step: continue
        g = fix_frame(n, f)
        cv2.putText(g, f'{n / 30:.2f}', (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 255), 3)
        tiles.append(cv2.resize(g, (426, 240), interpolation=cv2.INTER_AREA))
    while len(tiles) % 4: tiles.append(np.zeros_like(tiles[0]))
    os.makedirs(os.path.join(HERE, 'build', 'comp'), exist_ok=True)
    cv2.imwrite(os.path.join(HERE, 'build', 'comp', 'tag-check.jpg'),
                np.vstack([np.hstack(tiles[k:k + 4]) for k in range(0, len(tiles), 4)]))


if __name__ == '__main__':
    if sys.argv[1:] == ['--check']: check()
    else: track()
