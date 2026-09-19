# Vendored canvas editor source

The `Ben.Canvas.*` and `Ben.Wasm.Canvas` projects are a **vendored copy** of the separate
MessageEditor repository, where Ben built the case canvas on its own before it joined the site.

| Fact | Value |
|---|---|
| Vendored from | `github.com/paranormal365/MessageEditor`, `main` @ `b3b30b9d6a9a41b0acc419b548097951d7b72c3f` (2026-09-15) |
| Taken | `Messenger/Ben.Canvas.Core`, `Ben.Canvas.Editor`, `Ben.Wasm.Canvas`, `Ben.Canvas.Tests`, `Ben.Canvas.Playwright`, and its `ProjectNotes/README-canvas-editor.md` |
| Placed at | the repository root, one folder per project, as every other project here is — `scripts/deploy-ishaunted.ps1` already looked for `Repo\Ben.Wasm.Canvas` |
| Deliberately not taken | `Ben.Web.Website.Library.Manage.slnx` (its projects live in `Ben.slnx` now) and the repo's own `README.md` |
| Changed on the way in | nothing inside the projects: the source repo's README said the folder moves across unchanged, and it did. Project references are between siblings, so they still resolve. |

**The server half was already here**, built on `feature/canvas-editor` and merged to develop: the
boards API, link unfurling, the published picture, the `features.canvas-editor` switch (off until
Ben turns it on), and the deploy scripts that publish this host to `/editors/canvas/`.

**From this point the copies diverge on purpose.** Work on the canvas happens here; the
MessageEditor repo keeps its own history. A change that needs to travel is a deliberate port, not
a merge — there is no git relationship between the copies. Same arrangement as
[`Ben.Video.VENDORED.md`](Ben.Video.VENDORED.md).
