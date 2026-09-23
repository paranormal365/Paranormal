<#
.SYNOPSIS
    Renders the Microsoft Store artwork: poster art and the square tile icons.

.DESCRIPTION
    Five PNGs from two compositions.

      poster.html -> poster-1440x2160.png  (2:3 poster art, twice the Store's 720x1080 minimum)
                     poster-1080x1080.png  (the square)

      icon.html   -> icon-300x300.png, icon-150x150.png, icon-71x71.png  (app tile icons)

    There is no image editor on this machine, and this wants none. The compositions are CSS, which
    means they diff, they re-render at any size, and they are built from the SITE'S OWN font files
    and wordmark markup rather than someone's recollection of them. To change the art, edit the
    HTML and run this again.

    THE ICONS ARE NOT TRANSPARENT, ON PURPOSE. The haunted house in the logo is negative space -
    the dark ground showing through - so on a light background the house vanishes and the white
    ghost dissolves. icon.html has the evidence and the reasoning.

    EVERY ASSET IS INLINED BEFORE RENDERING, and that is the whole reason this script is more than
    a one-liner. Headless Chrome will happily screenshot a page whose subresources have not
    arrived yet: it produced a completely blank 150x150 tile (logo.svg missing) and, on another
    run, a poster with its entire backdrop missing (editor-shot.png missing) - both correctly
    sized, both passing every check that looks at a file rather than at a picture.
    --virtual-time-budget does not reliably prevent it. So the stylesheet, the fonts, the SVG and
    the screenshot are folded into one self-contained document with no external fetches, and the
    race cannot happen. The .html files stay readable and editable on their own; the inlining is
    done to a temporary copy that is deleted afterwards.

    Deliberately kept ASCII: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a stray
    em-dash in a comment becomes a parse error rather than a typo.

.EXAMPLE
    .\make-poster.ps1
    Re-render all five PNGs next to this script.

.EXAMPLE
    .\make-poster.ps1 -Only icons
    Just the tile icons.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    # Full path to chrome.exe or msedge.exe. Found automatically when not given.
    [string] $Browser,

    # Where the PNGs land. Defaults to this folder.
    [string] $OutDir,

    [ValidateSet('all', 'poster', 'icons')]
    [string] $Only = 'all'
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = $here }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Drawing

# ---- helpers ---------------------------------------------------------------

function ConvertTo-DataUri {
    param([string] $Path, [string] $MediaType)
    if (-not (Test-Path $Path)) { throw "Cannot inline a file that is not there: $Path" }
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($Path))
    return "data:$MediaType;base64,$b64"
}

<#
    Builds a self-contained copy of one of the .html files: the stylesheet, every font, the SVG
    and the screenshot all become data URIs, so the rendered document fetches nothing.

    This exists because of a real and silent failure, twice - see the note at the top of the file.
    Everything that CAN be inlined is, rather than only the asset that happened to go missing on
    the day this was written.
#>
function New-SelfContainedPage {
    param([string] $SourceHtml, [string] $BaseDir)

    $html = Get-Content $SourceHtml -Raw

    # 1. fonts.css, with every woff2 folded into it, replacing the <link>.
    if ($html.Contains('href="fonts.css"')) {
        $cssPath = Join-Path $BaseDir 'fonts.css'
        if (-not (Test-Path $cssPath)) { throw "$SourceHtml asks for fonts.css and there is none in $BaseDir." }
        $css = Get-Content $cssPath -Raw

        $fontRefs = [regex]::Matches($css, 'url\((fonts/[^)]+\.woff2)\)')
        if ($fontRefs.Count -eq 0) { throw 'fonts.css names no woff2 files - the wordmark would render in a fallback face.' }
        foreach ($m in $fontRefs) {
            $rel = $m.Groups[1].Value
            $uri = ConvertTo-DataUri -Path (Join-Path $BaseDir $rel) -MediaType 'font/woff2'
            $css = $css.Replace(('url(' + $rel + ')'), ('url(' + $uri + ')'))
        }
        $html = $html.Replace('<link rel="stylesheet" href="fonts.css" />', ('<style>' + [Environment]::NewLine + $css + [Environment]::NewLine + '</style>'))
    }

    # 2. the blurred backdrop
    $shotRef = "url('editor-shot.png')"
    if ($html.Contains($shotRef)) {
        $uri = ConvertTo-DataUri -Path (Join-Path $BaseDir 'editor-shot.png') -MediaType 'image/png'
        $html = $html.Replace($shotRef, ("url('" + $uri + "')"))
    }

    # 3. the logo
    $logoRef = 'src="logo.svg"'
    if ($html.Contains($logoRef)) {
        $uri = ConvertTo-DataUri -Path (Join-Path $BaseDir 'logo.svg') -MediaType 'image/svg+xml'
        $html = $html.Replace($logoRef, ('src="' + $uri + '"'))
    }

    # Nothing may still point outward. A reference this function failed to catch is exactly the
    # bug it exists to prevent, so it throws rather than rendering something quietly wrong.
    foreach ($leftover in @('fonts.css', 'editor-shot.png', 'logo.svg', '.woff2')) {
        if ($html.Contains($leftover)) {
            throw "$([System.IO.Path]::GetFileName($SourceHtml)) still references $leftover after inlining."
        }
    }

    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('ishaunted-' + [guid]::NewGuid().ToString('N') + '.html')
    [System.IO.File]::WriteAllText($temp, $html, (New-Object System.Text.UTF8Encoding $false))
    return $temp
}

