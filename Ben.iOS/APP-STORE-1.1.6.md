# App Store update: IsHaunted 1.1.6 (build 17)

Started 10/23/2026 on branch `release/ios-1.1.6`, from master f5481e26. **Submitted for review 10/23/2026, build 17** (Ben).

1.1.6 fixes one thing: a session's sound was lost if a setting changed after Start.

| | |
|---|---|
| Version | **1.1.6** (`MARKETING_VERSION`) |
| Build | **17** (`CURRENT_PROJECT_VERSION`). 1.1.5 was 16. |
| Replaces | 1.1.5 (16), released 10/23/2026 |
| Server work first | **None owed.** No server change. |
| Screenshots | **Unchanged.** No screen changed. |
| Help | **None owed.** The help never said the sound stopped; it is now true as written. |
| Checks | `scripts/build.sh` builds, `scripts/warnings.sh` reports **no warnings**, BenKit 625 tests pass, Field Kit UI suites 22 of 22 on iPhone 18 Pro (10/23). |

## 1. What changed

Ben's first 1.1.5 session (10/23, 7:48 AM) had a moving sound reading and no sound file. Turning Watch
for Motion on or off, switching a channel, or arming the sentry after Start restarted the sensors, and
that closed the sound recording. At Stop the empty file was thrown away. See
`ProjectNotes/branches/ios-session-sound-kept.md`.

## 2. Before submitting: try it on a phone

Install the build on your iPhone (Xcode ▶ with the phone selected, or TestFlight after the upload):

1. Start a session with **Video** on, and press **Start**.
2. After a few seconds, turn **Watch for motion** on or off.
3. Talk for a few seconds, then **Stop**.
4. The session's recordings list **audio-001.m4a**, and it plays your voice.

## 3. Ship it

1. **Before archiving:** `scripts/build.sh`, then `scripts/warnings.sh`. Fix anything it lists.
2. Product → **Archive** on the IsHaunted scheme. In Organizer, choose **IsHaunted 1.1.6 (17)**, then
   **Distribute App** → App Store Connect → **Upload**. Leave "Manage version and build number" **off**.
3. App Store Connect → IsHaunted → **+ Version** → **1.1.6**. Keep the screenshots, subtitle and description.
4. **What's New in This Version**:

   > Fixed: a session's sound recording stopped if you changed a setting partway through, such as
   > turning Watch for Motion on or off. The sound now keeps recording the whole session.

5. Select build **17**, then **Add for Review**.
6. Add ` · ` and the date to the `## 1.1.6` heading in `Ben.Web.Services/Changelog/Content/ios.md`.

If Apple rejects it for code, the next build is **18**. An uploaded build number is never reused.
