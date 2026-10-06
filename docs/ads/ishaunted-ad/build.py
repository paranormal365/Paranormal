#!/usr/bin/env python3
"""
The thirty-second IsHaunted ad (Ben, 10/05/2026) — the one HomeAdPlayer plays on Home.

Everything in it is drawn in code (pycairo) and every sound is synthesised (numpy/scipy), so there
is nothing licensed in it and it can be rebuilt from this folder alone:

    ./setup.sh            # once: Homebrew ffmpeg + cairo, a venv in ~/.cache/ishaunted-ad, fonts
    ~/.cache/ishaunted-ad/bin/python build.py

It reads the logo from wwwroot (static/images/is-haunted-logo.svg) and the palette from the Signal
tokens copied into common.py, and writes:

    build/IsHaunted-Ad-30s.mp4                              the master (1080p, CRF 18)
    Ben.Web.Website/wwwroot/static/video/ads/anime-hd.mp4   what Home plays on wide screens
    Ben.Web.Website/wwwroot/static/video/ads/anime-sd.mp4    ...and on narrow ones
    Ben.Web.Website/wwwroot/static/video/ads/anime-poster.jpg the end card, before it loads

The QR code on the pass in the ad is real and points at https://ishaunted.com/pass/HM-31OCT-0047,
which is not a pass. Change QR data in common.py if it should go somewhere useful.

Fonts: Irish Grover (the wordmark) and Public Sans (the UI) by name. setup.sh installs them from
the site's own wwwroot/fonts files.

Scenes, timings and the tagline live in scenes.py (TAGLINE, T1..TEND); the sound in audio.py.
`python render.py still 4.3 18.9` writes single frames to build/ for checking.
"""
import os, re, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
BUILD = os.environ.setdefault('AD_BUILD', os.path.join(HERE, 'build'))
WWW = os.path.join(REPO, 'Ben.Web.Website', 'wwwroot')
OUT = os.path.join(WWW, 'static', 'video', 'ads')
FFMPEG = os.environ.setdefault('FFMPEG', shutil.which('ffmpeg') or '/opt/homebrew/bin/ffmpeg')
PY = sys.executable


def step(name):
    print(f'\n── {name}', flush=True)


def run(*args, **kw):
    subprocess.run(args, check=True, **kw)


def _svg_path(ctx, d):
    """Just enough SVG path for the logo: M L H V C S Z, absolute and relative, implicit repeats."""
    toks = re.findall(r'[MmLlHhVvCcSsZz]|-?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?', d)
    i = 0; cmd = None; x = y = sx = sy = 0.0; lc = None
    def num():
        nonlocal i
        v = float(toks[i]); i += 1; return v
    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1
            if cmd in 'Zz':
                ctx.close_path(); x, y = sx, sy; lc = None; continue
        rel = cmd.islower(); c = cmd.upper()
        ox, oy = (x, y) if rel else (0.0, 0.0)
        if c == 'M':
            x, y = ox + num(), oy + num(); ctx.move_to(x, y); sx, sy = x, y; lc = None
            cmd = 'l' if rel else 'L'
        elif c == 'L':
            x, y = ox + num(), oy + num(); ctx.line_to(x, y); lc = None
        elif c == 'H':
            x = ox + num(); ctx.line_to(x, y); lc = None
        elif c == 'V':
            y = (y if rel else 0.0) + num(); ctx.line_to(x, y); lc = None
        elif c == 'C':
            x1, y1, x2, y2 = ox + num(), oy + num(), ox + num(), oy + num()
            x, y = ox + num(), oy + num(); ctx.curve_to(x1, y1, x2, y2, x, y); lc = (x2, y2)
        elif c == 'S':
            x1, y1 = (2 * x - lc[0], 2 * y - lc[1]) if lc else (x, y)
            x2, y2 = ox + num(), oy + num(); x, y = ox + num(), oy + num()
            ctx.curve_to(x1, y1, x2, y2, x, y); lc = (x2, y2)
        else:
            raise ValueError(f'path command {cmd} not handled')


