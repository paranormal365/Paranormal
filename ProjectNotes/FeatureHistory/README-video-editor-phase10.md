# Video editor phase 10 — deployment, shell and sidecar

Branch: `feature/video-editor-phase5-persistence` (phase 10 continues on it).
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 10.
Follows `README-video-editor-phase9.md`.

Three commits, `b76d6a1f` to the phase-notes commit. The first is Ben's mid-phase request for an
Intel Mac sidecar; the rest is the phase itself.

Nothing in this phase changes what the editor does. All of it is about what it depends on, what it
sends, and whether the things it says about itself are true.

## The ffmpeg core now ships with the app

Thirty megabytes of WebAssembly came from `cdn.jsdelivr.net` at every load: undocumented, third
party, with a three-attempt retry loop around it because it failed often enough to need one. An
editor whose entire pitch is that your footage never leaves your machine could not start if
somebody else's CDN was having a bad morning.

Both cores are vendored under `Ben.Video.Editor/wwwroot/js/ffmpeg-core/{st,mt}/`. **That is 62 MB
in a public repository, permanently** — git keeps blobs forever — and Ben chose it with the number
in front of him. What it buys: the editor starts from its own origin, offline included, and the
retry loop is gone along with the reason for it.

Both archives were checked against the CDN's own bytes and both `.wasm` files start with the
WebAssembly preamble, which a test now re-checks on every run.

## The headers, two of which are load-bearing

The app shipped none. `TokenStore`'s own doc comment cited a Content-Security-Policy that did not
exist as the reason a bearer token in memory was safe enough.

| Header | Why |
|---|---|
| `X-Frame-Options`, `frame-ancestors 'none'` | The attack this app has is being framed invisibly while it holds somebody's footage |
| `nosniff`, `Referrer-Policy` | Ordinary hardening |
| `Cross-Origin-Opener-Policy`, `Cross-Origin-Embedder-Policy` | These decide whether the browser hands out `SharedArrayBuffer` |

The last two are not hardening. Without them `crossOriginIsolated` is false, the multi-thread core
can never be selected, and the editor has always rendered on one core in production while carrying
the code for the other. `require-corp` has a real cost — any cross-origin resource must opt in —
which vendoring the core is what made affordable.

`index.html` and `appsettings.json` are no longer cached, because a stale copy of either points a
returning visitor at a deployment that no longer exists.

## The deploy can now tell whether it landed

The editor had no build identity, so its only smoke check was that `index.html` carried the right
`<base href>` — which the *previous* deploy's `index.html` answers just as happily. It writes a
stamp and demands it back, and asks for the vendored core directly, because that file's absence
leaves an editor that loads, looks right and cannot start.

The deploy also rewrites the downloads page to name whichever installer format it staged. The page
linked one fixed filename per platform while the script accepted either of two, so it could offer a
404 to somebody who came to the site specifically to download it.

## Sidecar, tokens and honesty

- The panel said **"Download and run it"** and gave nobody anything to click, while the downloads
  page sat one level below the editor with nothing linking to it. `SidecarDownloadUrl` is a host
  setting because the two hosts reach it differently.
- The pairing instructions told everyone that `pair.sh` reopens the pairing page. That is macOS
  wording, shown to Windows users, where no such file exists.
- The **bearer handler** sent tokens it already knew were expired, collected a 401 and refreshed
  afterwards — two round trips where one would do. It also offered to retry any body by buffering a
  second copy, which its own comment claimed it would not do for large uploads and nothing enforced.
- Three claims were untrue: help promised hardware acceleration that exists nowhere in this
  codebase, the export dialog said rendering happens **entirely** in the browser which stopped
  being true the moment a sidecar could be paired, and `publish-editor.sh` announced a SPA rewrite
  that `web.config` explains at length why it deliberately omits.

`EditorDeploymentGuardTests` pins all of it: the cores exist and are real WebAssembly, the loader
reaches no CDN, each header is present, both hosts set the download URL, every file and platform
the downloads page links is one the deploy can stage, and neither the help nor the export dialog
has drifted back to the old claims.

## Ben's mid-phase request: an Intel Mac sidecar

The ffmpeg manifest had `osx-x64` as a placeholder with zeroed hashes, so `fetch-ffmpeg.sh` failed
on it and the installer refused for want of a bundled binary. It is pinned to the same source
`osx-arm64` uses, both archives verified against the source's published hashes before extraction,
both binaries confirmed Mach-O x86_64.

`BenVideoSidecar-osx-x64.dmg` is 122 MB, built and verified here: x86_64 throughout, including the
bundled ffmpeg and ffprobe. It was committed at Ben's instruction and then taken back out, because
GitHub hard-rejects any file over 100 MiB and the push was refused. `.gitignore` records both
facts. The deploy stages `osx-x64` alongside the other two and the downloads page has an Intel
card, so a staged image is reachable rather than sitting on disk with nothing pointing at it —
which means the deploy runs from a machine holding the built images, and both Mac ones are on Ben's
Desktop under `ishaunted-files/sidecar-video/<rid>/` in the server's own layout, with checksums.

## Verified on screen

Standalone host, storage cleared.

- The engine loads from `/_content/Ben.Video.Editor/js/ffmpeg-core/st/`, exactly two fetches, no
  CDN, and the status chip reaches **Ready**.
- A doubled slash in the core URL was caught here and fixed; it worked on this server and is a
  different path to some others.
- The sidecar panel shows the corrected wording: all of your CPU's cores *and outside the browser's
  memory limits*, and pairing instructions that name no shell script.

## Not verified, and why

- **The Disconnected state's download link.** It renders only when no sidecar is found, and Ben's
  is installed and paired on this machine. Reaching that state means resetting his pairing, which I
  will not do. The link is covered by a source scan and by inspection instead.
- **The cross-origin headers.** They live in `web.config`, which is IIS. The local dev server sends
  nothing, so `crossOriginIsolated` is false here and the multi-thread core cannot be exercised
  locally — the first real test is the next deploy, where the smoke check will at least prove the
  file arrived.
- **The deploy script itself.** There is no PowerShell on this machine. The changes are structural
  and the file's braces and parentheses balance, but it has not been run.
