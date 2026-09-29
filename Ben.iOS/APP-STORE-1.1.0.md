# App Store update — IsHaunted 1.1.0 (build 8)

Ben's own notes for putting **1.1.0** on the App Store: what is in it, what to do on the server
first, and exactly where to go in App Store Connect and what to click. Nothing here is published on
the website; `APP-STORE-1.0.3.md` stays as the record of 1.0.3.

Ben, 2026-09-28: *"The next push of the iOS app is going to be 1.1.0. This cleans up bugs and
clarifies handling timezones and will add a launch button for the planner, tour guide or
investigation lead etc."*

| | |
|---|---|
| Version | **1.1.0** (`MARKETING_VERSION`) — what people see in the store |
| Build | **8** (`CURRENT_PROJECT_VERSION`) — above every build ever uploaded; never reuse a number |
| Set in | `master` at `262e1646` (2026-09-29). App, Share extension and UI tests all at 1.1.0 (8) — checked on a Release build |
| Archived / uploaded | **Not yet** — §4 |
| Replaces | 1.0.3 (7), the build on people's phones |

---

## 1. What is new since 1.0.3 (7)

Everything the app gained after build 7 was archived (2026-09-17, 7:23 AM). The public record is the
apps change log (`Ben.Web.Services/Changelog/Content/apps.md`, the **1.1.0** sections).

- **A lead starts everybody's Field Kit** (item 252). A tour's guide, an event's organiser or door
  staff, or an investigation's lead taps *Launch a session for your group* in Field Kit. Everybody
  registered gets a **push notification** and a card in the feed; Field Kit lists it under
  *Happening now*. Nobody is started automatically — *Join* opens a session already set up for the
  night, and nothing records until Start.
- **Joining by QR code.** The lead's page shows a code. At a public tour or event anybody who scans
  is straight in; at a private night they sign in and ask, and the lead taps *Let in* or *No*.
- **Sessions go to the group's night** — that night or days later. Rows say *Sent* / *Not sent*;
  the send screen says where it goes; the next ten minutes of a long session no longer replaces the
  part sent before it.
- **Time zones.** Times show where the phone is, with a *Local time · My time* switch on events,
  tours, investigations and a client's visits. A client's case now lists its visits.
- **Field sessions as Ben tested them** (2026-09-27): louder recordings and playback at full volume;
  video for the whole session with its sound; photos in the replay; *Watch for motion*; *Start
  another session* after Stop; the new-session sheet knows where you are; *Find public sessions*;
  a refused permission says so and opens Settings; 10 minutes of video and 600 MB per upload, and
  *Send without the video*.
- **Fixes.** A client's case no longer shows as closed while the group is working it (it did since
  1.0.0 — the phone read the server's status numbers wrongly). On an iPad, a notification or link
  into a page deeper than a section's front screen opens that page. The iPad New session sheet shows
  its button. The send screen says 600 MB, not 629.1 MB.

---

## 2. The server goes first — do this before you submit

The reviewer's app talks to **production**. If production is behind, the new screens fail in front
of Apple.

1. **Apply the migrations first, then deploy `master` at `407713e4` or later** to ishaunted.com
   (`docs/deploy-production.md`). **Nothing applies migrations at startup** — run
   `dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi` against
   production before the new API goes up (check with `dotnet ef migrations list …` first: these five
   should be the only `(Pending)` ones). All five only add columns, tables and indexes — nothing is
   dropped or rewritten. Production was at `353f1674` on 09/29, which has none of them:
   - `TimeZonesForPeopleGroupsCasesAndInvestigations`
   - `PushDevicesForLeadLaunch`
   - `FieldLaunchesAndExpiringPosts`
   - `FieldLaunchJoinByCode`
   - `FieldSessionsAtEvents`
2. **Put the push key on the server.** The key is `AuthKey_TVH4P55742.p8` in `~/.ishaunted/` on
   this Mac (the one made for sandbox and production). Copy it to the server beside the other Apple
   keys, and give the site's application-pool identity **read** on that file. Then add to the
   production API settings:
   ```json
   "Apns": {
     "KeyId": "TVH4P55742",
     "PrivateKeyPath": "<full path to AuthKey_TVH4P55742.p8 on the server>"
   }
   ```
   Team and bundle id need nothing: the team falls back to `Apple:TeamId`, and `BundleId` is already
   `com.ishaunted.ios`. Restart the API.
3. **Check it:**
   - `https://ishaunted.com/webapi/api/public/build` answers the commit you deployed.
   - `https://ishaunted.com/.well-known/apple-app-site-association` lists `/field-kit/join/*`
     (that is what makes a scanned QR code open the app).
   - The API started — a key it cannot read stops it with a sentence naming the file.
