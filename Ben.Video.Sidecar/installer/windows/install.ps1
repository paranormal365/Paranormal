# Installs the BenVideo sidecar for the current user. No administrator rights, by design:
# everything lives under %LOCALAPPDATA% and autostart is a per-user registry key, so there is no
# UAC prompt to approve for an application that is not signed yet.
#
#   powershell -ExecutionPolicy Bypass -File install.ps1
#
# THIS BUILD IS UNSIGNED. Windows will have marked the downloaded zip with the "mark of the web",
# and every file extracted from it inherits it — SmartScreen then refuses the executable with
# "Windows protected your PC". Unblock-File strips that marker, which is why this script exists
# rather than a "copy these files somewhere" instruction. It is the same job `xattr -dr
# com.apple.quarantine` does in the macOS installer.

$ErrorActionPreference = 'Stop'

$Source    = Split-Path -Parent $MyInvocation.MyCommand.Path
$Dest      = Join-Path $env:LOCALAPPDATA 'BenVideoSidecar'
$Exe       = Join-Path $Dest 'Ben.Video.Sidecar.exe'
$LogDir    = Join-Path $Dest 'logs'
$RunKey    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunName   = 'BenVideoSidecar'
$FirstPort = 43117
$LastPort  = 43121

Write-Host '==> Stopping any running sidecar'
Get-Process -Name 'Ben.Video.Sidecar' -ErrorAction SilentlyContinue | ForEach-Object {
    $_ | Stop-Process -Force
    # Give the socket a moment to close, or the fresh copy binds the next port up and the browser
    # keeps talking to a version that is no longer installed.
    Start-Sleep -Milliseconds 500
}

Write-Host "==> Installing to $Dest"
if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
New-Item -ItemType Directory -Path $Dest, $LogDir -Force | Out-Null

Copy-Item -Path (Join-Path $Source 'app\*') -Destination $Dest -Recurse -Force

# The mark of the web rides on every extracted file, not just the .exe — a blocked DLL or a blocked
# ffmpeg.exe fails later and less obviously than a blocked launcher.
Write-Host '==> Removing the downloaded-file marker'
Get-ChildItem -Path $Dest -Recurse -File | Unblock-File

if (-not (Test-Path $Exe)) { throw "install failed: $Exe is not there after the copy" }

Write-Host '==> Registering it to start at sign-in'
New-ItemProperty -Path $RunKey -Name $RunName -Value "`"$Exe`"" -PropertyType String -Force | Out-Null

Write-Host '==> Starting the sidecar'
$stdout = Join-Path $LogDir 'sidecar.log'
$stderr = Join-Path $LogDir 'sidecar.err.log'
Start-Process -FilePath $Exe -WorkingDirectory $Dest -WindowStyle Hidden `
              -RedirectStandardOutput $stdout -RedirectStandardError $stderr

# Ask the running process what it is, rather than reading the log. The log is append-only, so an
# old "listening" line from a previous run reads exactly like a new one; only a live health
# response proves this copy is up. It also tells us which port it actually took — the sidecar
# walks upwards from 43117 when a port is occupied.
Write-Host '==> Waiting for it to answer'
$port = $null
foreach ($attempt in 1..20) {
    foreach ($candidate in $FirstPort..$LastPort) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:$candidate/v1/health" -TimeoutSec 2 -UseBasicParsing
            if ($r.StatusCode -eq 200) { $port = $candidate; break }
        } catch { }
    }
    if ($port) { break }
    Start-Sleep -Milliseconds 500
}

if (-not $port) {
    Write-Warning "The sidecar did not answer on ports $FirstPort-$LastPort."
    Write-Warning "Look in $stderr — if antivirus removed the executable, that is where it shows."
    exit 1
}

Write-Host ''
Write-Host "Installed and running on port $port."
Write-Host "  app   $Dest"
Write-Host "  logs  $LogDir"
Write-Host ''
Write-Host 'Opening the pairing page. Type the 6-digit code into the editor, under the'
Write-Host 'sidecar chip in the toolbar. It starts by itself when you sign in from now on.'
Start-Process "http://127.0.0.1:$port/pair"
