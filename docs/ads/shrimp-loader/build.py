#!/usr/bin/env python3
"""
The shrimp loader's video (BenShrimpLoader): a square, seamless 79-frame loop of the shrimp on its treadmill in
the dark, IsHaunted-coloured ocean, 400x400. Writes Ben.Web.Website.Library/wwwroot/kit/shrimp-loader.mp4 and
shrimp-loader-poster.jpg.
    ~/.cache/ishaunted-ad/bin/python build.py
Sources (not in git): GREEN_SRC, the green-screen original, and OCEAN_SRC, the ocean composite, both in
~/Downloads/shrimp/ by default.
  1. key_v2.py      keys the green original: an exact shrimp-and-treadmill matte for every frame
  2. grade_ocean.py the dark grade for the water and floor (ink, indigo, brand purple, brand cyan)
  3. loader_video.py frames 0-78, the water crossfaded so it loops as cleanly as the shrimp, a square crop
"""
import os, sys, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); BUILD = os.path.join(HERE, 'build'); os.makedirs(BUILD, exist_ok=True)
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'Ben.Web.Website.Library', 'wwwroot', 'kit'); os.makedirs(OUT, exist_ok=True)
GREEN = os.environ.get('GREEN_SRC', os.path.expanduser('~/Downloads/shrimp/e70bc9a2-6113-42b7-83a3-39ba86971ab6-2026-10-07.mp4'))
env = dict(os.environ, MATTE_DIR=os.path.join(BUILD, 'matte'))
os.makedirs(env['MATTE_DIR'], exist_ok=True)
print('── matte'); subprocess.run([sys.executable, os.path.join(HERE, 'key_v2.py'), GREEN, env['MATTE_DIR']], check=True, env=env)
print('── loader video'); subprocess.run([sys.executable, os.path.join(HERE, 'loader_video.py'), os.path.join(OUT, 'shrimp-loader.mp4'),
                                       os.path.join(OUT, 'shrimp-loader-poster.jpg')], check=True, env=env, cwd=HERE)
for f in ('shrimp-loader.mp4', 'shrimp-loader-poster.jpg'):
    print(f'   {f}  {os.path.getsize(os.path.join(OUT, f)) / 1e3:.0f} KB')
