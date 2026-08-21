# Deploying the video editor to IIS

The editor is a standalone WebAssembly app: static files, no server process of its own. It is
served from a sub-path of the main site — `https://ishaunted.com/editors/video/` — so that it
inherits the site's certificate. A subdomain would need its own.

The path is `/editors/video` and **not** `/video-editor`: the website already routes
`/video-editor` to its own in-app editor page, and an IIS Application at that path would shadow it
permanently. The `/editors/…` prefix also leaves room for the next editor.

## Build it

```powershell
.\scripts\deploy-ishaunted.ps1 -Apps editor
```

or, on macOS, `scripts/publish-editor.sh` — same job, same values. Output lands in
`artifacts/editor/wwwroot`. Either one sets the two things that differ between a development run
and the deployed app, both of which fail quietly if wrong:

- **`<base href="/editors/video/">`** in `index.html`. Blazor resolves the runtime and every asset
  against it. Point it at `/` and the browser asks the site root for files that live under the
  editor's folder, gets the website's 404 page, and the app sits on "Loading" with nothing in the
  log to say why. The router resolves routes against the base too, which is why the entry page
  (`@page "/"`) answers at `/editors/video/` with no redirect and no code change.
- **`WebApiBaseUrl`** in `wwwroot/appsettings.json`, `https://ishaunted.com/webapi`. Same origin as
  the sub-path, so no CORS; the `/webapi` suffix is part of the value, because the editor appends
  `/api/...` to it. An empty value is a *working* configuration — a fully local editor with no
  Server tab — so a mistake here does not throw, it just removes the half of the product that talks
  to the site.

To publish against a different API origin, pass it: `scripts/publish-editor.sh https://example.com/webapi`.

## Copy it

The deploy script does this. By hand: copy the **contents** of `artifacts/editor/wwwroot` into
`editors\video` at the site root, so that `index.html` lands at
`C:\ishaunted\editors\video\index.html`.

Include `web.config`. IIS serves nothing whose file extension it does not recognise, and a Blazor
app is almost entirely `.wasm` and `.dat` — without it the runtime 404s and the app never starts.

The folder does have to be an **IIS Application** (pool: No Managed Code), or the website's
handler at `path="*"` swallows every request to it and answers with its own 404. No .NET install
and no registration beyond that: these are static files, and the editor runs in the browser.
`scripts\setup-iis-ishaunted.ps1` creates it.

## Check it

Open `https://ishaunted.com/editors/video/`. You should get the dark editor with a full toolbar —
Initialize, Open, Preview, Export — and a Media & Properties panel on the right.

If it hangs on "Loading", open the browser's network tab and look for 404s under
`/editors/video/_framework/` (MIME types — `web.config` did not get copied) or at the site root
(`<base href>` is wrong).

If you get the *website's* 404 page instead of the editor, the folder was never converted to an
Application. That failure returns HTTP 200, so a status-code check will not catch it — the deploy
script looks for the `<base href>` in the body for exactly this reason.

If it loads but looks unstyled, the scoped-CSS bundle did not arrive. Note that a 404 stylesheet
still gives the browser a stylesheet object with no rules, so nothing appears in the console — check
the network tab rather than the log.

## The sidecar

Optional, and separate: it installs on each editor's own machine, not on the server. The server's
only involvement is that `https://ishaunted.com` and `https://www.ishaunted.com` are in the
sidecar's allowed-origins list, so pairing from the deployed editor is accepted. A missing origin
is refused the same way a wrong pairing code is — a 403, reading to the user as "the code did not
work" while a healthy sidecar sits right there. That list is host-based, so moving the editor's
path does not affect it.

Its installers are **not** served from the editor's folder. At ~160 MB and ~107 MB they would be
re-copied on every editor deploy, so they live outside the site in `C:\ishaunted-files`, published
as their own Application at `/files`, and the downloads page links them absolutely at
`/files/sidecar-video/<rid>/`. `scripts\deploy-ishaunted.ps1` stages them there from
`Ben.Video.Sidecar/installer/dist/` and writes a `checksums.txt` beside each — for unsigned builds
a published hash is the only integrity story a tester has. A missing installer is a warning, not a
failed deploy; the link 404s until it is built.

**Windows ships a `.zip`, macOS a `.dmg`**, and the staging step prefers the `.dmg` for `osx-*`
RIDs, falling back to a zip if that is all that has been built. The formats differ because the
install does: a Mac zip meant unzip it, open Terminal and run a script, which is three steps and a
terminal for something whose whole purpose is to sit in the background. The disk image is open it,
right-click the installer, Open.

Right-click, because these builds are unsigned. macOS quarantines anything downloaded and refuses
to launch unsigned quarantined items from Finder, so a double-click is refused with "unidentified
developer" — indistinguishable, to the person holding it, from a corrupt download. Opening via
right-click records consent and macOS allows it. The permanent fix is a Developer ID signature over
the bundle and the image plus notarization, which needs a paid Apple Developer account; until then
the downloads page explains the extra click rather than letting someone conclude it is broken.

Build them with `installer/macos/build.sh <rid>` followed by `installer/macos/build-dmg.sh <rid>`.
