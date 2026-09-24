# The website's errors had nowhere to go

Found on the storefront branch, 09/24/2026: a Blazor circuit the server closed with an error left no
record anywhere. The only clue was "Connection closed with an error" in the browser console.
Branch `fix/website-dev-error-logging`, from develop.

## Why

`Ben.Web.Website` logs through Serilog, and the only sink it commits is MSSqlServer. That sink's
connection string in `appsettings.json` is the placeholder `SET-BY-DEPLOY-OR-ENVIRONMENT`, which
the deploy replaces. `UseSerilog()` also replaces the framework's console logger. So:

- **From a worktree** (no gitignored `appsettings.Development.json`), the site did not even start.
  `autoCreateSqlTable` makes the sink open a connection while it is being constructed, the
  placeholder host does not resolve, and `ReadFrom.Configuration` threw out of `Main`.
- **From the main checkout**, Ben's local Development file points the sink at
  `IsHauntedDb_player`. Under `scripts/run-e2e.sh` that is a different database from the one being
  tested, and `web.log` held build output only, because nothing wrote to the console.

## What changed

- **`Ben.Web.Website/Program.cs`**
  - In Development, a Console sink at Warning and above, so `run-e2e`'s `web.log` captures errors.
    It is added in code, not config, because `Serilog:WriteTo` merges by array index (the API's
    W-S7 double-console bug). It is skipped if configuration already declares a Console sink.
  - In Development, if a sink still has the placeholder, its `autoCreateSqlTable` is switched off
    so the site starts, and one startup Warning says errors are going to the console only.
    Outside Development the startup throw stays: a deploy with no log connection should stop.
  - `CircuitOptions.DetailedErrors = true` in Development only, so the browser console shows the
    real exception. Only ever switched on, so a local `"DetailedErrors"` setting still applies.
- **`scripts/run-e2e.sh`**
  - The website's sink gets `Serilog__WriteTo__0__Args__connectionString` set to the e2e
    database, so website errors land in the run's own `Logs` table beside the API's
    (`Source = 'Website'`).
  - Host environment is now `export`ed inside the subshell instead of passed as `env` arguments,
    so the SQL password is never in a process's argv.
- **`Ben.Web.Tests/Website/SecurityHardeningTests.cs`**: `DetailedErrors = true` must sit directly
  under an `IsDevelopment()` check, and the committed settings files must not set it. Checked that
  the test fails when the check is removed.

Nothing here points at the default connection. `IsHauntedDb` is production.

## Verified

Using a temporary page (not committed) whose button throws inside the circuit, the website was
run from a worktree with `ASPNETCORE_ENVIRONMENT=Development` and output redirected, the way
run-e2e starts it:

1. With no log connection: the site started, the placeholder warning printed, and the click wrote
   `[ERR] Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost Unhandled exception in
   circuit …` with the full stack to the log. The browser console showed the
   `InvalidOperationException` itself instead of the generic message.
2. With the sink exported to `IsHauntedDb_e2e` (as run-e2e now does): the same error became row 19
   of `IsHauntedDb_e2e.Logs`, Level Error, Source Website, Application Ben.Web.Website. `ps` showed
   no process with the password in its arguments.
