# feature/xcode-recommended-settings (10/15/2026)

Xcode 27.1 flagged "Update to recommended settings" on the iOS project. Held until 1.1.4 (14) was
uploaded, then applied on this branch exactly as Xcode offered them:

- **Recommended compiler warnings** (26 `CLANG_WARN_*` / `GCC_WARN_*` settings). Almost all are for
  C and Objective-C; the app is Swift, so they find little, but they keep the project at Xcode's
  baseline and stop the prompt.
- **String Catalog symbol generation** (`STRING_CATALOG_GENERATE_SYMBOLS = YES`): Swift symbols for
  manually added catalog strings.
- `LastUpgradeCheck` 1600 → 2710 in the project and scheme.

Not taken: "Generated Asset Symbol Extensions" (Xcode left it unticked).

Verified: Release device build with Xcode 27.1 (27A9275), no warnings. Ships with the next version
after 1.1.4.
