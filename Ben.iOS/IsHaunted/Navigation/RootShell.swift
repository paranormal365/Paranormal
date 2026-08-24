import SwiftUI
import BenKit

/// The adaptive chrome: TabView in compact width (iPhone, iPad split-screen),
/// NavigationSplitView sidebar in regular width (full-screen iPad). Driven by
/// size class, NOT device idiom, so Stage Manager and Split View degrade
/// gracefully. All destination screens are shared — only the chrome differs.
struct RootShell: View {
    @Environment(\.horizontalSizeClass) private var sizeClass
    @Environment(Router.self) private var router

    var body: some View {
        if sizeClass == .regular {
            splitView
        } else {
            tabView
        }
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
        case .feed: FeedPlaceholderView()
        case .cases: PlaceholderScreen(section: .cases, slice: "Slice 6")
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
        case .notifications:
            PlaceholderScreen(title: "Notifications", icon: "bell", slice: "Slice 5")
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
