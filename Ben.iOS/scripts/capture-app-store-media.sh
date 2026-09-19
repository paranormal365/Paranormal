#!/bin/zsh
#
# The App Store set and previews for one device, dark, driven through a real scripted night.
#
#   Ben.iOS/scripts/capture-app-store-media.sh iphone   1.0.3
#   Ben.iOS/scripts/capture-app-store-media.sh ipad     1.0.3
#
# Everything the 1.0.2 capture taught, written down once instead of retyped (see
# screenshots-1.0.2/README.md, which this replaces as the way to run it):
#
#   * TEST_RUNNER_* variables must be EXPORTED in this shell. Passed as `xcodebuild KEY=value`
#     they never reach the runner, and the test skips itself while reporting success.
#   * The API has to be up and seeded, because the Send screen signs in. Without it the set still
#     captures, but frame 14 photographs "Your session ended" instead of the trimmer.
#   * Dark, always: the store set is dark, and `simctl ui … appearance dark` has to be set on a
#     BOOTED device.
#   * No Release build needed for the Field Kit frames, but a Debug build must never photograph
#     the profile screen — it carries the DEBUG-only Developer section, and debug UI in a
#     screenshot is a rejection. This set does not go there.
#
set -euo pipefail
cd "$(dirname "$0")/.."

DEVICE_KIND="${1:-iphone}"
VERSION="${2:-1.0.3}"
OUT="screenshots-$VERSION"

case "$DEVICE_KIND" in
  iphone) SIM_NAME="iPhone 17 Pro Max"; SHOT_DIR="$OUT/iphone-6.5-dark"; CUT="886 1920 30 28" ;;
  ipad)   SIM_NAME="iPad Pro 13-inch (M5)"; SHOT_DIR="$OUT/ipad-13-dark";  CUT="1200 1600 27 28" ;;
  *) echo "usage: $0 [iphone|ipad] [version]"; exit 2 ;;
esac

# The seeded client account, from the one place the repo keeps them. Nothing here echoes a value.
set -a; source ../scripts/seeded-passwords.sh; set +a
export BEN_SCREENSHOTS=1
export TEST_RUNNER_BEN_SCREENSHOTS=1
export TEST_RUNNER_BEN_API_BASE_URL="${BEN_API_BASE_URL:-http://localhost:5252}"
export TEST_RUNNER_BEN_CLIENT_EMAIL="${BEN_CLIENT_EMAIL:-daniel.park@benco.dev}"
export TEST_RUNNER_BEN_CLIENT_PASSWORD="$BEN_CLIENT_PASSWORD"

if ! curl -fsS -o /dev/null --max-time 3 "$TEST_RUNNER_BEN_API_BASE_URL/api/public/build"; then
  echo "REFUSING: no API at $TEST_RUNNER_BEN_API_BASE_URL."
  echo "The Send screen signs in; without it frame 14 photographs the signed-out fallback."
  exit 1
fi

UDID=$(xcrun simctl list devices available | grep -m1 "$SIM_NAME (" | sed -E 's/.*\(([0-9A-F-]{36})\).*/\1/')
[[ -n "$UDID" ]] || { echo "no simulator called $SIM_NAME"; exit 1; }
echo "── $SIM_NAME ($UDID) ────────────────────────────────"

xcrun simctl boot "$UDID" 2>/dev/null || true
xcrun simctl bootstatus "$UDID" -b
xcrun simctl ui "$UDID" appearance dark
# Answer the camera question before it is asked. A simulator that has just been reset has no
# answer on file, so the first tap on the camera put iOS's own permission sheet over a black
# screen — and that is what frame 15 photographed on 2026-09-17. Granting it up front means the
# app reaches its own "No camera is available on this device." sentence, which the test knows to
# skip. simctl on older Xcodes has no `privacy camera`, so a failure here is not fatal.
xcrun simctl privacy "$UDID" grant camera com.ishaunted.ios 2>/dev/null || true

mkdir -p "$SHOT_DIR" "$OUT/app-preview"

echo "── Building once ────────────────────────────────────"
xcodebuild build-for-testing -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination "platform=iOS Simulator,id=$UDID" -quiet

echo "── The set ──────────────────────────────────────────"
rm -rf /tmp/shots-$DEVICE_KIND.xcresult
xcodebuild test-without-building -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination "platform=iOS Simulator,id=$UDID" \
  -only-testing:IsHauntedUITests/FieldKitScreenshotTests \
  -resultBundlePath /tmp/shots-$DEVICE_KIND.xcresult -quiet
