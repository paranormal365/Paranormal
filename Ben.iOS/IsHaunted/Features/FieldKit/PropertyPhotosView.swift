import SwiftUI
import BenKit

/// The photographs of the place itself.
///
/// Distinct from evidence: these are pictures of the property — the front of the house, the room
/// somebody keeps hearing, the stairs. One of them can be chosen to REPRESENT the case and the
/// investigation, so a list of cases shows the building rather than a row of identical folders.
///
/// Choosing is optional and reversible. Most photographs in a session are evidence and should
/// never be a portrait of anything, so nothing is picked unless somebody picks it.
struct PropertyPhotosView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let sessionId: UUID

    @State private var photos: [CaptureMark] = []
    @State private var problem: String?

    private var store: FieldSessionStore { dependencies.fieldKit }

    private let columns = [GridItem(.adaptive(minimum: 104), spacing: 8)]

    var body: some View {
        NavigationStack {
            Group {
                if photos.isEmpty {
                    ContentUnavailableView {
                        Label("No photos in this session", systemImage: "photo.on.rectangle")
                    } description: {
                        Text("Take one from the session screen and it will appear here.")
                    }
                } else {
                    ScrollView {
                        LazyVGrid(columns: columns, spacing: 8) {
                            ForEach(photos) { photo in
                                tile(photo)
                            }
                        }
                        .padding(12)

                        Text("The one you choose stands for this place on the case and the investigation. Tap it again to un-choose it.")
                            .font(.caption).foregroundStyle(Theme.fog)
                            .padding(.horizontal, 16)
                    }
                }
            }
            .background(Theme.ink)
            .navigationTitle("Property photos")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .alert("Couldn't change that",
                   isPresented: Binding(get: { problem != nil },
                                        set: { if !$0 { problem = nil } })) {
                Button("OK", role: .cancel) { problem = nil }
            } message: { Text(problem ?? "") }
            .onAppear { reload() }
        }
    }

    @ViewBuilder
    private func tile(_ photo: CaptureMark) -> some View {
        let url = store.files.fileURL(for: sessionId, relativePath: photo.relativePath)
        let exists = store.hasLocalFile(photo.relativePath, in: sessionId)

        VStack(alignment: .leading, spacing: 3) {
        Button {
            choose(photo)
        } label: {
            ZStack(alignment: .bottomLeading) {
            ZStack(alignment: .topTrailing) {
                if exists, let image = UIImage(contentsOfFile: url.path) {
                    Image(uiImage: image)
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                        .frame(height: 104)
                        .clipped()
                } else {
                    // The row survives its bytes when somebody has cleared the phone. Saying so
                    // beats a grey square nobody can explain.
                    VStack(spacing: 4) {
                        Image(systemName: "icloud")
                        Text("on the server").font(.caption2)
                    }
                    .foregroundStyle(Theme.fog)
                    .frame(maxWidth: .infinity, minHeight: 104)
                    .background(Theme.mist)
                }

                if photo.isRepresentative {
                    Image(systemName: "star.fill")
                        .font(.caption)
                        .padding(5)
                        .background(.black.opacity(0.6), in: Circle())
                        .foregroundStyle(Theme.warning)
                        .padding(5)
                }
            }
            // The room the operator said they were in. A photograph of "a doorway" is worth
            // very little a month later; "Cellar — doorway" is worth a great deal, and nothing
            // else in the file can supply it.
            if let room = photo.room {
                Text(room)
                    .font(.caption2.weight(.semibold))
                    .lineLimit(1)
                    .padding(.horizontal, 5).padding(.vertical, 2)
                    .background(.black.opacity(0.55), in: Capsule())
                    .foregroundStyle(Theme.bone)
                    .padding(5)
                    .accessibilityIdentifier("property-photo-room")
            }
            }
            .clipShape(RoundedRectangle(cornerRadius: 10))
            .overlay(
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(photo.isRepresentative ? Theme.warning : .clear, lineWidth: 2))
        }
        .buttonStyle(.plain)
        .disabled(!exists)
        .accessibilityIdentifier("property-photo")
        .accessibilityLabel(photo.isRepresentative
                            ? "Chosen to represent this place"
                            : "Photo, tap to choose it for this place")
        }
    }

    private func choose(_ photo: CaptureMark) {
        do {
            try store.setRepresentative(photo.id, in: sessionId)
            reload()
        } catch {
            problem = error.localizedDescription
        }
    }

    private func reload() {
        photos = store.captures(for: sessionId).filter { $0.kind == .photo }
    }
}
