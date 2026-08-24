import SwiftUI
import BenKit

/// Top-level sections. Same five everywhere; iPhone shows them as tabs,
/// iPad as a sidebar (plus a Notifications row).
enum AppSection: String, CaseIterable, Identifiable, Hashable {
    case feed, cases, investigations, events, profile

    var id: String { rawValue }

    var title: String {
        switch self {
        case .feed: "Feed"
        case .cases: "My Cases"
        case .investigations: "Investigations"
        case .events: "Events"
        case .profile: "Profile"
        }
    }

    var icon: String {
        switch self {
        case .feed: "sparkles.rectangle.stack"
        case .cases: "folder"
        case .investigations: "binoculars"
        case .events: "calendar"
        case .profile: "person.crop.circle"
        }
    }
}

/// Screens pushable within a section.
enum AppRoute: Hashable {
    case feedPost(UUID)
    case feedProfile(UUID)
    case feedHashtag(String)
    /// A pinned feed slice — a tag page, a type page, an author page.
    case feedFiltered(FeedFilter)
    case notifications
    case messages
    case caseDetail(UUID)
    case caseReportPDF(caseId: UUID, reportId: UUID)
    case investigationDetail(UUID)
    case attendedMap
    case eventDetail(UUID)
    case security
    case developerSettings
    case register
    case confirmEmail(token: String)
    // Reserved for the universal-link RSVP flow: case attending(token: String)
}

/// One router, one selected section, one NavigationPath per section — path
/// state survives switching tabs.
@Observable
@MainActor
final class Router {
    var selection: AppSection = .feed
    private var paths: [AppSection: NavigationPath] = [:]

    func path(for section: AppSection) -> Binding<NavigationPath> {
        Binding(
            get: { self.paths[section] ?? NavigationPath() },
            set: { self.paths[section] = $0 })
    }

    func push(_ route: AppRoute, in section: AppSection? = nil) {
        let target = section ?? selection
        selection = target
        var path = paths[target] ?? NavigationPath()
        path.append(route)
        paths[target] = path
    }

    /// A deep link lands on the logically matching native screen.
    func open(_ link: DeepLink) {
        switch link {
        case .feed:
            selection = .feed
            paths[.feed] = NavigationPath()
        case .feedPost(let id): push(.feedPost(id), in: .feed)
        case .feedProfile(let id): push(.feedProfile(id), in: .feed)
        case .feedHashtag(let tag): push(.feedFiltered(.hashtag(tag)), in: .feed)
        case .feedType(let id): push(.feedFiltered(.experienceType(id, name: nil)), in: .feed)
        case .events:
            selection = .events
            paths[.events] = NavigationPath()
        case .eventDetail(let id): push(.eventDetail(id), in: .events)
        case .myCases:
            selection = .cases
            paths[.cases] = NavigationPath()
        case .myCaseDetail(let id): push(.caseDetail(id), in: .cases)
        case .myInvestigations:
            selection = .investigations
            paths[.investigations] = NavigationPath()
        case .notifications: push(.notifications)
        case .profile:
            selection = .profile
            paths[.profile] = NavigationPath()
        case .confirmEmail(let token): push(.confirmEmail(token: token), in: .profile)
        case .attending:
            // Website-only flow until universal links are hosted (AASA); the
            // route case is reserved so nothing here needs restructuring.
            selection = .events
        }
    }
}
