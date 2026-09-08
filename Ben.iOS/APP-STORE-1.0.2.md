# App Store submission — IsHaunted 1.0.2 (build 3)

Everything needed to build, upload and submit **1.0.2**, and every answer App Review has asked for
so far. This supersedes `APP-STORE.md` for the submission itself; that file stays as the record of
1.0.0 and the reasoning behind the listing.

**State on 2026-09-08:** ready, not sent, and re-verified line by line against the tree as it now
stands (evaluation phase 9). Two evaluation phases landed in the app after this document was
written on 2026-09-04, so every claim in it was a claim about a tree that no longer existed. What
changed is recorded below; what did not is marked re-verified rather than carried.

Bundle id `com.ishaunted.ios` · `MARKETING_VERSION` **1.0.2** · `CURRENT_PROJECT_VERSION` **3** ·
iPhone and iPad · iOS 18.0 minimum. Both numbers are set in `IsHaunted.xcodeproj` and were proven
on 2026-09-08 in a bundle built from this tree (`CFBundleShortVersionString` 1.0.2,
`CFBundleVersion` 3, `MinimumOSVersion` 18.0, `CFBundleIdentifier` com.ishaunted.ios).

---

## 1. Where 1.0.0 stands, and why this is 1.0.2

1.0.0 build 1 was submitted 2026-08-30 09:25 and **rejected 2026-09-02 under Guideline 2.1,
Information Needed** — a request for seven answers in the review notes, not a bug and not a policy
breach. Build 1 was never approved and was never resubmitted.

There was no 1.0.1. Ben chose **1.0.2** as the next version name so the two packages can never be
confused (2026-09-04). App Store Connect accepts any higher version string.

**The build-number rule:** `CFBundleVersion` must be strictly higher than any build ever uploaded
for a marketing version, and an uploaded number is never reusable even after a rejection. Build 1
was uploaded for 1.0.0.

**This is build 3**, not 2. Build 2 was prepared on 2026-09-04 and, as far as this repository
knows, never sent — but only App Store Connect knows that for certain, and a number is cheap where
a rejected upload is not. Ben's call on 2026-09-08: burn 2 and go up as 3. If build 3 is uploaded
and then needs a code fix, the next upload is 4 — never reset it.

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
- **The roster decodes when nobody has been marked** (iOS-3, found by the 2026-09-06 evaluation).
  `didAttend` arrives null for an attendee nobody has recorded either way, and one such row made
  the whole Investigations list fail — which also emptied the Send screen's destination list, so a
  member could not file a night against the visit they were on.
- **A member can open the case behind a visit** (iOS-8). Investigations → the visit → the case:
  what the client reported, the timeline, the files, and the group's thread with them. Read-only
  on the phone; adding to a case is done on the website.
- **Smaller truths.** An untitled session no longer prints its timestamp twice in the Field Kit
  list; the review screen says "no base level was set" over the empty chart instead of leaving it
  blank; and My Cases no longer tells a group member to ask a group for help.
- **Nothing in the app mentions a plan any more.** The two sentences that did — the session
  review's comparison and the server's refusal when a publication is retracted — now describe what
  this account does and stop there. See §4; this is the change that made it build 3.

**What's New (paste into the version's "What's New in This Version"):**

> Send only the part of a session that mattered: drag the in and out points on the Send screen,
> preview exactly what will go, and keep the full recording on your phone. Publish a session to a
> location's public archive and see how your night compares with everyone else's there. Your
> evidence from other people's events now lives under Settings → My evidence. Open the case behind
> a visit you are on, and read what the client reported without leaving the house. And the app
> shows only the sections that apply to you.

---

## 3. Listing copy, App Privacy, URLs

Unchanged from 1.0.0 and still accurate — see `APP-STORE.md` §1 and §2 for the text and the
reasoning. Copy them forward as they are. Three things were re-verified against **this** tree on
2026-09-04 rather than carried from build 1:

