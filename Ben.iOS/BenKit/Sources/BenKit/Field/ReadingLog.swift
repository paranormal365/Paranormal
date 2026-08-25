import Foundation

/// The append-only log a session's readings stream into: one JSON object per line, already
/// shaped exactly as the spec's `readings[]` entries.
///
/// **Why a file and not the database.** A five-hour hybrid session is tens of thousands of
/// readings. Writing those through a MainActor SwiftData context would fight the live screen for
/// the main thread all night, and export would have to re-serialise every row anyway. A line of
/// JSON costs an append; export splices the lines verbatim between an envelope's `"readings": [`
/// and `]`.
///
/// **Why it survives a crash.** Each reading is one `write(2)` of a complete line ending in a
/// newline. If the phone dies mid-write the tail is a partial line and nothing before it is
/// harmed — `recover()` truncates the torn tail and reports what survived. The alternative, a
/// single JSON array, is unreadable the moment it is not closed, which is precisely the moment
/// you most want the data.
public actor ReadingLog {

    private let fileURL: URL
    private var handle: FileHandle?
    private var appendsSinceSync = 0
    /// Every 20 readings ≈ every 40 s at the default heartbeat. Frequent enough that a hard
    /// crash costs seconds, rare enough not to thrash the flash all night.
    private let syncEvery = 20

    public init(fileURL: URL) {
        self.fileURL = fileURL
    }

    public var url: URL { fileURL }

    /// Appends one reading. The line is built completely before any of it is written, so a
    /// failure to encode cannot leave half an object in the file.
    public func append(_ reading: FieldReading) throws {
        var line = try DeviceDataJSON.encoder.encode(reading)
        line.append(0x0A)   // newline

        let handle = try openedHandle()
        try handle.write(contentsOf: line)

        appendsSinceSync += 1
        if appendsSinceSync >= syncEvery {
            try handle.synchronize()
            appendsSinceSync = 0
        }
    }

    /// Flushes and closes. Safe to call more than once — ending a session that was already
    /// ended must not throw.
    public func close() throws {
        guard let handle else { return }
        try handle.synchronize()
        try handle.close()
        self.handle = nil
        appendsSinceSync = 0
    }

    /// Repairs a log left behind by a crash and returns how many readings survived.
    ///
    /// A torn final line — the tail of a write that never finished — is truncated. Anything
    /// before it is intact by construction, which is the whole point of the format.
    @discardableResult
    public func recover() throws -> Int {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return 0 }
        try close()

        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        guard !data.isEmpty else { return 0 }

        // Everything up to and including the last newline is whole; anything after it is a
        // partial write.
        guard let lastNewline = data.lastIndex(of: 0x0A) else {
            // Not one complete line — the file is a single torn write.
            try Data().write(to: fileURL)
            return 0
        }

        let whole = data[data.startIndex...lastNewline]
        if whole.count != data.count {
            try Data(whole).write(to: fileURL)
        }

        return Data(whole).split(separator: UInt8(0x0A), omittingEmptySubsequences: true).count
    }

    /// Every reading, decoded. Used by export and the review chart.
    ///
    /// A line that will not decode is SKIPPED rather than failing the read: one unreadable
    /// record must not cost a reviewer the other twenty thousand.
    public func readings() throws -> [FieldReading] {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return [] }
        try? close()

        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        return data.split(separator: UInt8(0x0A), omittingEmptySubsequences: true)
            .compactMap { try? DeviceDataJSON.decoder.decode(FieldReading.self, from: Data($0)) }
    }

    /// The raw lines, for splicing into an export without a decode/re-encode round trip.
    public func rawLines() throws -> [Data] {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return [] }
        try? close()
        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        return data.split(separator: UInt8(0x0A), omittingEmptySubsequences: true).map { Data($0) }
    }

    public func lineCount() throws -> Int {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return 0 }
        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        return data.split(separator: UInt8(0x0A), omittingEmptySubsequences: true).count
    }

    private func openedHandle() throws -> FileHandle {
        if let handle { return handle }

        let manager = FileManager.default
        if !manager.fileExists(atPath: fileURL.path) {
            try manager.createDirectory(at: fileURL.deletingLastPathComponent(),
                                        withIntermediateDirectories: true)
            manager.createFile(atPath: fileURL.path, contents: nil)
        }

        let handle = try FileHandle(forWritingTo: fileURL)
        try handle.seekToEnd()
        self.handle = handle
        return handle
    }
}
