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
