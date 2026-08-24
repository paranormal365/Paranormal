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


    @Test func feedTypeURL() {
        #expect(DeepLinkParser.parse(
            URL(string: "https://ishaunted.com/feed/types/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")!)
            == .feedType(postId))
    }

    @Test func feedTextLinkifiesMentionsAndTags() {
        let mentions = [FeedMentionRecord(
            appUserId: postId, handle: "jamesthornton", displayName: "James")]
        let text = FeedText.attributed(
            body: "clear #EVP with @jamesthornton at #bellwitch", mentions: mentions)
        let links = text.runs.compactMap(\.link)
        #expect(links.contains(URL(string: "ishaunted://feed/people/\(postId.uuidString.lowercased())")!))
        #expect(links.contains(URL(string: "ishaunted://feed/tags/evp")!))
        #expect(links.contains(URL(string: "ishaunted://feed/tags/bellwitch")!))
    }

    @Test func hashtagRuleMatchesTheServers() {
        // Letters lead; a leading digit is a year or a list, not a subject.
        #expect(FeedText.hashtags(in: "#2026 #evp #EVP #a1") == ["evp", "a1"])
    }

    @Test func unknownPathsReturnNilNotACrash() {
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/admin/users")!) == nil)
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/")!) == nil)
    }
}
