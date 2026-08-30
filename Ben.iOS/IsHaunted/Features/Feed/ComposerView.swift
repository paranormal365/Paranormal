import SwiftUI
import PhotosUI
import AVFoundation
import BenKit

/// Writing a post (iOS Slice 4). Plain text by design — short-form is the point, and a
/// long piece belongs in a publication. Media is optional; a category is offered only
/// once media is attached, because "what does this show?" is a question about footage.
struct ComposerView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    /// Non-nil composes a reply.
    var parentPostId: UUID?
    /// Handed the created post so the caller can put it straight on screen.
    var onPosted: (FeedPostRecord) -> Void

    @State private var text = ""
    @State private var media: MediaUpload?
    @State private var experienceTypeId: UUID?
    @State private var taxonomy: [ExperienceCategoryWithTypes] = []
    @State private var pickerItem: PhotosPickerItem?
    @State private var showCamera = false
    @State private var isPosting = false
    @State private var errorMessage: String?
    @FocusState private var bodyFocused: Bool

    /// The server's own cap, so the box refuses what the API would.
    private let maxLength = 1000

    private var remaining: Int { maxLength - text.count }

    /// A picture on its own is a post: the server needs a body, so a media-only post is
    /// captioned with the file's name rather than refusing somebody who said everything
    /// they wanted to with the photograph.
    private var canPost: Bool {
        (!text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || media != nil)
            && remaining >= 0 && !isPosting
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField(parentPostId == nil ? "What did you find?" : "Write a reply",
                              text: $text, axis: .vertical)
                        .lineLimit(3...10)
                        .focused($bodyFocused)
                        .onChange(of: text) { errorMessage = nil }
                } footer: {
                    HStack {
                        Text("Use @name to mention somebody and #tag to tag a post.")
                        Spacer()
                        if remaining <= 100 {
                            Text("\(remaining)")
                                .foregroundStyle(remaining < 0 ? Theme.danger : Theme.fog)
                                .monospacedDigit()
                        }
                    }
                }

                if let media {
                    Section("Attached") {
                        HStack {
                            Image(systemName: media.isVideo ? "video" : "photo")
                                .foregroundStyle(Theme.ecto)
                            VStack(alignment: .leading) {
                                Text(media.filename).lineLimit(1)
                                Text(media.displaySize)
                                    .font(.caption).foregroundStyle(Theme.fog)
                            }
                            Spacer()
                            Button("Remove", role: .destructive) { clearMedia() }
                                .buttonStyle(.borderless)
                                .disabled(isPosting)
                        }
                    }

                    if !taxonomy.isEmpty {
                        Section {
                            Picker("What does this show?", selection: $experienceTypeId) {
                                Text("Not sure").tag(UUID?.none)
                                ForEach(taxonomy) { group in
                                    // Section inside a Picker renders as a grouped list on
                                    // iOS — the taxonomy's own shape, not a flattened blur.
                                    Section(group.category.name) {
                                        ForEach(group.selectableTypes) { type in
                                            Text(type.name).tag(UUID?.some(type.id))
                                        }
                                    }
                                }
                            }
                        } footer: {
                            Text("Optional. It helps people find your footage — and if it "
                                 + "doesn't match what we measure, only you will see a note "
                                 + "offering to change it.")
                        }
                    }
                }

                Section {
                    PhotosPicker(selection: $pickerItem, matching: .any(of: [.images, .videos])) {
                        Label("Choose a photo or video", systemImage: "photo.on.rectangle")
                    }
                    .disabled(isPosting)

                    if UIImagePickerController.isSourceTypeAvailable(.camera) {
                        Button {
                            showCamera = true
                        } label: {
                            Label("Take a photo or video", systemImage: "camera")
                        }
                        .disabled(isPosting)
                    }
                }

                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(Theme.danger)
                            .font(.callout)
                    }
                }

                if media != nil {
                    Section {
                        Label("Photos and video are checked before they appear.",
                              systemImage: "checkmark.shield")
                            .font(.caption)
                            .foregroundStyle(Theme.fog)
                    }
                }
            }
            .navigationTitle(parentPostId == nil ? "New post" : "Reply")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(isPosting)
                }
                ToolbarItem(placement: .confirmationAction) {
                    if isPosting {
                        ProgressView()
                    } else {
                        Button("Post") { Task { await post() } }
                            .disabled(!canPost)
                    }
                }
            }
            .task {
                bodyFocused = true
                taxonomy = await dependencies.feedActions.experienceTaxonomy()
            }
            .onChange(of: pickerItem) { _, item in
                Task { await stage(item) }
            }
            .fullScreenCover(isPresented: $showCamera) {
                CameraPicker { staged in
                    media = staged
                    experienceTypeId = nil
                }
            }
        }
        .interactiveDismissDisabled(isPosting)
    }

    /// Copies the picked item to a scratch file. A URL, never bytes on the heap: a 200 MB
    /// video must not live in memory while somebody finishes typing.
    private func stage(_ item: PhotosPickerItem?) async {
        guard let item else { return }
        errorMessage = nil
        do {
            guard let data = try await item.loadTransferable(type: Data.self) else {
                errorMessage = "That file couldn't be read."
                return
            }
            let isVideo = item.supportedContentTypes.contains { $0.conforms(to: .movie) }
            let ext = isVideo ? "mov" : "jpg"
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("feed-\(UUID().uuidString).\(ext)")
            try data.write(to: url)
            media = MediaUpload(
                fileURL: url,
                filename: url.lastPathComponent,
                contentType: isVideo ? "video/quicktime" : "image/jpeg",
                byteCount: Int64(data.count))
            experienceTypeId = nil
        } catch {
            errorMessage = "That file couldn't be read."
        }
    }

    private func clearMedia() {
        if let media { try? FileManager.default.removeItem(at: media.fileURL) }
        media = nil
        experienceTypeId = nil
        pickerItem = nil
    }

    private func post() async {
        guard canPost else { return }
        isPosting = true
        errorMessage = nil
        defer { isPosting = false }

        let caption = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let result = await dependencies.feedActions.createPost(
            body: caption.isEmpty ? (media?.filename ?? caption) : caption,
            parentPostId: parentPostId,
            experienceTypeId: experienceTypeId,
            media: media)

        switch result {
        case .success(let post):
            if let media { try? FileManager.default.removeItem(at: media.fileURL) }
            onPosted(post)
            dismiss()
        case .failure(let error):
            // The server's own sentence where it wrote one — the participation refusal is
            // something a person can act on; "couldn't post" is not.
            errorMessage = error.message
        }
    }
}

