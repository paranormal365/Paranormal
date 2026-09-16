# App Store submission — IsHaunted 1.0.3 (build 6)

Everything needed to finish submitting **1.0.3**, and every answer App Review has asked for so far.
This supersedes `APP-STORE-1.0.2.md` for the submission itself; that file stays as the record of
1.0.2 and its two rejections, and `APP-STORE.md` as the record of 1.0.0 and the listing copy.

**State on 2026-09-16, evening: SUBMITTED.** Ben created version 1.0.3 in App Store Connect,
filled the page, selected build 1.0.3 (6) and submitted it for review. It is now *Waiting for
Review*. While it waits: production stays at `12e55322` or later, the feed flag stays on,
apple@apple.com stays working, rostered and booked on a hosted event, and the old per-file upload
endpoints stay until 1.0.3 is live on the store (§1). The verdict lands in App Store Connect →
1.0.3 → App Review; §7 says what to do with either answer.

| | |
|---|---|
| Item | iOS App **1.0.3**, build **1.0.3 (6)** |
| Type | App Version — a new version, not a build added to 1.0.2 |
| Archived | 2026-09-16, from `master` at `12e55322`, clean working tree |
| Uploaded | 2026-09-16 6:33 PM, `xcodebuild -exportArchive` with the plist in §9 — *"Uploading IsHaunted.ipa is complete"* |
| Server | Production at `12e55322` since 2026-09-16 6:24 PM — the same commit the app was built from |

**On the number.** The version is **1.0.3**. The **6** beside it is the build number, and every
upload has one: it is how App Store Connect tells two uploads of the same version apart, and it
only ever goes up (2 and 3 for 1.0.0, 4 and 5 for 1.0.2, so 6 now). It is not a version and the
listing never shows it; people see 1.0.3. The reason it is 6 and not 1 is in the note at the top
of `APP-STORE-1.0.2.md`: a number that only goes up cannot collide with anything uploaded before.

Bundle id `com.ishaunted.ios` · `MARKETING_VERSION` **1.0.3** · `CURRENT_PROJECT_VERSION` **6** ·
iPhone and iPad · iOS 18.0 minimum. Proven in the archive itself:
`CFBundleShortVersionString` 1.0.3, `CFBundleVersion` 6, and the Share extension
`com.ishaunted.ios.share` at the same 1.0.3 (6) — App Store Connect refuses an extension whose
numbers differ from the app's.

---

## 1. Why this is 1.0.3 and not 1.0.2 (6)

1.0.2 build 5 was approved and is the version on people's phones. Ben, 2026-09-16: *"instead of it
becoming 1.0.2(6) bump it up to 1.0.3."* What landed since is not a fix to 1.0.2 — the Field Kit
takes its own photographs and clips, a session is one sealed file that opens on another phone,
hosted events are in the app with their passes and their door — so it is a version.

**The server had to go first, and it has.** 1.0.3 sends a session as one `.ben` whose ZIP carries
a `seal.json`; the API that was live until this evening (`43df7b76`, 2026-09-15) refused any bundle
with a `.json` inside it, so a reviewer's upload would have failed with *"cannot carry a .json
file"* and that would have been a 2.1 rejection. The fix (`945084c0`) and today's other server
work reached production at 6:24 PM; `GET /webapi/api/public/build` answers `12e55322`, the
association file still returns 200 as `application/json`, and the field-session routes answer 401
to an anonymous call, which means their tables migrated. Do not roll production back behind
`945084c0` while 1.0.3 is in review or live.

