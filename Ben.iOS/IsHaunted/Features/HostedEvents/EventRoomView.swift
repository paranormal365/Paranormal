import SwiftUI
import PhotosUI
import QuickLook
import BenKit

/// An event's room on the phone: what the people at the event post while they are there (item 235 phase 14b).
///
/// **Private to the people at the event**, exactly as on the website — the server decides who is in it, and
/// somebody who is not never gets this far.
///
/// **A photo stays the poster's.** Posting one to the room does not hand it over; *Also send to the organizers*
/// shares it with them, and taking a post down leaves the photo in the poster's own files. The first photo a guest
/// adds at an event waits for them to agree, in the organizers' words, to it being shown.
///
/// Moderation — hiding posts, closing the room — stays on the website, where a moderator sees the whole room.
struct EventRoomView: View {
    let hostedEventId: UUID
    /// Opened from an "add photos" link (the photo wall's code): go straight to the composer.
    var startWithComposer = false

    @Environment(AppDependencies.self) private var dependencies

    @State private var store: HostedEventsStore?
    @State private var room: EventRoom?
    @State private var messages: [EventRoomMessage] = []
    @State private var loaded = false
    @State private var failure: String?
    @State private var note: String?
    @State private var composing = false
    @State private var olderMayExist = false
    @State private var loadingOlder = false
    @State private var takingDown: EventRoomMessage?
    @State private var reporting: EventRoomMessage?
    @State private var reportReason = ""
    @State private var preview: URL?
    @State private var busy = false

    private static let page = 50

