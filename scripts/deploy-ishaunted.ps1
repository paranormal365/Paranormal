<#
.SYNOPSIS
    Publishes and deploys IsHaunted.com - website, WebApi and WASM editor - to the local IIS box,
    and stages the sidecar downloads.

.DESCRIPTION
    One site, four IIS applications:

        /               C:\ishaunted                Blazor Server   pool IsHaunted.com
        /webapi         C:\ishaunted\webapi         ASP.NET Core    pool IsHaunted.com-webapi
        /editors/video  C:\ishaunted\editors\video  static (WASM)   pool IsHaunted.com-static
        /files          C:\ishaunted-files          static          pool IsHaunted.com-static

    The website and the WebApi are both in-process ASP.NET Core applications, so they cannot share
    an application pool - IIS answers 500.35 if they do. Run scripts\setup-iis-ishaunted.ps1 once
    to create the pools, applications and ACLs; this script only publishes and copies.

    This is the Windows counterpart of scripts/publish-{webapi,website,editor}.sh and
    scripts/uat-webapi-config.py, which are bash + python and carry macOS-isms. It differs from
    them in three deliberate ways, each noted at the point it happens:

      1. The website's settings are merged into appsettings.json, not appsettings.Production.json.
      2. Serilog's own copy of the connection string is patched in BOTH apps.
      3. The sidecar zips are staged outside the site, under /files, not into the editor's folder.

    Secrets never come from source control. They are read from a machine-local JSON file
    (default C:\ishaunted-deploy\secrets.json, readable only by Administrators and SYSTEM), and the
    SMTP password is never written to any deployed file - it is set as an environment variable on
    the WebApi's application pool.

    This file is deliberately pure ASCII. Windows PowerShell 5.1 reads a .ps1 with no byte-order
    mark as ANSI, so a stray em-dash in a comment becomes a parse error rather than a typo.

.PARAMETER Apps
    Which of website, webapi, editor, files to deploy. The order given is ignored; they always run
    webapi -> editor -> files -> website, so the visible cut-over happens last.

.EXAMPLE
    .\scripts\deploy-ishaunted.ps1
    Full deploy: publish everything, patch, stage sidecar zips, copy, smoke-test.

.EXAMPLE
    .\scripts\deploy-ishaunted.ps1 -Apps webapi -StdoutLog
    Redeploy just the API with startup logging on, to diagnose a 500.30.

.EXAMPLE
    .\scripts\deploy-ishaunted.ps1 -StageOnly
    Publish and patch into artifacts\ and stop. Touches nothing on the server and needs no
    elevation, so it is the way to check what a deploy would actually ship.
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]   $SecretsPath  = 'C:\ishaunted-deploy\secrets.json',
    [ValidateSet('website', 'webapi', 'editor', 'files')]
    [string[]] $Apps         = @('webapi', 'editor', 'files', 'website'),
    [string]   $SiteRoot     = 'C:\ishaunted',
    [string]   $FilesRoot    = 'C:\ishaunted-files',
    [string]   $UploadsRoot  = 'S:\ishaunted-uploads',
    [string]   $SiteUrl      = 'https://ishaunted.com',
    [string]   $EditorPath   = 'editors/video',
    [string]   $WebApiPool   = 'IsHaunted.com-webapi',
    # The website's pool, not the API's. Entra sign-in is done by the website, so its client
    # secret has to land on this pool - see section 5.
    [string]   $RootPool     = 'IsHaunted.com',
    # Integrated Security: SQL Server is on this machine, and the pools authenticate as themselves
    # (scripts\setup-iis-ishaunted.ps1 creates the logins). There is no password in this string,
    # which is why it is a plain default here rather than something read from the secrets file.
    # Override it, or set SqlConnectionString in secrets.json, to point somewhere else.
    [string]   $SqlConnectionString =
        'data source=localhost;initial catalog=IsHauntedDb;integrated security=True;persist security info=False;encrypt=True;trustservercertificate=True;',
    [string]   $SidecarDrop,
    [string[]] $SidecarRids  = @('win-x64', 'osx-arm64', 'osx-x64'),
    [switch]   $StdoutLog,
    [switch]   $SkipBuild,
    [switch]   $SkipSmoke,
    [switch]   $StageOnly
)

$ErrorActionPreference = 'Stop'

