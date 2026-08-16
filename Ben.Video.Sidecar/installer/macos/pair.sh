#!/usr/bin/env bash
# Opens the sidecar's pairing page in the default browser.
#
# This used to run `--pair`, which ROTATED the long token (un-pairing every browser) and needed a
# service restart to pick up. The 6-digit flow makes both problems go away: the page asks the
# RUNNING service to mint a short-lived code, the editor exchanges it for the existing long token,
# and previously paired browsers stay paired. Nothing restarts; nothing rotates.
set -euo pipefail

for candidate in $(seq 43117 43121); do
  if curl -sS --max-time 2 "http://127.0.0.1:$candidate/v1/health" 2>/dev/null | grep -q '"protocolVersion"'; then
    URL="http://127.0.0.1:$candidate/pair"
    echo "==> Opening $URL"
    open "$URL"
    exit 0
  fi
done

echo "error: no running sidecar found on ports 43117-43121." >&2
echo "       If it's installed, start it:  launchctl kickstart gui/$(id -u)/video.ben.sidecar" >&2
echo "       If not: installer/macos/install.sh" >&2
exit 1
