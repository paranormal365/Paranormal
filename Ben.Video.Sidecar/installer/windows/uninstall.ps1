# Removes the BenVideo sidecar for the current user: stops it, drops the autostart entry, and
# deletes the install directory. Nothing here needs administrator rights, because nothing the
# installer wrote was outside the user's own profile.
#
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1

$ErrorActionPreference = 'Stop'

$Dest    = Join-Path $env:LOCALAPPDATA 'BenVideoSidecar'
$RunKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunName = 'BenVideoSidecar'

Write-Host '==> Stopping the sidecar'
Get-Process -Name 'Ben.Video.Sidecar' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host '==> Removing the autostart entry'
Remove-ItemProperty -Path $RunKey -Name $RunName -ErrorAction SilentlyContinue

if (Test-Path $Dest) {
    Write-Host "==> Deleting $Dest"
    Remove-Item -Recurse -Force $Dest
}

Write-Host ''
Write-Host 'Removed. Browsers that were paired with it will simply stop finding a sidecar and'
Write-Host 'fall back to rendering in the browser, which is what they do without one installed.'
