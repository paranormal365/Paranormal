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

    /// What a session bundle is called.
    ///
    /// Ben, 2026-09-16: "Even if we need to give it its own type like .ben but know that is a
    /// field session zipped for player." A ZIP underneath, and named for what it is rather than
    /// for how it is packed — so a phone handed one knows to open it, and a person looking at one
    /// knows what they have. The same file the site takes in, hands back, and plays out of.
    public static let fileExtension = ".ben"

    /// What it is, when something asks for a type rather than a name.
    public static let contentType = "application/vnd.ishaunted.field-session"

    /// What to call a session's bundle.
    ///
    /// **A name a person can read.** These were `session-<uuid>.ben`, which is fine for a thing
    /// only a server ever opens and useless the moment somebody AirDrops one — a phone showing
    /// "session-afbc7810-1ae8-4a57-90a9-2b2380047d64.ben" has told the person nothing about what
    /// they are being handed. The id still travels inside `data.json`, where it is what actually
    /// identifies the session; the server matches on that and never on the file name.
    ///
    /// The date leads so a folder of them sorts into the order they were recorded, and it is
    /// written year-first because that is the one spelling nobody can misread — the rule that
    /// dates are unambiguous, kept somewhere a slash cannot go.
    public static func bundleName(startedAt: Date, label: String?) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd HHmm"
        let stamp = formatter.string(from: startedAt)

        let words = cleanedForFileName(label) ?? "Field session"
        return "\(stamp) \(words)\(fileExtension)"
    }

    /// The operator's own words, made safe to be a file name — or nil if nothing is left.
    ///
    /// Slashes and colons are the two that matter: one makes a path and the other is a separator
    /// on the Mac these land on. The rest is trimming, so a label somebody typed with a trailing
    /// space does not become a file name with one.
    static func cleanedForFileName(_ label: String?) -> String? {
        guard let label else { return nil }
        let forbidden = CharacterSet(charactersIn: "/\\:?%*|\"<>\u{0}")
        let cleaned = label.components(separatedBy: forbidden).joined(separator: " ")
            .components(separatedBy: .whitespacesAndNewlines)
            .filter { !$0.isEmpty }
            .joined(separator: " ")
        // Long enough for a real description of a room, short enough to survive every filesystem
        // and every mail attachment name this may pass through.
        let trimmed = String(cleaned.prefix(60)).trimmingCharacters(in: .whitespaces)
        return trimmed.isEmpty ? nil : trimmed
    }

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

        /// A file to use in place of the session's own, by relative path.
        ///
        /// How a trimmed or shrunk recording reaches the bundle: the send screen cuts and
        /// converts into a scratch directory first, and hands the result here. The bundle keeps
        /// the ORIGINAL path — the readings name it, and rewriting those to point at a temporary
        /// file name would break every reference in the document. The session's own copy is never
        /// touched either way.
        public var substitutes: [String: URL]
        /// Details added to marks after the session (`FieldSessionStore.markDetails(for:)`).
        public var markDetails: [DeviceDataEnvelope.MarkDetails] = []
        /// The account signed in when this was recorded, and the device that recorded it.
        ///
        /// Both go into the seal. Neither is required: a session recorded with nobody signed in
        /// is an ordinary session, and the device id is best-effort by nature.
        public var recordedByAccountId: UUID?
        public var deviceId: String?
        /// The stretch to send, when the operator chose one (item 210).
        ///
        /// **Nil means the whole session**, which is what every caller wanting the old behaviour
        /// gets by leaving it out. When set, readings outside it are not written and the
        /// document's own `started_at` and `ended_at` become the window's — otherwise the server
        /// would record an hour-long session holding three readings, and the player would draw an
        /// hour of empty timeline around them.
        public var window: SessionWindow?

        public init(sessionId: UUID, startedAt: Date, endedAt: Date?, locationLabel: String?,
                    deviceModel: String, timezone: String?, batteryPercentAtStart: Double?,
                    trigger: DeviceDataEnvelope.Trigger, includedMedia: [String],
                    substitutes: [String: URL] = [:],
                    recordedByAccountId: UUID? = nil, deviceId: String? = nil,
                    window: SessionWindow? = nil) {
            self.sessionId = sessionId
            self.startedAt = startedAt
            self.endedAt = endedAt
            self.locationLabel = locationLabel
            self.deviceModel = deviceModel
            self.timezone = timezone
            self.batteryPercentAtStart = batteryPercentAtStart
            self.trigger = trigger
            self.includedMedia = includedMedia
            self.substitutes = substitutes
            self.recordedByAccountId = recordedByAccountId
            self.deviceId = deviceId
            self.window = window
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
        // A trimmed session declares the WINDOW as its span. The alternative — keeping the
        // original start and end — would tell every reader the session ran for an hour and then
        // hand them three readings, which reads as a night of missing data rather than as a
        // deliberate excerpt.
        let envelope = DeviceDataEnvelope(
            device: .init(manufacturer: "Apple", model: request.deviceModel),
            session: .init(startedAt: request.window?.start ?? request.startedAt,
                           endedAt: request.window?.end ?? request.endedAt,
                           batteryPercentAtStart: request.batteryPercentAtStart,
                           locationLabel: request.locationLabel,
                           timezone: request.timezone,
                           trigger: request.trigger,
                           recordedByAccountId: request.recordedByAccountId),
            // Only the marks inside a chosen stretch: details for a mark that was cut would name a
            // moment the document no longer has.
            markDetails: Self.details(request.markDetails, within: request.window))

        // Encoded with no readings, then the array spliced in — so the readings never have to
        // be held as objects.
        var document = try DeviceDataJSON.encoder.encode(envelope)
        let lines = Self.inTimeOrder(try await log.rawLines(within: request.window))

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

    static func details(_ all: [DeviceDataEnvelope.MarkDetails],
                        within window: SessionWindow?) -> [DeviceDataEnvelope.MarkDetails]? {
        let kept = all.filter { detail in window.map { detail.at >= $0.start && detail.at <= $0.end } ?? true }
        return kept.isEmpty ? nil : kept
    }

    /// Writes the whole bundle: `data.json` plus the chosen media under `media/`.
    /// - Parameter document: a document already built and adjusted by the caller, when it has one.
    ///   The send screen rebases each cut recording's audio offsets before sealing, and rebuilding
    ///   the document here would quietly throw that away — putting every trimmed recording back as
    ///   far from its readings as the amount cut off its front.
    public func export(_ request: Request, log: ReadingLog, to directory: URL,
                       document prepared: Data? = nil) async throws -> Result {
        var document: Data
        if let prepared { document = prepared } else { document = try await buildDocument(request, log: log) }

        // Stamp each included file's digest into the readings that name it, so a reader can
        // prove the pairing survived transit — audio attached to the wrong reading is worse
        // than no audio.
        var entries: [ZipWriter.Entry] = []
        var included: Set<String> = []

        // Stripped copies of photographs live here for as long as it takes to seal the bundle.
        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("bundle-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: scratch) }

        for path in request.includedMedia {
            // A cut or shrunk copy stands in for the session's own, under the same path.
            let original = request.substitutes[path]
                ?? files.fileURL(for: request.sessionId, relativePath: path)
            guard FileManager.default.fileExists(atPath: original.path) else { continue }

            // A photograph goes in with its EXIF off. Not because the position is unwanted — the
            // session records position and heading on purpose — but because a bundle is served as
            // the bytes that were sent, and a session can be shared with its coordinates withheld.
            // A JPEG still carrying a fix would hand over the address the document refused. The
            // original is untouched on the phone, where it is the evidence.
            var url = original
            if ImageMetadataStripper.canStrip(original) {
                let clean = scratch.appendingPathComponent(
                    (path as NSString).lastPathComponent)
                if ImageMetadataStripper.write(original, to: clean) { url = clean }
            }

            entries.append(ZipWriter.Entry(path: path, file: url))
            included.insert(path)

            // Digested from what is actually going IN. Stamping the original's digest over a
            // stripped copy would make every photograph fail the check it exists to pass.
            if let digest = try? Self.sha256(of: url) {
                document = Self.stampDigest(digest, forFilename: path, in: document)
            }
        }

        // Comparisons with a place's room scan go in with the readings that name them (LiDAR plan,
        // phase 2): what they found is the session's own finding. A place's scan itself is never in
        // a session — it belongs to the place. So do the person map's joints files and pictures
        // (phase 4), with the videos they belong to.
        // A trimmed video's joints files are moved to its clock, and stand in under the same path.
        let companions = Self.comparisonPathsNamed(in: document)
            .union(Self.figurePathsNamed(in: document, withVideos: Set(request.includedMedia)))
        for path in companions.sorted() where !included.contains(path) {
            let url = request.substitutes[path] ?? files.fileURL(for: request.sessionId, relativePath: path)
            guard FileManager.default.fileExists(atPath: url.path) else { continue }
            entries.append(ZipWriter.Entry(path: path, file: url))
            included.insert(path)
        }

        let named = Self.mediaPathsNamed(in: document)
        let omitted = named.subtracting(included).sorted()

        entries.insert(ZipWriter.Entry(path: "data.json", data: document), at: 0)

        // The seal goes in last and covers everything else, including data.json — so a changed
        // reading is as detectable as a swapped recording. It cannot cover itself, which is why
        // it is added after the digests are taken rather than counted among them.
        var sealed: [SessionSeal.Entry] = []
        for entry in entries {
            guard let (digest, size) = try? Self.digestAndSize(of: entry) else { continue }
            sealed.append(SessionSeal.Entry(path: entry.path, sha256: digest, byteCount: size))
        }
        let seal = SessionSeal(
            recordedByAccountId: request.recordedByAccountId,
            deviceId: request.deviceId,
            sessionId: request.sessionId,
            sealedAt: Date(),
            entries: sealed)
        entries.append(ZipWriter.Entry(
            path: SessionSeal.entryPath, data: try DeviceDataJSON.encoder.encode(seal)))

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let destination = directory
            .appendingPathComponent(
                Self.bundleName(startedAt: request.startedAt, label: request.locationLabel))
        if FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.removeItem(at: destination)
        }

        let bytes = try ZipWriter().write(entries, to: destination)
        // Counted from what was WRITTEN, not from the log: a trimmed bundle reporting the whole
        // log's line count would overstate what it contains on the one screen that says so.
        let readingCount = try await log.rawLines(within: request.window).count

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

    /// The digest and size of whatever a zip entry is carrying, from disk or from memory.
    ///
    /// Taken off the bytes that are actually going in, which for a photograph is the stripped
    /// copy rather than the original. A seal computed from the originals would fail on every
    /// bundle the moment anybody checked it.
    static func digestAndSize(of entry: ZipWriter.Entry) throws -> (String, Int64) {
        switch entry.source {
        case .data(let data):
            let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            return (digest, Int64(data.count))
        case .file(let url):
            let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size]
                        as? NSNumber)??.int64Value ?? 0
            return (try sha256(of: url), size)
        }
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

    /// Moves every `start_offset_seconds` naming this file back by `seconds` (item 210).
    ///
    /// **Why a trim needs this at all.** A reading's `start_offset_seconds` says how far INTO the
    /// recording that moment sits, and the player reconstructs where the recording begins by
    /// subtracting it from the reading's time. Cut sixty minutes down to ten and every one of
    /// those offsets is still measured from a beginning that is no longer in the file — so the
    /// player would place the audio an hour away from the readings it belongs to, and the one
    /// thing the whole feature is for, hearing what happened at the spike, would be broken.
    ///
    /// **A targeted rewrite rather than a re-encode.** Re-serialising the document would reorder
    /// keys and reformat numbers across every reading, and those reading lines are the bytes the
    /// device wrote. This walks to each `audio_ref` naming the file and edits only its offset,
    /// leaving everything else exactly as it was.
    ///
    /// An offset that would go negative is clamped to zero: the reading sits at or before the
    /// start of what was kept, which is where the cut file now begins.
    public static func rebaseAudioOffsets(forFilename path: String, by seconds: TimeInterval,
                                   in document: Data) -> Data {
        guard seconds > 0, let text = String(data: document, encoding: .utf8) else { return document }

        // Both spellings, because a log line written by an older build escapes its slashes and is
        // spliced in verbatim.
        let markers = [path, path.replacingOccurrences(of: "/", with: "\\/")]
            .map { "\"filename\":\"\($0)\"" }

        // Built forward into a new string rather than edited in place: an in-place edit
        // invalidates every index after it, and the bookkeeping to recover from that is where
        // this kind of loop stops terminating.
        var out = ""
        var cursor = text.startIndex

        while cursor < text.endIndex {
            let next = markers
                .compactMap { text.range(of: $0, range: cursor..<text.endIndex) }
                .min(by: { $0.lowerBound < $1.lowerBound })

            guard let found = next else { break }

            // The offset lives in the same small object as the filename, so the search stops at
            // that object's closing brace and can never run on into the next reading.
            guard let objectEnd = text[found.upperBound...].firstIndex(of: "}"),
                  let key = text.range(of: "\"start_offset_seconds\":",
                                       range: found.upperBound..<objectEnd)
            else {
                out += text[cursor..<found.upperBound]
                cursor = found.upperBound
                continue
            }

            let numberStart = key.upperBound
            let numberEnd = text[numberStart..<objectEnd]
                .firstIndex(where: { $0 == "," || $0 == "}" }) ?? objectEnd
            let current = Double(text[numberStart..<numberEnd].trimmingCharacters(in: .whitespaces))

            out += text[cursor..<numberStart]
            if let current {
                // Clamped at zero: a reading at or before the start of what was kept sits exactly
                // where the cut file now begins.
                out += Self.number(max(0, current - seconds))
            } else {
                out += text[numberStart..<numberEnd]
            }
            cursor = numberEnd
        }

        out += text[cursor...]
        return out.data(using: .utf8) ?? document
    }

    /// A JSON number: whole values without a pointless ".0".
    public static func number(_ value: Double) -> String {
        value == value.rounded() && abs(value) < 1e15
            ? String(Int64(value))
            : String(value)
    }

    /// Every media path the document refers to — from `audio_ref` names and from the capture
    /// notes that carry photo and video paths.
    /// The comparison scans a document names — `scans/compare-001.scan`, from a `scan` reading's
    /// value or a change's picture (`…scan#changes/01.jpg`).
    static func comparisonPathsNamed(in document: Data) -> Set<String> {
        let text = String(decoding: document, as: UTF8.self)
        var found: Set<String> = []
        var search = text.startIndex..<text.endIndex
        while let start = text.range(of: "\"scans/compare-", range: search) {
            let after = text.index(after: start.lowerBound)
            guard let end = text[after...].firstIndex(where: { $0 == "\"" || $0 == "#" }) else { break }
            let path = String(text[after..<end])
            if path.hasSuffix(".scan"), !path.contains(".."), !path.contains("\\") { found.insert(path) }
            search = end..<text.endIndex
        }
        return found
    }

    /// A trimmed video's person map, moved to the trimmed video's clock (LiDAR plan, phase 4).
    ///
    /// A figure or shape says when it was seen in seconds into its video, and the joints file says
    /// the same for every frame. Cut the first `from` seconds off the video and both are that far
    /// out, so the figure would be drawn beside the person. The document's `from` and `to` are
    /// rewritten in place — only those numbers, as `rebaseAudioOffsets` rewrites only its offsets —
    /// and each joints file is copied into `scratch` with its times moved and anything outside
    /// the kept stretch left out. The copies stand in for the session's own under the same paths.
    public static func rebasePersonMap(forVideo path: String, from: TimeInterval, duration: TimeInterval,
                                       in document: Data, files: SessionFileStore, sessionId: UUID,
                                       into scratch: URL) -> (document: Data, substitutes: [String: URL]) {
        guard var text = String(data: document, encoding: .utf8) else { return (document, [:]) }
        var substitutes: [String: URL] = [:]
        func moved(_ t: Double) -> Double { (min(max(t - from, 0), duration) * 1000).rounded() / 1000 }

        for spelling in [path, path.replacingOccurrences(of: "/", with: "\\/")] {
            var search = text.startIndex..<text.endIndex
            while let found = text.range(of: "\"video\":\"\(spelling)\"", range: search) {
                // A figure or shape is one object with no objects inside it, so its braces are the
                // nearest either side.
                guard let open = text[..<found.lowerBound].lastIndex(of: "{"),
                      let close = text[found.upperBound...].firstIndex(of: "}") else { break }
                var object = String(text[open...close])
                for key in ["from", "to"] {
                    guard let keyRange = object.range(of: "\"\(key)\":") else { continue }
                    let numberEnd = object[keyRange.upperBound...].firstIndex { $0 == "," || $0 == "}" } ?? object.endIndex
                    guard let value = Double(object[keyRange.upperBound..<numberEnd].trimmingCharacters(in: .whitespaces))
                    else { continue }
                    object.replaceSubrange(keyRange.upperBound..<numberEnd, with: number(moved(value)))
                }
                // The joints file this figure names, its times moved the same way.
                if let valueRange = object.range(of: "\"value\":\""),
                   let end = object[valueRange.upperBound...].firstIndex(of: "\"") {
                    let joints = String(object[valueRange.upperBound..<end]).replacingOccurrences(of: "\\/", with: "/")
                    if joints.hasPrefix("figures/"), joints.hasSuffix(".json"), substitutes[joints] == nil,
                       let data = try? Data(contentsOf: files.fileURL(for: sessionId, relativePath: joints)),
                       let entries = try? FigureSidecar.read(data) {
                        try? FileManager.default.createDirectory(at: scratch, withIntermediateDirectories: true)
                        let copy = scratch.appendingPathComponent("moved-\(UUID().uuidString.lowercased()).json")
                        if let writer = try? FigureSidecar.Writer(url: copy) {
                            for entry in entries where entry.t >= from - 0.001 && entry.t <= from + duration + 0.001 {
                                writer.append(FigureSidecar.Entry(t: moved(entry.t), joints: entry.joints))
                            }
                            writer.finish()
                            substitutes[joints] = copy
                        }
                    }
                }
                text.replaceSubrange(open...close, with: object)
                let resume = text.index(open, offsetBy: object.count)
                search = resume..<text.endIndex
            }
        }
        return (text.data(using: .utf8) ?? document, substitutes)
    }

    /// The log's lines in time order, as the format requires and the server checks.
    ///
    /// The log is in the order things were written, which until the person map was always time
    /// order. Its readings are written when their video is filed but dated to when the figure was
    /// first in view (LiDAR plan, phase 4), so they arrive late. A stable sort of whole lines:
    /// each stays the bytes the device wrote, and a log already in order comes back as it was.
    static func inTimeOrder(_ lines: [Data]) -> [Data] {
        var times: [Date] = []
        times.reserveCapacity(lines.count)
        var last = Date.distantPast
        for line in lines {
            // A line with no time keeps its place behind the one before it.
            if let stamp = try? DeviceDataJSON.decoder.decode(LineTime.self, from: line) { last = stamp.at }
            times.append(last)
        }
        guard zip(times, times.dropFirst()).contains(where: { $0 > $1 }) else { return lines }
        return lines.indices
            .sorted { times[$0] != times[$1] ? times[$0] < times[$1] : $0 < $1 }
            .map { lines[$0] }
    }

    private struct LineTime: Decodable {
        let at: Date
    }

    /// The person map's files a document names — `figures/figure-001.json` and its picture,
    /// `figures/shape-001.jpg` — for the videos going in with them. A figure drawn over a video
    /// that was left out has nothing to be drawn over.
    static func figurePathsNamed(in document: Data, withVideos videos: Set<String>) -> Set<String> {
        // A night's document is large, and most have no person map: read it only when it names one.
        guard !videos.isEmpty,
              document.range(of: Data("\"figures/".utf8)) != nil || document.range(of: Data("\"figures\\/".utf8)) != nil,
              let root = try? JSONSerialization.jsonObject(with: document) as? [String: Any],
              let readings = root["readings"] as? [[String: Any]] else { return [] }
        var found: Set<String> = []
        for reading in readings {
            guard let measurements = reading["measurements"] as? [String: Any] else { continue }
            for channel in ["figure", "shape"] {
                guard let measurement = measurements[channel] as? [String: Any],
                      let video = measurement["video"] as? String, videos.contains(video) else { continue }
                var paths = [measurement["picture"] as? String]
                if channel == "figure" { paths.append(measurement["value"] as? String) }
                for case let path? in paths
                where path.hasPrefix("figures/") && !path.contains("..") && !path.contains("\\") {
                    found.insert(path)
                }
            }
        }
        return found
    }

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