    var body: some View {
        Group {
            if let room {
                list(room)
            } else if !loaded {
                ProgressView("Opening the room…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let failure {
                ContentUnavailableView {
                    Label("Couldn't open the room", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure)
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            } else {
                ContentUnavailableView("The room is for the people at the event", systemImage: "bubble.left.and.bubble.right",
                                       description: Text("If you've asked for a place, you can join in once the venue confirms it."))
            }
        }
        .navigationTitle("The room")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let room, room.canPost {
                ToolbarItem(placement: .primaryAction) {
                    Button { composing = true } label: {
                        Label(room.canAddPhotos ? "Post or add photos" : "Post", systemImage: "square.and.pencil")
                    }
                    .accessibilityIdentifier("room-compose")
                }
            }
        }
        .refreshable { await load() }
        .task(id: dependencies.session.me?.userId) {
            if store == nil { store = HostedEventsStore(api: dependencies.api) }
            await load()
            if startWithComposer, room?.canPost == true, !composing { composing = true }
        }
        .sheet(isPresented: $composing) {
            if let room, let store {
                RoomComposerView(hostedEventId: hostedEventId, room: room, store: store) { updated in
                    apply(updated)
                }
            }
        }
        .confirmationDialog("Take this post down?", isPresented: Binding(get: { takingDown != nil }, set: { if !$0 { takingDown = nil } }),
                            titleVisibility: .visible, presenting: takingDown) { message in
            Button("Take it down", role: .destructive) { Task { await write { await $0.takeDown(hostedEventId, message: message.id) } } }
        } message: { message in
            Text(message.hasMedia ? "It leaves the room. The photo stays in your own files." : "It leaves the room.")
        }
        .alert("Report this post?", isPresented: Binding(get: { reporting != nil }, set: { if !$0 { reporting = nil } }),
               presenting: reporting) { message in
            TextField("What's wrong with it (optional)", text: $reportReason)
            Button("Report", role: .destructive) {
                let reason = reportReason.trimmingCharacters(in: .whitespacesAndNewlines)
                reportReason = ""
                Task { await write { await $0.report(hostedEventId, message: message.id, reason: reason.isEmpty ? nil : reason) } }
            }
            Button("Cancel", role: .cancel) { reportReason = "" }
        } message: { _ in
            Text("The organizers see reports, and so does the site.")
        }
        .quickLookPreview($preview)
    }

    @ViewBuilder
    private func list(_ room: EventRoom) -> some View {
        List {
            if let note {
                Section { Label(note, systemImage: "checkmark.circle").font(.footnote).foregroundStyle(Theme.success) }
            }
            if !room.canPost, let why = room.whyNotPost {
                Section { Text(why).font(.footnote).foregroundStyle(Theme.fog) }
            }
            if messages.isEmpty {
                Section {
                    Text(room.canPost ? "Nobody has posted yet. Say hello, or add the first photo." : "Nothing was posted here.")
                        .foregroundStyle(Theme.fog)
                }
            } else {
                Section {
                    ForEach(messages) { message in
                        messageRow(message, room)
                    }
                    if olderMayExist {
                        Button {
                            Task { await loadOlder() }
                        } label: {
                            if loadingOlder { ProgressView() } else { Text("Show older posts") }
                        }
                        .disabled(loadingOlder)
                    }
                }
            }
            if room.canModerate {
                Section {
                    Text("Hiding posts and closing the room are done on the website, where you can see the whole room.")
                        .font(.footnote).foregroundStyle(Theme.fog)
                }
            }
        }
        .listStyle(.insetGrouped)
    }

    @ViewBuilder
    private func messageRow(_ message: EventRoomMessage, _ room: EventRoom) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(message.isMine ? "You" : message.authorName).font(.subheadline.weight(.semibold))
                Spacer()
                Text(message.postedUtc.formatted(.relative(presentation: .named))).font(.caption).foregroundStyle(Theme.fog)
            }
            if !message.body.isEmpty { Text(message.body) }
            if message.hasMedia {
                if message.isVideo {
                    Button { Task { await openMedia(message) } } label: {
                        Label("Play the video", systemImage: "play.rectangle")
                    }
                    .buttonStyle(.bordered).disabled(busy)
                } else {
                    RoomPhoto(eventId: hostedEventId, messageId: message.id, loader: dependencies.imageLoader)
                        .frame(maxWidth: .infinity).frame(height: 220).clipped()
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                        .contentShape(Rectangle())
                        .onTapGesture { Task { await openMedia(message) } }
                }
            }
            HStack(spacing: 6) {
                if message.mediaWaiting { StatusBadge(text: "Waiting for a check", colour: Theme.warning) }
                if message.isHidden { StatusBadge(text: "Hidden by the organizers", colour: Theme.fog) }
                if message.sentToHosts && message.isMine { StatusBadge(text: "Sent to the organizers", colour: Theme.ecto) }
            }
        }
        .padding(.vertical, 4)
        .contextMenu {
            if message.isMine {
                if message.hasMedia && !message.sentToHosts && !room.hostNames.isEmpty {
                    Button {
                        Task { await write { await $0.sendToHosts(hostedEventId, message: message.id) } }
                    } label: {
                        Label("Send to \(room.hostNames.joined(separator: " and "))", systemImage: "paperplane")
                    }
                }
                Button(role: .destructive) { takingDown = message } label: { Label("Take down", systemImage: "trash") }
            } else {
                Button(role: .destructive) { reporting = message } label: { Label("Report", systemImage: "flag") }
            }
        }
        .accessibilityIdentifier("room-message-\(message.id.uuidString.lowercased())")
    }

    // ── reads and writes ─────────────────────────────────────────────────────

    private func load() async {
        guard let store else { return }
        switch await store.loadRoom(hostedEventId) {
        case .ok(let value):
            room = value
            messages = value?.messages ?? []
            olderMayExist = messages.count >= Self.page
            failure = nil
        case .failed(let reason, _):
            failure = reason ?? "Check your connection and try again."
        case .sessionEnded:
            failure = "Sign in again to open the room."
        case .rateLimited:
            failure = "Too many requests — try again shortly."
        }
        loaded = true
    }

    private func loadOlder() async {
        guard let store, let oldest = messages.last?.postedUtc else { return }
        loadingOlder = true
        defer { loadingOlder = false }
        if case .ok(let value?) = await store.loadRoom(hostedEventId, before: oldest) {
            let known = Set(messages.map(\.id))
            messages.append(contentsOf: value.messages.filter { !known.contains($0.id) })
            olderMayExist = value.messages.count >= Self.page
        }
    }

    private func write(_ action: (HostedEventsStore) async -> LoadResult<EventRoom>) async {
        guard let store else { return }
        busy = true
        defer { busy = false }
        switch await action(store) {
        case .ok(let updated):
            apply(updated)
        case .failed(let reason, _):
            note = nil
            failure = nil
            showRefusal(reason ?? "That couldn't be done just now.")
        case .sessionEnded:
            showRefusal("Sign in again first.")
        case .rateLimited:
            showRefusal("Too many requests — try again shortly.")
        }
    }

    private func apply(_ updated: EventRoom) {
        room = updated
        messages = updated.messages
        olderMayExist = messages.count >= Self.page
        note = updated.note
    }

    private func showRefusal(_ sentence: String) {
        guard var current = room else { return }
        current.whyNotPost = sentence
        room = current
    }

    private func openMedia(_ message: EventRoomMessage) async {
        busy = true
        defer { busy = false }
        let ext = message.isVideo ? "mov" : "jpg"
        let destination = FileManager.default.temporaryDirectory.appendingPathComponent("room-\(message.id.uuidString).\(ext)")
        if case .ok(let url) = await dependencies.api.download(HostedEventsStore.roomMediaEndpoint(hostedEventId, message: message.id),
                                                               to: destination) {
            preview = url
        }
    }
}

