"""Per frame: how much chroma green there is, and where (for choosing each scene's screen window)."""
import cv2, numpy as np, json, os, sys
SRC = os.environ.get('PO_SRC', os.path.expanduser('~/_Fun/IsHaunted/Advertisement Place Ownership.mp4'))
def green(f):
    b, g, r = [f[..., i].astype(np.int16) for i in range(3)]
    return ((g - np.maximum(r, b)) > 60) & (g > 120)
if __name__ == '__main__':
    cap = cv2.VideoCapture(SRC); out = []; n = 0
    while True:
        ok, f = cap.read()
        if not ok: break
        m = green(f); c = int(m.sum())
        if c > 150:
            ys, xs = np.nonzero(m); out.append([n, c, int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())])
        else: out.append([n, c])
        n += 1
    json.dump(out, open('build/survey.json', 'w'))
    for row in out[::6]: print(f'{row[0]/30:5.1f}', row[1:])
