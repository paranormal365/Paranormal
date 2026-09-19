# App Store submission — IsHaunted

Everything App Store Connect asks for, and the state of each requirement as of 08/28/2026.
Bundle id `com.ishaunted.ios`, version 0.1.0 (build 1), iPhone and iPad, iOS 18.0 minimum.

---

## 1. Listing copy

**Name:** IsHaunted

**Subtitle (30 char max):** `Paranormal field investigation` *(30)*

**Promotional text (170 max):**
> Run an investigation from your pocket. Record a field session with the sensors you already
> carry, capture what happens as it happens, and file it straight onto the case.

**Description:**

> IsHaunted is the field companion for paranormal investigators.
>
> Start a session and the app records from the sensors already in your phone — magnetometer,
> motion, ambient sound level — alongside the photos, audio and notes you take while you work.
> Everything is stamped with when and where it happened, so a spike in the cellar is never
> confused with one in the hall.
>
> **In the field**
> • Live field sessions with a running readings log
> • Photo, video and audio capture into the session
> • Voice notes, transcribed on the device
> • Sentry mode — leave the phone watching a room and have it flag what changed
> • Replay a finished session as a timeline
>
> **Back at the case**
> • Upload a session to your group and attach it to the case it belongs to
> • Case files, reports, timelines and messages
> • Investigations, rosters and schedules
> • Public events and ghost-walk tours near you
>
> **On a ghost walk**
> • Offer what you photographed to the group running the event
> • Keep your own copy of everything you offered, whatever they decide
> • Add it to the public archive of the place it was taken at
>
> **The archive**
> • Publish your readings to a location's public record
> • See every session anybody has recorded there — one person's readings are an anecdote,
>   eleven people's are evidence, or a demonstrated absence of it
>
> **Who it is for**
> Paranormal investigation groups and their members; guides who run public ghost walks; and
> clients who have asked a group to look at their property. Anyone can browse the public feed
> and upcoming events without an account.
>
> **What we do with your data**
> Nothing you did not give us. No advertising, no analytics service, no tracking across other
> apps, and nothing sold to anyone. Field-session recordings stay on your device unless you
> choose to upload them. Full policy: https://ishaunted.com/privacy
>
> IsHaunted works with a free account. Groups that need more than the free tier subscribe on
> ishaunted.com.

**Keywords (100 char max):**
`paranormal,ghost,investigation,EVP,EMF,haunted,field,evidence,ghost hunt,spirit,tour,case`

**Category:** Primary — Lifestyle. Secondary — Utilities.

**Availability:** United States only at launch (2026-08-30). Deliberate: every real group is in
Tennessee, the discovery surfaces are empty screens elsewhere, and worldwide availability is
worldwide data-rights obligations on day one. Expanding is an instant no-review checkbox edit —
second wave when wanted: Canada, United Kingdom, Ireland, Australia, New Zealand. Price: Free.

**Age rating:** 13+ as computed by the questionnaire (2026-08-30, 171 countries/regions).
Driven by: Horror/Fear themes = infrequent/mild, Social Media = yes, Messaging and Chat = yes,
Declared Age Range API gating = **no** (nothing in the app calls it — do not answer yes to that
one without building the gate). The 13+ floor is what keeps under-13 accounts from downloading
a social app with no age gating, and it matches the privacy policy's under-13 statement.

**URLs**
| Field | Value |
|---|---|
| Privacy policy | `https://ishaunted.com/privacy` |
| Support | `https://ishaunted.com/contact` |
| Marketing | `https://ishaunted.com` |

All three are anonymous. Verified 08/28/2026 — a reviewer with no account can open every one.

---

## 2. App Privacy answers

These must match `IsHaunted/PrivacyInfo.xcprivacy` exactly; a mismatch is a rejection.

**Tracking:** No. Nothing is shared with a data broker and there is no cross-app tracking.
There are no third-party SDKs in the app at all — verified by search, not by memory.

**Data collected — all "Linked to you", none "Used for tracking", all purpose "App Functionality":**

