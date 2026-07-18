# AverageBen — Production Deployment Guide

This guide walks through standing up a fresh **Ben.Data.WebApi** + **Ben.Web.WebApp** installation on a production (or staging) server from a clean OS.

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
9. [Run the Applications](#9-run-the-applications)
10. [Verify Deployment](#10-verify-deployment)
11. [Updating an Existing Deployment](#11-updating-an-existing-deployment)

---

## 1. Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | 10.0 or newer | `dotnet --version` to check |
| SQL Server | 2019+ (or SQL Azure) | Any edition; Express works for small deployments |
| Git | any | For cloning / pulling |
| Telerik UI for Blazor license | 14.1.0+ | Required at **build time** — see §6 |

**Optional (for Docker-based SQL Server):**

```bash
# Docker Desktop or Docker Engine
docker --version
```

---

## 2. Clone the Repository

```bash
git clone https://github.com/VandyBen/AverageBen.git
cd AverageBen
git checkout main   # or the branch/tag you want to deploy
```

---

## 3. SQL Server Database

### Option A — Use an existing SQL Server instance

Create an empty database named `BenDb` (or any name you choose; update the connection string to match):

```sql
-- Run in SSMS, sqlcmd, or Azure Data Studio
CREATE DATABASE BenDb;
```

### Option B — Docker (lightweight / staging)

```bash
docker run -e ACCEPT_EULA=Y \
           -e MSSQL_SA_PASSWORD=<YOUR_STRONG_PASSWORD> \
           -p 1433:1433 \
           --name bendb-sql \
           --restart unless-stopped \
           -d mcr.microsoft.com/mssql/server:2022-latest
```

> **Apple Silicon (M1/M2/M3):** Add `--platform linux/amd64` — SQL Server has no ARM image and runs under Rosetta 2.

---

## 4. Configure Ben.Data.WebApi

Create `Ben.Data.WebApi/appsettings.Production.json` (this file is git-ignored — never commit real secrets).

```json
{
  "ConnectionStrings": {
    "BenDbConnectionString": "Server=<host>,<port>;Database=BenDb;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;"
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

| Placeholder | Example |
|---|---|
| `<host>` | `localhost` / `sql.mycompany.com` / `mydb.database.windows.net` |
| `<port>` | `1433` (default; omit for Azure SQL) |
| `<user>` | `sa` / `benapp` |
| `<password>` | strong password — store in a secrets manager in production |

### SeedData notes

- **`SuperAdmin`** — created once at first startup. Sets the email/password for the initial administrator account.  
  Set `Password` to `REPLACE_ME_WITH_YOUR_PASSWORD` to disable seeding (the seeder skips on that value).
- **`SeedOrganization.Enabled`** — set `false` for a clean production deployment (seed org is for development only).

---

## 5. Configure Ben.Web.WebApp

Create `Ben.Web.WebApp/appsettings.Production.json`.

```json
{
  "WebApi": {
    "BaseUrl": "https://<webapi-host>:<port>",
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
      "BaseUrl": "https://<webapi-host>:<port>",
      "Scope": "api://e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1/access_as_user"
    }
  },
  "ConnectionStrings": {
    "BenDbConnectionString": "Server=<host>,<port>;Database=BenDb;User Id=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;"
  }
}
```

### Key values

| Key | Description |
|---|---|
| `WebApi:BaseUrl` | URL the **browser-server** uses to call the API (must be reachable from the app server, not the user's browser) |
| `WebApi:TelerikKey` | Telerik license key — copied from your Telerik account → `telerik-license.txt` (base64 content) |
| `AzureAd:ClientSecret` | Entra app registration client secret — see §7 |

> **If Entra login is not required**, you can leave `AzureAd` and `DownstreamApis` out of the config. Local Identity login (email/password) will still work.

---

## 6. Telerik License

Telerik UI for Blazor requires a license key at **build time**.  
The key lives in `~/.telerik/telerik-license.txt` (checked automatically by the NuGet package) or can be passed as the `TelerikKey` config value at runtime.

1. Log in at [telerik.com](https://www.telerik.com) → Your Account → Downloads → License Keys.
2. Download `telerik-license.txt`.
3. Copy it to `~/.telerik/telerik-license.txt` on the build/server machine.

```bash
mkdir -p ~/.telerik
cp telerik-license.txt ~/.telerik/telerik-license.txt
```

---

## 7. Microsoft Entra OIDC (optional)

Only needed if users will sign in with Microsoft accounts (Entra ID).  
If skipped, local Identity login (email/password) is fully functional.

### App registration (already created — `AverageBen.net`)

| Setting | Value |
|---|---|
| Client ID | `e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1` |
| Tenant | `common` (multi-tenant) |

### Steps for a new environment

1. In the Azure Portal → App Registrations → `AverageBen.net`:
   - **Authentication** → add redirect URIs for your production domain:
     - `https://<webapp-host>/signin-oidc`
     - `https://<webapp-host>/signout-oidc`
   - **Certificates & Secrets** → create a new client secret, copy the value.
2. Add the secret to `Ben.Web.WebApp/appsettings.Production.json` → `AzureAd:ClientSecret`.

---

## 8. Apply Database Schema

### Option A — EF Core migrations (recommended)

```bash
cd /path/to/AverageBen

# Set environment so Production config is used
export ASPNETCORE_ENVIRONMENT=Production

dotnet ef database update \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi
```

This applies all 10 migrations in order. If the database already exists and is partially migrated, only pending migrations are applied.

### Option B — SQL script

```bash
# Execute the pre-generated idempotent script
sqlcmd -S <host>,<port> -U <user> -P <password> \
       -d BenDb -i scripts/create-database.sql
```

The script is in `scripts/create-database.sql`. It is idempotent — re-running it against an existing database is safe.

### Regenerate the script after new migrations

```bash
dotnet ef migrations script \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi \
  --output scripts/create-database.sql \
  --idempotent
```

> Always commit the updated `scripts/create-database.sql` after adding a migration.

---

## 9. Run the Applications

Both applications must be running simultaneously. The WebApp calls the WebApi on every authenticated request.

### Ben.Data.WebApi

```bash
cd /path/to/AverageBen
ASPNETCORE_ENVIRONMENT=Production \
dotnet run --project Ben.Data.WebApi/Ben.Data.WebApi.csproj \
           --urls "http://0.0.0.0:5252"
```

On first startup, the three seeders run automatically:

| Seeder | What it creates |
|---|---|
| `SuperAdminSeeder` | `SuperAdmin` role + initial admin user (email/password from config) |
| `OrganizationSeeder` | Seed org + users (only if `Enabled: true` in config — disable for production) |
| `UploadFileTypeSeeder` | `"Logo"` upload file type with 6 image extension patterns |

### Ben.Web.WebApp

```bash
ASPNETCORE_ENVIRONMENT=Production \
dotnet run --project Ben.Web.WebApp/Ben.Web.WebApp.csproj \
           --urls "http://0.0.0.0:5078"
```

### Running as a systemd service (Linux)

Create `/etc/systemd/system/ben-webapi.service`:

```ini
[Unit]
Description=Ben WebApi
After=network.target

[Service]
WorkingDirectory=/path/to/AverageBen
ExecStart=/usr/bin/dotnet run --project Ben.Data.WebApi/Ben.Data.WebApi.csproj --urls http://0.0.0.0:5252
Restart=always
Environment=ASPNETCORE_ENVIRONMENT=Production
User=www-data

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable ben-webapi
sudo systemctl start ben-webapi
```

Repeat for `ben-webapp.service` pointing at `Ben.Web.WebApp`.

---

## 10. Verify Deployment

| Check | URL / Command | Expected |
|---|---|---|
| WebApi health | `GET http://<host>:5252/swagger/index.html` | Swagger UI with ~131 endpoints |
| WebApi login | `POST http://<host>:5252/login` with `{ "email": "...", "password": "..." }` | `{ "accessToken": "...", "refreshToken": "..." }` |
| WebApp loads | `http://<host>:5078` | Login page renders |
| Local login | Log in with SuperAdmin email/password | Home page, "Administration" button visible |
| Entra login | Click "Sign in with Microsoft" | Redirects to Microsoft, returns to app |
| DB tables | `SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'` | 41 tables |
| Seeders ran | `SELECT Email FROM AppUsers` | SuperAdmin email present |

---

## 11. Updating an Existing Deployment

```bash
# 1. Pull latest code
git pull origin main

# 2. If new migrations exist, apply them
dotnet ef database update \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi

# 3. Regenerate SQL script and commit
dotnet ef migrations script \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi \
  --output scripts/create-database.sql \
  --idempotent
git add scripts/create-database.sql
git commit -m "Update create-database.sql for migration <name>"
git push

# 4. Restart services
sudo systemctl restart ben-webapi
sudo systemctl restart ben-webapp
```

---

## Quick-Reference: Config File Locations

| File | Purpose | Git-tracked? |
|---|---|---|
| `Ben.Data.WebApi/appsettings.json` | Base config (non-secret defaults) | ✅ Yes |
| `Ben.Data.WebApi/appsettings.Development.json` | Dev secrets (DB, seed passwords) | ❌ No (gitignored) |
| `Ben.Data.WebApi/appsettings.Production.json` | Production secrets | ❌ No (gitignored) |
| `Ben.Web.WebApp/appsettings.json` | Base config | ✅ Yes |
| `Ben.Web.WebApp/appsettings.Development.json` | Dev secrets (Entra, Telerik key) | ❌ No (gitignored) |
| `Ben.Web.WebApp/appsettings.Production.json` | Production secrets | ❌ No (gitignored) |
| `~/.telerik/telerik-license.txt` | Telerik license key | ❌ No (machine-local) |
| `scripts/create-database.sql` | Full idempotent schema script | ✅ Yes |
