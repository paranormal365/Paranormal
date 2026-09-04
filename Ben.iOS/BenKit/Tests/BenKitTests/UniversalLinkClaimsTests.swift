import Foundation
import Testing
@testable import BenKit

/// The other half of the association file the website serves (item 209).
///
/// The site publishes a list of paths at `/.well-known/apple-app-site-association`, and iOS uses
/// it to send those links here instead of to Safari. Nothing connects the two lists at build
/// time — one is C#, the other is this parser — so this suite restates the claimed list and
/// asserts the app can actually do something with every entry.
///
/// **The failure this exists to prevent.** A claimed path that the parser rejects, or that the
/// router sends to a placeholder, takes somebody away from a working web page and shows them
/// nothing. It cannot be seen in the simulator, because iOS only performs the association check
/// on a real device with a real provisioning profile — so a test is the only place it can be
/// caught before a stranger finds it.
///
/// Keep in step with `AppleAppSiteAssociation.ClaimedPaths` in
/// `Ben.Web.Website/Services/AppleAppSiteAssociation.cs`.
@Suite("Universal links — every path the site claims lands somewhere real")
struct UniversalLinkClaimsTests {
    private let id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

    /// One concrete URL per claimed pattern, since a pattern cannot be parsed.
    private var claimedExamples: [String] {
        [
            "https://ishaunted.com/feed",
            "https://ishaunted.com/feed/\(id)",
            "https://ishaunted.com/feed/people/\(id)",
            "https://ishaunted.com/feed/tags/evp",
            "https://ishaunted.com/feed/types/\(id)",
            "https://ishaunted.com/events",
            "https://ishaunted.com/my-cases",
            "https://ishaunted.com/my-cases/\(id)",
            "https://ishaunted.com/my-investigations",
            "https://ishaunted.com/notifications",
            "https://ishaunted.com/profile",
            "https://ishaunted.com/validate-email/\(id):somecode",
        ]
    }

    @Test func everyClaimedPathParses() {
        for raw in claimedExamples {
            let link = DeepLinkParser.parse(URL(string: raw)!)
            #expect(link != nil, "the site claims \(raw) but the app cannot parse it")
        }
    }

    /// Paths the site deliberately leaves to the website.
    ///
    /// The first two are the dangerous ones: they parse perfectly well, and the router then sends
    /// them to a screen that does not exist yet. They are listed here so that whoever finally
    /// builds those screens finds this test and the association file together.
    @Test func pathsWithNoScreenAreParsedButMustNotBeClaimed() {
        // /events/{id} — parses to .eventDetail, which RootShell has no destination for.
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/events/\(id)")!) == .eventDetail(UUID(uuidString: id)!))

        // /organizations/{org}/cases/{case} — parses, and also has no destination.
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/organizations/\(id)/cases/\(id)")!) != nil)

        // Anything else under /organizations is not a link into the app at all.
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/organizations/\(id)")!) == nil)
    }

    /// Share links (item 207) go to people with no account, and have no app screen.
    @Test func shareLinksAreNotAppLinks() {
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/s/GyPprOUz_cFUmBlsAoNPnQ")!) == nil)
    }

    /// The public website keeps everything the app has no answer for.
    @Test func publicWebsitePathsAreNotAppLinks() {
        for raw in [
            "https://ishaunted.com/o/ghost-squad",
            "https://ishaunted.com/find",
            "https://ishaunted.com/admin/users",
            "https://ishaunted.com/help/your-profile",
        ] {
            #expect(DeepLinkParser.parse(URL(string: raw)!) == nil,
                    "\(raw) parses, so it would open the app if it were ever claimed")
        }
    }

    /// A universal link arrives as https; the same grammar has to serve the custom scheme too,
    /// because linkified @mentions inside the app carry `ishaunted://`.
    @Test func theSameGrammarServesBothSchemes() {
        for raw in claimedExamples {
            let web = DeepLinkParser.parse(URL(string: raw)!)
            let scheme = DeepLinkParser.parse(
                URL(string: raw.replacingOccurrences(
                    of: "https://ishaunted.com/", with: "ishaunted://"))!)
            #expect(web == scheme, "\(raw) parses differently as a scheme link")
        }
    }
}
