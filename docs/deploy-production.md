# Deploying IsHaunted.com

Three applications share one IIS site and one certificate:

| Path | What | Kind |
|---|---|---|
| `/` | the website | Blazor Server — a .NET process |
| `/webapi` | the WebApi | a .NET process |
| `/editor` | the video editor | static files, runs in the browser |

They are **separate IIS Applications** under a single site. That is what lets three things share
`https://ishaunted.com` without a second certificate, and it is not optional — see
[Why each one is an Application](#why-each-one-is-an-application).

The API sits at `/webapi` rather than `/api` because its own routes already start with `/api`;
mounting it at `/api` would give you `/api/api/cases`. ASP.NET Core learns its path base from IIS,
so nothing in the code changes: a controller at `/api/cases` answers on
`https://ishaunted.com/webapi/api/cases`.

## Prerequisites

- **.NET 10 Hosting Bundle** on the server. Supplies both the ASP.NET Core runtime and the
  `AspNetCoreModuleV2` handler that each `web.config` names. Missing it gives HTTP 500.19 or 502.5,
  and neither error mentions it.
- **WebSockets** enabled (Server Manager → Add Roles and Features → Web Server → Application
  Development → WebSocket Protocol). Blazor Server holds a SignalR circuit per visitor. Without
  WebSockets it falls back to long polling — it works, and it is worse.
- A **SQL Server** the web server can reach, with the schema created. Nothing applies migrations at
  startup, so an empty or absent database is a running site where every request fails.

## Build the three packages

```bash
scripts/publish-webapi.sh "<sql-connection-string>" "D:\ishaunted-files"
scripts/publish-website.sh https://ishaunted.com/webapi
scripts/publish-editor.sh https://ishaunted.com/webapi
```

Each writes an `artifacts/` folder and prints what it set. The values above are production facts
this repository does not know, and each fails quietly if wrong — a site that loads and then fails
every operation, rather than one that refuses to start.

## Copy them

| From | To |
|---|---|
| `artifacts/webapi/` contents | `C:\ishaunted\webapi\` |
| `artifacts/website/` contents | `C:\ishaunted\` |
| `artifacts/editor/wwwroot/` contents | `C:\ishaunted\editor\` |

Copy the **contents**, not the folder — `index.html` belongs at `C:\ishaunted\editor\index.html`,
not `C:\ishaunted\editor\wwwroot\index.html`.

Delete the target folder first rather than copying over it. Published filenames are fingerprinted,
so an old build's files do not get overwritten — they sit alongside the new ones.

## Why each one is an Application

The root `web.config` registers the ASP.NET Core handler at `path="*"`. **Every** request under the
site — including `/webapi/...` and `/editor/...` — is handed to the website process unless that
folder is its own Application. The website looks in its own `wwwroot`, finds nothing, and returns
its 404. The files are all present and correct; nothing serves them.

In IIS Manager, for **each** of `webapi` and `editor`:

1. Right-click the folder under the site → **Convert to Application**.
2. Application pool → **No Managed Code**.

"No Managed Code" is counter-intuitive and correct: .NET Core runs its own runtime, so the pool
must not load the old CLR. The editor needs no runtime at all — it is static files — but the same
setting is right for it.

The generated `web.config` files use `inheritInChildApplications="false"`, so the root's handler
does not leak down into the children.

## Why the settings are in appsettings.json, not appsettings.Production.json

An environment-specific settings file loads only when `ASPNETCORE_ENVIRONMENT` matches its name,
and a copy-deployed package has no say in what that variable says on the far end. The API's upload
root was in `appsettings.Production.json` and the server started with an environment that did not
load it — so it fell back to the empty string in the base file and refused to start with
"FileStorage:RootPath is not configured", for a setting that was sitting right there in the
package, correctly spelled, and simply never read.

The publish scripts now merge into `appsettings.json`, which loads whatever the environment says,
and ship no environment-specific file at all.

## Secrets

The SMTP password is **not** in any file in this repository and must not be. Set it on the server
as an environment variable on the API's application pool:

```
Smtp__Password
```

(Double underscore — that is how .NET maps an environment variable to the `Smtp:Password`
configuration key.)

## Check it, in this order

1. `https://ishaunted.com/webapi/api/...` — any endpoint. If this is wrong nothing else can work,
   so prove it first.
2. `https://ishaunted.com/` — the home page, with the map and public investigations.
3. **Sign in.** This is the only real test of `WebApi:BaseUrl`: the page renders whether or not the
   setting is right, and only an actual API call tells you.
4. `https://ishaunted.com/editor/` — the editor, not the website's 404 page. Sign in there too: the
   Server tab is what proves the editor's own API URL.

If the site returns 502.5 the app failed to start. Set `stdoutLogEnabled="true"` in that app's
`web.config`, reproduce, read `logs\stdout`, then set it back — it grows without bound.

## Related

- [deploy-editor.md](deploy-editor.md) — what the editor's publish sets, and why each part fails
  silently if wrong.
