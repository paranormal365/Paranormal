#!/bin/zsh
# BenKit unit tests — run on the Mac host, no simulator needed.
set -euo pipefail
cd "$(dirname "$0")/.."
swift test --package-path BenKit "$@"