The old per-file upload endpoints stay until 1.0.3 is live (Ben, 2026-09-16: *"keep end points for
now until we upload the next version of the app"*), because approved 1.0.2 still uses them.

---

## 2. What changed since the reviewed build

Everything committed to `Ben.iOS` after `0734f6c7` (2026-09-12, 7:29 AM — the build-5 archive) is
new to review: 21 commits. All additive; nothing a reviewer exercised on 1.0.2 behaves differently.
The user-facing record is `Ben.Web.Services/Changelog/Content/apps.md`, entries 2026-09-12 (the
two items after the merge), 2026-09-13 and 2026-09-16.

- **The camera is the app's own.** Photographs and clips are taken inside the session. In 1.0.2 a
  photo handed the microphone to Apple's camera, which stopped the recording and left a hole in
  the sound; now the sound carries straight through, and a clip's own audio fills the gap while it
  runs. One camera button; the Start screen says why it cannot when it cannot.
- **A session is one `.ben` file, and it opens too.** Exporting writes one sealed file and hands
  it to the share sheet. A `.ben` somebody sends — AirDrop, Mail, Messages, Files — opens in the
  app and plays exactly as it did for them: the same trace, map, marks and recordings, with a line
  on the review screen saying where it came from. *Open a .ben file* on the Field Kit screen picks
  one from Files; *On the server, not on this phone* lists the sessions sent from another device or
  cleared from this one, and *Download* brings a whole night back. A file that lost or changed bytes
  on the way is refused rather than opened with a hole in it; the same session is not imported
  twice. This is the `CFBundleDocumentTypes` / `UTExportedTypeDeclarations` entry in
  `Support/Info.plist` — the app declares the `com.ishaunted.field-session` type, extension `.ben`,
  conforming to `public.data`.
- **The map arrow knows which way somebody walked**, and the review chart says when no field base
  level was set even when a sound base was.
- **Hosted events** (item 235, 2026-09-13). *What I'm going to*, under Profile, lists every hosted
  event the person has asked for, with dates, venue and pass. Each event has its own screen —
  pass, programme, menus, downloads and the event's room, showing only the parts that event has.
  The pass shows its code, short code, party size, nights and table, turns the screen to full
  brightness, and opens with no signal. Programme sign-up for as many of the party as are coming,
  a waiting list when a session is full. **Organizers and their helpers run the door from the
  app**: scanning a pass with the camera (VisionKit's data scanner — the second use of the camera
  permission) finds its reservation, tapping checks the party in; it works with no signal once
  tonight's list has been opened. The event's room: posts and photos, kept on the phone with no
  signal and sent when it returns.
- **A Share extension** (`IsHauntedShare`, `com.ishaunted.ios.share`). Photos and videos can be
  shared to one of the person's events straight from the Photos app, with a caption, the photo
  notice and the choice to send the organizers a copy. It shares the app group
  `group.com.ishaunted.ios` with the app and nothing else; it is the one new target since 1.0.2.
- **Local reminders for a seat** (`SeatReminders`). The app asks for notification permission only
  when somebody turns a reminder on for an event they are going to; the notifications are
  scheduled on the device. There is no push entitlement and no server involvement.
- **The Field Kit asks what to record before the session opens** — field, sound, video, location,
  each with what it costs in battery — and before sending says how much a session weighs, offering
  a smaller video size where a window is too heavy (2026-09-12, after the build-5 archive).
- **Fixes from the 2026-09-16 review pass.** Signing in on a phone that still held an expired
  session no longer ends the new one a moment later; a busy or rate-limited server no longer signs
  you out; a long replay no longer reworks every reading on every tick; a phone propped against a
  wall no longer marks "moved" on every sample; a location-only session still writes its readings;
  dictation recovers after a failed start; property, room and case photos are decoded at the size
  shown so a session with many photos no longer runs the phone out of memory; the pass turns the
  brightness back down when the app is put away; a session over 4 GB says so before exporting.

**What's New (paste into the version's "What's New in This Version"):**

> Take photographs and clips without leaving the session — the sound carries straight through.
> A session is now one .ben file: share it by AirDrop, Mail or Files and it opens on the other
> phone exactly as it played on yours, or pull a night back down from the server. Hosted events
> are in the app: your pass, the programme, the menus, the event's room, and — for organizers — a
> door that scans passes and checks people in, with or without a signal. Share photos to an event
> straight from Photos. The map arrow now shows which way you walked. And a review pass fixed the
> sign-out that could follow a fresh sign-in, smoother long replays, and photos that no longer
> fill the phone's memory.

---

## 3. Listing copy, App Privacy, URLs

Listing copy is unchanged from 1.0.0 — `APP-STORE.md` §1 and §2. It pre-fills from 1.0.2; check it
rather than retyping. Re-verified against **this** tree on 2026-09-16:

- **No third-party SDKs.** Every `import` in `IsHaunted/` and `BenKit/` is an Apple framework or
  BenKit itself. New since the 1.0.2 list: `CoreImage.CIFilterBuiltins` (the pass code), `ImageIO`
  (photo downsampling), `Network`, `QuickLook`, `SafariServices` (the event's own page, opened
  in-app to ask for a place), `UniformTypeIdentifiers` (the `.ben` type), `UserNotifications` (seat
  reminders), `VisionKit` (the door scanner). All Apple's. The Xcode project has **zero** remote
  package references and `BenKit/Package.swift` has no dependencies.