$Repo       = Split-Path -Parent $PSScriptRoot
$Artifacts  = Join-Path $Repo 'artifacts'
$ApiUrl     = "$SiteUrl/webapi"
$EditorBase = '/' + $EditorPath.Trim('/') + '/'                       # "/editors/video/"
$EditorDir  = Join-Path $SiteRoot ($EditorPath -replace '/', '\')     # C:\ishaunted\editors\video
$WebApiDir  = Join-Path $SiteRoot 'webapi'
if (-not $SidecarDrop) { $SidecarDrop = Join-Path $Repo 'Ben.Video.Sidecar\installer\dist' }

# Canonical order regardless of what the caller typed: the API first (the Coming Soon page or the
# previous build is still serving), the static apps next (invisible), the website last.
$order = @('webapi', 'editor', 'files', 'website')
$Apps  = @($order | Where-Object { $Apps -contains $_ })

function Write-Step   ([string]$m) { Write-Host ''; Write-Host "== $m" -ForegroundColor Cyan }
function Write-Detail ([string]$m) { Write-Host "   $m" }
function Write-Warn   ([string]$m) { Write-Host "   WARNING: $m" -ForegroundColor Yellow }

# ---- JSON helpers -----------------------------------------------------------
# Configuration paths use the .NET colon notation ("Geocodio:ApiKey") so they read the same here as
# they do in the C# that consumes them.

function Read-JsonFile ([string]$path) {
    (Get-Content -LiteralPath $path -Raw -Encoding UTF8) | ConvertFrom-Json
}

function Write-JsonFile ([string]$path, $obj) {
    $json = $obj | ConvertTo-Json -Depth 64
    [IO.File]::WriteAllText($path, $json + "`r`n", (New-Object Text.UTF8Encoding($false)))
}

function Test-JsonNode ($node) {
    $null -ne $node -and $node -is [System.Management.Automation.PSCustomObject]
}

function Get-JsonValue ($obj, [string]$path) {
    $node = $obj
    foreach ($part in $path.Split(':')) {
        if (-not (Test-JsonNode $node)) { return $null }
        if ($node.PSObject.Properties.Name -notcontains $part) { return $null }
        $node = $node.$part
    }
    $node
}

function Set-JsonValue ($obj, [string]$path, $value) {
    $parts = $path.Split(':')
    $node  = $obj
    for ($i = 0; $i -lt $parts.Count - 1; $i++) {
        $part = $parts[$i]
        if ($node.PSObject.Properties.Name -notcontains $part -or -not (Test-JsonNode $node.$part)) {
            $node | Add-Member -NotePropertyName $part -NotePropertyValue ([pscustomobject]@{}) -Force
        }
        $node = $node.$part
    }
    $node | Add-Member -NotePropertyName $parts[-1] -NotePropertyValue $value -Force
}

# Serilog's MSSqlServer sink holds its OWN copy of the connection string, so pointing
# ConnectionStrings at a database is not enough on its own - and the sink creates its table at
# startup (autoCreateSqlTable), which makes a wrong value a startup dependency rather than a
# logging nicety. Patched in place so the sink keeps its columnOptionsSection: those four columns
# are what the Application / Source / EntityType / Operation properties get written into.
function Set-SerilogConnectionString ($cfg, [string]$conn) {
    if (-not $conn) {
        # An empty WriteTo replaces the list from the base file rather than adding to it. Better a
        # site with no error log than a site that will not start because the log has nowhere to go.
        Set-JsonValue $cfg 'Serilog:WriteTo' @()
        Write-Detail 'Serilog SQL sink removed (no connection string given)'
        return
    }
    $writeTo = Get-JsonValue $cfg 'Serilog:WriteTo'
    if ($null -eq $writeTo) { return }
    $patched = 0
    foreach ($sink in @($writeTo)) {
        if ((Test-JsonNode $sink) -and
            $sink.PSObject.Properties.Name -contains 'Args' -and
            (Test-JsonNode $sink.Args) -and
            $sink.Args.PSObject.Properties.Name -contains 'connectionString') {
            $sink.Args.connectionString = $conn
            $patched++
        }
    }
    Write-Detail "Serilog sink connection string patched ($patched sink(s))"
}

# ---- web.config helpers -----------------------------------------------------
# web.config is REGENERATED by every 'dotnet publish', so anything set here has to be re-applied on
# every deploy. That is why these are functions in the deploy script and not a one-time server edit.

function Set-WebConfigRequestLimit ([string]$path, [int64]$bytes) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($path)
    $sws = $xml.SelectSingleNode('/configuration/location/system.webServer')
    if (-not $sws) { $sws = $xml.SelectSingleNode('/configuration/system.webServer') }
    if (-not $sws) { throw "no <system.webServer> in $path" }

    $node = $sws
    foreach ($name in @('security', 'requestFiltering', 'requestLimits')) {
        $child = $node.SelectSingleNode($name)
        if (-not $child) { $child = $node.AppendChild($xml.CreateElement($name)) }
        $node = $child
    }
    # IIS caps request bodies at 30 MB by default and rejects anything larger before ASP.NET Core
    # ever sees it. The app sets no size limit of its own, so this is the only thing standing
    # between a user and a failed video upload.
    $node.SetAttribute('maxAllowedContentLength', "$bytes")
    # $node is <requestLimits>; its parent is <requestFiltering>. removeServerHeader stops IIS
    # announcing "Server: Microsoft-IIS/10.0" on every response (the website's source web.config
    # carries the same attribute; the API's web.config only exists after publish, so it is set here).
    $node.ParentNode.SetAttribute('removeServerHeader', 'true')
    $xml.Save($path)
    Write-Detail "maxAllowedContentLength = $bytes; removeServerHeader = true"
}

function Set-WebConfigEnvironment ([string]$path, [string]$name, [string]$value) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($path)
    $ancm = $xml.SelectSingleNode('//aspNetCore')
    if (-not $ancm) { throw "no <aspNetCore> in $path" }
    $vars = $ancm.SelectSingleNode('environmentVariables')
    if (-not $vars) { $vars = $ancm.AppendChild($xml.CreateElement('environmentVariables')) }

    # The element is <environmentVariable>, NOT <add>. Nearly every other IIS collection uses
    # <add>, and aspnetcore_schema_v2.xml declares this one as
    #     <element name="environmentVariables"><collection addElement="environmentVariable" ...>
    # so <add> here is not a variable that fails to arrive - it makes the whole web.config
    # unparseable. IIS then cannot read system.webServer/aspNetCore at all and ANCM answers every
    # request with a bodyless HTTP 500, logging event 1034 "Unable to get required configuration
    # section ... Possible reason is web.config authoring error". Nothing points at this line, and
    # the site's static child applications keep working - the <location inheritInChildApplications>
    # wrapper keeps the broken section away from them - so it reads like the .NET apps crashed on
    # startup rather than like a config typo. It cost a deployment on 2026-08-21.
    $existing = $vars.SelectSingleNode("environmentVariable[@name='$name']")
    if (-not $existing) {
        $existing = $vars.AppendChild($xml.CreateElement('environmentVariable'))
        $existing.SetAttribute('name', $name)
    }
    # Clear out anything a previous run of the broken version left behind, so a redeploy heals the
    # file rather than carrying the poison forward.
    foreach ($stale in @($vars.SelectNodes("add[@name='$name']"))) { [void]$vars.RemoveChild($stale) }
    $existing.SetAttribute('value', $value)
    $xml.Save($path)
    Write-Detail "$name = $value"
}

function Enable-WebConfigStdoutLog ([string]$path, [string]$outDir) {
    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($path)
    $ancm = $xml.SelectSingleNode('//aspNetCore')
    if (-not $ancm) { throw "no <aspNetCore> in $path" }
    $ancm.SetAttribute('stdoutLogEnabled', 'true')
    $xml.Save($path)
    # The module does not create the directory, and a missing folder silently disables the log you
    # just switched on.
    New-Item -ItemType Directory -Force (Join-Path $outDir 'logs') | Out-Null
    Write-Detail 'startup logging ON -> logs\stdout*.log (turn it off once the site is up)'
}

# ---- process helpers --------------------------------------------------------

# The commit being deployed. Resolved once so every published app carries the same stamp, and so
# the smoke checks have something to compare the running build against. Empty when the source is
# not a git checkout, which downgrades the identity check to a warning rather than failing a
# deploy that is otherwise fine.
$script:DeployCommit = ''
try {
    $script:DeployCommit = (& git -C $Repo rev-parse HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0) { $script:DeployCommit = '' }
} catch { $script:DeployCommit = '' }

function Invoke-Publish ([string]$project, [string]$outDir, [switch]$RidSpecific) {
    Write-Detail "publishing $project -> $outDir"
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    # -r win-x64 with --self-contained false: framework-dependent (the Hosting Bundle supplies the
    # runtime) but RID-specific, so runtimes\ carries only the native assets Windows actually
    # loads. Without the RID the publish copies every platform's natives - SkiaSharp, the SQL
    # client and friends for linux, macOS and arm - and the package reaches ~488 MB, of which
    # ~444 MB could never execute on the target.
    $publishArgs = @((Join-Path $Repo $project), '-c', 'Release', '-o', $outDir, '--nologo', '-v', 'q')
    if ($RidSpecific) { $publishArgs += @('-r', 'win-x64', '--self-contained', 'false') }
    # Stamp the commit into the binary. .NET appends it to InformationalVersion as "+<sha>", which
    # is how /api/public/build can later say WHICH build is answering - the check that would have
    # caught the 2026-08-26 deploy that reported success and shipped the previous build.
    if ($script:DeployCommit) { $publishArgs += "-p:SourceRevisionId=$script:DeployCommit" }
    & dotnet publish @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project (exit $LASTEXITCODE)" }
}

