#!/bin/zsh
#
# The raw material for the App Store pages (Ben, 10/15/2026): every capture suite run once on one
# device, its frames and its screen recordings collected into one pool the page builder reads.
#
#   Ben.iOS/scripts/capture-store-pages.sh iphone 1.1.4
#   Ben.iOS/scripts/capture-store-pages.sh ipad   1.1.4
#
# Writes Ben.iOS/screenshots-<version>/raw/<device>/: the frames under the names the tests gave them,
# each suite's screen recordings, and manifest.json (every attachment with its time, so a video clip
# can be cut at the moment a frame was taken). The pages are then built by store-pages/build.py.
#
# Before it: the seeded stack, kept, with the feed arranged for store shots —
#   scripts/run-e2e.sh --keep --filter "TestCategory=StoreShotsArrange"
# The iPhone Duo is captured by its own walk-through on the Xcode beta (see ProjectNotes/branches/ios-duo.md).
#
set -euo pipefail
cd "$(dirname "$0")/.."
DEVICE_KIND="${1:-iphone}"
VERSION="${2:-1.1.4}"
case "$DEVICE_KIND" in
  iphone) SIM_NAME="iPhone 17 Pro Max" ;;
  ipad)   SIM_NAME="iPad Pro 13-inch (M5)" ;;
  # The iPhone Duo needs the Xcode beta (27.1 or later) and must be OPENED first in Device Hub (bottom-right
  # toolbar, "fully open"); nothing on the command line can fold or unfold it. A test's own screenshot of the
  # opened Duo comes out black, so its frames are cut from a recording of the inner screen at each frame's time.
  iphone-duo) SIM_NAME="iPhone Duo"; export DEVELOPER_DIR="${DUO_DEVELOPER_DIR:-$HOME/Downloads/Xcode-beta.app/Contents/Developer}" ;;
  *) echo "usage: $0 [iphone|ipad|iphone-duo] [version]"; exit 2 ;;
esac
OUT="screenshots-$VERSION/raw/$DEVICE_KIND"

# The seeded accounts, from the one place the repo keeps them. Nothing here echoes a value.
set -a; source ../scripts/seeded-passwords.sh; set +a
export TEST_RUNNER_BEN_SCREENSHOTS=1
export TEST_RUNNER_BEN_DOC_SHOTS=1
export TEST_RUNNER_BEN_API_BASE_URL="${BEN_API_BASE_URL:-http://localhost:5252}"
export TEST_RUNNER_BEN_CLIENT_EMAIL="${BEN_CLIENT_EMAIL:-daniel.park@benco.dev}"
export TEST_RUNNER_BEN_CLIENT_PASSWORD="$BEN_CLIENT_PASSWORD"
export TEST_RUNNER_BEN_MEMBER_EMAIL="${BEN_MEMBER_EMAIL:-james.thornton@benco.dev}"
export TEST_RUNNER_BEN_MEMBER_PASSWORD="$BEN_MEMBER_PASSWORD"
[[ -n "${BEN_DOOR_SCAN_CODE:-}" ]] && export TEST_RUNNER_BEN_DOOR_SCAN_CODE="$BEN_DOOR_SCAN_CODE"

if ! curl -fsS -o /dev/null --max-time 3 "$TEST_RUNNER_BEN_API_BASE_URL/api/public/build"; then
  echo "REFUSING: no API at $TEST_RUNNER_BEN_API_BASE_URL. Start the kept stack first."
  exit 1
fi

UDID=$(xcrun simctl list devices available | grep -m1 "$SIM_NAME (" | sed -E 's/.*\(([0-9A-F-]{36})\).*/\1/')
[[ -n "$UDID" ]] || { echo "no simulator called $SIM_NAME"; exit 1; }
echo "── $SIM_NAME ($UDID) ────────────────────────────────"
xcrun simctl boot "$UDID" 2>/dev/null || true
xcrun simctl bootstatus "$UDID" -b
xcrun simctl ui "$UDID" appearance dark
for p in camera microphone location; do xcrun simctl privacy "$UDID" grant $p com.ishaunted.ios 2>/dev/null || true; done
# Red Boiling Springs, Tennessee: the demo events' town, so "near you" lists something.
xcrun simctl location "$UDID" set 36.5309,-85.8496 2>/dev/null || true

DD="/tmp/store-pages-dd-$DEVICE_KIND"
xcodebuild build-for-testing -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination "platform=iOS Simulator,id=$UDID" -derivedDataPath "$DD" -quiet

