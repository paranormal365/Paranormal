#!/usr/bin/env python3
"""Builds the App Store's product pages for one version (Ben, 10/15/2026).

    python3 Ben.iOS/store-pages/build.py 1.1.4             # every page, every device
    python3 Ben.iOS/store-pages/build.py 1.1.4 audio       # one page

Reads the frames and screen recordings the capture runs collected in
`screenshots-<version>/raw/<device>/` (scripts/capture-store-pages.sh, and the Duo walk-through), and
writes one folder per page:

    screenshots-<version>/<page>/iphone/01-….png   1242 × 2688
    screenshots-<version>/<page>/ipad/01-….png     2064 × 2752
    screenshots-<version>/<page>/iphone-duo/…png   2853 × 2007 (the inner screen, opened)
    screenshots-<version>/<page>/preview-iphone.mp4   886 × 1920, 30 s at most
    screenshots-<version>/<page>/preview-ipad.mp4    1200 × 1600, 30 s at most
    screenshots-<version>/<page>/README.md           what the page is for and its words

**Only the app.** Every screen and every second of video is the app itself, as App Review requires
(Guidelines 2.3.3 and 2.3.4). The website's tools appear only in the header and search images
(`creative-assets/`); here the captions say where the work is finished.

**What makes them pop:** the site's night colors and violet-to-cyan gradient, a headline per frame in
Public Sans, the screen raised off the page, and in the videos a caption per scene with a soft
cross-fade between scenes. No device frames: Apple refuses those in previews.
"""
import json, os, pathlib, shutil, subprocess, sys, tempfile
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
IOS = HERE.parent
ROOT = IOS.parent
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
FONTS = ROOT / "Ben.Web.Website/wwwroot/fonts"
FFMPEG = shutil.which("ffmpeg") or "/opt/homebrew/bin/ffmpeg"

# The store's sizes.
STILL = {"iphone": (1242, 2688), "ipad": (2064, 2752), "iphone-duo": (2853, 2007)}
PREVIEW = {"iphone": (886, 1920), "ipad": (1200, 1600)}

sys.path.insert(0, str(HERE))
from pages import PAGES  # noqa: E402  (the words and the frames, kept apart from the machinery)


def font_faces():
    return (FONTS / "fonts.css").read_text().replace("url(/fonts/", f"url({FONTS.as_uri()}/")


def chrome(html, w, h, out, transparent=False):
    with tempfile.TemporaryDirectory() as tmp:
        src = pathlib.Path(tmp) / "page.html"
        src.write_text(html)
        args = [CHROME, "--headless", "--hide-scrollbars", "--force-device-scale-factor=1",
                "--allow-file-access-from-files", "--virtual-time-budget=3000",
                f"--window-size={w},{h}", f"--screenshot={out}"]
        if transparent:
            args.append("--default-background-color=00000000")
        subprocess.run(args + [src.as_uri()], check=True, capture_output=True)


CSS = """
* { box-sizing: border-box; margin: 0; padding: 0; }
html, body { width: %(w)dpx; height: %(h)dpx; overflow: hidden; }
body { font-family: 'Public Sans', -apple-system, sans-serif; color: #E9EDF4; position: relative;
       background: radial-gradient(ellipse 70%% 45%% at 20%% 8%%, rgba(124, 92, 255, .55), transparent 70%%),
                   radial-gradient(ellipse 60%% 40%% at 90%% 95%%, rgba(34, 211, 238, .30), transparent 70%%),
                   linear-gradient(180deg, #151032, #0C1017 55%%, #0A1420); }
.kicker { font-weight: 700; text-transform: uppercase; letter-spacing: .14em; color: #22D3EE; }
.headline { font-weight: 800; letter-spacing: -.025em; line-height: 1.05; color: #fff; }
.headline em { font-style: normal; background: linear-gradient(120deg, #A78BFA, #22D3EE);
               -webkit-background-clip: text; background-clip: text; color: transparent; }
.sub { font-weight: 500; color: #C3CBD8; line-height: 1.3; }
.screen { position: absolute; overflow: hidden; background: #05070B;
          box-shadow: 0 50px 120px rgba(0, 0, 0, .6), 0 0 0 3px rgba(255, 255, 255, .10), 0 0 140px rgba(124, 92, 255, .28); }
.screen img { width: 100%%; height: 100%%; display: block; object-fit: cover; object-position: top; }
"""


def headline_html(text):
    # "First part|second part": the second part takes the gradient.
    first, _, second = text.partition("|")
    return f"{first} <em>{second}</em>" if second else first


