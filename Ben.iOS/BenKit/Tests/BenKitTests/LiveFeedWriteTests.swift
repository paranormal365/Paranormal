import Foundation
import Testing
@testable import BenKit

/// The write path against the REAL API, opt-in.
///
/// Every other test in this package drives `MockTransport`, which proves the bytes BenKit
/// produces are the bytes it intends — not that a server accepts them. This one closes
/// that gap: real sign-in, real bearer token, real multipart upload, real post created,
/// real like/unlike. It is the nearest thing to a finger on the screen that does not need
/// one, and it caught nothing the mocks could have.
///
/// Opt-in because it needs a running dev API, a seeded member, and the feed switched on:
///
///     BEN_LIVE=1 swift test --package-path BenKit --filter LiveFeedWriteTests
///
/// Without `BEN_LIVE=1` — CI, a fresh checkout, a laptop with nothing running — every test
/// here returns immediately rather than failing, in the same spirit as the .NET suite's
/// model-gated screening test.
@Suite("Live feed writes (opt-in: BEN_LIVE=1)")
struct LiveFeedWriteTests {

    private static var enabled: Bool { ProcessInfo.processInfo.environment["BEN_LIVE"] == "1" }

    private static var memberEmail: String {
        ProcessInfo.processInfo.environment["BEN_MEMBER_EMAIL"] ?? "james.thornton@benco.dev"
    }
    private static var memberPassword: String {
        ProcessInfo.processInfo.environment["BEN_MEMBER_PASSWORD"] ?? "J@mes!Thornton26"
    }

    /// A signed-in client pointed at the dev API, or nil when the environment isn't there.
    ///
    /// Also nil when the PUBLIC FEED IS SWITCHED OFF, which is the site's resting state: the
    /// whole feed controller 404s while it is dark, so these tests have nothing to exercise.
    /// That is a fact about the site, not a failure — the same reasoning as the .NET suite's
    /// model-gated screening test. Turn the feed on to run them.
    private func signedIn() async -> (APIClient, FeedActions)? {
        guard Self.enabled else { return nil }

        let transport = URLSessionTransport()
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let auth = IdentityAuthClient(environment: { .dev }, transport: transport)

        guard case .success(let response) = await auth.login(
            LoginRequest(email: Self.memberEmail, password: Self.memberPassword))
        else {
            Issue.record("BEN_LIVE=1 but sign-in failed — is the dev API up on :5252?")
            return nil
        }
        await tokens.adopt(response)

        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)

        // Is the feed even on? A 404 here is the switch, not a broken endpoint.
        if case .failed(_, let statusCode) = await api.load(
            Endpoint(.get, "api/feed", query: [URLQueryItem(name: "mode", value: "all")]),
            as: FeedPageRecord.self), statusCode == 404 {
            return nil
        }

