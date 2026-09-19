# The stylesheet nothing ever loaded

`Ben.Web.Website/wwwroot/css/app.css` was never on any page, and had never been on any page. Every
rule in it had gone straight from being written to having no effect.

## How it happened

`App.razor` loads the site's own stylesheet through the asset map:

```razor
<link rel="stylesheet" href="/@Assets["app.css"]" />
```

That resolves to `wwwroot/app.css` — a different, much larger file. Nothing anywhere links
`/css/app.css`. An earlier pass read the link as pointing at `wwwroot/css/app.css`, found no such
file, and committed one to satisfy it; `README-remaining-work-nine-phases.md` still says so, and
that paragraph is corrected here. The file it added has sat unreferenced ever since, collecting
rules from two later commits.

## What was not working

Three things, none of which had ever appeared in a browser:

- **Feed line breaks.** `.bv-feed-post__body { white-space: pre-wrap }`. Without it a post typed as
  three short lines arrived as one run-on paragraph — a visible bug, and the one that matters. Added
  in 2baaefb9.
- **Mentions and tags.** `.bv-feed-mention` / `.bv-feed-tag`: link colour with no underline until
  hover. Also 2baaefb9.
- **Message rows.** `.mail-row` cursor, hover tint, focus ring, and the left-border marker on the
  open message. Added in 067212a9.

## The approach

The rules moved into scoped stylesheets beside the components that own the markup, which is the
pattern the newer feed components already use, and the dead file is gone. No global stylesheet grew.

| File | Rules |
| --- | --- |
| `Ben.Web.Website.Library/Feed/FeedPostCard.razor.css` | `.bv-feed-post__body` |
| `Ben.Web.Website.Library/Feed/FeedText.razor.css` | `.bv-feed-mention`, `.bv-feed-tag` |
| `Ben.Web.Website.Library/Messaging/MailRow.razor.css` | `.mail-row`, `.mail-row--selected` |

No rule needs `::deep`. Each selector names an element written in that component's own `.razor`
file, so the element carries that component's scope attribute. The one that looks like it might is
`.bv-feed-post__body`, whose text is rendered by the `<FeedText>` child — but the rule sits on the
wrapper, and `white-space` and `word-break` are inherited properties that reach the child's text
without the scope having to.

## Verifying it

On the running site, not by reading the CSS. A feed post with deliberate line breaks, a blank line,
a leading indent, a `#tag`, an `@mention` and a long unbroken string was posted to the player
database and read back at 1280 px and 390 px in both themes: every break survived, the long string
broke inside the card, and the page never scrolled sideways. The message list was checked the same
way — the scope attribute is on the row, the hover colour resolves per theme, and the open row
carries its marker. The test post was hidden again afterwards from `/admin/feed-reports`.

`dotnet build Ben.slnx` is clean. `dotnet test Ben.slnx` is 4696 passed with one failure,
`TitleSuggestedRolesTests.Both_halves_are_reachable_from_a_screen`, which fails identically on the
unmodified tree — a write-only-feature guard about suggested roles, unrelated to this change.
