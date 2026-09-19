# Testing the IsHaunted app — a guide for a C# developer

You know Visual Studio and `dotnet`; here is the same mental model in Apple
terms, then the exact steps.

## Rosetta stone

| You know | Apple equivalent | Notes |
|---|---|---|
| Visual Studio | **Xcode** | Already installed on this Mac |
| `.sln` / `.csproj` | `IsHaunted.xcodeproj` | Open this one file; it pulls in everything |
| Launch profile (F5 target) | **Scheme + destination** | Scheme = "IsHaunted"; destination = which device/simulator |
| F5 (run with debugger) | **⌘R** | Stop is ⌘. |
| `dotnet build` | `xcodebuild` (or ⌘B) | `scripts/build.sh` wraps it |
| `dotnet test` | `swift test` | `scripts/test.sh` wraps it; runs in seconds, no simulator |
| NuGet package | Swift Package | `BenKit/` is a local one, like a project reference |
| IIS Express / localhost run | **iOS Simulator** | A full fake iPhone/iPad on the Mac |
| `appsettings.json` | `Support/Info.plist` | App manifest (permissions, URL scheme, ATS) |
| Beta channel / ClickOnce | **TestFlight** | Needs the paid Apple Developer Program |

## One-time setup (do this first)

Run this once in Terminal (it needs your password, so I can't do it for you):

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

This points the command-line tools at the full Xcode install. Without it the
in-session simulator panel and input automation don't work (builds still do).

## Option A — test on this Mac in the Simulator (easiest, start here)

1. **Start the API** (the app's backend), exactly as you always do:
   ```bash
   cd ~/Source/Ben/Ben.Data.WebApi && dotnet run
   ```
   (If things look stale: `pkill -f "Ben.Data.WebApi"` first — the usual trap.)

2. **Open the project**: double-click `Ben.iOS/IsHaunted.xcodeproj` in Finder,
   or `open Ben.iOS/IsHaunted.xcodeproj` in Terminal. Xcode opens like VS.

3. **Pick a device**: in the toolbar at the top, next to the "IsHaunted"
   scheme name, there's a device dropdown. Choose **iPhone 17 Pro** or
   **iPad Pro 13-inch (M5)**.

4. **Press ⌘R** (or the ▶ button). Xcode builds, boots the simulator, and
   launches the app. First boot of a simulator takes a minute; after that
   it's fast.

5. In the app: **Profile tab → API environment** should say **Dev**
   (localhost:5252). Sign in with a seed account, e.g.
   `james.thornton@benco.dev` (ordinary member — the right seat for testing,
   per the repo's own rule). The password is the one you set as
   `SeedData:DevData:Password`; it is deliberately not written down here.

No Xcode? Terminal only:

```bash
cd ~/Source/Ben/Ben.iOS
./scripts/run-sim.sh                           # iPhone 17 Pro
./scripts/run-sim.sh "iPad Pro 13-inch (M5)"   # iPad
```

Simulator tips:
- It's a real iOS — Home button is ⌘⇧H, rotate is ⌘←/⌘→, dark mode toggles
  from Features menu → Toggle Appearance.
- `localhost` inside the simulator IS the Mac, so the Dev environment just
  works. No IP configuration needed.
- To reset a simulator to factory-clean: Device menu → Erase All Content and
  Settings.

## Option B — test on your real iPhone (USB cable)

Free with any Apple ID, with one limitation: the install expires after
**7 days** (re-run from Xcode to refresh). Steps:

1. In Xcode: **Xcode menu → Settings → Accounts → "+"** and sign in with your
   Apple ID. A "Personal Team" appears.
2. Click the blue **IsHaunted** project icon in the left sidebar → target
   **IsHaunted** → **Signing & Capabilities** tab → set **Team** to your
   Personal Team. Xcode auto-manages the rest.
3. Plug the iPhone into the Mac with a cable. Tap **Trust** on the phone.
4. On the phone: **Settings → Privacy & Security → Developer Mode → on**
   (appears after the first install attempt), then restart the phone.
5. Pick your iPhone in Xcode's device dropdown, press ⌘R.
6. First launch is blocked until you trust yourself as a developer:
   **Settings → General → VPN & Device Management → trust your Apple ID.**

**Networking on a real phone** — `localhost` is now the *phone*, not the Mac,
so the Dev preset won't reach the API. Two options:

- **Easiest**: in the app, Profile → API environment → **UAT**
  (https://ishaunted.com). Works anywhere, nothing to configure.
- **Against your local API**: phone and Mac on the same Wi-Fi; find the Mac's
  address (System Settings → Wi-Fi → Details, e.g. `192.168.1.50`); run the
  API bound to all interfaces:
  ```bash
  cd ~/Source/Ben/Ben.Data.WebApi && dotnet run --urls "http://0.0.0.0:5252"
  ```
  then in the app: Profile → API environment → **Custom** →
  `http://192.168.1.50:5252`. (macOS may ask to allow incoming connections
  for `dotnet` — allow it.)

## The paid Apple Developer Program ($99/year) — when you'll need it

- **TestFlight** (send betas to other people's phones — the proper beta channel)
- **App Store** distribution
- **Sign in with Apple** entitlement on real devices — required for the
  Apple-account login you asked for, so plan on enrolling before that slice.
  Enroll at developer.apple.com with your Apple ID.

## Running the unit tests

```bash
cd ~/Source/Ben/Ben.iOS && ./scripts/test.sh
```

~60 tests, a few seconds, no simulator. Like `dotnet test` on a class library.

## Web-first development: how the app keeps up with the website

You can develop the website freely — nothing in C# can break this app's build.
The only shared thing is the API contract, and the app is tolerant of the
common case (new fields/endpoints are ignored). When you're ready to bring the
apps up to date:

```bash
# with the dev API running:
cd ~/Source/Ben/Ben.iOS
curl -s http://localhost:5252/api/public/events > BenKit/Tests/BenKitTests/Fixtures/public-events.json
# ...re-capture any other fixtures the same way, then:
./scripts/test.sh
```

A failing fixture test = the contract moved; the failure names the model to
update. Green = the app is still in sync.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Sign-in says "Too many tries" | `/login` is rate-limited: 20/min | Wait for the countdown; it's the API, not the app |
| App can't reach Dev API | API not running, or stale host | `pkill -f "Ben.Data.WebApi"` then `dotnet run` |
| Everything fails on a real phone | Phone can't see the Mac's localhost | Use UAT, or the Custom LAN URL above |
| Sign-in doesn't survive relaunch | App built with signing disabled | Use ⌘R or `run-sim.sh` (only `build.sh` disables signing, and that's compile-check-only) |
| "Untrusted Developer" on phone | Personal team not trusted yet | Settings → General → VPN & Device Management |
| Simulator acting haunted (fitting) | State from old builds | Device → Erase All Content and Settings |