        return (api, FeedActions(api: api))
    }

    @Test func aTextPostReachesTheRealFeed() async {
        guard let (api, actions) = await signedIn() else { return }

        let tag = "t\(UUID().uuidString.prefix(8))".lowercased()
        let result = await actions.createPost(body: "live write check #\(tag)")

        guard case .success(let post) = result else {
            Issue.record("create refused: \(result)")
            return
        }
        #expect(post.body.contains(tag))
        // The server parsed the tag out of the body into its own table — proof the post
        // went through the real pipeline, not just an echo.
        #expect(post.hashtags.contains { $0.lowercased() == tag })
        #expect(post.isOwnPost)

        // And it is readable back through the feed.
        let page = await api.load(
            Endpoint(.get, "api/feed", query: [URLQueryItem(name: "mode", value: "all")]),
            as: FeedPageRecord.self)
        #expect(page.value?.posts.contains { $0.id == post.id } == true)
        #expect(page.value?.canPost == true)
    }

    @Test func aPhotoPostIsAcceptedAndScreened() async {
        guard let (_, actions) = await signedIn() else { return }

        // A real JPEG: the ingest pipeline DECODES uploads, so bytes that merely claim to
        // be an image are refused — correctly.
        let file = FileManager.default.temporaryDirectory
            .appendingPathComponent("live-\(UUID().uuidString).jpg")
        guard let jpeg = Data(base64Encoded: Self.tinyJpegBase64) else {
            Issue.record("fixture decode failed"); return
        }
        try? jpeg.write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }

        let result = await actions.createPost(
            body: "live media check #t\(UUID().uuidString.prefix(6))".lowercased(),
            media: MediaUpload(fileURL: file, filename: "live.jpg",
                               contentType: "image/jpeg", byteCount: Int64(jpeg.count)))

        guard case .success(let post) = result else {
            Issue.record("media post refused: \(result)")
            return
        }
        // Either the automatic screener cleared it inline (hasMedia) or it is waiting for a
        // moderator (mediaAwaitingReview, author-only). Both are correct; a post claiming
        // neither would mean the media vanished silently.
        #expect(post.hasMedia || post.mediaAwaitingReview)
        if post.hasMedia { #expect(post.mediaKind == .image) }
    }

    @Test func likeAndUnlikeRoundTripAgainstTheRealServer() async {
        guard let (api, actions) = await signedIn() else { return }

        let result = await actions.createPost(body: "live like check #t\(UUID().uuidString.prefix(6))".lowercased())
        guard case .success(let post) = result else {
            Issue.record("create refused: \(result)"); return
        }

        #expect(await actions.setLiked(true, postId: post.id))
        var thread = await api.load(
            Endpoint(.get, "api/feed/posts/\(post.id.uuidString.lowercased())"),
            as: [FeedPostRecord].self)
        #expect(thread.value?.first?.likedByCurrentUser == true)
        #expect(thread.value?.first?.likeCount == 1)

        // Liking twice is liking once — the server's composite key, not a client guard.
        #expect(await actions.setLiked(true, postId: post.id))
        thread = await api.load(
            Endpoint(.get, "api/feed/posts/\(post.id.uuidString.lowercased())"),
            as: [FeedPostRecord].self)
        #expect(thread.value?.first?.likeCount == 1)

        #expect(await actions.setLiked(false, postId: post.id))
        thread = await api.load(
            Endpoint(.get, "api/feed/posts/\(post.id.uuidString.lowercased())"),
            as: [FeedPostRecord].self)
        #expect(thread.value?.first?.likeCount == 0)
    }

    @Test func theTaxonomyTheComposerOffersIsTheRealOne() async {
        guard let (_, actions) = await signedIn() else { return }

        let taxonomy = await actions.experienceTaxonomy()
        #expect(!taxonomy.isEmpty)
        // The categories the website's composer shows, from the platform taxonomy.
        #expect(taxonomy.contains { $0.category.name == "Audible" })
        #expect(taxonomy.allSatisfy { !$0.selectableTypes.isEmpty })
    }

    @Test func theRealRegisterEndpointRefusesWithASentenceNotAStatus() async {
        guard Self.enabled else { return }

        // Against the REAL endpoint: a taken handle must come back as the server's own
        // sentence. This is the case the mock could only assume — and the reason
        // `register` bypasses the prose mapper, since the refusal is JSON, not prose.
        let transport = URLSessionTransport()
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let actions = AccountActions(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))

        // A handle that certainly exists: the seeded member's.
        let result = await actions.register(RegisterRequest(
            email: "collision-\(UUID().uuidString.prefix(8))@example.test",
            password: "N3wUser!Test26",
            displayName: "Collision Test",
            handle: "jamesthornton"))

        guard case .failure(let error) = result else {
            Issue.record("expected the taken handle to be refused"); return
        }
        // The point: a real sentence a person can act on, not a status paraphrase.
        #expect(!error.message.contains("The server answered"))
        #expect(error.message.count > 10)
    }

    /// An 8×8 gray JPEG — small enough to inline, real enough to decode.
    private static let tinyJpegBase64 = """
        /9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a\
        HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAAIAAgBAREA/8QAHwAAAQUBAQEB\
        AQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1Fh\
        ByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZ\
        WmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXG\
        x8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+v//Z
        """
}
