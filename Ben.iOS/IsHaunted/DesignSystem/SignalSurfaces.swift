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
            .listRowBackground(Theme.mist)
    }
}

extension View {
    /// Signal's page behind a `List` or `Form`, Signal's cards for its rows.
    func signalListSurface() -> some View { modifier(SignalListSurface()) }

    /// Signal's page colour behind a whole screen in a navigation stack — the parts no list
    /// covers, like a search bar above one, or a page built from a plain stack. Set once, on every
    /// section's front screen and every pushed page, in `RootShell`.
    func signalPage() -> some View { containerBackground(Theme.ink, for: .navigation) }
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
enum SignalAppearance {
    static func apply() {
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
