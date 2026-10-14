# Debugging the IsHaunted app

Where to look when something on the phone is wrong. The [TESTING.md](TESTING.md) guide covers
building and running; this one picks up from "it runs, but…". Paths are relative to `Ben.iOS/`, and
`BenKit/…` means `BenKit/Sources/BenKit/…`.

**The app writes no log.** There is no `Logger` or `os_log` anywhere, so nothing turns up in
Console.app. What it does is watched in the debugger, at the few places below that everything
passes through.

## First: which server is it talking to?

Half of "it doesn't work" is the app talking to a different API than you think.

| Build | Talks to, unless told otherwise |
|---|---|
| Debug (⌘R, `scripts/run-sim.sh`) | `http://localhost:5252` — the dev API on this Mac |
| Release (an archive) | `https://api.ishaunted.com` — production |

- **Changing it:** in a Debug build, Profile → Settings → **Developer › API environment** offers
  both, or a custom address. Switching signs you out and empties the cache
  (`AppDependencies.switchEnvironment`). The section does not exist in a Release build.
- **For one launch only:** `-apiBaseURL http://localhost:5299` as a launch argument (Debug only).
  It is not saved, so the next launch goes back to the saved choice.
- **A Release build ignores a saved localhost or LAN address** and uses production
  (`BenKit/Config/APIEnvironment.swift`).
- **On a real phone,** `localhost` is the phone itself, not the Mac. Use production, or a custom
  address with the Mac's LAN IP.

## Watching a request

- **Every call goes through `Transport`** (`BenKit/Networking/Transport.swift`), including the
  token refresh. Put a breakpoint in `URLSessionTransport.send`, `upload` or `download`.
- **In the debugger:**
  - before the `await`: `po request.url`, and `po String(data: request.httpBody ?? Data(), encoding: .utf8)`;
  - after it: `po response.statusCode`, and `po String(data: data, encoding: .utf8)`.
- **No traffic at all:** check the environment (above). On the API side, the log is the dev API's
  own console.

## What the screen says, and what it means

Calls never throw: each returns a `LoadResult` (`BenKit/Networking/LoadResult.swift`).
`BenKit/Networking/ResponseMapping.swift` turns a status into words, and
`IsHaunted/DesignSystem/LoadStateView.swift` draws them.

