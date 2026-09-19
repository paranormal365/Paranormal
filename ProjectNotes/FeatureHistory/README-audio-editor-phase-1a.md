# Audio editor phase 1a — the editor gets its room back

Branch: `feature/audio-editor-phase-1a-size-and-close`, cut from `master` after phase 0 merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (P, A, I).

## Why this comes before everything else

The phase 0 walk found that "Open Full View" opens the editor in a 300 pixel dialog. Until that is
fixed, no other full-view finding can be judged honestly — the spectrogram controls, the EQ panel
and the edit panel were all being verified through a keyhole, and the walk had to record three of
them as unverifiable at that size.

One commit caused it. `bde4e03f` (2026-08-18, "wave C — the Manage area") replaced the editor's
`TelerikWindow Width="92vw" Height="92vh" VisibleChanged="@OnModalVisibleChanged"` with
`BenModal Size="sm"` and no `VisibleChanged`. So the same change made the editor tiny **and** made
closing it not stick, which is why `OnModalVisibleChanged` has been dead code ever since.

## What this phase does

- Restores the size (`Size="fullscreen"`, closest to the original 92vw × 92vh) and the close
  wiring, so the reset handler runs again and the modal stays closed (P, A).
- Fixes the same defect in `WsRegionExplorer`, which bound `@bind-Visible` to its own parameter
  and so never told its parent it had closed (I).
- Lets a clip saved from a nested sub-region reach the list that should show it (I).
- Corrects the reset handler's disagreement with its own field default for spectrogram labels.

## Verification

Re-run the phase 0 walk on the isolated stack and compare the screenshots. The findings the walk
could not judge at 300 pixels (C, D, E, G) are re-judged from the re-walk's evidence, and the
verdicts are folded back into the audit document.
