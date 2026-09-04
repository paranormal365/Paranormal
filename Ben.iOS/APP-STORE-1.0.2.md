# App Store submission — IsHaunted 1.0.2 (build 2)

Everything needed to build, upload and submit **1.0.2**, and every answer App Review has asked for
so far. This supersedes `APP-STORE.md` for the submission itself; that file stays as the record of
1.0.0 and the reasoning behind the listing.

**State on 2026-09-04 (end of day):** ready, not sent. Ben: *"I am going to wait until we finish
everything to try to get the submission done."* Items 214 (screenshots and previews) and 215
(delayed session start) have both landed; §5 describes the final set.

Bundle id `com.ishaunted.ios` · `MARKETING_VERSION` **1.0.2** · `CURRENT_PROJECT_VERSION` **2** ·
iPhone and iPad · iOS 18.0 minimum. Both numbers are set in `IsHaunted.xcodeproj` and were proven
in the built bundle's `Info.plist` (`CFBundleShortVersionString` 1.0.2, `CFBundleVersion` 2).

---

## 1. Where 1.0.0 stands, and why this is 1.0.2

1.0.0 build 1 was submitted 2026-08-30 09:25 and **rejected 2026-09-02 under Guideline 2.1,
Information Needed** — a request for seven answers in the review notes, not a bug and not a policy
breach. Build 1 was never approved and was never resubmitted.

There was no 1.0.1. Ben chose **1.0.2** as the next version name so the two packages can never be
confused (2026-09-04). App Store Connect accepts any higher version string.

**The build-number rule:** `CFBundleVersion` must be strictly higher than any build ever uploaded
for a marketing version, and an uploaded number is never reusable even after a rejection. Build 1
was uploaded, so this is **build 2**. If 1.0.2 build 2 is ever uploaded and then needs a code fix,
the next upload is build 3 — do not reset it.

---

## 2. What changed since the reviewed build

Everything committed after 2026-08-30 09:20 is new to review. All additive; nothing a reviewer
exercised on 1.0 behaves differently.

- **Field Kit: send only the part that mattered** (item 210). On the Send screen, an in point and
  an out point on a trimmer — green dot, red dot, the kept stretch bolder between them — with a
  preview that plays exactly what will be sent. Trimming happens **on the phone before upload**;
  the original recording never leaves the device. A trimmed session is named for its window:
  *back bedroom (20:00–30:00)*.
- **The place archive from the phone.** Publish a session to a public location's record, with a
  place picker; the review screen shows the archive's verdict — "was that unusual here?"
- **Settings → My evidence.** What this account offered at other people's public events,
  openable whatever the organiser decided, and addable to the place's archive.
- **The app shows only the sections that apply.** A solo investigator no longer carries a My Cases
  or Investigations tab that can hold nothing. Permission is still decided at every endpoint.
- **Universal links** (item 209). The `associated-domains` entitlement for `ishaunted.com` and
  `www.ishaunted.com`. Inert until the association file is live and the App ID capability is on —
  see §6 step 2.
- **Retracting an archive publication requires a paid plan** (server-side rule; the app surfaces
  the server's own sentence). See §4, the one review risk.

**What's New (paste into the version's "What's New in This Version"):**

> Send only the part of a session that mattered: drag the in and out points on the Send screen,
> preview exactly what will go, and keep the full recording on your phone. Publish a session to a
> location's public archive and see how your night compares with everyone else's there. Your
> evidence from other people's events now lives under Settings → My evidence. And the app shows
> only the sections that apply to you.

---

## 3. Listing copy, App Privacy, URLs

Unchanged from 1.0.0 and still accurate — see `APP-STORE.md` §1 and §2 for the text and the
reasoning. Copy them forward as they are. Three things were re-verified against **this** tree on
2026-09-04 rather than carried from build 1:

- **No third-party SDKs.** Every `import` in `IsHaunted/` and `BenKit/` is an Apple framework or
  BenKit itself (AVFoundation, AVKit, AuthenticationServices, Charts, CoreLocation, CoreMotion,
  CryptoKit, EventKit, EventKitUI, Foundation, MapKit, Observation, PDFKit, PhotosUI, Security,
  Speech, SwiftData, SwiftUI, UIKit, os). The Xcode project has **zero** remote package
  references and `BenKit/Package.swift` has no dependencies.
- **App Privacy answers** still match `IsHaunted/PrivacyInfo.xcprivacy`: seven data types, all
  linked, none for tracking, purpose App Functionality; no tracking.
- **Usage strings** in `Support/Info.plist`: Calendars, Calendars (write-only), Camera, Location
  When In Use, Microphone, Motion, Photo Library (add), Speech Recognition. Calendar access is new
  since the checklist in `APP-STORE.md` was written — it is for adding an event to the person's own
  calendar and asks only when they tap that. `ITSAppUsesNonExemptEncryption` is `false`.

