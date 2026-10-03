import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Profile's "check for updates" (Ben, 10/03/2026): what the app makes of the live version.
@Suite("App update check")
struct AppUpdateCheckTests {

    private func live(_ version: String, minimumOS: String? = "18.0") -> AppVersionRecord {
        AppVersionRecord(platform: "ios", latestVersion: version, releasedUtc: nil,
                         releaseNotes: "Fixes.", storeUrl: "https://apps.apple.com/us/app/ishaunted/id6806786633",
                         minimumOsVersion: minimumOS)
    }

    @Test func versionsCompareByNumberNotByText() {
        #expect(AppVersionNumber.compare("1.1.10", "1.1.9") == .orderedDescending)
        #expect(AppVersionNumber.compare("1.2", "1.2.0") == .orderedSame)
        #expect(AppVersionNumber.compare("1.1.2", "1.1.3") == .orderedAscending)
        #expect(AppVersionNumber.compare("2.0", "1.9.9") == .orderedDescending)
        #expect(AppVersionNumber.compare("1.1.3b", "1.1.3") == .orderedSame)
    }

    @Test func theSameVersionIsUpToDate() {
        #expect(AppUpdateStatus.decide(current: "1.1.2", phoneOS: "26.0", live: live("1.1.2"))
                == .upToDate(current: "1.1.2"))
    }

    /// A build newer than the store's — 1.1.3 in TestFlight while 1.1.2 is live — is not offered a downgrade.
    @Test func aBuildAheadOfTheStoreIsUpToDate() {
        #expect(AppUpdateStatus.decide(current: "1.1.3", phoneOS: "26.0", live: live("1.1.2"))
                == .upToDate(current: "1.1.3"))
    }

    @Test func aNewerReleaseIsOffered() {
        #expect(AppUpdateStatus.decide(current: "1.1.2", phoneOS: "26.0", live: live("1.1.3"))
                == .available(live("1.1.3")))
    }

    /// The store will not install a version the phone's iOS is too old for; saying "update" would be a dead end.
    @Test func aReleaseThePhoneCannotInstallSaysWhy() {
        #expect(AppUpdateStatus.decide(current: "1.1.2", phoneOS: "17.6", live: live("1.1.3", minimumOS: "18.0"))
                == .needsNewerIOS(live("1.1.3", minimumOS: "18.0"), phoneOS: "17.6"))
    }

    @Test func noMinimumIsNoObstacle() {
        #expect(AppUpdateStatus.decide(current: "1.1.2", phoneOS: "17.0", live: live("1.1.3", minimumOS: nil))
                == .available(live("1.1.3", minimumOS: nil)))
    }

    /// Captured from the running API (api/public/app-version/ios), which read Apple's live answer.
    @Test func theLiveAnswerDecodes() throws {
        let record = try BenJSON.decoder.decode(
            AppVersionRecord.self, from: try Fixtures.data("app-version-ios", in: Bundle.module))
        #expect(record.platform == "ios")
        #expect(record.latestVersion == "1.1.2")
        #expect(record.storeUrl.hasPrefix("https://apps.apple.com/"))
        #expect(record.minimumOsVersion == "18.0")
        #expect(record.releasedUtc != nil)
    }
}
