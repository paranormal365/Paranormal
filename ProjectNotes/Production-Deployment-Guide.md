# AverageBen — Production Deployment Guide (Windows / MS SQL Server)

This guide walks through standing up **Ben.Data.WebApi** + **Ben.Web.WebApp** on a Windows production (or staging) server from scratch, using Microsoft SQL Server as the database.

All commands are **PowerShell** unless noted otherwise.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Clone the Repository](#2-clone-the-repository)
3. [SQL Server Database](#3-sql-server-database)
4. [Configure Ben.Data.WebApi](#4-configure-bendatawebapi)
5. [Configure Ben.Web.WebApp](#5-configure-benwebwebapp)
6. [Telerik License](#6-telerik-license)
7. [Microsoft Entra OIDC (optional)](#7-microsoft-entra-oidc-optional)
8. [Apply Database Schema](#8-apply-database-schema)
9. [Build Front-End Assets (WaveSurfer)](#9-build-front-end-assets-wavesurfer)
10. [Run the Applications](#10-run-the-applications)
11. [Verify Deployment](#11-verify-deployment)
12. [Updating an Existing Deployment](#12-updating-an-existing-deployment)

---

## 1. Prerequisites

| Requirement | Version | Download |
|---|---|---|
| Windows Server | 2019 / 2022 or Windows 10/11 | — |
| .NET SDK | 10.0 or newer | https://dot.net/download |
| Microsoft SQL Server | 2019+ (any edition; Express is fine for small installs) | https://www.microsoft.com/sql-server/sql-server-downloads |
| SQL Server Management Studio (SSMS) | Latest | https://aka.ms/ssms |
| Git for Windows | Latest | https://git-scm.com/download/win |
| Telerik UI for Blazor license | 14.1.0+ | https://www.telerik.com (see §6) |

**Verify .NET is installed (PowerShell):**

```powershell
dotnet --version   # must be 10.0.x or newer
```

**Install the EF Core CLI tools** (required for applying migrations):

```powershell
dotnet tool install --global dotnet-ef
dotnet ef --version   # verify
```

---

## 2. Clone the Repository

```powershell
git clone https://github.com/VandyBen/AverageBen.git
cd AverageBen
git checkout main   # or the branch/tag to deploy
```

---

## 3. SQL Server Database

### Create the database

Open **SSMS**, connect to your SQL Server instance, and run:

```sql
CREATE DATABASE BenDb;
```

Or use `sqlcmd` (comes with SQL Server):

```powershell
# Windows Authentication
sqlcmd -S .\SQLEXPRESS -E -Q "CREATE DATABASE BenDb"

# SQL Authentication
sqlcmd -S localhost -U sa -P "<password>" -Q "CREATE DATABASE BenDb"
```

Replace `.\SQLEXPRESS` with your SQL Server instance name (e.g., `localhost`, `SERVER01`, `SERVER01\MSSQLSERVER`).

### Create a dedicated SQL login (recommended)

```sql
-- Run in SSMS connected to your SQL Server instance
CREATE LOGIN benapp WITH PASSWORD = '<strong-password>';
USE BenDb;
CREATE USER benapp FOR LOGIN benapp;
ALTER ROLE db_owner ADD MEMBER benapp;
```

This gives the application a dedicated login rather than using `sa`.

### Authentication modes

| Mode | Connection string | When to use |
|---|---|---|
| **SQL Server Auth** | `User Id=benapp;Password=...` | Dedicated app account (recommended) |
| **Windows Auth** | `Integrated Security=True` | App runs as a domain/service account |

---

## 4. Configure Ben.Data.WebApi

Create `Ben.Data.WebApi\appsettings.Production.json`. This file is **git-ignored** — never commit secrets.

```json
{
  "ConnectionStrings": {
    "BenDbConnectionString": "Server=<instance>;Database=BenDb;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;"
  },
  "AzureAd": {
    "TenantId": "common",
    "ClientId": "e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1",
    "Audience": "api://e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1"
  },
  "SeedData": {
    "SuperAdmin": {
      "Email": "<admin-email>",
      "DisplayName": "<admin-display-name>",
      "Password": "<strong-password>"
    },
    "SeedOrganization": {
      "Enabled": false
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Warning",
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    }
  }
}
```

### Connection string reference

| Placeholder | SQL Auth example | Windows Auth example |
|---|---|---|
| `<instance>` | `SERVERNAME\SQLEXPRESS` or `localhost,1433` | same |
| Credentials | `User Id=benapp;Password=P@ss!` | `Integrated Security=True` (remove User Id/Password) |
| `Encrypt` | `True` (use `False` only if getting SSL errors on a non-TLS setup) | same |
| `TrustServerCertificate` | `True` for self-signed certs; `False` if a proper cert is installed | same |

### SeedData notes

| Key | Notes |
|---|---|
| `SuperAdmin.Email` / `Password` | Creates the initial admin account on first startup. Set `Password` to `REPLACE_ME_WITH_YOUR_PASSWORD` to skip seeding. |
| `SeedOrganization.Enabled: false` | Keep `false` for production. Set `true` only to pre-populate demo data. |

---

## 5. Configure Ben.Web.WebApp

Create `Ben.Web.WebApp\appsettings.Production.json`.

```json
{
  "WebApi": {
    "BaseUrl": "http://<webapi-host>:<port>",
    "TelerikKey": "<your-telerik-license-key>"
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "ClientId": "e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1",
    "ClientSecret": "<your-entra-client-secret>",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-oidc"
  },
  "DownstreamApis": {
    "BenWebApi": {
      "BaseUrl": "http://<webapi-host>:<port>",
      "Scope": "api://e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1/access_as_user"
    }
  },
  "ConnectionStrings": {
    "BenDbConnectionString": "Server=<instance>;Database=BenDb;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

### Key values

| Key | Description |
|---|---|
| `WebApi:BaseUrl` | URL the WebApp **server** uses to call the WebApi. If both run on the same machine: `http://localhost:5252`. |
| `WebApi:TelerikKey` | Telerik license key string — see §6. |
| `AzureAd:ClientSecret` | Entra client secret — see §7. Omit the entire `AzureAd` block if Microsoft login is not needed. |

---

## 6. Telerik License

Telerik UI for Blazor requires a valid license key at build time and runtime.

1. Log in at [telerik.com](https://www.telerik.com) → Your Account → Downloads → License Keys.
2. Download `telerik-license.txt`.
3. Place it in your Windows user profile so the NuGet package finds it automatically:

```powershell
New-Item -ItemType Directory -Force -Path "$env:USERPROFILE\.telerik"
Copy-Item "C:\Downloads\telerik-license.txt" "$env:USERPROFILE\.telerik\telerik-license.txt"
```

The Telerik build tasks automatically resolve `%USERPROFILE%\.telerik\telerik-license.txt`.  
Alternatively, paste the license key string into `WebApi:TelerikKey` in `appsettings.Production.json` (see §5).

---

## 7. Microsoft Entra OIDC (optional)

Only required for Microsoft account sign-in. Local email/password login works without any Entra configuration.

### For the existing registration (`AverageBen.net`, Client ID `e75f71ef-...`)

1. **Azure Portal** → App Registrations → `AverageBen.net` → **Authentication**:
   - Add **Redirect URI** (type: Web): `https://<webapp-host>/signin-oidc`
   - Add **Front-channel logout URL**: `https://<webapp-host>/signout-oidc`
2. **Certificates & Secrets** → **New client secret** → copy the value immediately (shown once only).
3. Add the secret to `Ben.Web.WebApp\appsettings.Production.json` → `AzureAd:ClientSecret`.

---

## 8. Apply Database Schema

### Option A — EF Core migrations (recommended)

Run from the repository root in PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"

dotnet ef database update `
  --project Ben.Data.Source `
  --startup-project Ben.Data.WebApi
```

Applies all 11 migrations. If the database is already partially migrated, only pending ones are applied.

### Option B — SQL script

```powershell
sqlcmd -S <instance> -U <user> -P <password> `
       -d BenDb `
       -i scripts\create-database.sql
```

The script at `scripts\create-database.sql` is **idempotent** — safe to re-run; only missing migrations are applied.

You can also open `scripts\create-database.sql` directly in **SSMS** and execute it.

### Regenerate the SQL script after adding a new migration

```powershell
dotnet ef migrations script `
  --project Ben.Data.Source `
  --startup-project Ben.Data.WebApi `
  --output scripts\create-database.sql `
  --idempotent
```

Commit the updated file: `git add scripts\create-database.sql && git commit -m "Update schema script"`

---

## 10. Build Front-End Assets (WaveSurfer)

The `WaveSurferPlayer` Blazor component (in `Ben.Web.Library/Manage/Audio/`) depends on
pre-built WaveSurfer ESM bundles in `Ben.Web.WebApp/wwwroot/js/wavesurfer/`.

> **node_modules and dist/ are git-ignored.** The build artifacts at `wwwroot/js/wavesurfer/`
> **are committed** and are already present after `git clone`/`git pull`. You only need to
> re-run the build when WaveSurfer source files have been changed.

**Prerequisites:** Node.js 20+ and npm installed on the build machine.

```powershell
# From the repository root
cd Ben.Web.WebApp\wwwroot\ts\wavesurfer
npm install --legacy-peer-deps   # first-time only; installs build tooling
npm run build:blazor             # compiles TypeScript → ESM bundles in wwwroot/js/wavesurfer/
cd ..\..\..\..
```

**What is built:**

| Output | Description |
|---|---|
| `wwwroot/js/wavesurfer/wavesurfer.esm.js` | Core WaveSurfer library |
| `wwwroot/js/wavesurfer/plugins/regions.esm.js` | Regions plugin |
| `wwwroot/js/wavesurfer/plugins/hover.esm.js` | Hover / cursor label plugin |
| `wwwroot/js/wavesurfer/plugins/timeline.esm.js` | Timeline ruler plugin |
| `wwwroot/js/wavesurfer/plugins/zoom.esm.js` | Mouse-wheel zoom plugin |
| `wwwroot/js/wavesurfer/plugins/minimap.esm.js` | Navigation minimap plugin |
| `wwwroot/js/wavesurfer/plugins/spectrogram.esm.js` | Spectrogram plugin |
| `wwwroot/js/wavesurfer/plugins/spectrogram-windowed.esm.js` | Windowed spectrogram (long files) |
| `wwwroot/js/wavesurfer/plugins/envelope.esm.js` | Volume envelope plugin |
| `wwwroot/js/wavesurfer/plugins/record.esm.js` | Microphone recording plugin |

**Telerik color integration:** Colors are resolved at runtime from Telerik CSS custom
properties (`--kendo-color-primary`, `--kendo-body-text`, etc.) — no build configuration needed.

---

## 10. Run the Applications

Both services must run simultaneously — the WebApp calls the WebApi on every authenticated request.

### Option A — Quick test (`dotnet run`)

Open two PowerShell windows:

```powershell
# Window 1 — WebApi
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --project Ben.Data.WebApi\Ben.Data.WebApi.csproj --urls "http://0.0.0.0:5252"
```

```powershell
# Window 2 — WebApp
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --project Ben.Web.WebApp\Ben.Web.WebApp.csproj --urls "http://0.0.0.0:5078"
```

> On **first startup** the three seeders run automatically:
>
> | Seeder | Creates |
> |---|---|
> | `SuperAdminSeeder` | `SuperAdmin` role + initial admin user |
> | `OrganizationSeeder` | Seed org + users (only if `Enabled: true`) |
> | `UploadFileTypeSeeder` | `"Logo"` file type with `.jpg/.jpeg/.png/.gif/.webp/.svg` |

### Option B — Windows Service (production / auto-start)

**Publish self-contained executables first:**

```powershell
# WebApi
dotnet publish Ben.Data.WebApi\Ben.Data.WebApi.csproj `
  -c Release -r win-x64 --self-contained true `
  -o C:\Ben\WebApi

# WebApp
dotnet publish Ben.Web.WebApp\Ben.Web.WebApp.csproj `
  -c Release -r win-x64 --self-contained true `
  -o C:\Ben\WebApp
```

**Register as Windows Services** (run PowerShell **as Administrator**):

```powershell
# WebApi
New-Service -Name "BenWebApi" `
            -BinaryPathName "C:\Ben\WebApi\Ben.Data.WebApi.exe --urls http://0.0.0.0:5252" `
            -DisplayName "Ben Web API" `
            -StartupType Automatic

Set-ItemProperty `
  -Path "HKLM:\SYSTEM\CurrentControlSet\Services\BenWebApi" `
  -Name "Environment" `
  -Value "ASPNETCORE_ENVIRONMENT=Production"

# WebApp
New-Service -Name "BenWebApp" `
            -BinaryPathName "C:\Ben\WebApp\Ben.Web.WebApp.exe --urls http://0.0.0.0:5078" `
            -DisplayName "Ben Web App" `
            -StartupType Automatic

Set-ItemProperty `
  -Path "HKLM:\SYSTEM\CurrentControlSet\Services\BenWebApp" `
  -Name "Environment" `
  -Value "ASPNETCORE_ENVIRONMENT=Production"

# Start both
Start-Service BenWebApi, BenWebApp
```

**Manage services:**

```powershell
Get-Service     BenWebApi, BenWebApp
Start-Service   BenWebApi, BenWebApp
Stop-Service    BenWebApi, BenWebApp
Restart-Service BenWebApi, BenWebApp
```

### Option C — IIS Reverse Proxy (recommended for enterprise / HTTPS termination)

1. Install the **ASP.NET Core Hosting Bundle** (includes the IIS Module):  
   https://dotnet.microsoft.com/download → ASP.NET Core Runtime → Hosting Bundle

2. Publish both apps (see Option B publish commands).

3. In **IIS Manager**, create two sites:
   - `BenWebApi` → physical path `C:\Ben\WebApi` → port `5252`
   - `BenWebApp` → physical path `C:\Ben\WebApp` → port `5078` (or `80`/`443` if user-facing)

4. Each site's `web.config` (auto-generated on publish) — verify it contains:

```xml
<aspNetCore processPath=".\Ben.Data.WebApi.exe" arguments="" stdoutLogEnabled="true">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
  </environmentVariables>
</aspNetCore>
```

5. Set the IIS application pool identity to a Windows account (or the `benapp` SQL login's service account) that has `db_owner` on `BenDb`.

---

## 11. Verify Deployment

| Check | How | Expected |
|---|---|---|
| WebApi responds | Browser: `http://<host>:5252/swagger/index.html` | Swagger UI — ~131 endpoints listed |
| WebApi login | `POST http://<host>:5252/login` `{"email":"...","password":"..."}` | `{"accessToken":"..."}` |
| WebApp loads | Browser: `http://<host>:5078` | Login page renders with Telerik styles |
| Local login | Sign in with SuperAdmin credentials | Home page — "Administration" button visible |
| DB tables | SSMS: `SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'` | **≥ 47** (11 migrations) |
| Seeders ran | SSMS: `SELECT Email FROM AppUsers` | SuperAdmin email present |
| Logo file type | SSMS: `SELECT Name FROM UploadFileTypes` | `Logo` row present |
| Entra login | Click "Sign in with Microsoft" (if configured) | Redirects to Microsoft, returns to app |

---

| WaveSurfer assets | Browser: `/_content/Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor.js` | Module loads without 404 |
| WaveSurfer core | Browser DevTools: `http://<host>:5078/js/wavesurfer/wavesurfer.esm.js` | JS module response (not 404) |

---

## 12. Updating an Existing Deployment

```powershell
# 1. Stop services
Stop-Service BenWebApi, BenWebApp

# 2. Pull latest code
git pull origin main

# 3. Apply any new EF migrations
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet ef database update `
  --project Ben.Data.Source `
  --startup-project Ben.Data.WebApi

# 4. Re-build WaveSurfer front-end assets (only needed if wavesurfer source changed)
cd Ben.Web.WebApp\wwwroot\ts\wavesurfer
npm run build:blazor
cd ..\..\..\..

# 5. Regenerate and commit the SQL script
dotnet ef migrations script `
  --project Ben.Data.Source `
  --startup-project Ben.Data.WebApi `
  --output scripts\create-database.sql `
  --idempotent
git add scripts\create-database.sql
git commit -m "Update create-database.sql for migration <name>"
git push

# 6. Re-publish
dotnet publish Ben.Data.WebApi\Ben.Data.WebApi.csproj `
  -c Release -r win-x64 --self-contained true -o C:\Ben\WebApi
dotnet publish Ben.Web.WebApp\Ben.Web.WebApp.csproj `
  -c Release -r win-x64 --self-contained true -o C:\Ben\WebApp

# 7. Restart services
Start-Service BenWebApi, BenWebApp
```

---

## Quick-Reference: Config File Locations

| File | Purpose | Git-tracked? |
|---|---|---|
| `Ben.Data.WebApi\appsettings.json` | Base config (non-secret defaults) | ✅ Yes |
| `Ben.Data.WebApi\appsettings.Development.json` | Dev secrets | ❌ Gitignored |
| `Ben.Data.WebApi\appsettings.Production.json` | **Production secrets** | ❌ Gitignored |
| `Ben.Web.WebApp\appsettings.json` | Base config | ✅ Yes |
| `Ben.Web.WebApp\appsettings.Development.json` | Dev secrets | ❌ Gitignored |
| `Ben.Web.WebApp\appsettings.Production.json` | **Production secrets** | ❌ Gitignored |
| `%USERPROFILE%\.telerik\telerik-license.txt` | Telerik license key | ❌ Machine-local |
| `scripts\create-database.sql` | Idempotent schema script | ✅ Yes |

---

## Firewall / Port Notes

| Port | Service | Expose externally? |
|---|---|---|
| `5252` | Ben.Data.WebApi | Only if WebApp is on a separate server; otherwise keep internal |
| `5078` | Ben.Web.WebApp | Yes — user-facing (or put behind IIS on 80/443) |
| `1433` | MS SQL Server | No — keep on the internal network; never expose SQL Server to the internet |

If using IIS with TLS termination, only ports `80`/`443` need to be open in the Windows Firewall.
