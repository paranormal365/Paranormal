#!/usr/bin/env bash
# Builds the downloadable macOS disk image for the sidecar.
#
#   installer/macos/build-dmg.sh [osx-arm64|osx-x64]
#
# Produces  installer/dist/BenVideoSidecar-<rid>.dmg
#
# WHY A DMG AND NOT A DRAG-TO-APPLICATIONS ONE:
# The usual disk image holds an .app and a symlink to /Applications, and dragging is the install.
# That shape does not fit here. The sidecar is a background service: it has no window, it is
# LSBackgroundOnly, and it is useless until a LaunchAgent exists to start it at login and a pairing
# token exists for the browser to authenticate against. Dragging the bundle somewhere would leave
# the user with an app that never runs and an editor that never finds it. So the image holds the
# app plus one double-clickable installer that does the whole job, and the app is deliberately NOT
# presented as something to drag.
#
# GATEKEEPER, MEASURED RATHER THAN ASSUMED:
# This image is unsigned and un-notarized, and a disk image downloaded with a browser arrives
# carrying com.apple.quarantine. Quarantine gates Launch Services — double-click, `open`, Finder —
# and does NOT gate `exec`. So:
#   * Double-clicking the installer inside the image is REFUSED on a default Mac. Right-click ->
#     Open, then "Open" in the dialog, gets through, because that path records user consent.
#   * Running it from a terminal works with no dialog at all.
#   * Once installed, launchd execs the service directly and quarantine never enters into it — and
#     install.sh clears the attribute from the copy it places in ~/Applications anyway.
# There is no scripting trick that removes that first dialog. The fix is a Developer ID signature
# over the bundle and the image plus notarization, which needs a paid Apple Developer account. Until
# that exists, the README inside the image tells the truth rather than letting someone conclude the
# download is broken.
set -euo pipefail

RID="${1:-}"
if [[ -z "$RID" ]]; then
  [[ "$(uname -m)" == "arm64" ]] && RID="osx-arm64" || RID="osx-x64"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER_DIR="$(dirname "$SCRIPT_DIR")"
DIST="$INSTALLER_DIR/dist"
APP="$DIST/BenVideoSidecar.app"
DMG="$DIST/BenVideoSidecar-$RID.dmg"
STAGE="$DIST/dmg-stage-$RID"
VOLNAME="BenVideo Sidecar"

if [[ ! -d "$APP" ]]; then
  echo "error: $APP not found." >&2
  echo "       Run: $SCRIPT_DIR/build.sh $RID" >&2
  exit 1
fi

# The app bundle in dist/ is whatever the last build.sh produced, for whichever architecture. A DMG
# named for one RID holding another RID's binaries would be undetectable until someone ran it on
# the wrong Mac, so check rather than trust the argument.
if [[ ! -x "$APP/Contents/MacOS/ffmpeg/$RID/ffmpeg" ]]; then
  echo "error: $APP does not carry ffmpeg for $RID — it was built for a different architecture." >&2
  echo "       Run: $SCRIPT_DIR/build.sh $RID" >&2
  exit 1
fi

echo "==> Staging the disk image for $RID"
rm -rf "$STAGE" "$DMG"
mkdir -p "$STAGE"

cp -R "$APP" "$STAGE/BenVideoSidecar.app"

# install.sh already prefers an app sitting beside it over the repo's dist/ layout, precisely so a
# downloaded copy works without knowing this repo's shape. Copying it under a .command name is what
# makes it double-clickable in Finder; the script itself is unchanged.
cp "$SCRIPT_DIR/install.sh"   "$STAGE/Install BenVideo Sidecar.command"
cp "$SCRIPT_DIR/uninstall.sh" "$STAGE/Uninstall BenVideo Sidecar.command"
chmod +x "$STAGE/Install BenVideo Sidecar.command" "$STAGE/Uninstall BenVideo Sidecar.command"

cat > "$STAGE/README.txt" <<'TXT'
BenVideo Sidecar for macOS
==========================

The sidecar is a small background helper. The video editor in your browser hands it the heavy work
— rendering and exporting — so that work runs at native speed instead of inside the browser tab.
It listens only on 127.0.0.1, so nothing it does is reachable from the network.


INSTALLING

  RIGHT-CLICK "Install BenVideo Sidecar.command", choose Open, then click Open in the dialog.

Right-click rather than double-click, and this is worth thirty seconds of explanation because
double-clicking will simply refuse with "cannot be opened because it is from an unidentified
developer", which reads like a broken download.

This build is not yet signed with an Apple Developer ID. macOS marks anything downloaded from the
web as quarantined, and refuses to launch unsigned quarantined items from Finder. Opening it via
right-click records that you chose to run it, and macOS then allows it. Nothing else about the
install differs, and the installed service is unaffected from then on.

The installer needs no password. Everything it writes lives in your own home folder:

  ~/Applications/BenVideoSidecar.app                    the helper itself
  ~/Library/LaunchAgents/video.ben.sidecar.plist        starts it when you log in
  ~/Library/Logs/BenVideo/sidecar.log                   its log

When it finishes it opens a pairing page in your browser showing a six-digit code. Type that code
into the editor under Settings -> Native acceleration. That code is what lets your browser talk to
the helper, and it is why installing alone is not enough.


IF YOU PREFER THE TERMINAL

Nothing is gated there, because the quarantine rule applies to Finder rather than to running a
program directly:

  "/Volumes/BenVideo Sidecar/Install BenVideo Sidecar.command"


REMOVING IT

Right-click "Uninstall BenVideo Sidecar.command" and choose Open. That stops the service and
removes the app and the LaunchAgent, keeping your pairing so a later reinstall still works. The
uninstaller is also copied to ~/Applications during install, so you do not need this disk image
again.
TXT

echo "==> Creating $DMG"
# UDZO: compressed and read-only, which is what a download should be. hdiutil carries the execute
# bits through from the staging folder, so the .command files stay runnable inside the image.
hdiutil create \
  -volname "$VOLNAME" \
  -srcfolder "$STAGE" \
  -ov -format UDZO \
  "$DMG" >/dev/null

rm -rf "$STAGE"

echo "==> Disk image: $DMG ($(du -sh "$DMG" | cut -f1))"
echo
echo "Unsigned: users must right-click -> Open the installer inside. See README.txt in the image."
