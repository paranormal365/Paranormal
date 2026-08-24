import Foundation
import Testing
@testable import BenKit

@Suite("Deep links — website URLs open the logically matching native screen")
struct DeepLinkParserTests {
    private let postId = UUID(uuidString: "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")!

    @Test func websiteFeedPostURL() {
        let link = DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/feed/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")!)
        #expect(link == .feedPost(postId))
    }

    @Test func schemeFeedPostURL() {
        let link = DeepLinkParser.parse(
            URL(string: "ishaunted://feed/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")!)
        #expect(link == .feedPost(postId))
    }

    @Test func feedProfileAndTags() {
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/feed/people/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")!)
            == .feedProfile(postId))
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/feed/tags/evp")!) == .feedHashtag("evp"))
    }

    @Test func emailConfirmationLink() {
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/validate-email/tok123")!)
            == .confirmEmail(token: "tok123"))
    }

    @Test func attendingTokenLink() {
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/attending/tok456")!)
            == .attending(token: "tok456"))
    }

    @Test func myCasesListAndDetail() {
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/my-cases")!) == .myCases)
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/my-cases/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")!)
            == .myCaseDetail(postId))
    }

    @Test func unknownPathsReturnNilNotACrash() {
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/admin/users")!) == nil)
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/")!) == nil)
    }
}
