#!/usr/bin/env bash
# Item #70 phase 175 — mints a new pairing code for the installed sidecar and restarts it.
#
#   installer/macos/pair.sh
#
# The restart is the point. PairingTokenStore reads the token file once at startup and holds only a
# hash; `--pair` runs as a separate process and rewrites that file, so without a restart the running
# service goes on accepting the OLD code and rejecting the new one with a 401 — which looks exactly
# like a mistyped code and sends you hunting in the wrong place. Rotating and restarting together is
# the only way these two can't drift apart.
set -euo pipefail

LABEL="video.ben.sidecar"
EXE="$HOME/Applications/BenVideoSidecar.app/Contents/MacOS/Ben.Video.Sidecar"
LOG="$HOME/Library/Logs/BenVideo/sidecar.log"

if [[ ! -x "$EXE" ]]; then
  echo "error: no installed sidecar at $EXE — run installer/macos/install.sh first." >&2
  exit 1
fi

"$EXE" --pair

echo "==> Restarting the service so it picks up the new code"
launchctl kickstart -k "gui/$(id -u)/$LABEL"

# Probe the port range rather than the log — see the note in install.sh about why an append-only
# log makes a stale port look like a live one.
for _ in $(seq 1 30); do
  for candidate in $(seq 43117 43121); do
    if curl -sS --max-time 2 "http://127.0.0.1:$candidate/v1/health" 2>/dev/null | grep -q '"protocolVersion"'; then
      echo "==> Back up on http://127.0.0.1:$candidate"
      exit 0
    fi
  done
  sleep 0.5
done

echo "warning: could not confirm the service came back — check $LOG" >&2
