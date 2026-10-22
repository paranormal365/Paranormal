"""The receiving player's kit, red in Ben's footage, turned the blue his teammate wears (Ben, 10/22/2026).

The kicker (0-2.5 s) plays in royal blue; the generated footage dressed the player he passes to (8.3-11 s)
in red. His shirt is a big piece of very saturated, true red (hue wrapping past 0); the goalkeeper's
shirt folds are red-orange, the banners paler and the crowd's reds small. His shorts and socks are the
red pieces just below his shirt. Those pixels are turned to the kicker's blue (OpenCV hue 108), keeping
their own shading.

    ~/.cache/ishaunted-ad/bin/python kit.py     # build/comp/kit-check.jpg
"""
import os, sys
import cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import track as TR

A, B = 250, 335          # the shots where he is on screen
BLUE = 108


_last = [None, None]     # the last frame his shirt was found in, and where: a motion-blurred frame borrows it


def mask(f, n=None):
    hsv = cv2.cvtColor(f, cv2.COLOR_BGR2HSV)
    h, s, v = [hsv[..., i].astype(np.int16) for i in range(3)]
    red = ((h < 9) | (h > 172)) & (s > 165) & (v > 45) & (v < 175)
    H = f.shape[0]
    red[:int(.36 * H)] = False                                  # the crowd
    m = cv2.morphologyEx(red.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    n, lab, st, _ = cv2.connectedComponentsWithStats(m)
    keep = np.zeros_like(m)
    # his kit is true red (hue wrapping past 0) and very saturated; the goalkeeper's orange shirt runs into
    # deep red-orange in its folds, and the crowd and banners are paler or small. So: big, true-red,
    # saturated pieces are his shirt, and the redder pieces just below one (his shorts and socks) go too
    stat = []
    for k in range(1, n):
        if st[k, 4] < 150: continue
        c = lab == k
        stat.append((k, ((h[c] >= 172) | (h[c] <= 2)).mean(), np.median(s[c])))
    shirts = [k for k, wrap, sat in stat if st[k, 4] >= 2500 and wrap >= .82 and sat >= 205]
    boxes = [tuple(st[j, :4]) for j in shirts]
    if shirts: _last[:] = [n, boxes]
    elif n is not None and _last[0] is not None and 0 < n - _last[0] <= 3:
        for k, wrap, sat in stat:          # where he was a moment ago, a saturated red piece is still his shirt
            x, y, w, hh = st[k, :4]
            if sat >= 190 and any(abs(x + w / 2 - bx - bw / 2) < bw and abs(y + hh / 2 - by - bh / 2) < bh
                                  for bx, by, bw, bh in _last[1]):
                shirts.append(k); boxes.append((x, y, w, hh))
    for k, wrap, sat in stat:
        x, y, w, hh = st[k, :4]
        near = any(x + w > bx - .3 * bw and x < bx + 1.3 * bw and by + .5 * bh < y < by + 2.2 * bh
                   for bx, by, bw, bh in boxes)
        if k in shirts or (near and wrap >= .6): keep[lab == k] = 1
    keep = cv2.morphologyEx(keep, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
    return cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 1.2)


def fix_frame(n, frame):
    if not (A <= n <= B): return frame
    m = mask(frame, n)
    if m.max() < .5: return frame
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    blue = hsv.copy(); blue[..., 0] = BLUE
    blue[..., 2] = np.clip(blue[..., 2].astype(np.int16) * 1.05, 0, 255).astype(np.uint8)
    b = cv2.cvtColor(blue, cv2.COLOR_HSV2BGR).astype(np.float32)
    out = frame.astype(np.float32) * (1 - m[..., None]) + b * m[..., None]
    return np.clip(out, 0, 255).astype(np.uint8)


def check(step=6):
    tiles = []
    for n, f in TR.frames(A, B):
        if (n - A) % step: continue
        g = fix_frame(n, f); cv2.putText(g, f'{n / 30:.2f}', (10, 40), 0, 1.2, (0, 255, 255), 3)
        tiles.append(cv2.resize(g, (426, 240)))
    while len(tiles) % 5: tiles.append(np.zeros_like(tiles[0]))
    cv2.imwrite(os.path.join(HERE, 'build', 'comp', 'kit-check.jpg'),
                np.vstack([np.hstack(tiles[k:k + 5]) for k in range(0, len(tiles), 5)]))


if __name__ == '__main__':
    check()
