#!/usr/bin/env bash
# Item #70 phase 175 — removes the locally installed sidecar service.
#
#   installer/macos/uninstall.sh [--purge]
#
# By default this removes the SERVICE and leaves the sidecar's own data alone: the pairing token,
# the source cache and any retained segments. --purge removes those too, which un-pairs every
# browser (they hold the old token in localStorage and will need a new code).
set -euo pipefail

PURGE=false
[[ "${1:-}" == "--purge" ]] && PURGE=true

LABEL="video.ben.sidecar"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DEST_APP="$HOME/Applications/BenVideoSidecar.app"
CONFIG_DIR="$HOME/Library/Application Support/BenVideo/sidecar"
CACHE_DIR="$HOME/Library/Caches/BenVideo"
LOG_DIR="$HOME/Library/Logs/BenVideo"

echo "==> Stopping and unloading $LABEL"
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null || true

# launchd's KeepAlive means a still-running process would be respawned; make sure it is really gone
# before deleting the binary out from under it.
for _ in $(seq 1 10); do
  pgrep -f "BenVideoSidecar.app/Contents/MacOS/Ben.Video.Sidecar" >/dev/null 2>&1 || break
  sleep 0.5
done
pkill -f "BenVideoSidecar.app/Contents/MacOS/Ben.Video.Sidecar" 2>/dev/null || true

rm -f "$PLIST"
rm -rf "$DEST_APP"
echo "==> Removed the app and the LaunchAgent"

if $PURGE; then
  rm -rf "$CONFIG_DIR" "$CACHE_DIR" "$LOG_DIR"
  echo "==> Purged config, cache and logs (every paired browser must re-pair)"
else
  cat <<EOF
==> Left in place (use --purge to remove):
      $CONFIG_DIR   (pairing token)
      $CACHE_DIR    (source cache, retained segments)
      $LOG_DIR      (logs)
EOF
fi
