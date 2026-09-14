import SwiftUI
import BenKit

/// What is being served, meal by meal, in the order the venue arranged it (item 235 phase 14b).
///
/// Times are the venue's own, as the host typed them — "Breakfast, 8:30 AM" is never converted to wherever the
/// phone happens to be.
struct MenusView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var menus: HostedEventMenus?
    @State private var loaded = false
    @State private var failure: String?

    var body: some View {
        Group {
            if let menus, !menus.menus.isEmpty {
                List {
                    ForEach(menus.menus.sorted(by: order)) { menu in
                        Section { meal(menu) } header: { Text(nightTitle(menu)) }
                    }
                }
                .listStyle(.insetGrouped)
            } else if !loaded {
                ProgressView("Loading the menus…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let failure {
                ContentUnavailableView {
                    Label("Couldn't load the menus", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure)
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            } else {
                ContentUnavailableView("No menus yet", systemImage: "fork.knife",
                                       description: Text("The venue shares its menus once your place is agreed."))
            }
        }
        .navigationTitle("Menus")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
    }

    @ViewBuilder
    private func meal(_ menu: HostedEventMenu) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline) {
                Text(menu.title).font(.headline)
                Spacer()
                if let time = menu.servedAtText { Text(time).font(.subheadline.monospacedDigit()).foregroundStyle(Theme.fog) }
            }
            if let notes = menu.notes, !notes.isEmpty { Text(notes).font(.footnote).foregroundStyle(Theme.fog) }
        }
        ForEach(courses(menu), id: \.name) { course in
            VStack(alignment: .leading, spacing: 6) {
                if !course.name.isEmpty {
                    Text(course.name.uppercased()).font(.caption2.weight(.semibold)).foregroundStyle(Theme.fog)
                }
                ForEach(course.items) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(item.name)
                        if let description = item.description, !description.isEmpty {
                            Text(description).font(.caption).foregroundStyle(Theme.fog)
                        }
                        if !item.tags.isEmpty {
                            HStack(spacing: 4) {
                                ForEach(item.tags, id: \.self) { StatusBadge(text: $0, colour: Theme.ecto) }
                            }
                        }
                    }
                }
            }
        }
    }

    private struct Course { let name: String; let items: [HostedEventMenuItem] }

    /// Courses in the order their first item appears, so a host's "Starter, Main, Pudding" stays in that order.
    private func courses(_ menu: HostedEventMenu) -> [Course] {
        var order: [String] = []
        var grouped: [String: [HostedEventMenuItem]] = [:]
        for item in menu.items.sorted(by: { $0.sortOrder < $1.sortOrder }) {
            let name = item.course ?? ""
            if grouped[name] == nil { order.append(name) }
            grouped[name, default: []].append(item)
        }
        return order.map { Course(name: $0, items: grouped[$0]!) }
    }

    private func order(_ a: HostedEventMenu, _ b: HostedEventMenu) -> Bool {
        a.nightDate == b.nightDate ? a.sortOrder < b.sortOrder : a.nightDate < b.nightDate
    }

    private func nightTitle(_ menu: HostedEventMenu) -> String {
        let date = menu.nightDate.formatted(Date.FormatStyle(timeZone: .gmt).weekday(.abbreviated).month(.twoDigits).day(.twoDigits))
        guard let title = menu.nightTitle, !title.isEmpty else { return date }
        return "\(date) · \(title)"
    }

    private func load() async {
        switch await HostedEventsStore(api: dependencies.api).loadMenus(hostedEventId) {
        case .ok(let value):
            menus = value
            failure = nil
        case .failed(let reason, _):
            failure = reason ?? "Check your connection and try again."
        case .sessionEnded:
            failure = "Sign in again to see the menus."
        case .rateLimited:
            failure = "Too many requests — try again shortly."
        }
        loaded = true
    }
}
