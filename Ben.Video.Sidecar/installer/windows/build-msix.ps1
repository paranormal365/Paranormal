<#
.SYNOPSIS
    Builds installer\dist\BenVideoSidecar-win-x64.msix - the Microsoft Store package.

.DESCRIPTION
    This is a SEPARATE build from the Inno installer beside it, and it is not the same payload.
    Two things differ, both on purpose:

      * ffmpeg. The .exe installer carries the GPL build. This carries the LGPL build of the same
        release, pinned in ffmpeg-manifest.store.json, because a Store submission is a
        redistribution we would rather keep permissively licensed. That manifest travels INTO the
        package as ffmpeg-manifest.json, so the integrity check at startup checks the binaries the
        package actually has. VideoEncoders picks an encoder from what ffmpeg reports, so the app
        needs no build-time switch to go with this.

      * Identity. A Store package is stamped with an identity Partner Center assigns. Until the app
        name is reserved there, this stamps a placeholder and warns; the resulting .msix is
        perfectly good for testing and CANNOT be uploaded.

    WHAT THIS DOES NOT DO: sign anything. Store packages are re-signed by Microsoft, which is the
    whole reason this route exists - no certificate, and no SmartScreen warning for the people who
    install it. To install it on THIS machine for testing, it has to be signed with a certificate
    the machine trusts, and trusting one needs an administrator. -SelfSign writes the package and
    prints the two elevated commands rather than running them.

    Deliberately kept ASCII: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a stray
    em-dash in a comment becomes a parse error rather than a typo.

.EXAMPLE
    .\build-msix.ps1
    Build with a placeholder identity, for testing.

.EXAMPLE
    .\build-msix.ps1 -IdentityName 54321Ben.BenVideoSidecar -Publisher "CN=ABCD1234-..." -PublisherDisplayName "Ben Clark"
    Build the real thing, with the identity copied from Partner Center.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    # All three come from Partner Center > your app > Product management > Product identity.
    [string] $IdentityName,
    [string] $Publisher,
    [string] $PublisherDisplayName,

    [string] $DisplayName = 'BenVideo Sidecar',
    [string] $AppVersion,

    # Reuse whatever is already staged instead of publishing again. Faster when iterating on the
    # manifest; wrong if the app changed.
    [switch] $SkipPublish,

    # Print the commands that would make this package installable on this machine for testing.
    [switch] $SelfSign
)

$ErrorActionPreference = 'Stop'

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path   # ...\installer\windows
$msixDir   = Join-Path $here 'msix'
$installer = Split-Path -Parent $here
$project   = Split-Path -Parent $installer                     # ...\Ben.Video.Sidecar
$dist      = Join-Path $installer 'dist'
$stage     = Join-Path $dist 'msix-stage'
$outMsix   = Join-Path $dist 'BenVideoSidecar-win-x64.msix'

function Write-Detail ([string]$m) { Write-Host "   $m" }
function Get-Sha256 ([string]$p) { (Get-FileHash -Algorithm SHA256 $p).Hash.ToLowerInvariant() }

# ---- identity --------------------------------------------------------------
# A package that cannot be uploaded is still worth building - it is how everything else here gets
# tested - but nobody should discover at upload time that it was stamped with a placeholder.
$placeholder = $false
if (-not $IdentityName)        { $IdentityName        = 'PLACEHOLDER.BenVideoSidecar'; $placeholder = $true }
if (-not $Publisher)           { $Publisher           = 'CN=PLACEHOLDER-NOT-A-REAL-STORE-IDENTITY'; $placeholder = $true }
if (-not $PublisherDisplayName){ $PublisherDisplayName= 'PLACEHOLDER'; $placeholder = $true }

# ---- version ---------------------------------------------------------------
# The package version is the app version with a fourth part. The Store REQUIRES that fourth part to
# be 0: it reserves the revision field for itself.
if (-not $AppVersion) {
    $csproj = Join-Path $project 'Ben.Video.Sidecar.csproj'
    $xml = [xml](Get-Content $csproj -Raw)
    $AppVersion = ($xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $AppVersion) { throw "No <Version> in $csproj, and none given with -AppVersion." }
}
$parts = @($AppVersion -split '\.')
while ($parts.Count -lt 3) { $parts += '0' }
$packageVersion = "{0}.{1}.{2}.0" -f $parts[0], $parts[1], $parts[2]
Write-Detail "app version:     $AppVersion"
Write-Detail "package version: $packageVersion"

# ---- MakeAppx --------------------------------------------------------------
# From the Windows SDK build tools NuGet package rather than a system SDK install, so this works on
# a machine with no SDK on it. Restored into the usual package cache by any build that references
# it; if it is not there, one `dotnet tool`-free restore of the package puts it there.
$sdkRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
$makeappx = Get-ChildItem -Path $sdkRoot -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*\x64\*' } |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $makeappx) {
    throw @"
No makeappx.exe found under $sdkRoot

