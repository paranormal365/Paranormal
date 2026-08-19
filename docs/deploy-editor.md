# Deploying the video editor to IIS

The editor is a standalone WebAssembly app: static files, no server process of its own. It is
served from a sub-path of the main site — `https://ishaunted.com/editor/` — so that it inherits the
site's certificate. A subdomain would need its own.

## Build it

```bash
scripts/publish-editor.sh
```

Output lands in `artifacts/editor/wwwroot`. The script sets the two things that differ between a
development run and the deployed app, both of which fail quietly if wrong:

- **`<base href="/editor/">`** in `index.html`. Blazor resolves the runtime and every asset against
  it. Point it at `/` and the browser asks the site root for files that live under `/editor`, gets
  the API's 404 page, and the app sits on "Loading" with nothing in the log to say why.
- **`WebApiBaseUrl`** in `wwwroot/appsettings.json`, defaulting to `https://ishaunted.com`. Same
  origin as the sub-path, so no CORS. An empty value is a *working* configuration — a fully local
  editor with no Server tab — so a mistake here does not throw, it just removes the half of the
  product that talks to the site.

To publish against a different API origin, pass it: `scripts/publish-editor.sh https://example.com`.

## Copy it

Copy the **contents** of `artifacts/editor/wwwroot` into an `editor` folder at the site root, so
that `index.html` lands at `C:\ishaunted\editor\index.html`.

Include `web.config`. IIS serves nothing whose file extension it does not recognise, and a Blazor
app is almost entirely `.wasm` and `.dat` — without it the runtime 404s and the app never starts.

Nothing else on the server changes. No application pool, no .NET install, no registration: these
are static files, and the editor runs in the browser.

## Check it

Open `https://ishaunted.com/editor/`. You should get the dark editor with a full toolbar —
Initialize, Open, Preview, Export — and a Media & Properties panel on the right.

If it hangs on "Loading", open the browser's network tab and look for 404s under `/editor/_framework/`
(MIME types — `web.config` did not get copied) or at the site root (`<base href>` is wrong).

If it loads but looks unstyled, the scoped-CSS bundle did not arrive. Note that a 404 stylesheet
still gives the browser a stylesheet object with no rules, so nothing appears in the console — check
the network tab rather than the log.

## The sidecar

Optional, and separate: it installs on each editor's own machine, not on the server. The server's
only involvement is that `https://ishaunted.com` and `https://www.ishaunted.com` are in the
sidecar's allowed-origins list, so pairing from the deployed editor is accepted. A missing origin
is refused the same way a wrong pairing code is — a 403, reading to the user as "the code did not
work" while a healthy sidecar sits right there.