<#
    A dimension check is not an "it rendered" check. Chrome handed back a correctly sized but
    entirely BLANK 150x150 tile, and a correctly sized poster with its whole backdrop missing.
    Both looked fine in a file listing. This samples a grid and insists the picture has some
    variety in it.

    The threshold is 12 and not something tighter on purpose. A blank tile has ONE colour, so 12
    is already decisive, while the artwork is flat-shaded vector: a crisp 1200px master samples
    only about 40 distinct values on this grid even when perfect, and its own downscales sample
    120-250 because antialiasing invents blends. A threshold near 40 would sit one redraw away
    from failing on a good render, which is how a useful check ends up deleted.

    It is a backstop, not the fix. The fix is that the page has nothing left to fetch.
#>
function Assert-NotFlat {
    param([string] $Path, [int] $MinDistinct = 12)

    $img = [System.Drawing.Bitmap]::FromFile($Path)
    try {
        $seen = @{}
        for ($x = 0; $x -lt $img.Width; $x += [Math]::Max(1, [int]($img.Width / 40))) {
            for ($y = 0; $y -lt $img.Height; $y += [Math]::Max(1, [int]($img.Height / 40))) {
                $seen[$img.GetPixel($x, $y).ToArgb()] = $true
            }
        }
        if ($seen.Count -lt $MinDistinct) {
            throw "$(Split-Path -Leaf $Path) is effectively blank - only $($seen.Count) distinct colours in a 40x40 sample. The page rendered nothing."
        }
        return $seen.Count
    }
    finally { $img.Dispose() }
}

<#
    High-quality downscale. The defaults are a smear that makes a 71px icon look like a thumbnail
    of a thumbnail; these four settings are what make it look drawn.
#>
function Resize-Png {
    param([string] $Source, [string] $Destination, [int] $Size)

    $src = [System.Drawing.Image]::FromFile($Source)
    try {
        $bmp = New-Object System.Drawing.Bitmap $Size, $Size
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.DrawImage($src, 0, 0, $Size, $Size)
            }
            finally { $g.Dispose() }
            $bmp.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bmp.Dispose() }
    }
    finally { $src.Dispose() }
}

# ---- the browser -----------------------------------------------------------
if (-not $Browser) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
    )
    $Browser = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Browser) { throw 'No Chrome or Edge found. Pass one with -Browser.' }
Write-Host "   browser: $Browser"

# A profile of its own, in TEMP. Sharing the real one fails outright while a window is open, and
# this leaves nothing behind in it either way.
$profileDir = Join-Path ([System.IO.Path]::GetTempPath()) 'ishaunted-poster-profile'

