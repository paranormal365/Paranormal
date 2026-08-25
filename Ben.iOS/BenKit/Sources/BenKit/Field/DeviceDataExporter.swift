import Foundation
import CryptoKit

/// Turns a finished session into a Device Data Format v1 bundle.
///
/// `ProjectNotes/specs/DeviceDataFormat-v1.md`. The point of exporting to a published format is
/// that somebody else's software can read it — so the output is checked against the schema's own
/// rules by tests, not just against what this app happens to expect.
///
/// Readings are spliced from the session's log LINE BY LINE rather than decoded and re-encoded.
/// A five-hour session is tens of thousands of records and never needs to be in memory at once.
public struct DeviceDataExporter: Sendable {

    private let files: SessionFileStore

    public init(files: SessionFileStore) {
        self.files = files
    }

    /// What goes into the bundle. Media is chosen by the caller, because an investigator picks
    /// what to hand over — not everything recorded is worth sending, and some of it is large.
    public struct Request: Sendable {
        public var sessionId: UUID
        public var startedAt: Date
        public var endedAt: Date?
        public var locationLabel: String?
        public var deviceModel: String
        public var timezone: String?
        public var batteryPercentAtStart: Double?
        public var trigger: DeviceDataEnvelope.Trigger
        /// Relative paths, as stored on the session.
        public var includedMedia: [String]

        public init(sessionId: UUID, startedAt: Date, endedAt: Date?, locationLabel: String?,
                    deviceModel: String, timezone: String?, batteryPercentAtStart: Double?,
                    trigger: DeviceDataEnvelope.Trigger, includedMedia: [String]) {
            self.sessionId = sessionId
            self.startedAt = startedAt
            self.endedAt = endedAt
            self.locationLabel = locationLabel
            self.deviceModel = deviceModel
            self.timezone = timezone
            self.batteryPercentAtStart = batteryPercentAtStart
            self.trigger = trigger
            self.includedMedia = includedMedia
        }
    }

    public struct Result: Sendable {
        public var url: URL
        public var byteCount: Int64
        public var readingCount: Int
        public var mediaCount: Int
        /// Files named by readings but left out of this bundle. The document still refers to
        /// them, so a reader is told rather than left wondering where they went.
        public var omittedMedia: [String]
    }

    /// Builds `data.json` on its own — the document that describes a session, without its media.
    /// This is what a server import wants first: small, checkable, and complete on its own.
    public func buildDocument(_ request: Request, log: ReadingLog) async throws -> Data {
        let envelope = DeviceDataEnvelope(
            device: .init(manufacturer: "Apple", model: request.deviceModel),
            session: .init(startedAt: request.startedAt,
                           endedAt: request.endedAt,
                           batteryPercentAtStart: request.batteryPercentAtStart,
                           locationLabel: request.locationLabel,
                           timezone: request.timezone,
                           trigger: request.trigger))

        // Encoded with no readings, then the array spliced in — so the readings never have to
        // be held as objects.
        var document = try DeviceDataJSON.encoder.encode(envelope)
        let lines = try await log.rawLines()

        guard let insertion = Self.readingsArrayRange(in: document) else {
            throw ExportError.couldNotBuildDocument
        }

        var readings = Data("[".utf8)
        for (index, line) in lines.enumerated() {
            if index > 0 { readings.append(0x2C) }   // comma
            readings.append(line)
        }
        readings.append(0x5D)                        // ]
        document.replaceSubrange(insertion, with: readings)
        return document
    }

    /// Writes the whole bundle: `data.json` plus the chosen media under `media/`.
    public func export(_ request: Request, log: ReadingLog, to directory: URL) async throws -> Result {
        var document = try await buildDocument(request, log: log)

        // Stamp each included file's digest into the readings that name it, so a reader can
        // prove the pairing survived transit — audio attached to the wrong reading is worse
        // than no audio.
        var entries: [ZipWriter.Entry] = []
        var included: Set<String> = []

        for path in request.includedMedia {
            let url = files.fileURL(for: request.sessionId, relativePath: path)
            guard FileManager.default.fileExists(atPath: url.path) else { continue }
            entries.append(ZipWriter.Entry(path: path, file: url))
            included.insert(path)

            if let digest = try? Self.sha256(of: url) {
                document = Self.stampDigest(digest, forFilename: path, in: document)
            }
        }

        let named = Self.mediaPathsNamed(in: document)
        let omitted = named.subtracting(included).sorted()

        entries.insert(ZipWriter.Entry(path: "data.json", data: document), at: 0)

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let destination = directory
            .appendingPathComponent("session-\(request.sessionId.uuidString.lowercased()).zip")
        if FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.removeItem(at: destination)
        }

        let bytes = try ZipWriter().write(entries, to: destination)
        let readingCount = try await log.lineCount()

        return Result(url: destination, byteCount: bytes, readingCount: readingCount,
                      mediaCount: included.count, omittedMedia: omitted)
    }

    // MARK: - Helpers

    /// Where `"readings":[]` sits in the encoded envelope.
    static func readingsArrayRange(in document: Data) -> Range<Data.Index>? {
        let needle = Data("\"readings\":".utf8)
        guard let keyRange = document.range(of: needle) else { return nil }
        guard let open = document[keyRange.upperBound...].firstIndex(of: 0x5B) else { return nil }
        guard let close = document[open...].firstIndex(of: 0x5D) else { return nil }
        return open..<(close + 1)
    }

    /// Public because the upload path needs the same digest the export stamps, and the
    /// server checks the two against each other.
    public static func sha256(of url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty {
            hasher.update(data: chunk)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    /// Adds `"sha256":"…"` to every `audio_ref` naming this file.
    static func stampDigest(_ digest: String, forFilename path: String, in document: Data) -> Data {
        guard var text = String(data: document, encoding: .utf8) else { return document }

        // Both spellings: this encoder leaves slashes alone, but a log written by an older build
        // has them escaped, and those lines are spliced in verbatim.
        var stamped = false
        for spelling in [path, path.replacingOccurrences(of: "/", with: "\\/")] {
            let marker = "\"filename\":\"\(spelling)\""
            guard text.contains(marker) else { continue }
            text = text.replacingOccurrences(
                of: marker, with: marker + ",\"sha256\":\"\(digest)\"")
            stamped = true
        }
        return stamped ? (text.data(using: .utf8) ?? document) : document
    }

    /// Every media path the document refers to — from `audio_ref` names and from the capture
    /// notes that carry photo and video paths.
    static func mediaPathsNamed(in document: Data) -> Set<String> {
        guard let text = String(data: document, encoding: .utf8) else { return [] }
        var found: Set<String> = []

        // `filename` covers audio; the capture notes carry photo and video paths, since v1 has
        // no field for them.
        for pattern in ["\"filename\":\"", "photo: ", "video: ", "audio: "] {
            var search = text[...]
            while let start = search.range(of: pattern) {
                let rest = search[start.upperBound...]
                if let end = rest.firstIndex(of: "\"") {
                    let candidate = String(rest[rest.startIndex..<end])
                        .replacingOccurrences(of: "\\/", with: "/")
                    if candidate.hasPrefix("media/") { found.insert(candidate) }
                }
                search = rest
            }
        }
        return found
    }
}

public enum ExportError: Error, LocalizedError {
    case couldNotBuildDocument

    public var errorDescription: String? {
        switch self {
        case .couldNotBuildDocument: "The session document couldn't be assembled."
        }
    }
}
