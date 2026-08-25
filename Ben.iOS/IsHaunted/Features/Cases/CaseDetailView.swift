import SwiftUI
import BenKit

/// One case, from the client's side: what it is, who is handling it, and the timeline of
/// what has happened — newest first, ordered by when things HAPPENED rather than when they
/// were typed.
struct CaseDetailView: View {
    @Environment(AppDependencies.self) private var dependencies

    let caseId: UUID

    @State private var store: CaseDetailStore?
    @State private var logging = false
    @State private var toast: String?

    var body: some View {
        Group {
            switch store?.state {
            case .none, .loading:
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)

            case .signedOut:
                ContentUnavailableView {
                    Label("Sign in to see this case", systemImage: "folder.badge.person.crop")
                }

            case .failed(let reason):
                ContentUnavailableView {
                    Label("Couldn't open this case", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(Theme.warning)
                } description: {
                    Text(reason ?? "The server couldn't be reached.")
                } actions: {
                    Button("Try again") { Task { await store?.load() } }
                }

            case .loaded:
                if let detail = store?.detail {
                    content(detail)
                } else {
                    ProgressView()
                }
            }
        }
        .navigationTitle(store?.detail?.caseReference ?? "Case")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            // Only on a case still open: logging onto a closed case would go unread.
            if let detail = store?.detail, detail.status != .closed, detail.status != .declined {
                ToolbarItem(placement: .primaryAction) {
                    Button { logging = true } label: {
                        Image(systemName: "square.and.pencil")
                    }
                    .accessibilityLabel("Log what happened")
                }
            }
        }
        .sheet(isPresented: $logging) {
            if let store {
                LogOccurrenceView(store: store) { _ in
                    // The saved entry lands mid-timeline by its own date, so re-read rather
                    // than guessing where to slot it.
                    Task {
                        await store.load()
                        toast = store.failedAttachments == 0
                            ? "Logged."
                            : "Logged, but \(store.failedAttachments) attachment\(store.failedAttachments == 1 ? "" : "s") didn't upload."
                    }
                }
                .environment(dependencies)
            }
        }
        .overlay(alignment: .bottom) {
            if let toast {
                Text(toast)
                    .font(.callout)
                    .padding(.horizontal, 16).padding(.vertical, 10)
                    .background(Theme.mist, in: Capsule())
                    .foregroundStyle(Theme.bone)
                    .shadow(radius: 6)
                    .padding(.bottom, 16)
                    .task {
                        try? await Task.sleep(for: .seconds(3))
                        self.toast = nil
                    }
            }
        }
        .animation(.default, value: toast)
        .refreshable { await store?.load() }
        .task {
            let store = CaseDetailStore(caseId: caseId, api: dependencies.api)
            self.store = store
            await store.load()
        }
    }

    private func content(_ detail: MyCaseDetail) -> some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        StatusChip(status: detail.status)
                        Spacer()
                        if !detail.isPrimaryClient {
                            // A co-client can read and log, but the group answers to the
                            // person who asked. Saying so avoids a wrong expectation.
                            Text("Shared with you")
                                .font(.caption).foregroundStyle(Theme.fog)
                        }
                    }
                    Text(detail.title)
                        .font(.title3.weight(.semibold))
                        .foregroundStyle(Theme.bone)
                    if let description = detail.description, !description.isEmpty {
                        Text(description).font(.callout).foregroundStyle(Theme.fog)
                    }
                }
                .padding(.vertical, 4)

                LabeledContent("Opened", value: detail.dateCaseOpened
                    .formatted(date: .abbreviated, time: .omitted))
                if let closed = detail.dateCaseClosed {
                    LabeledContent("Closed", value: closed.formatted(date: .abbreviated, time: .omitted))
                }
                if let manager = detail.caseManagerDisplayName {
                    LabeledContent("Case manager", value: manager)
                }
            }

            if !detail.contacts.isEmpty {
                Section("Who to contact") {
                    ForEach(detail.contacts, id: \.identity) { contact in
                        VStack(alignment: .leading, spacing: 2) {
                            Text(contact.displayName ?? "Someone at the group")
                                .font(.subheadline.weight(.medium))
                            if let role = contact.roleName {
                                Text(role).font(.caption).foregroundStyle(Theme.fog)
                            }
                            // Tappable where the phone can genuinely act on it.
                            if let email = contact.email, !email.isEmpty,
                               let url = URL(string: "mailto:\(email)") {
                                Link(email, destination: url).font(.caption)
                            }
                            if let phone = contact.phone, !phone.isEmpty,
                               let url = URL(string: "tel:\(phone.filter { $0.isNumber || $0 == "+" })") {
                                Link(phone, destination: url).font(.caption)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                }
            }

            Section("What's happened") {
                if detail.timeline.isEmpty {
                    Text("Nothing logged yet.")
                        .font(.callout).foregroundStyle(Theme.fog)
                } else {
                    ForEach(detail.timeline) { entry in
                        OccurrenceRow(entry: entry, loader: dependencies.imageLoader)
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
    }
}

/// One entry on the timeline. Which side wrote it is the first thing a reader needs.
struct OccurrenceRow: View {
    let entry: MyCaseOccurrence
    let loader: AuthenticatedImageLoader

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Image(systemName: entry.fromInvestigators ? "person.2" : "person")
                    .font(.caption)
                Text(entry.fromInvestigators ? "From the group" : "You logged this")
                    .font(.caption.weight(.medium))
                Spacer()
                Text((entry.eventDateTime ?? entry.dateCreated)
                    .formatted(date: .abbreviated, time: .shortened))
                    .font(.caption)
            }
            .foregroundStyle(entry.fromInvestigators ? Theme.ecto : Theme.fog)

            if let title = entry.title, !title.isEmpty {
                Text(title).font(.subheadline.weight(.semibold)).foregroundStyle(Theme.bone)
            }
            if let body = entry.body, !body.isEmpty {
                Text(body).font(.callout).foregroundStyle(Theme.bone)
            }

            if !entry.files.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(entry.files) { file in
                            if file.isImage {
                                AuthenticatedImage(fileId: file.id, loader: loader)
                                    .frame(width: 90, height: 90)
                                    .clipShape(RoundedRectangle(cornerRadius: 8))
                            } else {
                                // Not an image: name it rather than showing a broken frame.
                                Label(file.fileName ?? "Attachment", systemImage: "paperclip")
                                    .font(.caption)
                                    .padding(8)
                                    .background(Theme.mist, in: RoundedRectangle(cornerRadius: 8))
                            }
                        }
                    }
                }
            }
        }
        .padding(.vertical, 4)
    }
}

/// An image behind a bearer token. `AsyncImage` issues its own unauthenticated request and
/// would render a 401 as a broken frame, so the bytes come through `APIClient` instead.
struct AuthenticatedImage: View {
    let fileId: UUID
    let loader: AuthenticatedImageLoader

    @State private var image: UIImage?
    @State private var failed = false

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else if failed {
                // Says what happened instead of pretending to be a picture that is loading.
                Image(systemName: "photo.badge.exclamationmark")
                    .foregroundStyle(Theme.fog)
            } else {
                ProgressView()
            }
        }
        .task {
            guard let data = await loader.data(for: fileId), let decoded = UIImage(data: data) else {
                failed = true
                return
            }
            image = decoded
        }
    }
}
