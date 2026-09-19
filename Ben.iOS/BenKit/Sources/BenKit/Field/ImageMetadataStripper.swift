import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Writes a copy of a photograph with its EXIF taken off.
///
/// **Why, exactly.** Not because a field photograph's position is unwanted — the app records
/// position and heading on purpose and draws them on a map, and Ben put it plainly on 2026-09-16:
/// "the app records the location and even direction they are pointing... isn't that the same thing
/// the EXIF contains?" It is.
///
/// It comes off because of the SERVE boundary the site already keeps: the group keeps the facts,
/// the served file does not carry them. A session can be shared with positions withheld, and the
/// server nulls every coordinate in `data.json` to honour that — while a JPEG whose EXIF still
/// holds a fix hands over the address the document refused to give. Everything a session uploads
/// one file at a time is sanitized on arrival for exactly that reason; a `.ben` is served as the
/// bytes that were sent, so the stripping has to happen before it is sealed.
///
/// The original is untouched. It stays on the phone, where it is the evidence.
public enum ImageMetadataStripper {

    /// Content types this can strip. Anything else is handed back as it was.
    public static func canStrip(_ url: URL) -> Bool {
        guard let type = UTType(filenameExtension: url.pathExtension.lowercased()) else { return false }
        return type.conforms(to: .image)
    }

    /// A copy at `destination` with EXIF, GPS, IPTC and TIFF's camera fields removed.
    ///
    /// Returns false when the file is not an image this can rewrite, or when rewriting fails —
    /// and a false is not an error to raise: the caller sends the original, which is what it would
    /// have sent anyway. A photograph that will not re-encode must not cost somebody the night.
    @discardableResult
    public static func write(_ url: URL, to destination: URL) -> Bool {
        guard canStrip(url),
              let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              CGImageSourceGetCount(source) > 0,
              let type = CGImageSourceGetType(source)
        else { return false }

        guard let target = CGImageDestinationCreateWithURL(
            destination as CFURL, type, 1, nil) else { return false }

        // kCFNull rather than an empty dictionary: ImageIO reads null as "remove this block" and
        // an empty one as "change nothing in it", which is the difference between a stripped photo
        // and one that only looks stripped.
        let removals: [CFString: Any] = [
            kCGImagePropertyExifDictionary: kCFNull as Any,
            kCGImagePropertyExifAuxDictionary: kCFNull as Any,
            kCGImagePropertyGPSDictionary: kCFNull as Any,
            kCGImagePropertyIPTCDictionary: kCFNull as Any,
            kCGImagePropertyMakerAppleDictionary: kCFNull as Any,
            // Named fields rather than the whole TIFF block, because only the camera and the
            // moment are being removed and the rest of that block is how the file describes
            // itself. (Orientation survives either way — ImageIO rewrites it from the source — so
            // this is about not removing more than was meant, not about rescuing it.)
            kCGImagePropertyTIFFDictionary: [
                kCGImagePropertyTIFFMake: kCFNull as Any,
                kCGImagePropertyTIFFModel: kCFNull as Any,
                kCGImagePropertyTIFFDateTime: kCFNull as Any,
                kCGImagePropertyTIFFSoftware: kCFNull as Any,
            ] as CFDictionary,
        ]

        CGImageDestinationAddImageFromSource(target, source, 0, removals as CFDictionary)
        return CGImageDestinationFinalize(target)
    }

    /// What a stripped copy still carries, for a test to check rather than assume.
    public static func properties(of url: URL) -> [CFString: Any] {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              CGImageSourceGetCount(source) > 0,
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil)
                  as? [CFString: Any]
        else { return [:] }
        return properties
    }
}
