import SwiftUI
import MapKit
import BenKit

/// Investigations this member is on (iOS Slice 7): what's coming, where they've been, and
/// what has been. The map is the native counterpart of the website's attended map.
struct InvestigationsView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var store: InvestigationsStore?
    @State private var showMap = false

    var body: some View {
        Group {
            switch store?.state {
            case .none, .loading:
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut:
                ContentUnavailableView {
                    Label("Sign in to see your investigations", systemImage: "binoculars")
                } description: {
                    Text("Investigations belong to the group running them.")
                }

            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't load your investigations", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await store?.load() } }
                        .buttonStyle(.borderedProminent)
                }

            case .loaded:
                if let store, store.investigations.isEmpty && store.attended.isEmpty {
                    ContentUnavailableView {
                        Label("No investigations yet", systemImage: "binoculars")
                    } description: {
                        Text("When your group puts you on one, it appears here.")
                    }
                } else if let store {
                    list(store)
                }
            }
        }
        .navigationTitle("Investigations")
        .toolbar {
            if store?.mappable.isEmpty == false {
                ToolbarItem(placement: .primaryAction) {
                    Button { showMap = true } label: { Image(systemName: "map") }
                        .accessibilityLabel("Where you've been")
                }
            }
        }
        .sheet(isPresented: $showMap) {
            AttendedMapView(visits: store?.mappable ?? [])
        }
        .refreshable { await store?.load() }
        .task {
            let store = InvestigationsStore(api: dependencies.api)
            self.store = store
            await store.load()
        }
        .onChange(of: dependencies.session.me?.userId) { _, _ in
            Task { await store?.load() }
        }
    }

    private func list(_ store: InvestigationsStore) -> some View {
        List {
            if !store.upcoming.isEmpty {
                Section("Coming up") {
                    ForEach(store.upcoming) { investigation in
                        NavigationLink(value: AppRoute.investigationDetail(investigation.investigationId)) {
                            InvestigationRow(investigation: investigation)
                        }
                        .accessibilityIdentifier("investigation-row")
                    }
                }
            }
            if !store.past.isEmpty {
                Section("Been and gone") {
                    ForEach(store.past) { investigation in
                        NavigationLink(value: AppRoute.investigationDetail(investigation.investigationId)) {
                            InvestigationRow(investigation: investigation)
                        }
                        .accessibilityIdentifier("investigation-row")
                    }
                }
            }
            if !store.attended.isEmpty {
                Section {
                    // Every visit is listed; only the ones with coordinates can be drawn,
                    // and the count says so rather than quietly showing fewer pins.
                    let mappable = store.mappable.count
                    let total = store.attended.count
                    Button { showMap = true } label: {
                        Label(mappable == total
                              ? "See all \(total) on a map"
                              : "See \(mappable) of \(total) on a map",
                              systemImage: "map")
                    }
                    .disabled(mappable == 0)
                    if mappable < total {
                        // Agreement across the whole sentence, not just the noun: "1 visit
                        // have … so they can't" is the classic half-pluralised string.
                        let undrawn = total - mappable
                        Text(undrawn == 1
                             ? "1 visit has no coordinates recorded, so it can't be drawn."
                             : "\(undrawn) visits have no coordinates recorded, so they can't be drawn.")
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                } header: {
                    Text("Where you've been")
                }
            }
        }
        .listStyle(.insetGrouped)
    }
}

struct InvestigationRow: View {
    let investigation: MyInvestigation

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(investigation.title)
                    .font(.headline).foregroundStyle(Theme.bone)
                Spacer()
                if investigation.rsvp != .noAnswer {
                    Chip(text: investigation.rsvp.label, tint: rsvpTint)
                }
            }
            if let start = investigation.scheduledDateTime {
                Label(start.formatted(date: .abbreviated, time: .shortened),
                      systemImage: "calendar")
                    .font(.caption).foregroundStyle(Theme.fog)
            }
            if let caseTitle = investigation.caseTitle {
                Text("\(investigation.caseReference ?? "") \(caseTitle)")
                    .font(.caption).foregroundStyle(Theme.fog)
            }
            HStack(spacing: 6) {
                Text(investigation.orgName)
                if let role = investigation.assignedRole {
                    Text("· \(role)").foregroundStyle(Theme.ecto)
                }
            }
            .font(.caption).foregroundStyle(Theme.fog)

            if let location = investigation.location {
                Label(location, systemImage: "mappin.and.ellipse")
                    .font(.caption).foregroundStyle(Theme.fog)
            }
            if let due = investigation.evidenceDueDate {
                Label("Evidence due \(due.formatted(date: .abbreviated, time: .omitted))",
                      systemImage: "tray.and.arrow.up")
                    .font(.caption).foregroundStyle(Theme.warning)
            }
        }
        .padding(.vertical, 4)
        .accessibilityElement(children: .combine)
    }

    private var rsvpTint: Color {
        switch investigation.rsvp {
        case .going: Theme.ecto
        case .notGoing: Theme.danger
        case .maybe: Theme.warning
        default: Theme.fog
        }
    }
}

/// Where this member has been — the native counterpart of the website's attended map.
struct AttendedMapView: View {
    @Environment(\.dismiss) private var dismiss
    let visits: [AttendedInvestigation]

    var body: some View {
        NavigationStack {
            Map {
                ForEach(visits) { visit in
                    if let lat = visit.latitude, let lon = visit.longitude {
                        Marker(visit.placeLabel,
                               systemImage: visit.wasLead ? "star.fill" : "mappin",
                               coordinate: CLLocationCoordinate2D(latitude: lat, longitude: lon))
                            .tint(visit.wasLead ? Theme.warning : Theme.ecto)
                    }
                }
            }
            .navigationTitle("Where you've been")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .safeAreaInset(edge: .bottom) {
                // Says what the pins mean rather than leaving a star unexplained.
                if visits.contains(where: \.wasLead) {
                    Label("A star marks an investigation you led.", systemImage: "star.fill")
                        .font(.caption)
                        .padding(8)
                        .background(.thinMaterial, in: Capsule())
                        .padding(.bottom, 8)
                }
            }
        }
    }
}