def still(page, device, shot, index, out):
    w, h = STILL[device]
    css = CSS % {"w": w, "h": h}
    img = shot["image"].as_uri()
    kicker = page["kicker"]
    if device == "iphone-duo":
        # Landscape: the words on the left, the screen on the right.
        body = f"""
        <div style='position:absolute;left:110px;top:0;bottom:0;width:680px;display:flex;flex-direction:column;justify-content:center'>
          <div class='kicker' style='font-size:40px;margin-bottom:28px'>{kicker}</div>
          <div class='headline' style='font-size:108px;margin-bottom:34px'>{headline_html(shot['headline'])}</div>
          <div class='sub' style='font-size:48px'>{shot.get('sub', '')}</div>
        </div>
        <div class='screen' style='right:90px;top:{(h - 1358) / 2:.0f}px;width:1930px;height:1358px;border-radius:44px'><img src='{img}'></div>"""
    else:
        top = 0.075 if device == "iphone" else 0.06
        size = 104 if device == "iphone" else 120
        body = f"""
        <div style='position:absolute;left:0;right:0;top:{h*top:.0f}px;padding:0 {w*0.08:.0f}px;text-align:center'>
          <div class='kicker' style='font-size:{size*0.38:.0f}px;margin-bottom:{size*0.28:.0f}px'>{kicker}</div>
          <div class='headline' style='font-size:{size}px;margin-bottom:{size*0.3:.0f}px'>{headline_html(shot['headline'])}</div>
          <div class='sub' style='font-size:{size*0.44:.0f}px'>{shot.get('sub', '')}</div>
        </div>
        <div class='screen' style='left:{w*0.11:.0f}px;right:{w*0.11:.0f}px;top:{h*0.30:.0f}px;bottom:-{h*0.06:.0f}px;
             border-radius:{w*0.06:.0f}px {w*0.06:.0f}px 0 0'><img src='{img}'></div>"""
    html = f"<!doctype html><html><head><meta charset='utf-8'><style>{font_faces()}{css}</style></head><body>{body}</body></html>"
    chrome(html, w, h, out)
    Image.open(out).convert("RGB").save(out, optimize=True)


# ── the videos ────────────────────────────────────────────────────────────────

def caption_png(text, w, h, out):
    css = CSS % {"w": w, "h": h}
    html = (f"<!doctype html><html><head><meta charset='utf-8'><style>{font_faces()}{css}"
            f"body{{background:transparent!important}}</style></head><body>"
            f"<div style='position:absolute;left:{w*0.06:.0f}px;right:{w*0.06:.0f}px;bottom:{h*0.13:.0f}px;padding:{w*0.035:.0f}px {w*0.05:.0f}px;"
            f"border-radius:{w*0.05:.0f}px;background:rgba(12,16,23,.82);box-shadow:0 0 0 3px rgba(124,92,255,.55),0 20px 60px rgba(0,0,0,.5);text-align:center'>"
            f"<div class='headline' style='font-size:{w*0.068:.0f}px'>{headline_html(text)}</div></div></body></html>")
    chrome(html, w, h, out, transparent=True)


def clip_times(pool, frame):
    """The recording holding a frame, and the frame's moment within it."""
    shot = next((e for e in pool if e["file"] == frame), None)
    if not shot:
        return None
    # One recording per suite, covering every test in it.
    recordings = [e for e in pool if e["kind"] == "recording" and e["suite"] == shot["suite"]]
    if not recordings:
        return None
    rec = recordings[0]
    return rec["file"], max(0.0, shot["timestamp"] - rec["timestamp"])


# Where a scene really is in its recording (10/15/2026). The timestamps put a scene seconds away from
# what was on screen, 33 s on one iPad recording, so scenes were cut from the home screen, from black,
# or from the screen before. The frame the test saved is the truth: every recording frame near the
# stamped moment is compared with it, and the scene is cut where they match.
SAMPLE_FPS = 4
SEARCH = 45.0          # seconds either side of the stamped moment
MATCH = 0.97           # a frame this close to the saved one is the same screen
LEAD_IN = 2.0          # at most this much of the way there before it, if none of it is off-app


def steady(rec):
    """The recording at a steady 30 fps with a keyframe every second, made once and kept beside it.
    The simulator writes a frame only when the screen changes, so a cut that starts between changes
    came out black, or as the previous screen, until the next one."""
    rec = pathlib.Path(rec)
    out = rec.parent / "steady" / rec.name
    if not out.exists() or out.stat().st_mtime < rec.stat().st_mtime:
        out.parent.mkdir(exist_ok=True)
        subprocess.run([FFMPEG, "-loglevel", "error", "-y", "-i", str(rec), "-vf", "fps=30", "-c:v", "libx264",
                        "-preset", "veryfast", "-crf", "16", "-g", "30", "-pix_fmt", "yuv420p", "-an", str(out)],
                       check=True)
    return out


