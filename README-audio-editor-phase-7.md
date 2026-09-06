# Audio editor phase 7 — the help

Branch: `feature/audio-editor-phase-7-help`, cut from `master` after phase 5a merged.

## Why this is not optional

The audio editor had **no help article at all**, and `your-files.md` never mentioned audio. A
person who opened a recording found a spectrogram, three sensitivities of EVP scan and eight
destructive edits with nothing anywhere explaining what any of them were for, or the one thing that
matters most: that nothing here ever changes the recording.

Ben's own standing rule is that a user-visible feature is not done until the help is updated, which
means phases 2 through 5 were not done.

## What is here

`using-the-audio-editor.md` — how to select a stretch, what the spectrogram's three controls
actually trade against each other, what the EVP scan is and is not (it proposes; it never decides),
how to make a clip, what each of the eight edits does, the half-hour edit ceiling and why it exists,
the region explorer, and the case mixer. Four screenshots, captured on a clean isolated database so
the mixer's clip picker is not fifty copies of a test upload.

Help links from the editor's toolbar and the mixer's heading, and a pointer from **Your Files**.

## Two things the screenshots caught

- **The editor's toolbar bar left two bands of empty space** in a fullscreen modal: it wraps onto
  two lines, and a wrapped flex container spreads its lines through whatever height it has.
  `align-content: flex-start` fixes the ordinary case. With several panels open the gap comes back,
  which is recorded in the audit rather than guessed at here.
- **A caption claimed something the picture did not show.** The first spectrogram shot was Jet on a
  linear axis, which puts a quiet recording's whole voice band in one dark strip; the caption
  described bands that were not visible. The capture uses Viridis and the mel scale now, and the
  caption says what is actually there.

## Verification

`dotnet build Ben.slnx` clean, `Ben.Web.Tests` green including the help catalogue, media-reference
and link-target guards. The screenshots were re-captured and looked at.