It ships in the Microsoft.Windows.SDK.BuildTools NuGet package. To put it in the cache:
    dotnet new console -o `$env:TEMP\sdktools ; dotnet add `$env:TEMP\sdktools package Microsoft.Windows.SDK.BuildTools
or install the Windows SDK and pass its makeappx.exe on PATH.
"@
}
Write-Detail "makeappx: $makeappx"

# ---- payload ---------------------------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Write-Host ''
    Write-Host '==> Publishing the sidecar (self-contained, single file)' -ForegroundColor Cyan
    # Same profile the .exe installer uses, so the two packages are the same program.
    & dotnet publish (Join-Path $project 'Ben.Video.Sidecar.csproj') `
        -p:PublishProfile=win-x64 -p:BundledFfmpegRid=win-x64 `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $stage --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}
if (-not (Test-Path (Join-Path $stage 'Ben.Video.Sidecar.exe'))) {
    throw "No Ben.Video.Sidecar.exe in $stage - publish first (drop -SkipPublish)."
}

# ---- swap in the LGPL ffmpeg ----------------------------------------------
Write-Host ''
Write-Host '==> Staging the permissively licensed ffmpeg' -ForegroundColor Cyan
$storeManifest = Join-Path $project 'ffmpeg-manifest.store.json'
$storeFfmpeg   = Join-Path $dist 'ffmpeg-store\win-x64'

# Downloaded once and kept, because it is ~150 MB. fetch-ffmpeg.ps1 hash-checks both the archive
# and the extracted binaries, so a cached copy is only reused when it still matches the pin.
$pin = (Get-Content $storeManifest -Raw | ConvertFrom-Json).'win-x64'
$cachedOk = (Test-Path (Join-Path $storeFfmpeg 'ffmpeg.exe')) -and
            ((Get-Sha256 (Join-Path $storeFfmpeg 'ffmpeg.exe')) -eq $pin.sha256)
if ($cachedOk) {
    Write-Detail "already fetched and matching the pin: $storeFfmpeg"
} else {
    & (Join-Path $project 'scripts\fetch-ffmpeg.ps1') -Rid win-x64 -Manifest $storeManifest -OutDir $storeFfmpeg
    if ($LASTEXITCODE -ne 0) { throw "fetch-ffmpeg.ps1 failed (exit $LASTEXITCODE)" }
}

$payloadFfmpeg = Join-Path $stage 'ffmpeg\win-x64'
New-Item -ItemType Directory -Force -Path $payloadFfmpeg | Out-Null
Copy-Item (Join-Path $storeFfmpeg 'ffmpeg.exe')  (Join-Path $payloadFfmpeg 'ffmpeg.exe')  -Force
Copy-Item (Join-Path $storeFfmpeg 'ffprobe.exe') (Join-Path $payloadFfmpeg 'ffprobe.exe') -Force
Copy-Item $storeManifest (Join-Path $stage 'ffmpeg-manifest.json') -Force

# The app re-hashes these at startup and refuses to serve job endpoints on a mismatch, so checking
# it here turns a silent "renders nothing" on a tester's machine into a failure at build time.
$payloadPin = (Get-Content (Join-Path $stage 'ffmpeg-manifest.json') -Raw | ConvertFrom-Json).'win-x64'
foreach ($pair in @(@('ffmpeg.exe', $payloadPin.sha256), @('ffprobe.exe', $payloadPin.ffprobeSha256))) {
    $actual = Get-Sha256 (Join-Path $payloadFfmpeg $pair[0])
    if ($actual -ne $pair[1]) {
        throw "The staged $($pair[0]) does not match the manifest travelling with it.`n  expected: $($pair[1])`n  actual:   $actual"
    }
    Write-Detail "$($pair[0]) matches the packaged manifest"
}
# Nothing GPL should be in a package built for this reason. Asking the binary what it can encode is
# a better check than reading the file for a string: it is the same question the app asks at run
# time, and it would catch the GPL build being staged here by any route at all.
$encoders = & (Join-Path $payloadFfmpeg 'ffmpeg.exe') -hide_banner -encoders 2>$null
if ($LASTEXITCODE -ne 0) { throw "The staged ffmpeg.exe would not run (exit $LASTEXITCODE)." }
# -match against an array FILTERS it, so these are written as "did anything match", not as a
# boolean test. -notmatch would have returned the 237 lines that are not h264_mf, which is true.
if (@($encoders -match '\slibx26[45]\s').Count -gt 0) {
    throw 'The staged ffmpeg has libx264/libx265 - that is the GPL build, not the LGPL one.'
}
if (@($encoders -match '\sh264_mf\s').Count -eq 0) {
    throw 'The staged ffmpeg has no h264_mf - H.264 export would fail on this package.'
}
Write-Detail 'staged ffmpeg has h264_mf and no libx264/libx265'

