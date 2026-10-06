#!/usr/bin/env python3
"""
The underwater ad (52.5 s): a submarine, a diver, a pirate wreck, the IsHaunted EVP analyzer on the green tablet
on his wrist finding five EVPs ("Aargh" "Here" "Be" "Ye" "Booty", heard as garbled pirate ghosts), a scuba disco,
a bubble wipe to the site's dark background, the logo built to its own ditty, and the tagline.
Scored with the strings kit in ~/Music/IsHaunted Strings.

    ~/.cache/ishaunted-ad/bin/python build.py

Source clip: UW_SRC, default ~/Downloads/Underwater-Ad.mp4 (not in git). The five raw words are voice/*.opus
(espeak-ng, en-gb+m3, slow and low); the processing that makes them ghostly is in score_uw.py.
Writes Ben.Web.Website/wwwroot/static/video/ads/underwater-hd.mp4 (1280x720), underwater-sd.mp4 (960x540)
and underwater-poster.jpg.
"""
import os, sys, shutil, subprocess
HERE = os.path.dirname(os.path.abspath(__file__)); BUILD = os.path.join(HERE, 'build'); os.makedirs(os.path.join(BUILD, 'voice'), exist_ok=True)
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'Ben.Web.Website', 'wwwroot', 'static', 'video', 'ads'); os.makedirs(OUT, exist_ok=True)
FF = os.environ.setdefault('FFMPEG', shutil.which('ffmpeg') or '/opt/homebrew/bin/ffmpeg')
PY = sys.executable
def run(*a): subprocess.run(a, check=True)
print('── voices')
for i in range(1, 6):
    run(FF, '-y', '-loglevel', 'error', '-i', os.path.join(HERE, 'voice', f'raw_{i}.opus'), '-ar', '22050', '-ac', '1', os.path.join(BUILD, 'voice', f'raw_{i}.wav'))
print('── track the tablet'); run(PY, os.path.join(HERE, 'track_tab.py'))
print('── picture'); run(PY, os.path.join(HERE, 'render_uw.py'), os.path.join(BUILD, 'video.mp4'))
print('── sound'); run(PY, os.path.join(HERE, 'score_uw.py'), os.path.join(BUILD, 'score.wav'))
print('── master'); master = os.path.join(BUILD, 'IsHaunted-Underwater-Ad.mp4')
run(FF, '-y', '-loglevel', 'error', '-i', os.path.join(BUILD, 'video.mp4'), '-i', os.path.join(BUILD, 'score.wav'),
    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '224k', '-shortest', '-movflags', '+faststart', master)
print('── web copies')
run(FF, '-y', '-loglevel', 'error', '-i', master, '-c:v', 'libx264', '-preset', 'slow', '-crf', '23', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', os.path.join(OUT, 'underwater-hd.mp4'))
run(FF, '-y', '-loglevel', 'error', '-i', master, '-vf', 'scale=960:540', '-c:v', 'libx264', '-preset', 'slow', '-crf', '24', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '112k', '-movflags', '+faststart', os.path.join(OUT, 'underwater-sd.mp4'))
run(FF, '-y', '-loglevel', 'error', '-ss', '52.2', '-i', master, '-frames:v', '1', '-q:v', '3', os.path.join(OUT, 'underwater-poster.jpg'))
for f in sorted(os.listdir(OUT)):
    if f.startswith('underwater'): print(f'   {f}  {os.path.getsize(os.path.join(OUT, f)) / 1e6:.1f} MB')
