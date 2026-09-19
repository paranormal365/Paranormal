# Feature: every dev host binds IPv4 (item 187)

Branched from `develop` at `9369f25` (2026-08-31).

## The bug this is about

.NET on macOS crashes in its **IPv6 socket-accept path** (dotnet/runtime#102663): an unhandled
`ArgumentException` from `IPEndPoint.Create`, raised on a threadpool thread, below Kestrel and
below anything we wrote. Nothing of ours can catch it and the process dies.

It killed the API **nine times during one Playwright run** on 2026-08-25 and invalidated two runs
before the pattern was recognised. Its failures wear costumes: *"sign-in never left the login
page"*, *"no equipment categories in the taxonomy"*, timeouts, `ECONNREFUSED ::1:5252`. None of
them names the cause.

## What was already done, and why it had stopped working

The mitigation is to bind `127.0.0.1` rather than `localhost`, so Kestrel opens **no IPv6 listener
at all** and the faulting accept path is never reached.

Item 187 left the website and WASM host alone because neither *had been seen* to crash. That is an
argument about observation, not exposure — the API crashed because it takes the most connections,
not because it was built differently.

**Measuring first found something worse.** All three hosts were on both stacks, the API included:

```
Ben.Data.WebApi   127.0.0.1:5252   AND   [::1]:5252
Ben.Web.Website   127.0.0.1:5078   AND   [::1]:5078
Ben.Wasm.Video    127.0.0.1:5180   AND   [::1]:5180
```

The API was *supposed* to be IPv4-only. It was not, **because the mitigation lived only in the
shell scripts.** Every `applicationUrl` in every `launchSettings.json` still said `localhost`, so
an IDE run, a plain `dotnet run`, or a hand-typed `ASPNETCORE_URLS` restored the listener — which
is exactly how it came back here, from a host started by hand earlier the same day.

**A workaround that one ordinary command defeats is not a workaround.**

## What changed

| Where | Change |
|---|---|
| `launchSettings.json` ×3 | Every `applicationUrl` on 5252 / 5078 / 5180 (and IIS Express 50496) binds `127.0.0.1` |
| `run-e2e.sh` | Separates **BIND** (`127.0.0.1`) from **BROWSE** (`localhost`) |
| `start-website-with-api.sh` | Binds `127.0.0.1:5078`, still opens `localhost:5078` |
| Website `Services:BaseUrl` (gitignored) | `127.0.0.1:5252`, so server calls skip a doomed `::1` attempt |

**HTTPS profiles keep `localhost`** for their own port on purpose: the ASP.NET dev certificate is
issued for the *name* `localhost` and would fail validation against an address.

**The readiness probes deliberately ask for `localhost`.** That makes startup itself the proof that
the `::1` → `127.0.0.1` fallback works. If it ever stops, a run fails immediately with "never
became ready" rather than dying strangely somewhere in the middle.

## Why `localhost` is still what everything asks for

`:5078` is the redirect URI registered with **Entra** and an allow-listed **CORS origin** on the
API. Both are matched on the URL the *browser* used, not on what Kestrel bound. Binding does not
touch either.

## Verified, not assumed

- `lsof` shows **no `[::1]` listener on any of the three ports** after the change.
- All three still answer `localhost` with **200**.
- Playwright passes over the fallback — **including Blazor Server's SignalR websocket**, which is
  the part that actually rides the connection.

## Deliberately unchanged

- **UAT/production**: Windows/IIS, not macOS Kestrel-on-loopback. Unaffected, and must not copy the
  local `BaseUrl`.
- **`scripts/dev-api-supervisor.sh` stays.** A run with restarts in it is a run whose failures
  cannot be trusted: `grep -c restarting /tmp/ben-api-supervisor.log`.
- `DOTNET_SYSTEM_NET_DISABLEIPV6=1` still does **not** help — Kestrel opens both listeners for a
  `localhost` binding regardless. Recorded so nobody spends the time twice.
