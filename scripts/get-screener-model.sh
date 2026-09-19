#!/bin/zsh
# Fetches the feed's NSFW screening model (item 186 F5b).
#
# The model is NOT committed to git (87 MB). Run this once per machine — dev
# boxes and the deployment host alike — and the API picks it up on next start.
# Without the file the API falls back to manual-only screening (every photo and
# video waits in the moderator queue) and says so loudly in the startup log and
# on /admin/feed-reports.
#
# Model: onnx-community/nsfw_image_detection-ONNX (the ONNX export of
# Falconsai/nsfw_image_detection, ViT, Apache-2.0). Two classes: normal, nsfw.
set -euo pipefail
cd "$(dirname "$0")/.."

DEST="Ben.Data.WebApi/Models/nsfw/model_quantized.onnx"
URL="https://huggingface.co/onnx-community/nsfw_image_detection-ONNX/resolve/main/onnx/model_quantized.onnx"

if [[ -f "$DEST" ]]; then
  echo "Already present: $DEST ($(du -h "$DEST" | cut -f1))"
  exit 0
fi

echo "Downloading NSFW screening model (~87 MB)..."
curl -L --fail --progress-bar -o "$DEST.part" "$URL"
mv "$DEST.part" "$DEST"
echo "Done: $DEST ($(du -h "$DEST" | cut -f1))"