- **No third-party SDKs.** Re-verified 2026-09-08. Every `import` in `IsHaunted/` and `BenKit/` is
  an Apple framework or BenKit itself (AVFoundation, AVKit, AuthenticationServices, Charts,
  CoreLocation, CoreMotion, CryptoKit, EventKit, EventKitUI, Foundation, FoundationNetworking,
  MapKit, Observation, PDFKit, PhotosUI, Security, Speech, SwiftData, SwiftUI, UIKit, os).
  `FoundationNetworking` is Apple's too and is behind `#if canImport`, which is false on iOS — it
  is there so BenKit still compiles off Apple platforms. The Xcode project has **zero** remote
  package references and `BenKit/Package.swift` has no dependencies.
- **App Privacy answers** still match `IsHaunted/PrivacyInfo.xcprivacy` — re-read 2026-09-08:
  seven data types (email address, name, user ID, precise location, photos or videos, audio data,
  other user content), all linked, none for tracking, purpose App Functionality; `NSPrivacyTracking`
  false.
- **Usage strings** in `Support/Info.plist`: Calendars, Calendars (write-only), Camera, Location
  When In Use, Microphone, Motion, Photo Library (add), Speech Recognition. Calendar access is new
  since the checklist in `APP-STORE.md` was written — it is for adding an event to the person's own
  calendar and asks only when they tap that. `ITSAppUsesNonExemptEncryption` is `false`. All ten
  facts in this bullet were re-read from `Support/Info.plist` on 2026-09-08.

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
>    code, shows no price, names no plan, and has no purchase flow or link to one. Where an account
>    cannot do something, the app says what that account does and stops — it does not say what
>    would change it or where. Groups that want more than the free tier arrange that on the website
>    in a browser; the app neither says so nor links there.
> 4. **AI or machine-learning services:** photos and video posted to the feed are screened for
>    explicit content by a model that runs on our own server, not by any outside AI service. Voice
>    notes are transcribed on the device by Apple's Speech framework. Nothing is sent to a
>    third-party AI provider.
> 5. **Authentication:** email and password on our own server, and Sign in with Apple. Microsoft
>    sign-in is offered on the website only and is not in the app. (An account created through
>    Microsoft on the website is told, in the app, to finish setting it up there; that is the only
>    mention of Microsoft the app contains and it offers no such sign-in.)
> 6. **Location and mapping:** location is stamped on field-session readings on the device and
>    uploaded only when the person sends the session. The app makes no geocoding or routing
>    calls to any outside service; maps are Apple's MapKit.
> 7. **What the tabs are, and where Events is.** On **iPhone** the tab bar holds five: Feed, My
>    Cases, Investigations, Field Kit, Profile. **Public events are on the Profile tab** — tap
>    Profile, then *Public events*. On **iPad** the sidebar shows all six, Events among them.
>    Either way the app offers only the sections that apply to the signed-in person, so a
>    reviewer on the demo account sees every one of them; somebody with no group sees Feed,
>    Field Kit and Profile.
>
> **Privacy** is stated in-app at Profile → About & Privacy, reachable without signing in, and in
> full at https://ishaunted.com/privacy.

**The 3.1.1 risk this build used to carry, and how it was removed.** Two places said a paid plan
exists, with no way to buy one in the app. Neither linked anywhere, named a price or said "buy" —
but Apple's Guideline 3.1.1 has been applied to wording that merely points at an outside purchase,
and answering a reviewer's question is worse than not raising it. **Ben's decision, 2026-09-08:
soften both.** Both now describe the account rather than the plan:

| Where | Now says |
|---|---|
| `SessionReviewView` | *"This account doesn't compare your session with everyone else's here: your flagged moments per hour against what this place typically gives people, and whether this night stood out."* |
| `PaidPlan.WhyCannotKeepPrivateAsync`, surfaced verbatim by `EvidenceActions` | *"Publishing to a place's archive cannot be undone on this account. What you publish stays there — it is what makes the archive worth reading."* |

Each still names exactly what it withholds, which is the rule these sentences were written to: a
refusal that hides the shape of what you are missing teaches people to assume it is nothing.

