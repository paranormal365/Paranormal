# feature/ios-duo

Ben, 10/15/2026: get 1.1.4 ready for the iPhone Duo (on sale 10/23) before it ships.

## How the app behaves on the Duo

Tested in the iPhone Duo simulator (Xcode 27.2 beta 3, iOS 27.2), folded and open.

- **Folded** (466 × 678 points) it is a phone: the tab bar, here drawn by iOS as a rail down the right edge.
- **Open** (951 × 669 points) it reports regular width, so `RootShell` shows the iPad sidebar. The app
  chooses by width, not by device, so this needed no code.
- **Folding and unfolding** keeps you on the same screen and the same tab, both ways.
- **A recording survives the fold.** A session recording folded carried on when opened, and one started
  open carried on when folded: the timer and reading count kept counting, nothing restarted.

## Fixed

- **The live session's two columns were too narrow when open.** The open Duo is regular width, but with
  the sidebar showing the session pane is about 630 points, and the iPad's two columns there broke the run
  time onto two lines and wrapped every button. `LiveSessionView` now uses two columns only when the pane
  is at least 760 points wide; below that it is the phone's single column, kept to 560 points wide.

- **The sidebar took a third of the open screen** (Ben, 10/15). At the iPad's usual 320 points it left the
  page 630 of the Duo's 951. When the whole window is under 1,000 points wide, `RootShell` now gives the
  sidebar 236 points, just wide enough for "Investigations" and "Notifications" on one line. A full-size
  iPad (1,024 points or wider) keeps its usual sidebar. The sidebar button at its top still hides it
  altogether.

- **A ☰ menu instead of a standing sidebar** (Ben, 10/15: "maybe a hamburger-style approach?"). When the
  window is under 1,000 points wide (the Duo opened), the menu is hidden and the ☰ button opens it over the
  page; choosing a section closes it. A full iPad keeps its sidebar.

## The investigation column (Ben, 10/15)

"The hamburger should manage getting to other parts of the app, the controls along side during the
Paranormal Platform running should all be to control their investigation." On the Duo opened, iOS puts a
screen's toolbar buttons in a column at the right edge, so while a session runs the column holds (top to
bottom): Mark, Log an experience, Ask and listen, Note, Change room, Set/reset base, Flashlight (on phones
with a light), and Levels. Those buttons leave the page. Stop stays alone in the bottom bar.

- **Log an experience:** Cold spot, Touched, Heard a voice, Heard a sound, Saw something, Smelled something,
  Felt uneasy, or Something else (opens the note). A mark at that moment with those words.
- **Ask and listen** (`CallAndResponse` in `InvestigationRail.swift`): press while asking; press again to
  file the question and wait for an answer (the same EVP question and wait marks the EVP screen makes);
  press again for the next question; hold to stop listening. With "Type my questions as I ask them" on
  (Levels, on by default), on-device dictation types the question while it is asked and stops when listening
  starts. A strip across the top shows what is being typed, or how long the room has had to answer.
- Folded, and on iPad, the session screen is unchanged.

### Found by the walk-through and fixed (10/15)

`IsHauntedUITests/DuoInvestigationColumnTests` drives a recorded session through every control in the
column on the opened Duo. Its own screenshots come out black on the beta's two-screen simulator, so the
frames are cut from a recording of the inner screen (`simctl io recordVideo --display=3`) at each step's
time.

- **A press left no trace.** Mark and the experience list changed nothing on screen. A short line now says
  what happened ("Marked at 00:00:06", "Logged: Cold spot", "Base set"), over the reading count in the
  bottom bar so it never covers the run time.
- **Holding the microphone to stop listening didn't work** in the column, and nobody would find it.
  "Done listening" in the listening strip ends the wait instead.
- **Dictation switched the shared audio off when it stopped**, which would also have cut the session's
  own sound recording. It now leaves it on while the session's microphone is in use.
- **A second press could land while the first was still being handled**; presses are now taken one at a
  time.
- The simulator ignores a request to turn the opened Duo upright, so portrait is untested until a real
  phone or Device Hub.

## Readable menus (Ben, 10/15)

The hand button's list was a confirmation dialog, drawn by iOS in violet on translucent gray: unreadable
on the opened Duo. It is now a menu (system ink, an icon per experience). Every other confirmation dialog
and alert takes the system's ink too (`UIAlertController` appearance in `SignalAppearance`); destructive
choices stay red.

## Adding details to a mark (Ben, 10/15)

"Website is readonly. The app is where it should be able to be edited before submission if they marked
any events." and "Only allow the text to be added to the details for marks during the session... nothing
else can be added."

- `FieldMarker.details` / `detailsAddedAt`; `FieldSessionStore.addDetails` refuses a sent session
  (`alreadySent`) and a copy from someone else's file or a public place (`notYours`).
- The readings log is never rewritten. The package carries a top-level `mark_details` array beside the
  readings (`at`, `marker`, `details`, `added_at`), sealed with the rest at Send; the schema allows extra
  root fields. Opening a `.ben` reads them back.
- The review shows "Added after the session: …" under the mark and offers **Add details** until it's sent.
- The website's timeline builder attaches them to their mark by moment and kind; the engine player shows
  them read-only.
- Tests: `MarkDetailsTests` (5) and `FieldSessionTimelineBuilderTests.Details_added_after_the_session_reach_their_mark_and_nothing_else`.

## Found and fixed along the way

- The ☰ change set the iPad's sidebar to `.automatic`, which hid it on an upright iPad; it is `.all` now.
- The capture tests typed a session's name after the address the app fills in ("…north wallSprings");
  `AppNavigator.replaceText` taps the field's end and clears it first.

## App Store pages

`Ben.iOS/store-pages/` (`build.py`, `pages.py`, `events.py`) and `Ben.iOS/scripts/capture-store-pages.sh`
build the main page and seven custom product pages for iPhone, iPhone Duo and iPad, with previews, the
In-App Events and the featuring nomination, into `screenshots-1.1.4/`. The capture's raw frames and
recordings stay out of git.

## Still to do

- Duo App Store screenshots (outer 1398 × 2034, inner 2853 × 2007), captured on the simulator.
- The archive must be built with a release Xcode 27.1 (RC or final), not the beta.

## Simulator notes

- Folding is done in Device Hub (the beta's, in `~/Downloads/Xcode-beta.app/Contents/Applications`),
  bottom-right toolbar: closed, half-open, fully open. `simctl` has no fold command.
- On the beta simulator the first tap after a pause is often dropped; tap, check, then tap again.