# ---- manifest and assets ---------------------------------------------------
Write-Host ''
Write-Host '==> Writing the package manifest' -ForegroundColor Cyan
$assets = Join-Path $msixDir 'Assets'
if (-not (Test-Path (Join-Path $assets 'Square150x150Logo.png'))) {
    throw "No tile images in $assets - run msix\make-assets.ps1 and commit what it writes."
}
# Removed first: copying a directory ONTO an existing one of the same name nests it, so a second
# run without -SkipPublish's clean stage produced Assets\Assets\ inside the package.
$stagedAssets = Join-Path $stage 'Assets'
if (Test-Path $stagedAssets) { Remove-Item $stagedAssets -Recurse -Force }
Copy-Item $assets $stagedAssets -Recurse -Force

$manifestXml = Get-Content (Join-Path $msixDir 'AppxManifest.template.xml') -Raw
$manifestXml = $manifestXml.
    Replace('{IDENTITY_NAME}', $IdentityName).
    Replace('{PUBLISHER}', $Publisher).
    Replace('{PUBLISHER_DISPLAY_NAME}', $PublisherDisplayName).
    Replace('{DISPLAY_NAME}', $DisplayName).
    Replace('{VERSION}', $packageVersion)
if ($manifestXml -match '\{[A-Z_]+\}') {
    throw "A placeholder was left unfilled in the manifest: $($Matches[0])"
}
# No BOM: makeappx reads the manifest as XML and a BOM in front of the declaration upsets some
# tooling downstream.
[System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifestXml, (New-Object System.Text.UTF8Encoding $false))
Write-Detail "identity: $IdentityName / $Publisher"

# ---- pack ------------------------------------------------------------------
Write-Host ''
Write-Host '==> Packing' -ForegroundColor Cyan
if (Test-Path $outMsix) { Remove-Item $outMsix -Force }
& $makeappx pack /d $stage /p $outMsix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit $LASTEXITCODE)" }
if (-not (Test-Path $outMsix)) { throw "makeappx reported success but $outMsix is not there" }

# ---- check what actually went in ------------------------------------------
# Reading the package back rather than trusting the staging folder. This is how the nested
# Assets\Assets\ from copying a directory onto itself was found, and a package is not something
# anybody opens to look at before uploading it.
Write-Host ''
Write-Host '==> Checking the package contents' -ForegroundColor Cyan
$unpacked = Join-Path ([System.IO.Path]::GetTempPath()) ("msix-check-" + [guid]::NewGuid())
try {
    & $makeappx unpack /p $outMsix /d $unpacked /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx unpack failed (exit $LASTEXITCODE)" }

    foreach ($required in @(
        'AppxManifest.xml',
        'Ben.Video.Sidecar.exe',
        'ffmpeg-manifest.json',
        'ffmpeg\win-x64\ffmpeg.exe',
        'ffmpeg\win-x64\ffprobe.exe',
        'Assets\Square150x150Logo.png',
        'Assets\Square44x44Logo.png',
        'Assets\StoreLogo.png')) {
        if (-not (Test-Path (Join-Path $unpacked $required))) { throw "The package has no $required" }
    }
    if (Test-Path (Join-Path $unpacked 'Assets\Assets')) { throw 'The package has a nested Assets\Assets folder.' }

    $packedFfmpeg = Get-Sha256 (Join-Path $unpacked 'ffmpeg\win-x64\ffmpeg.exe')
    if ($packedFfmpeg -ne $payloadPin.sha256) {
        throw "The ffmpeg INSIDE the package hashes to $packedFfmpeg, not the pinned $($payloadPin.sha256)."
    }
    Write-Detail "$(@(Get-ChildItem $unpacked -Recurse -File).Count) files, ffmpeg matches the pin"
}
finally { Remove-Item $unpacked -Recurse -Force -ErrorAction SilentlyContinue }

$sizeMb = [math]::Round((Get-Item $outMsix).Length / 1MB, 1)
Write-Host ''
Write-Host "   $outMsix  ($sizeMb MB)" -ForegroundColor Green
Write-Host "   sha256 $(Get-Sha256 $outMsix)"

if ($placeholder) {
    Write-Host ''
    Write-Host '   THIS PACKAGE CANNOT BE UPLOADED.' -ForegroundColor Yellow
    Write-Host '   It carries a placeholder identity. Reserve the app name in Partner Center, then'
    Write-Host '   copy Product identity into:'
    Write-Host '      .\build-msix.ps1 -IdentityName <Name> -Publisher "<Publisher>" -PublisherDisplayName "<Name>"'
}

if ($SelfSign) {
    Write-Host ''
    Write-Host '   To install it on THIS machine for testing (both need an administrator):' -ForegroundColor Cyan
    Write-Host "      `$c = New-SelfSignedCertificate -Type Custom -Subject '$Publisher' -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My -TextExtension '2.5.29.37={text}1.3.6.1.5.5.7.3.3'"
    Write-Host "      Export-Certificate -Cert `$c -FilePath `$env:TEMP\sidecar-test.cer"
    Write-Host "      Import-Certificate -FilePath `$env:TEMP\sidecar-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople   # elevated"
    Write-Host "      signtool sign /fd SHA256 /sha1 `$c.Thumbprint '$outMsix'"
    Write-Host '   The certificate subject MUST equal the Publisher above, character for character.'
}