/// A room photo, fetched with the signed-in person's token.
private struct RoomPhoto: View {
    let eventId: UUID
    let messageId: UUID
    let loader: AuthenticatedImageLoader

    @State private var image: UIImage?
    @State private var failed = false

    var body: some View {
        ZStack {
            Theme.mist
            if let image {
                Image(uiImage: image).resizable().scaledToFill()
            } else if failed {
                Image(systemName: "photo.badge.exclamationmark").foregroundStyle(Theme.fog)
            } else {
                ProgressView()
            }
        }
        .task {
            guard let data = await loader.data(for: messageId, from: HostedEventsStore.roomMediaEndpoint(eventId, message: messageId)),
                  let decoded = UIImage(data: data) else {
                failed = true
                return
            }
            image = decoded
        }
    }
}

/// Writing in the room and adding photos: the words, the photos, the once-per-event agreement and the share.
struct RoomComposerView: View {
    let hostedEventId: UUID
    let room: EventRoom
    let store: HostedEventsStore
    var onPosted: (EventRoom) -> Void

    @Environment(\.dismiss) private var dismiss

    @State private var text = ""
    @State private var media: [MediaUpload] = []
    @State private var pickerItems: [PhotosPickerItem] = []
    @State private var showCamera = false
    @State private var agree = false
    @State private var sendToHosts = false
    @State private var posting = false
    @State private var progress: String?
    @State private var errorMessage: String?

    private var trimmed: String { text.trimmingCharacters(in: .whitespacesAndNewlines) }

    /// "2 photos", "1 video", "2 photos and 1 video".
    private var stagedTitle: String {
        let videos = media.filter(\.isVideo).count
        let photos = media.count - videos
        let words = [photos > 0 ? "\(photos) \(photos == 1 ? "photo" : "photos")" : nil,
                     videos > 0 ? "\(videos) \(videos == 1 ? "video" : "videos")" : nil].compactMap { $0 }
        return words.joined(separator: " and ")
    }
    private var needsAgreement: Bool { !media.isEmpty && room.needsPhotoConsent }
    private var canPost: Bool {
        !posting && (!trimmed.isEmpty || !media.isEmpty) && (!needsAgreement || agree)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField(media.isEmpty ? "Say something to the room" : "A caption (optional)", text: $text, axis: .vertical)
                        .lineLimit(2...6)
                        .accessibilityIdentifier("room-composer-text")
                }

                if room.canAddPhotos {
                    if !media.isEmpty {
                        Section(stagedTitle) {
                            ForEach(Array(media.enumerated()), id: \.offset) { index, upload in
                                HStack {
                                    Image(systemName: upload.isVideo ? "video" : "photo").foregroundStyle(Theme.ecto)
                                    Text(upload.displaySize).font(.callout)
                                    Spacer()
                                    Button("Remove", role: .destructive) { remove(at: index) }
                                        .buttonStyle(.borderless).disabled(posting)
                                }
                            }
                        }
                    }

                    Section {
                        PhotosPicker(selection: $pickerItems, matching: .any(of: [.images, .videos])) {
                            Label("Choose photos or videos", systemImage: "photo.on.rectangle")
                        }
                        .disabled(posting)
                        if UIImagePickerController.isSourceTypeAvailable(.camera) {
                            Button { showCamera = true } label: { Label("Take a photo or video", systemImage: "camera") }
                                .disabled(posting)
                        }
                    } footer: {
                        Text("They go into the event's room, and onto the photo wall the organizers may show at the venue. They stay yours.")
                    }

                    if needsAgreement, let notice = room.photoNotice {
                        Section {
                            Text(notice).font(.footnote)
                            Toggle("I agree", isOn: $agree).accessibilityIdentifier("room-composer-agree")
                        }
                    }

                    if !media.isEmpty && !room.hostNames.isEmpty {
                        Section {
                            Toggle("Also send to \(room.hostNames.joined(separator: " and "))", isOn: $sendToHosts)
                        } footer: {
                            Text("They get a copy to keep. It's still yours.")
                        }
                    }
                } else if let why = room.whyNotPost ?? (room.photoPosting == .teamOnly ? "The organizers have kept photos to the event's own team. You can still write in the room." : nil) {
                    Section { Text(why).font(.footnote).foregroundStyle(Theme.fog) }
                }

