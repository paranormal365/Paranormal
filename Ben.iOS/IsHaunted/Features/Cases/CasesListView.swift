import SwiftUI
import BenKit

/// The client's cases (iOS Slice 6). What the person who asked for help sees — deliberately
/// narrower than the investigating group's view of the same case.
struct CasesListView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    @State private var store: CasesStore?

    var body: some View {
        Group {
            switch store?.state {
            case .none, .loading:
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut:
                ContentUnavailableView {
                    Label("Sign in to see your cases", systemImage: "folder.badge.person.crop")
                } description: {
                    Text("A case is between you and the group looking into it.")
                }

            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't load your cases", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await store?.load() } }
                        .buttonStyle(.borderedProminent)
                }

            case .loaded where store?.cases.isEmpty == true:
                ContentUnavailableView {
                    Label("No cases yet", systemImage: "folder")
                } description: {
                    Text("When you ask a group to look into something, it appears here.")
                }

            case .loaded:
                List(store?.cases ?? []) { summary in
                    Button {
                        router.push(.caseDetail(summary.caseId), in: .cases)
                    } label: {
                        CaseSummaryRow(summary: summary)
                    }
                    .buttonStyle(.plain)
                }
                .listStyle(.insetGrouped)
            }
        }
        .navigationTitle("My Cases")
        .refreshable { await store?.load() }
        .task {
            let store = CasesStore(api: dependencies.api)
            self.store = store
            await store.load()
        }
        // Identity resolves a beat after launch; ask again rather than showing "sign in"
        // to somebody who is signing in. (The rule the feed and notifications both learned.)
        .onChange(of: dependencies.session.me?.userId) { _, _ in
            Task { await store?.load() }
        }
    }
}

struct CaseSummaryRow: View {
    let summary: MyCaseSummary

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(summary.caseReference)
                    .font(.caption.monospaced())
                    .foregroundStyle(Theme.fog)
                StatusChip(status: summary.status)
                Spacer()
            }
            Text(summary.title)
                .font(.headline)
                .foregroundStyle(Theme.bone)
            HStack(spacing: 6) {
                if let place = summary.placeLabel {
                    Text(place)
                }
                if let manager = summary.caseManagerDisplayName {
                    Text("· \(manager)")
                }
            }
            .font(.caption)
            .foregroundStyle(Theme.fog)

            if let next = summary.nextInvestigationDate {
                Label(next.formatted(date: .abbreviated, time: .shortened),
                      systemImage: "calendar")
                    .font(.caption)
                    .foregroundStyle(Theme.ecto)
            }
        }
        .padding(.vertical, 4)
        .accessibilityElement(children: .combine)
    }
}

struct StatusChip: View {
    let status: CaseStatus

    var body: some View {
        Text(status.label)
            .font(.caption2.weight(.medium))
            .padding(.horizontal, 7).padding(.vertical, 2)
            .background(tint.opacity(0.18), in: Capsule())
            .foregroundStyle(tint)
    }

    private var tint: Color {
        switch status {
        case .active: Theme.ecto
        case .pending: Theme.warning
        case .closed: Theme.fog
        case .declined: Theme.danger
        case .unknown: Theme.fog
        }
    }
}
