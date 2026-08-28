# App Store screenshots

Captured 08/28/2026 by `IsHauntedUITests/AppStoreScreenshotTests` against the dev API
(seeded data; the four feed posts were written for this set and the e2e debris hidden
through the real moderation queue).

| Folder | Device | Size |
|---|---|---|
| `iphone-6.9` | iPhone 17 Pro Max | 1320×2868 — the required 6.9" slot |
| `ipad-13` | iPad Pro 13-inch | 2064×2752 — the required 13" slot |

`01-home` (feed), `03-investigations`, `04-field-kit`, `05-events` are the signed-in owner
account; `02-cases` is the client seed account, which is the one with a case to show.

**No profile screenshot, deliberately:** in a Debug build the Profile screen carries the
DEBUG-only "Developer" section, and Apple rejects screenshots showing debug UI. If a profile
shot is ever wanted, capture it from a Release build (where the section does not compile).

To refresh after UI changes:

    xcrun simctl keychain <udid> reset   # tokens survive app uninstall — reset between accounts
    TEST_RUNNER_BEN_SCREENSHOTS=1 xcodebuild -project IsHaunted.xcodeproj -scheme IsHaunted \
      -destination 'platform=iOS Simulator,id=<udid>' \
      -only-testing:IsHauntedUITests/AppStoreScreenshotTests \
      -resultBundlePath /tmp/shots.xcresult test
    xcrun xcresulttool export attachments --path /tmp/shots.xcresult --output-path <dir>

Account overrides ride `TEST_RUNNER_BEN_CLIENT_EMAIL` / `TEST_RUNNER_BEN_CLIENT_PASSWORD`.