def _sample(rec, start, length, size, mode):
    from PIL import Image
    w, h = size
    fmt, bpp = ("gray", 1) if mode == "L" else ("rgb24", 3)
    raw = subprocess.run([FFMPEG, "-v", "error", "-ss", f"{start:.2f}", "-t", f"{length:.2f}", "-i", str(rec),
                          "-vf", f"fps={SAMPLE_FPS},scale={w}:{h},format={fmt}", "-f", "rawvideo", "-"],
                         capture_output=True).stdout
    n = len(raw) // (w * h * bpp)
    return [Image.frombytes(mode, (w, h), raw[i * w * h * bpp:(i + 1) * w * h * bpp]) for i in range(n)]


def _look(path):
    from PIL import Image
    img = Image.open(path)
    return img.convert("L").resize((64, max(16, round(64 * img.height / img.width))))


def _alike(a, b):
    from PIL import ImageChops, ImageStat
    if a.size != b.size:
        return 0.0
    return 1 - ImageStat.Stat(ImageChops.difference(a, b)).mean[0] / 255


def _off_app(img):
    """Black, or the colorful home screen: never in a preview."""
    from PIL import ImageStat
    mean = sum(ImageStat.Stat(img).mean) / 3
    sat = ImageStat.Stat(img.convert("HSV")).mean[1]
    return mean < 4 or (sat > 90 and mean > 40)


def find_scene(raw, pool, frame, length):
    """(recording, start, footage seconds) for a scene, or None when the saved screen is not in the recording.
    Footage may be shorter than asked; the rest is the last matching frame held."""
    from PIL import Image, ImageChops, ImageStat
    found = clip_times(pool, frame)
    if not found or not (raw / frame).exists():
        return None
    rec, at = found
    ref = Image.open(raw / frame)
    w, h = 64, max(16, round(64 * ref.height / ref.width))
    ref = ref.convert("L").resize((w, h))
    t0 = max(0.0, at - SEARCH)
    video = steady(raw / rec)
    gray = _sample(video, t0, 2 * SEARCH, (w, h), "L")
    color = _sample(video, t0, 2 * SEARCH, (48, 96), "RGB")
    if not gray:
        return None
    score = [1 - ImageStat.Stat(ImageChops.difference(ref, g)).mean[0] / 255 for g in gray]
    best = max(range(len(score)), key=score.__getitem__)
    if score[best] < MATCH:
        return None
    # iPad screens look alike when small (the sidebar, a dark page), so "close" is close to the best
    # match, not merely above a floor: the feed passed for the tours list at 0.97.
    floor = max(MATCH, score[best] - 0.006)
    lo = hi = best
    while lo > 0 and score[lo - 1] >= floor:
        lo -= 1
    while hi + 1 < len(score) and score[hi + 1] >= floor:
        hi += 1
    # Lead in only through on-app frames, and only as much as the scene has room for.
    room = max(0.0, length - (hi - lo + 1) / SAMPLE_FPS)
    lead = 0
    while (lead + 1) / SAMPLE_FPS <= min(LEAD_IN, room) and lo - lead - 1 >= 0 \
            and not _off_app(color[min(lo - lead - 1, len(color) - 1)]):
        lead += 1
    start = t0 + (lo - lead) / SAMPLE_FPS
    footage = min(length, (hi - lo + 1 + lead) / SAMPLE_FPS)
    return rec, start, footage


