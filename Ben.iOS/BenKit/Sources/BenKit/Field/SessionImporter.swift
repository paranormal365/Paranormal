import Foundation
import AVFoundation
import CryptoKit

/// Opens a `.ben` and turns it back into the pieces a session on this device is made of.
///
/// Ben, 2026-09-16: "someone else can share their .ben file with another person on the iphone and
/// the other person can view it like they had recorded it themselves." So this does not invent a
/// second kind of session. It rebuilds the SAME three things a night recorded here leaves behind —
/// the readings log, the rows for markers and captures, and the media files — from what the bundle
/// carries, and the review screen, the replay, the map and the trimmer never learn the difference.
///
/// Two halves, kept apart so the second can be tested without a file:
///  - `open` reads the archive, its document and its seal, and checks the seal against the
///    members;
///  - `rebuild` reads the markers, captures and base levels back out of the readings, which is
///    where the phone wrote them (`FieldSessionEngine.record` and `noteCapture`).
public enum SessionImporter {

    /// Everything read from a bundle before anything is written to the device.
    public struct Opened: Sendable {
        public var reader: ZipReader
        public var envelope: DeviceDataEnvelope
        /// The document's own bytes, kept so the seal can be checked against exactly what was read.
        public var document: Data
        /// Present and well-formed, or absent. Whether it MATCHES is `verify`'s job, once the
        /// members are on disk — checking it here would mean reading every recording twice.
        public var seal: SessionSeal?
        /// The session's own id: the seal's when there is one, so the same night opened twice is
        /// the same session; a fresh one for an unsealed bundle, which no build of this app writes.
        public var sessionId: UUID
        /// The members under `media/`, which is where every recording lives.
        public var media: [ZipReader.Entry]

        public var title: String {
            envelope.session.locationLabel?.nilIfEmpty ?? "Field session"
        }
    }

    /// The facts about one capture, as the readings tell them.
    public struct CaptureFacts: Sendable, Equatable {
        public var kind: CaptureKind
        public var relativePath: String
        /// When it began. A photo is its own instant; a recording's beginning is worked back
        /// from the reading that noted its end and the length it carried.
        public var at: Date
        public var durationSeconds: Double?
        public var latitude: Double?
        public var longitude: Double?
        public var headingDegrees: Double?
        public var room: String?
    }

    /// What the readings said happened, other than the readings themselves.
    public struct Rebuilt: Sendable {
        public var markers: [FieldMarkerRecord]
        public var captures: [CaptureFacts]
        public var baselines: Baselines
    }

    /// The name the bundle's document must carry.
    public static let documentPath = "data.json"

    // MARK: - Opening

    /// Reads the archive, the document and the seal. Throws, in a sentence, for anything that is
    /// not a session bundle this app can trust.
    public static func open(_ url: URL) throws -> Opened {
        let reader: ZipReader
        do { reader = try ZipReader(url: url) }
        catch let error as ZipReaderError {
            throw FieldSessionError.notASessionBundle(error.errorDescription ?? "it could not be read.")
        }

        guard let documentEntry = reader.entry(documentPath) else {
            throw FieldSessionError.notASessionBundle("there is no data.json inside it.")
        }
        let document = try reader.data(of: documentEntry)
        let envelope: DeviceDataEnvelope
        do { envelope = try DeviceDataJSON.decoder.decode(DeviceDataEnvelope.self, from: document) }
        catch {
            throw FieldSessionError.notASessionBundle("its data.json isn't a session document.")
        }

        // The seal, when there is one, is read and checked for shape here. Names and sizes are
        // compared now — cheap, and enough to catch a member added or removed. The digests wait
        // for `verify`, after the members have been copied out, so nothing is read twice.
        var seal: SessionSeal?
        if let sealEntry = reader.entry(SessionSeal.entryPath) {
            let parsed: SessionSeal
            do { parsed = try DeviceDataJSON.decoder.decode(SessionSeal.self, from: try reader.data(of: sealEntry)) }
            catch { throw FieldSessionError.notASessionBundle("its seal can't be read.") }
            guard parsed.version == 1 else {
                throw FieldSessionError.notASessionBundle("its seal is a newer kind than this app knows.")
            }
            let members = reader.entries.filter { $0.path != SessionSeal.entryPath }
            guard Set(parsed.entries.map(\.path)) == Set(members.map(\.path)) else {
                throw FieldSessionError.bundleTampered
            }
            for member in members {
                guard parsed.entries.first(where: { $0.path == member.path })?.byteCount == member.byteCount else {
                    throw FieldSessionError.bundleTampered
                }
            }
            seal = parsed
        }

        let media = reader.entries.filter { $0.path.hasPrefix("media/") }
        return Opened(reader: reader, envelope: envelope, document: document, seal: seal,
                      sessionId: seal?.sessionId ?? UUID(), media: media)
    }

