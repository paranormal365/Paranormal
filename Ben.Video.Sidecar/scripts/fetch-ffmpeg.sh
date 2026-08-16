#!/usr/bin/env bash
# Item #38 phase E — downloads and SHA-256-verifies the pinned ffmpeg/ffprobe binaries for one RID
# from ffmpeg-manifest.json, refusing to proceed on a hash mismatch (threat T7, supply chain). Run
# this before publishing a real per-OS build; a normal `dotnet build`/`dotnet test` of this repo
# never needs it — FfmpegLocator degrades gracefully when ffmpeg/<rid>/ doesn't exist.
#
# Usage: scripts/fetch-ffmpeg.sh <rid>   e.g. scripts/fetch-ffmpeg.sh osx-arm64
#
# Item #70 phase 174 rewrote the verification and extraction halves:
#
#   * TWO hashes per tool, and they mean different things. 'archiveSha256' is checked against the
#     downloaded archive BEFORE it is unpacked, so a tampered download never reaches the extractor;
#     'sha256' is checked against the EXTRACTED BINARY, because that is the exact byte sequence
#     FfmpegLocator.VerifyIntegrity re-hashes at every startup. Before this the script compared the
#     archive against 'sha256' — the field the runtime reads as a binary hash — so the first real
#     (non-placeholder) pin would have passed the fetch and then failed the startup check, with the
#     sidecar refusing to serve job endpoints for a binary it had just verified. Latent only
#     because every path exercised so far used placeholders or the dev-path overrides.
#   * Extraction goes through a temp dir and `find`s the binary by name instead of assuming a
#     layout. Sources disagree: martin-riedl/evermeet put the binary at the archive root, BtbN
#     nests it under <name>/bin/. Searching handles both without per-source special-casing.
#   * ffprobe may live in its own archive ('ffprobeUrl'), which is how the macOS sources publish it.
set -euo pipefail

RID="${1:?Usage: fetch-ffmpeg.sh <rid>  (win-x64|osx-x64|osx-arm64|linux-x64)}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
MANIFEST="$PROJECT_DIR/ffmpeg-manifest.json"
OUT_DIR="$PROJECT_DIR/ffmpeg/$RID"

case "$RID" in
  win-*) FFMPEG_EXE="ffmpeg.exe"; FFPROBE_EXE="ffprobe.exe" ;;
  *)     FFMPEG_EXE="ffmpeg";     FFPROBE_EXE="ffprobe" ;;
esac

field() {
  python3 -c "
import json,sys
m = json.load(open('$MANIFEST'))
e = m.get('$RID')
if e is None:
    sys.stderr.write(\"error: no manifest entry for RID '$RID'.\n\"); sys.exit(2)
print(e.get('$1') or '')
"
}

URL=$(field url)
ARCHIVE_SHA=$(field archiveSha256)
BINARY_SHA=$(field sha256)
PROBE_URL=$(field ffprobeUrl)
PROBE_ARCHIVE_SHA=$(field ffprobeArchiveSha256)
PROBE_BINARY_SHA=$(field ffprobeSha256)

is_placeholder() { [[ -z "$1" || "$1" == *TODO* || "$1" =~ ^0+$ ]]; }

if is_placeholder "$URL" || is_placeholder "$BINARY_SHA"; then
  echo "error: ffmpeg-manifest.json still has placeholder values for '$RID'." >&2
  echo "       Pin a real release URL + the SHA-256 of its extracted binary before running this." >&2
  echo "       (osx-arm64 is pinned for real — see its _comment for how those values were produced.)" >&2
  exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT_DIR"

sha_of() { shasum -a 256 "$1" | cut -d' ' -f1; }

# Downloads $1, checks it against archive hash $2 (skipped, with a warning, when $2 is absent or a
# placeholder), and unpacks it into a fresh directory under $WORK whose path is echoed on stdout.
download_and_extract() {
  local url="$1" expected="$2" label="$3"
  local archive="$WORK/$label-archive" dest="$WORK/$label"
  mkdir -p "$dest"

  echo "Downloading $label for $RID..." >&2
  curl -fSL --retry 3 "$url" -o "$archive"

  if is_placeholder "$expected"; then
    echo "warning: no archiveSha256 pinned for $label — the archive itself was NOT verified." >&2
    echo "         The extracted binary is still hash-checked below." >&2
  else
    local actual; actual=$(sha_of "$archive")
    if [[ "$actual" != "$expected" ]]; then
      echo "error: SHA-256 mismatch for the downloaded $label archive ($RID)." >&2
      echo "       expected: $expected" >&2
      echo "       actual:   $actual" >&2
      echo "       Refusing to extract an unverified archive." >&2
      exit 1
    fi
    echo "$label archive hash verified." >&2
  fi

  case "$url" in
    *.zip)            unzip -o -q "$archive" -d "$dest" ;;
    *.tar.xz|*.txz)   tar -xJf "$archive" -C "$dest" ;;
    *.tar.gz|*.tgz)   tar -xzf "$archive" -C "$dest" ;;
    *) echo "error: unrecognized archive extension for $url" >&2; exit 1 ;;
  esac

  echo "$dest"
}

