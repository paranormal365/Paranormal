# Running the case canvas locally (Windows)

The canvas editor lives in `Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Messenger` until it
joins `Ben.slnx`; its server half is on the `feature/canvas-editor` branch in the
`Paranormal-canvas` worktree. Everything below runs against the disposable **`IsHauntedDb_e2e`**
database on the local SQL Server — never `IsHauntedDb` (production) and never the UAT database.

## 1. The API on 5252

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Z:\_GitHub\Paranormal365\Paranormal\Paranormal-canvas\scripts\run-webapi-e2e.ps1
```

It migrates `IsHauntedDb_e2e` with `dotnet ef database update --connection` (the only form `dotnet ef`
obeys), then runs the API on `http://127.0.0.1:5252` with the connection string, Serilog's own copy
of it and the uploads folder (`.uploads-IsHauntedDb_e2e`) all pointed at the e2e database.
`-SkipMigrate` skips the migration; `-ResetSeedPasswords` puts the seeded BenCo passwords back to
the ones in the Development settings for that one run.

Ready when `http://localhost:5252/api/public/build` answers 200. Swagger is at
`http://localhost:5252/swagger`.

It needs `Ben.Data.WebApi\appsettings.Development.json` in the worktree. That file is gitignored,
one per checkout, and holds:

- `ConnectionStrings:BenDbConnectionString` for `IsHauntedDb_e2e` (Integrated Security);
- `FileStorage:RootPath`;
- `Cors:AllowedOrigins`: `http://localhost:5078`, `http://localhost:5180`, `http://localhost:5125`
  (the canvas host; `AllowAnyHeader` covers `If-Match` and `Authorization`);
- `RateLimits:AuthPerMinute` raised for test runs;
- `SeedData`: the SuperAdmin, the BenCo organisation and its users, and `DevData` with its password.

The seeded passwords live only in that file. Read them from there; never commit or paste them.

## 2. The canvas host on 5125

```powershell
dotnet run --project Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Messenger\Ben.Wasm.Canvas
```

## 3. The website on 5078 (only for map tiles and checking published files)

```powershell
dotnet run --project Z:\_GitHub\Paranormal365\Paranormal\Paranormal-canvas\Ben.Web.Website --no-launch-profile --urls http://127.0.0.1:5078
```

Its gitignored `appsettings.Development.json` sets `WebApi:BaseUrl` to `http://127.0.0.1:5252` and
`Maps:AllowedTokenOrigins` to `["http://localhost:5125"]`, so
`http://localhost:5078/auth/mapkit-token?origin=http://localhost:5125` can mint a token for the
canvas. That needs MapKit signing configured (`Maps:TeamId`, `KeyId`, `PrivateKeyPath`); without it
the endpoint is 404 and the canvas shows its address card.

## 4. Turning the canvas on

`features.canvas-editor` defaults **off**, and while it is off every canvas and link-unfurl address
answers 404 to a signed-in caller. Either switch **Feature — Canvas editor** on in the website's
Site settings as the SuperAdmin, or, on the e2e database only:

```powershell
sqlcmd -S localhost -d IsHauntedDb_e2e -E -C -Q "DECLARE @o uniqueidentifier = (SELECT Id FROM AppUsers WHERE Email='haveben@msn.com'); IF EXISTS (SELECT 1 FROM SiteSettings WHERE [Key]='features.canvas-editor') UPDATE SiteSettings SET Value='true' WHERE [Key]='features.canvas-editor' ELSE INSERT SiteSettings (Id,[Key],Value,DateCreated,CreatedByAppUserId) VALUES (NEWID(),'features.canvas-editor','true',SYSUTCDATETIME(),@o)"
```

## 5. Signing in from a script

`POST http://localhost:5252/login` with `{ "email": "sarah.mitchell@benco.dev", "password": "<from the Development settings>" }`
returns `accessToken`; send it as `Authorization: Bearer <token>`. Sarah administers BenCo, so she
holds Cases Create — which link unfurl requires.
