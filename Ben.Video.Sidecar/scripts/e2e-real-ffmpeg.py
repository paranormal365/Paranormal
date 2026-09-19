#!/usr/bin/env python3
"""End-to-end sidecar exercise against REAL ffmpeg — item #70 phase 174.

MANUAL verification, not part of `dotnet test`: it needs real binaries fetched into
ffmpeg/<rid>/ and a sidecar process already running. The automated suites deliberately run
against Ben.Video.Sidecar.FakeFfmpeg, which produces no real audio or video, so nothing in them
can see what this script sees.

    scripts/fetch-ffmpeg.sh osx-arm64
    BENVIDEO_SIDECAR_HOME=$(mktemp -d) \
        dotnet run --project Ben.Video.Sidecar -- --reset-token   # note port + pairing code
    python3 scripts/e2e-real-ffmpeg.py <port> <pairing-code>

BENVIDEO_SIDECAR_HOME is not optional advice. The pairing token lives in ONE per-user location,
so on a machine with the sidecar installed, `--reset-token` resets the INSTALLED one's token —
the browser that was paired with it then fails to connect, with nothing on screen to say why. The
variable gives this run its own config and cache and leaves the installed sidecar alone. It also
works against a published build: point it at the app inside the .app bundle instead of `dotnet
run`, which is how the osx-arm64 package was verified on 2026-09-05.

Why it exists: phase 162 shipped concat + audio mix as one sidecar job and recorded an explicit
gap — audio SYNC was unverifiable without real ffmpeg. The first run of this script closed that
gap by failing, and the failure was a genuine export bug in ExportArgBuilders.BuildAudioClipTrimArgs
(shared with the browser, so both paths were affected): output-side -ss/-to were applied AFTER the
filter graph, so an adelay'd clip was truncated by exactly its own timeline offset. Every unit
test passed throughout, because argv was correct — only the produced audio was wrong.

Test signal design — each source carries a distinct pure tone, so the output can be read back
per-second with a bandpass filter and compared against where each tone is SUPPOSED to be:
  clip A: 4s red   video @ 1000 Hz  -> timeline 0-4s
  clip B: 4s blue  video @  440 Hz  -> timeline 4-8s
  clip C: 4s audio-only  @  300 Hz  -> mixed in at 2s via adelay in its filter chain
The tones are far apart so a narrow bandpass isolates each one; a clip landing at the wrong time,
truncated, or dropped shows up as a band being loud in a window where it should be quiet.
"""
import json, subprocess, sys, time, urllib.error, urllib.request, uuid, os

if len(sys.argv) < 3:
    raise SystemExit(f"usage: {os.path.basename(__file__)} <sidecar-port> <pairing-token> [rid]")

PORT = int(sys.argv[1])
TOKEN = sys.argv[2]
RID = sys.argv[3] if len(sys.argv) > 3 else None
PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WORK = os.path.join(PROJECT, "obj", "e2e-real-ffmpeg")
os.makedirs(WORK, exist_ok=True)

def _default_rid():
    import platform
    arch = "arm64" if platform.machine() in ("arm64", "aarch64") else "x64"
    return {"Darwin": f"osx-{arch}", "Windows": f"win-{arch}"}.get(platform.system(), f"linux-{arch}")

RID = RID or _default_rid()
EXE = ".exe" if RID.startswith("win-") else ""
FFMPEG = os.path.join(PROJECT, "ffmpeg", RID, f"ffmpeg{EXE}")
FFPROBE = os.path.join(PROJECT, "ffmpeg", RID, f"ffprobe{EXE}")
for tool in (FFMPEG, FFPROBE):
    if not os.path.exists(tool):
        raise SystemExit(f"missing {tool} — run scripts/fetch-ffmpeg.sh {RID} first.")

BASE = f"http://127.0.0.1:{PORT}"
# Any origin on SidecarOptions.AllowedOrigins works; SecurityMiddleware 403s a missing one.
HEADERS = {"X-BenVideo-Sidecar-Token": TOKEN, "Origin": "http://localhost:5078"}

failures = []

def check(label, ok, detail=""):
    print(f"  {'PASS' if ok else 'FAIL'}  {label}{(' — ' + detail) if detail else ''}")
    if not ok:
        failures.append(f"{label}: {detail}")

def req(method, path, body=None, headers=None, raw=False):
    data = json.dumps(body).encode() if isinstance(body, (dict, list)) else body
    h = dict(HEADERS)
    if isinstance(body, (dict, list)):
        h["Content-Type"] = "application/json"
    h.update(headers or {})
    r = urllib.request.Request(BASE + path, data=data, headers=h, method=method)
    try:
        with urllib.request.urlopen(r, timeout=120) as resp:
            payload = resp.read()
            return resp.status, (payload if raw else (json.loads(payload) if payload else None))
    except urllib.error.HTTPError as e:
        payload = e.read()
        try:
            return e.code, json.loads(payload)
        except Exception:
            return e.code, payload.decode(errors="replace")

