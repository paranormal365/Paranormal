import SwiftUI

/// A photograph from a session's own directory, decoded at the size it is drawn.
///
/// Through `Thumbnails` and never `UIImage(contentsOfFile:)`: a replay with a strip of twelve-
/// megapixel photos would otherwise decode every one of them whole, on the main thread.
struct SessionPhotoTile: View {
    let url: URL
    let maxPixels: Int
    let contentMode: ContentMode

    @State private var image: UIImage?
    @State private var missing = false

    var body: some View {
        ZStack {
            Theme.mist
            if let image {
                Image(uiImage: image)
                    .resizable()
                    .aspectRatio(contentMode: contentMode)
            } else if missing {
                // A row whose bytes were cleared from the phone says so, rather than showing a grey
                // square nobody can explain.
                VStack(spacing: 4) {
                    Image(systemName: "photo.badge.exclamationmark")
                    Text("not on this phone").font(.caption2)
                }
                .foregroundStyle(Theme.fog)
            } else {
                ProgressView()
            }
        }
        .clipped()
        .task(id: url) {
            guard FileManager.default.fileExists(atPath: url.path) else { missing = true; return }
            image = await Thumbnails.load(url, maxPixels: maxPixels)
            missing = image == nil
        }
    }
}

/// One photograph, full screen, with when it was taken.
struct SessionPhotoViewer: View {
    @Environment(\.dismiss) private var dismiss
    let url: URL
    let caption: String

    var body: some View {
        ZStack(alignment: .topTrailing) {
            Color.black.ignoresSafeArea()
            // Big enough for an iPad Pro screen at 2×; the original stays untouched on disk.
            SessionPhotoTile(url: url, maxPixels: 2800, contentMode: .fit)
                .background(Color.black)
                .ignoresSafeArea()
            VStack(alignment: .trailing) {
                Button { dismiss() } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.title)
                        .symbolRenderingMode(.palette)
                        .foregroundStyle(.white, .black.opacity(0.6))
                }
                .accessibilityLabel("Close")
                .accessibilityIdentifier("close-session-photo")
                Spacer()
                Text(caption)
                    .font(.caption)
                    .padding(.horizontal, 10).padding(.vertical, 6)
                    .background(.black.opacity(0.6), in: Capsule())
                    .foregroundStyle(.white)
                    .frame(maxWidth: .infinity)
            }
            .padding(16)
        }
    }
}
