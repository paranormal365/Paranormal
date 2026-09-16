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
xcrun xcresulttool export attachments --path /tmp/shots-$DEVICE_KIND.xcresult --output-path "$SHOT_DIR"

# The iPhone's native 1320×2868 is not a size the store takes; 1242×2688 is.
if [[ "$DEVICE_KIND" == "iphone" ]]; then
  for png in "$SHOT_DIR"/*.png; do
    sips -Z 2698 --resampleWidth 1242 "$png" >/dev/null
    sips --cropToHeightWidth 2688 1242 "$png" >/dev/null
  done
fi

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
