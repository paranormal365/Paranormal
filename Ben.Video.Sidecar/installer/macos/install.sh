#!/usr/bin/env bash
# Item #70 phase 175 — installs the locally built sidecar as a per-user background service.
#
#   installer/macos/install.sh
#
# No sudo: everything lands under $HOME. A LaunchAgent (per-user, runs at login) is the right shape
# rather than a LaunchDaemon (system-wide, runs as root before login) — the sidecar serves one
# person's browser, binds 127.0.0.1 only, and reads/writes that user's own cache. Running it as root
# would gain nothing and hand a loopback HTTP server root privileges.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER_DIR="$(dirname "$SCRIPT_DIR")"
# Two layouts, one script. In the repo this lives at installer/macos/ with the app two levels up
# under installer/dist/; in the DOWNLOADED zip it sits directly beside the app, because a tester
# should not need to know this repo's directory shape. Beside-me wins when both exist — the zip
# is what a tester is holding.
if [[ -d "$SCRIPT_DIR/BenVideoSidecar.app" ]]; then
  SRC_APP="$SCRIPT_DIR/BenVideoSidecar.app"
else
  SRC_APP="$INSTALLER_DIR/dist/BenVideoSidecar.app"
fi
DEST_APP="$HOME/Applications/BenVideoSidecar.app"
LABEL="video.ben.sidecar"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOG_DIR="$HOME/Library/Logs/BenVideo"
EXE="$DEST_APP/Contents/MacOS/Ben.Video.Sidecar"

if [[ ! -d "$SRC_APP" ]]; then
  echo "error: $SRC_APP not found — run installer/macos/build.sh first." >&2
  exit 1
fi

# Unload before overwriting: replacing the binary under a running service leaves launchd supervising
# a process whose executable no longer exists, and KeepAlive then respawns from the old inode.
if launchctl list "$LABEL" >/dev/null 2>&1; then
  echo "==> Stopping the running sidecar"
  launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null || true
fi

echo "==> Installing to $DEST_APP"
mkdir -p "$HOME/Applications" "$LOG_DIR" "$(dirname "$PLIST")"
rm -rf "$DEST_APP"
cp -R "$SRC_APP" "$DEST_APP"

# Belt and braces for the quarantine story: if this .app ever arrived via a browser or a shared
# volume it would carry com.apple.quarantine and launchd would refuse to start an unsigned copy.
# Clearing it here is honest for a TESTING build and is exactly the step that a signed+notarized
# build would not need.
xattr -dr com.apple.quarantine "$DEST_APP" 2>/dev/null || true

echo "==> Writing LaunchAgent $PLIST"
cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>              <string>$LABEL</string>
    <key>ProgramArguments</key>   <array><string>$EXE</string></array>
    <key>RunAtLoad</key>          <true/>
    <!-- Restart if it dies, but not if it exits cleanly (a deliberate shutdown should stay down). -->
    <key>KeepAlive</key>          <dict><key>SuccessfulExit</key><false/></dict>
    <key>ProcessType</key>        <string>Adaptive</string>
    <key>StandardOutPath</key>    <string>$LOG_DIR/sidecar.log</string>
    <key>StandardErrorPath</key>  <string>$LOG_DIR/sidecar.log</string>
</dict>
</plist>
PLIST

# Pairing BEFORE the service starts, and this ordering is not cosmetic. PairingTokenStore reads the
# token file exactly once, at startup, and keeps only a hash in memory. `--pair` runs as a separate
# process and rewrites that file, so a code minted after the service is up is a code the running
# service rejects with a 401 — which presents as "the pairing code doesn't work" rather than as a
# staleness problem. (Measured: this script did exactly that on its first run.) Anything that
# rotates the code afterwards must restart the service; that is what pair.sh is for.
# 6-digit flow (pairing v2): the terminal no longer displays anything. Once the service is up,
# this script opens its /pair page in the default browser — the page mints a short-lived 6-digit
# code that the editor exchanges for the long token. The token file is only pre-created here on
# first install (by --pair, output suppressed) so the service has a stable token to hand out;
# nothing the user must read scrolls by in a terminal.
TOKEN_FILE="$HOME/Library/Application Support/BenVideo/sidecar/pairing-token"
if [[ ! -f "$TOKEN_FILE" ]]; then
  "$EXE" --pair > /dev/null