function Invoke-Mirror ([string]$from, [string]$to, [string[]]$excludeDirs, [string[]]$excludeFiles) {
    $roboArgs = @($from, $to, '/MIR', '/R:3', '/W:2', '/NFL', '/NDL', '/NP', '/NJH', '/NJS')
    if ($excludeDirs)  { $roboArgs += '/XD'; $roboArgs += $excludeDirs }
    if ($excludeFiles) { $roboArgs += '/XF'; $roboArgs += $excludeFiles }
    & robocopy.exe @roboArgs | Out-Null
    # Robocopy uses its exit code as a bit field: 0-7 are success (files copied, extras removed,
    # mismatches), 8 and above are genuine failures.
    if ($LASTEXITCODE -ge 8) { throw "robocopy $from -> $to failed (exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
    Write-Detail "mirrored -> $to"
}

# app_offline.htm is how ANCM is asked to let go: the module stops the app, drains it, and serves
# this file for everything under it. Without it the copy fights a running process for its own DLLs.
$AppOfflineHtml = @'
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Back shortly</title>
<style>body{font:16px/1.6 system-ui,sans-serif;margin:15vh auto;max-width:32rem;padding:0 1rem;
color:#dee2e6;background:#1a1d21}h1{font-size:1.4rem}</style></head>
<body><h1>Back shortly</h1><p>IsHaunted.com is being updated. This takes a moment.</p></body></html>
'@

function Set-AppOffline ([string]$dir) {
    if (-not (Test-Path $dir)) { return }
    [IO.File]::WriteAllText((Join-Path $dir 'app_offline.htm'), $AppOfflineHtml)
    Start-Sleep -Milliseconds 750     # let ANCM notice and drain before the files move
}

function Clear-AppOffline ([string]$dir) {
    $f = Join-Path $dir 'app_offline.htm'
    if (Test-Path $f) { Remove-Item -Force $f }
}

# =============================================================================
# 0. One deploy at a time
# =============================================================================
# Staging the sidecar drop and publishing the apps both write into the same site folders, and a
# second run starting while the first is mid-publish interleaves file copies — producing a site
# that is neither the old build nor the new one, with nothing in any log saying so. A lock file
# is enough: this is one machine, and the failure being prevented is a person running the script
# twice, not a distributed race.
#
# The handle is held for the life of the process, so an abandoned lock dies with the shell that
# owned it rather than needing a stale-lock timeout nobody would tune correctly.
$lockPath = Join-Path ([System.IO.Path]::GetTempPath()) 'ishaunted-deploy.lock'
try {
    $script:DeployLock = [System.IO.File]::Open(
        $lockPath, [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
}
catch {
    throw @"
Another deploy or stage is already running on this machine (lock: $lockPath).
Wait for it to finish. If you are certain nothing is running - the previous run was killed, say -
close that PowerShell window and try again; the lock is released when its process exits.
"@
}

# No explicit release, and none is needed: the handle belongs to this process, so Windows drops it
# when PowerShell exits — normally, on a throw, or if the window is closed. Wrapping the whole
# script in try/finally would release it a few milliseconds earlier and would be a structural
# change to a script that cannot be syntax-checked from the machine it is edited on.

# =============================================================================
# 1. Preflight
# =============================================================================
Write-Step 'Preflight'

if (-not (Test-Path $SecretsPath)) {
    throw @"
No secrets file at $SecretsPath.
Copy scripts\secrets.template.json there and fill it in (Administrators + SYSTEM only).
See docs/deploy-production.md.
"@
}

# Everything from step 3 onward writes to the server: the site folders, the downloads folder and
# applicationHost.config. Publishing and patching into artifacts\ needs none of that, so this check
# lives here rather than in a #Requires at the top - -StageOnly is meant to run as yourself.
if (-not $StageOnly) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin  = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
                    [Security.Principal.WindowsBuiltinRole]::Administrator)
    if (-not $isAdmin) {
        throw 'Deploying writes to the site folders and applicationHost.config - run this from an elevated PowerShell, or pass -StageOnly to publish and patch without touching the server.'
    }
}

$secrets = Read-JsonFile $SecretsPath

# The secrets file may override the connection string (a different server, or SQL authentication).
# When it does not, the Integrated Security default applies and no SQL credential exists anywhere.
$sqlConn = Get-JsonValue $secrets 'SqlConnectionString'
if (-not $sqlConn) { $sqlConn = $SqlConnectionString }

$smtpPassword = Get-JsonValue $secrets 'SmtpPassword'
$stripeSecret  = Get-JsonValue $secrets 'StripeSecretKey'
$stripeWebhook = Get-JsonValue $secrets 'StripeWebhookSecret'

# The two Stripe values are indistinguishable to everything downstream — both are opaque strings
# on an app pool — so the wrong one in the wrong slot fails silently, at the worst moment, in a
# way nothing explains. It has already happened once: a pk_live_ was pasted into
# StripeWebhookSecret, which would have made every live webhook delivery answer 400 while
# payments appeared to work. The prefixes are Stripe's own and are worth checking here, where
# somebody is watching, rather than in a dashboard delivery log three days later.
if ($stripeSecret -and -not $stripeSecret.StartsWith('sk_')) {
    if ($stripeSecret.StartsWith('pk_')) {
        throw "StripeSecretKey holds a PUBLISHABLE key (pk_...). The publishable key is not used by this app at all - it belongs to browser-side checkout, which this site does not use. Put the SECRET key (sk_live_... from Stripe > Developers > API keys) here."
    }
    throw "StripeSecretKey does not look like a Stripe secret key - it should start with sk_ (sk_live_ in production, sk_test_ in a sandbox)."
}
if ($stripeWebhook -and -not $stripeWebhook.StartsWith('whsec_')) {
    if ($stripeWebhook.StartsWith('pk_') -or $stripeWebhook.StartsWith('sk_')) {
        throw "StripeWebhookSecret holds an API key, not a signing secret. The signing secret starts with whsec_ and is shown on the webhook endpoint's own page in Stripe > Developers > Webhooks - not on the API keys page."
    }
    throw "StripeWebhookSecret does not look like a Stripe signing secret - it should start with whsec_."
}
if ($stripeSecret -and $stripeSecret.StartsWith('sk_test_')) {
    Write-Warn 'StripeSecretKey is a TEST key. Real cards will be refused and no money will move. Use sk_live_ for production.'
}
if (-not $smtpPassword) {
    Write-Warn 'SmtpPassword is not in the secrets file. Email will fail, and because accounts require a confirmed address, anyone who registers will never be able to sign in.'
}

# Entra sign-in needs the client secret the app registration issued. Like the SMTP password it is
# handed to the pool as an environment variable rather than written into appsettings.json, so it
# never lands in the deployed package. Absent is a legitimate state - Entra stays off.
$entraSecret   = Get-JsonValue $secrets 'AzureAd:ClientSecret'
$entraClientId = Get-JsonValue $secrets 'AzureAd:ClientId'
if ($entraClientId -and -not $entraSecret) {
    Write-Warn 'AzureAd:ClientId is set but AzureAd:ClientSecret is not. The website will offer Microsoft sign-in and then fail redeeming the code (AADSTS7000218), which looks like a broken button rather than a missing setting.'
}

Write-Detail "repo    : $Repo"
Write-Detail "apps    : $($Apps -join ', ')"
Write-Detail "site    : $SiteUrl  ($SiteRoot)"
Write-Detail "api     : $ApiUrl"
Write-Detail "editor  : $SiteUrl$EditorBase  ($EditorDir)"
Write-Detail "uploads : $UploadsRoot"
$sqlAuth = if ($sqlConn -match 'integrated security\s*=\s*true') { 'Integrated Security (no stored credential)' } else { 'SQL authentication' }
Write-Detail "sql     : $($sqlConn.Substring(0, [Math]::Min(42, $sqlConn.Length)))...  [$sqlAuth]"

if (-not $SkipBuild) {
    # The Telerik feed is only needed to acquire packages that are not already in the global cache.
    # A missing feed is therefore a warning, not a failure: the restore itself is the real test,
    # and it fails loudly.
    $sources = (& dotnet nuget list source) -join "`n"
    if ($sources -notmatch 'nuget\.telerik\.com') {
        Write-Warn 'No Telerik NuGet source configured. Restore will work only from the global package cache; a version bump would need the feed:'
        Write-Detail 'dotnet nuget add source https://nuget.telerik.com/v3/index.json --name Telerik --username <account> --password <password-or-key>'
    }
    # Telerik.Licensing looks for the key file in %APPDATA%\Telerik on Windows (and ~/.telerik
    # elsewhere). Without it the build still succeeds - it just renders a trial watermark, which is
    # the sort of thing you find out about from a screenshot rather than from the build log.
    $licenceLocations = @(
        (Join-Path $env:APPDATA 'Telerik\telerik-license.txt'),
        (Join-Path $env:USERPROFILE '.telerik\telerik-license.txt')
    )
    if (-not ($licenceLocations | Where-Object { Test-Path $_ }) -and -not $env:TELERIK_LICENSE) {
        Write-Warn "No Telerik licence found (looked in $($licenceLocations -join ', ')) and no TELERIK_LICENSE variable. The UI will render a trial watermark."
    }
}

if (-not $StageOnly) {
    foreach ($p in @($SiteRoot, $UploadsRoot, $FilesRoot)) {
        if (-not (Test-Path $p)) { throw "$p does not exist. Run scripts\setup-iis-ishaunted.ps1 first." }
    }
}

# =============================================================================
# 2. Publish and patch
# =============================================================================

$webapiOut  = Join-Path $Artifacts 'webapi'
$websiteOut = Join-Path $Artifacts 'website'
$editorOut  = Join-Path $Artifacts 'editor'

# ---- WebApi -----------------------------------------------------------------
if ($Apps -contains 'webapi') {
    Write-Step 'WebApi: publish and configure'
    if (-not $SkipBuild) { Invoke-Publish 'Ben.Data.WebApi' $webapiOut -RidSpecific }

    foreach ($f in @('Ben.Data.WebApi.dll', 'web.config')) {
        if (-not (Test-Path (Join-Path $webapiOut $f))) {
            throw "publish produced no $f, so IIS cannot host this"
        }
    }

    # Merged into appsettings.json, NOT appsettings.Production.json. An environment-specific file
    # only loads when ASPNETCORE_ENVIRONMENT matches its name, and a copy-deployed package has no
    # say in what that variable says on the far end. The upload root lived in Production.json once;
    # the server started with an environment that did not load it, fell back to the empty string in
    # the base file, and refused to start - for a setting that was sitting in the package,
    # correctly spelled, and simply never read.
    $cfgPath = Join-Path $webapiOut 'appsettings.json'
    $cfg = Read-JsonFile $cfgPath

    Set-JsonValue $cfg 'ConnectionStrings:BenDbConnectionString' $sqlConn
    Set-JsonValue $cfg 'FileStorage:RootPath' $UploadsRoot     # blank here is a hard startup failure
    Set-JsonValue $cfg 'AppBaseUrl' $SiteUrl                   # the links in outgoing email
    Set-SerilogConnectionString $cfg $sqlConn

    # Carried from the secrets file. Each of these turns a feature off SILENTLY when absent, which
    # is worse than failing loudly: geocoding returns nothing, and Entra sign-in disappears from
    # the UI because Program.cs only wires it up when ClientId parses as a GUID.
    $carry = [ordered]@{
        'TelerikKey'                      = (Get-JsonValue $secrets 'TelerikKey')
        'Geocodio:ApiKey'                 = (Get-JsonValue $secrets 'GeocodioApiKey')
        'Geocodio:BaseUrl'                = (Get-JsonValue $secrets 'GeocodioBaseUrl')
        'AzureAd:TenantId'                = (Get-JsonValue $secrets 'AzureAd:TenantId')
        'AzureAd:ClientId'                = (Get-JsonValue $secrets 'AzureAd:ClientId')
        # No Audience key. Program.cs derives ValidAudiences from ClientId as api://<id> and <id>,
        # and never reads AzureAd:Audience - it was carried here for a setting that does not exist.
        'SeedData:SuperAdmin:Email'       = (Get-JsonValue $secrets 'SeedSuperAdmin:Email')
        'SeedData:SuperAdmin:DisplayName' = (Get-JsonValue $secrets 'SeedSuperAdmin:DisplayName')
        'SeedData:SuperAdmin:Password'    = (Get-JsonValue $secrets 'SeedSuperAdmin:Password')
        'RateLimits:AuthPerMinute'        = (Get-JsonValue $secrets 'RateLimitAuthPerMinute')
        # Absolute path to ffmpeg.exe on the server. Without it video posts wait for a moderator
        # instead of being screened, and audio/video keeps its metadata - the app reports the
        # feature as unavailable rather than failing an upload (item 181, 186 F5b).
        'MediaTools:FfmpegPath'           = (Get-JsonValue $secrets 'FfmpegPath')
    }
    foreach ($key in $carry.Keys) {
        if ($null -ne $carry[$key] -and "$($carry[$key])" -ne '') {
            Set-JsonValue $cfg $key $carry[$key]
        } else {
            Write-Detail ("{0,-32} not set (feature stays off)" -f $key)
        }
    }

    Write-JsonFile $cfgPath $cfg
    Write-Detail 'merged into appsettings.json (loads in every environment)'

    # Development points at a laptop; Production is not written at all, since everything above
    # lives in the base file that loads regardless of what the environment says.
    foreach ($f in @('appsettings.Development.json', 'appsettings.Production.json')) {
        $p = Join-Path $webapiOut $f
        if (Test-Path $p) { Remove-Item -Force $p; Write-Detail "removed $f" }
    }

    $webConfig = Join-Path $webapiOut 'web.config'
    Set-WebConfigEnvironment $webConfig 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-WebConfigRequestLimit $webConfig 4294967295
    if ($StdoutLog) { Enable-WebConfigStdoutLog $webConfig $webapiOut }
}

# ---- Website ----------------------------------------------------------------
if ($Apps -contains 'website') {
    Write-Step 'Website: publish and configure'
    if (-not $SkipBuild) { Invoke-Publish 'Ben.Web.Website' $websiteOut -RidSpecific }

    foreach ($f in @('Ben.Web.Website.dll', 'web.config')) {
        if (-not (Test-Path (Join-Path $websiteOut $f))) {
            throw "publish produced no $f, so IIS cannot host this"
        }
    }

    # Build identity, asserted by the smoke checks in section 6. A fresh GUID is written into the
    # artifact's wwwroot on every run - including -SkipBuild runs, because this section always
    # executes - and the smoke check demands the live site serve it back. Without this, a deploy
    # that copies nothing passes its own checks: the OLD build answers 200 just as happily, which
    # is exactly what happened on 2026-08-27 when three "successful" deploys had shipped nothing.
    $script:BuildStamp = [Guid]::NewGuid().ToString('N')
    $stampCommit = ''
    try { $stampCommit = (& git -C (Split-Path $PSScriptRoot -Parent) rev-parse HEAD 2>$null) } catch { }
    $stampJson = '{"stamp":"' + $script:BuildStamp + '","commit":"' + $stampCommit + '","stampedUtc":"' + [DateTime]::UtcNow.ToString('o') + '"}'
    [IO.File]::WriteAllText((Join-Path $websiteOut 'wwwroot\build-info.json'), $stampJson)
    $commitShort = if ($stampCommit) { $stampCommit.Substring(0, 8) } else { 'unknown' }
    Write-Detail ("build stamp {0}  (commit {1})" -f $script:BuildStamp, $commitShort)

    # Same reasoning as the API: appsettings.json, not appsettings.Production.json. (The bash
    # publish-website.sh still writes Production.json - this is the deliberate divergence.)
    $cfgPath = Join-Path $websiteOut 'appsettings.json'
    $cfg = Read-JsonFile $cfgPath

    # The website is a front end - sign-in, cases, media, all of it goes through the API - so a
    # wrong value here gives a site that loads perfectly and then fails every single operation.
    # Signing in is the only thing that actually proves it.
    Set-JsonValue $cfg 'WebApi:BaseUrl' $ApiUrl
    Set-JsonValue $cfg 'SiteIdentity:BaseUrl' $SiteUrl
    Set-JsonValue $cfg 'ConnectionStrings:BenDbConnectionString' $sqlConn
    Set-SerilogConnectionString $cfg $sqlConn   # the bash script left this sink pointed at localhost

    # Entra, if it is configured at all. ClientId has to reach the WEBSITE and not only the API:
    # Program.cs decides whether to register the OpenIdConnect scheme by looking at this value, so
    # without it here the Microsoft button simply is not rendered and nothing says why. The client
    # secret is deliberately absent - it goes on the pool as an environment variable in section 5.
    # ApiScope is what makes the access token addressed to the API rather than to Microsoft Graph;
    # omit it and every /webapi call with an Entra token comes back 401 on audience validation.
    $siteCarry = [ordered]@{
        'AzureAd:TenantId'              = (Get-JsonValue $secrets 'AzureAd:TenantId')
        'AzureAd:ClientId'              = $entraClientId
        'DownstreamApis:BenWebApi:Scope' = (Get-JsonValue $secrets 'AzureAd:ApiScope')
    }
    foreach ($key in $siteCarry.Keys) {
        if ($null -ne $siteCarry[$key] -and "$($siteCarry[$key])" -ne '') {
            Set-JsonValue $cfg $key $siteCarry[$key]
        } else {
            Write-Detail ("{0,-32} not set (Entra sign-in stays off)" -f $key)
        }
    }

    Write-JsonFile $cfgPath $cfg
    Write-Detail "WebApi:BaseUrl = $ApiUrl"

    foreach ($f in @('appsettings.Development.json', 'appsettings.Production.json')) {
        $p = Join-Path $websiteOut $f
        if (Test-Path $p) { Remove-Item -Force $p; Write-Detail "removed $f" }
    }

    $webConfig = Join-Path $websiteOut 'web.config'
    Set-WebConfigEnvironment $webConfig 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-WebConfigRequestLimit $webConfig 4294967295
    if ($StdoutLog) { Enable-WebConfigStdoutLog $webConfig $websiteOut }
}

# ---- Editor (Blazor WebAssembly) --------------------------------------------
if ($Apps -contains 'editor') {
    Write-Step 'Editor: publish and configure'
    if (-not $SkipBuild) { Invoke-Publish 'Ben.Wasm.Video' $editorOut }

    $www = Join-Path $editorOut 'wwwroot'
    if (-not (Test-Path $www)) { throw "publish produced no wwwroot at $www" }

    # <base href> must be the sub-path, not "/". Blazor resolves the runtime and every asset
    # against it: point it at "/" and the browser asks the site root for files that live under the
    # editor's folder, gets the website's 404 page, and the app sits on "Loading" with nothing in
    # the console to say why. Matched loosely because the quoting style is the template's, not ours.
    $indexPath = Join-Path $www 'index.html'
    $html  = [IO.File]::ReadAllText($indexPath)
    $rx    = New-Object System.Text.RegularExpressions.Regex('<base\s+href="[^"]*"\s*/?>')
    $count = $rx.Matches($html).Count
    if ($count -ne 1) { throw "expected exactly one <base href> in index.html, found $count" }
    $patched = $rx.Replace($html, "<base href=""$EditorBase"" />", 1)
    [IO.File]::WriteAllText($indexPath, $patched, (New-Object Text.UTF8Encoding($false)))
    Write-Detail "base href set to $EditorBase"

    # Fetched at startup rather than compiled in, so this is a file edit and not a rebuild - but an
    # empty value is a *working* configuration (a fully local editor with no Server tab), so a
    # mistake here does not throw. It just quietly removes the half of the product that talks to
    # the site. The /webapi suffix is part of the value: the editor appends "/api/..." to it.
    $editorCfgPath = Join-Path $www 'appsettings.json'
    $editorCfg = Read-JsonFile $editorCfgPath
    Set-JsonValue $editorCfg 'BenVideo:WebApiBaseUrl' $ApiUrl
    Write-JsonFile $editorCfgPath $editorCfg
    Write-Detail "WebApiBaseUrl set to $ApiUrl"

    # The development override ships in the publish output and wins on any machine whose
    # environment says Development. It points at localhost:5252 - nothing at all, on this box.
    $devCfg = Join-Path $www 'appsettings.Development.json'
    if (Test-Path $devCfg) { Remove-Item -Force $devCfg }

    # Publish pre-compresses every static file, so each file patched or removed above still has .br
    # and .gz twins holding the ORIGINAL bytes: <base href="/"> and an empty WebApiBaseUrl. Stock
    # IIS never serves those, but a server with pre-compressed static serving enabled would hand
    # back a stale index.html and the app would look for its runtime at the site root. Deleted
    # rather than regenerated - there is no brotli encoder here to rebuild the .br with, and one
    # correct representation beats two that can disagree.
    $stale = @('index.html', 'appsettings.json', 'appsettings.Development.json')
    foreach ($s in $stale) {
        foreach ($ext in @('br', 'gz')) {
            $p = Join-Path $www "$s.$ext"
            if (Test-Path $p) { Remove-Item -Force $p }
        }
    }
    # Prove it rather than trust it: any surviving twin of a patched file is a stale copy of the
    # very values this step exists to set.
    foreach ($s in $stale) {
        foreach ($ext in @('br', 'gz')) {
            if (Test-Path (Join-Path $www "$s.$ext")) {
                throw "$s.$ext survived - it holds the pre-patch bytes"
            }
        }
    }
    Write-Detail 'no stale pre-compressed copies of the patched files'

    # IIS serves nothing whose file extension it does not recognise, and a Blazor app is almost
    # entirely .wasm and .dat.
    if (-not (Test-Path (Join-Path $www 'web.config'))) {
        throw 'no web.config in the editor publish - IIS will refuse .wasm and .dat files'
    }
    Write-Detail 'web.config present (WASM MIME types)'

    # The editor's own build identity, asserted below exactly as the website's is. Without it the
    # editor's only smoke check was "does index.html contain the right <base href>", which the
    # PREVIOUS deploy's index.html answers just as happily - so a deploy that copied nothing
    # passed (2026-09-05 audit, wasm-8). A plain wwwroot file works here because this app is
    # served by IIS as static files, with no build-time asset manifest deciding what exists.
    $script:EditorStamp = [Guid]::NewGuid().ToString('N')
    $editorCommit = ''
    try { $editorCommit = (& git -C (Split-Path $PSScriptRoot -Parent) rev-parse HEAD 2>$null) } catch { }
    $editorStampJson = '{"stamp":"' + $script:EditorStamp + '","commit":"' + $editorCommit + '","stampedUtc":"' + [DateTime]::UtcNow.ToString('o') + '"}'
    [IO.File]::WriteAllText((Join-Path $www 'build-info.json'), $editorStampJson)
    Write-Detail ("editor build stamp {0}" -f $script:EditorStamp)
}

# =============================================================================
# 3. Stage the sidecar downloads
# =============================================================================
if (($Apps -contains 'files') -and -not $StageOnly) {
    Write-Step 'Sidecar downloads'
    $sidecarRoot = Join-Path $FilesRoot 'sidecar-video'
    foreach ($rid in $SidecarRids) {
        # Both platforms now ship something you double-click: a disk image on macOS, an Inno Setup
        # installer on Windows. The zip they replaced meant extract it, find a script, and run it
        # past an execution policy or a Terminal prompt - three steps and a command line, to
        # install something whose whole job is to be invisible.
        #
        # The zip is still accepted for either RID, and always loses. That ordering is the point:
        # a stale zip left in dist/ cannot quietly outrank a freshly built installer, while a RID
        # that only ever produced a zip keeps working unchanged.
        # Windows gets the same treatment for the same reason: the zip meant extract it, find
        # install.ps1, right-click it and get past the execution policy. A double-click installer
        # is what that should have been. The .exe wins over the zip exactly as the .dmg does, so a
        # stale zip left in dist/ cannot quietly outrank a fresh installer, and a RID with only a
        # zip keeps working unchanged.
        $candidates = if ($rid -like 'osx-*') {
            @("BenVideoSidecar-$rid.dmg", "BenVideoSidecar-$rid.zip")
        } else {
            @("BenVideoSidecar-$rid.exe", "BenVideoSidecar-$rid.zip")
        }

        $name = $null
        foreach ($candidate in $candidates) {
            if (Test-Path (Join-Path $SidecarDrop $candidate)) { $name = $candidate; break }
        }

        if (-not $name) {
            Write-Warn "No installer for $rid in $SidecarDrop - the downloads page will 404 that link."
            # Named exactly, because this is the one message somebody reads at the moment their
            # installer is missing. Windows is two steps and they are not interchangeable: build.sh
            # produces the app payload (it cross-publishes, so it can run anywhere), and
            # build-installer.ps1 wraps that payload with Inno Setup and only runs on Windows.
            Write-Detail 'Build it. macOS: installer/macos/build.sh then build-dmg.sh.'
            Write-Detail '           Windows: installer/windows/build.sh for the payload, then'
            Write-Detail '           installer/windows/build-installer.ps1 on Windows for the .exe.'
            continue
        }

        $src = Join-Path $SidecarDrop $name
        $dstDir = Join-Path $sidecarRoot $rid
        New-Item -ItemType Directory -Force $dstDir | Out-Null

        # Remove the other format from a previous deploy. Leaving it behind means the folder serves
        # two installers of different vintages under names the page might still link.
        foreach ($stale in $candidates) {
            if ($stale -ne $name) { Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $dstDir $stale) }
        }

        Copy-Item -Force $src (Join-Path $dstDir $name)
        # These builds are unsigned, so a published hash is the only integrity story a tester has.
        # The format matches 'shasum -a 256', which is what the download page tells them to compare.
        $hash = (Get-FileHash -Algorithm SHA256 (Join-Path $dstDir $name)).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $dstDir 'checksums.txt'), "$hash  $name`n")
        $mb = [Math]::Round((Get-Item $src).Length / 1MB)
        Write-Detail "staged $name ($mb MB) -> /files/sidecar-video/$rid/"

        # The downloads page links one fixed filename per platform, and this block stages whichever
        # of two formats it found. When they disagree the page offers a 404, silently, to somebody
        # who came to the site specifically to download the thing (2026-09-05 audit, F17). Rewrite
        # the deployed page to name what is actually there, and say which.
        # The artifact, not the deployed copy: this section runs before section 4 copies the
        # editor to the site, so patching here is what reaches the server.
        $page = Join-Path $editorOut 'wwwroot\downloads\index.html'
        if (Test-Path $page) {
            $html = [IO.File]::ReadAllText($page)
            $rewrote = $false
            foreach ($stale in $candidates) {
                if ($stale -eq $name) { continue }
                $from = "/files/sidecar-video/$rid/$stale"
                if ($html.Contains($from)) {
                    $html = $html.Replace($from, "/files/sidecar-video/$rid/$name")
                    $rewrote = $true
                }
            }
            if ($rewrote) {
                [IO.File]::WriteAllText($page, $html)
                Write-Detail "downloads page now links $name for $rid"
            }
        }
    }
}

