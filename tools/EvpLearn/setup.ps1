# Item 242 — prepares the learning half on the Windows server. Run once from this folder in PowerShell:
#   powershell -ExecutionPolicy Bypass -File .\setup.ps1
# Needs Python 3.10 or newer from python.org (tick "Add python.exe to PATH"; the "py" launcher comes with it).
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

py -3 --version
if ($LASTEXITCODE -ne 0) { throw "Python 3 was not found. Install it from python.org first." }

if (-not (Test-Path .venv)) { py -3 -m venv .venv }
.\.venv\Scripts\python.exe -m pip install --upgrade pip
.\.venv\Scripts\python.exe -m pip install -r requirements.txt

# LibriSpeech test-clean (346 MB, CC BY 4.0): real voices from 40 speakers. Downloaded once.
New-Item -ItemType Directory -Force data | Out-Null
if (-not (Test-Path data\LibriSpeech\test-clean)) {
    $archive = "data\test-clean.tar.gz"
    if (-not (Test-Path $archive)) { Invoke-WebRequest https://www.openslr.org/resources/12/test-clean.tar.gz -OutFile $archive }
    $md5 = (Get-FileHash $archive -Algorithm MD5).Hash.ToLower()
    if ($md5 -ne "32fa31d27d2e1cad72775fee3f4849a9") { throw "test-clean.tar.gz checksum $md5 does not match openslr's." }
    tar -xzf $archive -C data   # tar ships with Windows 10 and Server 2019 onwards
}
.\.venv\Scripts\python.exe -c "import torch, onnxruntime; print('torch', torch.__version__, '| onnxruntime', onnxruntime.__version__)"
Write-Host "Ready. Train with .\train.ps1"
