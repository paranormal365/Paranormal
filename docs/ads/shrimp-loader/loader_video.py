"""
The shrimp loader's video: a square, seamless loop of the dark-ocean shrimp.
  frames 0-78 (the shrimp's seamless loop); the ocean behind it crossfades over the first 20 frames from the
  water that follows frame 78 (frames 79+), so the water loops as cleanly as the shrimp; the light shafts repeat
  exactly every 79 frames; a 768x768 square around the shrimp, scaled to 480x480.
    python3 loader_video.py out.mp4 poster.jpg
"""
import sys, subprocess, numpy as np, cv2
sys.argv, OUT, POSTER = ['x', 'none'], sys.argv[1], sys.argv[2]
import grade_ocean as G

N_LOOP, N_FADE = 79, 20
cap = cv2.VideoCapture(G.SRC); src = []
while True:
    ok, f = cap.read()
    if not ok: break
    src.append(f)
mattes = [cv2.imread(G.MATTE[i], cv2.IMREAD_UNCHANGED).astype(np.float32) for i in range(len(src))]

def shafts_loop(i):
    ph = 2 * np.pi * i / N_LOOP; s = np.zeros((G.H, G.W, 3), np.float32)
    for k, (x0, wdt, col) in enumerate(((.18, 70, G.HAUNT), (.42, 110, G.ECTO), (.66, 80, G.HAUNT), (.86, 130, G.ECTO))):
        cx = G.W * x0 + 40 * np.sin(ph + k * 1.7) + G.yy * .32
        band = np.exp(-((G.xx - cx) / wdt) ** 2) * np.clip(1 - G.yy / (G.H * .85), 0, 1) ** 1.6
        s += band[..., None] * col[::-1] * (.07 + .025 * np.sin(ph * 2 + k))
    return s

def frame(i):
    m = mattes[i]; a = m[..., 3:] / 255; own = src[i].astype(np.float32)
    bgsrc = own
    if i < N_FADE:                                      # the water that follows frame 78, fading into this frame's water
        alt = src[N_LOOP + i].astype(np.float32)
        a_alt = mattes[N_LOOP + i][..., 3:] / 255
        alt = np.where(a_alt > .02, own, alt)          # no ghost of the other frame's shrimp
        w = .5 - .5 * np.cos(np.pi * i / N_FADE)
        bgsrc = w * own + (1 - w) * alt
    fg = np.where(a > .98, own, m[..., :3])
    bg = G.grade(np.clip(bgsrc, 0, 255).astype(np.uint8)) + shafts_loop(i)
    out = np.clip(a * fg + (1 - a) * bg, 0, 255).astype(np.uint8)
    return out[:, 288:288 + 768]                        # the square around the shrimp

p = subprocess.Popen(['ffmpeg', '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', '768x768', '-r', '24', '-i', '-',
                      '-vf', 'scale=400:400:flags=lanczos+accurate_rnd+full_chroma_int,setsar=1',
                      '-c:v', 'libx264', '-preset', 'veryslow', '-crf', '20', '-pix_fmt', 'yuv420p', '-profile:v', 'high',
                      '-movflags', '+faststart', '-an', OUT], stdin=subprocess.PIPE)
for i in range(N_LOOP):
    f = frame(i)
    if i == 0: cv2.imwrite(POSTER, cv2.resize(f, (400, 400), interpolation=cv2.INTER_LANCZOS4), [cv2.IMWRITE_JPEG_QUALITY, 88])
    p.stdin.write(f.tobytes())
p.stdin.close(); p.wait(); print('frames', N_LOOP)
