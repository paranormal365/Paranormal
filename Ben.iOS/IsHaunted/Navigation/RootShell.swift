import SwiftUI
import BenKit

/// The adaptive chrome: TabView in compact width (iPhone, iPad split-screen),
/// NavigationSplitView sidebar in regular width (full-screen iPad). Driven by
/// size class, NOT device idiom, so Stage Manager and Split View degrade
/// gracefully. All destination screens are shared — only the chrome differs.
struct RootShell: View {
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(Router.self) private var router
    @Environment(AppDependencies.self) private var dependencies
    @State private var showSignInFromBanner = false

    var body: some View {
        Group {
            if sizeClass == .regular {
                splitView
            } else {
                tabView
            }
        }
        // Cold start: tokens in the Keychain mean quiet optimistic sign-in.
        .task { await dependencies.session.restore() }
        // The badge follows the session in both directions. Loading it before sign-in
        // resolves would ask as a visitor and be told nothing is waiting; leaving it up
        // after sign-out would show one person's count to the next.
        .onChange(of: dependencies.session.me?.userId) { _, userId in
            Task {
                if userId == nil {
                    dependencies.notifications.clear()
                } else {
                    await dependencies.notifications.load()
                }
            }
        }
        // The session-ended INTERRUPT: a banner over whatever the user was
        // doing — anonymous surfaces keep working, never a sign-in wall.
        .safeAreaInset(edge: .top) {
            if dependencies.session.sessionEndedBanner {
                sessionEndedBanner
            }
        }
        .sheet(isPresented: $showSignInFromBanner) {
            SignInView().environment(dependencies)
        }
    }

    private var sessionEndedBanner: some View {
        HStack(spacing: 12) {
            Image(systemName: "person.crop.circle.badge.xmark")
            Text("Your session ended.")
                .font(.callout)
            Spacer()
            Button("Sign in") {
                dependencies.session.dismissSessionEndedBanner()
                showSignInFromBanner = true
            }
            .font(.callout.bold())
            Button {
                dependencies.session.dismissSessionEndedBanner()
            } label: {
                Image(systemName: "xmark")
                    .accessibilityLabel("Dismiss")
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Theme.warning.opacity(0.5)))
        .padding(.horizontal, 12)
        .transition(.move(edge: .top).combined(with: .opacity))
    }

    private var tabView: some View {
        @Bindable var router = router
        return TabView(selection: $router.selection) {
            ForEach(AppSection.allCases) { section in
                sectionStack(section)
                    .tabItem { Label(section.title, systemImage: section.icon) }
                    .tag(section)
            }
        }
    }

    private var splitView: some View {
        @Bindable var router = router
        return NavigationSplitView {
            List(selection: Binding(
                get: { Optional(router.selection) },
                set: { if let value = $0 { router.selection = value } })
            ) {
                ForEach(AppSection.allCases) { section in
                    Label(section.title, systemImage: section.icon)
                        .tag(section)
                }
                Section {
                    Button {
                        router.push(.notifications)
                    } label: {
                        Label("Notifications", systemImage: "bell")
                            .badge(dependencies.notifications.badgeCount)
                    }
                }
            }
            .navigationTitle("IsHaunted")
        } detail: {
            sectionStack(router.selection)
        }
    }

    private func sectionStack(_ section: AppSection) -> some View {
        NavigationStack(path: router.path(for: section)) {
            sectionRoot(section)
                .navigationDestination(for: AppRoute.self) { route in
                    destination(route)
                }
        }
    }

    @ViewBuilder
    private func sectionRoot(_ section: AppSection) -> some View {
        switch section {
        case .feed: FeedListView()
        case .cases: CasesListView()
        case .investigations: PlaceholderScreen(section: .investigations, slice: "Slice 7")
        case .events: PublicEventsPreview()
        case .profile: SettingsHomeView()
        }
    }

    @ViewBuilder
    private func destination(_ route: AppRoute) -> some View {
        switch route {
        case .developerSettings:
            DeveloperSettingsView()
        case .feedPost(let id):
            FeedThreadView(postId: id)
        case .feedProfile(let id):
            FeedProfileView(appUserId: id)
        case .feedFiltered(let filter):
            FeedListView(fixedFilter: filter)
        case .notifications:
            NotificationsView()
        case .caseDetail(let id):
            CaseDetailView(caseId: id)
        default:
            PlaceholderScreen(title: "Coming soon", icon: "hammer", slice: "a later slice")
        }
    }
}

/// A branded placeholder for screens later slices deliver — visibly a
/// placeholder, never mistakable for an empty state.
struct PlaceholderScreen: View {
    var title: String
    var icon: String
    var slice: String

    init(title: String, icon: String, slice: String) {
        self.title = title
        self.icon = icon
        self.slice = slice
    }

    init(section: AppSection, slice: String) {
        self.init(title: section.title, icon: section.icon, slice: slice)
    }

    var body: some View {
        ContentUnavailableView {
            Label(title, systemImage: icon)
        } description: {
            Text("This screen arrives in \(slice).")
        }
        .navigationTitle(title)
    }
}
