# Site evaluation 2026-09-06 — Phase 8: documentation, screenshots, PDFs

Branch: `feature/site-eval-phase-8-docs-and-captures`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 8 — Documentation, screenshots, PDFs*.

## Why this phase exists

Phases 1–7 changed screens. Every screenshot in the help, in the product documentation PDF, in the
six persona handover documents and in the two app documents was taken before those changes, so each
one was a picture of a product that no longer exists. The text was updated inside each phase; the
pictures could not be, because they can only be taken once every phase has landed.

**No product code changed here.** Everything below is a capture, a rebuild, or a fix to the
machinery that takes the pictures.

## What was rebuilt

| Artefact | Before | After |
|---|---|---|
| `IsHaunted-Product-Documentation.pdf` | 11.0 MB, built 2026-08-25 | 13.9 MB, 17 documents |
| Six `IsHaunted-Web-*.pdf` | 2026-09-02 | all six, 47 screens between them |
| `IsHaunted-iOS-iPhone.pdf` | 3.82 MB, 16 of 17 sections | 3.92 MB, **22 sections** |
| `IsHaunted-iOS-iPad.pdf` | 4.74 MB, 16 of 17 sections | 5.14 MB, **24 sections** |
| `IsHaunted-Investor-Overview.pdf` | 2.67 MB | 2.94 MB |
| Help screenshots | — | 41 refreshed, dark, on the isolated stack |

The product documentation was **two whole articles out of date**: `using-the-audio-editor.md` and
`your-files.md` were written after the last print and had never appeared in it, and fifteen of the
seventeen articles had changed text.

## Five defects in the capture machinery

Each was found by looking at what came out, not by a failing build. Four of them had been shipping
quietly into documents.

### The app documents were captured against the live site

`DeveloperDocCaptureTests` never passed `-apiBaseURL`, so the app used its shipped address —
**production**. The first page of both documents says every account, case and recording in them is
simulated. It could not have been.

Worse, the two mid-run relaunches rebuilt `launchArguments` from scratch, so even passing the flag
would have been dropped after the first screen. There is now one `baseArguments` every launch uses,
and the default account is the seeded `daniel.park@benco.dev` rather than the live administrator
(which was paired, unworkably, with the seeded client's password).

### A stale simulator session silently outranked the credentials

`SessionStore.signIn` returns immediately unless the app is signed out, and the simulator Keychain
survives a reinstall. So `-autoSignIn` was a no-op over whoever was signed in last, and a whole
capture came out as a different person's account without a word about it. The test now confirms the
account on screen, signs a stale session out and asks again, and **fails loudly** if it ends up as
somebody else. A silent wrong answer became a noisy right one.

Confirming it had to match by **label**: `descendants(...)[key]` matches identifiers only, and the
account row is a plain `LabeledContent` with none — my first version of the check could never have
matched, and reported a good sign-in as a failure while the capture went on working.

### `53-session-review` was reachable all along

`docs/README.md` had said for weeks that the review screen "cannot reliably be reached". It can. The
obvious suspect was item 215, which made a session open *pending* so that `stop-field-session` does
not exist until Start is pressed — but the capture already pressed Start.

What actually blocked it sat five screens earlier. The note composer's field is a `TextField` with a
vertical axis, which XCUITest reports as a **textField**, and the capture asked for `textViews`.
Nothing was typed, Save stayed disabled, and the sheet sat over every control below it including
Stop. **Six sections** went missing that way, with no error anywhere. Fixed, plus a Cancel so a
stuck composer can never cost the rest again.

The five sections that came back: the note as typed, EVP, the sentry panel, the sentry armed, and
the session review itself. They are now written up in `docs/build-ios-documentation.py`.

### ffmpeg wedges, and the editor already knew what to do about it

`Capture_UsingTheVideoEditor` failed twice at the same place: the background render never returned
to Ready, so Export stayed disabled and the last six help screenshots were never taken. The trail it
printed gave it away — `59% → 43% → 22% → 68% → 32% → 78% → 0%`, several renders reporting into one
status — and the toolbar said **"Stuck — reset it"**. The capture now does what the screen says,
once, before giving up. It passed on the next run and all ten video-editor screenshots are current.

That underlying render defect is backlog item 94 and is untouched here.

### The feed clear could not clear the SuperAdmin's own posts

Nobody may report their own post, so `ClearFeedForCaptureAsync` skipped the admin's — and a junk
post reading *"posted from elsewhere #tc2c5c4200"* survived every clear and appeared in three of the
six persona documents. It reports those as the member now.

## Also changed

- **`scripts/seeded-passwords.sh`** — the seeded `BEN_*_PASSWORD` derivation lifted out of
  `run-e2e.sh`, which now sources it. The persona and iOS captures need the same values and were
  each deriving them by hand, which is how one ends up echoed into a log. It reads the gitignored
  dev settings, prints nothing, and works from zsh as well as bash.
- **`docs/README.md`** — the persona and iOS recipes, why `BEN_API_BASE_URL` is not optional, the
  Keychain trap, the UUID-to-name step the export needs, and the investor overview's place in the
  pipeline (its feed shot needs the feed switched on, and the help capture puts that flag back).

## Verification

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 warnings, 0 errors |
| Help captures (`TestCategory=Capture`) | 19 passed, 1 failed, 1 skipped; both re-run green after the fixes |
| Help catalogue and media reference tests | 68 passed, 0 failed |
| Persona captures | 6 of 6 seats, every screen present |
| iOS capture, iPhone 17 Pro | passed, 22 sections |
| iOS capture, iPad Pro 13-inch (M5) | passed, 24 sections |
| BenKit (`Ben.iOS/scripts/test.sh`) | 330 passed, 0 failed |
| Full Playwright suite, fresh `IsHauntedDb_p8b` | 473 passed, 8 failed, 40 skipped of 521 |
| Product documentation HTML | screenshotted and read: 17 documents, 8 September 2026 |

Everything was captured on `IsHauntedDb_p8`, a throwaway database created by `scripts/run-e2e.sh`
and dropped afterwards with its `.uploads-IsHauntedDb_p8` directory. Nothing touched the shared
database or the live site.

### The eighth Playwright failure

Seven of the eight are the pre-existing set this evaluation recorded on master before any of its
work: the case-closed message, the authored CMS page for a signed-out visitor, the impersonation
reload, the org public Cases nav, the group wizard tour, the file-delete dialog and the video editor
page title.

The eighth, `The_leak_warning_fires_before_save_not_after_it`, is **not** on that list and is not
explained. It passes on its own, and it passes run together with `PublishLeakWarningTests` — the
fixture phase 6 already found it colliding with. In the full run the dialog opens, the label reads
the address the test typed, Make Public is ticked, and no warning renders: something earlier in the
run leaves state the advisory then finds nothing to object to. Phase 8 changes no code it touches,
and the last full run before this one (phase 6) had it passing. It is order-dependent, it is
recorded here rather than quietly made green, and it wants a run of its own to pin down.

## Left undone, on purpose

- The **Organizations grid overflows** at 1440 wide and its Actions column is cut off in the member
  and owner documents. That is a site defect, not a capture one, and belongs to whoever picks up the
  grid rather than to a documentation phase.
- `11-composer`, `23-case-reports`, `48-evp-recording`, `54-export`, `55-publish-to-archive` and
  `73-about` are still unreached on one or both devices. None of them is a section the builders ask
  for, so no document has a hole; they are simply screens the walk does not open yet.
- The **feed flag** has to be switched on by hand before an iOS capture. The help capture restores
  it to whatever it found, which is correct for the help capture and inconvenient for everything
  that runs after it.
