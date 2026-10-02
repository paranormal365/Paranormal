# App Store update — IsHaunted 1.1.2 (build 11)

The magnetometer fix, the website's new look, and the fixes from a start-to-end walk of the app.
Ben, 2026-10-02: *"After downloading the update for the iOS app, the magnetometer quit tracking"*,
then *"can we style the iOS app similar to the site so they flow together seamlessly?"*

| | |
|---|---|
| Version | **1.1.2** (`MARKETING_VERSION`) |
| Build | **11** (`CURRENT_PROJECT_VERSION`) — moved from 10 so it is above anything already uploaded |
| Replaces | 1.1.1 (9), released 2026-10-01 |
| Server work first | **Yes** — deploy the website/API from master first. The app's "delete your own post" and "Mentions" need the new API; against the old one they answer "Couldn't delete it" and an ordinary feed. |
| Screenshots | **New** — the app looks different. Upload the set in `screenshots-1.1.2/`. |

## 1. What changed

**Magnetometer.** The magnetometer and the movement watcher each had their own `CMMotionManager`
and asked the phone's device motion for different reference frames; with Watch for Motion on (the
1.1.1 default) the magnetometer's field stopped updating. One `SharedDeviceMotion` now serves both
(`LiveSensors.swift`, `CameraSession.swift`).

**The look.** The site's Signal palette in light and dark (asset catalog), its gradient and outline
buttons, its page and card surfaces on every list and form, and its violet on every control.

**From the walk** (each one found on screen): sign in from any screen that needs it; Forgot your
password / Create an account on the sign-in sheet; the password rules stated up front; delete your own
post; "You were mentioned" opens the posts that name you; Investigations reachable on an iPhone, and a
case opened from one no longer throws you to the feed; permission switches ask iOS or offer Settings;
the microphone asked before Start; Profile shows your name; nights under way say so; your own
session pulled from the server no longer says "shared with you".

## 2. Check on a phone BEFORE archiving

The simulator has no magnetometer, so this can only be proved on a device:

1. Run the app from Xcode on your iPhone.
2. Field Kit → start a session with **Watch for Motion on** and the magnetic channel on.
3. Move a magnet, a phone speaker or a steel tool near the top of the phone: the gauge and the trace
   must move, and keep moving for a few minutes.
4. Turn on Video as well and repeat.
5. Stop, review the session: the magnetic trace is there for the whole time.

While it is on the phone, glance at it in light and in dark mode.

## 3. Ship it

1. Deploy the website and API from master (see section 1).
2. Xcode → destination **Any iOS Device (arm64)** → Product → **Archive** → Distribute App →
   App Store Connect → Upload.
3. App Store Connect → IsHaunted → **+ Version** → **1.1.2**.
4. **What's New in This Version**:

   > A new look that matches the website, in light and in dark.
   >
   > The magnetometer keeps reading through a whole session again.
   >
   > Sign in from any screen that needs it, delete your own posts, see the posts that mention you,
   > and switches that need a permission now ask for it — or take you to Settings if you said no.

5. Select build **11**, replace the screenshots with `screenshots-1.1.2/`, **Add for Review**.
