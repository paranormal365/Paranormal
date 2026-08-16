# Item #38 phase E — Windows counterpart to fetch-ffmpeg.sh. Downloads and SHA-256-verifies the
# pinned ffmpeg/ffprobe binaries for one RID from ffmpeg-manifest.json, refusing to proceed on a
# hash mismatch (threat T7, supply chain).
#
# Usage: .\scripts\fetch-ffmpeg.ps1 -Rid win-x64
#
# Item #70 phase 174 brought this in line with fetch-ffmpeg.sh and fixed two things that would only
# ever have surfaced against a real pin:
#
#   * 'archiveSha256' verifies the DOWNLOAD before extraction; 'sha256' verifies the EXTRACTED
#     BINARY, which is what FfmpegLocator.VerifyIntegrity re-hashes at startup. This script used to
#     compare the archive against 'sha256', so a real pin could not satisfy both checks at once.
#   * Expand-Archive preserves the archive's own layout, and BtbN's Windows zips nest the binaries
#     under <name>\bin\ — so the extracted ffmpeg.exe never landed at ffmpeg\<rid>\ffmpeg.exe where
#     FfmpegLocator looks. Extraction now goes to a temp dir and the binaries are located by name.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "osx-x64", "osx-arm64", "linux-x64")]
    [string]$Rid
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir
$Manifest = Join-Path $ProjectDir "ffmpeg-manifest.json"
$OutDir = Join-Path $ProjectDir "ffmpeg\$Rid"

$ManifestData = Get-Content $Manifest -Raw | ConvertFrom-Json
$Entry = $ManifestData.$Rid
if ($null -eq $Entry) { Write-Error "No manifest entry for RID '$Rid'."; exit 1 }

$FfmpegExe  = if ($Rid -like "win-*") { "ffmpeg.exe" }  else { "ffmpeg" }
$FfprobeExe = if ($Rid -like "win-*") { "ffprobe.exe" } else { "ffprobe" }

function Test-Placeholder([string]$Value) {
    return [string]::IsNullOrWhiteSpace($Value) -or $Value -like "*TODO*" -or $Value -match '^0+$'
}

if ((Test-Placeholder $Entry.url) -or (Test-Placeholder $Entry.sha256)) {
    Write-Error "ffmpeg-manifest.json still has placeholder values for '$Rid'. Pin a real release URL + the SHA-256 of its extracted binary before running this."
    exit 1
}

$Work = Join-Path ([System.IO.Path]::GetTempPath()) ("benvideo-ffmpeg-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $Work | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Downloads $Url, checks it against archive hash $ExpectedArchiveSha (skipped with a warning when
# absent or a placeholder), extracts it into a fresh directory under $Work, and returns that path.
function Get-ExtractedArchive([string]$Url, [string]$ExpectedArchiveSha, [string]$Label) {
    $archive = Join-Path $Work "$Label-archive$([System.IO.Path]::GetExtension($Url))"
    $dest = Join-Path $Work $Label
    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    Write-Host "Downloading $Label for $Rid..."
    Invoke-WebRequest -Uri $Url -OutFile $archive

    if (Test-Placeholder $ExpectedArchiveSha) {
        Write-Warning "No archiveSha256 pinned for $Label - the archive itself was NOT verified. The extracted binary is still hash-checked."
    }
    else {
        $actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLower()
        if ($actual -ne $ExpectedArchiveSha) {
            Write-Error "SHA-256 mismatch for the downloaded $Label archive ($Rid).`n  expected: $ExpectedArchiveSha`n  actual:   $actual`nRefusing to extract an unverified archive."
            exit 1
        }
        Write-Host "$Label archive hash verified."
    }

    if ($Url -like "*.zip") {
        Expand-Archive -Path $archive -DestinationPath $dest -Force
    }
    elseif ($Url -like "*.tar.xz" -or $Url -like "*.tar.gz") {
        # tar ships with Windows 10 1803+; .tar.xz support arrived with the bsdtar/libarchive build.
        tar -xf $archive -C $dest
        if ($LASTEXITCODE -ne 0) { Write-Error "tar failed to extract $archive"; exit 1 }
    }
    else {
        Write-Error "Unrecognized archive extension for $Url"
        exit 1
    }

    return $dest
}

# Moves the first file named $Exe found anywhere under $SrcRoot into $OutDir and checks it against
# the pinned binary hash. A mismatch deletes the file rather than leaving an unverified binary on
# disk.
function Install-Binary([string]$SrcRoot, [string]$Exe, [string]$Expected, [bool]$Required) {
    $found = Get-ChildItem -Path $SrcRoot -Recurse -File -Filter $Exe -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if ($null -eq $found) {
        if ($Required) { Write-Error "No '$Exe' found in the archive for $Rid."; exit 1 }
        Write-Warning "No '$Exe' in this archive - probe/thumbnail offload will be unavailable."
        return
    }

    $target = Join-Path $OutDir $Exe
    Move-Item -Path $found.FullName -Destination $target -Force

    if (Test-Placeholder $Expected) {
        # Only reachable for ffprobe; an ffmpeg placeholder exits above. FfmpegLocator will not
        # trust an unpinned binary at runtime, so don't let it look installed.
        Write-Warning "No hash pinned for $Exe - FfmpegLocator will NOT trust it at runtime."
        return
    }

    $actual = (Get-FileHash -Path $target -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $Expected) {
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        Write-Error "SHA-256 mismatch for the extracted $Exe ($Rid).`n  expected: $Expected`n  actual:   $actual`nRemoved it. This is the hash FfmpegLocator re-checks at startup."
        exit 1
    }
    Write-Host "$Exe verified: $actual"
}

try {
    $ffmpegSrc = Get-ExtractedArchive $Entry.url $Entry.archiveSha256 "ffmpeg"
    Install-Binary $ffmpegSrc $FfmpegExe $Entry.sha256 $true

    # ffprobe: its own archive when the source publishes one (the macOS sources do), otherwise it
    # rides along in the ffmpeg archive (BtbN's builds do). Optional either way.
    $probeSrc = if (-not (Test-Placeholder $Entry.ffprobeUrl)) {
        Get-ExtractedArchive $Entry.ffprobeUrl $Entry.ffprobeArchiveSha256 "ffprobe"
    } else { $ffmpegSrc }
    Install-Binary $probeSrc $FfprobeExe $Entry.ffprobeSha256 $false

    Write-Host "Done: $OutDir"
}
finally {
    Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
}
