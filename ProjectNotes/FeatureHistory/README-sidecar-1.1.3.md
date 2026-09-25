# Sidecar 1.1.3 — no console window, a pairing window, WebM lengths

Branch `sidecar-1.1.3`, from master at c62be0f1 (09/25/2026).

## Why

The Microsoft Store 1.1.2 package was installed and tested on Windows on 09/25/2026. Two problems
came out of that:

1. **A console window.** Every launch (the Store's Start-menu entry, Win+R `benvideo-sidecar:`, and
   the editor's Turn on) opened a black console window, and closing it stopped the sidecar. The
   project had no `OutputType`, so it built as a console program.
2. **WebM clips read as 0 seconds.** WebM streams carry no `duration` field, only the container
   does. `FfprobeOutputParser` never read `format.duration`, even though `ProbeEndpoints` already
   passed `-show_format`. This happened on every platform, not only the Store build, and browser
   recordings are WebM.

Ben asked for: "Instead of popping up a console, on the first launch, can we pop up a simple window
with the code and a button to copy it so they can paste it in on the web app? Then, from that point
on, they manage start and stop from app without it popping up any console... just run as service or
background app."

## What changed

- **Two builds.** `Ben.Video.Sidecar.csproj` targets `net10.0` (macOS, and the tests) and
  `net10.0-windows`. The Windows target is `WinExe` with Windows Forms, so it has no console. Every
  publish script now passes `-f`: `installer/macos/build.sh` uses `net10.0`, while
  `installer/windows/build.sh` and `build-msix.ps1` use `net10.0-windows`.
  `EnableWindowsTargeting` lets the Mac build the Windows target too. The Windows exe is about
  40 MB larger, because it carries the Windows Desktop runtime.
- **The pairing window** (`Desktop/PairingWindow.cs`, Windows only):
  - It shows a 6-digit code, **Copy code** and **Close**, and "Closing this window leaves the
    SideCar running."
  - When the code is exchanged it changes to "Paired", saying the sidecar now runs in the
    background with no window.
  - It never replaces its own code. If the code expires, or `/pair` mints a newer one, it says so
    and offers **Show a new code**.
- **When it shows** (`Lifetime/PairingWindowPolicy.cs`, `Desktop/SidecarDesktop.cs`):
  - At start, while no browser has paired with this install yet. The marker is
    `config/awaiting-first-pairing`, written when a token is made and deleted by the first
    successful exchange. Installs from before 1.1.3 have no marker, so they read as paired.
  - When somebody starts it again from the Start menu while it is already running. That second copy
    signals the first to show its window, then ends.
  - Never for a paired install started by the editor's Turn on (`benvideo-sidecar:start`) or at
    sign-in.
- **One copy per sign-in** on Windows, using a named mutex. A second start used to serve a second
  sidecar on 43118.
- **Installers.** `post-install.ps1` and `install.ps1` no longer pass `-WindowStyle Hidden`, because
  Windows would apply it to the pairing window. They also no longer open `/pair`, because loading it
  would retire the window's code.
- **WebM.** Duration precedence is now video stream, then audio stream, then `format.duration`.
- **Wording.** The following now describe the window:
  - the editor's pairing panel;
  - the help steps in `using-the-video-editor.md`;
  - the downloads page;
  - the Store listing copy.
- **Version 1.1.3** in the csproj and `SidecarRelease.Version`.

## Proof so far

- `PairingWindowPolicyTests` (10 tests) and two new `FfprobeOutputParserTests`. The WebM test fails
  on the old parser. Removing the marker write, the marker delete or the `Paired` event each fails
  the policy tests.
- The sidecar, video and wasm suites pass: 255, 2649 and 34 tests.
- A Windows publish from the Mac produces a PE marked GUI (subsystem 2); the console build is 3.
- The macOS publish runs and reports `appVersion` 1.1.3.0.

## Still needs a Windows machine

The window itself can only be seen on Windows. Before submitting, check that:

- a fresh install's first launch shows the window;
- Copy code puts the code on the clipboard;
- pasting it into the editor turns the window to "Paired";
- Turn on and Off after that show nothing;
- a Start-menu launch while it is running brings the window up.

## Release order

The notice in `SidecarRelease` applies: build and upload before the site advertises 1.1.3.

1. Build the MSIX on Windows with `build-msix.ps1`, submit it to the Store as a new submission, and
   update the listing text from `store-listing.md`.
2. Rebuild the hosted installers: the `.exe` on Windows, and both dmgs on the Mac, for the WebM fix.
3. Then merge and deploy the site.
