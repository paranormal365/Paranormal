#!/usr/bin/env python3
"""
The wizard EVP ad (43 s): a Sora 2 clip, with the green phone screen replaced by the IsHaunted EVP analyzer,
the last frame held while the scan finds its EVPs, a dark liquid in the site colours, and the logo lockup.
Scored with the strings kit in ~/Music/IsHaunted Strings (real VSCO 2 CE samples).

    ~/.cache/ishaunted-ad/bin/python build.py          # needs opencv-python-headless in that venv

Source clip: WIZ_SRC, default ~/Downloads/6a63ddcf-57b1-496b-9772-5e197287f52a-2026-10-06.mp4 (not in git).
Writes Ben.Web.Website/wwwroot/static/video/ads/wizard-{hd,sd}.mp4 and wizard-poster.jpg.
"""
import os, sys, shutil, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); BUILD = os.path.join(HERE, 'build'); os.makedirs(BUILD, exist_ok=True)
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'Ben.Web.Website', 'wwwroot', 'static', 'video', 'ads'); os.makedirs(OUT, exist_ok=True)
KIT = os.environ.get('STRINGS_KIT', os.path.expanduser('~/Music/IsHaunted Strings'))
FF = os.environ.setdefault('FFMPEG', shutil.which('ffmpeg') or '/opt/homebrew/bin/ffmpeg')
PY = sys.executable
def run(*a): subprocess.run(a, check=True)
print('── track the phone'); run(PY, os.path.join(HERE, 'track.py'))
print('── picture'); run(PY, os.path.join(HERE, 'render_evp.py'), os.path.join(BUILD, 'video.mp4'))
print('── strings score'); run(PY, os.path.join(KIT, 'example_wizard_ad.py'), os.path.join(BUILD, 'score.wav'))
print('── master'); master = os.path.join(BUILD, 'IsHaunted-EVP-Wizard-Ad.mp4')
run(FF, '-y', '-loglevel', 'error', '-i', os.path.join(BUILD, 'video.mp4'), '-i', os.path.join(BUILD, 'score.wav'),
    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '224k', '-shortest', '-movflags', '+faststart', master)
print('── web copies')
run(FF, '-y', '-loglevel', 'error', '-i', master, '-c:v', 'libx264', '-preset', 'slow', '-crf', '23', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', os.path.join(OUT, 'wizard-hd.mp4'))
run(FF, '-y', '-loglevel', 'error', '-i', master, '-vf', 'scale=-2:640', '-c:v', 'libx264', '-preset', 'slow', '-crf', '24', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '112k', '-movflags', '+faststart', os.path.join(OUT, 'wizard-sd.mp4'))
run(FF, '-y', '-loglevel', 'error', '-ss', '42.8', '-i', master, '-frames:v', '1', '-q:v', '3', os.path.join(OUT, 'wizard-poster.jpg'))
for f in sorted(os.listdir(OUT)):
    if f.startswith('wizard'): print(f'   {f}  {os.path.getsize(os.path.join(OUT, f)) / 1e6:.1f} MB')