def preview(page, device, raw, pool, out):
    w, h = PREVIEW[device]
    scenes = []
    with tempfile.TemporaryDirectory() as tmp:
        tmp = pathlib.Path(tmp)
        seen = []
        for i, scene in enumerate(page["video"]):
            length = scene.get("seconds", 4.0)
            if (raw / scene["frame"]).exists():
                look = _look(raw / scene["frame"])
                if any(_alike(look, earlier) >= 0.9995 for earlier in seen):
                    print(f"   {page['key']}/{device}: {scene['frame']} repeats an earlier screen, scene left out")
                    continue
                seen.append(look)
            found = find_scene(raw, pool, scene["frame"], length)
            if not found:
                print(f"   {page['key']}/{device}: {scene['frame']} is not in its recording, scene left out")
                continue
            rec, start, footage = found
            cap = tmp / f"cap{i}.png"
            caption_png(scene["caption"], w, h, cap)
            part = tmp / f"s{i}.mp4"
            # The footage fills the frame (cropped from the top), the caption sits over it; when the screen was
            # up for less than the scene, its last frame is held rather than cutting into whatever came next.
            hold = max(0.0, length - footage)
            subprocess.run([FFMPEG, "-loglevel", "error", "-y", "-ss", f"{start:.2f}", "-t", f"{footage:.2f}",
                            "-i", str(steady(raw / rec)), "-i", str(cap), "-filter_complex",
                            f"[0:v]scale={w}:-2:flags=lanczos,crop={w}:{h}:0:0,fps=30,format=yuv420p,"
                            f"tpad=stop_mode=clone:stop_duration={hold:.2f}[v];"
                            f"[v][1:v]overlay=0:0[o]", "-map", "[o]", "-an", "-r", "30", str(part)], check=True)
            # The real length: a scene near the end of its recording comes out shorter than asked, and the
            # cross-fades below are placed by length, so a guessed one cut every scene after it.
            real = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                                         "-of", "csv=p=0", str(part)], capture_output=True, text=True).stdout or 0)
            if real < 2.0:
                print(f"   {page['key']}/{device}: {scene['frame']} has under 2 s of footage, scene left out")
                continue
            scenes.append((part, real))
        if not scenes:
            return False
        # Soft cross-fades between scenes, kept under Apple's 30 seconds.
        fade = 0.5
        inputs, filters, last, offset = [], [], "0:v", 0.0
        for i, (part, length) in enumerate(scenes):
            inputs += ["-i", str(part)]
        total = scenes[0][1]
        for i in range(1, len(scenes)):
            offset = total - fade
            filters.append(f"[{last}][{i}:v]xfade=transition=fade:duration={fade}:offset={offset:.2f}[x{i}]")
            last = f"x{i}"
            total = offset + scenes[i][1]
        total = min(total, 29.5)
        # A silent stereo track: Apple's preview checker refuses some files with no audio at all.
        video = [FFMPEG, "-loglevel", "error", "-y", *inputs,
                 "-f", "lavfi", "-t", f"{total:.2f}", "-i", "anullsrc=r=44100:cl=stereo"]
        if filters:
            video += ["-filter_complex", ";".join(filters), "-map", f"[{last}]"]
        else:
            video += ["-map", "0:v"]
        video += ["-map", f"{len(scenes)}:a", "-t", f"{total:.2f}", "-c:v", "libx264", "-profile:v", "high",
                  "-pix_fmt", "yuv420p", "-r", "30", "-b:v", "10M", "-c:a", "aac", "-b:a", "256k", str(out)]
        subprocess.run(video, check=True)
    return True


def build(version, wanted):
    base = IOS / f"screenshots-{version}"
    for page in PAGES:
        if wanted and page["key"] not in wanted:
            continue
        folder = base / page["key"]
        print(f"── {page['key']}")
        for device in STILL:
            raw = base / "raw" / device
            if not raw.exists():
                print(f"   no raw captures for {device} yet")
                continue
            pool = json.loads((raw / "manifest.json").read_text()) if (raw / "manifest.json").exists() else []
            outdir = folder / device
            shutil.rmtree(outdir, ignore_errors=True)
            outdir.mkdir(parents=True)
            n, seen = 0, []
            for shot in (page["duo"] if device == "iphone-duo" else page["shots"]):
                frame = shot["frame"]
                image = raw / frame
                if not image.exists():
                    print(f"   {device}: missing {frame}, left out")
                    continue
                # A capture that never reached its screen saves the one it was on: on iPad the visits
                # frame is My Cases again. The same screen twice on one page is left out the second time.
                look = _look(image)
                if any(_alike(look, earlier) >= 0.9995 for earlier in seen):
                    print(f"   {device}: {frame} repeats an earlier screen, left out")
                    continue
                seen.append(look)
                n += 1
                still(page, device, dict(shot, image=image), n, outdir / f"{n:02d}-{pathlib.Path(frame).stem}.png")
            print(f"   {device}: {n} screenshots")
            if device in PREVIEW and preview(page, device, raw, pool, folder / f"preview-{device}.mp4"):
                print(f"   {device}: preview")
        (folder / "README.md").write_text(page_readme(page))


def page_readme(page):
    lines = [f"# {page['name']}", "", page["for"], "", f"**Promotional text:** {page['promo']}", "",
             "| # | Screenshot | Headline |", "|---|---|---|"]
    for i, s in enumerate(page["shots"], 1):
        lines.append(f"| {i} | `{s['frame']}` | {s['headline'].replace('|', ' ')} |")
    lines += ["", "Header and search images: `../../creative-assets/" + page.get("art", page["key"]) + "/`.", ""]
    return "\n".join(lines)


if __name__ == "__main__":
    version = sys.argv[1] if len(sys.argv) > 1 else "1.1.4"
    build(version, set(sys.argv[2:]))
