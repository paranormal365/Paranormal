import SwiftUI
import QuickLook
import BenKit

/// The files the organizers share with the people coming, by folder (item 235 phase 14b).
///
/// Tapping one fetches it and opens it in Quick Look, which previews documents, photos and video and offers the
/// share sheet — so a guest pack can go to Files or to the friend who is driving.
struct DownloadsView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var files: [HostedEventFile] = []
    @State private var loaded = false
    @State private var failure: String?
    @State private var message: String?
    @State private var fetching: UUID?
    @State private var preview: URL?

    var body: some View {
        Group {
            if !files.isEmpty {
                List {
                    if let message {
                        Section { Text(message).font(.footnote).foregroundStyle(Theme.warning) }
                    }
                    ForEach(folders, id: \.name) { folder in
                        Section(folder.name) {
                            ForEach(folder.files) { file in fileRow(file) }
                        }
                    }
                }
                .listStyle(.insetGrouped)
            } else if !loaded {
                ProgressView("Loading the downloads…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let failure {
                ContentUnavailableView {
                    Label("Couldn't load the downloads", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure)
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            } else {
                ContentUnavailableView("Nothing to download", systemImage: "arrow.down.doc",
                                       description: Text("The organizers haven't shared any files with guests."))
            }
        }
        .navigationTitle("Downloads")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
        .quickLookPreview($preview)
    }

    private func fileRow(_ file: HostedEventFile) -> some View {
        Button {
            Task { await open(file) }
        } label: {
            HStack(spacing: 12) {
                Image(systemName: icon(for: file.contentType)).foregroundStyle(Theme.ecto).frame(width: 24)
                VStack(alignment: .leading, spacing: 2) {
                    Text(file.fileName).foregroundStyle(.primary)
                    if let description = file.description, !description.isEmpty {
                        Text(description).font(.caption).foregroundStyle(Theme.fog)
                    }
                    Text(file.displaySize).font(.caption2).foregroundStyle(Theme.fog)
                }
                Spacer()
                if fetching == file.id { ProgressView() } else { Image(systemName: "arrow.down.circle").foregroundStyle(Theme.fog) }
            }
        }
        .buttonStyle(.plain)
        .disabled(fetching != nil)
        .accessibilityIdentifier("download-\(file.id.uuidString.lowercased())")
    }

    private struct Folder { let name: String; let files: [HostedEventFile] }

    private var folders: [Folder] {
        let grouped = Dictionary(grouping: files) { ($0.folder?.isEmpty == false ? $0.folder! : "Files") }
        return grouped.keys.sorted().map { name in
            Folder(name: name, files: grouped[name]!.sorted { ($0.sortOrder, $0.fileName) < ($1.sortOrder, $1.fileName) })
        }
    }

    private func icon(for contentType: String) -> String {
        if contentType.hasPrefix("image/") { return "photo" }
        if contentType.hasPrefix("video/") { return "film" }
        if contentType.hasPrefix("audio/") { return "waveform" }
        if contentType == "application/pdf" { return "doc.richtext" }
        return "doc"
    }

    private func load() async {
        switch await HostedEventsStore(api: dependencies.api).loadFiles(hostedEventId) {
        case .ok(let value):
            files = value
            failure = nil
        case .failed(let reason, _):
            failure = reason ?? "Check your connection and try again."
        case .sessionEnded:
            failure = "Sign in again to see the downloads."
        case .rateLimited:
            failure = "Too many requests — try again shortly."
        }
        loaded = true
    }

    private func open(_ file: HostedEventFile) async {
        fetching = file.id
        defer { fetching = nil }
        switch await HostedEventsStore(api: dependencies.api).download(hostedEventId, file: file) {
        case .ok(let url):
            message = nil
            preview = url
        case .failed(let reason, _):
            message = reason ?? "\(file.fileName) couldn't be fetched just now."
        case .sessionEnded:
            message = "Sign in again to download it."
        case .rateLimited:
            message = "Too many requests — try again shortly."
        }
    }
}
