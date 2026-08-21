<#
.SYNOPSIS
    One-time IIS setup for IsHaunted.com: application pools, applications, folders and permissions.

.DESCRIPTION
    Run this once, elevated, before the first scripts\deploy-ishaunted.ps1. It is idempotent, so
    running it again after a change is safe and is the intended way to reconcile the server.

    What it builds, and why each piece is the way it is:

    THREE POOLS, NOT ONE. The website and the WebApi are both hosted in-process by
    AspNetCoreModuleV2. Two in-process .NET applications cannot share an application pool - IIS
    refuses the second one with HTTP 500.35, "ANCM Multiple In-Process Applications in same
    Process". So the API gets its own pool, and the static apps share a third.

    EVERY POOL IS "NO MANAGED CODE". Counter-intuitive and correct: .NET (Core) brings its own
    runtime, so the pool must not load the old CLR. The static apps need no runtime at all.

    FOUR APPLICATIONS, NOT FOLDERS. The root web.config registers the ASP.NET Core handler at
    path="*", so EVERY request under the site - including /webapi/... and /editors/video/... - is
    handed to the website process unless that folder is its own IIS Application. The website looks
    in its own wwwroot, finds nothing, and returns its 404. The files are all present and correct;
    nothing serves them. inheritInChildApplications="false" in the generated web.config is what
    stops the root handler leaking down, and it only takes effect at an application boundary.

    This file is deliberately pure ASCII: Windows PowerShell 5.1 reads a .ps1 with no byte-order
    mark as ANSI, so a stray em-dash in a comment becomes a parse error rather than a typo.

.EXAMPLE
    .\scripts\setup-iis-ishaunted.ps1
    Reconcile everything. Prints what it changed and what was already right.

.EXAMPLE
    .\scripts\setup-iis-ishaunted.ps1 -WhatIf
    Show what it would do without touching the server.
#>
#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $SiteName    = 'ishaunted.com',
    [string] $SiteRoot    = 'C:\ishaunted',
    [string] $FilesRoot   = 'C:\ishaunted-files',
    [string] $UploadsRoot = 'S:\ishaunted-uploads',
    [string] $DeployRoot  = 'C:\ishaunted-deploy',
    [string] $EditorPath  = 'editors/video',
    [string] $RootPool    = 'IsHaunted.com',
    [string] $WebApiPool  = 'IsHaunted.com-webapi',
    [string] $StaticPool  = 'IsHaunted.com-static',
    [string] $SqlServer   = 'localhost',
    [string] $SqlDatabase = 'IsHauntedDb',
    [switch] $SkipSql
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration -ErrorAction Stop

