# Feature history — one document per branch

**This is where `README-*.md` lives.** Every feature and phase branch gets one, written when the
branch is created: what is being built, why it is being built that way, what was decided along the
route and what was deliberately left out. They are the record of reasoning that a commit message is
too short for and a code comment is in the wrong place for.

They used to sit in the repository root. By 2026-09-12 there were 118 of them there, against about
thirty real directories, so the first thing anybody saw on opening the repository was two screens of
branch notes and the actual project underneath. Four had already been moved here by hand; the rest
followed.

## The convention

- **One per branch**, named `README-<what-it-is>.md` — the branch's own name, or the item number and
  a short phrase: `README-hosted-events-235.md`, `README-verify-a-new-email.md`.
- **Written at the start**, not at the end. It is the plan of record while the branch is open, and
  the history once it merges. A plan written afterwards is a summary, and summaries leave out the
  decisions that turned out to matter.
- **Say what was rejected**, and why. The next person's first question is almost always "why not the
  obvious thing", and the answer is rarely in the code.
- **Say what is NOT built**, plainly. A branch that ships two thirds of a feature is fine; one that
  reads as though it shipped all of it is not.

## Finding one

They are all in this folder, named as they always were, so a search for the filename still finds it:

```bash
ls ProjectNotes/FeatureHistory/ | grep -i tour
grep -rl "some phrase" ProjectNotes/FeatureHistory/
```

Older documents refer to these by bare filename — `README-phase-118.md`, and so on — in doc
comments and in other notes. Those references are still correct; only the directory changed.

## What lives elsewhere

| Where | What |
|---|---|
| `ProjectNotes/DailyLogs/` | What happened on a given day, across whatever branches were open |
| `ProjectNotes/Future-Improvements.md` | The numbered backlog: ideas, and the record of what each became |
| `ProjectNotes/specs/` | Formats and contracts other people could implement against |
| `Ben.Web.Services/Changelog/Content/` | The **public** changelog rendered at `/changes` — user-facing, and nothing internal |
| the repository root | `README.md`, and nothing else of this kind |