# ---- what to render --------------------------------------------------------
$jobs = @()
if ($Only -in @('all', 'poster')) {
    $jobs += @{ Page = 'poster.html'; W = 1440; H = 2160; Name = 'poster-1440x2160.png' }
    $jobs += @{ Page = 'poster.html'; W = 1080; H = 1080; Name = 'poster-1080x1080.png' }
}
if ($Only -in @('all', 'icons')) {
    # ONE master, rendered big, then downscaled below. Downsampling a large render is plainly
    # better at 71px than asking a rasteriser to work at 71px, and it guarantees the three tiles
    # are the same picture rather than three renders that might not be.
    $jobs += @{ Page = 'icon.html'; W = 1200; H = 1200; Name = 'icon-master-1200.png' }
}

# One self-contained copy per page, reused across that page's sizes.
$pages = @{}
$temps = @()
try {
    foreach ($job in $jobs) {
        if (-not $pages.ContainsKey($job.Page)) {
            $source = Join-Path $here $job.Page
            if (-not (Test-Path $source)) { throw "No $($job.Page) beside this script (looked in $here)." }
            $temp = New-SelfContainedPage -SourceHtml $source -BaseDir $here
            $pages[$job.Page] = $temp
            $temps += $temp
        }

        $pageUrl = 'file:///' + $pages[$job.Page].Replace('\', '/')
        $out = Join-Path $OutDir $job.Name
        if (Test-Path $out) { Remove-Item $out -Force }

        $chromeArgs = @(
            '--headless=new'
            '--disable-gpu'
            '--hide-scrollbars'
            '--force-device-scale-factor=1'   # or the PNG comes out at the display's DPI, not this size
            "--user-data-dir=$profileDir"
            '--virtual-time-budget=8000'
            "--window-size=$($job.W),$($job.H)"
            "--screenshot=$out"
            $pageUrl
        )

        # Start-Process -Wait, NOT the call operator. chrome.exe is a GUI-subsystem binary, so the
        # call operator returns the instant it is launched and every check after it races the
        # render - which first showed up as this script reporting both files missing.
        $p = Start-Process -FilePath $Browser -ArgumentList $chromeArgs -Wait -PassThru -NoNewWindow
        if ($p.ExitCode -ne 0) { throw "$Browser exited $($p.ExitCode) rendering $($job.Name)." }
        if (-not (Test-Path $out)) { throw "No $($job.Name) was written." }

        # The window size is a REQUEST, so assert what actually landed.
        $img = [System.Drawing.Image]::FromFile($out)
        try {
            if ($img.Width -ne $job.W -or $img.Height -ne $job.H) {
                throw "$($job.Name) came out $($img.Width)x$($img.Height), not $($job.W)x$($job.H)."
            }
        } finally { $img.Dispose() }

        $colours = Assert-NotFlat -Path $out
        Write-Host ("   {0,-22} {1}x{2}  {3:N0} bytes  {4} colours" -f $job.Name, $job.W, $job.H, (Get-Item $out).Length, $colours) -ForegroundColor Green
    }
}
finally {
    foreach ($t in $temps) { if (Test-Path $t) { Remove-Item $t -Force -ErrorAction SilentlyContinue } }
}

# ---- the tile icons, downscaled from the master -----------------------------
if ($Only -in @('all', 'icons')) {
    $master = Join-Path $OutDir 'icon-master-1200.png'
    if (-not (Test-Path $master)) { throw "No icon master at $master." }

    foreach ($size in @(300, 150, 71)) {
        $out = Join-Path $OutDir ("icon-{0}x{0}.png" -f $size)
        if (Test-Path $out) { Remove-Item $out -Force }

        Resize-Png -Source $master -Destination $out -Size $size

        $img = [System.Drawing.Image]::FromFile($out)
        try {
            if ($img.Width -ne $size -or $img.Height -ne $size) {
                throw "icon-${size}x${size}.png came out $($img.Width)x$($img.Height)."
            }
        } finally { $img.Dispose() }

        $colours = Assert-NotFlat -Path $out
        Write-Host ("   {0,-22} {1}x{1}  {2:N0} bytes  {3} colours" -f (Split-Path -Leaf $out), $size, (Get-Item $out).Length, $colours) -ForegroundColor Green
    }

    # The master is scaffolding, not a deliverable. Leaving it beside the three real icons is an
    # invitation to upload the wrong file.
    Remove-Item $master -Force
}

Write-Host ''
Write-Host '   Partner Center > Store listings > English (United States).' -ForegroundColor Cyan
Write-Host '   Poster art and the square go under Store logos; the three icons are the app tile icons.'