# =============================================================================
# 4. Deploy
# =============================================================================
if ($StageOnly) {
    Write-Step 'Staged only - nothing was copied to the server'
    Write-Detail "Packages are in $Artifacts"
    Write-Detail 'Review them, then run the same command elevated without -StageOnly to deploy.'
    return
}

if ($Apps -contains 'webapi') {
    Write-Step 'Deploy WebApi'
    Set-AppOffline $WebApiDir
    try {
        Invoke-Mirror $webapiOut $WebApiDir -excludeDirs @((Join-Path $WebApiDir 'logs')) `
                                            -excludeFiles @('app_offline.htm')
    } finally {
        Clear-AppOffline $WebApiDir
    }
}

if ($Apps -contains 'editor') {
    Write-Step 'Deploy editor'
    New-Item -ItemType Directory -Force $EditorDir | Out-Null
    Invoke-Mirror (Join-Path $editorOut 'wwwroot') $EditorDir
}

if ($Apps -contains 'website') {
    Write-Step 'Deploy website'

    # First run: the Coming Soon page is still the site. Its logo and its videos exist nowhere
    # else, so they are moved aside rather than mirrored away.
    $firstRun = (Test-Path (Join-Path $SiteRoot 'index.html')) -and
                -not (Test-Path (Join-Path $SiteRoot 'Ben.Web.Website.dll'))
    if ($firstRun) {
        $backup = "${SiteRoot}-coming-soon-backup"
        New-Item -ItemType Directory -Force $backup | Out-Null
        foreach ($item in @('index.html', 'css', 'js', 'static')) {
            $p = Join-Path $SiteRoot $item
            if (Test-Path $p) { Move-Item -Force $p (Join-Path $backup $item) }
        }
        Write-Detail "Coming Soon content moved to $backup"
    } else {
        Set-AppOffline $SiteRoot
    }

    try {
        # /XD is load-bearing. The child applications live inside the website's own folder, and
        # /MIR deletes anything in the destination that is not in the source - without these
        # exclusions this copy wipes the API and the editor that were just deployed.
        Invoke-Mirror $websiteOut $SiteRoot `
            -excludeDirs @($WebApiDir, (Join-Path $SiteRoot 'editors'), (Join-Path $SiteRoot 'logs')) `
            -excludeFiles @('app_offline.htm')
    } finally {
        Clear-AppOffline $SiteRoot
    }
}

# =============================================================================
# 5. The secret that never touches a file, and a restart
# =============================================================================
# The double underscore is how .NET maps an environment variable onto a nested configuration key -
# Smtp__Password becomes Smtp:Password. These live in applicationHost.config, readable only by
# administrators, and deliberately never appear in any appsettings file in the deployed package.
function Set-PoolEnv ([string]$pool, [string]$name, [string]$value) {
    $filter = "system.applicationHost/applicationPools/add[@name='$pool']/environmentVariables"
    # -ErrorAction SilentlyContinue rather than Stop-inside-try: with Stop, the cmdlet still
    # writes its complaint to the error stream on the way out, so a first deploy printed a red
    # "element not found" for every variable it was about to add correctly. Alarming, meaningless,
    # and the thing an operator then learns to ignore — which is how a real error gets missed.
    Remove-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter `
        -Name '.' -AtElement @{ name = $name } -ErrorAction SilentlyContinue -ErrorVariable null
    Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter `
        -Name '.' -Value @{ name = $name; value = $value }
    Write-Detail "$name set on $pool"
}

if (($Apps -contains 'webapi') -or ($Apps -contains 'website')) {
    Write-Step 'Application pool configuration'
    Import-Module WebAdministration -ErrorAction Stop

    if (($Apps -contains 'webapi') -and $smtpPassword) {
        Set-PoolEnv $WebApiPool 'Smtp__Password' $smtpPassword
    }

    # Stripe's two secrets ride the same rail as the SMTP password: pool environment, never a
    # deployed file. Both empty is a valid state - checkout answers with a sentence and the
    # manual admin path keeps working - so absence warns rather than fails.
    if ($Apps -contains 'webapi') {
        if ($stripeSecret)  { Set-PoolEnv $WebApiPool 'Stripe__SecretKey' $stripeSecret }
        else { Write-Warn 'StripeSecretKey is not in the secrets file. Online payment will report itself unavailable; manual subscription entry still works.' }
        if ($stripeWebhook) { Set-PoolEnv $WebApiPool 'Stripe__WebhookSecret' $stripeWebhook }
        elseif ($stripeSecret) { Write-Warn 'StripeSecretKey is set but StripeWebhookSecret is not. Checkouts and renewals still fulfill synchronously, but Stripe''s webhook deliveries will all be refused - register the endpoint in the dashboard and put its whsec here.' }
    }

    # On the root pool, not the API's: Ben.Web.Website is the confidential client that redeems the
    # authorization code, so it is the process that needs the secret. The API only validates the
    # resulting JWT, which takes no secret at all.
    if (($Apps -contains 'website') -and $entraSecret) {
        Set-PoolEnv $RootPool 'AzureAd__ClientSecret' $entraSecret
    }

    if ($Apps -contains 'webapi') {
        Restart-WebAppPool -Name $WebApiPool
        Write-Detail "recycled $WebApiPool"
    }
    if ($Apps -contains 'website') {
        Restart-WebAppPool -Name $RootPool
        Write-Detail "recycled $RootPool"
    }
}

# =============================================================================
# 6. Smoke checks
# =============================================================================
if (-not $SkipSmoke) {
    Write-Step 'Smoke checks'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    # Run FROM the server on purpose. The website calls the API server-side at this same public
    # URL, so if the router cannot route a request back to itself - no NAT hairpin - these checks
    # fail here exactly the way the website will fail later, while every page still renders.
    $checks = @()
    if ($Apps -contains 'webapi') {
        $checks += @{ url = "$ApiUrl/api/public/cases?page=1&pageSize=1"; what = 'API + database' }
        # Process identity is NOT checked here. It lives in the 'Build identity' step at the end,
        # which asks /api/public/build which commit the worker is running - a direct answer that
        # needs no route to keep existing. This spot briefly held a probe asserting the
        # chunked-uploads route answered (401) where an older build says (404); that worked, but
        # tied the check to one feature's URL surviving forever. The commit comparison is the
        # better shape, so this is deliberately empty.
    }
    if ($Apps -contains 'website') {
        $checks += @{ url = "$SiteUrl/"; what = 'website' }
        if ($script:BuildStamp) {
            # File identity, which the commit comparison at the end cannot give: that one asks the
            # WORKER what it is running, and a worker can be running new code while the website's
            # static files are stale (or the reverse). This asks the FILES. Cache-busted so
            # nothing between here and the disk can answer from before the copy.
            #
            # Served by a minimal endpoint in the website's Program.cs, not as a static file:
            # MapStaticAssets serves only what the build-time manifest lists, and this stamp is
            # written after publish - so as a plain wwwroot file it 404s while sitting on disk.
            $checks += @{ url = "$SiteUrl/build-info.json?cb=$([Guid]::NewGuid().ToString('N'))"
                          what = 'website build identity'; expect = $script:BuildStamp
                          why = 'the site is serving a build-info.json OLDER than the one just copied - the deploy did not land' }
        }
    }
    if ($Apps -contains 'editor') {
        $checks += @{ url = "$SiteUrl$EditorBase"; what = 'editor'; expect = "<base href=""$EditorBase""" }
        $checks += @{ url = "$SiteUrl${EditorBase}downloads/"; what = 'sidecar downloads page' }

        # Demands the stamp this run just wrote, so a deploy that copied nothing fails instead of
        # being confirmed by the build it was supposed to replace (2026-09-05 audit, wasm-8).
        if ($script:EditorStamp) {
            $checks += @{ url = "$SiteUrl${EditorBase}build-info.json?cb=$([Guid]::NewGuid().ToString('N'))"
                          what = 'editor build identity'; expect = $script:EditorStamp
                          why = 'the editor is serving a build-info.json OLDER than the one just copied - the deploy did not land' }
        }

        # The ffmpeg core is served by the app now rather than fetched from a CDN, and it is the
        # one thing whose absence leaves an editor that loads, looks right and cannot start
        # (2026-09-05 audit, media-13).
        $checks += @{ url = "$SiteUrl${EditorBase}_content/Ben.Video.Editor/js/ffmpeg-core/st/ffmpeg-core.wasm"
                      what = 'vendored ffmpeg core'
                      why = 'the editor cannot start its engine without it' }
    }
    if ($Apps -contains 'files') {
        foreach ($rid in $SidecarRids) {
            if (Test-Path (Join-Path $FilesRoot "sidecar-video\$rid\checksums.txt")) {
                $checks += @{ url = "$SiteUrl/files/sidecar-video/$rid/checksums.txt"; what = "sidecar $rid" }
            }
        }
    }

    # Section 5 recycles the pools immediately above this, and an in-process ANCM application does
    # not answer while it is still starting - IIS returns a bare HTTP 503 until the worker is up.
    # A single attempt therefore reports a perfectly good deployment as a failure, and the script
    # throws at the very end having done everything correctly. That happened twice on 2026-08-21,
    # both times on the API, which is the slowest to start because it builds its EF model and runs
    # the file-migration service. Retry a 503 rather than treat startup as an outage; anything that
    # is genuinely broken still fails, just $SmokeRetries * $SmokeRetryDelay seconds later.
    $SmokeRetries    = 6
    $SmokeRetryDelay = 5

    $failed = @()
    foreach ($check in $checks) {
        $lastError = $null
        $ok        = $false

        for ($attempt = 1; $attempt -le $SmokeRetries -and -not $ok; $attempt++) {
            try {
                # Some checks EXPECT a refusal: the build-identity probe asserts (401) from a route
                # the old build does not have. A non-2xx makes Invoke-WebRequest throw, so the
                # status is fished out of the WebException rather than treated as failure outright.
                $expectStatus = if ($check.ContainsKey('expectStatus')) { [int]$check['expectStatus'] } else { 200 }
                $r = $null
                $status = $null
                try {
                    $r = Invoke-WebRequest -Uri $check['url'] -UseBasicParsing -TimeoutSec 60
                    $status = [int]$r.StatusCode
                } catch [System.Net.WebException] {
                    if ($null -eq $_.Exception.Response) { throw }
                    $status = [int]$_.Exception.Response.StatusCode
                }
                if ($status -ne $expectStatus) { throw "HTTP ($status) where ($expectStatus) was expected" }
                if ($check.ContainsKey('expect') -and ($null -eq $r -or $r.Content -notlike "*$($check['expect'])*")) {
                    # A 200 is not proof on its own: if the sub-application was never created, the
                    # website answers with its own 404 page, which is also HTML and also 200-shaped.
                    $why = if ($check.ContainsKey('why')) { $check['why'] }
                           else { "200, but the body is not the editor - is $EditorBase an IIS Application?" }
                    throw $why
                }
                $ok = $true
                $suffix = if ($attempt -gt 1) { "  (after $attempt attempts)" } else { '' }
                Write-Host "   OK   $($check['what'])  $($check['url'])$suffix" -ForegroundColor Green
            } catch {
                $lastError = $_.Exception.Message
                # Only a starting application is worth waiting for. A 404, a 500 or a wrong body is
                # a real result and retrying it just delays the report.
                $starting = $lastError -match '\(503\)|actively refused|Unable to connect'
                if (-not $starting -or $attempt -eq $SmokeRetries) { break }
                Write-Host "   ...  $($check['what']) not up yet (attempt $attempt/$SmokeRetries), waiting $SmokeRetryDelay s" -ForegroundColor DarkGray
                Start-Sleep -Seconds $SmokeRetryDelay
            }
        }

        if (-not $ok) {
            Write-Host "   FAIL $($check['what'])  $($check['url'])" -ForegroundColor Red
            Write-Host "        $lastError" -ForegroundColor Red
            $failed += $check['what']
        }
    }
    if ($failed.Count -gt 0) { throw "smoke checks failed: $($failed -join ', ')" }

    # ---- Build identity -------------------------------------------------------
    # The check every one above is missing. They ask whether the site RESPONDS; this asks whether
    # what is responding is what we just published. On 2026-08-26 a deploy reported success and
    # served the previous build - stale files and a not-recycled pool both pass a liveness check
    # cleanly - and it was found by hand, comparing an endpoint that should have existed against
    # one that did. Answered from inside the running worker, so it also catches the case where the
    # files copied correctly and IIS kept serving the old process.
    if (($Apps -contains 'webapi') -and $script:DeployCommit) {
        Write-Step 'Build identity'
        $expected = $script:DeployCommit
        $running  = $null
        try {
            $running = (Invoke-RestMethod -Uri "$ApiUrl/api/public/build" -TimeoutSec 20).commit
        } catch {
            Write-Host "   WARN could not read $ApiUrl/api/public/build - $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "        An API older than 2026-08-26 has no such route; deploy it once and this check starts working." -ForegroundColor DarkGray
        }

        if ($running) {
            if ($running -eq $expected) {
                Write-Host "   OK   API is running $($expected.Substring(0,[Math]::Min(9,$expected.Length)))" -ForegroundColor Green
            } else {
                $r = if ($running) { $running.Substring(0,[Math]::Min(9,$running.Length)) } else { '(none)' }
                throw ("the API is serving $r but this deploy published " +
                       "$($expected.Substring(0,[Math]::Min(9,$expected.Length))). " +
                       'The copy landed or it did not, and the pool restarted or it did not - ' +
                       'check the publish output above, then run iisreset.')
            }
        }
    }
}

Write-Step 'Done'
Write-Host @"
   Deployed: $($Apps -join ', ')

   Still to verify by hand - none of it is provable from a status code:
     1. Sign in at $SiteUrl/ . The pages render whether or not WebApi:BaseUrl is right;
        only a real API call tells you, and signing in is the first one.
     2. Sign in inside the editor at $SiteUrl$EditorBase and open the Server tab. That is
        what proves the editor's own API URL.
     3. Register a test account. It needs a confirmation email, so this is the SMTP check.
"@