def split_logo():
    """The logo as layers the end card can fly in one at a time; the ghost comes last.
    Drawn with pycairo from the SVG's own paths (no cairosvg, so no hunting for libcairo)."""
    import cairo
    src = open(os.path.join(WWW, 'static', 'images', 'is-haunted-logo.svg')).read()
    paths = [(m.group(1), m.group(2)) for m in re.finditer(r'<path\s+fill="([^"]+)"[^>]*?\sd="([^"]+)"', src, re.S)]
    paths = [(f, d) for (f, d) in paths]
    g = re.search(r'<linearGradient[^>]*x1="([\d.\-]+)"[^>]*y1="([\d.\-]+)"[^>]*x2="([\d.\-]+)"[^>]*y2="([\d.\-]+)"'
                  r'[^>]*gradientTransform="matrix\(([^)]+)\)"', src, re.S)
    stops = re.findall(r'stop\s+offset="([\d.]+)"\s+style="stop-color:(#[0-9A-Fa-f]{6})"', src)
    a, b, c_, d_, e, f_ = (float(v) for v in g.group(5).split())
    def tf(px, py): return a * px + c_ * py + e, b * px + d_ * py + f_
    gx1, gy1 = tf(float(g.group(1)), float(g.group(2))); gx2, gy2 = tf(float(g.group(3)), float(g.group(4)))
    def hexrgb(h): return tuple(int(h[k:k + 2], 16) / 255 for k in (1, 3, 5))
    groups = {'ghost': [0, 1, 2, 3, 4, 5], 'moon': [6, 7, 8, 9, 11], 'arc': [10, 16],
              'win0': [12], 'win1': [13], 'win2': [14], 'win3': [15]}
    groups['full'] = list(range(17))
    assets = os.path.join(BUILD, 'assets'); os.makedirs(assets, exist_ok=True)
    for name, idx in groups.items():
        s = cairo.ImageSurface(cairo.FORMAT_ARGB32, 1254, 1254); ctx = cairo.Context(s)
        ctx.scale(1254 / 627, 1254 / 627)
        for k in idx:
            fill, d = paths[k]
            _svg_path(ctx, d)
            if fill.startswith('url('):
                lg = cairo.LinearGradient(gx1, gy1, gx2, gy2)
                for off, col in stops: lg.add_color_stop_rgb(float(off), *hexrgb(col))
                ctx.set_source(lg)
            else:
                ctx.set_source_rgb(*hexrgb(fill))
            ctx.fill()
        s.write_to_png(os.path.join(assets, f'logo_{name}.png'))


def main():
    os.makedirs(BUILD, exist_ok=True); os.makedirs(OUT, exist_ok=True)
    t0 = time.time()
    step('logo layers'); split_logo()
    step('soundtrack'); run(PY, os.path.join(HERE, 'audio.py'))
    step('frames (900)')
    silent = os.path.join(BUILD, 'video_noaudio.mp4')
    run(PY, os.path.join(HERE, 'render.py'), 'video', '0', '30', silent)
    step('master')
    master = os.path.join(BUILD, 'IsHaunted-Ad-30s.mp4')
    run(FFMPEG, '-y', '-loglevel', 'error', '-i', silent, '-i', os.path.join(BUILD, 'soundtrack.wav'),
        '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-shortest', '-movflags', '+faststart', master)
    step('web copies -> wwwroot/static/video')
    run(FFMPEG, '-y', '-loglevel', 'error', '-i', master, '-c:v', 'libx264', '-preset', 'slow', '-crf', '23',
        '-profile:v', 'high', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart',
        os.path.join(OUT, 'anime-hd.mp4'))
    run(FFMPEG, '-y', '-loglevel', 'error', '-i', master, '-vf', 'scale=1280:720', '-c:v', 'libx264', '-preset', 'slow',
        '-crf', '24', '-profile:v', 'high', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '112k', '-movflags', '+faststart',
        os.path.join(OUT, 'anime-sd.mp4'))
    run(FFMPEG, '-y', '-loglevel', 'error', '-ss', '29.6', '-i', master, '-frames:v', '1', '-vf', 'scale=1280:720',
        '-q:v', '3', os.path.join(OUT, 'anime-poster.jpg'))
    for f in sorted(os.listdir(OUT)):
        print(f'   {f}  {os.path.getsize(os.path.join(OUT, f)) / 1e6:.1f} MB')
    print(f'\ndone in {time.time() - t0:.0f}s')


if __name__ == '__main__':
    main()