**The website keeps its own words.** The second sentence is the server's, and the website shows it
too — so the plan is explained beside it in `MyFieldSessions.razor`, which is Razor markup no phone
ever renders. `PaidPlanTests` now asserts the sentence carries the reason and **does not contain
the word "plan"**, so nobody can put it back by accident; the assertion was run against the old
wording and seen to fail.

A grep of `IsHaunted/` and `BenKit/` finds no "paid plan", no price, no "subscribe" and no
"upgrade" in any string the app can show.

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

**Re-checked 2026-09-08, and kept as they are.** `FieldKitScreenshotTests` was re-run against a
build of this tree on the iPhone 17 Pro Max, and every frame came back the same screen: nothing
phase 7 changed shows in this set (its untitled-session fix only shows on a session with no name,
and these have one; its "no base level was set" line only shows when no base was taken, and this
night takes one). Re-capturing now would have made them worse, not newer — this simulator carries
months of interrupted test sessions that would have filled the Field Kit list behind the frame.

**Why the tab bar differs between frames, and why that is right.** The six Field Kit frames show a
three-tab bar (Feed, Field Kit, Profile) and the four carried frames show five. That is the app
doing what §4 item 7 says it does: it offers only the sections that apply, so somebody with no
group has three and a group member has five. The set shows both, which is the product, not a
mistake in the capture.

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

4. **Confirm the numbers.** In Xcode, target IsHaunted → General: Version **1.0.2**, Build **3**.
   They are already set; this is a look, not an edit.

5. **The two paid-plan sentences are already decided and changed** (§4) — nothing to do here.
   The server half of that change ships with the website deploy from the same merge, so deploy
   before a reviewer opens the app: until then the API still answers with the old wording.

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

10. **Select build 3** in the Build section once processing finishes.

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
  reply in Resolution Center, **Add for Review** again with the **same build 3**. No new build.
- **A code problem** (crash, a flow that does not work, a guideline that needs a change): fix it,
  bump `CURRENT_PROJECT_VERSION` to **4** (marketing stays 1.0.2), Archive, Upload, select build 4
  on the version page, reply in Resolution Center saying what changed, Add for Review.
- The "How to Prevent Common Issues" list at the bottom of a 2.1 letter is boilerplate on every
  such letter, not findings.

**What does NOT need redoing on a rejection:** listing copy, privacy answers, screenshots (unless
they are the complaint), the demo account, the association file, the App ID capability.

---

## 8. Checklist

Everything ticked below was re-verified against **this** tree on 2026-09-08, not carried from the
2026-09-04 pass.

- [x] `MARKETING_VERSION` 1.0.2, `CURRENT_PROJECT_VERSION` 3 — proven in a bundle built from this tree
- [x] No third-party frameworks or packages — every import re-read; zero remote package references
- [x] `PrivacyInfo.xcprivacy` present; seven types, all linked, no tracking; App Privacy matches
- [x] `ITSAppUsesNonExemptEncryption = false`
- [x] Usage strings for every permission the app asks for (eight)
- [x] Associated-domains and Sign in with Apple entitlements in `Support/IsHaunted.entitlements`
- [x] No StoreKit, no price, no plan named, nothing pointing at a purchase (§4)
- [x] `screenshots-1.0.2/` — ten per device; re-run against this tree and unchanged, so kept (§5)
- [x] Two new 28 s previews at Apple's sizes, from Start onward (item 214)
- [x] **Item 215** — delayed session start, in this build
- [x] Ben's decision on the two paid-plan sentences — soften both; done, and guarded by a test
- [x] BenKit suite green on this tree (330 tests)
- [ ] Associated Domains capability enabled on the App ID (§6 step 2)
- [ ] Association file live on ishaunted.com (§6 step 3)
- [ ] **Website and API deployed from this merge** — the softened server sentence ships with it
- [ ] Demo account apple@apple.com verified working on production the day of submission
- [ ] Archive, upload, + Version 1.0.2, fill, select build 3, submit
