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

## Still to do

- Duo App Store screenshots (outer 1398 × 2034, inner 2853 × 2007), captured on the simulator.
- The archive must be built with a release Xcode 27.1 (RC or final), not the beta.

## Simulator notes

- Folding is done in Device Hub (the beta's, in `~/Downloads/Xcode-beta.app/Contents/Applications`),
  bottom-right toolbar: closed, half-open, fully open. `simctl` has no fold command.
- On the beta simulator the first tap after a pause is often dropped; tap, check, then tap again.