                if let progress {
                    Section { Label(progress, systemImage: "arrow.up.circle").font(.footnote) }
                }
                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.danger).font(.callout)
                    }
                }
            }
            .navigationTitle(room.canAddPhotos ? "Post or add photos" : "Post")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { discardAndDismiss() }.disabled(posting)
                }
                ToolbarItem(placement: .confirmationAction) {
                    if posting {
                        ProgressView()
                    } else {
                        Button("Post") { Task { await post() } }.disabled(!canPost)
                            .accessibilityIdentifier("room-composer-post")
                    }
                }
            }
            .onChange(of: pickerItems) { _, items in Task { await stage(items) } }
            .fullScreenCover(isPresented: $showCamera) {
                CameraPicker { media.append($0) }
            }
        }
        .interactiveDismissDisabled(posting)
    }

    private func stage(_ items: [PhotosPickerItem]) async {
        guard !items.isEmpty else { return }
        for item in items {
            guard let data = try? await item.loadTransferable(type: Data.self) else {
                errorMessage = "One of those couldn't be read."
                continue
            }
            let isVideo = item.supportedContentTypes.contains { $0.conforms(to: .movie) }
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("room-\(UUID().uuidString).\(isVideo ? "mov" : "jpg")")
            guard (try? data.write(to: url)) != nil else { continue }
            media.append(MediaUpload(fileURL: url, filename: url.lastPathComponent,
                                     contentType: isVideo ? "video/quicktime" : "image/jpeg", byteCount: Int64(data.count)))
        }
        pickerItems = []
    }

    private func remove(at index: Int) {
        guard media.indices.contains(index) else { return }
        try? FileManager.default.removeItem(at: media[index].fileURL)
        media.remove(at: index)
    }

    private func discardAndDismiss() {
        for upload in media { try? FileManager.default.removeItem(at: upload.fileURL) }
        dismiss()
    }

    /// One post per photo, the words on the first — the room takes one photo to a post. A refusal part-way stops
    /// there and keeps what hasn't gone, so nothing is lost and nothing goes twice.
    private func post() async {
        guard canPost else { return }
        posting = true
        errorMessage = nil
        defer { posting = false; progress = nil }

        if media.isEmpty {
            switch await store.post(hostedEventId, body: trimmed, media: nil, sendToHosts: false, agreeToShow: false) {
            case .ok(let updated):
                onPosted(updated)
                dismiss()
            case let other:
                errorMessage = sentence(other)
            }
            return
        }

        var latest: EventRoom?
        let total = media.count
        var caption = trimmed
        while let upload = media.first {
            progress = total == 1 ? "Sending…" : "Sending \(total - media.count + 1) of \(total)…"
            switch await store.post(hostedEventId, body: caption, media: upload, sendToHosts: sendToHosts, agreeToShow: agree) {
            case .ok(let updated):
                latest = updated
                try? FileManager.default.removeItem(at: upload.fileURL)
                media.removeFirst()
                caption = ""
                text = ""
            case let other:
                errorMessage = sentence(other)
                if let latest { onPosted(latest) }
                return
            }
        }
        if let latest { onPosted(latest) }
        dismiss()
    }

    private func sentence(_ result: LoadResult<EventRoom>) -> String {
        switch result {
        case .ok: ""
        case .failed(let reason, _): reason ?? "That couldn't be sent just now. Nothing was lost — try again."
        case .sessionEnded: "Sign in again to post."
        case .rateLimited: "Too many at once — wait a moment and try again."
        }
    }
}
