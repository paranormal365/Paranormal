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

    /// Whether this section leads anywhere for the person described by `surfaces`.
    ///
    /// **Ben, 2026-08-31:** somebody investigating alone should not be carrying a My Cases tab
    /// that can never hold anything, or an Investigations tab belonging to groups they have not
    /// joined. An empty screen is not neutral — it reads either as a broken app or as a feature
    /// the person is failing to find.
    ///
    /// Feed, Field Kit and Profile are unconditional on purpose. The first two are the whole of
    /// the app for a solo investigator, and Profile is where signing in and out lives, so hiding
    /// any of them would strand somebody.
    func applies(to surfaces: MeSurfaces) -> Bool {
        switch self {
        case .feed, .fieldKit, .profile: true
        case .cases:          surfaces.hasCases
        case .investigations: surfaces.hasInvestigations
        // Events earns its place for a ghost-walk guest, and for anybody in a group that runs
        // them. A solo investigator who has never attended one is not shown it.
        case .events:         surfaces.attendsPublicEvents || surfaces.hasGroups
        }
    }

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
    /// The guest's own copy of what they offered at somebody's public event, and contributing it
    /// to the place's archive. Theirs, whatever the operator decided about their own gallery.
    case myEvidence
    /// Managing who you've blocked (App Review 1.2) — the block itself happens on a post.
    case blockedAccounts
    /// Deleting your own account — App Review 5.1.1(v) requires this to live in the app.
    case deleteAccount
    /// What the app is and what it does with your data. Reachable signed OUT — a
    /// reviewer with no account still has to be able to find the privacy statement.
    case about
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
