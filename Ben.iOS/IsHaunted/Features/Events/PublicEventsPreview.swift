import SwiftUI
import BenKit

/// Slice-1 proof that the kernel speaks to the real API: fetches
/// `GET api/public/events` anonymously and renders the decoded records —
/// which is also the one-time runtime verification that the camelCase
/// assumption holds. Grows into the real Events feature in Slice 7.
struct PublicEventsPreview: View {
    @Environment(AppDependencies.self) private var dependencies
    @State private var result: LoadResult<[PublicEventListItem]>?

    var body: some View {
        LoadStateView(
            result,
            isEmpty: \.isEmpty,
            emptyTitle: "No upcoming events",
            emptyMessage: "Public events groups post will show up here.",
            retry: { await load() }
        ) { events in
            List(events) { event in
                VStack(alignment: .leading, spacing: 4) {
                    Text(event.title)
                        .font(.headline)
                        .foregroundStyle(Theme.bone)
                    Text(event.organizationName)
                        .font(.subheadline)
                        .foregroundStyle(Theme.fog)
                    HStack(spacing: 6) {
                        Image(systemName: event.isOnline ? "video" : "mappin.and.ellipse")
                        Text(event.startDateTime, format: .dateTime.month().day().hour().minute())
                        if let city = event.city {
                            Text("· \(city)\(event.state.map { ", \($0)" } ?? "")")
                        }
                    }
                    .font(.caption)
                    .foregroundStyle(Theme.fog)
                }
                .padding(.vertical, 2)
            }
            .refreshable { await load() }
        }
        .navigationTitle("Events")
        .task { await load() }
    }

    private func load() async {
        result = await dependencies.api.load(
            Endpoint(.get, "api/public/events", requiresAuth: false),
            as: [PublicEventListItem].self)
    }
}