4. **Give the reviewer something to launch.** Launch only appears for something the signed-in
   person leads that is on *now* (from three hours before it starts until it ends), and nobody knows
   when a reviewer will look. So, on the website as Paranormal365's owner:
   - Create an investigation **"App Review night"** at a public place (not somebody's home),
     starting today and **ending 14 days from now**.
   - Add **apple@apple.com** to it and mark them **Lead**.
   - Add one more account you control to it as well, so the launch visibly reaches somebody.

   The demo account then has *Launch a session for your group* in Field Kit for the whole review.
   Move the end date on if review runs longer. Delete it after approval.

**Keep** the old per-file upload endpoints — phones still on 1.0.2 use them (`APP-STORE-1.0.3.md` §10).

---

## 3. Try it on your own phone first (recommended)

Push only really works on a real device, and TestFlight uses the same **production** push service
the App Store will.

1. After §4 step 2, the build appears in App Store Connect → **TestFlight** → *iOS Builds* as
   **1.1.0 (8)** (10–30 minutes after upload).
2. Add yourself as an internal tester if you are not one already (*Internal Testing* → your group →
   **+**), then install it from the **TestFlight** app on your iPhone.
3. Sign in, open an event or investigation you are on, and **Allow** notifications when asked.
4. From another account (the website can't launch — use a second phone or the iPad simulator
   pointed at production), launch something you are registered for. The notification should arrive
   within seconds; tapping it should open Field Kit ready to Start.

If no notification arrives: check §2 step 2 first — nine times in ten it is the key's path or the
pool identity's read permission.

---

## 4. Build and upload

From an up-to-date `master` with a clean working tree:

```bash
cd Ben.iOS
xcodebuild archive -project IsHaunted.xcodeproj -scheme IsHaunted -configuration Release \
  -destination "generic/platform=iOS" \
  -archivePath "$HOME/Library/Developer/Xcode/Archives/$(date +%Y-%m-%d)/IsHaunted 1.1.0 (8).xcarchive" \
  -allowProvisioningUpdates
```

```bash
xcodebuild -exportArchive \
  -archivePath "$HOME/Library/Developer/Xcode/Archives/$(date +%Y-%m-%d)/IsHaunted 1.1.0 (8).xcarchive" \
  -exportOptionsPlist ExportOptions-appstore.plist \
  -exportPath /tmp/ishaunted-export -allowProvisioningUpdates
```

Or the same thing in Xcode: **Product → Archive**, then in the Organizer **Distribute App → App
Store Connect → Upload**, and leave *Manage version and build number* **off**.

**New this time — push.** The app now carries the `aps-environment` entitlement. With
`-allowProvisioningUpdates` (or automatic signing in Xcode), Xcode turns on **Push Notifications**
for `com.ishaunted.ios` in your developer account and signs the upload for **production** push —
the `development` in `Support/IsHaunted.entitlements` is only for builds run from Xcode. If the
upload complains about the capability instead, turn it on by hand: developer.apple.com →
*Certificates, Identifiers & Profiles* → *Identifiers* → `com.ishaunted.ios` → tick **Push
Notifications** → Save, then run the export again.

**What actually happened on 09/29, and the fix.** The first **Product → Archive** failed with
*"Unable to log in with account 'haveben@msn.com' … rejected"* and *"Provisioning profile 'iOS Team
Provisioning Profile: com.ishaunted.ios' doesn't include the Push Notifications capability"* (and
*"…aps-environment entitlement"*). One cause: Xcode's Apple account session had expired, so it could
not make the new push-enabled profile and fell back to the old one. **Xcode → Settings → Accounts →**
sign in again, check **Signing & Capabilities** lists *Push Notifications*, and archive again. Also:
**Archive is greyed out** while the destination at the top is a simulator — pick **Any iOS Device
(arm64)** first. Leave *Background Modes → Remote notifications* **off** (that is for silent pushes;
ours are ordinary alerts).

