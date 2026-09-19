import Foundation

/// Reads the archives `ZipWriter` writes — and refuses everything else, out loud.
///
/// The other half of the `.ben` format. Ben, 2026-09-16: "someone else can share their .ben file
/// with another person on the iphone and the other person can view it like they had recorded it
/// themselves." Until this existed the phone could seal a bundle and never open one.
///
/// **Stored entries only, no Zip64.** The same rules the server's reader keeps: the writer never
/// deflates (the payload is already H.264, AAC and JPEG), so a member's bytes sit contiguously in
/// the file and can be copied straight out. Anything compressed, anything past 4 GB, and any
/// path that would leave the session directory is refused with a reason rather than guessed at —
/// a bundle is a file somebody else made, and an importer that trusts one is a way into the
/// phone.
///
/// Only the central directory is read to open one: a few kilobytes at the end, whatever the
/// bundle weighs. A member's bytes are read once, when it is extracted, in chunks.
public struct ZipReader: Sendable {

    /// One member: where it is in the file, and what the directory says it holds.
    public struct Entry: Sendable, Equatable {
        public var path: String
        public var byteCount: Int64
        /// Where the member's bytes begin — after its local header, which the directory does
        /// not describe exactly, so it was read too.
        public var dataOffset: Int64
        public var crc32: UInt32
    }

    /// More members than any session could honestly hold. Mirrors the server's ceiling.
    public static let mostEntries = 520

    /// A path inside a bundle is a name and maybe a folder, not an essay.
    public static let longestPath = 512

    /// How far back from the end the end-of-directory record can sit: its own 22 bytes plus a
    /// comment of at most 65 535.
    private static let endRecordSearch = 66_000

    private static let endSignature: UInt32 = 0x0605_4B50
    private static let centralSignature: UInt32 = 0x0201_4B50
    private static let localSignature: UInt32 = 0x0403_4B50

    public let url: URL
    public let entries: [Entry]

    /// Opens an archive, reading its directory. Throws for anything this reader does not take.
    public init(url: URL) throws {
        self.url = url
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        let length = Int64((try? handle.seekToEnd()) ?? 0)

        // ── The end-of-central-directory record, searched backwards ──
        let tailLength = Int(min(length, Int64(Self.endRecordSearch)))
        guard tailLength >= 22 else { throw ZipReaderError.notAnArchive }
        try handle.seek(toOffset: UInt64(length - Int64(tailLength)))
        let tail = try handle.read(upToCount: tailLength) ?? Data()
        guard tail.count == tailLength else { throw ZipReaderError.truncated }

        var endAt: Int?
        var index = tail.count - 22
        while index >= 0 {
            if tail.u32(at: index) == Self.endSignature { endAt = index; break }
            index -= 1
        }
        guard let endAt else { throw ZipReaderError.notAnArchive }

        let count = Int(tail.u16(at: endAt + 10))
        let directorySize = Int64(tail.u32(at: endAt + 12))
        let directoryOffset = Int64(tail.u32(at: endAt + 16))
        // 0xFFFF and 0xFFFFFFFF are the markers that say "look in the Zip64 record instead".
        guard count != 0xFFFF, directorySize != 0xFFFF_FFFF, directoryOffset != 0xFFFF_FFFF else {
            throw ZipReaderError.unsupported("Zip64 archives")
        }
        guard count <= Self.mostEntries else { throw ZipReaderError.tooManyEntries(count) }
        guard directoryOffset + directorySize <= length else { throw ZipReaderError.truncated }

        // ── The directory itself ──
        try handle.seek(toOffset: UInt64(directoryOffset))
        let directory = try handle.read(upToCount: Int(directorySize)) ?? Data()
        guard directory.count == Int(directorySize) else { throw ZipReaderError.truncated }

        var found: [Entry] = []
        var seen: Set<String> = []
        var cursor = 0
        for _ in 0..<count {
            guard cursor + 46 <= directory.count,
                  directory.u32(at: cursor) == Self.centralSignature
            else { throw ZipReaderError.notAnArchive }

            let method = directory.u16(at: cursor + 10)
            let crc = directory.u32(at: cursor + 16)
            let compressed = Int64(directory.u32(at: cursor + 20))
            let size = Int64(directory.u32(at: cursor + 24))
            let nameLength = Int(directory.u16(at: cursor + 28))
            let extraLength = Int(directory.u16(at: cursor + 30))
            let commentLength = Int(directory.u16(at: cursor + 32))
            let localOffset = Int64(directory.u32(at: cursor + 42))

            guard cursor + 46 + nameLength <= directory.count else { throw ZipReaderError.truncated }
            let nameBytes = directory[(directory.startIndex + cursor + 46)
                                      ..< (directory.startIndex + cursor + 46 + nameLength)]
            guard let path = String(data: nameBytes, encoding: .utf8) else {
                throw ZipReaderError.badPath("a name that is not text")
            }
            cursor += 46 + nameLength + extraLength + commentLength

            guard method == 0 else { throw ZipReaderError.unsupported("compressed members (\(path))") }
            guard compressed == size else { throw ZipReaderError.unsupported("a member whose sizes disagree (\(path))") }
            guard size != 0xFFFF_FFFF, localOffset != 0xFFFF_FFFF else {
                throw ZipReaderError.unsupported("Zip64 archives")
            }
            try Self.checkPath(path)
            guard seen.insert(path).inserted else { throw ZipReaderError.badPath("\(path) appears twice") }

            // The local header's name and extra lengths can differ from the directory's, so the
            // data offset comes from the local header and never from arithmetic on the central one.
            guard localOffset + 30 <= length else { throw ZipReaderError.truncated }
            try handle.seek(toOffset: UInt64(localOffset))
            let local = try handle.read(upToCount: 30) ?? Data()
            guard local.count == 30, local.u32(at: 0) == Self.localSignature else {
                throw ZipReaderError.notAnArchive
            }
            let dataOffset = localOffset + 30 + Int64(local.u16(at: 26)) + Int64(local.u16(at: 28))
            guard dataOffset + size <= length else { throw ZipReaderError.truncated }

            found.append(Entry(path: path, byteCount: size, dataOffset: dataOffset, crc32: crc))
        }
        entries = found
    }

