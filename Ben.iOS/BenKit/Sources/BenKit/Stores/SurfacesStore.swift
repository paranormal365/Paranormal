import Foundation

/// Holds the server's answer about which parts of the app apply to this person.
///
/// **Why a store rather than a fetch at each screen.** The tab bar asks this question on every
/// render, and a navigation shell that re-fetched on each one would flicker tabs in and out as
/// requests landed. One answer, fetched when the session changes, read everywhere.
@Observable
@MainActor
public final class SurfacesStore {
    /// Everything on until the server says otherwise.
    ///
    /// **The default is deliberately permissive, unlike almost everything else here.** A gate that
    /// fails closed is usually right; this one is not a gate. Hiding somebody's daily tab while a
    /// request is in flight makes the app look broken on every cold start, and the endpoint being
    /// unreachable is not a reason to tell a group member they have no groups. Showing a section
    /// that turns out to be empty costs one screen for a moment; the permission that actually
    /// protects anything is at the endpoint, not here.
    public private(set) var surfaces: MeSurfaces = .all

    /// False until the first successful answer — so a caller can tell "everything, provisionally"
    /// from "everything, confirmed" if it ever needs to.
    public private(set) var loaded = false

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// Asks the server what applies. Safe to call repeatedly; the last answer wins.
    public func refresh() async {
        switch await api.load(Endpoint(.get, "api/me/surfaces"), as: MeSurfaces.self) {
        case .ok(let value):
            surfaces = value
            loaded = true
        case .sessionEnded:
            // Signed out: the public half of the app is what remains, and offering group tabs to
            // somebody with no session would be a row of doors that all ask them to sign in.
            surfaces = .none
            loaded = true
        default:
            // A failed call leaves the previous answer standing rather than collapsing the app's
            // navigation over one bad request.
            break
        }
    }

    /// Back to the provisional default — called on sign-out so the next account starts clean.
    public func reset() {
        surfaces = .all
        loaded = false
    }
}
