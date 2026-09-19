import Foundation

/// One case as its investigating group sees it — the header, its timeline, its files and the
/// thread with the client.
///
/// **iOS-8 of the 2026-09-06 evaluation.** The app had no group-side case view at all: My Cases is
/// the client's list, so a member rostered on a visit could not open the case they were visiting
/// from the phone. This reads the same three org endpoints the website's case page reads.
///
/// **Read-only, deliberately, and for now.** A phone in a dark house is where somebody wants to
/// LOOK something up — what the client said, which photo that was, what the group last told them.
/// Writing to a case is a desk job, and every write has a permission the app would have to model
/// correctly before offering the button.
///
/// **Four loads, not one, and a failure in one does not lose the others.** The header is the only
/// part that decides whether the screen can be shown at all; a timeline that will not load is a
/// section that says so, over a case that still opens. Loading them together and failing together
/// would turn one refused endpoint into "this case is unavailable", which is the mistake this
/// codebase keeps finding (LoadResult, items 119/120).
/// **@Observable, and it was missing.** Without it SwiftUI never watches these properties, so the
/// screen renders its first frame — the spinner — and never redraws when the load finishes. The
/// UI test caught it on the first run that actually reached the screen: "the group case screen
/// opened but drew neither the case nor a reason", which is what a permanent ProgressView looks
/// like from the outside. Every other store in this folder carries the attribute; this one was
/// written from the shape of the others and lost it.
@Observable
@MainActor
public final class GroupCaseStore {
    public enum State: Equatable {
        case loading
        case loaded
        case signedOut
        /// The server said no, or could not be asked. The reason is the sentence to show.
        case failed(reason: String)
    }

    public private(set) var state: State = .loading
    public private(set) var groupCase: GroupCase?

    public private(set) var timeline: [GroupTimelineEntry] = []
    public private(set) var files: [GroupCaseFile] = []
    public private(set) var messages: [GroupCaseMessage] = []

    /// Why a section is empty, when the answer is "we could not ask" rather than "there are none".
    public private(set) var timelineProblem: String?
    public private(set) var filesProblem: String?
    public private(set) var messagesProblem: String?

    private let api: APIClient
    private let organizationId: UUID
    private let caseId: UUID

    public init(organizationId: UUID, caseId: UUID, api: APIClient) {
        self.organizationId = organizationId
        self.caseId = caseId
        self.api = api
    }

    private var org: String { organizationId.uuidString.lowercased() }
    private var kase: String { caseId.uuidString.lowercased() }

    public func load() async {
        if case .loaded = state {} else { state = .loading }

        switch await api.load(Endpoint(.get, "api/organizations/\(org)/cases/\(kase)"),
                              as: GroupCase.self) {
        case .ok(let record):
            groupCase = record
            state = .loaded
        case .sessionEnded:
            state = .signedOut
            return
        case .failed(_, let statusCode) where statusCode == 401:
            state = .signedOut
            return
        case .failed(_, let statusCode) where statusCode == 403:
            // Said as what it is. A member whose group has not given them a functional role gets
            // this, and "not found" would send them looking for a case that is right there
            // (W-M1, and the reason phase 2 exists).
            state = .failed(reason: "Your group hasn't given you access to its cases.")
            return
        case .failed(_, let statusCode) where statusCode == 404:
            state = .failed(reason: "That case isn't available.")
            return
        case .failed(let reason, _):
            state = .failed(reason: reason ?? "The case couldn't be loaded.")
            return
        case .rateLimited:
            state = .failed(reason: "Too many requests — try again shortly.")
            return
        }

        await loadTimeline()
        await loadFiles()
        await loadMessages()
    }

    private func loadTimeline() async {
        switch await api.load(Endpoint(.get, "api/organizations/\(org)/cases/\(kase)/timeline"),
                              as: [GroupTimelineEntry].self) {
        case .ok(let entries):
            timeline = entries.sorted { $0.occurredAt > $1.occurredAt }
            timelineProblem = nil
        case .failed(let reason, _):
            timeline = []
            timelineProblem = reason ?? "The timeline couldn't be loaded."
        case .sessionEnded, .rateLimited(_):
            timeline = []
            timelineProblem = "The timeline couldn't be loaded."
        }
    }

    private func loadFiles() async {
        // A different route prefix from the two above — `api/orgs`, not `api/organizations`.
        // Getting that wrong is a 404 the browser reports and no unit test can see; phase 3 shipped
        // exactly that mistake once.
        switch await api.load(Endpoint(.get, "api/orgs/\(org)/cases/\(kase)/files"),
                              as: [GroupCaseFile].self) {
        case .ok(let loaded):
            files = loaded
            filesProblem = nil
        case .failed(let reason, _):
            files = []
            filesProblem = reason ?? "The files couldn't be loaded."
        case .sessionEnded, .rateLimited(_):
            files = []
            filesProblem = "The files couldn't be loaded."
        }
    }

    private func loadMessages() async {
        switch await api.load(Endpoint(.get, "api/orgs/\(org)/cases/\(kase)/messages"),
                              as: [GroupCaseMessage].self) {
        case .ok(let loaded):
            messages = loaded.sorted { $0.dateCreated < $1.dateCreated }
            messagesProblem = nil
        case .failed(let reason, _):
            messages = []
            messagesProblem = reason ?? "The messages couldn't be loaded."
        case .sessionEnded, .rateLimited(_):
            messages = []
            messagesProblem = "The messages couldn't be loaded."
        }
    }
}
