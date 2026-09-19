import SwiftUI
import BenKit

/// A case from the investigating group's side, read-only (iOS-8).
///
/// **The gap this closes.** The app had no group-side case view at all. My Cases is the *client's*
/// list — what the person who asked for help sees — so a member rostered on a visit could not open
/// the case they were about to walk into. The one route to it now is from the investigation, which
/// is also the only place the app already knows both ids it needs.
///
/// **Read-only.** A phone in a dark house is for looking something up: what the client said, which
/// photo that was, what the group last told them. Writing to a case is a desk job, and every write
/// carries a permission the app would have to model correctly before offering a button for it.
struct GroupCaseView: View {
    @Environment(AppDependencies.self) private var dependencies

    let organizationId: UUID
    let caseId: UUID

    @State private var store: GroupCaseStore?

    var body: some View {
        Group {
            switch store?.state {
            case .none, .loading:
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut:
                ContentUnavailableView {
                    Label("Sign in to open this case", systemImage: "folder.badge.person.crop")
                } description: {
                    Text("A case belongs to the group working it.")
                }

            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't open the case", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason)
                } actions: {
                    Button("Try again") { Task { await store?.load() } }
                        .buttonStyle(.borderedProminent)
                }

            case .loaded:
                loaded
            }
        }
        .navigationTitle(store?.groupCase?.reference ?? "Case")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await store?.load() }
        .task {
            let store = GroupCaseStore(organizationId: organizationId, caseId: caseId,
                                       api: dependencies.api)
            self.store = store
            await store.load()
        }
    }

    @ViewBuilder
    private var loaded: some View {
        List {
            if let record = store?.groupCase {
                Section {
                    Text(record.title).font(.headline)
                    LabeledContent("Status", value: record.status.label)
                    if let place = record.placeLabel {
                        LabeledContent("Where", value: place)
                    }
                    if let manager = record.caseManagerDisplayName, !manager.isEmpty {
                        LabeledContent("Case manager", value: manager)
                    }
                    if let opened = record.dateCaseOpened {
                        LabeledContent("Opened",
                                       value: opened.formatted(date: .abbreviated, time: .omitted))
                    }
                } footer: {
                    Text("Read-only on the phone. Adding to a case is done on the website.")
                }
                .accessibilityIdentifier("group-case-header")
            }

            timelineSection
            filesSection
            messagesSection
        }
        .listStyle(.insetGrouped)
    }

    // ── Sections ────────────────────────────────────────────────────────────
    // Each one says its own "we could not ask" separately. A refused timeline is a section that
    // says so over a case that still opens — not a screen that reports the whole case missing.

    @ViewBuilder
    private var timelineSection: some View {
        Section("Timeline") {
            if let problem = store?.timelineProblem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.warning)
            } else if store?.timeline.isEmpty ?? true {
                Text("Nothing recorded on this case yet.")
                    .font(.caption).foregroundStyle(Theme.fog)
            } else {
                ForEach(store?.timeline ?? []) { entry in
                    VStack(alignment: .leading, spacing: 3) {
                        HStack {
                            Text(entry.entryType.label)
                                .font(.caption2).foregroundStyle(Theme.haunt)
                            Spacer()
                            Text(entry.occurredAt.formatted(date: .abbreviated, time: .shortened))
                                .font(.caption2).foregroundStyle(Theme.fog)
                        }
                        if let title = entry.title, !title.isEmpty {
                            Text(title).foregroundStyle(Theme.bone)
                        }
                        if let body = entry.body, !body.isEmpty {
                            Text(PlainText.from(body))
                                .font(.callout).foregroundStyle(Theme.fog)
                                .lineLimit(6)
                        }
                        if let author = entry.authorDisplayName, !author.isEmpty {
                            Text(author).font(.caption2).foregroundStyle(Theme.fog)
                        }
                        if let files = entry.files, !files.isEmpty {
                            Label("\(files.count) attached",
                                  systemImage: files.contains(where: \.isImage) ? "photo" : "paperclip")
                                .font(.caption2).foregroundStyle(Theme.fog)
                        }
                    }
                    .accessibilityIdentifier("group-case-timeline-entry")
                }
            }
        }
    }

    @ViewBuilder
    private var filesSection: some View {
        Section("Files") {
            if let problem = store?.filesProblem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.warning)
            } else if store?.files.isEmpty ?? true {
                Text("No files on this case yet.")
                    .font(.caption).foregroundStyle(Theme.fog)
            } else {
                ForEach(store?.files ?? []) { file in
                    HStack(spacing: 10) {
                        Image(systemName: file.isImage ? "photo" : "doc")
                            .foregroundStyle(Theme.fog)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(file.fileName ?? "Untitled file").foregroundStyle(Theme.bone)
                            if let size = file.fileSize {
                                Text(ByteCountFormatter.string(fromByteCount: size,
                                                               countStyle: .file))
                                    .font(.caption2).foregroundStyle(Theme.fog)
                            }
                        }
                    }
                    .accessibilityIdentifier("group-case-file")
                }
            }
        }
    }

    @ViewBuilder
    private var messagesSection: some View {
        Section("Messages with the client") {
            if let problem = store?.messagesProblem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.warning)
            } else if store?.messages.isEmpty ?? true {
                Text("Nothing has been said on this case yet.")
                    .font(.caption).foregroundStyle(Theme.fog)
            } else {
                ForEach(store?.messages ?? []) { message in
                    VStack(alignment: .leading, spacing: 3) {
                        HStack {
                            Text(message.isFromTheGroup
                                 ? (message.authorDisplayName ?? "Your group")
                                 : (message.authorDisplayName ?? "The client"))
                                .font(.caption2)
                                .foregroundStyle(message.isFromTheGroup ? Theme.haunt : Theme.ecto)
                            Spacer()
                            Text(message.dateCreated.formatted(date: .abbreviated,
                                                               time: .shortened))
                                .font(.caption2).foregroundStyle(Theme.fog)
                        }
                        Text(PlainText.from(message.body ?? ""))
                            .font(.callout).foregroundStyle(Theme.bone)
                    }
                    .accessibilityIdentifier("group-case-message")
                }
            }
        }
    }
}
