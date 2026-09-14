<#
.SYNOPSIS
    Runs the WebApi on http://127.0.0.1:5252 against the disposable IsHauntedDb_e2e database,
    for developing the canvas editor (http://localhost:5125) on this Windows box.

.DESCRIPTION
    The Windows counterpart of the API half of scripts/run-e2e.sh.

    It exists because the easy way to run the API here is the wrong one. The development
    configuration, the design-time EF factory and the Mac's own settings have all at some point
    pointed at the real IsHauntedDb, and the dev seeder once ran against production. So this
    script names the e2e database in exactly one place and passes it explicitly to both halves:

      1. dotnet ef database update --connection <e2e>. The flag is the ONLY form dotnet ef obeys:
         it ignores ConnectionStrings__BenDbConnectionString and silently uses the default
         connection string, which is how a scratch run once migrated the wrong database.
      2. dotnet run with ConnectionStrings__BenDbConnectionString=<e2e>, which dotnet run does obey.

    Uploads go to .uploads-IsHauntedDb_e2e beside the repo (gitignored by .uploads-*/), named after
    the database for the reason run-e2e.sh gives: a seeded file migrated to disk leaves a row
    pointing at bytes, so a fresh database must always get a fresh directory.

    Binds 127.0.0.1, not localhost, so Kestrel opens one IPv4 listener; browsers asking for
    localhost still reach it.

    Needs Ben.Data.WebApi\appsettings.Development.json (gitignored, one per checkout) for the CORS
    origins (http://localhost:5125 among them) and the SeedData section. See docs/dev-loop-canvas.md.

    This file is deliberately pure ASCII: Windows PowerShell 5.1 reads a .ps1 with no byte-order
    mark as ANSI.

.PARAMETER SkipMigrate
    Start without running dotnet ef first (it takes ~20 s even when nothing is pending).

.PARAMETER ResetSeedPasswords
    For this run only, put the seeded BenCo accounts' passwords back to the ones written in the
    Development configuration (SeedData__SeedOrganization__ResetPasswords=true). Off by default:
    a seeder that rewrote passwords on every start would be a standing way into an account.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-webapi-e2e.ps1
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $SkipMigrate,
    [switch] $ResetSeedPasswords
)

$ErrorActionPreference = 'Stop'

$database = 'IsHauntedDb_e2e'
$conn     = "Server=localhost;Database=$database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"

# Belt and braces: the one mistake this script exists to prevent.
if ($conn -match 'Database=IsHauntedDb;' -or $conn -match 'IsHauntedDb_player') {
    throw 'refusing to run: the connection string names the production or UAT database'
}

$root    = Split-Path -Parent $PSScriptRoot
$api     = Join-Path $root 'Ben.Data.WebApi'
$source  = Join-Path $root 'Ben.Data.Source'
$uploads = Join-Path $root ".uploads-$database"
$devJson = Join-Path $api 'appsettings.Development.json'

if (-not (Test-Path $devJson)) {
    throw "no $devJson - create it (gitignored) with Cors:AllowedOrigins including http://localhost:5125 and the SeedData section; see docs/dev-loop-canvas.md"
}

try {
    $busy = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 'http://localhost:5252/api/public/build'
    if ($busy.StatusCode -eq 200) {
        throw 'something is already serving http://localhost:5252 - it may be pointed at another database; stop it first'
    }
}
catch [System.Net.WebException] { }  # nothing listening: the normal case

New-Item -ItemType Directory -Force $uploads | Out-Null

if (-not $SkipMigrate) {
    Write-Host "== Migrating $database"
    & dotnet ef database update --project $source --startup-project $api --connection $conn
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed ($LASTEXITCODE)" }
}

$env:ASPNETCORE_ENVIRONMENT                  = 'Development'
$env:ConnectionStrings__BenDbConnectionString = $conn
$env:FileStorage__RootPath                   = $uploads
if ($ResetSeedPasswords) { $env:SeedData__SeedOrganization__ResetPasswords = 'true' }

Write-Host "== Starting the WebApi on http://127.0.0.1:5252 against $database"
& dotnet run --project $api --no-launch-profile --urls 'http://127.0.0.1:5252'
exit $LASTEXITCODE
