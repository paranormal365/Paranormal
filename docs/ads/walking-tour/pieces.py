"""The metallic IsHaunted logo (Ben's image, 10/09/2026) cut into the pieces the ending builds it from.

The image is one glowing shape on black, so the pieces are cut by what they are rather than by gaps:
  - moon:    the green arc — green pixels, and the highlights right beside them
  - windows: the four gold panes — gold pixels
  - arc:     the silver arc — inside a hand-drawn outline (its inner edge runs within a few pixels of the
             ghost's wing, and the ghost's tail sweeps past its tip, so no rule of thumb separates them)
  - ghost:   everything else in the symbol, the roof line included
  - word:    the "IsHaunted.com" wordmark under the symbol

Each piece is saved as a premultiplied BGRA image the size of the original, with alpha taken from its
brightness: the background is pure black, so brightness is how much of the metal is there.
"""
import os, sys
import cv2, numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.environ.get('WT_BUILD', os.path.join(HERE, 'build'))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(BUILD, 'logo-metal.webp')

# The silver arc's outline, in the source image's pixels (1254x1254), read off a gridded crop.
ARC = np.array([(532, 232), (430, 278), (343, 358), (306, 480), (321, 597), (369, 696), (394, 694),
                (406, 626), (386, 551), (380, 481), (398, 410), (443, 324), (522, 256)], np.int32)
WORD_TOP = 820   # the wordmark starts below this row


def cut(src=SRC, out=BUILD):
    os.makedirs(out, exist_ok=True)
    im = cv2.imread(src, cv2.IMREAD_UNCHANGED)
    bgr = im[..., :3].astype(np.float32)
    hsv = cv2.cvtColor(im[..., :3], cv2.COLOR_BGR2HSV)
    h, s, v = [hsv[..., k].astype(np.int32) for k in range(3)]

    alpha = np.clip(bgr.max(axis=2) / 255.0 * 1.35, 0, 1)
    present = alpha > 0.02

    arc = np.zeros(h.shape, np.uint8); cv2.fillPoly(arc, [ARC], 1); arc = arc.astype(bool)
    green = (h >= 35) & (h <= 90) & (s > 60) & (v > 40)
    moon = cv2.dilate(green.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool) & ~arc
    gold = (h >= 12) & (h <= 34) & (s > 70) & (v > 80)
    windows = cv2.dilate(gold.astype(np.uint8), np.ones((7, 7), np.uint8)).astype(bool)
    rows = np.arange(h.shape[0])[:, None]
    word = (rows >= WORD_TOP) & np.ones_like(arc)
    symbol = (rows < WORD_TOP) & np.ones_like(arc)

    pieces = {
        'windows': windows & symbol,
        'arc': arc & symbol & ~windows,
        'moon': moon & symbol & ~windows,
    }
    pieces['ghost'] = symbol & ~pieces['windows'] & ~pieces['arc'] & ~pieces['moon']
    pieces['word'] = word

    # The ghost's eyes and mouth are black in the image, so brightness reads them as holes and the
    # background would glow through. Fill what the ghost encloses: those stay black and solid.
    body = ((alpha > .08) & pieces['ghost']).astype(np.uint8)
    flood = body.copy(); ff = np.zeros((body.shape[0] + 2, body.shape[1] + 2), np.uint8)
    cv2.floodFill(flood, ff, (0, 0), 1)
    holes = (flood == 0)
    solid = {'ghost': holes}

    for name, m in pieces.items():
        a = (alpha * m)
        if name in solid: a = np.maximum(a, solid[name].astype(np.float32))
        a = a[..., None]
        bgra = np.concatenate([bgr * a, a * 255], axis=2).clip(0, 255).astype(np.uint8)   # premultiplied
        cv2.imwrite(os.path.join(out, f'piece-{name}.png'), bgra)
    return pieces


if __name__ == '__main__':
    p = cut()
    print({k: int(v.sum()) for k, v in p.items()})
