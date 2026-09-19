# Ben.Canvas.Playwright

Browser tests for the canvas editor at desktop, iPad and iPhone sizes.

## Build

```powershell
dotnet build Messenger/Ben.Canvas.Playwright
```

## Browsers

This machine has no `pwsh`, so use Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Messenger/Ben.Canvas.Playwright/bin/Debug/net10.0/playwright.ps1 install chromium
```

`%LOCALAPPDATA%\ms-playwright` already holds Chromium; the install is a no-op when it matches. The
WebKit fixtures (iPhone and iPad engines) need `install webkit` as well.

## Run

```powershell
dotnet vstest Messenger/Ben.Canvas.Playwright/bin/Debug/net10.0/Ben.Canvas.Playwright.dll --TestCaseFilter:TestCategory=Shell
```

**Not `dotnet test` on this project.** It sets `IsTestProject=false` so the solution's `dotnet test`
skips it, and `dotnet test` on the project itself then prints nothing and exits 0 — which reads as a
pass.

Start the host first; with it stopped every test reports Ignored with the command to start it:

```powershell
dotnet run --project Messenger/Ben.Wasm.Canvas --urls http://localhost:5125
```

## Two things that made this suite lie on a Mac

Both were written on Windows and both failed silently — as a skip, or as a test that could never
pass — which reads as a pass either way (2026-09-17).

- **Keyboard shortcuts use `ControlOrMeta`, never `Control`.** The canvas is Blazor WebAssembly: it
  runs on the visitor's machine, so copy, cut, paste, select-all and bold are whatever that platform
  binds them to. `Control+c` on a Mac arms the editor's own handler and never triggers the browser's
  copy command, so the clipboard stays empty and the test fails on an assertion about the board. The
  app itself has always accepted either modifier (`e.ctrlKey || e.metaKey`); only the tests were
  one-sided.
- **Ask Playwright whether a browser is installed.** The WebKit fixtures used to look for a folder
  under `LocalApplicationData` with a backslash in the path, which is never there on a Mac, so all
  eighteen skipped with "WebKit not installed" however many times you installed it. They now try to
  launch it and skip only if that fails. Install it with the bundled driver rather than `pwsh`:

```bash
bin/Debug/net10.0/.playwright/node/darwin-arm64/node bin/Debug/net10.0/.playwright/package/cli.js install webkit
```

## Categories

Shell, Layout, Editing, Persistence, Touch, Paste, Server, Capture.

## Environment

| Variable | Default | Meaning |
|---|---|---|
| `BEN_CANVAS_URL` | `http://localhost:5125` | The canvas host |
| `BEN_API_URL` | `http://localhost:5252` | The Web API (server tests) |
| `BEN_USER_EMAIL` | `sarah.mitchell@benco.dev` | Seeded account for server tests |
| `BEN_USER_PASSWORD` | — | Its password; tests that need it are Ignored without it. Never written to a file. |
| `BEN_CANVAS_WALK_OUT` | test work folder | Where screenshot walks are written |
