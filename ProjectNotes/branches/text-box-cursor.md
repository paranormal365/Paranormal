# fix/text-box-cursor: typing keeps the cursor where it is, in every box

Ben, 10/15/2026: holding Backspace in the middle of a sentence deleted the end of it instead. He
first saw it in the **site-wide announcement** box (Administration → Site settings) and then in the
feed composer. The feed composer got its own fix (caf1249c, merged and live by 10/22). This branch
fixes every other box with the same pattern, the announcement included. On 10/22 Ben found the
announcement still doing it on the live site, which is what this branch fixes.

Started from `develop` at 2aab9c05 (10/22). The first attempt (10/15, 8a78f1a4)
was cut before the feed fix merged and was never merged; it was redone here, not rebased, because
develop had changed seven of the files since.

## The cause

A box drawn as `value="@_field"` with `@oninput` setting `_field` is written to by the server after
every keystroke. If the reply lags behind the typing (a held key, a slow connection), an older copy
of the text lands on the box and the browser moves the cursor to the end.

Boxes bound with `@bind` / `@bind:event="oninput"` do **not** have this problem, despite looking
similar. For a bind, the Razor compiler marks the element (`SetUpdatesAttributeName("value")`), and
before redrawing, Blazor takes the text the browser sent with the event as its own record of what the
box holds. Drawing that same text again then sends nothing. A plain `@oninput` handler gets no such
mark. `TypingStaysPutTests` measures both kinds, so this is tested, not assumed. The ~300 `@bind`
boxes are therefore unchanged.

## The fix: `BenTextBox` and `BenTextArea` (Kit/)

Two small components over a shared base, `BenTypedBox`. Each one draws a plain `<input>` or
`<textarea>` with `@bind:get` / `@bind:set` / `@bind:event="oninput"`, so it gets Blazor's own
protection. Its text changes only when code gives it a new `Value` (clearing after saving, a
handler that tidies what was typed). It is a drop-in replacement:

```razor
<input class="form-control" value="@_notes" @oninput="@(e => _notes = e.Value?.ToString())" />
<BenTextBox class="form-control" Value="@_notes" OnInput="@(e => _notes = e.Value?.ToString())" />
```

- `Value` takes what `value=` took, including a number for a number box.
- `OnInput` takes the old handler unchanged; it still receives a `ChangeEventArgs`.
- Every other attribute (id, type, maxlength, disabled, `@onkeydown`) goes onto the element.

Unlike the feed composer's `@key` approach, no element is rebuilt when code changes the text, so
focus is never lost.

## What changed

134 boxes in 57 files, converted mechanically by a script, with the build and the unit suite as
the check. That is every `value="@…"` + `@oninput` text, number, email, search, tel, time and url box in
Ben.Web.Website.Library and Ben.Web.Website, including the announcement and
every other site setting (AdminSiteSettings), and the ones Ben listed: NewInvestigationWindow, OrgEquipmentEditor, InvestigationRoster, MyEquipmentItemEditor,
LoanFeedbackForm, EquipmentCheckoutRequestDialog, AskOwnerDialog, EquipmentFaqSection.

The one hand edit was SignUp: a component attribute cannot mix text and C#, so
`class="form-control @HandleFieldClass"` became `class="@($"form-control {HandleFieldClass}")"`.
`Oct16GapsTests` looked for the literal `<input id="…"` on four boxes; it now accepts
`<BenTextBox id="…"` as well (it still renders an `<input>`), with the same label, Enter and
placeholder checks.

Left alone on purpose:
- **Range sliders** (`type="range"`): they have no cursor to lose.
- **Ben.Canvas.Editor**: it runs in the browser (WebAssembly), so nothing lags behind the typing.
- **FeedComposer**: already fixed its own way (`_shown` + `@key`), and the tests here show that
  holds. It could later switch to `BenTextArea` and drop `_shown` / `_shownVersion`.

## Test

`Ben.Web.Playwright/Tests/TypingStaysPutTests.cs` (category `TypingStaysPut`) types a sentence,
puts the cursor mid-sentence, and makes six deletes in one burst, so all six reach the server before
the first reply. It then checks the text, the cursor position and one more real Backspace:
- The site-wide announcement (`SiteAnnouncementTests`), where Ben saw it.
- Contact page message (a `BenTextArea`) and subject (a `BenTextBox`).
- The feed composer (`FeedTests.DeletingMidSentenceStaysPutWhenTheKeysOutrunTheServer`).
- Help search (`@bind:event="oninput"`), the control.

Results, 10/22 (cursor should end at 24):

| Box | Before this branch | With it |
|---|---|---|
| Site-wide announcement | **failed: cursor at 40, the end** | passed |
| Contact message | **failed: 40** | passed |
| Contact subject | **failed: 40** | passed |
| Feed composer (fixed 10/15), burst + key-press tests | passed | passed |
| Help search (`@bind`) | passed | passed |

The rest of `SiteAnnouncementTests` (save, banner shows, clear) also passes with it.

```bash
scripts/run-e2e.sh --filter "TestCategory=TypingStaysPut|FullyQualifiedName~DeletingMidSentence|FullyQualifiedName~SiteAnnouncementTests"
```
