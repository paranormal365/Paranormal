# Clearing the 43 older build warnings (10/27/2026)

Branch `fix/older-warnings-2026-10-27`. Ben asked for the 43 warnings left in `Ben.Data.WebApi` and
`Ben.Web.Tests` after the site audit to be cleared, with each nullable one checked for a real bug.
The whole solution now builds with 0 warnings (`dotnet build Ben.slnx -c Release --no-incremental`).

## Real bugs behind the warnings

| Where | What was wrong | Fix |
|---|---|---|
| `UploadFileController` thumbnail route | A helper method had been inserted between the thumbnail route and its comment and `[DisableRateLimiting]` attribute. The attribute landed on the private helper, so picture thumbnails were counted against the sitewide rate limit. The only symptom at build time was a misplaced-comment warning. | The comment and attribute are back on `Thumbnail`. A new test, `FileServingSkipsTheRateLimitTests`, failed on the old code and passes now. |
| `StoreImageStorage.SaveAsync` | A store picture uploaded without a file name got a null name, because `Path.ChangeExtension(null)` returns null. | It falls back to `picture.jpg`. |
| Places with no name | `Place.Name` is nullable. An unnamed place would have produced the email "A file was added to  and is waiting", an empty link in the field archive, and blank rows in the venue review lists. | The email and the archive link say "this place"; the review lists say "An unnamed place". |
| `RateLimiting`, `EvpDetector` | A method's documentation had drifted onto the next method, which then had two summaries. | Each block is back on its own method. |

## Checked, not bugs

- **`StoreOrderTransitions` `alerts`:** the parameter is unused because shipping alerts moved to per-package shipping in P7. That code sends them, so no alert went missing. The parameter is removed.
- **`FieldSessionPublishController`:** the tuple types were mismatched, but a file with no stored path was already held for review. Only the types are fixed.
- **`StoreLowStockJob`:** the null subject only mattered in memory, because SQL translates the comparison safely. A null check was added anyway.
- **`CmsGroupSections`:** the `!` was in the wrong place. It now uses `OfType<GalleryFile>()`.

## The rest

- Doc comments inside positional record parameter lists, which the compiler ignores, are now plain `//` comments with the same words.
- Ambiguous crefs are now spelled out.
- Param tags that pointed nowhere are fixed.
- A duplicate `using` is removed.
- Stripe's obsolete `CalculationService.ListLineItemsAutoPagingAsync` is replaced by `CalculationLineItemService.ListAutoPagingAsync`, which calls the same endpoint.
- xUnit analyzer suggestions in the tests are applied.

There's no migration.
