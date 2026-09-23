<#
.SYNOPSIS
    Renders the Microsoft Store poster art from poster.html.

.DESCRIPTION
    Two sizes, both from one composition: 1440x2160 (the 2:3 poster art, twice the Store's 720x1080
    minimum) and 1080x1080 (the square). poster.html sizes everything in vmin, so the proportions
    hold across both without a second layout.

    There is no image editor on this machine, and this wants none. The composition is CSS, which
    means it can be diffed, re-rendered at any size, and built from the SITE'S OWN font files and
    wordmark markup rather than someone's recollection of them. To change the art, edit the HTML.

    Headless Chrome does the rasterising. Edge works identically and is the fallback - both are
    Chromium, and -Browser takes a path if neither is where this expects.

    Deliberately kept ASCII: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a stray
    em-dash in a comment becomes a parse error rather than a typo.

.EXAMPLE
    .\make-poster.ps1
    Re-render both PNGs next to this script.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    # Full path to chrome.exe or msedge.exe. Found automatically when not given.
    [string] $Browser,

    # Where the PNGs land. Defaults to this folder.
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = $here }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$page = Join-Path $here 'poster.html'
if (-not (Test-Path $page)) { throw "No poster.html beside this script (looked in $here)." }

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

# A profile of its own, in TEMP. Sharing the real one fails outright when a window is open, and
# this leaves nothing behind in it either way.
$profileDir = Join-Path ([System.IO.Path]::GetTempPath()) 'ishaunted-poster-profile'

$pageUrl = 'file:///' + $page.Replace('\', '/')

Add-Type -AssemblyName System.Drawing

foreach ($size in @(
    @{ W = 1440; H = 2160; Name = 'poster-1440x2160.png' },
    @{ W = 1080; H = 1080; Name = 'poster-1080x1080.png' })) {

    $out = Join-Path $OutDir $size.Name
    if (Test-Path $out) { Remove-Item $out -Force }

    # --virtual-time-budget, not a sleep: it runs the page's clock forward as fast as it can and
    # screenshots when the work drains. Without it the fonts routinely miss the shot and the
    # wordmark renders in the fallback face - which looks fine at a glance and is wrong.
    $chromeArgs = @(
        '--headless=new'
        '--disable-gpu'
        '--hide-scrollbars'
        '--force-device-scale-factor=1'     # or the PNG comes out at the display's DPI, not this size
        "--user-data-dir=$profileDir"
        '--virtual-time-budget=8000'
        "--window-size=$($size.W),$($size.H)"
        "--screenshot=$out"
        $pageUrl
    )

    # Start-Process -Wait, NOT the call operator. chrome.exe is a GUI-subsystem binary, so `&`
    # returns the instant it is launched and every check after it races the render.
    $p = Start-Process -FilePath $Browser -ArgumentList $chromeArgs -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { throw "$($Browser) exited $($p.ExitCode) rendering $($size.Name)." }
    if (-not (Test-Path $out)) { throw "No $($size.Name) was written." }

    # The window size is a REQUEST. Assert what actually landed - a scrollbar, a device scale
    # factor or a minimum window size will all quietly hand back something else.
    $img = [System.Drawing.Image]::FromFile($out)
    try {
        if ($img.Width -ne $size.W -or $img.Height -ne $size.H) {
            throw "$($size.Name) came out $($img.Width)x$($img.Height), not $($size.W)x$($size.H)."
        }
    } finally { $img.Dispose() }

    Write-Host ("   {0}  {1}x{2}  {3:N0} bytes" -f $size.Name, $size.W, $size.H, (Get-Item $out).Length) -ForegroundColor Green
}

Write-Host ''
Write-Host '   Upload under Store listings > English (United States) > Store logos.' -ForegroundColor Cyan
