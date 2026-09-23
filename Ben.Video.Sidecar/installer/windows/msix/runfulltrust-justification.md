# Why this package declares runFullTrust

Partner Center asks for a justification of every restricted capability. This is the answer for
`rescap:Capability Name="runFullTrust"` in `AppxManifest.template.xml`, written 2026-09-23.

Every claim below was checked against the code rather than recalled, because a justification that
overstates what the app does is worse than one that says too little - it is the document a reviewer
holds us to. The checks:

* child processes - `Jobs/FfmpegLocator.cs` resolves ffmpeg/ffprobe by absolute path under
  `AppContext.BaseDirectory`, **never** a PATH lookup, and hash-verifies both against the manifest
  packaged beside them. `Jobs/FfmpegRunner.cs` and `Jobs/FfmpegEncoders.cs` are the only
  `Process.Start` call sites in the project.
* loopback only - `Program.cs` calls `kestrel.Listen(IPAddress.Loopback, resolvedPort)`. There is
  no other `Listen`.
* storage - `Storage/SidecarPaths.cs` roots everything under `%LOCALAPPDATA%\BenVideo\sidecar`,
  and its own doc comment states the rule: never user-chosen directories, never a path derived
  from request input.
* no outbound traffic - the project contains no `HttpClient`, no `AddHttpClient` and no update
  check. `SidecarUpdateCheck` lives in `Ben.Video.Editor`, which is the web editor, not this app.

If any of those four change, this justification has to change with it.

---

## The answer to paste

IsHaunted.com SideCar is a classic Win32 desktop application - a self-contained .NET (ASP.NET Core)
process - packaged as MSIX. Its manifest entry point is Windows.FullTrustApplication, and
runFullTrust is the capability that declares that. It is not a UWP app and could not be written as
one, for two specific reasons.

First, it runs FFmpeg as a child process. That is the product's entire function: it orchestrates
FFmpeg to split, join, encode and inspect video on the user's own computer, so that editing work
does not have to happen inside a browser tab. A process inside an AppContainer cannot create
arbitrary child processes, so this is not possible without full trust. The only executables it ever
launches are the two binaries shipped inside this package, ffmpeg.exe and ffprobe.exe under
ffmpeg\win-x64\. It resolves them by absolute path inside its own install directory and never by a
PATH lookup, so nothing on the user's PATH can be substituted for them, and it verifies both
against SHA-256 hashes committed inside the package before running them. If a hash does not match,
it refuses to serve any job at all.

Second, it listens on a loopback TCP socket. Our web-based video editor at https://ishaunted.com
connects to it on 127.0.0.1 to hand it work and collect progress. AppContainer network isolation
blocks loopback connections by default, so a package without full trust could not be reached by the
browser at all, and the product would not function.

What the app deliberately does NOT do with that trust:

- It never requests elevation. It runs as the signed-in user, with that user's rights and no more.
- It does not read or write the user's files. All of its storage is under
  %LOCALAPPDATA%\BenVideo\sidecar. Media reaches it by upload from the editor over the loopback
  connection and is cached there; it never opens user-chosen directories, and no file path is ever
  derived from request input.
- It makes no outbound network connections. Kestrel is bound to 127.0.0.1 only, never to a routable
  interface, and the app contains no HTTP client, no update check and no telemetry.
- It starts no other processes, installs no service or driver, and changes no system settings or
  registry keys outside its own package state.
- Requests are refused unless they carry a pairing token generated on that machine, and repeated
  authentication failures are rate-limited.
- It does not run at startup. The declared windows.startupTask is Enabled="false", so only the user
  can turn it on, from Windows Settings > Apps > Startup.

---

## Shorter version, if the field is small

IsHaunted.com SideCar is a Win32 desktop app (self-contained .NET) packaged as MSIX, with
Windows.FullTrustApplication as its entry point; runFullTrust is what declares that. It needs full
trust for two things an AppContainer forbids: it launches FFmpeg as a child process - the two
binaries bundled in this package, resolved by absolute path and SHA-256 verified before each run,
never from PATH - and it listens on a 127.0.0.1 loopback socket so our web video editor can hand it
rendering work. It requests no elevation, reads and writes only under %LOCALAPPDATA%\BenVideo\
sidecar and never the user's own directories, makes no outbound network connections at all, starts
no other process, and does not run at startup unless the user enables it in Windows Settings.