# Exported into a directory of its own, NOT straight into the set. The export writes UUID names
# plus a manifest mapping each to the attachment's own name, and both of those used to land beside
# the set's finished frames — where the resize below then globbed the FINISHED ones and re-encoded
# them in place while this run's real captures sat unrenamed next to them. The set looked
# refreshed and was the old pictures, re-compressed (2026-09-17). Now: export apart, rename from
# the manifest, resize only what this run took, and copy over the set last.
RAW_SHOTS=/tmp/shots-$DEVICE_KIND-export
rm -rf "$RAW_SHOTS"
xcrun xcresulttool export attachments --path /tmp/shots-$DEVICE_KIND.xcresult --output-path "$RAW_SHOTS"

python3 - "$RAW_SHOTS" <<'PLACE'
import json, pathlib, re, sys
export = pathlib.Path(sys.argv[1])
manifest = json.loads((export / "manifest.json").read_text())
renamed = 0
for run in manifest if isinstance(manifest, list) else [manifest]:
    for shot in run.get("attachments") or []:
        # "04-field-kit_0_<uuid>.png" is the name the test gave it plus what Xcode adds.
        name = re.sub(r"_\d+_[0-9A-Fa-f-]{36}(?=\.png$)", "", shot.get("suggestedHumanReadableName") or "")
        exported = export / (shot.get("exportedFileName") or "")
        if not re.match(r"^\d\d-[a-z0-9-]+\.png$", name) or not exported.exists():
            continue
        exported.rename(export / name)
        renamed += 1
print(f"   {renamed} frames named")
PLACE

# `find`, not a glob: with every attachment renamed there is nothing left for the pattern to match,
# and zsh treats an unmatched glob as an error — which killed the run before it resized or copied
# anything, leaving the set untouched and the script reporting success (2026-09-17).
rm -f "$RAW_SHOTS/manifest.json"
find "$RAW_SHOTS" -maxdepth 1 -name '????????-????-????-????-????????????.png' -delete

# The iPhone's native 1320×2868 is not a size the store takes; 1242×2688 is.
if [[ "$DEVICE_KIND" == "iphone" ]]; then
  for png in "$RAW_SHOTS"/*.png; do
    sips -Z 2698 --resampleWidth 1242 "$png" >/dev/null
    sips --cropToHeightWidth 2688 1242 "$png" >/dev/null
  done
fi

# The camera frame is never real on a simulator: there is no camera, so the best it can show is
# the app saying so. It belongs in the set only from a capture run on a real phone, which this
# script cannot be (README). Dropped here rather than trusted not to appear.
rm -f "$RAW_SHOTS/15-fieldkit-camera.png"

# A frame the run did not take keeps whatever the set already holds, rather than being blanked.
cp "$RAW_SHOTS"/*.png "$SHOT_DIR"/

echo "── The preview ──────────────────────────────────────"
RAW=/tmp/demo-$DEVICE_KIND.mp4
rm -f "$RAW"
xcrun simctl io "$UDID" recordVideo --codec h264 --force "$RAW" &
REC=$!
sleep 2
TEST_RUNNER_BEN_DEMO_DRIVE=1 xcodebuild test-without-building -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination "platform=iOS Simulator,id=$UDID" \
  -only-testing:IsHauntedUITests/FieldKitDemoDriveUITests -quiet || true
sleep 2
kill -INT $REC 2>/dev/null || true
wait $REC 2>/dev/null || true

swiftc -O "$OUT/tools/preview.swift" -o /tmp/preview 2>/dev/null || swiftc -O screenshots-1.0.2/tools/preview.swift -o /tmp/preview
SILENT="$OUT/app-preview/$DEVICE_KIND-preview.mp4"
/tmp/preview cut "$RAW" /tmp/cut-$DEVICE_KIND.mp4 ${=CUT}

# App Review refused a preview with NO audio stream at all ("unsupported or corrupted audio",
# 2026-09-09), so a silent track is not optional. -c:v copy keeps the captured picture untouched.
FFMPEG=../Ben.Video.Sidecar/ffmpeg/osx-arm64/ffmpeg
"$FFMPEG" -y -i /tmp/cut-$DEVICE_KIND.mp4 -f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 \
  -c:v copy -c:a aac -b:a 128k -shortest -movflags +faststart "$SILENT" 2>/dev/null

echo ""
echo "── What came out ────────────────────────────────────"
ls -la "$SHOT_DIR" | tail -n +2
ls -la "$SILENT"
echo ""
echo "Look at every frame before uploading. The capture is scripted; the judgement is not."
