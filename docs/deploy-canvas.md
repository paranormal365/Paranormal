# Deploying the canvas editor to IIS

The case canvas is a standalone WebAssembly app, like the video editor: static files, no server
process of its own. It is served from a sub-path of the main site —
`https://ishaunted.com/editors/canvas/` — so it shares the site's certificate and, just as
importantly, the site's **origin**: MapKit tokens are minted for one origin and Apple refuses them
anywhere else, and the canvas's sign-in, save and publish calls go to `/webapi` with no CORS.

Until the canvas project joins `Ben.slnx` (plan M8) it lives outside this repository, in
`Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Messenger\Ben.Wasm.Canvas`, so every deploy
names it with `-CanvasProjectPath`.

## Build it

Stage first. `-StageOnly` publishes and patches into `artifacts\canvas\` and touches nothing on the
server, so it needs no elevation:

```powershell
.\scripts\deploy-ishaunted.ps1 -Apps canvas -StageOnly -CanvasProjectPath 'Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Messenger\Ben.Wasm.Canvas'
```

The script refuses outright when there is no `Ben.Wasm.Canvas.csproj` at that path, rather than
publishing the wrong thing. It then sets the values that differ between a development run and the
deployed app, each of which fails quietly if wrong:

- **`<base href="/editors/canvas/">`** in `index.html`. Blazor resolves its runtime and every
  asset against it; pointed at `/`, the app asks the site root for its own files, gets the
  website's 404 page, and sits on "Loading". The script insists on exactly one `<base href>`.
- **`Canvas:WebApiBaseUrl`** = `https://ishaunted.com/webapi` in `wwwroot\appsettings.json`.
  The `/webapi` suffix is part of the value.
- **`Canvas:SiteBaseUrl`** = `https://ishaunted.com`.
- **`Canvas:MapTokenUrl`** = `https://ishaunted.com/auth/mapkit-token`. Same origin as the canvas,
  the only origin Apple honours for the token. (The `?origin=` override the endpoint accepts is
  for development only; `Maps:AllowedTokenOrigins` is empty in production.)

It also deletes `appsettings.Development.json` (which points at localhost), deletes the `.br` and
`.gz` twins of every patched file and proves they are gone (they hold the pre-patch bytes), checks
`web.config` is present, refuses a `web.config` that asks for cross-origin isolation, and writes
`build-info.json` with a fresh stamp and the canvas repository's commit.

## Copy it

The deploy copies the contents of `artifacts\canvas\wwwroot` into `C:\ishaunted\editors\canvas`.

That folder must be an **IIS Application** on the `IsHaunted.com-static` pool (No Managed Code),
or the website's handler at `path="*"` answers every canvas request with its own 404 — which
returns 200-shaped HTML, which is why the smoke check reads the body. `scripts\setup-iis-ishaunted.ps1`
creates it; run it once (elevated) before the first canvas deploy. It is idempotent.

## What the app ships with, and what it sends

`web.config` sets the headers a static app can set and mean:

| Header | Why |
|---|---|
| `X-Frame-Options: DENY`, `Content-Security-Policy: frame-ancestors 'none'` plus a script policy | The board holds case evidence and witness names; nothing may frame it, and no script runs that the app did not ship |
| `X-Content-Type-Options: nosniff`, `Referrer-Policy` | Ordinary hardening |

There is deliberately no `Cross-Origin-Opener-Policy` or `Cross-Origin-Embedder-Policy` header, and
the deploy refuses one: cross-origin isolation exists only to hand ffmpeg a `SharedArrayBuffer`,
which the canvas never needs, and `require-corp` would block Apple's map tiles.

What the canvas sends, all to its own origin:

- load, save and publish: `/webapi/api/canvas-documents` (save sends `If-Match`, a stale save is 409);
- pasted links: `/webapi/api/link-unfurl` and pictures through `/webapi/api/link-unfurl/image`;
- the map token: `/auth/mapkit-token`.

## Before you deploy the API

The API half needs the **`AddCanvasEditor`** migration applied to the production database first.
The deploy script never migrates. Check what is pending:

```powershell
dotnet ef migrations list --project Ben.Data.Source --startup-project Ben.Data.WebApi --connection "data source=localhost;initial catalog=IsHauntedDb;integrated security=True;persist security info=False;encrypt=True;trustservercertificate=True;"
```

Exactly one migration should say `(Pending)`: `AddCanvasEditor`. Anything else pending means the
branch is not what production expects — stop. Always pass `--connection`: `dotnet ef` ignores the
`ConnectionStrings__BenDbConnectionString` environment variable and silently uses the default.

Apply it with the same command, `database update` in place of `migrations list`.

A missing table does not stop the API starting, and the anonymous smoke probe still answers 401,
so the deploy looks fine. The tell is the API log line `DATABASE IS BEHIND`, and 500s on the canvas
addresses once somebody signs in.

Only production is migrated from this machine. The UAT database belongs to the Mac session and is
left alone.

## The switch

After deploying, **Feature — Canvas editor** is **off**: the API answers 404 on every canvas and
link-unfurl address for a signed-in person (an anonymous caller still gets 401, because sign-in is
checked first). A SuperAdmin turns it on in Site settings, in the Features section.

The flag also gates the link-unfurl fetcher — the one endpoint that makes this server fetch a page
somebody pasted — so it is off until somebody decides otherwise.

## Check it

The deploy's smoke section should show OK for:

- `canvas editor` — `https://ishaunted.com/editors/canvas/` contains `<base href="/editors/canvas/"`;
- `canvas build identity` — `build-info.json` holds the stamp this run just wrote;
- `canvas module loader` — `_content/Ben.Canvas.Editor/js/moduleLoader.js` answers;
- `canvas API` — `/webapi/api/canvas-documents` answers 401 (only when `webapi` was deployed too).

Then by hand: open `https://ishaunted.com/editors/canvas/`, it is dark with the site look; sign in;
with the flag on, open a case board and **Save to case**; paste an https link and see a title.

A signed-in probe while the flag is still off must answer 404:
`GET https://ishaunted.com/webapi/api/link-unfurl?url=https://example.com` with a bearer token.

## Rollout

The assistant first runs, unelevated, the `migrations list` above and shows that exactly
`AddCanvasEditor` is pending, and that `feature/canvas-editor` contains everything production is
running. Then Ben runs these, one at a time, in an **elevated** Windows PowerShell, waiting for the
prompt before the next:

1. `Set-Location Z:\_GitHub\Paranormal365\Paranormal\Paranormal-canvas`
2. `dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi --connection "data source=localhost;initial catalog=IsHauntedDb;integrated security=True;persist security info=False;encrypt=True;trustservercertificate=True;"`
3. `.\scripts\setup-iis-ishaunted.ps1`
4. `.\scripts\deploy-ishaunted.ps1 -Apps webapi,canvas,website -CanvasProjectPath 'Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Messenger\Ben.Wasm.Canvas'`
   — the website is included because `Ben.Web.Services` changed (the canvas flag and
   `StandaloneCanvasAddress`).
5. In the browser: sign in at `https://ishaunted.com/`, open Site settings, turn on
   **Feature — Canvas editor**, then open `https://ishaunted.com/editors/canvas/` and sign in.

After each step the assistant reads the output: step 2 ends `Done.` with `AddCanvasEditor`
applied; step 3's summary lists `/editors/canvas`; step 4 shows the four canvas smoke lines OK and
`API is running <sha>` matching the worktree's HEAD. On any FAIL, stop and diagnose before running
anything else.