# Moves the first file named $2 found anywhere under $1 into $OUT_DIR, then checks it against the
# pinned binary hash $3. A mismatch deletes the file rather than leaving an unverified binary on
# disk where a later run (or a curious operator) might execute it.
install_binary() {
  local src_root="$1" exe="$2" expected="$3" required="$4"
  # `head -1` rather than find's -quit: -quit is a BSD/GNU extension and this script has to run on
  # both a developer's Mac and a Linux publish box.
  local found; found=$(find "$src_root" -type f -name "$exe" 2>/dev/null | head -n 1 || true)

  if [[ -z "$found" ]]; then
    if [[ "$required" == "required" ]]; then
      echo "error: no '$exe' found in the archive for $RID." >&2
      exit 1
    fi
    echo "warning: no '$exe' in this archive — probe/thumbnail offload will be unavailable." >&2
    return 0
  fi

  local target="$OUT_DIR/$exe"
  mv -f "$found" "$target"
  chmod +x "$target" 2>/dev/null || true

  if is_placeholder "$expected"; then
    # Only reachable for ffprobe: an ffmpeg placeholder already exited above. FfmpegLocator will
    # refuse to trust this binary at runtime (an unpinned ffprobe is exactly what T7 guards
    # against), so say so here rather than letting it look installed.
    echo "warning: no hash pinned for $exe — FfmpegLocator will NOT trust it at runtime." >&2
    return 0
  fi

  local actual; actual=$(sha_of "$target")
  if [[ "$actual" != "$expected" ]]; then
    rm -f "$target"
    echo "error: SHA-256 mismatch for the extracted $exe ($RID)." >&2
    echo "       expected: $expected" >&2
    echo "       actual:   $actual" >&2
    echo "       Removed it. This is the hash FfmpegLocator re-checks at startup." >&2
    exit 1
  fi
  echo "$exe verified: $actual"
}

FFMPEG_SRC=$(download_and_extract "$URL" "$ARCHIVE_SHA" "ffmpeg")
install_binary "$FFMPEG_SRC" "$FFMPEG_EXE" "$BINARY_SHA" "required"

# ffprobe: its own archive when the source publishes one, otherwise it rides along in the ffmpeg
# archive (BtbN's builds do). Either way it stays OPTIONAL — FfmpegLocator fails soft on a missing
# ffprobe, withholding only the probe/thumbnails capabilities.
if [[ -n "$PROBE_URL" ]] && ! is_placeholder "$PROBE_URL"; then
  PROBE_SRC=$(download_and_extract "$PROBE_URL" "$PROBE_ARCHIVE_SHA" "ffprobe")
else
  PROBE_SRC="$FFMPEG_SRC"
fi
install_binary "$PROBE_SRC" "$FFPROBE_EXE" "$PROBE_BINARY_SHA" "optional"

echo "Done: $OUT_DIR"
