# fix/feed-composer-cursor

Ben, 10/15/2026, on the website's feed: "When I tried to delete part of a message in the middle of a
sentence, it jumped to the end and started deleting the end of the sentence instead of where I put the
cursor."

## Why

Not the @name or #tag handling: the composer doesn't look for those while typing. The textarea was
rendered with `value="@_body"` and `@oninput` setting `_body`, so every keystroke went to the server and
the server wrote the text back into the box. When the reply lagged a keystroke or two (holding or
tapping Backspace), a stale copy was written over the box, and a browser moves the cursor to the end
whenever a box's value is replaced with different text.

## Fix

The box's `value` is `_shown`: what the code last put in it, never what was typed. Typing updates
`_body` only, so nothing is ever written back while somebody types. Code that changes the text
(inserting an emoji or GIF, clearing after posting) goes through `SetBody`, which also bumps the
textarea's `@key` so the new text always lands.

## Test

`FeedTests.DeletingMidSentenceStaysWhereTheCursorIs` puts the cursor mid-sentence, presses Backspace six
times with no delay, waits for every echo, and checks both the text and the cursor position. Run against
the unfixed composer it fails; with the fix it passes.

The same pattern is in about ten other text boxes on the site; a separate task fixes those.