fi

echo "==> Loading the service"
launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load "$PLIST"

# Confirm it actually came up by probing the health endpoint across the port range — the same
# discovery the browser performs (SidecarProtocol.DefaultPort/DefaultPortScanRange), so this also
# proves the browser will find it.
#
# Deliberately NOT by scraping "listening on" out of the log: that log is append-only across
# restarts, so on a reinstall the scrape matched a line from the PREVIOUS run and raced ahead of the
# new process, producing a scary and completely false "ffmpegIntegrityOk is not true" warning.
# Asking the running service is the only answer that can't be stale.
PORT=""
HEALTH=""
for _ in $(seq 1 30); do
  for candidate in $(seq 43117 43121); do
    body=$(curl -sS --max-time 2 "http://127.0.0.1:$candidate/v1/health" 2>/dev/null || true)
    if [[ "$body" == *'"protocolVersion"'* ]]; then
      PORT="$candidate"; HEALTH="$body"; break 2
    fi
  done
  sleep 0.5
done

if [[ -z "$PORT" ]]; then
  echo "error: the sidecar did not answer /v1/health on 43117-43121 within 15s." >&2
  echo "       Check $LOG_DIR/sidecar.log" >&2
  exit 1
fi
echo
echo "==> Running on http://127.0.0.1:$PORT"
echo "    health: $HEALTH"
case "$HEALTH" in
  *'"ffmpegIntegrityOk":true'*) ;;
  *) echo
     echo "WARNING: ffmpegIntegrityOk is not true — the installed app will refuse every job with a"
     echo "         503. The bundled binaries failed their manifest hash check." >&2 ;;
esac

# Optional install ping. Only fires when a site URL is configured — the installer has no business
# guessing one, and a sidecar installed for local use should not be phoning anywhere by default.
# Anonymous by nature: nobody has signed in yet. The pairing event (sent by the editor, with a
# token) is what attaches a person to this installation.
if [[ -n "${BEN_API_URL:-}" ]]; then
  INSTALL_ID_FILE="$HOME/Library/Application Support/BenVideo/sidecar/install-id"
  if [[ -f "$INSTALL_ID_FILE" ]]; then
    INSTALL_ID="$(cat "$INSTALL_ID_FILE")"
    VERSION="$(defaults read "$DEST_APP/Contents/Info.plist" CFBundleShortVersionString 2>/dev/null || echo "")"
    RID="$(uname -m | sed 's/arm64/osx-arm64/; s/x86_64/osx-x64/')"
    curl -sS --max-time 5 -X POST "${BEN_API_URL%/}/api/sidecar-telemetry/installs" \
      -H "Content-Type: application/json" \
      -d "{\"installId\":\"$INSTALL_ID\",\"version\":\"$VERSION\",\"platform\":\"$RID\"}" \
      >/dev/null 2>&1 && echo "==> Reported this install to ${BEN_API_URL%/}" \
      || echo "note: could not reach ${BEN_API_URL%/} to report the install (harmless)"
  fi
fi

echo
echo "==> Opening the pairing page — type the 6-digit code into the editor"
echo "    (Settings -> Native acceleration). Already-paired browsers keep working."
open "http://127.0.0.1:$PORT/pair"

cat <<EOF

Installed:
  app      $DEST_APP
  agent    $PLIST
  logs     $LOG_DIR/sidecar.log

It starts automatically at login. To remove it: installer/macos/uninstall.sh
EOF