---

## 4. Review notes — paste into App Review Information → Notes

The 2.1 rejection asked for specific answers. Those answers were meant to live in `APP-STORE.md`
§3c and **never reached the file** — the notes say they did; the file has no such section. They
are restated here from facts verified in the 1.0.2 tree. The letter's exact question wording is
in App Store Connect → 1.0 → Resolution Center; match the order there.

> IsHaunted is a field tool for paranormal investigation groups. Most of the app works without an
> account: the feed, public events and the entire Field Kit are usable signed out.
>
> **Field Kit** is the core feature and needs no account: Field Kit tab → Start a session. It asks
> for microphone, motion and location permission; all three are optional and a session runs
> without them, with fewer channels. On the Send screen the in/out trimmer chooses what is
> uploaded; the original recording stays on the device.
>
> **Demo account** for the group-side features: apple@apple.com — password in the sign-in fields
> of this form. A member of Paranormal365, rostered on an investigation, with cases to open.
>
> **Answers to the questions from the 1.0 review (Guideline 2.1):**
>
> 1. **Device used for testing:** iPhone 15 Pro Max on iOS 26.6.1 (Ben's physical device);
>    iPhone 17 Pro and iPad Pro 13-inch simulators for the automated suite.
> 2. **Third-party SDKs or analytics:** none. Every framework the app links is Apple's. No
>    analytics service, no crash reporter, no advertising SDK.
> 3. **In-app purchases, subscriptions or links to purchase:** none. The app contains no StoreKit
>    code, shows no price, and has no purchase flow or link to one. Groups that want more than the
>    free tier subscribe on the website in a browser; the app does not link to that page. (Two
>    sentences in the app mention that a paid plan exists — see the note to Ben below.)
> 4. **AI or machine-learning services:** photos and video posted to the feed are screened for
>    explicit content by a model that runs on our own server, not by any outside AI service. Voice
>    notes are transcribed on the device by Apple's Speech framework. Nothing is sent to a
>    third-party AI provider.
> 5. **Authentication:** email and password on our own server, and Sign in with Apple. Microsoft
>    sign-in is offered on the website only and is not in the app.
> 6. **Location and mapping:** location is stamped on field-session readings on the device and
>    uploaded only when the person sends the session. The app makes no geocoding or routing
>    calls to any outside service; maps are Apple's MapKit.
> 7. **What the tabs are:** Feed, My Cases, Investigations, Field Kit, Events, Profile. The app
>    shows only the ones that apply to the signed-in person, so a reviewer on the demo account
>    sees all six; a person with no group sees Feed, Field Kit and Profile.
>
> **Privacy** is stated in-app at Profile → About & Privacy, reachable without signing in, and in
> full at https://ishaunted.com/privacy.

**Note to Ben — the one review risk in this build.** Two places say a paid plan exists, with no way
to buy one in the app:

- `SessionReviewView`: *"A paid plan compares this session with theirs — your flagged moments per
  hour against what this place typically gives people, and whether this night stood out."*
- The server's sentence when retracting an archive publication: *"Keeping your sessions private is
  part of a paid plan"*, surfaced verbatim by `EvidenceActions`.

Apple's Guideline 3.1.1 has been applied to wording that points at an outside purchase. Neither
sentence links anywhere, names a price, or says "buy", which is the usual line — but a reviewer
could ask. Options: leave them and answer as in item 3 above; or soften both to describe the
feature without the word "paid". Your call before archiving; changing them is a code change and
therefore a new build number.

---

## 5. Screenshots and previews

`Ben.iOS/screenshots-1.0.2/` — see its README. Captured 2026-09-04 (item 214), **dark mode**, at the
required sizes. Ten screenshots per device: five new Field Kit frames driven through a real
scripted night (base level set, the needle swung to ~+60 mG, sentry armed, marks, the review, the
trimmer), the Field Kit home with sessions in it, and the four unchanged 1.0.0 frames for the feed,
cases, investigations and events. Two new 28 s previews, from Start onward, video only.

Upload order suggestion — lead with the Field Kit, since it is the reason the app exists:

| Slot in App Store Connect | Files, in order |
|---|---|
| iPhone 6.7"/6.5" display | `iphone-6.5-dark/` 11, 13, 14, 12, 10, 04, 01, 03, 02, 05 (1242×2688) |
| iPad Pro 13" display | `ipad-13-dark/` same order (2064×2752) |
| App Preview, iPhone | `app-preview/fieldkit-preview-886x1920.mp4` (28 s) |
| App Preview, iPad | `app-preview/fieldkit-preview-ipad-1200x1600.mp4` (28 s) |

The previews have no audio track. App Store Connect accepts silent previews; if it objects at
upload, the README says how a silent track would be added.

---

## 6. Build and submit — the whole procedure

Do these in order. Steps 1–3 are one-time and only matter because of the new entitlement.

1. **Merge and pull.** Everything in `develop` is on `master`; build from `master` at a clean
   working tree. Confirm `git status` shows nothing but your own untracked files.

2. **Enable Associated Domains on the App ID** (one-time, for item 209). Developer portal →
   Certificates, Identifiers & Profiles → Identifiers → `com.ishaunted.ios` → tick **Associated
   Domains** → Save. Without this, archiving with the new entitlement fails signing or Xcode
   strips it. Then in Xcode, Signing & Capabilities, let it regenerate the profile.

3. **Make sure the association file is live** before anybody installs the build:
   `https://ishaunted.com/.well-known/apple-app-site-association` must return 200 as
   `application/json` with no redirect. It ships with the website deploy from the same merge. iOS
   fetches it once at install and caches the answer.

4. **Confirm the numbers.** In Xcode, target IsHaunted → General: Version **1.0.2**, Build **2**.
   They are already set; this is a look, not an edit.

5. **Decide on the two paid-plan sentences** (§4). If you change them, commit first.

6. **Archive.** Select "Any iOS Device (arm64)" as the destination, then Product → Archive.
   The Organizer opens with the archive.

7. **Upload.** Distribute App → App Store Connect → Upload. Accept the defaults (upload symbols,
   manage version and build number OFF — the numbers are ours). Wait for "Upload Successful".
   Processing takes 10–30 minutes; the build appears under TestFlight → iOS Builds.

8. **Create the version.** App Store Connect → My Apps → IsHaunted → **+ Version** → 1.0.2.
   (1.0 stays in its rejected state; a new version supersedes it.)

9. **Fill the version page.** What's New from §2. Screenshots and previews from §5. Promotional
   text, description, keywords, support and marketing URLs carried from `APP-STORE.md` §1 — they
   pre-fill from 1.0, check them rather than retyping. Age rating and category are unchanged.

10. **Select build 2** in the Build section once processing finishes.

11. **App Review Information.** Sign-in required: yes; demo user apple@apple.com and its password.
    Notes: paste §4 in full. Contact: Ben's phone and email.

12. **App Privacy** — pre-filled from 1.0; confirm it still says seven types, linked, no tracking.

13. **Export compliance** — the upload declares `ITSAppUsesNonExemptEncryption = false`, so no
    question should appear. If one does: "No" to proprietary encryption.

14. **Add for Review** → **Submit**. Availability stays United States, Free.

**During review:** keep production up, keep the feed flag ON, keep apple@apple.com working and
rostered — the reviewer uses all three.

---

## 7. If it is rejected again

Check App Store Connect → 1.0.2 → App Review → Resolution Center for the letter.

- **Metadata only** (2.1 Information Needed, screenshot or wording complaints): fix in the form,
  reply in Resolution Center, **Add for Review** again with the **same build 2**. No new build.
- **A code problem** (crash, a flow that does not work, a guideline that needs a change): fix it,
  bump `CURRENT_PROJECT_VERSION` to **3** (marketing stays 1.0.2), Archive, Upload, select build 3
  on the version page, reply in Resolution Center saying what changed, Add for Review.
- The "How to Prevent Common Issues" list at the bottom of a 2.1 letter is boilerplate on every
  such letter, not findings.

**What does NOT need redoing on a rejection:** listing copy, privacy answers, screenshots (unless
they are the complaint), the demo account, the association file, the App ID capability.

---

## 8. Checklist

- [x] `MARKETING_VERSION` 1.0.2, `CURRENT_PROJECT_VERSION` 2 — set and proven in the built bundle
- [x] No third-party frameworks or packages — re-verified in this tree
- [x] `PrivacyInfo.xcprivacy` present; App Privacy answers match it
- [x] `ITSAppUsesNonExemptEncryption = false`
- [x] Usage strings for every permission the app asks for (eight)
- [x] Associated-domains entitlement in `Support/IsHaunted.entitlements`
- [x] `screenshots-1.0.2/` — ten per device, dark, right sizes, driven through a real night (item 214)
- [x] Two new 28 s previews at Apple's sizes, from Start onward (item 214)
- [x] **Item 215** — delayed session start, in this build
- [ ] Ben's decision on the two paid-plan sentences (§4)
- [ ] Associated Domains capability enabled on the App ID (§6 step 2)
- [ ] Association file live on ishaunted.com (§6 step 3)
- [ ] Demo account apple@apple.com verified working on production the day of submission
- [ ] Archive, upload, + Version 1.0.2, fill, select build 2, submit
