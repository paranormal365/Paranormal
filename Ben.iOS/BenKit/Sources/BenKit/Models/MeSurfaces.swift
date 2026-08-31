import Foundation

/// Which parts of the app apply to this person at all — the server's answer, not the app's guess.
///
/// **What it answers is "is there anything here?", not "may you do this?"** Permission is decided
/// at each endpoint and stays there. This decides whether a door leads anywhere today, which is a
/// different question with different answers: somebody may be perfectly entitled to open a case
/// and still have none, and an empty screen is the wrong way to tell them so.
///
/// Ben, 2026-08-31: "if they are alone and investigating alone, they should not see things that
/// should be available to people who log in and are members of groups."
public struct MeSurfaces: Codable, Sendable, Equatable {
    public let hasGroups: Bool
    public let administersAGroup: Bool
    public let hasCases: Bool
    public let hasInvestigations: Bool
    /// A confirmed attendance at a public event — the ghost-walk guest. Past ones count.
    public let attendsPublicEvents: Bool
    public let hasOwnFieldSessions: Bool

    public init(hasGroups: Bool, administersAGroup: Bool, hasCases: Bool,
                hasInvestigations: Bool, attendsPublicEvents: Bool, hasOwnFieldSessions: Bool) {
        self.hasGroups = hasGroups
        self.administersAGroup = administersAGroup
        self.hasCases = hasCases
        self.hasInvestigations = hasInvestigations
        self.attendsPublicEvents = attendsPublicEvents
        self.hasOwnFieldSessions = hasOwnFieldSessions
    }

    /// What a signed-out visitor gets: the public half of the app and nothing that needs an
    /// account. Also the safe answer when the call fails — see `SurfacesStore`.
    public static let none = MeSurfaces(
        hasGroups: false, administersAGroup: false, hasCases: false,
        hasInvestigations: false, attendsPublicEvents: false, hasOwnFieldSessions: false)

    /// Everything on, for the window before the answer arrives.
    ///
    /// **Deliberately the opposite default from `none`.** Hiding a tab somebody uses every day
    /// while a request is in flight makes the app look broken on every cold start; showing one
    /// that turns out not to apply costs a single empty screen for a moment. The wrong direction
    /// here is the one that flickers things away from people.
    public static let all = MeSurfaces(
        hasGroups: true, administersAGroup: true, hasCases: true,
        hasInvestigations: true, attendsPublicEvents: true, hasOwnFieldSessions: true)
}