    /// Checks the seal's digests against the document and the members now on disk.
    ///
    /// A bundle that lost bytes on the way — or had them changed — is refused rather than opened
    /// with a hole in it. Nothing to check is nothing to refuse: an unsealed bundle passes, and
    /// what it is worth is then whatever its sender is worth.
    public static func verify(_ opened: Opened, extracted: [String: URL]) throws {
        guard let seal = opened.seal else { return }
        var digested: [SessionSeal.Entry] = []
        for entry in seal.entries {
            if entry.path == documentPath {
                let digest = SHA256.hash(data: opened.document).map { String(format: "%02x", $0) }.joined()
                digested.append(SessionSeal.Entry(path: entry.path, sha256: digest,
                                                  byteCount: Int64(opened.document.count)))
            } else if let url = extracted[entry.path] {
                let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size]
                            as? NSNumber)??.int64Value ?? 0
                digested.append(SessionSeal.Entry(path: entry.path,
                                                  sha256: try DeviceDataExporter.sha256(of: url),
                                                  byteCount: size))
            } else {
                throw FieldSessionError.bundleTampered
            }
        }
        guard seal.matches(digested) else { throw FieldSessionError.bundleTampered }
    }

    // MARK: - Reading the night back out of its readings

    /// Markers, captures and base levels, from the readings that recorded them.
    ///
    /// The phone writes a marker as a reading whose `marker` measurement names a `MarkerKind`,
    /// and a capture as one whose `marker` names a `CaptureKind` with the path in the note —
    /// see `FieldSessionEngine`. Base levels ride on every `emf` and `sound_level` measurement
    /// as `baseline`, so the last one written is what the session was measured against.
    public static func rebuild(from readings: [FieldReading]) -> Rebuilt {
        var markers: [FieldMarkerRecord] = []
        var captures: [CaptureFacts] = []
        var emfBaseline: Double?
        var soundBaseline: Double?

        for reading in readings {
            let measurements = reading.measurements ?? [:]
            if let baseline = measurements["emf"]?.baseline { emfBaseline = baseline }
            if let baseline = measurements["sound_level"]?.baseline { soundBaseline = baseline }

            guard case .string(let label)? = measurements["marker"]?.value else { continue }
            let room: String? = {
                if case .string(let name)? = measurements["room"]?.value { return name }
                return nil
            }()

            if let kind = MarkerKind(rawValue: label) {
                markers.append(FieldMarkerRecord(
                    at: reading.at, kind: kind, note: reading.note, room: room,
                    magneticMicrotesla: number(measurements["emf"]),
                    soundDbfs: number(measurements["sound_level"]),
                    latitude: reading.position?.latitude,
                    longitude: reading.position?.longitude,
                    audioFilename: reading.audioRef?.filename,
                    audioOffsetSeconds: reading.audioRef?.startOffsetSeconds))
                continue
            }

            if let kind = CaptureKind(rawValue: label) {
                // "photo: media/photo-001.jpg" — the note is where v1 carries a path the format
                // has no field for. Audio also names its file in audio_ref, which wins when both.
                let noted = reading.note.flatMap { note -> String? in
                    let prefix = "\(kind.rawValue): "
                    return note.hasPrefix(prefix) ? String(note.dropFirst(prefix.count)) : nil
                }
                guard let path = reading.audioRef?.filename ?? noted, path.hasPrefix("media/") else { continue }
                let duration = reading.audioRef?.durationSeconds
                // The note was written when the recording ENDED; a recording begins its length
                // earlier. A photo is an instant either way.
                let began = kind == .photo ? reading.at
                    : reading.at.addingTimeInterval(-(duration ?? 0))
                captures.append(CaptureFacts(
                    kind: kind, relativePath: path, at: began, durationSeconds: duration,
                    latitude: reading.position?.latitude, longitude: reading.position?.longitude,
                    headingDegrees: reading.motion?.headingDegrees, room: room))
            }
        }

        return Rebuilt(markers: markers, captures: captures,
                       baselines: Baselines(magneticMicrotesla: emfBaseline, soundDbfs: soundBaseline))
    }

    /// How long a recording runs, read from the file — for a clip whose reading carried no length.
    public static func duration(of url: URL) async -> Double? {
        guard let seconds = try? await AVURLAsset(url: url).load(.duration).seconds,
              seconds.isFinite, seconds > 0 else { return nil }
        return seconds
    }

    // MARK: - Helpers

    private static func number(_ measurement: FieldReading.Measurement?) -> Double? {
        if case .number(let value)? = measurement?.value { return value }
        return nil
    }

}
