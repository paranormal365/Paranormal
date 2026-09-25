import UIKit
import ImageIO

/// Small copies of big pictures, decoded at the size they will be drawn.
///
/// `UIImage(data:)` and `UIImage(contentsOfFile:)` decode the whole picture the first time it is
/// drawn — twelve megapixels, some 48 MB, for a tile a hundred points wide — and a grid of them is
/// how an app gets killed for memory in the middle of a review. ImageIO can decode straight to a
/// smaller size, reading only what it needs, and that is all this does. Off the main thread,
/// because even the small decode of a large JPEG is tens of milliseconds and a grid asks for
/// twenty at once.
enum Thumbnails {

    /// The picture at `url`, no longer than `maxPixels` on its longest side, the right way up.
    static func load(_ url: URL, maxPixels: Int) async -> UIImage? {
        await Task.detached(priority: .userInitiated) {
            decode(CGImageSourceCreateWithURL(url as CFURL, nil), maxPixels: maxPixels)
        }.value
    }

    /// The same, from bytes already in hand.
    static func load(_ data: Data, maxPixels: Int) async -> UIImage? {
        await Task.detached(priority: .userInitiated) {
            decode(CGImageSourceCreateWithData(data as CFData, nil), maxPixels: maxPixels)
        }.value
    }

    private static func decode(_ source: CGImageSource?, maxPixels: Int) -> UIImage? {
        guard let source else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            // Applies the EXIF orientation, so a portrait photo is not handed over on its side.
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixels,
            kCGImageSourceShouldCacheImmediately: true,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else {
            return nil
        }
        return UIImage(cgImage: image)
    }
}