function Write-Step   ([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Write-Detail ([string]$m) { Write-Host "   $m" }
function Write-Change ([string]$m) { Write-Host "   + $m" -ForegroundColor Green }
function Write-Warn   ([string]$m) { Write-Host "   WARNING: $m" -ForegroundColor Yellow }

$EditorDir = Join-Path $SiteRoot ($EditorPath -replace '/', '\')
$WebApiDir = Join-Path $SiteRoot 'webapi'
$appcmd    = Join-Path $env:SystemRoot 'System32\inetsrv\appcmd.exe'

# =============================================================================
# 1. Prerequisites
# =============================================================================
Write-Step 'Prerequisites'

# Blazor Server holds a SignalR circuit per visitor. Without WebSockets it falls back to long
# polling: it works, and it is worse. This is Windows 11 Pro, so it is an optional feature rather
# than a Server Manager role.
$ws = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets
if ($ws.State -ne 'Enabled') {
    if ($PSCmdlet.ShouldProcess('IIS-WebSockets', 'Enable Windows optional feature')) {
        Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All -NoRestart | Out-Null
        Write-Change 'WebSockets enabled'
    }
} else {
    Write-Detail 'WebSockets already enabled'
}

# The Hosting Bundle supplies both the runtime the apps are published against and the
# AspNetCoreModuleV2 handler their web.config names. Missing it gives HTTP 500.19 or 502.5, and
# neither error mentions it.
$ancm = 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
if (-not (Test-Path $ancm)) {
    throw 'AspNetCoreModuleV2 is not installed. Install the .NET 10 Hosting Bundle before continuing.'
}
$runtimes = Get-ChildItem 'C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App' -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '10.*' }
if (-not $runtimes) { throw 'No ASP.NET Core 10.x runtime found. Install the .NET 10 Hosting Bundle.' }
Write-Detail "ASP.NET Core runtime(s): $(($runtimes.Name) -join ', ')"

# A site's name is a local fact this script has no way to know, and guessing it wrong stops
# everything at the first step. So it is looked for three ways - the name given, that name in any
# casing, then whichever site actually serves $SiteRoot - and if none of them find it, the error
# says what IS on this machine rather than only what is not.
function Get-NormalPath ([string]$p) {
    if (-not $p) { return '' }
    try { [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($p)).TrimEnd('\') }
    catch { $p.TrimEnd('\') }
}

$allSites = @(Get-Website)
$site = $allSites | Where-Object { $_.Name -ceq $SiteName } | Select-Object -First 1

if (-not $site) {
    $site = $allSites | Where-Object { $_.Name -eq $SiteName } | Select-Object -First 1
    if ($site) { Write-Detail "site '$($site.Name)' matched (differs from '$SiteName' only by case)" }
}

if (-not $site) {
    $wanted = Get-NormalPath $SiteRoot
    $byPath = @($allSites | Where-Object { (Get-NormalPath $_.physicalPath) -eq $wanted })
    if ($byPath.Count -eq 1) {
        $site = $byPath[0]
        Write-Detail "no site called '$SiteName'; using '$($site.Name)', which is the one serving $SiteRoot"
    } elseif ($byPath.Count -gt 1) {
        throw "More than one IIS site serves ${SiteRoot}: $(($byPath.Name) -join ', '). Pass -SiteName to say which."
    }
}

if (-not $site) {
    $listing = if ($allSites.Count -gt 0) {
        ($allSites | ForEach-Object { "    '$($_.Name)'  ->  $($_.physicalPath)" }) -join [Environment]::NewLine
    } else { '    (none)' }
    throw @"
No IIS site named '$SiteName', and none serving $SiteRoot.

Sites on this machine:
$listing

Re-run with -SiteName '<one of the names above>', or -SiteRoot if the site lives elsewhere.
"@
}

# Everything below addresses the site by its real name, whatever was passed in.
$SiteName = $site.Name
Write-Detail "site '$SiteName' -> $($site.physicalPath)"
if ((Get-NormalPath $site.physicalPath) -ne (Get-NormalPath $SiteRoot)) {
    Write-Warn "this site's physical path is $($site.physicalPath), but the deploy script writes to $SiteRoot. One of the two is wrong."
}

# The sidecar refuses work from an origin that is not on its allow-list, and it has both the bare
# and the www host baked in, so both need to answer on HTTPS.
$bindings = Get-WebBinding -Name $SiteName | ForEach-Object { $_.protocol + ' ' + $_.bindingInformation }
Write-Detail "bindings: $($bindings -join ' | ')"
if (-not ($bindings | Where-Object { $_ -like 'https*' })) {
    Write-Warn 'no HTTPS binding found. The editor, the sidecar pairing and the OIDC flow all assume https://ishaunted.com.'
}

# =============================================================================
# 2. Folders
# =============================================================================
Write-Step 'Folders'
foreach ($dir in @($SiteRoot, $WebApiDir, $EditorDir, $FilesRoot, (Join-Path $FilesRoot 'sidecar-video'), $UploadsRoot, $DeployRoot)) {
    if (-not (Test-Path $dir)) {
        if ($PSCmdlet.ShouldProcess($dir, 'Create directory')) {
            New-Item -ItemType Directory -Force $dir | Out-Null
            Write-Change "created $dir"
        }
    } else {
        Write-Detail "$dir"
    }
}

# =============================================================================
# 3. Application pools
# =============================================================================
Write-Step 'Application pools'

function Set-Pool ([string]$name, [bool]$dotnetApp) {
    if (-not (Test-Path "IIS:\AppPools\$name")) {
        if ($PSCmdlet.ShouldProcess($name, 'Create application pool')) {
            New-WebAppPool -Name $name | Out-Null
            Write-Change "created pool $name"
        }
    }
    if (-not (Test-Path "IIS:\AppPools\$name")) { return }   # -WhatIf

    # "No Managed Code" for every pool: .NET Core hosts its own runtime and must not have the old
    # CLR loaded underneath it. The static pool needs no runtime at all.
    Set-ItemProperty "IIS:\AppPools\$name" -Name managedRuntimeVersion -Value ''

    # autoStart=False is what IIS writes into applicationHost.config when a pool is stopped by
    # hand, and it does not mean "idle" - it means disabled. The pool will not start on demand, so
    # every request under it gets a bare HTTP 503 and the application's own logs say nothing,
    # because the application never runs. This happened to the static pool on 2026-08-21: the
    # editor and /files went 503 while the website stayed 200, which reads like an application
    # fault in two apps rather than one pool being switched off. Reconciling the server is this
    # script's whole job, so it turns the pool back on rather than reporting it.
    Set-ItemProperty "IIS:\AppPools\$name" -Name autoStart -Value $true
    if ((Get-WebAppPoolState -Name $name).Value -ne 'Started') {
        if ($PSCmdlet.ShouldProcess($name, 'Start application pool')) {
            Start-WebAppPool -Name $name | Out-Null
            Write-Change "started pool $name (it was stopped)"
        }
    }

    if ($dotnetApp) {
        # Data Protection. ASP.NET Core Identity's bearer tokens and the support form's anti-abuse
        # tokens are encrypted with a key ring that, by default, lives in the app pool's user
        # profile. Without loadUserProfile the ring has nowhere durable to live, and every recycle
        # invalidates every token that is currently out there.
        Set-ItemProperty "IIS:\AppPools\$name" -Name processModel.loadUserProfile -Value $true
        # A Blazor Server circuit is in-memory state. Idle shutdown drops every signed-in visitor,
        # and there is nothing to fall back on: the API session lives in the circuit.
        Set-ItemProperty "IIS:\AppPools\$name" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
        Set-ItemProperty "IIS:\AppPools\$name" -Name startMode -Value 'AlwaysRunning'
    }
    Write-Detail "$name configured (No Managed Code$(if ($dotnetApp) { ', profile loaded, no idle timeout' }))"
}

Set-Pool $RootPool   $true
Set-Pool $WebApiPool $true
Set-Pool $StaticPool $false

# =============================================================================
# 3b. SQL Server access for the pool identities
# =============================================================================
# SQL Server runs on this machine, so the applications authenticate as themselves rather than with
# a username and password. That keeps the SQL password out of secrets.json AND out of the
# appsettings.json that ends up on disk in the site folder - there is no password to leak, and
# nothing to rotate.
#
# This has to run AFTER the pools are created: "IIS APPPOOL\<pool>" is a virtual Windows account
# that comes into existence with the pool, and CREATE LOGIN cannot resolve a principal that is not
# there yet.
if (-not $SkipSql) {
    Write-Step 'SQL Server access'

    $connString = "Server=$SqlServer;Database=$SqlDatabase;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15"
    try {
        $sql = New-Object System.Data.SqlClient.SqlConnection $connString
        $sql.Open()

        function Invoke-Sql ([string]$statement) {
            $cmd = New-Object System.Data.SqlClient.SqlCommand($statement, $sql)
            $cmd.ExecuteNonQuery() | Out-Null
        }

        # The API is the data layer: it seeds reference data at startup, migrates legacy file blobs
        # out of the database, and lets Serilog create its own Logs table. db_owner is the honest
        # description of what it does rather than a shortcut.
        #
        # The website holds no DbContext at all - its only use of the database is Serilog's error
        # sink - so reader and writer is all it gets.
        $grants = @(
            @{ Pool = $WebApiPool; Roles = @('db_owner');                      Why = 'seeding, file migration, logging' },
            @{ Pool = $RootPool;   Roles = @('db_datareader', 'db_datawriter'); Why = 'Serilog error sink only' }
        )

        foreach ($g in $grants) {
            $principal = "IIS APPPOOL\$($g.Pool)"
            if (-not $PSCmdlet.ShouldProcess($principal, "Grant $($g.Roles -join '+') on $SqlDatabase")) { continue }

            Invoke-Sql @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$principal')
    CREATE LOGIN [$principal] FROM WINDOWS;
"@
            Invoke-Sql @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$principal')
    CREATE USER [$principal] FOR LOGIN [$principal];
"@
            foreach ($role in $g.Roles) {
                # ALTER ROLE ... ADD MEMBER is a no-op when the member is already in the role.
                Invoke-Sql "ALTER ROLE [$role] ADD MEMBER [$principal];"
            }
            Write-Detail "$principal -> $($g.Roles -join ', ')  ($($g.Why))"
        }

        $sql.Close()
    } catch {
        Write-Warn "could not configure SQL access: $($_.Exception.Message)"
        Write-Detail "Run this script as an account with SQL sysadmin on $SqlServer, or use -SkipSql and grant access by hand."
        Write-Detail "The applications cannot reach the database until this is done."
    }
}

# =============================================================================
# 4. Applications
# =============================================================================
Write-Step 'Applications'

# The root application is the site itself; only its pool needs asserting.
if ($PSCmdlet.ShouldProcess("/ ($SiteName)", "Assign pool $RootPool")) {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $RootPool
    Write-Detail "/ -> $RootPool"
}

# appcmd rather than New-WebApplication: the editor sits at a NESTED path (/editors/video), where
# appcmd is the reliable way to create the application without first inventing a virtual directory
# for the parent segment.
function Set-App ([string]$urlPath, [string]$physical, [string]$pool) {
    $appPath = '/' + $urlPath.Trim('/')
    # Asked of the configuration rather than inferred from appcmd's output: "not found" is an
    # error message, and whether it lands on stdout or stderr is a detail to depend on.
    $exists = @(Get-WebApplication -Site $SiteName | Where-Object { $_.path -eq $appPath }).Count -gt 0
    if (-not $exists) {
        if ($PSCmdlet.ShouldProcess("$appPath", 'Create IIS application')) {
            & $appcmd add app /site.name:"$SiteName" /path:"$appPath" /physicalPath:"$physical" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "appcmd failed creating $appPath (exit $LASTEXITCODE)" }
            Write-Change "created application $appPath -> $physical"
        }
    }
    if ($PSCmdlet.ShouldProcess("$appPath", "Set pool $pool and path $physical")) {
        & $appcmd set app "$SiteName$appPath" /applicationPool:"$pool" | Out-Null
        & $appcmd set app "$SiteName$appPath" "/[path='/'].physicalPath:$physical" | Out-Null
        Write-Detail "$appPath -> $pool  ($physical)"
    }
}

Set-App 'webapi'     $WebApiDir $WebApiPool
Set-App $EditorPath  $EditorDir $StaticPool
Set-App 'files'      $FilesRoot $StaticPool

# =============================================================================
# 5. Permissions
# =============================================================================
Write-Step 'Permissions'

# An application pool running as ApplicationPoolIdentity authenticates as the virtual account
# "IIS AppPool\<pool name>". That account is what these grants name.
function Invoke-Icacls ([string]$path, [string[]]$icaclsArgs, [string]$what) {
    if (-not $PSCmdlet.ShouldProcess($path, $what)) { return }
    & icacls.exe $path @icaclsArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warn "icacls failed on $path ($what)" } else { Write-Detail "$what : $path" }
    $global:LASTEXITCODE = 0
}

# Uploads. The API writes here, and after the pool split it is the API's own identity that needs
# the rights - the grant made earlier to the root pool is now pointed at the wrong account.
Invoke-Icacls $UploadsRoot @('/inheritance:d') 'stop inheriting'
Invoke-Icacls $UploadsRoot @('/grant', "IIS AppPool\${WebApiPool}:(OI)(CI)M") 'grant API modify'
Invoke-Icacls $UploadsRoot @('/remove:g', 'NT AUTHORITY\Authenticated Users') 'remove Authenticated Users'

# Downloads. Served by the static pool, which only ever reads them; the deploy script writes them
# as an administrator.
Invoke-Icacls $FilesRoot @('/inheritance:d') 'stop inheriting'
Invoke-Icacls $FilesRoot @('/grant', "IIS AppPool\${StaticPool}:(OI)(CI)RX") 'grant static read'
Invoke-Icacls $FilesRoot @('/remove:g', 'NT AUTHORITY\Authenticated Users') 'remove Authenticated Users'

# The site root. Every deploy writes the SQL password into appsettings.json here, so a blanket
# Modify for Authenticated Users is a real exposure and not a tidiness point.
Invoke-Icacls $SiteRoot @('/inheritance:d') 'stop inheriting'
Invoke-Icacls $SiteRoot @('/grant', "IIS AppPool\${RootPool}:(OI)(CI)RX",
                                    "IIS AppPool\${WebApiPool}:(OI)(CI)RX",
                                    "IIS AppPool\${StaticPool}:(OI)(CI)RX") 'grant pools read'
Invoke-Icacls $SiteRoot @('/remove:g', 'NT AUTHORITY\Authenticated Users') 'remove Authenticated Users'

# Startup logging, when it is switched on, is written by the app itself.
$logDir = Join-Path $WebApiDir 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
Invoke-Icacls $logDir @('/grant', "IIS AppPool\${WebApiPool}:(OI)(CI)M") 'grant API modify (stdout logs)'

# Secrets. Administrators and SYSTEM only - no pool identity reads this file; the deploy script
# reads it as you and hands the values on.
Invoke-Icacls $DeployRoot @('/inheritance:r',
                            '/grant', 'BUILTIN\Administrators:(OI)(CI)F',
                            '/grant', 'NT AUTHORITY\SYSTEM:(OI)(CI)F') 'lock to Administrators + SYSTEM'

$secretsFile  = Join-Path $DeployRoot 'secrets.json'
$templateFile = Join-Path $PSScriptRoot 'secrets.template.json'

if (-not (Test-Path $secretsFile)) {
    if ($PSCmdlet.ShouldProcess($secretsFile, 'Create secrets file')) {
        if (Test-Path $templateFile) {
            Copy-Item $templateFile $secretsFile
            Write-Change "copied the secrets template to $secretsFile"
        } else {
            # The full template lives beside this script in the repository. Running a lone copy of
            # this file from somewhere else is a reasonable thing to do, and failing at the last
            # step over a missing comment block would not be, so the essential shape is written
            # here instead. scripts\secrets.template.json documents the optional keys.
            $starter = @'
{
  "SmtpPassword": "",
  "GeocodioApiKey": "",
  "GeocodioBaseUrl": null,
  "SqlConnectionString": null
}
'@
            [IO.File]::WriteAllText($secretsFile, $starter, (New-Object Text.UTF8Encoding($false)))
            Write-Change "wrote a starter $secretsFile"
            Write-Detail "secrets.template.json was not beside this script; see it in the repo for the optional keys."
        }
        Write-Detail 'Fill in SmtpPassword (and GeocodioApiKey) before deploying. Leave SqlConnectionString null - the pools use Integrated Security.'
    }
} else {
    Write-Detail "$secretsFile already exists - left alone"
}

# =============================================================================
# 6. Summary
# =============================================================================
Write-Step 'Ready'
Write-Host @"
   /                $SiteRoot
   /webapi          $WebApiDir
   /$EditorPath   $EditorDir
   /files           $FilesRoot

   Next:
     1. Fill in $secretsFile (SQL connection string, SMTP password, Geocodio key).
     2. Bring the database up to date:
        dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi --connection "<conn>"
     3. Copy the existing upload tree (users\, orgs\, cases\) into $UploadsRoot .
     4. Deploy:  .\scripts\deploy-ishaunted.ps1
"@