| On screen | What happened | Look at |
|---|---|---|
| "The server couldn't be reached." | The request threw: no network, wrong environment, the API not running | Environment; is the API up? |
| "The server answered 500 (…)." | The API sent JSON or HTML instead of a sentence, usually an unhandled exception | The API's log, same minute |
| A plain sentence from the server | The API refused on purpose, and said why | The controller that wrote that sentence |
| "The server's answer couldn't be read." | 2xx, but the JSON did not match the model | The BenKit model, against the real response |
| "Your session ended" | A 401 | [Signed out](#signed-out-unexpectedly) |
| "Slow down a moment" | A 429 (sign-in is limited to 20 a minute) | Wait. It's the API, not the app |

**When the JSON doesn't match**, capture the real response into a test fixture (TESTING.md, "Web-first
development") and compare the two. An invented fixture once shipped a feature that could not decode
the real thing.

## Signed out unexpectedly

Every sign-out goes through `TokenSession.endSession()` (`BenKit/Auth/TokenSession.swift`). Break
there and read the stack: it is one of these.

- **The refresh token was refused.** The API answered `/refresh` with a 4xx other than 408 or 429.
- **A 401 on the token still held** (`handleUnauthorized`). A 401 for an older token, from a request
  that outlived a refresh, is ignored on purpose.
- **`api/me` refused** (`BenKit/Stores/SessionStore.swift`).
- **A deliberate Sign out, or an environment switch.**

No signal, a 5xx, a 429 and a 408 never sign anybody out. The tokens are kept, and the app keeps
showing the person it last knew (`me.json`) until it can check again, each time it comes to the
front.

**Where it all lives:**
- **Tokens:** one Keychain item, service `com.ishaunted.ios`, account `tokens`. **It survives
  deleting the app.** A "fresh install" on a Simulator stays signed in unless you erase it:
  Device → Erase All Content and Settings, or `xcrun simctl keychain booted reset`.
- **Who is signed in:** `Library/Application Support/Session/me.json`.
- **An unsigned build can't use the Keychain at all.** That is why `scripts/build.sh` (unsigned) is
  compile-only, and `run-sim.sh` stays signed.

## Field Kit sessions

**Where they are on disk**, in the app's data container:

```bash
xcrun simctl get_app_container booted com.ishaunted.ios data
```

Under that, in `Library/Application Support/`:

| Path | What |
|---|---|
| `FieldSessions/<session id>/readings.jsonl` | The readings, one JSON object per line, flushed to disk every 20 |
| `FieldSessions/<session id>/media/` | Recordings, photos and clips (`audio-001.m4a`, …) |
| `field-sessions.store` | The list of sessions, marks and captures (SwiftData) |

`tmp/exports/` holds `.ben` files made for sharing. A `.ben` is a zip: open it and read `data.json`.

**Common cases:**
- **"Interrupted."** The app was killed while a session was recording. At the next launch,
  `FieldSessionStore.recoverInterruptedSessions()` keeps every reading up to the last complete line
  and marks the session interrupted. Nothing recorded is lost, but it can't be started again.
- **"Sessions can't be stored."** `field-sessions.store` would not open, so the app is working from
  `tmp/` for this launch only. Copy the store off before you erase anything.
- **No sensors on a Simulator.** Launch with `-fieldKitFakeSensors` (Debug only) for scripted
  readings. That is what the UI tests use.
- **"Didn't finish sending."** An upload is one request carrying one `.ben`. On failure nothing
  retries on its own; Send again replaces that part of the session rather than adding a copy.
  Break in `FieldUploadClient.submitBundle`.
- **The device model reads `arm64`.** That's a Simulator: it reports the Mac, not a phone model.

## Links and notifications

To open a link as if somebody tapped it:

```bash
xcrun simctl openurl booted "ishaunted://events"
```

You can also launch with `-openLink <url>`, which skips the "Open in IsHaunted?" prompt, or run
`OPEN_LINK=… scripts/run-sim.sh`.

- **Shapes the app understands:** `BenKit/Config/DeepLinkParser.swift`. A link it doesn't
  recognize does nothing; breaking in `DeepLinkParser.parse` shows why.
- **Universal links (`https://ishaunted.com/…`) don't work on a Simulator.** They only verify on a
  real device that has reached the live site.
- **Push:** the device token is stored in UserDefaults as `push.deviceToken`. Debug builds register
  with Apple's sandbox, and Release with production, so a Debug build never gets a production
  notification. A failed registration is silently ignored (`IsHaunted/App/Push/PushRegistrar.swift`).

## Sharing to an event (the Share Extension)

The extension and the app share the App Group `group.com.ishaunted.ios`.

- **What the extension does:** it reads `shareable-events.json` and adds to
  `RoomOutbox/posts.json` and `RoomOutbox/files/`.
- **What the app does:** it sends that outbox when it comes to the front, after a sign-in, and when
  the network returns.
- **Debugging it:** run the **IsHauntedShare** scheme in Xcode and pick Photos as the app to run.
  Breakpoints in the extension then work.
- **If a shared post never arrives:** look in the outbox file first. Still there means the app
  hasn't sent it; empty means it went.

## Launch arguments

Set these in the scheme (Product → Scheme → Edit Scheme → Run → Arguments) as `-name value`, or
pass them to `simctl launch`.

| Argument | Effect | Debug only? |
|---|---|---|
| `-apiBaseURL <url>` | Use this API for this launch | Yes |
| `-autoSignIn "email:password"` | Sign in through the real flow at launch | Yes |
| `-fieldKitFakeSensors` | Scripted sensors for Field Kit | Yes |
| `-doorScanCode <code>` | Stand in for the camera at an event's door | Yes |
| `-openLink <url>` | Open a link at launch | **No.** It's also in Release, but it only does what tapping the same link would |

UI tests get credentials from `TEST_RUNNER_…` environment variables, because plain environment
variables never reach a UI test (`IsHauntedUITests/LocalAPI.swift`).

## Warnings

Run `scripts/warnings.sh` after a build or archive, and fix what it lists before the next archive.
It reads the newest Xcode build log and builds nothing. The one line it leaves out is "Metadata
extraction skipped, no AppIntents.framework dependency found": Xcode prints it for every app that
doesn't offer Shortcuts, and silencing it would mean adding a framework the app doesn't use.

## When it is the website, not the app

The app shows what the API sends, so a wrong count, a missing case or a refused action is usually
decided on the server. If the website shows the same thing to the same account, the answer is in the
API and its database, not here.
