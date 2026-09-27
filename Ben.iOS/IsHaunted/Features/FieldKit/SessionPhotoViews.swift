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
