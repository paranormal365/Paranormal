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
    /// A password read from the environment, with no fallback. These used to be literals here;
    /// because the development database is the one ishaunted.com uses, that put working
    /// production credentials in a public repository. An unset variable now stops the test with
    /// its name rather than signing in as somebody real.
    /// A password for an account this test tries to create. Generated, never written down: the
    /// registration here is expected to FAIL, but on a shared database a literal would still be a
    /// working credential in a public repository if it ever succeeded.
    private static func throwawayPassword() -> String {
        let alphabet = Array("abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789")
        let body = String((0..<20).map { _ in alphabet.randomElement()! })
        return "T!\(body)9"
    }

    private static func requiredSecret(_ variable: String) -> String {
        guard let value = ProcessInfo.processInfo.environment[variable], !value.isEmpty else {
            Issue.record("\(variable) is not set — export it before running the live write tests.")
            return ""
        }
        return value
    }

    private static var memberPassword: String {
        Self.requiredSecret("BEN_MEMBER_PASSWORD")
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
            password: Self.throwawayPassword(),
            displayName: "Collision Test",
            handle: "jamesthornton"))

        guard case .failure(let error) = result else {
            Issue.record("expected the taken handle to be refused"); return
        }
        // The point: a real sentence a person can act on, not a status paraphrase.
        #expect(!error.message.contains("The server answered"))
        #expect(error.message.count > 10)
    }

    @Test func loggingAnOccurrenceWithAPhotoReachesTheRealCase() async {
        guard Self.enabled else { return }

        // The case client, not the group member: this is the CLIENT's door.
        let transport = URLSessionTransport()
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let auth = IdentityAuthClient(environment: { .dev }, transport: transport)
        guard case .success(let login) = await auth.login(LoginRequest(
            email: ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com",
            password: Self.requiredSecret("BEN_CLIENT_PASSWORD")))
        else { Issue.record("client sign-in failed"); return }
        await tokens.adopt(login)
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)

        guard case .ok(let cases) = await api.load(
            Endpoint(.get, "api/my-cases"), as: [MyCaseSummary].self),
              let target = cases.first
        else { return }   // no client case on this database — nothing to exercise

        let file = FileManager.default.temporaryDirectory
            .appendingPathComponent("live-occ-\(UUID().uuidString).jpg")
        guard let jpeg = Data(base64Encoded: Self.tinyJpegBase64) else { return }
        try? jpeg.write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }

        let store = await CaseDetailStore(caseId: target.caseId, api: api)
        let marker = "live occurrence check \(UUID().uuidString.prefix(6))"
        let result = await store.logOccurrence(
            eventDateTime: Date().addingTimeInterval(-3600),
            title: marker, body: "Written by the live test suite.",
            media: [MediaUpload(fileURL: file, filename: "occ.jpg",
                                contentType: "image/jpeg", byteCount: Int64(jpeg.count))])

        guard case .success(let entry) = result else {
            if case .failure(let e) = result { Issue.record("logging refused: \(e.message)") }
            return
        }
        #expect(entry.title == marker)
        #expect(await store.failedAttachments == 0)

        // And it comes back on the case, with its photo attached.
        await store.load()
        let saved = await store.detail?.occurrences.first { $0.id == entry.id }
        #expect(saved != nil)
        #expect(saved?.files.isEmpty == false)
        #expect(saved?.fromInvestigators == false)   // the CLIENT wrote it

        // Clean up after itself: this runs against Ben's real dev case, and a suite that
        // leaves debris behind stops being runnable on demand.
        _ = await api.send(Endpoint(.delete,
            "api/my-cases/\(target.caseId.uuidString.lowercased())/occurrences/\(entry.id.uuidString.lowercased())"))
    }

    @Test func aPublishedReportListsAndDownloadsAsARealPDF() async {
        guard Self.enabled else { return }

        let transport = URLSessionTransport()
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let auth = IdentityAuthClient(environment: { .dev }, transport: transport)
        guard case .success(let login) = await auth.login(LoginRequest(
            email: ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com",
            password: Self.requiredSecret("BEN_CLIENT_PASSWORD")))
        else { Issue.record("client sign-in failed"); return }
        await tokens.adopt(login)
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)

        guard case .ok(let cases) = await api.load(
            Endpoint(.get, "api/my-cases"), as: [MyCaseSummary].self), let target = cases.first
        else { return }

        let store = await CaseReportsStore(caseId: target.caseId, api: api)
        await store.load()
        guard let report = await store.reports.first else {
            return   // no published report on this database — nothing to exercise
        }

        guard case .success(let url) = await store.downloadPDF(report) else {
            Issue.record("the report would not download"); return
        }
        defer { try? FileManager.default.removeItem(at: url) }

        // A bearer-token route with no Range support: the ONLY safe way to read it is to
        // download it whole. Proving it is a real PDF is the point — an unauthorized fetch
        // returns a perfectly well-formed empty body that a viewer renders as a blank page.
        let bytes = (try? Data(contentsOf: url)) ?? Data()
        #expect(bytes.count > 500)
        #expect(bytes.prefix(5) == Data("%PDF-".utf8))
    }

    @Test func aMessageToTheGroupReachesTheRealCase() async {
        guard Self.enabled else { return }

        let transport = URLSessionTransport()
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let auth = IdentityAuthClient(environment: { .dev }, transport: transport)
        guard case .success(let login) = await auth.login(LoginRequest(
            email: ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com",
            password: Self.requiredSecret("BEN_CLIENT_PASSWORD")))
        else { Issue.record("client sign-in failed"); return }
        await tokens.adopt(login)
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)

        guard case .ok(let cases) = await api.load(
            Endpoint(.get, "api/my-cases"), as: [MyCaseSummary].self), let target = cases.first
        else { return }

        let store = await CaseMessagesStore(caseId: target.caseId, api: api)
        await store.load()
        let before = await store.messages.count

        let marker = "live message check \(UUID().uuidString.prefix(6))"
        guard case .success(let sent) = await store.send(marker) else {
            Issue.record("the message was refused"); return
        }
        // The SERVER decided which side this is. A client's message coming back as the group's
        // would put their own words on the wrong side of every screen.
        #expect(sent.senderSide == .client)
        #expect(sent.body == marker)
        #expect(await store.messages.count == before + 1)

        // And it is really there on a fresh read, not just in memory.
        let fresh = await CaseMessagesStore(caseId: target.caseId, api: api)
        await fresh.load()
        #expect(await fresh.messages.contains { $0.id == sent.id })
    }

    /// An 8×8 black JPEG — small enough to inline, real enough to decode (and to render:
    /// a thumbnail of it looks like a black square because it IS one).
    private static let tinyJpegBase64 = """
        /9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a\
        HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAAIAAgBAREA/8QAHwAAAQUBAQEB\
        AQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1Fh\
        ByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZ\
        WmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXG\
        x8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+v//Z
        """
}
