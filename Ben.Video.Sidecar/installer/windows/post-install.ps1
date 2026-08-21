# Starts the sidecar after the Inno Setup installer has laid the files down, waits for it to prove
# it is alive, and opens the pairing page on whichever port it took.
#
# This is the tail of install.ps1 and nothing else. The copy, the autostart registry value and the
# Unblock-File sweep all moved into the installer: Inno writes the files, [Registry] writes the Run
# key, and files written by an installer never carry the mark of the web that a downloaded zip puts
# on everything it contains.
#
# Inno invokes it with -ExecutionPolicy Bypass, so the person installing never meets that prompt.

param(
    [Parameter(Mandatory = $true)]
    [string] $InstallDir
)

$ErrorActionPreference = 'Stop'

$Exe       = Join-Path $InstallDir 'Ben.Video.Sidecar.exe'
$LogDir    = Join-Path $InstallDir 'logs'
$FirstPort = 43117
$LastPort  = 43121

if (-not (Test-Path $Exe)) { throw "install failed: $Exe is not there" }
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }

$stdout = Join-Path $LogDir 'sidecar.log'
$stderr = Join-Path $LogDir 'sidecar.err.log'
Start-Process -FilePath $Exe -WorkingDirectory $InstallDir -WindowStyle Hidden `
              -RedirectStandardOutput $stdout -RedirectStandardError $stderr

# Ask the running process what it is, rather than reading the log. The log is append-only, so an
# old "listening" line from a previous run reads exactly like a new one; only a live health
# response proves this copy is up. It also tells us which port it actually took - the sidecar walks
# upwards from 43117 when a port is occupied.
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
    # Nothing here is fatal to the installation - the files are in place and it will start at the
    # next sign-in. Say where to look rather than failing the installer over a slow first start.
    Write-Warning "The sidecar did not answer on ports $FirstPort-$LastPort."
    Write-Warning "Look in $stderr - if antivirus removed the executable, that is where it shows."
    exit 0
}

Start-Process "http://127.0.0.1:$port/pair"
