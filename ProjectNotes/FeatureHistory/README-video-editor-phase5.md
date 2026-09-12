# Video editor phase 5 — persistence integrity and not losing work

Branch: `feature/video-editor-phase5-persistence`.
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 5.
Follows `README-video-editor-phase4.md`.

Phase 3 made the export match the timeline; phase 4 made the editing session survive its own
engine. This phase is about the work surviving the session.

## Slice 1 — the file holds what the model holds (persistence-1/2/8, motion-1, audio-7, callouts-1)

The round-trip test could not catch the bug it existed for. It built a project file by hand,
listing the fields it expected, so a field the mapper had never learned about was equally absent
from the test — the two agreed with each other and disagreed with the model. It was green while all
of this was being dropped on every save:

| dropped | what that looked like |
|---|---|
| Per-axis scale and rotation on a keyframe | Stretching a layer on one axis or rotating it came back uniform and upright |
| A callout's text alignment, wrap, shadow and padding | The shape survived; everything about the words in it did not |
| A clip's mute and has-audio state, and both halves of a link | "Separate Audio" reopened with the picture unmuted and the audio track playing too, doubling every word |
| A callout's OPFS extension | A custom shape's asset could not be found |

Replaced by `ProjectRoundTripParityTests`, which reflects over the model, gives every settable
property a distinctive value, and names anything that comes back different. Properties that
genuinely do not survive are listed with reasons, so a new one fails closed. It found four more
gaps on its first run, including the re-link hint being saved from the session-local filesystem
path — so reopening a project offered a name like `vid_3f2a91c4….mp4`.

There were also four copies of the JSON settings, and the site's two pages had no string-enum
converter, so neither could read a project the editor had written. One `ProjectSerializer` now,
plus a `Parse` that refuses JSON that is not a project — which used to open successfully as an
empty timeline.

## Slice 2 — saving tells the truth (persistence-4/5/6/7/9/11/19/20)

`setItem` returns false when the quota is full or storage is blocked; the C# discarded it and
reported success. Also: keyframe edits did not mark the project dirty, renaming baked the `*` into
the name, Save was disabled without a video clip, Open and Import replaced unsaved work without
asking, Import did not remount media, the import input was never cleared, and the project grid
sorted dates and sizes as text.

## Slice 3 — autosave and the unload guard (F9)

Nothing wrote anything unless somebody chose Save, and nothing asked before the page went away. The
project now writes itself two seconds after editing stops, with a last-chance write on `pagehide`,
and the browser prompt is registered only while there is something to lose — unsaved edits, a
pending write, or a render, which lives in the tab and dies with it.

## Slice 4 — media stops accumulating (media-2, persistence-12)

Nothing ever freed a source. The editor reconciles on startup and deletes stored media no project
refers to, and the media panel shows how much storage is in use. Reconciliation rather than
reference counting, because a removed clip is one keystroke from coming back. The load-bearing part
is the refusal: if the project list could not be read, every file looks unreferenced, so it
declines rather than deleting everything.

## Found on screen, not by the tests

Three, and one of them is the most serious thing in this phase.

- **No placed clip's media had ever come back after a reload.** The stored file is named after
  whichever clip first imported it, and placing from the bin makes a copy with an id of its own —
  so a placed clip's media sits under its bin entry's id, and the restore looked only under the
  clip's own. Since the media bin was introduced, reopening a project restored the timeline, left
  the file sitting right there in storage, and marked every clip missing. The bin link exists
  exactly for this; it is now followed, and the bin's own entries are remounted too.
- **The engine was never started for a restored project.** Media can only be written into a running
  engine, and on a reload it is not running — so the project came back with everything missing and
  nothing asking anybody to press Initialize. A project with clips in it is reason enough to start
  it (persistence-16).
- **A Razor comment inside a component's attribute list compiles and then throws at render.**
  Blazor reads it as an attribute name and reports that the component "does not have a property
  matching the name" followed by the whole comment; the editor showed the unhandled-error bar and
  nothing else. A well-meant explanatory note took the whole editor down.
  `RazorMarkupGuardTests` now scans for it, because the build cannot.
- And a race I introduced: two callers now ask whether storage is usable, and the check cached the
  module before the answer, so the second returned "no" and the editor announced that this browser
  cannot keep your media — on a browser that plainly can.

## Verified on screen

Standalone host at 1440x900, browser storage cleared first.

- Import one clip, touch nothing else, wait. The project is written to storage on its own:
  `bv-proj-<id>`, `bv-proj-active` and `bv-proj-index` all present, and the unsaved mark gone.
- Reload. The project name, the timeline clip and the media bin all come back, the engine starts
  itself, the media remounts (`IsMediaMissing` false), and the Working Window rebuilds and plays
  the footage. Before this phase a reload opened an empty editor.
- The media panel header reads **427 KB**, with the tooltip naming the browser's 6.2 GB allowance
  and explaining that unused media is cleared at startup.

## Not done in this phase

- **A `SourceRegistry` refcounting sources against the undo stack.** The plan named one. The
  reconciling sweep gets the same space back without needing to be right about undo, and a refcount
  that is wrong deletes media a person could still have brought back. Worth doing only if the
  startup sweep proves too slow or too late.
- **`ProjectFile.Export` settings, and re-resolving clip-art control-point definitions from the
  asset on load** (export-18, part of callouts-1). Both are additive DTO work the parity test will
  now demand as soon as the model carries them.
