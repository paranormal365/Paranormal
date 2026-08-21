<#
.SYNOPSIS
    Compiles installer\dist\BenVideoSidecar-win-x64.exe from the staged app payload.

.DESCRIPTION
    Runs on WINDOWS, unlike build.sh beside it. That script cross-publishes the sidecar from macOS
    and produces dist\BenVideoSidecar-win-x64\app\ plus a zip of it; this one takes that same app\
    folder and wraps it in an Inno Setup installer. So the order is: build.sh first (anywhere), then
    this (on Windows). It does not build the app, and says so rather than silently packaging a
    stale payload.

    THE INSTALLER IS UNSIGNED, and that is the one thing worth knowing before shipping it.
    SmartScreen shows "Windows protected your PC" for an executable from an unknown publisher, and
    the Run button is hidden behind "More info". That is not a bug in this script and no amount of
    Inno configuration removes it - only an Authenticode certificate does (a commercial one, or
    SignPath, which is free for open source). Until then the download page has to tell people what
    they are about to see, exactly as the macOS page explains right-click-Open.

    Deliberately kept ASCII: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a stray
    em-dash in a comment becomes a parse error rather than a typo.

.EXAMPLE
    .\build-installer.ps1
    Compile with the version read from Ben.Video.Sidecar.csproj.

.EXAMPLE
    .\build-installer.ps1 -AppVersion 1.4.0
    Compile with an explicit version.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $AppVersion,
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$installer = Split-Path -Parent $here                      # ...\Ben.Video.Sidecar\installer
$project   = Split-Path -Parent $installer                 # ...\Ben.Video.Sidecar
$dist      = Join-Path $installer 'dist'
$payload   = Join-Path $dist 'BenVideoSidecar-win-x64\app'
$iss       = Join-Path $here 'BenVideoSidecar.iss'

function Write-Detail ([string]$m) { Write-Host "   $m" }

# ---- the payload has to exist, and has to be the Windows one -----------------
if (-not (Test-Path $payload)) {
    throw @"
No staged payload at $payload

Build it first - that step cross-publishes from macOS and is not done here:
    Ben.Video.Sidecar/installer/windows/build.sh
"@
}
$exe = Join-Path $payload 'Ben.Video.Sidecar.exe'
if (-not (Test-Path $exe)) {
    # A macOS payload in this folder would otherwise compile happily into an installer that cannot
    # run anything, and the failure would land on whoever downloaded it.
    throw "The payload at $payload has no Ben.Video.Sidecar.exe - that is not a win-x64 publish."
}
if (-not (Test-Path (Join-Path $payload 'ffmpeg\win-x64\ffmpeg.exe'))) {
    throw "The payload has no ffmpeg\win-x64\ffmpeg.exe - rendering would fail on the target machine."
}

# ---- Inno Setup ------------------------------------------------------------
if (-not $IsccPath) {
    # The per-user path is first because that is where `winget install` puts it when it runs
    # unelevated, which is the normal case - and looking only in Program Files reports Inno Setup as
    # missing on a machine that has it.
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        if ($onPath) { $IsccPath = $onPath.Source }
    }
}
if (-not $IsccPath) {
    throw @"
Inno Setup is not installed. Install it, then run this again:
    winget install --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
Or pass -IsccPath to point at an existing ISCC.exe.
"@
}
Write-Detail "ISCC: $IsccPath"

# ---- version ---------------------------------------------------------------
if (-not $AppVersion) {
    $csproj = Join-Path $project 'Ben.Video.Sidecar.csproj'
    if (Test-Path $csproj) {
        $xml = [xml](Get-Content $csproj -Raw)
        $AppVersion = ($xml.Project.PropertyGroup.Version        | Where-Object { $_ } | Select-Object -First 1)
        if (-not $AppVersion) {
            $AppVersion = ($xml.Project.PropertyGroup.AssemblyVersion | Where-Object { $_ } | Select-Object -First 1)
        }
    }
    # Inno wants a numeric-looking version; anything else is only cosmetic here, so fall back
    # rather than refusing to build over a missing property.
    if (-not $AppVersion) { $AppVersion = '1.0.0' }
}
Write-Detail "version: $AppVersion"

$sizeMb = [math]::Round(((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
Write-Detail "payload: $payload ($sizeMb MB)"

# ---- compile ---------------------------------------------------------------
Write-Host ''
Write-Host '==> Compiling the installer' -ForegroundColor Cyan
& $IsccPath "/DMyAppVersion=$AppVersion" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$out = Join-Path $dist 'BenVideoSidecar-win-x64.exe'
if (-not (Test-Path $out)) { throw "ISCC reported success but $out is not there" }

$outMb = [math]::Round((Get-Item $out).Length / 1MB, 1)
$hash  = (Get-FileHash -Algorithm SHA256 $out).Hash.ToLowerInvariant()

Write-Host ''
Write-Host "   $out  ($outMb MB)" -ForegroundColor Green
Write-Host "   sha256 $hash"
Write-Host ''
Write-Host '   deploy-ishaunted.ps1 prefers this .exe over the zip when both are in dist\.'
Write-Host '   It is UNSIGNED: expect SmartScreen to warn about an unknown publisher.'
