#!/usr/bin/env bash
# Item #70 phase 175 — builds an UNSIGNED, local-testing macOS install bundle for the sidecar.
#
#   installer/macos/build.sh [osx-arm64|osx-x64]
#
# Produces, under Ben.Video.Sidecar/installer/dist/:
#   BenVideoSidecar.app          the app directory, ready for install.sh
#   BenVideoSidecar-<rid>.pkg    the same thing as an unsigned installer package
#
# THIS IS A TESTING BUILD — not signed with a Developer ID, not notarized. What that does and does
# not mean on macOS, measured on this build rather than assumed:
#
#   * It RUNS. The SDK ad-hoc-signs the apphost, which is all Apple Silicon's kernel requires to
#     exec a binary at all, and launchd execs the service directly. The upstream ffmpeg/ffprobe are
#     in better shape than our own executable — they carry a real Developer ID signature.
#   * Quarantine (com.apple.quarantine) gates LAUNCH SERVICES — double-clicking, `open`, opening a
#     downloaded .pkg from Finder — NOT `exec`. A quarantined copy of this app still runs fine from
#     a shell or from a LaunchAgent; that was verified directly, so "downloading it breaks
#     everything" would be an overstatement.
#   * `spctl -a` DOES reject the .app ("code has no resources but signature indicates they must be
#     present"): the inner executable is ad-hoc signed but the bundle itself is not signed as a
#     bundle. So anything that routes through Gatekeeper assessment — a user double-clicking the
#     .pkg, or any managed/locked-down Mac — refuses it.
#
# Net: fine for local testing, wrong for distribution. Shipping needs a Developer ID signature over
# the whole bundle plus notarization. Do not treat this script as a distribution path.
set -euo pipefail

RID="${1:-}"
if [[ -z "$RID" ]]; then
  [[ "$(uname -m)" == "arm64" ]] && RID="osx-arm64" || RID="osx-x64"
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_DIR="$(dirname "$INSTALLER_DIR")"
DIST="$INSTALLER_DIR/dist"
APP="$DIST/BenVideoSidecar.app"
BUNDLE_ID="video.ben.sidecar"

echo "==> Building BenVideo sidecar installer for $RID (unsigned, testing only)"

# The bundled binaries are the whole point of installing rather than running from the build tree —
# a published app can't fall back to a dev-path override, so a missing ffmpeg here would produce an
# install that starts, passes its health check, and refuses every job with a 503.
if [[ ! -x "$PROJECT_DIR/ffmpeg/$RID/ffmpeg" ]]; then
  echo "error: no ffmpeg bundled for $RID." >&2
  echo "       Run: $PROJECT_DIR/scripts/fetch-ffmpeg.sh $RID" >&2
  exit 1
fi

rm -rf "$DIST"
mkdir -p "$APP/Contents/MacOS"

echo "==> dotnet publish (self-contained, so the target machine needs no .NET runtime)"
dotnet publish "$PROJECT_DIR/Ben.Video.Sidecar.csproj" \
  -c Release -r "$RID" --self-contained true \
  -o "$APP/Contents/MacOS" --nologo -v q

if [[ ! -x "$APP/Contents/MacOS/ffmpeg/$RID/ffmpeg" ]]; then
  echo "error: publish output has no ffmpeg/$RID — the csproj content glob did not fire." >&2
  exit 1
fi

# CFBundlePackageType/CFBundleExecutable are what make this a bundle rather than a folder with a
# suffix. LSBackgroundOnly keeps it out of the Dock: it is a companion process the browser
# discovers by port scan, not something with a window to show.
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>BenVideo Sidecar</string>
    <key>CFBundleDisplayName</key>       <string>BenVideo Sidecar</string>
    <key>CFBundleIdentifier</key>        <string>$BUNDLE_ID</string>
    <key>CFBundleVersion</key>           <string>1.0.0</string>
    <key>CFBundleShortVersionString</key><string>1.0.0</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>CFBundleExecutable</key>        <string>Ben.Video.Sidecar</string>
    <key>LSMinimumSystemVersion</key>    <string>13.0</string>
    <key>LSBackgroundOnly</key>          <true/>
</dict>
</plist>
PLIST

SIZE=$(du -sh "$APP" | cut -f1)
echo "==> App bundle: $APP ($SIZE)"

# pkgbuild only — no productbuild/distribution XML, no welcome or licence screens. This exists so
# the install can be exercised in its real shape; it is not a shippable installer.
#
# --install-location puts it in the USER's ~/Applications so nothing here needs root. A root-domain
# pkg would then have to figure out which user's LaunchAgents directory to write to, which is
# exactly the kind of complexity a testing build should not carry.
PKG="$DIST/BenVideoSidecar-$RID.pkg"
pkgbuild \
  --identifier "$BUNDLE_ID" \
  --version "1.0.0" \
  --install-location "$HOME/Applications/BenVideoSidecar.app" \
  --root "$APP" \
  "$PKG" >/dev/null

echo "==> Package:    $PKG ($(du -sh "$PKG" | cut -f1))"
echo
echo "Next:  installer/macos/install.sh"
