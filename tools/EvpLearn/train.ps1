# Item 242 — one training run on the Windows server. Safe to run from Task Scheduler (e.g. weekly, off-hours).
#   powershell -ExecutionPolicy Bypass -File .\train.ps1 [-Backgrounds D:\evp\room-tone] [-Epochs 12] [-Threads 4]
# Leave a few cores for IIS: -Threads caps how many this run uses.
param(
    [string]$Backgrounds = "",
    [int]$Epochs = 12,
    [int]$Steps = 500,
    [int]$Threads = 4
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$arguments = @("train.py", "--librispeech", "data\LibriSpeech\test-clean", "--epochs", $Epochs, "--steps", $Steps, "--threads", $Threads)
if ($Backgrounds) { $arguments += @("--backgrounds", $Backgrounds) }
New-Item -ItemType Directory -Force runs | Out-Null
$log = "runs\train-$(Get-Date -Format yyyyMMdd-HHmmss).log"
# Windows PowerShell 5.1 turns a native program's stderr into terminating errors when it is redirected under "Stop",
# and torch prints warnings there — so the run itself goes under "Continue" and success is judged by the exit code.
$ErrorActionPreference = "Continue"
.\.venv\Scripts\python.exe @arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) { throw "Training failed; see $log" }
