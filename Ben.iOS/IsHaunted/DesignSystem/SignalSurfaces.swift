import SwiftUI
import UIKit

/// The website's page and card colours on a SwiftUI `List` or `Form` (Signal, 2026-10-02).
///
/// Left alone, a grouped list paints the system's own grey-on-black — pure black in dark mode,
/// which sat beside the site's blue night like a different app. This hides that background, lays
/// the page colour (`Theme.ink`) behind it and gives every row the card colour (`Theme.mist`), so a
/// list of sessions reads as the site's stack of cards.
struct SignalListSurface: ViewModifier {
    func body(content: Content) -> some View {
        content
            .scrollContentBackground(.hidden)
            .background(Theme.ink)
            // A sheet does not inherit the app's tint, so its icons fell back to system blue
            // (walk, 2026-10-02 — the Send session sheet). Every list and form carries it.
            .tint(Theme.ecto)
            // Scrolling puts the keyboard away. Nothing did before, so on New session the one
            // button sat under the keyboard until somebody thought to press Return (walk,
            // 2026-10-02).
            .scrollDismissesKeyboard(.interactively)
            .toolbarBackground(Theme.ink, for: .navigationBar)
            .listRowBackground(Theme.mist)
    }
}

extension View {
    /// Signal's page behind a `List` or `Form`, Signal's cards for its rows.
    func signalListSurface() -> some View { modifier(SignalListSurface()) }

    /// Signal's page colour behind a whole screen in a navigation stack — the parts no list
    /// covers, like a search bar above one, or a page built from a plain stack. Set once, on every
    /// section's front screen and every pushed page, in `RootShell`.
    func signalPage() -> some View {
        containerBackground(Theme.ink, for: .navigation)
            // Once the page scrolls, its words ran under the title and the clock with nothing
            // behind them and both became unreadable (walk, 2026-10-02). The bar takes the page's
            // colour when content is under it, the way the website's header stays solid.
            .toolbarBackground(Theme.ink, for: .navigationBar)
    }
}

/// A `List` in Signal's colours. `.listRowBackground` only reaches rows when it is set on the
/// list's content — set on the `List` itself it does nothing — so the colours are applied here,
/// inside, once, instead of on every `Section` of every screen.
struct SignalList<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        List {
            content.listRowBackground(Theme.mist)
        }
        .signalListSurface()
    }
}

extension SignalList {
    /// `List(items) { item in … }`, in Signal's colours.
    init<Data, Row>(_ data: Data, @ViewBuilder row: @escaping (Data.Element) -> Row)
    where Content == ForEach<Data, Data.Element.ID, Row>,
          Data: RandomAccessCollection, Data.Element: Identifiable, Row: View {
        self.content = ForEach(data, content: row)
    }
}

/// A `Form` in Signal's colours — see `SignalList`.
struct SignalForm<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        Form {
            content.listRowBackground(Theme.mist)
        }
        .signalListSurface()
    }
}

/// The few UIKit controls SwiftUI gives no style for, in Signal's colours — set once at launch.
///
/// **Main actor.** UIKit's appearance proxies are main-thread only, and Swift 6 says so: without
/// `@MainActor` the 1.1.3 archive logged seven "main actor-isolated … from a nonisolated context"
/// warnings here. It always ran on the main thread — `IsHauntedApp.init` calls it, and an `App`'s
/// init is main-actor — so the annotation states what was already true.
///
/// **Debugging a color that will not take.** An appearance proxy only reaches controls created
/// *after* it is set, so this must run before the first scene draws. If a UIKit control shows the
/// system blue or grey, check it is a control this styles (a `UISegmentedControl`, or a view's
/// tint); SwiftUI's own controls take `.tint` instead. Xcode's Debug View Hierarchy (Debug ▸ View
/// Debugging ▸ Capture View Hierarchy) shows which UIKit class is actually drawing.
@MainActor
enum SignalAppearance {
    static func apply() {
        // The accent for everything UIKit draws under SwiftUI — including a presented sheet's own
        // hosting view, which does not inherit `.tint` from the screen that presented it. The
        // Send session sheet's icons came out system blue until this (walk, 2026-10-02).
        UIView.appearance().tintColor = UIColor(named: "Ecto")

        // A segmented picker (Local time / My time, the feed's tabs) shows its choice as the site's
        // violet pill with white words, not the system's grey.
        let segment = UISegmentedControl.appearance()
        segment.selectedSegmentTintColor = UIColor(named: "Ecto")
        segment.setTitleTextAttributes([.foregroundColor: UIColor.white], for: .selected)
        // Except in a navigation bar (the feed's For You / Latest): there the system draws its own
        // glass pill and ignores the violet, so white words would vanish on it in light mode.
        UISegmentedControl.appearance(whenContainedInInstancesOf: [UINavigationBar.self])
            .setTitleTextAttributes([.foregroundColor: UIColor.label], for: .selected)
    }
}
