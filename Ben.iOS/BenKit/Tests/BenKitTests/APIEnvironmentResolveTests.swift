import Testing
import Foundation
@testable import BenKit

/// A link from the API opens whether the server sent it whole or server-relative (10/21/2026).
@Suite struct APIEnvironmentResolveTests {
    @Test func a_server_relative_link_goes_under_the_environments_host() {
        #expect(APIEnvironment.production.resolve("/api/events/abc/file")?.absoluteString
                == "https://api.ishaunted.com/api/events/abc/file")
        #expect(APIEnvironment.dev.resolve("/api/x")?.absoluteString == "http://localhost:5252/api/x")
    }

    @Test func an_absolute_link_is_left_alone() {
        #expect(APIEnvironment.dev.resolve("https://cdn.example.com/a.png")?.absoluteString
                == "https://cdn.example.com/a.png")
    }

    @Test func nothing_gives_nothing() {
        #expect(APIEnvironment.production.resolve("  ") == nil)
    }

    /// The bug: a bare relative path has no host to open.
    @Test func a_relative_path_alone_has_no_host() {
        #expect(URL(string: "/api/events/abc/file")?.host == nil)
    }
}