**Xcode may rewrite `Support/Info.plist` and `project.pbxproj`** when you open the target's
settings — moving the keys into `INFOPLIST_KEY_…` build settings and dropping the comments. The app it
builds is the same, but don't commit that churn: `git checkout -- Ben.iOS/Support/Info.plist
Ben.iOS/IsHaunted.xcodeproj/project.pbxproj`.

The log should end *"Uploading “IsHaunted.ipa” is complete … EXPORT SUCCEEDED"*.

---

## 5. App Store Connect — step by step

Go to **appstoreconnect.apple.com** → **Apps** → **IsHaunted**.

### 5.1 Make the new version

1. In the left column under **iOS App**, 1.0.3 should say *Ready for Distribution*. (If it says
   *Pending Developer Release*, release 1.0.3 first — otherwise nobody ever gets it.)
2. Click the **+** beside **iOS App** and type **1.1.0**. It must match the build exactly, or the
   build will not be offered in step 5.4.
3. The new **1.1.0** page opens with the description, keywords, URLs and screenshots carried over
   from 1.0.3.

### 5.2 What's New in This Version

Paste this (it is required for an update; 4,000 characters at most — this is well under):

> Start a group session together. Tour guides, event organisers and investigation leads can now
> launch a session for everybody at once: you'll get a notification and a card in your feed, and
> Join opens Field Kit already set up for the night — nothing records until you press Start. The
> lead can hold up a QR code so anyone who isn't registered can ask to join.
>
> Record as many sessions as you like and send them to the group that night or days later; each
> one shows whether it's been sent.
>
> Times now show where you are, with a Local time / My time switch on events, tours and
> investigations. Clients can see the visits on their case, and a case still being worked no
> longer shows as closed.
>
> Field Kit is louder and fuller: video records for the whole session and plays with its sound,
> photos appear in the replay, Watch for motion marks movement, and Find public sessions shows
> what others recorded nearby. Plus fixes throughout, including links on iPad that now open the
> right page.

**Promotional text** (optional, top of the page — can be changed any time without review): for
example *"New: launch a session for your whole group, and everyone joins with one tap."*

### 5.3 Screenshots

The 1.0.3 set (`Ben.iOS/screenshots-1.0.3/`) is still accurate — nothing on those screens changed —
so you can leave the slots as they are. Screenshots only *must* change when the screens they show
changed. If you want the new feature in the gallery, say so and I'll capture a proper set
(notification, feed card, the lead's page with the QR code) at the store's exact sizes.

### 5.4 Choose the build

In the **Build** section click **+** (or *Select a build before you submit your app*) and pick
**1.1.0 (8)**. It appears only after processing has finished and only because the page says 1.1.0.
If a question about **export compliance** appears, the answer is **No** (the app uses no
proprietary encryption; `ITSAppUsesNonExemptEncryption` is already false in the build).

### 5.5 App Review Information

- **Sign-in required:** Yes. User **apple@apple.com**, and its password (not written here — this
  repository is public).
- **Contact:** your name, phone and email.
- **Notes:** paste §6 in full.

### 5.6 App Privacy — add Device ID

The app now sends the phone's **push token** to our server so it can be notified. Apple's *Device
ID* category covers "other device-level IDs", so it is declared — Ben's decision, 2026-09-29. The
build already says so: `IsHaunted/PrivacyInfo.xcprivacy` has the entry (checked in a Release build).
The App Store page has to say the same thing, and that is yours to click:

1. App Store Connect → **IsHaunted** → **App Privacy** (left column, under *General*).
2. Next to *Data Types*, click **Edit**.
3. Under **Identifiers**, tick **Device ID** → **Next**.
4. On the Device ID page: purpose **App Functionality** only → **Next**; *Is it linked to the
   user's identity?* **Yes** → **Next**; *Used for tracking?* **No** → **Save**.
5. Back on App Privacy, click **Publish** (top right). App Privacy is not part of the version, so
   it takes effect as soon as it is published — do it before you submit.

The privacy policy at ishaunted.com/privacy and the app's About & Privacy now say it too — the
notification address, kept while signed in, removed at sign-out — so the label and the policy
agree. The website line goes live with the §2 deploy.

App Privacy then shows **eight** data types, all linked, none for tracking: Email Address, Name,
User ID, **Device ID**, Precise Location, Photos or Videos, Audio Data, Other User Content.

### 5.7 Release and submit

1. **Version Release** (bottom of the page): *Manually release this version* if you want to press
   the button yourself when it is approved; *Automatically release* otherwise.
2. **Add for Review** (top right) → **Submit to App Review**.
3. Status goes to *Waiting for Review*, then *In Review*, then *Pending Developer Release* or
   *Ready for Distribution*. Apple emails at each step.

**While it is in review:** keep production up and deployed (§2), keep apple@apple.com working and
on the "App Review night" investigation, and keep the feed switched on.

---

## 6. Notes for App Review — paste into App Review Information → Notes

> IsHaunted is a field tool for paranormal investigation groups. Most of the app works without an
> account: the feed, public events and the entire Field Kit are usable signed out.
>
> **Field Kit** is the core feature and needs no account: Field Kit tab → Start a session. It asks
> for microphone, motion, location and camera permission; all are optional and a session runs
> without them, with fewer channels. A finished session can be exported as one .ben file and opens
> in the app on any phone.
>
> **New in 1.1.0 — starting a group session.** A tour guide, event organiser or investigation lead
> can start a session for everybody registered. To see it, sign in with the demo account below:
> Field Kit → "Launch a session for your group" → Launch next to "App Review night". Registered
> people receive a push notification and a card in their feed; tapping either opens Field Kit ready
> to record, and nothing records until they press Start. The launch page shows a QR code that
> someone not registered can scan to ask to join.
>
> **Demo account:** apple@apple.com — password in the sign-in fields of this form. A member of
> Paranormal365 and the lead of "App Review night", with cases to open and a hosted event under
> Profile → What I'm going to.
>
> **Push notifications** are used only to tell registered people that their group's session has
> started, to tell a lead that someone asked to join, and to tell that person they were let in.
> Permission is asked when it is useful (a seat on a walk, a booking, something coming up), never
> at first launch. There is no marketing or promotional use. Everything works with notifications
> turned off — the same session appears in the feed and in Field Kit.
>
> **Answers to the questions from earlier reviews (Guideline 2.1):**
>
> 1. **Device used for testing:** iPhone 15 Pro Max on iOS 26 (physical device); iPhone 17 Pro Max,
>    iPhone 17 Pro and iPad Pro 13-inch simulators for the automated suite.
> 2. **Third-party SDKs or analytics:** none. Every framework the app links is Apple's.
> 3. **In-app purchases, subscriptions or links to purchase:** none.
> 4. **AI or machine-learning services:** photos and video posted to the feed or an event's room are
>    screened by a model on our own server; voice notes are transcribed on the device by Apple's
>    Speech framework; pass codes are read on the device by VisionKit. No third-party AI provider.
> 5. **Authentication:** email and password on our own server, and Sign in with Apple.
> 6. **Location and mapping:** stamped on field-session readings on the device and sent only when
>    the person sends the session. Maps are Apple's MapKit.
> 7. **Where things are:** on iPhone the tab bar holds Feed, My Cases, Investigations, Field Kit and
>    Profile; public events are under Profile. On iPad the sidebar shows all of them.
> 8. **Notifications:** push, only as described above; local reminders for a seat, as before.
>
> **Privacy** is stated in-app at Profile → About & Privacy, reachable without signing in, and in
> full at https://ishaunted.com/privacy.

---

## 7. If it is rejected

The letter is in App Store Connect → 1.1.0 → **App Review** → *Resolution Center*.

- **Information or metadata only** (a question, a screenshot, a wording point): answer in Resolution
  Center or fix the page, then **Add for Review** again with the **same build**.
- **A code problem:** fix it, set `CURRENT_PROJECT_VERSION` to **9** (the version stays 1.1.0),
  archive and upload as in §4 with `(9)` in the names, then on the 1.1.0 page remove build 8 in
  *Build*, choose **1.1.0 (9)**, reply in Resolution Center saying what changed, and **Add for
  Review**. Never reuse 8.
- **Likeliest questions, answered in §6 already:** "we could not find the new feature" (the
  "App Review night" investigation — make sure it is still running); "what are the push
  notifications for" (Guideline 4.5.4: not required, not marketing).

---

## 8. Checklist

- [x] `MARKETING_VERSION` 1.1.0, `CURRENT_PROJECT_VERSION` 8 — app, Share extension and UI tests
- [x] No third-party frameworks or packages (re-checked 2026-09-29; zero remote package references)
- [x] Server suite, BenKit, iPhone and iPad UI suites green at `262e1646`; three-phone role-play passed twice
- [x] Help, change logs, screenshots and product PDFs updated for 1.1.0
- [x] *Device ID* declared in the build's privacy manifest (§5.6), and said in plain words in the
      website's privacy policy and the app's About & Privacy (Guideline 5.1.1: the policy names
      what is collected) — the policy change goes live with the §2 deploy
- [ ] *Device ID* added and published in App Store Connect → App Privacy (§5.6)
- [ ] Migrations applied with `dotnet ef database update`, then production deployed at `407713e4` or later (§2.1)
- [ ] APNs key on the server, `Apns` settings added, API restarted (§2.2), checks pass (§2.3)
- [ ] "App Review night" set up with apple@apple.com as Lead, running 14 days (§2.4)
- [x] Archived and uploaded 1.1.0 (8) (§4) — 09/29 from Xcode, after signing in again; Apple processing
- [ ] Tried from TestFlight on a real phone — a push arrives and opens the session (§3)
- [ ] Version 1.1.0 made, What's New pasted, build 8 chosen, review notes pasted (§5)
- [ ] Submitted for review
- [ ] Approved and released; remove "App Review night"
