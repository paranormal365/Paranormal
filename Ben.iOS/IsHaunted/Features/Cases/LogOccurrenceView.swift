import SwiftUI
import PhotosUI
import BenKit

/// Logging something that happened, from the phone. This is the case surface a phone is
/// genuinely better at than a laptop: people remember at 2am, standing in the room, with the
/// camera already in their hand.
struct LogOccurrenceView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let store: CaseDetailStore
    var onLogged: (MyCaseOccurrence) -> Void

    @State private var happenedAt = Date()
    @State private var knowsWhen = true
    @State private var title = ""
    @State private var detail = ""
    @State private var media: [MediaUpload] = []
    @State private var pickerItems: [PhotosPickerItem] = []
    @State private var showCamera = false
    @State private var isSaving = false
    @State private var errorMessage: String?

    private var canSave: Bool {
        !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !isSaving
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("What happened?", text: $title)
                    TextField("Anything else worth saying", text: $detail, axis: .vertical)
                        .lineLimit(3...8)
                } footer: {
                    Text("Your group sees this on the case timeline.")
                }

                Section {
                    Toggle("I know when it happened", isOn: $knowsWhen.animation())
                    if knowsWhen {
                        DatePicker("When", selection: $happenedAt,
                                   in: ...Date(), displayedComponents: [.date, .hourAndMinute])
                    }
                } footer: {
                    // Guessing a time and recording it as fact is worse than saying so.
                    Text(knowsWhen
                         ? "Roughly is fine — it's more useful than nothing."
                         : "It'll be filed under when you wrote it, and say so.")
                }

                if !media.isEmpty {
                    Section("Photos and video") {
                        ForEach(Array(media.enumerated()), id: \.offset) { index, upload in
                            HStack {
                                Image(systemName: upload.isVideo ? "video" : "photo")
                                    .foregroundStyle(Theme.ecto)
                                VStack(alignment: .leading) {
                                    Text(upload.filename).lineLimit(1)
                                    Text(upload.displaySize)
                                        .font(.caption).foregroundStyle(Theme.fog)
                                }
                                Spacer()
                                Button("Remove", role: .destructive) { remove(at: index) }
                                    .buttonStyle(.borderless)
                                    .disabled(isSaving)
                            }
                        }
                    }
                }

                Section {
                    PhotosPicker(selection: $pickerItems, matching: .any(of: [.images, .videos])) {
                        Label("Add photos or video", systemImage: "photo.on.rectangle")
                    }
                    .disabled(isSaving)

                    if UIImagePickerController.isSourceTypeAvailable(.camera) {
                        Button { showCamera = true } label: {
                            Label("Take a photo or video", systemImage: "camera")
                        }
                        .disabled(isSaving)
                    }
                }

                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle")
                            .foregroundStyle(Theme.danger).font(.callout)
                    }
                }
            }
            .navigationTitle("Log what happened")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.disabled(isSaving)
                }
                ToolbarItem(placement: .confirmationAction) {
                    if isSaving {
                        ProgressView()
                    } else {
                        Button("Save") { Task { await save() } }.disabled(!canSave)
                    }
                }
            }
            .onChange(of: pickerItems) { _, items in
                Task { await stage(items) }
            }
            .fullScreenCover(isPresented: $showCamera) {
                CameraPicker { media.append($0) }
            }
        }
        .interactiveDismissDisabled(isSaving)
    }

    private func stage(_ items: [PhotosPickerItem]) async {
        for item in items {
            guard let data = try? await item.loadTransferable(type: Data.self) else { continue }
            let isVideo = item.supportedContentTypes.contains { $0.conforms(to: .movie) }
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("case-\(UUID().uuidString).\(isVideo ? "mov" : "jpg")")
            guard (try? data.write(to: url)) != nil else { continue }
            media.append(MediaUpload(
                fileURL: url, filename: url.lastPathComponent,
                contentType: isVideo ? "video/quicktime" : "image/jpeg",
                byteCount: Int64(data.count)))
        }
        pickerItems = []
    }

    private func remove(at index: Int) {
        guard media.indices.contains(index) else { return }
        try? FileManager.default.removeItem(at: media[index].fileURL)
        media.remove(at: index)
    }

    private func save() async {
        guard canSave else { return }
        isSaving = true
        errorMessage = nil
        defer { isSaving = false }

        let result = await store.logOccurrence(
            eventDateTime: knowsWhen ? happenedAt : nil,
            title: title.trimmingCharacters(in: .whitespacesAndNewlines),
            body: detail.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                ? nil : detail.trimmingCharacters(in: .whitespacesAndNewlines),
            media: media)

        switch result {
        case .success(let entry):
            for upload in media { try? FileManager.default.removeItem(at: upload.fileURL) }
            onLogged(entry)
            dismiss()
        case .failure(let error):
            errorMessage = error.message
        }
    }
}
