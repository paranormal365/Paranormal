# Fetches the feed's NSFW screening model (item 186 F5b) — Windows twin of
# get-screener-model.sh, for the deployment host.
#
# The model is NOT committed to git (87 MB). Run once on the machine that BUILDS
# the publish output (the model flows into publish automatically when present —
# see the conditional Content item in Ben.Data.WebApi.csproj). Without it the
# API runs manual-only screening: every feed photo/video waits in the moderator
# queue, the startup log says so, and /admin/feed-reports shows it.
#
# Model: onnx-community/nsfw_image_detection-ONNX (ONNX export of
# Falconsai/nsfw_image_detection, ViT, Apache-2.0). Two classes: normal, nsfw.
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $repoRoot 'Ben.Data.WebApi\Models\nsfw\model_quantized.onnx'
$url = 'https://huggingface.co/onnx-community/nsfw_image_detection-ONNX/resolve/main/onnx/model_quantized.onnx'

if (Test-Path $dest) {
    Write-Host "Already present: $dest ($([math]::Round((Get-Item $dest).Length / 1MB)) MB)"
    exit 0
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
Write-Host 'Downloading NSFW screening model (~87 MB)...'
Invoke-WebRequest -Uri $url -OutFile "$dest.part" -UseBasicParsing
Move-Item "$dest.part" $dest
Write-Host "Done: $dest ($([math]::Round((Get-Item $dest).Length / 1MB)) MB)"