- **App Privacy answers** still match `IsHaunted/PrivacyInfo.xcprivacy`, which has not changed since
  build 5: seven data types, all linked, none for tracking, `NSPrivacyTracking` false.
- **Usage strings** in `Support/Info.plist`, eight, unchanged in wording: Calendars, Calendars
  (write-only), Camera, Location When In Use, Microphone, Motion, Photo Library (add), Speech
  Recognition. The camera string now covers two things — session photos and clips, and the door
  scanner. `ITSAppUsesNonExemptEncryption` is `false`. `UIBackgroundModes` carries `audio`, as it
  did in 1.0.2, so a session keeps recording with the screen off.
- **Entitlements** in `Support/IsHaunted.entitlements`: Sign in with Apple, associated domains
  (`ishaunted.com`, `www.ishaunted.com`), and now the app group `group.com.ishaunted.ios`. The
  Share extension's `Support/IsHauntedShare.entitlements` carries the app group only.

---

## 4. Review notes — paste into App Review Information → Notes

The 1.0.2 notes with the 1.0.3 additions. Reviewers read the whole thing; keep the order.

> IsHaunted is a field tool for paranormal investigation groups. Most of the app works without an
> account: the feed, public events and the entire Field Kit are usable signed out.
>
> **Field Kit** is the core feature and needs no account: Field Kit tab → Start a session. It asks
> for microphone, motion, location and camera permission; all are optional and a session runs
> without them, with fewer channels. Photographs and clips are taken inside the session. On the
> Send screen the in/out trimmer chooses what is uploaded; the original recording stays on the
> device. A finished session can be exported as one `.ben` file (Review → Export) and that file
> opens in the app on any phone — Field Kit → *Open a .ben file*, or tap it in Files, Mail or
> AirDrop.
>
> **Demo account** for the group-side features: apple@apple.com — password in the sign-in fields
> of this form. A member of Paranormal365, rostered on an investigation, with cases to open, and
> with a hosted event under Profile → What I'm going to.
>
> **The Share extension** ("IsHaunted" in the Photos share sheet) sends a photo or video to one
> of the signed-in person's events. It needs the demo account signed in and an event to send to.
>
> **Answers to the questions from the 1.0 review (Guideline 2.1):**
>
> 1. **Device used for testing:** iPhone 15 Pro Max on iOS 26.6.1 (Ben's physical device);
>    iPhone 17 Pro Max and iPad Pro 13-inch simulators for the automated suite.
> 2. **Third-party SDKs or analytics:** none. Every framework the app links is Apple's. No
>    analytics service, no crash reporter, no advertising SDK.
> 3. **In-app purchases, subscriptions or links to purchase:** none. The app contains no StoreKit
>    code, shows no price, names no plan, and has no purchase flow or link to one. Where an account
>    cannot do something, the app says what that account does and stops.
> 4. **AI or machine-learning services:** photos and video posted to the feed or an event's room
>    are screened for explicit content by a model that runs on our own server, not by any outside
>    AI service. Voice notes are transcribed on the device by Apple's Speech framework. Pass codes
>    at the door are read on the device by Apple's VisionKit. Nothing is sent to a third-party AI
>    provider.
> 5. **Authentication:** email and password on our own server, and Sign in with Apple. Microsoft
>    sign-in is offered on the website only and is not in the app.
> 6. **Location and mapping:** location is stamped on field-session readings on the device and
>    uploaded only when the person sends the session. The app makes no geocoding or routing
>    calls to any outside service; maps are Apple's MapKit.
> 7. **What the tabs are, and where Events is.** On **iPhone** the tab bar holds five: Feed, My
>    Cases, Investigations, Field Kit, Profile. **Public events are on the Profile tab** — tap
>    Profile, then *Public events*; hosted events the person is going to are under *What I'm going
>    to*. On **iPad** the sidebar shows all six. The app offers only the sections that apply to the
>    signed-in person, so a reviewer on the demo account sees every one of them; somebody with no
>    group sees Feed, Field Kit and Profile.
> 8. **Notifications:** local only, scheduled on the device when somebody turns on a reminder for
>    an event's programme session. No push.
>
> **Privacy** is stated in-app at Profile → About & Privacy, reachable without signing in, and in
> full at https://ishaunted.com/privacy.

**The 3.1.1 wording rule still holds.** A grep of `IsHaunted/` and `BenKit/` on 2026-09-16 finds no
"paid plan", no price, no "subscribe" and no "upgrade" in any string the app can show;
`PaidPlanTests` still asserts the server's sentence carries no "plan". See `APP-STORE-1.0.2.md` §4
for the two sentences and why they were softened.

---

## 5. Screenshots and previews

`Ben.iOS/screenshots-1.0.3/` — see its README. Captured 2026-09-16 from this tree, **dark mode**,
through a real scripted night on the simulator. **Six frames per device** and one preview each,
every one re-captured from the current build rather than carried over:

| Slot in App Store Connect | Files, in order |
|---|---|
| iPhone 6.7"/6.5" display | `iphone-6.5-dark/` 11, 13, 14, 12, 10, 04 (1242×2688) |
| iPad Pro 13" display | `ipad-13-dark/` same order (2064×2752) |
| App Preview, iPhone | `app-preview/iphone-preview.mp4` (28 s, silent AAC track) |
| App Preview, iPad | `app-preview/ipad-preview.mp4` (28 s, silent AAC track) |

Lead with the Field Kit (11 live excursion, 13 review, 14 trimmer, 12 marks, 10 base set) and end
with 04, the Field Kit screen with its new doors — *Open a .ben file* and *On the server, not on
this phone*. The four 1.0.0 frames for the feed, cases, investigations and events are **not** in
this set; if App Store Connect keeps the 1.0.2 ones in the slots after the six, either leave them
(those screens are unchanged) or remove them — six is above Apple's minimum of one.

**There is no camera frame.** A simulator has no camera and the frame came out black with "No
camera is available on this device" on it, so the capture skips it; a real-phone capture is
needed for one. The preview shows a photograph being taken part-way through instead.

Both previews carry a silent audio track on purpose — App Store Connect refused silent-by-absence
previews on 2026-09-09 (*"unsupported or corrupted audio"*).

Before uploading, look at every frame: no Developer section, no wrapped clock, the trimmer's in
point somewhere other than the very start.

---

## 6. Submit — what is left, in order

Steps 1–3 are **done**; 4–12 are Ben's, in App Store Connect.

1. ~~Merge, pull, clean tree.~~ Done: `develop` = `master` = `12e55322`, pushed.
2. ~~Deploy the server first.~~ Done 2026-09-16 6:24 PM; verified in §1.
3. ~~Archive and upload.~~ Done 6:33 PM. Processing takes 10–30 minutes; the build then appears
   under **TestFlight → iOS Builds** as **1.0.3 (6)**. If an email arrives about a missing
   compliance answer or a `dSYM`, read it — the upload declared no non-exempt encryption and
   included both dSYMs, so neither should.

4. **Open App Store Connect → My Apps → IsHaunted.** In the sidebar under *iOS App*, look at what
   1.0.2 says. If it is *Ready for Distribution* (released), the **+** next to *iOS App* works —
   click it, enter **1.0.3**. If it is still *Pending Developer Release*, release it first (or the
   store never gets 1.0.2 at all), then add 1.0.3. On 1.0.2 there was no "+" and the version had to
   be renamed, because nothing had ever been released; once something has, adding works.

5. **Fill the 1.0.3 page.** What's New from §2. Promotional text, description, keywords, support
   and marketing URLs pre-fill from 1.0.2 — check them. Age rating and category unchanged.

6. **Screenshots and previews** from §5, per slot. Drag the six frames in the order given; upload
   the preview into the App Preview slot of the same display size and pick a poster frame.

7. **Build.** In the *Build* section click **+** (or *Select a build before you submit your app*)
   and choose **1.0.3 (6)**. It is offered only once processing has finished **and** because the
   page's version string matches the build's — that is why the page must say 1.0.3 first.

8. **App Review Information.** Sign-in required: yes; demo user apple@apple.com and its password.
   Notes: paste §4 in full. Contact: Ben's phone and email. Attachment: none needed.

9. **App Privacy** — pre-filled; confirm seven types, linked, no tracking.

10. **Export compliance** — `ITSAppUsesNonExemptEncryption = false` is in the binary, so no question
    should appear. If one does: "No" to proprietary encryption.

11. **Version release:** *Manually release this version* if you want to choose the moment, else
    automatic. Availability stays United States, Free.

12. **Add for Review → Submit to App Review.**

**During review:** keep production up at `12e55322` or later, keep the feed flag ON, keep
apple@apple.com working, rostered, and holding a hosted-event booking — the reviewer uses all of
them. Check the demo account signs in on production the day of submission.

---

## 7. If it is rejected

Check App Store Connect → 1.0.3 → App Review → Resolution Center for the letter.

- **Metadata only** (2.1 Information Needed, screenshot or wording complaints): fix in the form,
  reply in Resolution Center, **Add for Review** again with the **same build**.
- **A code problem**: fix it, set `CURRENT_PROJECT_VERSION` to **7** (marketing stays 1.0.3),
  archive and upload with the commands in §9, select 1.0.3 (7) on the version page, reply in
  Resolution Center saying what changed, Add for Review. Never reuse 6.
- **Likeliest questions this time**, with the answer ready: what the Share extension is for (§2);
  why the app opens `.ben` files (§2 — it is the app's own export format); why the camera is used
  twice (§3 — session photos and the door scanner). Each is already in §4.

---

## 8. Checklist

- [x] `MARKETING_VERSION` 1.0.3, `CURRENT_PROJECT_VERSION` 6 — app and Share extension alike, proven in the archive
- [x] No third-party frameworks or packages — every import re-read 2026-09-16; zero remote package references
- [x] `PrivacyInfo.xcprivacy` unchanged since build 5; seven types, all linked, no tracking
- [x] `ITSAppUsesNonExemptEncryption = false`
- [x] Usage strings for every permission the app asks for (eight)
- [x] Sign in with Apple, associated domains and the app group in `Support/IsHaunted.entitlements`
- [x] No StoreKit, no price, no plan named, nothing pointing at a purchase
- [x] `screenshots-1.0.3/` — six per device and both previews, from this tree
- [x] BenKit suite green on this tree (490 tests); help, PDFs and What's New updated with the changes
- [x] Server deployed to production from the same commit, **before** the upload (§1)
- [x] Association file live: 200, `application/json`, no redirect
- [x] Archive built from `master` at `12e55322`, clean tree
- [x] Uploaded to App Store Connect, 2026-09-16 6:33 PM
- [x] Version 1.0.3 created in App Store Connect (§6 step 4)
- [x] Page filled, media uploaded, build 1.0.3 (6) selected (§6 steps 5–7)
- [x] Review notes pasted
- [x] Submitted 2026-09-16 — Waiting for Review
- [ ] Approved and released; then retire the per-file upload endpoints the approved 1.0.2 still used

---

## 9. The archive and the upload, as they were done

Archived from `master` at `12e55322` with automatic signing:

    cd Ben.iOS
    xcodebuild archive -project IsHaunted.xcodeproj -scheme IsHaunted -configuration Release \
      -destination "generic/platform=iOS" \
      -archivePath "$HOME/Library/Developer/Xcode/Archives/2026-09-16/IsHaunted 1.0.3 (6).xcarchive" \
      -allowProvisioningUpdates

It is in the Organizer under that name: version 1.0.3, build 6, `com.ishaunted.ios`, the Share
extension inside it at the same numbers, both dSYMs included, all three entitlements present, no
warnings. Like build 5 it is signed with *Apple Development* — the only certificate in this Mac's
keychain — and that is expected: the export re-signs for distribution.

Exported and uploaded in one step with `Ben.iOS/ExportOptions-appstore.plist` — the same choices
as Organizer's *Distribute App → App Store Connect → Upload*, written down so nobody has to
remember that **manage version and build number must be OFF**:

    xcodebuild -exportArchive \
      -archivePath "$HOME/Library/Developer/Xcode/Archives/2026-09-16/IsHaunted 1.0.3 (6).xcarchive" \
      -exportOptionsPlist Ben.iOS/ExportOptions-appstore.plist \
      -exportPath /tmp/ishaunted-export -allowProvisioningUpdates

Xcode signed the upload for distribution through its cloud-managed signing against Ben's Apple ID
(the one signed into Xcode → Settings → Accounts); no *Apple Distribution* certificate was
created in the local keychain, and none is needed for the next upload either. The log ended
*"Uploading “IsHaunted.ipa” is complete. Uploaded IsHaunted. ** EXPORT SUCCEEDED **"*. With
`destination = upload` nothing is written to the export path but logs.

For build 7, if there is one: bump `CURRENT_PROJECT_VERSION`, change `(6)` to `(7)` in both
commands, run both.