| Type | Why |
|---|---|
| Email address | Your account |
| Name | Display name; optional real name |
| User ID | The account identifier |
| Precise location | Stamped on field-session readings, uploaded only with the session |
| Photos or Videos | Evidence and feed posts you upload |
| Audio data | Session recordings, EVP, voice notes you upload |
| Other user content | Cases, notes, reports, messages, posts |

**Required-reason APIs declared:** `UserDefaults` (CA92.1), file timestamps (C617.1).

---

## 3. Review notes (paste into App Review Information)

> IsHaunted is a tool for paranormal investigation groups. Most of the app works without an
> account — the feed, public events and the whole Field Kit are usable signed out.
>
> **Field Kit** is the core feature and needs no account: Field Kit tab → Start a session.
> It asks for microphone, motion and location permission; all three are optional and the
> session runs without them, with fewer channels.
>
> **What the app does with data** is stated in-app at Profile → About & Privacy, reachable
> without signing in, and in full at https://ishaunted.com/privacy
>
> **Demo account** for the group-side features (cases, investigations, rosters):
> apple@apple.com — password in `demo-credentials.local.md` (gitignored) and in
> App Store Connect's sign-in fields. Verified working against production 08/29,
> member of Paranormal365, rostered on an investigation.
>
> Sign in with Apple is offered alongside email and Microsoft sign-in.

---

## 3b. What changed since the 1.0 submission (2026-08-31)

An update built while 1.0 was in review. Everything here is additive; nothing that a reviewer
exercised on 1.0 behaves differently.

- **Settings → My evidence.** What this account offered at other people's public events, openable
  whatever the organiser decided, and addable to the public archive of the place it was taken at.
  Two independent decisions, shown separately: the organiser curates their own event's gallery,
  the photographer decides about the place's record.
- **The app offers only the sections that apply.** A person investigating alone no longer carries
  a My Cases tab that can never hold anything, or Investigations belonging to groups they have not
  joined. Driven by `GET api/me/surfaces`, which answers "is there anything here?" — permission is
  still decided at every endpoint, unchanged.
- **Retracting an archive publication now requires a paid plan.** Publishing stays free and
  available to anybody. This is the paywall, and the app surfaces the server's own sentence
  explaining it rather than a generic failure.

**For App Review:** the demo account still reaches the group-side features exactly as before. A
reviewer signing in as `apple@apple.com` sees the same cases, investigations and rosters; the
adaptive sections ADD nothing to hide from that account, since it is a group member.

---

## 4. Requirement checklist

### Done

- [x] **App icon** — `IsHaunted/Resources/IsHaunted-iOS26-Button.icon`; confirmed compiled into
      `Assets.car` in the built bundle, not just referenced.
- [x] **Privacy manifest** — `IsHaunted/PrivacyInfo.xcprivacy`; confirmed present in the built
      `.app`. (It is picked up by the synchronized root group; nothing lists it explicitly, so
      if the project ever stops using `PBXFileSystemSynchronizedRootGroup`, check it again.)
- [x] **Privacy policy URL** — `/privacy`, anonymous, live.
- [x] **In-app privacy statement** — Profile → About & Privacy, reachable signed out.
- [x] **What the app does / who it is for** — same screen, and section 1 above.
- [x] **Usage descriptions** — camera, microphone, location-when-in-use, motion, photo library.
      Location wording says the readings can leave the device when you upload a session; the
      earlier wording said "stays on this device", which was not true of an uploaded session.
- [x] **Export compliance** — `ITSAppUsesNonExemptEncryption = false` (HTTPS and Keychain only).
- [x] **Sign in with Apple** — required because Microsoft sign-in is offered. Implemented.
- [x] **HTTPS only in release** — no ATS exceptions beyond local networking; the release build
      refuses any saved base URL that is loopback, private-range or plain http.
- [x] **Points at production** — `https://ishaunted.com/webapi`, confirmed 200 against the live
      host while the un-prefixed path 404s.