    public func entry(_ path: String) -> Entry? {
        entries.first { $0.path == path }
    }

    /// A member's bytes, for the small ones — the document, the seal.
    public func data(of entry: Entry) throws -> Data {
        guard entry.byteCount <= 64 * 1024 * 1024 else {
            throw ZipReaderError.unsupported("reading \(entry.path) into memory — extract it instead")
        }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        try handle.seek(toOffset: UInt64(entry.dataOffset))
        let data = try handle.read(upToCount: Int(entry.byteCount)) ?? Data()
        guard data.count == Int(entry.byteCount) else { throw ZipReaderError.truncated }
        guard CRC32.checksum(data) == entry.crc32 else { throw ZipReaderError.corrupt(entry.path) }
        return data
    }

    /// Copies a member out to a file, in chunks, checking its CRC as it goes.
    public func extract(_ entry: Entry, to destination: URL) throws {
        let reader = try FileHandle(forReadingFrom: url)
        defer { try? reader.close() }
        try reader.seek(toOffset: UInt64(entry.dataOffset))

        try FileManager.default.createDirectory(
            at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
        if FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.removeItem(at: destination)
        }
        FileManager.default.createFile(atPath: destination.path, contents: nil)
        let writer = try FileHandle(forWritingTo: destination)
        defer { try? writer.close() }

        var remaining = entry.byteCount
        var crc = CRC32()
        while remaining > 0 {
            let chunk = try reader.read(upToCount: Int(min(remaining, 1 << 20))) ?? Data()
            guard !chunk.isEmpty else { throw ZipReaderError.truncated }
            crc.update(chunk)
            try writer.write(contentsOf: chunk)
            remaining -= Int64(chunk.count)
        }
        try writer.synchronize()
        guard crc.value == entry.crc32 else {
            try? FileManager.default.removeItem(at: destination)
            throw ZipReaderError.corrupt(entry.path)
        }
    }

    /// The same rule the format's `FileRef.relative` keeps on the way out: relative, forward
    /// slashes only, no upward traversal — so a bundle can never steer an importer outside the
    /// directory it was given.
    static func checkPath(_ path: String) throws {
        guard !path.isEmpty, path.count <= longestPath else { throw ZipReaderError.badPath(path) }
        guard !path.hasPrefix("/"), !path.contains("\\"), !path.contains("\0") else {
            throw ZipReaderError.badPath(path)
        }
        guard !path.split(separator: "/", omittingEmptySubsequences: false).contains("..") else {
            throw ZipReaderError.badPath(path)
        }
        guard !path.hasSuffix("/") else { throw ZipReaderError.badPath(path) }
    }
}

public enum ZipReaderError: Error, LocalizedError, Equatable {
    case notAnArchive
    case truncated
    case unsupported(String)
    case tooManyEntries(Int)
    case badPath(String)
    case corrupt(String)

    public var errorDescription: String? {
        switch self {
        case .notAnArchive: "it isn't a bundle at all."
        case .truncated: "the file is cut short — some of it never arrived."
        case .unsupported(let what): "it uses \(what), which this app doesn't read."
        case .tooManyEntries(let count): "it claims \(count) files, which no session holds."
        case .badPath(let path): "it holds a file name this app won't write to disk: \(path)."
        case .corrupt(let path): "\(path) doesn't match its own checksum."
        }
    }
}

private extension Data {
    func u16(at offset: Int) -> UInt16 {
        let i = startIndex + offset
        return UInt16(self[i]) | (UInt16(self[i + 1]) << 8)
    }

    func u32(at offset: Int) -> UInt32 {
        let i = startIndex + offset
        return UInt32(self[i]) | (UInt32(self[i + 1]) << 8)
            | (UInt32(self[i + 2]) << 16) | (UInt32(self[i + 3]) << 24)
    }
}
