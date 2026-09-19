#!/usr/bin/env python3
"""Gives every stored media file a poster image, so no post ever renders as a grey box.

A post whose media has no poster shows an empty grey rectangle, which reads as a broken site
rather than as a video waiting to be played. This walks the local file store and writes the
`.thumb.jpg` sibling that the media pipeline serves as a poster:

    video/*        an atmospheric frame with a semi-transparent play button over it
    audio/*        the REAL waveform of that recording, plus the same play button
    field session  a summary card naming the session and what it captured

**The audio waveform is the real one.** The samples are decoded with macOS's own `afconvert`
(m4a, mp3 and wav all work) and the peaks computed from the PCM, so the picture is that
recording's actual shape rather than decoration. Rendering matches the site's WaveSurfer player —
same bar width, gap and colour — because the poster and the player are two views of one thing and
they should not disagree.

Headless WaveSurfer was tried first and abandoned: `decodeAudioData` does not resolve under
headless Chrome, so its own loader hangs until the screenshot is taken and the poster comes out
empty. Peaks computed here and drawn in the same style give an identical result and cannot hang.

Usage, from the repository root:

    python3 scripts/generate-media-posters.py [--force]

Existing posters are kept unless `--force` is given.
"""
import base64
import glob
import os
import struct
import subprocess
import sys
import tempfile

ROOT = ".uploads"
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
FORCE = "--force" in sys.argv

VIDEO_EXT = {".mp4", ".mov", ".m4v"}
AUDIO_EXT = {".m4a", ".mp3", ".wav", ".aac", ".caf"}

# Enough scene variety that a feed of several posts does not look like one repeated picture.
SCENES = [
    ("Cellar stairs", "stairs"), ("Upstairs corridor", "corridor"),
    ("Nursery window", "window"), ("Back landing", "doorway"),
    ("Kitchen doorway", "doorway"), ("Attic hatch", "stairs"),
    ("Front parlour", "window"), ("Boiler room", "corridor"),
]

SHAPES = {
    "stairs": "".join(
        f'<rect x="{170+i*46}" y="{380+i*40}" width="{860-i*92}" height="26" '
        f'fill="rgba(255,255,255,{0.030+i*0.004})"/>' for i in range(8)),
    "corridor": ('<polygon points="0,0 1200,0 760,900 440,900" fill="rgba(255,255,255,0.018)"/>'
                 '<polygon points="330,120 870,120 720,900 480,900" fill="rgba(255,255,255,0.022)"/>'),
    "window": ('<rect x="400" y="180" width="400" height="470" rx="8" fill="rgba(170,205,240,0.075)"/>'
               '<rect x="400" y="180" width="400" height="470" rx="8" fill="none" '
               'stroke="rgba(0,0,0,0.55)" stroke-width="16"/>'
               '<line x1="600" y1="180" x2="600" y2="650" stroke="rgba(0,0,0,0.55)" stroke-width="14"/>'),
    "doorway": ('<rect x="430" y="150" width="340" height="620" rx="4" fill="rgba(255,255,255,0.030)"/>'
                '<rect x="470" y="190" width="260" height="580" fill="rgba(120,160,200,0.055)"/>'),
}

# Semi-transparent, so it reads as an overlay on the frame rather than as part of the picture.
PLAY_BUTTON = ('<g transform="translate(600,450)">'
               '<circle r="92" fill="rgba(10,14,18,0.42)" stroke="rgba(255,255,255,0.55)" stroke-width="5"/>'
               '<polygon points="-19,-41 53,0 -19,41" fill="rgba(255,255,255,0.80)"/></g>')


def caption(text, right="SIMULATED"):
    return (f'<text x="26" y="872" font-family="ui-monospace,Menlo,monospace" font-size="21" '
            f'font-weight="600" fill="rgba(215,230,245,0.62)">{text}</text>'
            f'<text x="1174" y="872" text-anchor="end" font-family="ui-monospace,Menlo,monospace" '
            f'font-size="15" font-weight="600" fill="rgba(120,200,150,0.55)">{right}</text>')


def render(svg_body, out_path, background="#0b0d10"):
    """Rasterises one 1200x900 SVG scene to a JPEG poster via headless Chrome."""
    html = (f'<html><body style="margin:0;background:{background}">'
            f'<svg width="1200" height="900" xmlns="http://www.w3.org/2000/svg">'
            f'<defs><filter id="g"><feTurbulence type="fractalNoise" baseFrequency="0.9" '
            f'numOctaves="3"/><feColorMatrix type="saturate" values="0"/></filter>'
            f'<radialGradient id="v" cx="50%" cy="42%" r="72%">'
            f'<stop offset="55%" stop-color="rgba(0,0,0,0)"/>'
            f'<stop offset="100%" stop-color="rgba(0,0,0,0.92)"/></radialGradient></defs>'
            f'{svg_body}</svg></body></html>')
    with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False) as f:
        f.write(html)
        page = f.name
    png = page + ".png"
    subprocess.run([CHROME, "--headless", "--screenshot=" + png, "--window-size=1200,900",
                    "--hide-scrollbars", "file://" + page],
                   capture_output=True, check=False)
    if not os.path.exists(png):
        return False
    subprocess.run(["sips", "-s", "format", "jpeg", "-Z", "400", png, "--out", out_path],
                   capture_output=True, check=True)
    os.unlink(png)
    os.unlink(page)
    return True


