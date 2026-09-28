import SwiftUI
import BenKit

/// Sessions other people published — near here, or at a place looked up by name.
///
/// Ben, 2026-09-27: "a server lookup to see if there are any available public .ben files from
/// nearby", and "be able to look up .ben files based on looking up locations". Each one downloads
/// as the public copy and plays exactly like a session recorded on this phone, with its positions
/// at the place's public point.
struct PublicSessionsView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    @State private var locator = FieldLocator()
    @State private var query = ""
    @State private var rows: [PublicArchiveSession] = []
    @State private var loading = false
    @State private var problem: String?
    /// What the list is showing, for its headings and its empty sentence.
    @State private var showing: Showing = .nothingYet
    @State private var downloading: UUID?

    private enum Showing: Equatable { case nothingYet, near, search(String) }

    var body: some View {
        List {
            if showing != .nothingYet || loading || problem != nil {
                resultsSection
            }
            if case .search = showing {} else { nearSection }
        }
        .navigationTitle("Public sessions")
        .searchable(text: $query, placement: .navigationBarDrawer(displayMode: .always),
                    prompt: "A place or a town")
        .onSubmit(of: .search) { Task { await search() } }
        .onChange(of: query) { _, text in
            if text.trimmingCharacters(in: .whitespaces).isEmpty, case .search = showing {
                Task { await lookNearby() }
            }
        }
        .task { if locator.access == .allowed { await lookNearby() } }
    }

    // MARK: - Sections

    @ViewBuilder
    private var nearSection: some View {
        switch locator.access {
        case .notAsked:
            Section {
                Button {
                    Task {
                        await locator.requestAccess()
                        await lookNearby()
                    }
                } label: {
                    Label("Find sessions near me", systemImage: "location")
                }
                .accessibilityIdentifier("public-sessions-use-location")
            } footer: {
                Text("Or search for a place or a town above.")
            }
        case .refused:
            Section {
                Label("Location is off for IsHaunted, so sessions near you can't be found. Search for a place or a town instead.",
                      systemImage: "location.slash")
                    .font(.callout).foregroundStyle(Theme.fog)
                OpenSettingsButton()
            }
        case .allowed:
            EmptyView()
        }
    }

    @ViewBuilder
    private var resultsSection: some View {
        Section {
            if loading || locator.isLocating {
                HStack { ProgressView(); Text(locator.isLocating ? "Finding where you are…" : "Looking…").padding(.leading, 6) }
            } else if let problem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.callout).foregroundStyle(Theme.warning)
            } else if rows.isEmpty {
                Text(emptySentence).font(.callout).foregroundStyle(Theme.fog)
                    .accessibilityIdentifier("public-sessions-empty")
            } else {
                ForEach(rows) { row in sessionRow(row) }
            }
        } header: {
            switch showing {
            case .near: Text("Recorded within 10 miles")
            case .search(let text): Text("Places matching “\(text)”")
            case .nothingYet: EmptyView()
            }
        } footer: {
            if !rows.isEmpty {
                Text("Public copies: every position is the place's public point, not where the recorder walked.")
            }
        }
    }

    private var emptySentence: String {
        switch showing {
        case .near: "Nobody has published a session within 10 miles of here yet."
        case .search(let text): "Nothing published at a place matching “\(text)”."
        case .nothingYet: ""
        }
    }

    private func sessionRow(_ row: PublicArchiveSession) -> some View {
        let onPhone = dependencies.fieldKit.summary(for: row.id) != nil
        return HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(row.placeTitle).font(.headline).foregroundStyle(Theme.bone)
                if !row.whereLine.isEmpty {
                    Text(row.whereLine).font(.caption).foregroundStyle(Theme.fog)
                }
                Text("\(row.startedAt.readerFormatted(date: .abbreviated, time: .shortened)) · by \(row.recordedBy)")
                    .font(.caption).foregroundStyle(Theme.fog)
                Text(counts(row)).font(.caption2).foregroundStyle(Theme.fog)
            }
            Spacer(minLength: 8)
            if onPhone {
                Button("Open") { router.push(.fieldSessionReview(row.id)) }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("open-public-session")
            } else if !row.canDownload {
                Text("On the website only").font(.caption2).foregroundStyle(Theme.fog)
                    .multilineTextAlignment(.trailing)
            } else if downloading == row.id {
                ProgressView()
            } else {
                Button {
                    Task { await download(row) }
                } label: {
                    Image(systemName: "arrow.down.circle").font(.title3)
                }
                .disabled(downloading != nil)
                .accessibilityLabel("Download \(row.placeTitle), \(row.startedAt.readerFormatted(date: .abbreviated, time: .omitted))")
                .accessibilityIdentifier("download-public-session")
            }
        }
        .padding(.vertical, 2)
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("public-session-row")
    }

    private func counts(_ row: PublicArchiveSession) -> String {
        var parts = ["\(row.readingCount) readings"]
        if row.markerCount > 0 { parts.append("\(row.markerCount) marked") }
        parts.append(row.mediaCount == 0 ? "no recordings" : "\(row.mediaCount) recordings")
        return parts.joined(separator: " · ")
    }

    // MARK: - Work

    private func lookNearby() async {
        problem = nil
        guard let point = await locator.locate() else {
            if locator.access == .allowed {
                problem = "Couldn't work out where you are. Search for a place or a town instead."
                showing = .near
            }
            return
        }
        loading = true
        defer { loading = false }
        showing = .near
        take(await dependencies.publicArchive.nearby(latitude: point.latitude, longitude: point.longitude))
    }

    private func search() async {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.count >= 2 else {
            problem = "Type at least two letters of a place or a town."
            showing = .search(text)
            rows = []
            return
        }
        problem = nil
        loading = true
        defer { loading = false }
        showing = .search(text)
        take(await dependencies.publicArchive.search(text))
    }

    private func download(_ row: PublicArchiveSession) async {
        downloading = row.id
        defer { downloading = nil }
        let destination = FileManager.default.temporaryDirectory
            .appendingPathComponent("public-\(row.id.uuidString.lowercased()).ben")
        switch await dependencies.publicArchive.downloadBundle(sessionId: row.id, to: destination) {
        case .ok(let url):
            await FieldBundleOpener.open(url, dependencies: dependencies, router: router)
            try? FileManager.default.removeItem(at: url)
        case let other:
            problem = Self.sentence(for: other) ?? "That session couldn't be downloaded just now."
        }
    }

    private func take(_ result: LoadResult<[PublicArchiveSession]>) {
        if case .ok(let found) = result {
            rows = found
        } else {
            rows = []
            problem = Self.sentence(for: result) ?? "The archive couldn't be reached just now. Try again in a moment."
        }
    }

    /// Something to say for anything but success — never an empty warning.
    private static func sentence<T>(for result: LoadResult<T>) -> String? {
        switch result {
        case .ok: nil
        case .failed(let reason, _): (reason?.isEmpty == false) ? reason : nil
        case .sessionEnded: "Your sign-in ended. The archive is public, so try again."
        case .rateLimited: "Too many lookups at once. Wait a moment and try again."
        }
    }
}
