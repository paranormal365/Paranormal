# CMS editor: tree, ordering, section pictures and previews (`feature/cms-editor-ux`, 10/09/2026)

Ben's list, and what answered each part.

| Ben asked | What was built |
|---|---|
| "More Actions" in the page grid cut off by the cell/row | The grid is four roomy columns (the title had been squeezed to nothing below ~1100px); the dropdown sits inline beside Sections (`.k-grid td .dropdown`); every BenDropdown now closes when a choice is clicked (`CloseOnClick`) and anchors to its toggle so a closed menu never widens its container. |
| Add Section should scroll to the form | `OrgCmsPageEdit` scrolls `#cms-section-editor` into view (domInterop `scrollToElementId`) on Add Section and on a section's edit button. |
| Thumbnail examples of each section | `CmsSectionTypes` (label, sentence, inline SVG sketch per kind) and `CmsSectionTypePicker` (radio-group cards). Contact details, file gallery and member roster are not offered: the public page draws them as placeholders (backlog 256). |
| Preview button before Cancel | `POST …/sections/preview` cleans and resolves the unsaved section exactly as the public page does; the modal draws it with `OrgPublicSection`. Saves nothing. |
| Title typed or picked from ideas | `CmsPageIdeas` (grouped ideas for groups, tours and venues); the address follows the title until edited (`SlugFrom`). |
| Sections below the summary | The intro (`PageHtml`) was never shown to visitors at all. `OrgPublicPageItem.IntroHtml` (sanitized at read; sanitized on save from now) is drawn by `OrgPublicIntro` above the sections on the public page, home page and preview. |
| Intro image from public images | BenEditor `PictureLibrary`/`PictureUrl` add a toolbar button offering the group's public pictures. |
| Links pick from existing CMS pages | BenEditor `PageLinks` adds "Link to one of your pages" (wraps the selected words, or inserts the page title); the banner's link has a "Your pages" list. |
| Sort order as a draggable tree, three levels | `CmsPagePlacer` (Telerik TreeView, only the current page drags, arrow buttons too) in the New/Edit Page dialog and the page editor; `PUT …/pages/{id}/position` moves and renumbers both sibling rows; `Ben.Data.Common.CmsPageTree` is the one depth/cycle rule for server and client. Visitors get the pages under the one they read as a sub-row (`OrgPublicNav`). |
| Help text under every field | `form-text` under every field in the page dialog, page settings, section form and banner editor. |
| Ordering modal | `CmsOrderingDialog` + `GET …/pages/outline`: pages drag anywhere (depth rule), sections only within their page, hideable tips (remembered via the tours table), each move saved at once, Done. |
| Docs, help, images, PDFs | Help "Editing your public pages" rewritten with nine new pictures (`HelpMediaCapture.Capture_CmsEditor`, sample site from `CmsSampleSite`); owner persona shots 5g–5j; product and owner PDFs rebuilt. |

Found and fixed on the way: the section reorder endpoint did not check the page belonged to the group;
section reorder and draft publish answered 500 after succeeding (audit shapes; backlog 257); publishing a
draft copied the draft's stale menu position back over the live page.

Tests: `Ben.Web.Tests/Controllers/CmsPageOrderingTests.cs` (24), `AuditLogServiceFailureTests`,
`Ben.Web.Playwright/Tests/CmsEditorUxTests.cs` (6, including a real drag in the tree).
