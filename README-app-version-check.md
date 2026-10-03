# Check for a new version of the app (10/03/2026)

Ben: *"On your profile page, have a check for updated version which checks the webapi to see if a newer
version has been released."*

## The two halves

| Half | Where | Ships |
|---|---|---|
| `GET api/public/app-version/ios` | this branch → master/develop | with the next website deploy (additive, anonymous) |
| Profile's "Check for updates" | branch `ios-1.1.3` | in iOS 1.1.3, kept off master so a 1.1.2 resubmission could not carry it |

## How the API knows

`AppStoreVersionLookup` asks Apple's public lookup (`itunes.apple.com/lookup?bundleId=…&country=us`)
which version of `com.ishaunted.ios` is live. Nobody keeps a setting up to date after a release; the
answer is right the moment Apple releases. Cached for an hour (a failure for two minutes), so every phone
opening Profile costs Apple one request an hour. Answers 503 with a sentence when Apple can't be asked.
Settings: `AppStore:BundleId`, `AppStore:Country`.

The test fixture is Apple's real answer, fetched the morning 1.1.2 went live (10/03/2026, 01:42 UTC).
