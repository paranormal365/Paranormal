import Foundation

/// Where a link into the app lands. Mirrors the website's URL space
/// (`@page` routes in Ben.Web.Website.Library) so that a link that works on
/// ishaunted.com opens the logically matching native screen.
public enum DeepLink: Sendable, Equatable {
    case feed
    case feedPost(UUID)
    case feedProfile(UUID)
    case feedHashtag(String)
    /// `/feed/types/{id}` — one experience type's posts (item 186 F6).
    case feedType(UUID)
    case events
    case eventDetail(UUID)
    case myCases
    case myCaseDetail(UUID)
    /// `/organizations/{orgId}/cases/{caseId}` — a case from the GROUP's side, which is a
    /// different surface from the client's `myCaseDetail` even for the same case.
    case orgCase(organizationId: UUID, caseId: UUID)
    case myInvestigations
    case notifications
    case profile
    /// `/validate-email/{token}` — the emailed confirmation link.
    case confirmEmail(token: String)
    /// `/attending/{token}` — the emailed no-account event RSVP link.
    case attending(token: String)
}

/// Parses both `ishaunted://…` scheme URLs and `https://ishaunted.com/…`
/// universal links into a `DeepLink`. The path grammar is the WEBSITE's route
/// table — one URL space, two front ends.
public enum DeepLinkParser {
    public static func parse(_ url: URL) -> DeepLink? {
        // Scheme links carry the path in host+path; web links in path alone.
        let components: [String]
        if url.scheme == "ishaunted" {
            let host = url.host.map { [$0] } ?? []
            components = host + url.pathComponents.filter { $0 != "/" }
        } else {
            components = url.pathComponents.filter { $0 != "/" }
        }
        guard !components.isEmpty else { return nil }

        switch components[0].lowercased() {
        case "feed":
            guard components.count > 1 else { return .feed }
            switch components[1].lowercased() {
            case "people":
                guard components.count > 2, let id = UUID(uuidString: components[2]) else { return .feed }
                return .feedProfile(id)
            case "tags":
                guard components.count > 2 else { return .feed }
                return .feedHashtag(components[2])
            case "types":
                guard components.count > 2, let id = UUID(uuidString: components[2]) else { return .feed }
                return .feedType(id)
            default:
                guard let id = UUID(uuidString: components[1]) else { return .feed }
                return .feedPost(id)
            }
        case "events":
            guard components.count > 1, let id = UUID(uuidString: components[1]) else { return .events }
            return .eventDetail(id)
        case "my-cases":
            guard components.count > 1, let id = UUID(uuidString: components[1]) else { return .myCases }
            return .myCaseDetail(id)
        case "organizations":
            // /organizations/{orgId}/cases/{caseId} — the group's side of a case. Anything
            // shorter or otherwise shaped has no app screen, so it stays unhandled rather
            // than landing somebody on a page that isn't what their link said.
            guard components.count > 3,
                  let orgId = UUID(uuidString: components[1]),
                  components[2].lowercased() == "cases",
                  let caseId = UUID(uuidString: components[3])
            else { return nil }
            return .orgCase(organizationId: orgId, caseId: caseId)
        case "my-investigations":
            return .myInvestigations
        case "notifications":
            return .notifications
        case "profile":
            return .profile
        case "validate-email":
            guard components.count > 1 else { return nil }
            return .confirmEmail(token: components[1])
        case "attending":
            guard components.count > 1 else { return nil }
            return .attending(token: components[1])
        default:
            return nil
        }
    }
}
