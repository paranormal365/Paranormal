import SwiftUI
import BenKit

/// Top-level sections.
///
/// iPad shows all of them in the sidebar. **iPhone shows five**, because a sixth tab makes iOS
/// collapse the overflow into a "More" list — and Field Kit, which is used one-handed in a dark
/// building, is the last thing that should live two taps deep. Events is the section that gives
/// way: browsing and RSVPing to public events is the least in-the-field thing here, and it keeps
/// a home on the Profile tab.
enum AppSection: String, CaseIterable, Identifiable, Hashable {
    case feed, cases, investigations, fieldKit, events, profile

    /// What the compact shell shows as tabs — five, in the order a night actually runs.
    static let compactTabs: [AppSection] = [.feed, .cases, .investigations, .fieldKit, .profile]

    var id: String { rawValue }

    var title: String {
        switch self {
        case .feed: "Feed"
        case .cases: "My Cases"
        case .investigations: "Investigations"
        case .fieldKit: "Field Kit"
        case .events: "Events"
        case .profile: "Profile"
        }
    }

    var icon: String {
        switch self {
        case .feed: "sparkles.rectangle.stack"
        case .cases: "folder"
        case .investigations: "binoculars"
        case .fieldKit: "gauge.with.needle"
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
    /// A case from the GROUP's side — its screen arrives with the cases slice.
    case orgCase(organizationId: UUID, caseId: UUID)
    case caseReports(UUID)
    case caseMessages(UUID)
    case caseReportPDF(caseId: UUID, reportId: UUID)
    case investigationDetail(UUID)
    case attendedMap
    /// Field Kit's own screens. `fieldKit` is only ever PUSHED on the compact shell's Profile
    /// tab — on iPad it is a section, and pushing it there would stack it on itself.
    case fieldKit
    case fieldSession(UUID)
    case fieldSessionReview(UUID)
    /// Public events as a pushed screen, for the shell that has no Events tab.
    case eventsList
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

    /// Which sections this shell actually shows. Set by RootShell as the size class changes, so
    /// a link to a section that is not on screen lands somewhere real instead of selecting a tab
    /// that does not exist.
    var availableSections: [AppSection] = AppSection.allCases

    func isSection(_ section: AppSection) -> Bool { availableSections.contains(section) }

    /// Opens a top-level area wherever this shell keeps it: as a section when it has one, and
    /// otherwise as a screen pushed onto Profile.
    func openArea(_ section: AppSection, pushing route: AppRoute, then deeper: AppRoute? = nil) {
        if isSection(section) {
            selection = section
            paths[section] = NavigationPath()
            if let deeper { push(deeper, in: section) }
        } else {
            selection = .profile
            paths[.profile] = NavigationPath()
            push(route, in: .profile)
            if let deeper { push(deeper, in: .profile) }
        }
    }

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
            openArea(.events, pushing: .eventsList)
        case .eventDetail(let id):
            openArea(.events, pushing: .eventsList, then: .eventDetail(id))
        case .myCases:
            selection = .cases
            paths[.cases] = NavigationPath()
        case .myCaseDetail(let id): push(.caseDetail(id), in: .cases)
        case .orgCase(let organizationId, let caseId):
            // The GROUP's side of a case — a different surface from the client's view of
            // the same case. Its screen arrives with the cases slice; until then the link
            // resolves and lands on the cases section rather than nowhere.
            selection = .cases
            push(.orgCase(organizationId: organizationId, caseId: caseId), in: .cases)
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
            openArea(.events, pushing: .eventsList)
        }
    }
}