def ff(args):
    p = subprocess.run([FFMPEG, "-y", "-hide_banner", *args], capture_output=True, text=True)
    if p.returncode != 0:
        print(p.stderr[-2000:])
        raise SystemExit(f"ffmpeg failed: {args}")
    return p.stderr

def make_media():
    print("\n[1] Generating real test media")
    ff(["-f", "lavfi", "-i", "color=c=red:s=640x360:d=4:r=30",
        "-f", "lavfi", "-i", "sine=frequency=1000:duration=4",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", f"{WORK}/clipA.mp4"])
    ff(["-f", "lavfi", "-i", "color=c=blue:s=640x360:d=4:r=30",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", f"{WORK}/clipB.mp4"])
    ff(["-f", "lavfi", "-i", "sine=frequency=300:duration=4", "-c:a", "aac", f"{WORK}/clipC.m4a"])
    for n in ("clipA.mp4", "clipB.mp4", "clipC.m4a"):
        print(f"      {n}: {os.path.getsize(f'{WORK}/{n}')} bytes")

def upload(clip_id, path, ext):
    with open(path, "rb") as f:
        status, body = req("PUT", f"/v1/sources/{clip_id}?ext={ext}", f.read(),
                           {"Content-Type": "application/octet-stream"})
    check(f"upload {os.path.basename(path)}", status == 200, f"status={status} {body}")

def poll(job_id, label):
    for _ in range(600):
        status, body = req("GET", f"/v1/jobs/{job_id}")
        if status != 200:
            return None, f"status poll returned {status}"
        if body["state"] == "Succeeded":
            return body, None
        if body["state"] == "Failed":
            return None, body.get("errorMessage")
        time.sleep(0.25)
    return None, "timed out waiting for job"

QUALITY = {"videoCodec": "H264", "audioCodec": "Aac", "bitrate": 2000, "useCrf": True, "crf": 23,
           "includeAudio": True, "audioBitrate": 128, "preset": "Medium", "fps": 30}

def segment_spec(clip_id):
    return {"kind": "Video", "clipId": clip_id, "sourceExt": ".mp4", "pass": "Export",
            "duration": 4.0, "startTrim": 0.0, "endTrim": 4.0, "speed": 1.0, "muteAudio": False,
            "gain": 1.0, "outputWidth": 640, "outputHeight": 360, "effects": None,
            "appliedEffects": [], "volumeAutomation": [], "exportQuality": QUALITY, "retain": True}

def tone_energy(audio_path, freq, start, dur):
    """Mean volume (dBFS) inside a narrow band around `freq` for one window of the output."""
    p = subprocess.run(
        [FFMPEG, "-hide_banner", "-ss", str(start), "-t", str(dur), "-i", audio_path,
         "-af", f"bandpass=f={freq}:w=40,volumedetect", "-f", "null", "-"],
        capture_output=True, text=True)
    for line in p.stderr.splitlines():
        if "mean_volume:" in line:
            return float(line.split("mean_volume:")[1].strip().split()[0])
    return -999.0

def main():
    make_media()
    a_id, b_id, c_id = str(uuid.uuid4()), str(uuid.uuid4()), str(uuid.uuid4())

    print("\n[2] Uploading sources")
    upload(a_id, f"{WORK}/clipA.mp4", ".mp4")
    upload(b_id, f"{WORK}/clipB.mp4", ".mp4")
    upload(c_id, f"{WORK}/clipC.m4a", ".m4a")

    print("\n[3] POST /v1/probe (needs the hash-verified ffprobe)")
    status, body = req("POST", "/v1/probe", {"clipId": a_id, "sourceExt": ".mp4"})
    check("probe returns 200", status == 200, f"status={status} {body}")
    if status == 200:
        print(f"      {body}")
        # MediaProbeInfo is exactly (Duration, Width, Height) — no hasAudio field exists.
        check("probe duration ~4s", abs(body.get("duration", 0) - 4.0) < 0.3,
              f"got {body.get('duration')}")
        check("probe reports 640x360", body.get("width") == 640 and body.get("height") == 360,
              f"got {body.get('width')}x{body.get('height')}")

    print("\n[4] POST /v1/jobs/thumbnails")
    status, body = req("POST", "/v1/jobs/thumbnails",
                       {"clipId": a_id, "sourceExt": ".mp4", "count": 5, "duration": 4.0})
    check("thumbnail job accepted", status == 202, f"status={status} {body}")
    if status == 202:
        result, err = poll(body["jobId"], "thumbnails")
        check("thumbnail job succeeded", result is not None, err or "")
        if result:
            status, manifest = req("GET", f"/v1/jobs/{body['jobId']}/result")
            names = [f["name"] for f in manifest.get("files", [])]
            check("5 real frames produced", len(names) == 5, f"got {len(names)}: {names}")
            if names:
                status, raw = req("GET", f"/v1/jobs/{body['jobId']}/result/{names[0]}", raw=True)
                # Check the RIFF/WEBP magic rather than a byte count — these frames are a flat
                # colour and compress to ~130 bytes, which is small but perfectly valid.
                check("frame downloads as a real WebP",
                      status == 200 and raw[:4] == b"RIFF" and raw[8:12] == b"WEBP",
                      f"status={status} bytes={len(raw) if raw else 0} magic={raw[:12] if raw else b''}")

    print("\n[5] POST /v1/jobs/segment x2 (Pass=Export, Retain=true)")
    retained = []
    for label, cid in (("A", a_id), ("B", b_id)):
        status, body = req("POST", "/v1/jobs/segment", segment_spec(cid))
        check(f"segment {label} accepted", status == 202, f"status={status} {body}")
        if status != 202:
            continue
        result, err = poll(body["jobId"], f"segment {label}")
        check(f"segment {label} rendered", result is not None, err or "")
        if result:
            check(f"segment {label} retained", result.get("retainedSegmentId") is not None,
                  f"got {result.get('retainedSegmentId')}")
            print(f"      segment {label}: {result.get('resultSizeBytes')} bytes, "
                  f"retained id {result.get('retainedSegmentId')}")
            retained.append(result["retainedSegmentId"])

    if len(retained) != 2:
        print("\n!! cannot continue to assemble without two retained segments")
        return report()

    print("\n[6] POST /v1/jobs/export-assemble (concat + audio mix, ONE job)")
    assemble = {
        "segmentIds": retained,
        "quality": QUALITY,
        # adelay places clip C at t=2s on the timeline — the browser bakes position into the chain,
        # so if the sidecar's mix is desynced this is exactly where it shows.
        "audio": {"clips": [{"clipId": c_id, "sourceExt": ".m4a", "start": 0.0, "end": 4.0,
                             "filterChain": "volume=1.0,adelay=2000|2000"}]},
    }
    status, body = req("POST", "/v1/jobs/export-assemble", assemble)
    check("assemble accepted", status == 202, f"status={status} {body}")
    if status != 202:
        return report()
    result, err = poll(body["jobId"], "assemble")
    check("assemble succeeded", result is not None, err or "")
    if not result:
        return report()

    status, raw = req("GET", f"/v1/jobs/{body['jobId']}/result", raw=True)
    out = f"{WORK}/assembled.mp4"
    with open(out, "wb") as f:
        f.write(raw)
    check("assembled result downloaded", status == 200 and len(raw) > 1000, f"bytes={len(raw)}")
    print(f"      {out}: {len(raw)} bytes")

    print("\n[7] Verifying the assembled output with ffprobe")
    p = subprocess.run([FFPROBE, "-v", "quiet", "-print_format", "json",
                        "-show_format", "-show_streams", out], capture_output=True, text=True)
    info = json.loads(p.stdout)
    dur = float(info["format"]["duration"])
    kinds = sorted(s["codec_type"] for s in info["streams"])
    check("output duration ~8s (both segments, in order)", abs(dur - 8.0) < 0.4, f"got {dur:.3f}s")
    check("output has one video + one audio stream", kinds == ["audio", "video"], f"got {kinds}")

    print("\n[8] AUDIO SYNC — the check phase 162 could not run")
    print("      window   1000Hz(A)   440Hz(B)   300Hz(C)")
    windows = []
    for start in range(8):
        e1000, e440, e300 = (tone_energy(out, f, start + 0.25, 0.5) for f in (1000, 440, 300))
        windows.append((start, e1000, e440, e300))
        print(f"      {start}-{start+1}s   {e1000:8.1f}   {e440:8.1f}   {e300:8.1f}")

    # A tone is "present" when its band sits well above the noise floor of a band with no tone.
    def present(v):
        return v > -50.0

    for start, e1000, e440, e300 in windows:
        expect_a = start < 4          # clip A occupies 0-4s
        expect_b = start >= 4         # clip B occupies 4-8s
        expect_c = 2 <= start < 6     # clip C delayed to 2s, 4s long
        check(f"t={start}s clip A tone {'present' if expect_a else 'absent'}",
              present(e1000) == expect_a, f"1000Hz={e1000:.1f}dB")
        check(f"t={start}s clip B tone {'present' if expect_b else 'absent'}",
              present(e440) == expect_b, f"440Hz={e440:.1f}dB")
        check(f"t={start}s clip C tone {'present' if expect_c else 'absent'}",
              present(e300) == expect_c, f"300Hz={e300:.1f}dB")

    report()

def report():
    print("\n" + "=" * 64)
    if failures:
        print(f"{len(failures)} FAILURE(S):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    print("All checks passed.")

main()
