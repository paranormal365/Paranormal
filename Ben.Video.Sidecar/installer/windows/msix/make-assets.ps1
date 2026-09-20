<#
.SYNOPSIS
    Regenerates the MSIX tile images in Assets\ from the project artwork.

.DESCRIPTION
    The generated PNGs ARE committed beside this script, so building the package needs neither this
    script nor the artwork. Run it only when the artwork changes, then commit what it writes - that
    way the package is reproducible from a clean checkout, and the images are reviewable as files
    rather than as the output of a script nobody runs.

    The source art is a transparent ghost on nothing. Windows draws these tiles against the user's
    accent colour or a background colour named in the manifest, so the transparency is kept rather
    than flattened, and the art is inset a little so it does not touch the tile edge.

    Deliberately kept ASCII: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a stray
    em-dash in a comment becomes a parse error rather than a typo.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Source,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here 'Assets' }
if (-not $Source) {
    # ...\Ben.Video.Sidecar\installer\windows\msix -> up four to the repository root
    $repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $here)))
    $Source = Join-Path $repo 'ProjectNotes\Art\ghost glass 699x640.png'
}
if (-not (Test-Path $Source)) { throw "No source artwork at $Source" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# name -> width x height. The three the manifest names are required; the target-size variants are
# what Windows uses for the taskbar and the app list, where a 44px tile would be resampled badly.
$sizes = [ordered]@{
    'Square44x44Logo.png'                                = @(44, 44)
    'Square44x44Logo.targetsize-24_altform-unplated.png' = @(24, 24)
    'Square44x44Logo.targetsize-256.png'                 = @(256, 256)
    'Square150x150Logo.png'                              = @(150, 150)
    'Wide310x150Logo.png'                                = @(310, 150)
    'StoreLogo.png'                                      = @(50, 50)
}

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
try {
    foreach ($name in $sizes.Keys) {
        $w, $h = $sizes[$name]

        $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

            # Fit inside 88% of the shorter side, keeping the aspect ratio, centred. Windows' own
            # icon guidance leaves a margin; without one the ghost is clipped by the tile's corners.
            $box = [Math]::Min($w, $h) * 0.88
            $scale = [Math]::Min($box / $src.Width, $box / $src.Height)
            $dw = [int][Math]::Round($src.Width * $scale)
            $dh = [int][Math]::Round($src.Height * $scale)
            $g.DrawImage($src, [int](($w - $dw) / 2), [int](($h - $dh) / 2), $dw, $dh)
        }
        finally { $g.Dispose() }

        $path = Join-Path $OutDir $name
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host ("  {0,-52} {1}x{2}" -f $name, $w, $h)
    }
}
finally { $src.Dispose() }

Write-Host ''
Write-Host "Wrote $((Get-ChildItem $OutDir -Filter *.png).Count) images to $OutDir"
Write-Host 'Commit them: the package build reads these files, not the artwork.'
