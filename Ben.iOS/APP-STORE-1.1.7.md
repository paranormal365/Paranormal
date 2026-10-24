# App Store update: IsHaunted 1.1.7 (build 18)

Started 10/23/2026 on branch `release/ios-1.1.7`, from develop 58c86ef1. **Submitted for review 10/24/2026, build 18** (Ben).

1.1.7 brings one thing: asking an event's organizers a question before the event, without a booking.

| | |
|---|---|
| Version | **1.1.7** (`MARKETING_VERSION`) |
| Build | **18** (`CURRENT_PROJECT_VERSION`). 1.1.6 was 17. |
| Replaces | 1.1.6 (17), approved by 10/24/2026 |
| Server work first | **Deploy the site from master 58c86ef1 or later first.** Before that, the server never offers the conversation to anybody without a booking, and the button doesn't appear. |
| Screenshots | **Unchanged.** |
| Help | `APP-STORE-1.1.7-HELP.md`, when it's live. |
| Checks | `scripts/build.sh` builds, `scripts/warnings.sh` reports **no warnings** (10/23). |

## 1. What changed

On an event's screen, somebody signed in with no booking sees **Ask the organizers a question** until the
event starts. It opens the same conversation guests have. Once the event starts, they can read the answer
and can't write again. The organizers see beside each message whether the person is confirmed, signed up or
not signed up (on the website). See `ProjectNotes/branches/ask-organizers-before-event.md`.

## 2. Before submitting: try it on a phone

1. Sign in as somebody with no booking at an upcoming event.
2. Events → the event → **Ask the organizers a question** → write something → **Send**.
3. On the website, the organizer's **Guest messages** shows it with *Not signed up*.

## 3. Ship it

1. `scripts/build.sh`, then `scripts/warnings.sh`. Fix anything it lists.
2. Product → **Archive**. In Organizer, choose **IsHaunted 1.1.7 (18)** → **Distribute App** → App Store Connect → **Upload**.
3. App Store Connect → IsHaunted → **+ Version** → **1.1.7**. Keep the screenshots, subtitle and description.
4. **What's New in This Version**:

   > Ask an event's organizers a question before the event, even if you haven't booked yet.

5. Select build **18**, then **Add for Review**.
6. Add ` · ` and the date to the `## 1.1.7` heading in `Ben.Web.Services/Changelog/Content/ios.md`.

If Apple rejects it for code, the next build is **19**.
