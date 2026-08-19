#!/usr/bin/env bash
# Builds an UNSIGNED Windows package for the sidecar, ready to hand to a tester.
#
#   installer/windows/build.sh
#
# Produces installer/dist/BenVideoSidecar-win-x64/ and a zip of it, containing:
#   app/                the self-contained sidecar, its ffmpeg pair, and the manifest
#   install.ps1         copies it under %LOCALAPPDATA%, unblocks it, starts it, sets autostart
#   uninstall.ps1       the reverse
#   README.txt          what a tester needs, including what "unsigned" costs them
#
# This cross-publishes from macOS, which .NET does happily. It also means the result CANNOT be run
# or tested here — the first execution of this build is on somebody's Windows machine. That is a
# real limitation, not a formality: SmartScreen behaviour and antivirus reactions are exactly the
# things this script cannot verify.
#
# UNSIGNED means Authenticode-unsigned. On first run SmartScreen shows "Windows protected your PC"
# (More info -> Run anyway), and some antivirus products quarantine an unsigned single-file
# executable outright. install.ps1 handles the part that CAN be handled — the mark of the web —
# but nothing here removes the SmartScreen prompt. Signing is what removes it.
set -euo pipefail

RID="win-x64"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALLER_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_DIR="$(dirname "$INSTALLER_DIR")"
DIST="$INSTALLER_DIR/dist"
PKG="$DIST/BenVideoSidecar-$RID"

echo "==> Building the BenVideo sidecar for $RID (unsigned)"

# Without these the install starts, passes its health check, and then refuses every job with a 503
# — a failure that looks like a broken sidecar rather than a broken build.
if [[ ! -f "$PROJECT_DIR/ffmpeg/$RID/ffmpeg.exe" ]]; then
  echo "error: no ffmpeg bundled for $RID." >&2
  echo "       Run: $PROJECT_DIR/scripts/fetch-ffmpeg.sh $RID" >&2
  exit 1
fi

rm -rf "$PKG"
mkdir -p "$PKG/app"

echo "==> dotnet publish (self-contained, so the target machine needs no .NET runtime)"
# Through the project's own win-x64 profile rather than loose flags: the profile is where
# PublishSingleFile lives, and passing -c/-r by hand quietly bypasses it — the result still works,
# but it is a folder of ~200 loose DLLs instead of one executable, which is a worse thing to ask
# somebody to trust and a worse thing for antivirus to look at.
#
# BundledFfmpegRid is passed explicitly, and it is load-bearing. The csproj derives it from
# $(RuntimeIdentifier), falling back to the SDK's host RID — but a publish PROFILE supplies the RID
# too late for that property to see it, so the fallback wins and the build bundles the ffmpeg of
# the machine doing the building. Cross-publishing Windows from a Mac then produces a Windows
# package containing macOS binaries, which fails at the far end as "no ffmpeg for win-x64" on a
# tester's machine rather than here.
dotnet publish "$PROJECT_DIR/Ben.Video.Sidecar.csproj" \
  -p:PublishProfile="$RID" -p:BundledFfmpegRid="$RID" \
  -p:DebugType=none -p:DebugSymbols=false \
  -o "$PKG/app" --nologo -v q

if [[ ! -f "$PKG/app/Ben.Video.Sidecar.exe" ]]; then
  echo "error: publish produced no Ben.Video.Sidecar.exe" >&2
  exit 1
fi

# The runtime re-hashes these at startup against ffmpeg-manifest.json, so both have to travel.
if [[ ! -f "$PKG/app/ffmpeg/$RID/ffmpeg.exe" || ! -f "$PKG/app/ffmpeg-manifest.json" ]]; then
  echo "error: publish output is missing ffmpeg/$RID or ffmpeg-manifest.json" >&2
  exit 1
fi

cp "$SCRIPT_DIR/install.ps1" "$SCRIPT_DIR/uninstall.ps1" "$PKG/"

cat > "$PKG/README.txt" <<'TXT'
BenVideo Sidecar for Windows — TEST BUILD
=========================================

The sidecar is optional. It makes the video editor faster by doing the heavy work — decoding,
rendering, exporting — on your machine directly instead of inside the browser's sandbox. Without
it the editor still works; it is just slower on long projects.

It runs only on your own computer and listens only to your own machine. It is not a server.

THIS BUILD IS NOT SIGNED
------------------------
Windows will warn you about it, and the warnings are accurate: nothing here proves who wrote this
software. You are trusting the person who sent you the link. Expect:

  * "Windows protected your PC" on first run. Click "More info", then "Run anyway".
  * Some antivirus tools may quarantine it. If it vanishes after install, that is what happened.

Installing
----------
1. Unzip this folder somewhere (not inside the zip viewer — actually extract it).
2. Right-click install.ps1 and choose "Run with PowerShell".
   If that is blocked, open PowerShell in this folder and run:
       powershell -ExecutionPolicy Bypass -File install.ps1
3. A pairing page opens in your browser showing a six-digit code.
4. In the video editor, click the sidecar chip in the toolbar and type that code in.

The sidecar starts by itself when you sign in from then on.

Pairing is per browser and per site address. If you open the editor in a different browser, pair
again — the code is single-use and expires after ten minutes.

Where it goes
-------------
  %LOCALAPPDATA%\BenVideoSidecar        the application
  %LOCALAPPDATA%\BenVideoSidecar\logs   its log

Nothing is installed outside your own user profile, and it never asks for administrator rights.

Removing it
-----------
Run uninstall.ps1 the same way. The editor goes back to rendering in the browser.
TXT

echo "==> Zipping"
( cd "$DIST" && rm -f "BenVideoSidecar-$RID.zip" && zip -qr "BenVideoSidecar-$RID.zip" "BenVideoSidecar-$RID" )

echo
echo "Built:"
echo "  $PKG"
echo "  $DIST/BenVideoSidecar-$RID.zip  ($(du -h "$DIST/BenVideoSidecar-$RID.zip" | cut -f1))"
echo
echo "Not signed, and not runnable from this machine — a Windows tester is the first to execute it."
