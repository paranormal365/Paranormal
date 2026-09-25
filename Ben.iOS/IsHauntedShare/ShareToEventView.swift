import SwiftUI
import UniformTypeIdentifiers
import BenKit

/// What was shared, which event it's for, and the same choices the room offers (item 235 phase 14d).
@MainActor
@Observable
final class ShareModel {
    struct Staged: Identifiable {
        let id = UUID()
        let url: URL
        let contentType: String
        let originalName: String
        var isVideo: Bool { contentType.hasPrefix("video/") }
    }

    private let context: NSExtensionContext?

    let events: [ShareableEvents.Event]
    var eventId: UUID?
    var caption = ""
    var agree = false
    var sendToHosts = false
    var staged: [Staged] = []
    var loading = true
    var problems: [String] = []
    var saved: String?

    init(context: NSExtensionContext?) {
        self.context = context
        events = ShareableEvents.shared().offerable()
        eventId = events.first?.hostedEventId
    }

    var event: ShareableEvents.Event? { events.first { $0.hostedEventId == eventId } }

    /// Unknown counts as needed: the room hasn't been opened in the app yet, so agreeing is asked for, and the server
    /// only records it where it is needed.
    var needsAgreement: Bool { event?.needsPhotoConsent != false }

    var canAdd: Bool { !loading && !staged.isEmpty && event != nil && (!needsAgreement || agree) && saved == nil }

    var summary: String {
        let videos = staged.filter(\.isVideo).count
        let photos = staged.count - videos
        return [photos > 0 ? "\(photos) \(photos == 1 ? "photo" : "photos")" : nil,
                videos > 0 ? "\(videos) \(videos == 1 ? "video" : "videos")" : nil]
            .compactMap { $0 }.joined(separator: " and ")
    }

    /// Copies every shared photo and video into the extension's own scratch folder. The file a provider hands over
    /// is only valid inside its callback, so it is copied there and then — never read into memory.
    func loadAttachments() async {
        defer { loading = false }
        let providers = (context?.inputItems as? [NSExtensionItem] ?? []).flatMap { $0.attachments ?? [] }
        for provider in providers {
            let type: UTType
            if provider.hasItemConformingToTypeIdentifier(UTType.movie.identifier) { type = .movie }
            else if provider.hasItemConformingToTypeIdentifier(UTType.image.identifier) { type = .image }
            else { continue }

            if let copy = await Self.copy(from: provider, as: type) {
                staged.append(copy)
            } else {
                problems.append("One of those couldn't be read.")
            }
        }
        if staged.isEmpty && problems.isEmpty { problems.append("There's nothing here IsHaunted can add — share photos or videos.") }
    }

    private static func copy(from provider: NSItemProvider, as type: UTType) async -> Staged? {
        await withCheckedContinuation { (continuation: CheckedContinuation<Staged?, Never>) in
            _ = provider.loadFileRepresentation(forTypeIdentifier: type.identifier) { url, _ in
                guard let url else { return continuation.resume(returning: nil) }
                let destination = FileManager.default.temporaryDirectory
                    .appendingPathComponent("share-\(UUID().uuidString).\(url.pathExtension.isEmpty ? "bin" : url.pathExtension)")
                do {
                    try FileManager.default.copyItem(at: url, to: destination)
                } catch {
                    return continuation.resume(returning: nil)
                }
                let mime = UTType(filenameExtension: url.pathExtension)?.preferredMIMEType
                    ?? (type == .movie ? "video/quicktime" : "image/jpeg")
                continuation.resume(returning: Staged(url: destination, contentType: mime, originalName: url.lastPathComponent))
            }
        }
    }

    /// Keeps each item in the shared outbox, the words on the first. The app sends them.
    func add() {
        guard let event, canAdd else { return }
        let outbox = RoomOutbox.shared()
        var words = caption.trimmingCharacters(in: .whitespacesAndNewlines)
        var kept = 0
        problems = []
        for item in staged {
            do {
                try outbox.add(hostedEventId: event.hostedEventId, eventName: event.eventName, body: words, file: item.url,
                               contentType: item.contentType, originalName: item.originalName, sendToHosts: sendToHosts,
                               agreeToShow: agree, moveFile: true)
                words = ""
                kept += 1
            } catch let error as RoomOutbox.AddError {
                problems.append(error.sentence)
            } catch {
                problems.append("One of those couldn't be kept.")
            }
        }
        staged = []
        guard kept > 0 else { return }
        saved = "\(kept == 1 ? "It's" : "They're") kept for \(event.eventName). IsHaunted sends \(kept == 1 ? "it" : "them") the next time it's open with a signal — open it now to send straight away."
    }

    func finish() { context?.completeRequest(returningItems: nil) }

    func cancel() {
        for item in staged { try? FileManager.default.removeItem(at: item.url) }
        context?.cancelRequest(withError: NSError(domain: NSCocoaErrorDomain, code: NSUserCancelledError))
    }
}

struct ShareToEventView: View {
    @Bindable var model: ShareModel

    /// Named for the hosts when the room has told us who they are, and plainly when it has not.
    private var hostsTitle: String {
        if let names = model.event?.hostNames, !names.isEmpty {
            return "Also send to \(names.joined(separator: " and "))"
        }
        return "Also send to the organizers"
    }

    var body: some View {
        NavigationStack {
            Form {
                if let saved = model.saved {
                    Section {
                        Label(saved, systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                    }
                } else if model.events.isEmpty {
                    Section {
                        Text("There are no events to add photos to yet. Open IsHaunted first — the events you're going to, and the ones you're helping at, appear here once the app has looked them up.")
                    }
                } else {
                    Section {
                        if model.loading {
                            ProgressView("Getting them ready…")
                        } else if !model.staged.isEmpty {
                            Label(model.summary, systemImage: model.staged.contains(where: \.isVideo) ? "photo.on.rectangle.angled" : "photo.stack")
                        }
                    }

                    Section("Event") {
                        Picker("Event", selection: $model.eventId) {
                            ForEach(model.events) { event in
                                Text(event.eventName).tag(Optional(event.hostedEventId))
                            }
                        }
                        .pickerStyle(.inline)
                        .labelsHidden()
                    }

                    Section {
                        TextField("A caption (optional)", text: $model.caption, axis: .vertical).lineLimit(1...4)
                    }

                    if model.needsAgreement {
                        Section {
                            Text(model.event?.photoNotice
                                 ?? "Photos you add are shown to the people at this event in its room, and the organizers may show them on a photo wall or slideshow at the venue. Your photos stay yours. Please only add photos of people who are happy to be shown.")
                                .font(.footnote)
                            Toggle("I agree", isOn: $model.agree)
                        }
                    }

                    Section {
                        Toggle(hostsTitle, isOn: $model.sendToHosts)
                    } footer: {
                        Text("They get a copy to keep. It's still yours.")
                    }
                }

                if !model.problems.isEmpty {
                    Section {
                        ForEach(model.problems, id: \.self) { Label($0, systemImage: "exclamationmark.triangle").foregroundStyle(.red) }
                    }
                }
            }
            .navigationTitle("Add to an event")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if model.saved != nil {
                    ToolbarItem(placement: .confirmationAction) { Button("Done") { model.finish() } }
                } else {
                    ToolbarItem(placement: .cancellationAction) { Button("Cancel") { model.cancel() } }
                    if !model.events.isEmpty {
                        ToolbarItem(placement: .confirmationAction) {
                            Button("Add") { model.add() }.disabled(!model.canAdd)
                        }
                    }
                }
            }
        }
    }
}
