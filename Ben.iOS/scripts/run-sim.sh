#!/bin/zsh
# Build, install, and launch on a booted simulator.
#   ./scripts/run-sim.sh                          # iPhone 17 Pro
#   ./scripts/run-sim.sh "iPad Pro 13-inch (M5)"  # iPad
#   OPEN_LINK="https://ishaunted.com/events" ./scripts/run-sim.sh
set -euo pipefail
cd "$(dirname "$0")/.."

DEVICE="${1:-iPhone 17 Pro}"

xcrun simctl boot "$DEVICE" 2>/dev/null || true
xcodebuild -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination "platform=iOS Simulator,name=$DEVICE" \
  CODE_SIGNING_ALLOWED=NO build | grep -E "BUILD (SUCCEEDED|FAILED)"

APP=$(find ~/Library/Developer/Xcode/DerivedData/IsHaunted-*/Build/Products/Debug-iphonesimulator \
  -maxdepth 1 -name "IsHaunted.app" | head -1)
UDID=$(xcrun simctl list devices booted | grep "$DEVICE" | grep -oE '[A-F0-9-]{36}' | head -1)

xcrun simctl install "$UDID" "$APP"
if [[ -n "${OPEN_LINK:-}" ]]; then
  xcrun simctl launch "$UDID" com.ishaunted.ios -openLink "$OPEN_LINK"
else
  xcrun simctl launch "$UDID" com.ishaunted.ios
fi
open -a Simulator
