import Foundation
import ImageIO
import Testing
@testable import BenKit

/// The `.ben` session bundle: what it is called, and what it does not carry.
@MainActor
struct SessionBundleTests {

    // MARK: - The name

    private func moment(_ iso: String) -> Date {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: iso)!
    }

    /// Ben, 2026-09-16: "How is the name of the bundle determined? What is it '?.ben'" — because
    /// it was `session-<uuid>.ben`, which tells somebody handed one absolutely nothing.
    @Test func theNameSaysWhenAndWhereRatherThanAnIdentifier() {
        let name = DeviceDataExporter.bundleName(
            startedAt: moment("2026-09-16T10:32:00Z"), label: "back bedroom, north wall")

        #expect(name.hasSuffix(".ben"))
        #expect(name.contains("back bedroom, north wall"))

    }

    @Test func aSessionWithNoLabelIsStillCalledSomething() {
        let name = DeviceDataExporter.bundleName(
            startedAt: moment("2026-09-16T10:32:00Z"), label: nil)
        #expect(name.hasSuffix("Field session.ben"))

        let blank = DeviceDataExporter.bundleName(
            startedAt: moment("2026-09-16T10:32:00Z"), label: "   ")
        #expect(blank.hasSuffix("Field session.ben"))
    }

    /// The date leads so a folder sorts into the order the nights happened.
    @Test func namesSortIntoTheOrderTheyWereRecorded() {
        let earlier = DeviceDataExporter.bundleName(
            startedAt: moment("2026-09-16T10:32:00Z"), label: "cellar")
        let later = DeviceDataExporter.bundleName(
            startedAt: moment("2026-11-02T01:15:00Z"), label: "attic")
        let nextYear = DeviceDataExporter.bundleName(
            startedAt: moment("2027-01-04T22:00:00Z"), label: "hall")

        #expect([later, nextYear, earlier].sorted() == [earlier, later, nextYear])
    }

    /// A label is somebody's free text, and it becomes a file name on a Mac.
    @Test func aLabelCannotMakeTheNameIntoAPath() {
        let name = DeviceDataExporter.bundleName(
            startedAt: moment("2026-09-16T10:32:00Z"),
            label: "../../etc/passwd: the \"cellar\"")

        #expect(!name.contains("/"))
        #expect(!name.contains("\\"))
        #expect(!name.contains(":"))
        #expect(!name.contains("\""))
        #expect(name.hasSuffix(".ben"))
    }

    @Test func anEnormousLabelIsCutRatherThanCarried() {
        let name = DeviceDataExporter.bundleName(
            startedAt: moment("2026-09-16T10:32:00Z"),
            label: String(repeating: "cellar ", count: 40))
        #expect(name.count < 100)
    }

    // MARK: - What a photograph carries into the bundle

    /// Writes a JPEG that has a position and a camera in it, exactly as a phone would.
    private func photographWithEXIF(at url: URL) throws {
        let size = 8
        let context = CGContext(
            data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)!
        context.setFillColor(CGColor(red: 0.1, green: 0.1, blue: 0.1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: size, height: size))
        let image = context.makeImage()!

        let destination = CGImageDestinationCreateWithURL(
            url as CFURL, "public.jpeg" as CFString, 1, nil)!
        let properties: [CFString: Any] = [
            kCGImagePropertyGPSDictionary: [
                kCGImagePropertyGPSLatitude: 35.92656,
                kCGImagePropertyGPSLatitudeRef: "N",
                kCGImagePropertyGPSLongitude: 86.86732,
                kCGImagePropertyGPSLongitudeRef: "W",
            ] as CFDictionary,
            kCGImagePropertyExifDictionary: [
                kCGImagePropertyExifDateTimeOriginal: "2026:09:16 10:32:00",
            ] as CFDictionary,
            kCGImagePropertyTIFFDictionary: [
                kCGImagePropertyTIFFMake: "Apple",
                kCGImagePropertyTIFFModel: "iPhone17,1",
                kCGImagePropertyTIFFOrientation: 6,
            ] as CFDictionary,
        ]
        CGImageDestinationAddImage(destination, image, properties as CFDictionary)
        #expect(CGImageDestinationFinalize(destination))
    }

    /// The reason this exists is NOT that a field photograph's position is unwanted — the session
    /// records position and heading on purpose. It is that a bundle is served as the bytes that
    /// were sent, and a session can be shared with its coordinates withheld: a JPEG still carrying
    /// a fix would hand over the address the document refused to give.
    @Test func aPhotographGoesIntoTheBundleWithNoPositionInIt() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("strip-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }

        let original = root.appendingPathComponent("photo-001.jpg")
        try photographWithEXIF(at: original)

        // The fixture has to actually carry what the test claims to remove, or this proves nothing.
        let before = ImageMetadataStripper.properties(of: original)
        #expect(before[kCGImagePropertyGPSDictionary] != nil)
        #expect(before[kCGImagePropertyExifDictionary] != nil)

        let cleaned = root.appendingPathComponent("clean.jpg")
        #expect(ImageMetadataStripper.write(original, to: cleaned))

        let after = ImageMetadataStripper.properties(of: cleaned)
        #expect(after[kCGImagePropertyGPSDictionary] == nil, "the fix survived the strip")

        let tiff = after[kCGImagePropertyTIFFDictionary] as? [CFString: Any]
        #expect(tiff?[kCGImagePropertyTIFFMake] == nil, "the camera survived the strip")
        #expect(tiff?[kCGImagePropertyTIFFModel] == nil)

        // And the original is exactly as it was: it stays on the phone, where it is the evidence.
        let stillThere = ImageMetadataStripper.properties(of: original)
        #expect(stillThere[kCGImagePropertyGPSDictionary] != nil)
    }

    /// A photograph that came out of the strip lying on its side would be a poor trade for a
    /// header nobody reads. Locked as behaviour rather than as proof of the implementation: it
    /// holds whether the TIFF block is trimmed field by field or removed whole, because ImageIO
    /// rewrites orientation from the source either way.
    @Test func aStrippedPhotographIsStillTheRightWayUp() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("strip-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }

        let original = root.appendingPathComponent("photo-001.jpg")
        try photographWithEXIF(at: original)
        let cleaned = root.appendingPathComponent("clean.jpg")
        #expect(ImageMetadataStripper.write(original, to: cleaned))

        let after = ImageMetadataStripper.properties(of: cleaned)
        #expect(after[kCGImagePropertyOrientation] as? Int == 6)
    }

    /// Sound and video are not images and are handed through untouched — a session's own
    /// recordings are the evidence, and remuxing them on a phone to scrub a header nobody wrote
    /// would cost a battery for nothing.
    @Test func aRecordingIsNotAnImageAndIsLeftAlone() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("strip-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }

        let audio = root.appendingPathComponent("audio-001.m4a")
        try Data(count: 2_048).write(to: audio)

        #expect(!ImageMetadataStripper.canStrip(audio))
        #expect(!ImageMetadataStripper.write(audio, to: root.appendingPathComponent("x.m4a")))
    }
}