- [x] **No developer surface in release** — the API environment picker is `#if DEBUG`.
- [x] **iPhone and iPad** — `TARGETED_DEVICE_FAMILY = "1,2"`.
- [x] **Report objectionable content** — feed posts can be reported; reports reach an admin.
- [x] **Published contact** — `/contact`, no account needed.

### Requirements — all met

- [x] **Account deletion inside the app** (Guideline 5.1.1(v)) — built 08/28/2026. Profile →
      Delete account. `DELETE /api/me` with a typed `DELETE` confirmation the *server* also
      requires; `GET /api/me/closure` is asked first so the screen can explain a refusal instead
      of failing at the button.

      Ben chose **anonymise, and stop an owner first**. Closing an account destroys the identity,
      credentials and contact details and keeps the row, so cases, evidence, reports and messages
      stay with the group that depends on them, attributed to "A former member". A member does
      not own the case files they wrote on their group's behalf, and one person leaving must not
      erase a group's history. An organization's owner is refused until they have handed the group
      over — exactly one owner exists per organization — and is told by name which groups those
      are, because Apple rejects a blocked path that cannot say what to do about it.
      
      External logins go with it, so Sign in with Apple cannot walk back into a closed account; a
      later Apple sign-in creates a new one. `RecordingSignInManager.CanSignInAsync` refuses a
      closed account by any route.
      
      **Needs a migration applied before this works anywhere:**
      ```
      dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi
      ```
      `20260828164309_AddAppUserDateClosed` — one nullable column on `AppUsers`, additive, and
      invisible to the currently deployed build. Not applied here: it is a schema change to the
      database serving the live site, which is Ben's call rather than a side effect of a feature.
      
      Verified end-to-end against a scratch database instead (`IsHauntedDb_closure`, safe to
      drop): an owner is refused by name, a lower-case confirmation is rejected, a non-owner
      account deleted with 204, sign-in afterwards answered 401, and the row came back as
      "A former member" with the address anonymised.

- [x] **Block an abusive user** (Guideline 1.2) — built 08/28/2026. All four obligations now
      stand: filter (NSFW screening + moderation), report, **block**, published contact.

      Blocking is the reader's own act and takes effect immediately, unlike Report which waits
      for a moderator. `POST/DELETE /api/me/blocks/{userId}` + `GET /api/me/blocks`; the feed
      read path excludes blocked authors' posts and replies for that reader (list, ranked page,
      and threads — a blocked author's own thread is NotFound). Blocking severs follows in both
      directions; unblocking does not restore them. Deliberately not gated on feed participation
      or the feed flag — being abused doesn't require standing to post.
      
      In the app: Block on every post's menu (with confirmation; removes their posts from screen
      at once), and Profile → Blocked accounts to review and unblock. The person blocked is not
      told and sees nothing different.
      
      Migration `20260828172652_AddUserBlocks` applied to dev; production gets it with the next
      deploy's `database update`. The WEBSITE has no block UI yet — server filtering is in place,
      so it is a small follow-up, not a launch blocker (the feed is dark on the site anyway).

### Before the first upload

- [x] Enrol in the Apple Developer Program — Ben has this (2026-08-28).
- [x] Screenshots: captured 08/28/2026 by `AppStoreScreenshotTests` on iPhone 17 Pro Max
      (1320×2868) and iPad Pro 13-inch (2064×2752) — five per device in `Ben.iOS/screenshots/`
      (feed, cases, investigations, field kit, events). No profile shot: Debug builds show the
      Developer section there, and debug UI in a screenshot is a rejection. The folder README
      has the refresh procedure, including why `simctl keychain reset` is part of it.
- [ ] **Demo account** — the one item only Ben can do: the reviewer signs into PRODUCTION, so
      the account must exist on ishaunted.com (a member of a group with a case or two to see).
      Create it on the live site, then paste the credentials into the review notes above.
- [x] `MARKETING_VERSION` bumped to 1.0.0 (build 1) 08/28/2026.