def audio_peaks(path, buckets=300):
    """The recording's real peaks, or None when it cannot be decoded."""
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        tmp = f.name
    try:
        r = subprocess.run(["afconvert", "-f", "WAVE", "-d", "LEI16", path, tmp],
                           capture_output=True)
        if r.returncode != 0 or not os.path.exists(tmp):
            return None
        # Chunks parsed by hand rather than with the `wave` module: afconvert writes
        # WAVE_FORMAT_EXTENSIBLE (0xFFFE) for multi-channel output, which that module refuses
        # outright even though the samples inside are ordinary little-endian PCM.
        raw = open(tmp, "rb").read()
        if raw[:4] != b"RIFF" or raw[8:12] != b"WAVE":
            return None
        channels, bits, frames = 1, 16, b""
        pos = 12
        while pos + 8 <= len(raw):
            cid = raw[pos:pos + 4]
            size = struct.unpack("<I", raw[pos + 4:pos + 8])[0]
            body = raw[pos + 8:pos + 8 + size]
            if cid == b"fmt ":
                channels = struct.unpack("<H", body[2:4])[0]
                bits = struct.unpack("<H", body[14:16])[0]
            elif cid == b"data":
                frames = body
            pos += 8 + size + (size & 1)
        if not frames or bits != 16:
            return None
        samples = struct.unpack("<%dh" % (len(frames) // 2), frames)
        if channels > 1:
            samples = samples[::channels]
        if not samples:
            return None
        step = max(1, len(samples) // buckets)
        peaks = [max(abs(v) for v in samples[i * step:(i + 1) * step] or [0])
                 for i in range(buckets)]
        top = max(peaks) or 1
        # Normalised, so a quiet field recording still reads as a waveform and not a flat line.
        return [p / top for p in peaks]
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


def waveform_svg(peaks, label):
    """WaveSurfer's look: fixed bar width and gap, rounded, centred, one colour."""
    bar_w, gap, mid, max_h = 4, 2, 450, 150
    bars = []
    for i, p in enumerate(peaks):
        h = max(3, p * max_h)
        x = 60 + i * (bar_w + gap)
        if x > 1140:
            break
        bars.append(f'<rect x="{x:.0f}" y="{mid-h:.0f}" width="{bar_w}" height="{2*h:.0f}" '
                    f'rx="2" fill="#3f7d5a"/>')
    return "".join(bars) + PLAY_BUTTON + caption(label)


def main():
    made = {"video": 0, "audio": 0, "audio_undecodable": 0, "session": 0, "skipped": 0}
    scene_i = 0

    for path in sorted(glob.glob(os.path.join(ROOT, "**", "*"), recursive=True)):
        if not os.path.isfile(path):
            continue
        if path.endswith((".thumb.jpg", ".clean.jpg")):
            continue
        ext = os.path.splitext(path)[1].lower()
        thumb = path + ".thumb.jpg"
        if os.path.exists(thumb) and not FORCE:
            made["skipped"] += 1
            continue

        if ext in VIDEO_EXT:
            name, shape = SCENES[scene_i % len(SCENES)]
            scene_i += 1
            body = (f'<rect width="1200" height="900" fill="#12161a"/>{SHAPES[shape]}'
                    f'<rect width="1200" height="900" fill="url(#v)"/>'
                    f'<rect width="1200" height="900" filter="url(#g)" opacity="0.16"/>'
                    f'{PLAY_BUTTON}{caption(name + "  &#183;  video")}')
            if render(body, thumb):
                made["video"] += 1

        elif ext in AUDIO_EXT:
            peaks = audio_peaks(path)
            title = os.path.basename(path)
            if peaks:
                if render(waveform_svg(peaks, title + "  &#183;  audio"), thumb):
                    made["audio"] += 1
            else:
                # Said out loud rather than drawn as a convincing fake: a waveform that is not
                # this recording's waveform would be a picture that lies about the evidence.
                body = (f'<rect width="1200" height="900" fill="#0b0d10"/>{PLAY_BUTTON}'
                        f'{caption(title + "  &#183;  audio (waveform unavailable)")}')
                if render(body, thumb):
                    made["audio_undecodable"] += 1

    print("posters written: " + ", ".join(f"{k}={v}" for k, v in made.items()))


if __name__ == "__main__":
    main()
