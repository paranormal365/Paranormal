import Foundation
import Testing
@testable import BenKit

/// The reader opens exactly what the writer writes, and refuses what it must.
///
/// Both halves live in this app now: a phone seals a bundle and another phone opens it. The
/// refusals matter as much as the round trip — a bundle is a file somebody else made, and an
/// importer that trusts a path in one is a way into the phone.
@Suite("Zip reader")
struct ZipReaderTests {

    private func scratch() -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("zip-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    @Test("what the writer writes, the reader reads back byte for byte")
    func roundTrip() throws {
        let dir = scratch()
        let big = dir.appendingPathComponent("big.bin")
        // Past one chunk, so the streamed copy has to go round more than once.
        try Data((0..<(1 << 20) + 4_321).map { UInt8($0 % 251) }).write(to: big)
        let archive = dir.appendingPathComponent("session.ben")

        try ZipWriter().write([
            .init(path: "data.json", data: Data("{\"a\":1}".utf8)),
            .init(path: "media/audio-001.m4a", file: big),
            .init(path: "seal.json", data: Data("{}".utf8)),
        ], to: archive)

        let reader = try ZipReader(url: archive)
        #expect(reader.entries.map(\.path) == ["data.json", "media/audio-001.m4a", "seal.json"])
        #expect(try reader.data(of: reader.entry("data.json")!) == Data("{\"a\":1}".utf8))

        let out = dir.appendingPathComponent("out/audio-001.m4a")
        try reader.extract(reader.entry("media/audio-001.m4a")!, to: out)
        #expect(try Data(contentsOf: out) == Data(contentsOf: big))
        #expect(reader.entry("media/audio-001.m4a")!.byteCount == (1 << 20) + 4_321)
    }

    @Test("a member whose bytes were changed after sealing fails its checksum")
    func corruptMember() throws {
        let dir = scratch()
        let archive = dir.appendingPathComponent("session.ben")
        try ZipWriter().write([.init(path: "data.json", data: Data(repeating: 0x41, count: 200))], to: archive)

        // Flip one byte inside the member: past the 30-byte local header and the 9-byte name.
        var bytes = try Data(contentsOf: archive)
        bytes[30 + 9 + 50] ^= 0xFF
        try bytes.write(to: archive)

        let reader = try ZipReader(url: archive)
        #expect(throws: ZipReaderError.corrupt("data.json")) {
            _ = try reader.data(of: reader.entry("data.json")!)
        }
    }

    @Test("a file cut short is said to be, not read as a shorter archive")
    func truncated() throws {
        let dir = scratch()
        let archive = dir.appendingPathComponent("session.ben")
        try ZipWriter().write([.init(path: "data.json", data: Data(repeating: 0x41, count: 5_000))], to: archive)
        let bytes = try Data(contentsOf: archive)
        try bytes.prefix(bytes.count - 40).write(to: archive)

        #expect(throws: (any Error).self) { _ = try ZipReader(url: archive) }
    }

    @Test("something that is not a zip at all is refused as such")
    func notAnArchive() throws {
        let file = scratch().appendingPathComponent("photo.jpg")
        try Data(repeating: 0xFF, count: 3_000).write(to: file)
        #expect(throws: ZipReaderError.notAnArchive) { _ = try ZipReader(url: file) }
    }

    /// The writer does not police names — it is the app's own — so the reader must, because the
    /// archive it opens was written by somebody else's copy of the app, or by no app at all.
    @Test("a path that would leave the session folder is refused",
          arguments: ["../etc/passwd", "/absolute", "media\\backslash.m4a", "media/", "", "a/../b"])
    func badPaths(path: String) throws {
        #expect(throws: ZipReaderError.badPath(path)) { try ZipReader.checkPath(path) }
    }

    @Test("an archive carrying a bad path is refused on opening")
    func badPathInArchive() throws {
        let archive = scratch().appendingPathComponent("evil.ben")
        try ZipWriter().write([.init(path: "../escape.txt", data: Data("x".utf8))], to: archive)
        #expect(throws: ZipReaderError.badPath("../escape.txt")) { _ = try ZipReader(url: archive) }
    }

    @Test("the same name twice is refused rather than one silently winning")
    func duplicateName() throws {
        let archive = scratch().appendingPathComponent("twice.ben")
        try ZipWriter().write([
            .init(path: "data.json", data: Data("1".utf8)),
            .init(path: "data.json", data: Data("2".utf8)),
        ], to: archive)
        #expect(throws: ZipReaderError.badPath("data.json appears twice")) { _ = try ZipReader(url: archive) }
    }
}

/// The writer refuses what the format cannot hold, in a sentence, instead of trapping.
@Suite("Zip writer limits")
struct ZipWriterLimitTests {

    @Test("sizes a plain zip can address fit; anything past 4 GB does not")
    func fits() {
        #expect(ZipWriter.fits(sizes: [1, 2, 3], headerBytes: 100))
        #expect(ZipWriter.fits(sizes: [ZipWriter.mostBytes - 200], headerBytes: 100))
        #expect(!ZipWriter.fits(sizes: [ZipWriter.mostBytes + 1]))
        #expect(!ZipWriter.fits(sizes: [ZipWriter.mostBytes / 2, ZipWriter.mostBytes / 2 + 10]))
        #expect(!ZipWriter.fits(sizes: [-1]))
    }

    @Test("the refusal names the recording and says what to do")
    func sentence() {
        let text = ZipWriterError.tooLarge("media/video-003.mov").errorDescription ?? ""
        #expect(text.contains("video-003.mov"))
        #expect(text.contains("4 GB"))
        #expect(text.contains("Leave a video out"))
    }
}
