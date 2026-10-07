"""
Make the water and ocean floor dark and mysterious, in the IsHaunted palette, leaving the shrimp and treadmill alone.
  matte: the shrimp+treadmill key of the green original (frame-for-frame aligned with the ocean composite)
  grade: darken, keep the caustic texture, map tones to ink -> indigo -> brand purple -> brand cyan,
         a faint glow from the surface, deeper toward the floor and the edges
  out = a * original + (1 - a) * graded        (antenna edges blend naturally)
    python3 grade_ocean.py out.mp4 | still 0 40 ...
"""
import sys, os, glob, subprocess, numpy as np, cv2
HERE = os.path.dirname(os.path.abspath(__file__))

SRC = os.environ.get('OCEAN_SRC', os.path.expanduser('~/Downloads/shrimp/shrimp-loop-ocean.mp4'))
MATTE = sorted(glob.glob(os.path.join(os.environ.get('MATTE_DIR', os.path.join(HERE, 'build', 'matte')), '*.png')))
W, H = 1344, 768
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)

def rgb(h): h = h.lstrip('#'); return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)
INK, ECTO, HAUNT = rgb('#0C1017'), rgb('#7C5CFF'), rgb('#22D3EE')
# tone -> colour, darkest to brightest (RGB)
STOPS = [(0.00, rgb('#04060B')),
         (0.10, INK),
         (0.22, rgb('#141436')),            # deep indigo
         (0.38, ECTO * .42),                # brand purple, in shadow
         (0.55, ECTO * .62 + HAUNT * .08),
         (0.72, HAUNT * .55 + ECTO * .12),  # brand cyan, dimmed: the caustic highlights
         (1.00, HAUNT * .95 + 20)]
xs = np.array([s[0] for s in STOPS]); cs = np.stack([s[1] for s in STOPS])
LUT = np.stack([np.interp(np.linspace(0, 1, 1024), xs, cs[:, c]) for c in range(3)], -1)   # 1024 x RGB

# depth: a faint glow from the surface (upper right, where the light is), darker to the floor and the edges
glow = np.exp(-(((xx - W * .78) / (W * .45)) ** 2 + ((yy + H * .1) / (H * .55)) ** 2))
vign = 1 - .45 * np.clip((((xx - W / 2) / (W * .62)) ** 2 + ((yy - H * .45) / (H * .7)) ** 2), 0, 1)
depth = (.78 + .32 * glow) * vign

def grade(bgr):
    rgbf = bgr[..., ::-1].astype(np.float32) / 255
    L = rgbf[..., 0] * .2126 + rgbf[..., 1] * .7152 + rgbf[..., 2] * .0722
    Ld = np.clip(L ** 1.45 * .78 * depth, 0, 1)                 # darker, the ripples kept
    out = LUT[(Ld * 1023).astype(np.int32)]
    out += (ECTO * .06)[None, None] * glow[..., None]           # a breath of purple where the light comes in
    return np.clip(out[..., ::-1], 0, 255)                      # back to BGR

def shafts(i):
    """faint light shafts from the surface, drifting slowly, in brand cyan and purple"""
    t = i / 24.0; s = np.zeros((H, W, 3), np.float32)
    for k, (x0, wdt, col) in enumerate(((.18, 70, HAUNT), (.42, 110, ECTO), (.66, 80, HAUNT), (.86, 130, ECTO))):
        cx = W * x0 + 40 * np.sin(t * .9 + k * 1.7) + (yy * .32)          # leaning, like light through water
        band = np.exp(-((xx - cx) / wdt) ** 2) * np.clip(1 - yy / (H * .85), 0, 1) ** 1.6
        s += band[..., None] * col[::-1] * (.07 + .025 * np.sin(t * 1.3 + k))
    return s

def frame(bgr, i):
    m = cv2.imread(MATTE[i % len(MATTE)], cv2.IMREAD_UNCHANGED).astype(np.float32)
    a = m[..., 3:] / 255
    clean = m[..., :3]                                           # the key's own, green-free foreground colour
    src = bgr.astype(np.float32)
    fg = np.where(a > .98, src, clean)                           # solid parts as they are; strands and edges, clean
    bg = grade(bgr) + shafts(i)
    return np.clip(a * fg + (1 - a) * bg, 0, 255).astype(np.uint8)

if __name__ == '__main__':
    cap = cv2.VideoCapture(SRC)
    if sys.argv[1] == 'still':
        for s in sys.argv[2:]:
            cap.set(cv2.CAP_PROP_POS_FRAMES, int(s)); ok, f = cap.read()
            cv2.imwrite(os.path.join(HERE, 'build', f'grade_{s}.png'), frame(f, int(s)))
    else:
        p = subprocess.Popen(['ffmpeg', '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', f'{W}x{H}', '-r', '24', '-i', '-',
                              '-c:v', 'libx264', '-preset', 'veryslow', '-crf', '14', '-pix_fmt', 'yuv420p', '-profile:v', 'high',
                              '-movflags', '+faststart', sys.argv[1]], stdin=subprocess.PIPE)
        i = 0
        while True:
            ok, f = cap.read()
            if not ok: break
            p.stdin.write(frame(f, i).tobytes()); i += 1
        p.stdin.close(); p.wait(); print('frames', i)
