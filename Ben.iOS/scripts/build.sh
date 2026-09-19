#!/bin/zsh
# Unsigned simulator build — CI-safe (no signing identity required).
set -euo pipefail
cd "$(dirname "$0")/.."
xcodebuild -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination 'generic/platform=iOS Simulator' \
  CODE_SIGNING_ALLOWED=NO build "$@"