/// The camera, wrapped. `UIImagePickerController` rather than a custom capture session:
/// it is the system camera a person already knows, and this app needs a photo or a clip,
/// not a viewfinder of its own.
struct CameraPicker: UIViewControllerRepresentable {
    @Environment(\.dismiss) private var dismiss
    let onCaptured: (MediaUpload) -> Void

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let controller = UIImagePickerController()
        controller.sourceType = .camera
        controller.mediaTypes = ["public.image", "public.movie"]
        controller.delegate = context.coordinator
        return controller
    }

    func updateUIViewController(_ controller: UIImagePickerController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
        private let parent: CameraPicker

        init(_ parent: CameraPicker) {
            self.parent = parent
        }

        func imagePickerController(
            _ picker: UIImagePickerController,
            didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]
        ) {
            defer { parent.dismiss() }

            // A captured video is already a file on disk — move it, never read it into memory.
            if let videoURL = info[.mediaURL] as? URL {
                let destination = FileManager.default.temporaryDirectory
                    .appendingPathComponent("feed-\(UUID().uuidString).mov")
                try? FileManager.default.moveItem(at: videoURL, to: destination)
                let size = (try? FileManager.default.attributesOfItem(atPath: destination.path)[.size] as? Int64) ?? 0
                parent.onCaptured(MediaUpload(
                    fileURL: destination, filename: destination.lastPathComponent,
                    contentType: "video/quicktime", byteCount: size))
                return
            }

            guard let image = info[.originalImage] as? UIImage,
                  // JPEG at 0.85: HEIC would be refused by nothing, but a phone photo is
                  // 12 MP and the feed serves it to everyone who scrolls past.
                  let data = image.jpegData(compressionQuality: 0.85)
            else { return }

            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("feed-\(UUID().uuidString).jpg")
            try? data.write(to: url)
            parent.onCaptured(MediaUpload(
                fileURL: url, filename: url.lastPathComponent,
                contentType: "image/jpeg", byteCount: Int64(data.count)))
        }

        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            parent.dismiss()
        }
    }
}
