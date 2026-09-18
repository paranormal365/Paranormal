# IsHaunted Case Canvas — acceptance notes

One section per milestone: what can be opened, the commands that prove it, and what is not built yet.
A milestone is ticked only with its date and evidence.

## M1 Core engine — done 2026-09-14

**What exists.** `Ben.Canvas.Core` holds the board format and everything that edits it, with no browser
and no packages: the document model and typed block data, one serializer with additive migrations,
every editing command with undo and redo, live drag and resize sessions, viewport and pinch maths, hit
testing for blocks and curves, snapping, resize handles, connector geometry, the block registry and
options, the keyboard map, address, place and picture detection, the paste classifier and placer, the
message HTML normaliser, persistence policies, the copy catalogue and invariant formatting.

**Proof.**

```
dotnet build Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Ben.Web.Website.Library.Manage.slnx -warnaserror
dotnet test  Z:\_GitHub\VandyBen\Ben.Web.Website.Library.Manage\Ben.Web.Website.Library.Manage.slnx --filter "FullyQualifiedName~Ben.Canvas.Tests"
```

Build: 0 warnings, 0 errors. Tests: 496 passed, 0 failed.

| Folder | Tests |
|---|---|
| Model (model, registry, options, serializer, migrations) | 79 |
| Geometry | 70 |
| Paste (detectors, HTML normaliser, classifier, placer) | 62 |
| Input (key map) | 61 |
| Commands (store, groups, live drag) | 58 |
| Persistence | 24 |
| Formatting | 13 |
| Text | 6 |
| Hosting, Guards, Components (from M0) | 123 |

**Red first.** The tests were shown failing against 27 deliberate breaks, one per step, each restored
byte for byte afterwards (script and table kept with the session evidence). Every break was caught.
Examples: removing the redo clear fails `A_new_command_clears_redo`; skipping message HTML cleaning on
read fails `An_imported_message_loses_its_script_handlers`; a live move that does not bump block
versions fails `A_live_move_bumps_the_version_of_each_dragged_node`; keeping pasted ids fails
`Pasting_the_same_payload_twice_gives_new_ids`.

**Review corrections applied here.**

- R2a: message HTML is normalised by a pure C# allow-list on every read, and unsafe link addresses are
  dropped. No `img`, `figure` or `figcaption` in messages (R23).
- R7: block versions are bumped for every change a block is named in, including live moves, commit and
  cancel; readers use `VersionOf`, which answers 0 for an unnumbered block; loading raises
  `LoadGeneration`.
- R8, R18: compact serializer options for storage and the clipboard, and source-generated metadata for
  the persisted types.

**Deferred, with where it lands.** Keyboard script contract scans and gesture bridge tests: M2. Block
renderer coverage: M3. Paste service, the browser-side sanitiser and the export package: M4.

**Deviation.** Adding an ASP.NET framework reference to Core is refused by the build itself (the
WebAssembly host has no runtime pack for it), so the purity test's red demonstration used a banned
identifier instead.

### Red-first table