# SUITES="FieldKitScreenshotTests" captures only those, into the pool as it stands (a redo of one suite).
if [[ -z "${SUITES:-}" ]]; then rm -rf "$OUT"; fi
mkdir -p "$OUT"
# One suite per result bundle, so a suite that fails still leaves the others' frames.
# The last run is the door alone, as somebody who runs one (the seeded Sarah runs two demo doors).
for SUITE in ${=SUITES:-AppStoreScreenshotTests FieldKitScreenshotTests DeveloperDocCaptureTests HelpMediaCaptureTests DoorAsHost}; do
  echo "── $SUITE"
  ONLY="IsHauntedUITests/$SUITE"
  if [[ "$SUITE" == "DoorAsHost" ]]; then
    ONLY="IsHauntedUITests/HelpMediaCaptureTests/testCaptureTheDoor"
    export TEST_RUNNER_BEN_CLIENT_EMAIL="${BEN_HOST_EMAIL:-sarah.mitchell@benco.dev}"
    export TEST_RUNNER_BEN_CLIENT_PASSWORD="$BEN_USER_PASSWORD"
  fi
  RESULT="/tmp/store-pages-$DEVICE_KIND-$SUITE.xcresult"
  rm -rf "$RESULT"
  # The whole suite's screen, recorded here: Xcode keeps its own recording only for a test that failed, and the
  # previews are cut from this one. The Duo's opened screen is its second display.
  VIDEO="/tmp/store-pages-$DEVICE_KIND-$SUITE.mp4"; rm -f "$VIDEO" /tmp/store-pages-rec-$DEVICE_KIND.log
  DISPLAY_ARG=(); [[ "$DEVICE_KIND" == "iphone-duo" ]] && DISPLAY_ARG=(--display=3)
  xcrun simctl io "$UDID" recordVideo $DISPLAY_ARG --codec h264 --force "$VIDEO" 2>/tmp/store-pages-rec-$DEVICE_KIND.log &
  REC_PID=$!
  until grep -q "Recording started" /tmp/store-pages-rec-$DEVICE_KIND.log 2>/dev/null; do sleep 0.2; done
  REC_START=$(python3 -c 'import time; print(time.time())')
  xcodebuild test-without-building -project IsHaunted.xcodeproj -scheme IsHaunted \
    -destination "platform=iOS Simulator,id=$UDID" -derivedDataPath "$DD" \
    -only-testing:"$ONLY" -resultBundlePath "$RESULT" -quiet || echo "   ($SUITE reported failures; its frames are kept)"
  if [[ -n "$REC_PID" ]]; then sleep 1.5; kill -INT $REC_PID; wait $REC_PID 2>/dev/null || true; fi
  EXPORT="/tmp/store-pages-$DEVICE_KIND-$SUITE-export"
  rm -rf "$EXPORT"
  xcrun xcresulttool export attachments --path "$RESULT" --output-path "$EXPORT" >/dev/null
  python3 - "$EXPORT" "$OUT" "$SUITE" "$REC_START" "$VIDEO" "$DEVICE_KIND" <<'PLACE'
import json, pathlib, re, shutil, subprocess, sys
export, out, suite = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
rec_start, video, device = float(sys.argv[4]), sys.argv[5], sys.argv[6]
duo_video = video if device == "iphone-duo" else None
pool = out / "manifest.json"
entries = json.loads(pool.read_text()) if pool.exists() else []
entries = [e for e in entries if e.get("suite") != suite]
taken = 0
for run in json.loads((export / "manifest.json").read_text()):
    test = run.get("testIdentifier", "")
    for a in run.get("attachments") or []:
        name = a.get("suggestedHumanReadableName") or ""
        src = export / (a.get("exportedFileName") or "")
        if not src.exists():
            continue
        if name.endswith(".png"):
            clean = re.sub(r"_\d+_[0-9A-Fa-f-]{36}(?=\.png$)", "", name)
            if clean.startswith("UI Snapshot") or clean.startswith("Debug"):
                continue
            kind = "frame"
        elif name.endswith(".mp4"):
            clean = f"{suite}-{test.split('/')[-1].rstrip('()')}.mp4"
            kind = "recording"
        else:
            continue
        if duo_video and kind == "frame":
            # The Duo: the frame from the inner screen's recording, at the moment the test took it.
            # The recorder sometimes drops to black for a moment; try a little earlier, then later.
            from PIL import Image
            for shift in (-0.3, -0.8, -1.4, 0.4, -2.0, 1.0):
                at = max(0.0, a.get("timestamp", 0) - rec_start + shift)
                subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-ss", f"{at:.2f}", "-i", duo_video,
                                "-frames:v", "1", str(out / clean)], check=False)
                if (out / clean).exists() and Image.open(out / clean).convert("L").getextrema()[1] > 20:
                    break
        elif kind == "recording":
            continue   # Xcode's own recording of a failed test; the suite's own is kept below
        else:
            shutil.copy(src, out / clean)
        entries.append({"file": clean, "kind": kind, "suite": suite, "test": test,
                        "timestamp": a.get("timestamp")})
        taken += 1
if pathlib.Path(video).exists():
    shutil.copy(video, out / f"{suite}.mp4")
    entries = [e for e in entries if not (e["kind"] == "recording" and e["suite"] == suite)]
    entries.append({"file": f"{suite}.mp4", "kind": "recording", "suite": suite, "test": suite, "timestamp": rec_start})
pool.write_text(json.dumps(entries, indent=1))
print(f"   {taken} files")
PLACE
done
echo "── done: $OUT"
ls "$OUT" | wc -l
