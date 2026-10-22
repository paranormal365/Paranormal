# App Store update: IsHaunted 1.1.5 (build 16)

Started 10/22/2026 on branch `release/ios-1.1.5`, from master 94b83613. **Submitted for review 10/22/2026, build 16** (Ben).

1.1.5 brings two things:
- events: their results, the walk's room, writing to the organizers, and class attendance at the door;
- the Paranormal Platform: asking questions in three clear steps, and sound that keeps recording.

| | |
|---|---|
| Version | **1.1.5** (`MARKETING_VERSION`) |
| Build | **16** (`CURRENT_PROJECT_VERSION`). The project was at 15 for 1.1.4, so 16 is clear of every number ever uploaded. |
| Replaces | 1.1.4 (15), released 10/16/2026 |
| Server work first | **None owed.** Checked 10/22 against api.ishaunted.com: the results, organizer-thread and walk-room endpoints answer there. |
| Screenshots | **Unchanged.** No store frame shows a screen that changed: the live session frame shows the readings, not the question buttons. |
| Checks | `scripts/build.sh` builds, `scripts/warnings.sh` reports **no warnings**, `scripts/test.sh`: 621 tests pass (10/22, Xcode 27.1). |

## 1. What changed

The full list is under **1.1.5** in `Ben.Web.Services/Changelog/Content/ios.md`. In short:

- **Events.** An event shows its **Results**. A ghost walk you were checked in on has **The walk's room**.
  Posts from organizers, guides and the venue carry a badge. **Write to the organizers** from an event you
  have a booking at. The door has **Who's at the classes**. **May be shown with the results** lets the
  organizers show a session you sent them (off unless you turn it on). Menus: the credit carries the
  IsHaunted mark, and alcohol is marked **21+**.
- **Asking questions** is one button in three steps (**Ask a question** → **Done asking — listening** →
  **Ask the next question**), with the live readings on screen; type the question as you ask it, or cancel.
- **Sound.** It keeps recording while video records and while a question is written down. A recording's
  length is the sound it holds, so playback lines up with the readings. The sound reading follows the room
  (a quiet room reads around -80 dB), and no reading shows "-0".
- **Watch for Motion** shows **Motion photos on**, how many it has taken, and **Photo taken** each time.
  Photos make no shutter sound wherever the law allows it.

## 2. Ship it

1. **Before archiving:** `scripts/build.sh`, then `scripts/warnings.sh`. Fix anything it lists.
2. Product → **Archive** on the IsHaunted scheme. In Organizer, choose **IsHaunted 1.1.5 (16)**, then
   **Distribute App** → App Store Connect → **Upload**. Leave "Manage version and build number" **off**.
3. App Store Connect → IsHaunted → **+ Version** → **1.1.5**. Keep the screenshots, subtitle and description.
4. **What's New in This Version**:

   > Events: see an event's results, keep in touch with your ghost walk in the walk's room, and write to
   > the organizers of an event you've booked. Organizers can mark who came to each class at the door.
   >
   > Asking questions is now one button in three steps — ask, listen, ask the next — with the live
   > readings on screen the whole time. Sound keeps recording while you film or write a question down,
   > and the sound reading now follows a quiet room.
   >
   > Watch for Motion tells you when it takes a photo, and photos no longer make the shutter sound
   > where the law allows.

5. Select build **16**, then **Add for Review**.

If Apple rejects it for code, the next build is **17**. An uploaded build number is never reused.

## 3. When it's live

Follow `APP-STORE-1.1.5-HELP.md`: add the three-step asking paragraph to the help, recapture its EVP
screenshot, and rebuild the product PDF and app guides. The events help (the walk's room, writing to the
organizers) is already on the site.