| Step | Break | Tests that failed |
|---|---|---|
| M1-02 | Text\CanvasCopy.cs | Nothing_shouts_or_apologises, Sentences_end_with_a_full_stop_or_question_mark |
| M1-03 | Formatting\BcFormatting.cs | A_French_culture_still_writes_a_dot |
| M1-04 | Model\CanvasModel.cs | Palette_accepts_one_to_six_and_nothing_else(key: |
| M1-05 | Model\NodeData.cs | Clone_is_deep(type: |
| M1-06 | Geometry\Viewport.cs | Zooming_about_a_point_keeps_that_world_point_under_the_cursor, The_world_point_under_the_focal_point_does_not_move, Moving_both_fingers_pans_by_the_focal_delta |
| M1-07 | Blocks\BlockRegistry.cs | Default_data_kind_matches_the_type(type:, Minimums_match_the_designed_values(type:, Disabled_blocks_are_not_offered_in_the_palette, Every_node_type_has_a_descriptor |
| M1-08a | Serialization\CanvasSerializer.cs | Enums_are_written_as_names_so_inserting_a_value_cannot_shift_them |
| M1-08b | Serialization\CanvasSerializer.cs | Json_that_is_not_a_board_is_refused(json: |
| M1-09a | Serialization\CanvasDocumentMigrations.cs | An_edge_to_a_missing_node_is_dropped |
| M1-09b (R2a) | Serialization\CanvasDocumentMigrations.cs | An_imported_message_loses_its_script_handlers |
| M1-10 | Commands\CanvasStore.cs | A_new_command_clears_redo |
| M1-11 | Commands\Commands.cs | Undoing_a_move_after_a_resize_keeps_the_new_size |
| M1-12 | Commands\Commands.cs | Removing_a_node_takes_its_edges_and_undo_brings_both_back |
| M1-13 | Geometry\HitTesting.cs | The_front_most_of_two_overlapping_nodes_wins |
| M1-14 | Geometry\Snapping.cs | Snap_returns_the_adjusted_leading_edge_when_the_centre_matches |
| M1-15 | Geometry\Snapping.cs | Dragging_the_right_handle_keeps_the_left_edge_fixed |
| M1-16 | Geometry\EdgeGeometry.cs | A_node_to_the_left_connects_left_to_right, An_explicit_side_is_honoured, A_node_to_the_right_connects_right_to_left, Control_points_are_at_least_40px_out |
| M1-17a | Commands\LiveSessions.cs | A_drag_pushes_one_undo_entry_not_one_per_move, Cancel_restores_every_node, A_live_move_bumps_the_version_of_each_dragged_node, A_drag_that_ends_where_it_started_pushes_nothing |
| M1-17b (R7) | Commands\LiveSessions.cs | A_live_move_bumps_the_version_of_each_dragged_node |
| M1-18 | Input\CanvasKeyMap.cs | Ctrl_Y_is_redo |
| M1-21 | Paste\PasteClassifier.cs | Pasting_the_same_payload_twice_gives_new_ids |
| M1-22 | Commands\CanvasStore.cs | An_asset_referenced_only_by_the_undo_stack_is_kept |
| R2a | Paste\PasteHtmlAllowList.cs | Only_allowed_markup_survives(input:, A_safe_link_keeps_its_address_and_opens_safely |
| M1-01 (retry) | Text\CanvasCopy.cs | The_core_library_has_no_package_or_browser_references |
| M1-17c (retry) | Commands\LiveSessions.cs | Dragging_a_node_out_of_a_group_clears_it, Dropping_a_node_into_a_group_joins_it_in_the_same_undo_step |
| M1-19 (retry) | Paste\Detectors.cs | A_trailing_full_stop_is_not_part_of_the_link |
| M1-20 (retry) | Paste\PasteClassifier.cs | A_lone_url_is_a_link_not_text |

## M2 Desktop board — done 2026-09-14

**What Ben can open.** `http://localhost:5125/`: header with save state, undo, redo, properties and
shortcuts; a rail to add cards, notes, messages, maps, images and links; the dotted board with zoom controls
and a minimap; a properties panel. Blocks drag, resize from eight handles, connect from four ports, lock,
duplicate, delete and undo. The camera pans (Space-drag, middle button, trackpad), zooms (Ctrl+wheel,
Safari pinch, buttons) and fits. Right-click opens a menu, `?` lists every shortcut, and the keyboard alone
can move (arrows), resize (R) and connect (C then Enter). A reload empties the board until M4.

**Proof.**

| Run | Result |
|---|---|
| `dotnet build` of the slnx with `-warnaserror` | 0 warnings, 0 errors |
| Canvas unit, component and guard tests | 576 passed |
| Playwright `Category=Editing` and `Category=Shell` (Desktop, Tablet, Phone shells) | 42 passed |
| Screenshot walk, desktop 1440 x 900, dark and light | 8 images |

**Red first.** 23 deliberate breaks, all caught (script kept with the session evidence). The one first
missed - removing the board's render gate - was caught once its unit test also changed the selection during
a gesture. The 300-block browser test does not go red when the gate is removed, because nothing reaches C#
during a drag at all; it pins that zero-interop behaviour, and the unit test carries the gate.

**Bugs the tests found.** A block's group label was passed as the literal word "GroupLabel"; the editor's
icons had no size in the standalone host, so each 300 x 150 icon covered the next rail button; the Playwright
helper returned a locator that followed the selection instead of a block.

**Deviations.** Block labels live in the editor (`NodeWords`) rather than Core, and arrowheads are built in
`EdgeLayer`; both were planned for Core in M3-01. Connectors follow a drag in the script (R9) from the edge
list `BeginMove` returns.

## M3 Blocks, connectors and groups — done 2026-09-15

**What Ben can open.** `http://localhost:5125/`: every block has its own head, icon and accent bar. A card
fills in the Evidence template (description, date, category, verified) and one Ctrl+Z undoes the whole
edit. A note links web addresses. A message is written with bold, italic, underline, lists and links. A map
box takes an address and coordinates. A link card shows the site's name with the grey "somebody else's
site" bar and an explicit open button. Connectors take a label, a colour, arrows and fixed sides. Groups
move their members, take a name (F2) and a colour, and a block dropped inside one joins it.

**Proof.**

| Run | Result |
|---|---|
| `dotnet build` of the slnx with `-warnaserror` | 0 warnings, 0 errors |
| Canvas unit, component and guard tests | 608 passed |
| Playwright `Category=Editing` and `Category=Shell` | 59 passed (17 of them new block tests) |
| Screenshot walk (`BlocksWalk`), 1440 x 900, dark and light | 6 images: node types, connectors, group |

**Red first.** 14 deliberate breaks, all caught:

| Step | Break | Test that failed |
|---|---|---|
| M3-06 | a note's web address not linked | A_note_links_web_addresses_and_nothing_else |
| M3-06 | a note rendered as markup | A_note_never_renders_markup |
| M3-06 | a file size formatted with the machine's culture | A_file_shows_its_size_with_a_dot_under_fr_FR |
| M3-07 | card fields sorted by label | The_evidence_card_lists_its_four_fields_in_order |
| M3-07 | a ticked box shown without words | A_ticked_checkbox_says_yes_with_a_glyph |
| M3-07 | an unknown template's values hidden | An_unknown_template_still_shows_what_was_typed |
| M3-08 | message HTML shown without the allow-list | A_message_at_rest_shows_clean_markup_and_an_iso_time |
| M3-08 | initials from one word | Initials_come_from_the_first_two_words |
| M3-09 | the away bar on our own records | An_our_records_link_has_no_away_bar |
| M3-09 | a double slash in the image proxy address | The_image_goes_through_the_webapi_proxy |
| M3-09 | a javascript: address offered as a link | A_javascript_url_gets_no_open_link |
| M3-09 | the map's "more pins" count | A_map_shows_its_address_and_pin_count |
| M3-04 | an edit that changed nothing recorded | Opening_and_closing_an_edit_without_changes_records_nothing |
| M3-13 | the connector's To side unlabelled | A_selected_connector_offers_label_colour_arrow_and_sides |

**Bugs the tests and the walk found.** A double-click on a block went to the board, because the board holds
pointer capture; it now looks up the element under the pointer and releases capture on pointer-up. The
message editor had no room once a block was in edit mode; editing blocks now scroll. The walk showed a
message losing its last words when the pointer left within the editor's 100 ms debounce; the editor now
sends every change (`DebounceDelay="0"`).

**Deviations.** Card fields are edited inside the card, not in the properties panel. Dates use the browser's
own date input rather than TelerikDatePicker, so the card also works in a static render. Block labels live in
the editor (`NodeWords`). Not built in M3: hover-revealed ports; dragging a connector's ends to re-attach
(use the From and To side lists); live maps (M6); stored pictures and files (M4); rich link previews (M6).
## M4 Smart paste, saving on this device, export and import — done 2026-09-15

**What Ben can open.** `http://localhost:5125/`: Ctrl+V a screenshot and it becomes a picture the shape of
the screenshot; paste a web address and it becomes a link card; paste formatted text from a web page and it
becomes a message that keeps bold but loses scripts; paste two paragraphs for a note, or coordinates for a map
box. Drag files onto the board and they land where they are dropped. Ctrl+C, Ctrl+X and Ctrl+V copy, cut and
paste blocks, the copy landing 24 px from the original. The Paste button in the rail reads the clipboard. The
header says "Saved on this device" about two seconds after an edit, a reload brings back the board and its
zoom, and closing the tab with unsaved work asks first. Export board downloads `{title}.ishcanvas`; Import
board brings it back with its pictures. An iPhone HEIC photo is refused with the Share advice.

**How it works (and why).**
- Board bodies are kept in IndexedDB (`bc-docs`); localStorage holds only the board list, the open board and
  each board's view, because ishaunted.com shares its 5 MB localStorage with the video editor (R8).
- Pictures and files are stored as files on the device (OPFS `bc-assets/{assetId}{ext}`), falling back to an
  IndexedDB store of Blobs where the browser cannot write OPFS files, such as Safari before 26 (R4). The board
  holds only the asset id; pictures are displayed through `blob:` addresses. No base64.
- Clipboard and drop data are read synchronously before any wait (R5). A board shortcut moves focus to an
  off-screen textarea for the one key press, so Safari fires copy, cut and paste (R16). JPEG, PNG and WebP are
  redrawn before they are stored, which removes EXIF and the GPS position in it (R24).
- The .ishcanvas ZIP is built and read in the browser from Blob slices, so a 300 MB export never passes through
  .NET memory; pictures are stored and only document.json is deflated; over 300 MB is refused naming the largest
  files; touch devices get a Download button because iOS downloads only from a tap (R12). Core's CanvasPackage
  is the reference for the same format, and the browser tests prove each side reads what the other writes.
- Autosave waits two seconds after the last edit and never runs while a block is being dragged (R18). A write
  the browser refuses is said once and never shown as saved. A browser that will not promise to keep the site's
  storage is mentioned once per device. The sweep deletes stored files no board, open board or undo history
  names, leaves anything younger than an hour alone (another tab may not have saved it), and refuses to run if
  the board list cannot be read.

**Proof.**

| Run | Result |
|---|---|
| `dotnet build` of the slnx with `-warnaserror` | 0 warnings, 0 errors |
| Canvas unit, component and guard tests | 659 passed (50 new) |
| Playwright `Persistence` and `Paste` (new) | 25 passed |
| Playwright `Editing` and `Shell` (regression) | 59 passed |

**Red first.** 23 deliberate breaks, all caught. 21 are caught by unit, component and guard tests (package size
limit, file-name cleaning, unreadable board list, autosave mid-drag, refusal said once, sweep grace, sweep keeps
undo, stale pointer, persistence mentioned once, leave-page guard only on change, pasted HTML sanitised, unused
files deleted, copy steps 24 px, maps switched off, cut only after copy, export packs each file once, import
cleans the board, Paste button uses the native click, module export names, callback names, scripts never build
nodes). 2 need a browser: photos not redrawn (the stored bytes kept their Exif block) and paste taking over a
note being typed (a second block appeared).

**What the checks themselves got wrong first.** The Paste-button test first rendered the component, but the
static renderer never writes Blazor click handlers, so it could not fail; it is now a source check. The first
browser break runs edited a script without rebuilding: the page checks each script against its build
fingerprint, blocked the edited one, and paste never started, so one test "failed" and one "passed" for that
reason alone. Browser breaks are now rebuilt and the host restarted before every run.

**Deviations.** Copy and cut live in pasteInterop.js beside paste, sharing the off-screen textarea, instead of a
separate clipboardInterop.js. Pasted HTML is cleaned by Core's allow-list (the one sanitiser, also run whenever a
board is read) instead of a second browser sanitiser. Pictures inside pasted formatted text are dropped rather
than turned into link cards, so no request reaches a third party. The WebKit Cmd+V test waits for M5, when the
Safari-engine browser is installed with Ben's permission. Not built in M4: a board list to switch between boards
on the device, and link previews beyond the site name (M6).
## M5 iPad and iPhone — done 2026-09-15

**What Ben can open.** `http://localhost:5125/` at any width. 1024 px and wider: the tool rail on the left and
the properties panel on the right, as before. iPad portrait (768 to 1023 px): the same rail, and the properties
panel lies over the board instead of squeezing it. iPhone (below 768 px): a bottom bar with Add, Paste, Undo,
Redo and More; properties, the Add list and More slide up from the bottom as a sheet that can be dragged to half
or full height or down to close, and stays above the on-screen keyboard. Pinch zooms about the fingers, one
finger pans the board or drags a block, a long press opens the menu, a double tap on the board makes a note,
and Apple Pencil draws a selection box. Add > Photo opens the iOS photo picker. When iOS will not let the board
read the clipboard, a paste box opens where a long press and Paste works. The page never bounces or scrolls
sideways, and every button is at least 44 px.

**How it works (and why).**
- One layout service answers four media queries (phone, iPad portrait, coarse pointer, reduced motion); CSS
  decides what is visible on first paint, the service only feeds behaviour and ARIA. Closing the panel and the
  sheet height are kept on the device (`bc-layout`); a sheet never reopens collapsed.
- The sheet is a non-modal dialog, so the board above it stays usable. Its grip is a real button (a tap takes it
  between half and full, for keyboard and VoiceOver users) and can be dragged; during the drag the script only
  writes `--bc-sheet-dy`, and C# hears once at the end. WebKit ignores `interactive-widget`, so a
  `visualViewport` listener writes `--bc-kb-inset` to lift the sheet above the keyboard (R6).
- A long press that opens the menu swallows the click the finger-lift makes, or the menu would close at once.
- A Paste button tap or a picked photo lands where the last long press was (for ten seconds); a keyboard paste
  does not.
- Safari (WebKit) has no OffscreenCanvas, so photos are redrawn on a detached canvas instead; its IndexedDB
  cannot hold Blobs, so the fallback store keeps the bytes and the type.

**Proof.**

| Run | Result |
|---|---|
| `dotnet build` of the slnx with `-warnaserror` | 0 warnings, 0 errors |
| Canvas unit, component and guard tests | 672 passed (13 new) |
| Playwright, every category (Chromium desktop, tablet, phone; CDP touch; WebKit iPhone 13 and iPad gen 7) | 137 passed, 0 failed; 37 skipped because they belong to another device size |

**Red first.** 14 deliberate breaks, all caught, plus one real bug found while taking the screenshots. 11 by unit, component and guard tests (phone query, collapsed
sheet refused, sheet turns into the panel, layout kept on the device, grip tap cycles, sheet not modal, phone bar
tools and order, long-press point only for buttons and photos, clipboard refusal opens the paste box, media
watcher export, sheet callback name). 3 need a browser: the long press menu closing on finger-lift, the Safari
fallback store writing Blobs (a pasted photo was lost in WebKit), and the theme switch sitting on the phone bar
(the first check looked only at the bar's buttons, so a new test measures the switch against the bar). The screenshots showed Export as a small white browser button: the header's scoped styles never reached Export and Import, which the editor hands to the header. A new test compares every header button with Undo; it failed (36 x 26 px, grey) and passes after the fix (M4 bug).

**Deviations.** The layout snapshot reuses M1's `LayoutSnapshot` instead of a new `CanvasLayoutSnapshot`. The
sheet drag lives in `boardGestures.js` beside the board gestures. The rail and bar Paste buttons are covered by
the one delegated native listener from M4, so there is no second binding.

**What only a real iPhone or iPad proves (Ben chose automated checks only).** The iOS Paste permission bubble,
the real on-screen keyboard over the sheet, and Safari deleting site data after seven days unused. Playwright
WebKit on Windows is not iOS Safari; a problem there would first show up for a real person.
## M6 The case: save, reopen, conflicts, publish, link cards and maps — editor side done 2026-09-15

**What Ben can open.** With the e2e API running (`scripts\run-webapi-e2e.ps1` in the Paranormal-canvas worktree) and
signed in, `http://localhost:5125/#case={case}&org={org}` opens that case's newest board, or a new one for it. Save
to case sends the board (and first uploads any pasted pictures and files into the case's Files); the header then
says "Saved to case". Opening the board on a clean device brings it back from the case. If somebody else saved a
newer copy first, a dialog offers Keep mine, Take theirs or Export mine first, and nothing is overwritten until the
choice. Publish asks first, then files a PNG picture of the whole board in the case. A pasted web address becomes a
card with the page's title, description and picture when a preview can be made, and keeps its site and address
when not. A map box shows a still Apple map of the place with Open in Apple Maps, and Live map loads Apple's
interactive map in the box. People who can read a case but not change it get a view-only board.

**How it works (and why).**
- `CanvasServerSession` owns the flow; the device copy is always written first, so no server answer can lose work,
  and autosave never writes to the server. The server's revision goes back as `If-Match`; a 409 carries the server's
  copy. A board the server cleaned (message HTML) is reopened from its copy.
- Pictures and files upload in the browser from the device copy, and case pictures display through `blob:`
  addresses fetched with the bearer token (R17); the token is only ever sent under the API base (R15).
- View-only (R33): the API's new `CanEdit` (or a 403) sets `BoardAccess`; every change is refused in C# and the
  script pans instead of moving; the rail and empty state offer nothing to add; properties are disabled.
- Publish draws the picture from the document on a canvas (`BoardSnapshot` + `snapshotInterop.js`), so every block
  is in it however big the board (R11), at most 4096 px and 16 million pixels for Safari.
- Maps (R35): Apple's developer agreement allows map data only temporary storage, so the still picture is signed by
  the website (`/auth/mapkit-snapshot`, Referer-limited to our pages, canvas flag gated) and fetched from Apple each
  time the box is shown; nothing map-related is stored with the board or in the published picture (map boxes are
  drawn as their address there).

**Proof.**

| Run | Result |
|---|---|
| `dotnet build` of the slnx with `-warnaserror` | 0 warnings, 0 errors |
| Canvas unit, component and guard tests | 761 passed (89 new) |
| Playwright, every category, with the e2e API (Server category new, 6 tests) | 145 passed, 0 failed; 41 skipped (other device sizes) |
| API worktree: board controller, MapKit signer and snapshot request tests | 51 + 29 passed |

**Red first.** 19 editor breaks, all caught (two only after a stronger break and a stronger assertion); a browser
break of the conflict choice caught by the Server tests; server breaks caught for `CanEdit` and the snapshot
Referer check.

**What the checks themselves got wrong first.** The M4 paste test said a link card may reach no server "before
M6"; it now checks the real rule, that the browser never contacts the linked site. A break restored with Copy-Item
kept the backup's old timestamp, the incremental build skipped it, and a correct test "failed" against the stale
build.

**Deviations.** Publishing draws the board itself instead of vendoring html-to-image (no download, no Safari
foreignObject trouble). Map pictures are never stored (Apple's terms) and there is no `IMapConfig` seam; the map
addresses come from `CanvasEditorOptions`. The server session is its own service beside the device store. Not done
here: a browser test of the view-only board (no seeded read-only BenCo account; covered by unit tests), and live map
tiles in development (the website on 5078 needs MapKit signing keys).

## M7 Live at ishaunted.com/editors/canvas — everything but the rollout done 2026-09-15

**What Ben will open.** `https://ishaunted.com/editors/canvas/` on desktop, iPad and iPhone: dark, in the site's
look, with a Sign in chip. After he turns on "Feature — Canvas editor" in Site settings, signing in there lets him
save a board to a case. Until that switch is on, the API answers 404 to the board and link addresses, so the
feature is invisible even though the files are live.

**What is done, and proven.**

| Run | Result |
|---|---|
| `dotnet test Ben.Web.Tests --filter CanvasDeployScriptGuardTests` (worktree) | 13 passed |
| `dotnet vstest ... TestCategory=Layout` | 22 passed, 0 failed, 26 skipped (other device sizes) |
| `deploy-ishaunted.ps1 -Apps canvas -StageOnly` | exit 0, artifacts inspected by hand (below) |
| `BEN_CANVAS_WALK=1 dotnet vstest ... CanvasWalk` | 6 passed, 90 pictures, 0 console errors, 0 responses of 500 or worse, no third-party hosts |

**The staged canvas, checked file by file rather than from the script's own log.**
`<base href="/editors/canvas/">`; the three patched settings (`WebApiBaseUrl https://ishaunted.com/webapi`,
`SiteBaseUrl https://ishaunted.com`, `MapTokenUrl https://ishaunted.com/auth/mapkit-token`); no
`appsettings.Development.json`; no `.br` or `.gz` twin of either patched file; `web.config` carrying X-Frame-Options,
Content-Security-Policy, X-Content-Type-Options and Referrer-Policy, and mentioning cross-origin isolation only in
the comment that says why it is deliberately absent — `require-corp` would blank the map tiles. `build-info.json`
stamp `bd6072e8…`, commit `e96c74a2`.

**Pre-flight before the rollout.** The canvas branch is not behind master or the deploy branch (0 and 0), and the
commit the live API reports (`2ff3def9`) is an ancestor of it, so deploying the website cannot roll back the other
session's work. `dotnet ef migrations list` against production shows exactly one pending migration,
`20260914235349_AddCanvasEditor`, whose `Up()` runs two `CreateTable` calls (`CanvasDocuments`, `LinkUnfurlCache`)
and their keys and indexes — nothing existing is altered, and the two `DropTable` calls are in `Down()`.

**The screenshot walk.** `CanvasWalk` imports one board (all seven block types, a group, a labelled connector and a
two-headed one) and photographs it at 1440x900, 768x1024 and 390x844 in both themes: empty board, the blocks, a
selected block with its handles, properties (a column on desktop and iPad, a sheet on the iPhone), the colour
swatches, a selected connector, a group, the press-and-hold menu, the zoom controls and minimap, the help panel, the
HEIC refusal, the two save states, Export and Import, and the sign-in card. Every picture carries the sentence it is
there to show plus the console errors, 500s and outside hosts seen since the previous one, and `WalkReport` writes
report.md when the last of the six fixtures finishes. The board is imported rather than built by clicking, so all
three sizes show identical content and only the layout differs.

**Still to run, and why.**
- The rollout itself: Ben runs the production migration, `setup-iis-ishaunted.ps1` and
  `deploy-ishaunted.ps1 -Apps webapi,canvas,website -CanvasProjectPath …` in an elevated shell. Per R26 the UAT
  database is not touched; it belongs to the other session.
- The production probes (base href, build stamp, no `.br` twins, headers, 401 anonymous and 404 signed-in while the
  flag is off), the Playwright run against the deployed mount in Chromium and WebKit, and the device checklist all
  need the site to be live first.
- The three server pictures in the walk (saved to case, the newer-copy choice, the publish question) are marked
  NOT TAKEN. The second run did try, with the seeded password read straight from the worktree's own
  `appsettings.Development.json`, and the API answered `401 LockedOut`: the seeded BenCo account is locked out on
  the shared e2e database, which another session also signs in to. The lock clears itself, and the same three
  things are already proven by the Server category (7 of 7 on the final M6 build), so the walk was left as it is
  rather than restarting that API underneath the other session.

**What came up along the way.** Every block type prints its title twice, once in the head row and again as the
first line of its body — the map box repeats its pin icon as well. It is not new in M7, but the walk pictures make
it plain, and on a 390px iPhone the repeat costs real height. Raised as its own piece of work rather than changed
during a rollout.

## M8 Research is boards, and a board can be presented — done 2026-09-16

**What changed, and why it is a milestone rather than a feature.** The site had two ways to write up
a case: the block-editor research pages (2026-09-14) and the canvas boards. Ben chose the boards, so
the pages are gone, and with them `features.canvas-editor` — a switch whose off position leaves a case
with no research at all is a trap, not a choice. Research also returns to the timeline's Add Entry
list: a board is not a dated moment, so a note about what the deeds said needs the timeline again.

**What Ben asked to keep, and where it lives now.** "I like that researchers can create their own
pages and publish them independently. I also like the smart copy-and-paste." Neither was the block
editor's:

- a draft nobody else sees — `CanvasDocumentController` lists every published board plus the caller's
  own drafts, and hands a reader the published copy rather than what is being written now;
- paste that knows what you pasted — `PasteClassifier` reads a link, picture, recording or file and
  makes the right block, judging a picture by its first bytes rather than its name.

**One behaviour genuinely changed.** A picture on an unpublished research page was its author's alone.
A file dropped on an unpublished board goes to the case's Files at once, where the group can see it —
because dropped files going to the case files is what Ben asked for. The board's contents stay private
until published; the file does not.

**Growing the next card.** A block's four side handles do two things now: dragged they aim a connector
as before, clicked they make the next block on that side already joined, at the same size, kind and
colour, open for typing. Drag one onto empty board and it lands where you let go. Ctrl+Shift+Arrow is
the keyboard form — not Alt+Arrow, which is Back and Forward in Chrome on Windows. `AddConnected` adds
the block and the connector as ONE command, so one Undo takes back the whole gesture.

**Presenting.** `Present` walks the board full screen with everything else dimmed. Nothing is prepared:
`SlideOrder` reads the running order off the board — groups if any exist (Miro's frames), otherwise
the arrows, then down the page — so growing one card out of another writes the deck as the thinking
happens. It changes nothing: no command, no undo entry, no edit, so a board can be presented by
somebody who may only read the case, which is the meeting it exists for.

**Proof.**

```
dotnet build Ben.slnx                      # 0 warnings, 0 errors
dotnet test  Ben.Canvas.Tests              # 862 passed
dotnet test  Ben.Web.Tests                 # 6182 passed, 2 skipped
./scripts/run-e2e.sh --filter "TestCategory=Canvas"
```

The e2e harness starts the canvas as a fourth host (5125) beside api, web and wasm. The two Research
tab fixtures had skipped on every run until now — no canvas host, plus a deliberate skip for the flag.

**Three things only the doing found.**

1. *The handover was broken and every test passed.* The API's development CORS list did not name the
   canvas host, so the one-use code could not be exchanged and the editor opened SIGNED OUT on
   whatever board the browser had kept on the device. The tests checked that the editor appeared, not
   that it arrived signed in. Fixed; guarded on the settings file; the fixture now asserts both, and
   a second fixture asserts the case's own board opens with its five blocks.
2. *Every card stayed lit while presenting.* `CanvasNodeHost`'s render gate exists so a board of
   hundreds redraws only what moved, and presentation was not one of the things it watched. On stage
   is a parameter now, and part of the gate.
3. *The repo guards earned their keep twice* — the view-only action list insisted presenting must be
   refused (it must not), and the theme guard caught a literal shadow in the new bar's CSS.

**Docs.** The help's Research section rewritten for boards, presenting and the side handles; three new
pictures and a GIF shot from the seeded board; What's New and the service changelog; the product and
persona PDFs rebuilt. The seeder plants a published board — four joined cards and a note — so a fresh
install has something to show, with a test that reads it back through the editor's own reader.

**The migration destroys rows.** `RetireResearchPages` drops `CaseResearchEntries` and
`CaseResearchAttachments`; `Down` rebuilds them empty. Both runbooks carry the copy-first step.
Applied to `IsHauntedDb_player` only; production gets it with the deploy.


## M9 Pieces, styles, links and templates — planned 2026-09-18

**What Ben asked for.** Four boards, sent as pictures: a *moodboard* (coloured section panels, circle
"theme" bubbles, an image collage, shown as a blank template beside a filled sample); a *research plan*
(coloured title tiles, cards holding a 2×2 matrix, a calendar grid and sticky clusters); a *family tree*
(boxes joined by dashed sibling lines, marriage lines with diamond ends, a child line with a chick at its
middle, all at right angles, with a legend); and a *presentation deck* (slide-sized frames with coloured
backgrounds: agenda, timeline, steps, 2×2, table, charts). Then: *"add research note from case … a link to
open the other page to one of the cards … and a back button to go back."* And the reminder from
2026-09-14: *"Remind me later to have you create the table."* The vision those sit inside, in Ben's words:
*"a combination of Notion, Canva, OneNote and Obsidian's Canvas."* M1–M8 built the Obsidian half. M9 is
the pieces those four boards are made of, the styles that make them look designed, the link between
boards, and templates that assemble them.

**What Ben can open when it is done.** New board offers a template — Blank, Moodboard, Research plan,
Family tree, Presentation deck — and opens with the frames, cards and connectors already placed. The rail
adds a **table** (rows and columns, add or remove either, paste tab-separated text and get one) and a
**shape** (rectangle, ellipse, diamond with centred text). Any card or note can be **filled** with its colour
instead of carrying a bar — that is the sticky. A group can be a **panel**: solid tint, solid border, its
label as a title bar — that is the section and the slide. A connector can be **dashed**, run **straight**
or in **right angles**, end in an **arrow, a diamond, a dot or nothing** at either end, and carry a
**small icon or word at its middle**. A card can name **another board on the case**, picked from a list
the way a file is picked; opening it saves this board first, and the header shows **Back to <board>**
until you use it. Everything is undoable, exports and imports, publishes to the picture, and presents.

**Decisions, and why.**

- *Sticky is not a new kind.* `TextNode` already calls itself "a sticky note of plain text"; what it lacks
  is a fill. Every block already has a palette colour drawn as a 3 px bar. A `Fill` on the node — `Bar`
  (today) or `Solid` — turns that colour into the background. Zero visual change to any existing board,
  no migration, and it works for a card or a picture caption too.
- *Panel is a group style, not a new kind.* `CanvasGroup` already has bounds, a label and a colour. `Fill`
  — `Outline` (today: dashed, 8 %) or `Panel` (solid tint, solid border, label as a bar) — is the moodboard
  section and the deck slide in one. `SlideOrder`'s first rule, groups win, means a deck template presents
  correctly with nothing else built.
- *Legend and 2×2 are templates, not kinds.* A panel with a note beside sample connectors is a legend; four
  panels in a square are a matrix. Kinds are for things with their own data.
- *Charts are not in M9.* A bar or bubble chart is data entry plus a renderer plus a snapshot painter — a
  different product from a board piece. Named for M10 and left out on purpose.
- *A board card opens with a button, not a link.* `boardGestures.js` already lets a `<button>` inside a
  block escape drag and select (it is how a link card's Open works); a bare anchor would fight the board.
  Open saves the current board first (`documents.SaveAsync()` already runs before a switch), then
  `ServerSession.OpenAsync(id)`. A trail of `(serverId, title)` pairs gives the header its Back; it is
  view state, never stored on the board. Alt+Left is deliberately not bound — it is Back in Chrome on
  Windows, and the M8 notes already record why.
- *Only a published board can be linked, and the server holds the line.* Ben, 2026-09-18: *"the only
  way for it to hit the load link to other page is if the other page has been published … it will cause
  issues if one is not published and one that is published has a link to a page which is not published."*
  A published board is what a reader sees; a draft is its author's alone. A link from the first to the
  second would hand a reader a door into somebody's private work, or a 404. So the rule is an invariant —
  **a published board never points at an unpublished one** — held in three places, because a picker
  alone only stops the state being *created*: the picker lists **only published boards** on the case
  (`GetAll` already tells published from the caller's own drafts); `Publish` **refuses** a board carrying
  a board card whose target is not published, and says which; and `Delete` **warns** — *N published boards link here; their links will say this board no longer
  exists* — and then deletes. Ben, 2026-09-18: *"if a published page is deleted, it should delete links
  on other boards or when clicked, it should pop a message up saying the published page or board has
  been deleted."* The message, not the cascade: deleting a card out of somebody else's published board
  is the server rewriting a snapshot a reader may be looking at, and the additive guard exists to stop
  exactly that. So the link stays, and it tells the truth **at rest** — every board card checks its
  target when its board loads (one list call, the same one the picker makes) and shows *This board no
  longer exists* in place — and **on click**, which pops the same sentence and does nothing else. The
  picture and the deck treat such a card as inert. Following a live card always opens the target's
  **published copy** — the author too, so what they check is what a reader gets. The card stores the
  target's id and the title it had when picked; nothing else, so it can never leak a draft's contents.
- *A board card can point at one card on the other board, and a missing card is not an error.* Ben,
  2026-09-18: *"if there is a link to a card in a different page, and the card has been removed, default to
  opening the other page and not focusing in on that card."* So `BoardData` carries an optional target
  block id and the title that block had when picked. Opening looks the block up in the target's
  **published copy**: found, the view fits to it and selects it; gone, the board opens at fit-to-content
  with nothing selected and a quiet note that the card is no longer there. Nothing on the source board
  changes when the target card is removed — the link degrades at the moment it is followed, where the
  person can see what happened, not silently on somebody else's board. The picker's second step offers
  the published copy's blocks by the same title the published picture prints (`Words()`), so what you
  pick is what a reader would recognise. And the picker **says** the rule — Ben: *"a small note in the
  page/card picker to let the end user know only published pages are in the list"* — because a board
  that is missing from a list is a bug report waiting to happen unless the list says why; the same
  sentence is its empty state.
- *Templates are documents, not a server concept.* `CanvasDocumentController.Create` stores whatever
  document the client posts, and a new board is `CanvasDocumentStore.New(caseId)` — "not stored until its
  first edit." So a template is a pure function `Guid? caseId → CanvasDocument` in `Ben.Canvas.Core`,
  loaded by `New(caseId, template)`. The seeder already proves the shape (`SeededBoardTests` reads its
  board with the real reader). No API change, no migration.
- *Every person in the family tree gets an empty picture frame.* Ben, mid-build 2026-09-18: *"include a
  place to put a photo of the person - if they want to do that or even just using a male and female icon.
  It should be up to the end user."* So a person is a PAIR — an Image block above a name note — and the
  frame ships **empty**, which is a finished state rather than a gap: the image block already draws "No
  picture yet. Paste or drop one here." A photo, a drawn icon, or nothing at all are all equally done,
  which is the choice Ben asked to leave open, and a stock silhouette would have quietly made it for him.
  `Fit` is `Cover` so whatever arrives fills the frame at everyone else's size instead of each portrait
  being as tall as its own file. The connectors join the **names**, never the frames: a line into a photo
  would move the moment somebody decided to go without one. There is no gendered symbol in the sprite and
  none is added — the frame takes any picture, which covers the icon case without the site having an
  opinion about it.
- *The seam is the fragment.* The Research tab's New board already hands off `#handoff=&case=&org=`;
  it gains `template=<id>`, `CanvasHandoff.Parse` gains the arm, `Editor.razor` threads it to a
  `CanvasEditor.TemplateId` parameter, and `RestoreAsync` honours it **before** restoring the device
  copy — an explicit request wins over what was open last time — and only when no `doc` arrived.
- *Connector routing lives in one function.* `EdgeGeometry.Resolve` is called from exactly three places —
  drawing, the published picture and hit-testing — so a `Route` is answered there and all three follow.
  `Straight` is the existing cubic with its controls on its ends; `Elbow` is a polyline, so `EdgePath` grows
  an optional corner list and `ToSvgPath`, `PointAt` and the hit sampler learn it. Heads are built by hand in
  two places (`EdgeLayer.ArrowHead`, `BoardSnapshot.Head`); each gets a marker argument and
  `SnapshotConnector.Heads` stops meaning "six numbers each".
- *Schema stays at 1.* Every new field has a default that reproduces today's look, and `Upgrade` already
  clears what cannot be right, so old boards open unchanged. The real compatibility edge is the other way:
  a browser holding a **stale** editor meets a board with `kind: "table"` and the reader fails on the
  unknown kind by design. The host and the API deploy together and the shell has a freshness test
  (`CanvasHostFreshnessTests`), so that is the accepted risk, written here rather than discovered.

**Pieces, by the machinery each touches.** A new kind is: an enum value → `XData : NodeData` with its
`[JsonDerivedType]` → one `BlockRegistry` line → `Nodes/XNode.razor : BlockRendererBase` → a
`BlockRendererMap` entry → a `RailOrder` slot, `CanvasCommand.AddX`, its keymap letter and its keyboard
arm → a `Words()` arm for the picture → a `--bc-type-x` token in both theme files → a `PastePlacer` arm
if it can be pasted. Every one of those is enforced by a test that already exists.

| Step | Builds | Where |
|---|---|---|
| M9-01 | `TableData` (rows of cells, header flag), `TableNode` (table at rest; cells, add/remove row and column ≥44 px in edit), `PasteIntent.Table` from tab-separated text with two or more columns in two or more rows, placed before `Text`; chord `b`; icon `grid` | Core/Model, Editor/Nodes, Core/Paste |
| M9-02 | `ShapeData` (`Rectangle`, `Ellipse`, `Diamond`, text), `ShapeNode` (solid palette fill, centred text); chord `o`; icon `circle` | Core/Model, Editor/Nodes |
| M9-03 | `CanvasNode.Fill` `Bar`/`Solid`; `NodeFrame` paints it; properties panel toggle beside the swatches; `SnapshotBlock.Filled` and the painter | Core/Model, Editor/Nodes, Chrome, Persistence, js |
| M9-04 | `CanvasGroup.Fill` `Outline`/`Panel`; `GroupLayer` styles; properties toggle; `SnapshotGroup.Fill` and the painter; `Group(ids, label, fill)` | Core/Model, Editor/Board, Chrome, Persistence, js |
| M9-05 | `CanvasEdge.Line` `Solid`/`Dashed`, `Route` `Curve`/`Straight`/`Elbow`, `FromMarker`/`ToMarker` `Arrow`/`Diamond`/`Dot`/`None` (replacing `Arrow`'s meaning, `Arrow` kept and mapped on read), `Icon` (≤ 8 chars at the midpoint) | Core/Model, Core/Serialization |
| M9-06 | `EdgeGeometry.Resolve(…, route)`; `EdgePath` corners; `ToSvgPath`/`PointAt`; hit sampling over corners; `ArrowHead(marker)`; `Head(marker)`; `SnapshotConnector` gains `Dash`, `Icon`, variable-length heads; painter | Core/Geometry, Editor/Board, Persistence, js |
| M9-07 | Connector properties: line, route, each end's marker, icon; `bc-edge--dashed`; midpoint label already exists for the icon's placement | Chrome, EdgeLayer |
| M9-08 | `BoardData` (`DocumentId`, `Title`, optional `NodeId`, `NodeTitle`), `BoardNode` (icon `book-open`, board title, "→ card title" when a card is named, Open **button**; "no longer published" state), `CaseBoardPicker` (copy of `CaseFilePicker` over `server.ListAsync(caseId)` filtered to **published**; second step lists the published copy's blocks by `Words()` title, "the whole board" first; a one-line note at the top — *Only published boards can be linked. A board you are still working on appears here once it is published.* — in `CanvasCopy`, and an empty state that says the same when the case has none), action `case-boards`, rail button "Add from the case's boards" | Core/Model, Editor/Nodes, Chrome, Text |
| M9-08b | Server invariant: `Publish` refuses a document whose `board` cards name an unpublished target (same `nodes[].data.kind` walk `SanitizeDocument` already does) and names it; `Delete` answers how many published boards link to the board, the website's confirm shows it, then deletes; the editor greys Publish and says why before the round trip | WebApi `CanvasDocumentController`, website confirm |
| M9-09 | `BoardTrail` (view state: stack of `(serverId, title)`), Open = save, then open the target's **published copy**, push; a named card that still exists is selected and fitted to; a named card that is gone opens the board at fit-to-content, selects nothing, and toasts once; header **Back to <title>** beside `BackContent`, pop; trail cleared on case change; a 404 toasts and leaves the trail alone | Editor/Services, Chrome |
| M9-10 | `BoardTemplates` in Core: `blank`, `moodboard`, `research-plan`, `family-tree`, `deck`; each a pure builder over a `BoardBuilder` that floors sizes at the registry's minimums, refuses anything but a palette key, and leaves `NextZ` ahead of the board; the family tree gives every person an EMPTY picture frame above their name | Core/Templates (new) |
| M9-11 | `CanvasDocumentStore.New(caseId, template)`; `CanvasEditor.TemplateId`; `CanvasHandoff` `template` arm; `Editor.razor`; `RestoreAsync` ordering | Editor/Services, Wasm host, Lifecycle |
| M9-12 | Research tab: New board becomes a choice of template (names and one line each), fragment carries `template=` | Website `CaseResearchBoards.razor` |
| M9-13 | Help: Research section gains tables, shapes, fills, panels, connector styles, board links, templates; three pictures and a GIF; What's New; service changelog; product and persona PDFs | Help, Changelog, docs |
| M9-14 | Walks: `BlocksWalk` gains the new kinds; new `TemplatesWalk` shoots each template dark and light | Playwright/Capture |

**Red first — the tests that must fail before each step and pass after.**

| Step | Break | Test that fails |
|---|---|---|
| M9-01 | a kind with no descriptor | Every_node_type_has_a_descriptor (exists) |
| M9-01 | a kind with no renderer | Every_block_type_has_a_renderer (exists) |
| M9-01 | renderer count left at 9 | Every_type_maps_to_its_own_renderer (exists; count moves) |
| M9-01 | a table that does not round-trip its cells | A_table_round_trips_every_cell |
| M9-01 | a table at rest rendered as text | A_table_at_rest_is_a_table_element_with_a_header_row |
| M9-01 | a cell input with no label | Every_table_cell_in_edit_names_its_row_and_column |
| M9-01 | tab-separated text pasted as a note | Tab_separated_text_becomes_a_table |
| M9-01 | one column mistaken for a table | A_single_column_of_text_stays_a_note |
| M9-01 | a command with no keyboard arm | Every_canvas_command_has_a_case (exists) |
| M9-02 | shape text off centre / an unknown shape kind | A_shape_centres_its_text; An_unknown_shape_kind_reads_as_a_rectangle |
| M9-03 | an old board opening with a fill | A_board_with_no_fill_field_reads_as_bar |
| M9-03 | a solid fill ignoring the theme | CanvasThemeTokenTests (exists) |
| M9-04 | a panel losing its label | A_panel_group_keeps_its_label_as_a_title |
| M9-04 | the picture ignoring a panel | A_panel_group_carries_its_fill_to_the_snapshot |
| M9-05 | `Arrow` on an old board read as no markers | An_old_arrow_maps_to_an_end_marker |
| M9-05 | an icon longer than eight characters stored | A_connector_icon_is_clamped_on_read |
| M9-06 | a straight route that curves | A_straight_route_is_a_line |
| M9-06 | an elbow with an oblique segment | An_elbow_route_turns_only_at_right_angles |
| M9-06 | a corner the hit-test cannot find | Elbow_hit_test_finds_the_corner |
| M9-06 | a diamond drawn as a triangle | A_diamond_head_has_four_points_and_a_dot_is_round |
| M9-06 | a dashed connector solid in the picture | A_dashed_connector_is_flagged_for_the_painter |
| M9-07 | a connector's icon control with no label | LabelAssociation / RazorMarkupGuardTests (exist) |
| M9-08 | a board card opening in a new tab | A_board_card_opens_with_a_button_not_a_link |
| M9-08 | the picker listing boards of another case | The_board_picker_lists_only_this_cases_boards |
| M9-08 | the picker offering a draft | The_board_picker_offers_only_published_boards |
| M9-08 | the picker silent about why a board is missing | The_board_picker_says_only_published_boards_are_listed |
| M9-08 | an empty picker with no explanation | An_empty_board_picker_says_publish_one_first |
| M9-08 | a card storing anything but the target's id and title | A_board_card_carries_no_content_of_its_target |
| M9-08b | publishing a board that links to a draft | Publishing_refuses_a_board_that_links_to_an_unpublished_board_and_names_it |
| M9-08b | deleting a linked board without a word | Deleting_a_linked_board_warns_how_many_published_boards_link_to_it |
| M9-08 | a card whose target is deleted looking live until clicked | A_board_card_whose_target_is_gone_says_so_at_rest |
| M9-08 | clicking a dead card doing anything but the message | Clicking_a_dead_board_card_pops_the_message_and_nothing_else |
| M9-08b | the refusal arriving as a bare string | TierValidationShape-style: the refusal is a record the editor reads |
| M9-09 | following a card to the draft rather than the published copy | Following_a_board_card_opens_the_published_copy |
| M9-09 | a card whose target is gone opening nothing silently | A_board_card_whose_target_is_gone_says_so_and_stays_put |
| M9-09 | a named card that exists not focused | A_card_link_selects_and_fits_to_the_card_when_it_still_exists |
| M9-09 | a named card that is gone treated as an error | A_card_link_whose_card_is_gone_opens_the_board_at_fit_and_selects_nothing |
| M9-09 | the lookup reading the draft rather than the published copy | A_card_link_is_resolved_against_the_published_copy |
| M9-08 | the picker naming blocks differently from the picture | The_picker_lists_the_published_copys_blocks_by_their_snapshot_title |
| M9-09 | Back after opening returns to the wrong board | Opening_a_board_card_pushes_and_back_pops |
| M9-09 | switching without saving | Opening_a_board_card_saves_the_current_board_first |
| M9-10 | a template that does not validate or overflows | Every_template_opens_with_the_editors_own_reader, Every_template_fits_MaxNodes |
| M9-10 | a deck frame that is not a panel | The_deck_template_is_panels_in_reading_order |
| M9-10 | a template naming a raw colour | Templates_use_palette_tokens_only, A_template_may_not_name_a_colour |
| M9-10 | a block placed under its kind's minimum | A_block_is_never_placed_under_its_own_minimum |
| M9-10 | a template whose next block lands behind it | The_paint_counter_is_ahead_of_the_template |
| M9-10 | a grouped block hanging outside its panel | Every_grouped_block_sits_in_a_group_that_exists |
| M9-10 | a template shipping a board link | No_template_carries_a_board_link |
| M9-10 | a template icon that is not in the sprite | Every_template_icon_exists_in_the_sprite |
| M9-10 | a pre-filled portrait, or a frame the lines join | Every_person_in_the_family_tree_has_a_picture_frame_that_starts_empty |
| M9-10 | a template presenting out of order | Slide_frames_in_a_deck_template_present_top_to_bottom |
| M9-11 | a device restore beating an explicit template | A_template_request_wins_over_the_last_open_board |
| M9-11 | a template applied on top of a `doc` | A_template_is_ignored_when_a_board_id_arrives |
| M9-11 | an unknown template id | An_unknown_template_opens_a_blank_board_and_says_so |
| M9-12 | New board losing the template in the fragment | Playwright `CanvasTemplatesTests.New_board_from_the_deck_template_opens_with_its_frames` |
| M9-08/09 | link → open → back, end to end | Playwright `CanvasBoardLinksTests.Open_another_board_and_come_back` |

**Proof to run.**

```
dotnet build Ben.slnx -warnaserror
dotnet test  Ben.Canvas.Tests
dotnet test  Ben.Web.Tests
./scripts/run-e2e.sh --filter "TestCategory=Canvas"
```

**Risks named up front.** (1) The stale-editor edge above. (2) `Every_type_maps_to_its_own_renderer`
hard-codes `9`; it becomes `12` and stays hard-coded on purpose — the number is the assertion. (3)
`SnapshotConnector.Heads` changes shape; the published picture is regenerated on publish, never
rewritten, so old pictures stand. (4) A board card's target can stop existing — `Delete` warns and proceeds — so the dead-card state is
expected, not exceptional: it is shown at rest and on click, and the at-rest check is one list call per
board load, cached for the session. (4b) The publish and delete refusals are **API changes**, small and additive; the editor greys
the button first so the round trip is the backstop, not the message. (5) Templates
are code, so a change to one changes every *new* board from it and no existing board — which is the
right way round, and the tests say so.

**Not in M9.** Charts (M10). Cross-case board links (a board names a board on its own case; the picker
is scoped there, and the additive guard already stops a link from becoming an edit elsewhere). Linking
to a draft, ever — by rule, above. A template
gallery with pictures (names and a sentence in M9; pictures once the walk has shot them). Table cell
merging, formulas, sorting. Connector waypoints you can drag (Elbow is automatic).
