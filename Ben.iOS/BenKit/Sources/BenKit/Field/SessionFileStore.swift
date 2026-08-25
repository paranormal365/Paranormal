import Foundation

/// Where a session's files live on the device.
///
/// **Application Support, not tmp or Caches.** The rest of this app stages uploads in
/// `temporaryDirectory` and deletes them on success, which is right for a file that exists only
/// long enough to be posted. A field session is the opposite: it is evidence, it may sit on the
/// phone for a week before anyone reviews it, and the system may purge Caches and tmp whenever
/// it likes. Losing a night's recordings to a disk-space sweep would be unforgivable.
///
/// ```
/// Application Support/FieldSessions/<sessionId>/
///   readings.jsonl
///   media/
///     audio-001.m4a
///     photo-001.jpg
/// ```
public struct SessionFileStore: Sendable {

    private let root: URL

    /// The real location. Marked as excluded from backup? No — deliberately backed up: a person
    /// who restores a phone should get their sessions back.
    public init(root: URL) {
        self.root = root
    }

    public static func applicationSupport() throws -> SessionFileStore {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory, in: .userDomainMask,
            appropriateFor: nil, create: true)
        return SessionFileStore(root: base.appendingPathComponent("FieldSessions", isDirectory: true))
    }

    public var rootDirectory: URL { root }

    public func directory(for sessionId: UUID) -> URL {
        root.appendingPathComponent(sessionId.uuidString.lowercased(), isDirectory: true)
    }

    public func mediaDirectory(for sessionId: UUID) -> URL {
        directory(for: sessionId).appendingPathComponent("media", isDirectory: true)
    }

    public func readingLogURL(for sessionId: UUID) -> URL {
        directory(for: sessionId).appendingPathComponent("readings.jsonl")
    }

    @discardableResult
    public func createDirectories(for sessionId: UUID) throws -> URL {
        let directory = directory(for: sessionId)
        try FileManager.default.createDirectory(
            at: mediaDirectory(for: sessionId), withIntermediateDirectories: true)
        return directory
    }

    /// The next free media name of a kind — `media/photo-003.jpg`, relative to the session
    /// directory because that is the form the export bundle needs.
    public func nextMediaPath(for sessionId: UUID, kind: CaptureKind,
                              fileExtension: String) throws -> (relative: String, url: URL) {
        try createDirectories(for: sessionId)
        let directory = mediaDirectory(for: sessionId)
        let existing = (try? FileManager.default.contentsOfDirectory(atPath: directory.path)) ?? []

        var index = 1
        var name = ""
        repeat {
            name = String(format: "%@-%03d.%@", kind.rawValue, index, fileExtension)
            index += 1
        } while existing.contains(name)

        return ("media/\(name)", directory.appendingPathComponent(name))
    }

    /// Moves a captured file in from wherever the system handed it to us. A move, never a copy:
    /// a 200 MB video should not exist twice on a phone that is recording all night.
    public func adopt(_ source: URL, for sessionId: UUID, kind: CaptureKind)
        throws -> (relativePath: String, url: URL, byteCount: Int64) {
        let ext = source.pathExtension.isEmpty ? defaultExtension(for: kind) : source.pathExtension
        let (relative, destination) = try nextMediaPath(for: sessionId, kind: kind, fileExtension: ext)

        if FileManager.default.fileExists(atPath: destination.path) {
            try FileManager.default.removeItem(at: destination)
        }
        try FileManager.default.moveItem(at: source, to: destination)

        let attributes = try? FileManager.default.attributesOfItem(atPath: destination.path)
        let size = (attributes?[.size] as? NSNumber)?.int64Value ?? 0
        return (relative, destination, size)
    }

    public func fileURL(for sessionId: UUID, relativePath: String) -> URL {
        directory(for: sessionId).appendingPathComponent(relativePath)
    }

    /// Deletes a session's whole directory. Called only when the person deletes the session.
    public func delete(sessionId: UUID) throws {
        let directory = directory(for: sessionId)
        guard FileManager.default.fileExists(atPath: directory.path) else { return }
        try FileManager.default.removeItem(at: directory)
    }

    /// Session directories on disk — used at launch to spot anything the database lost track of.
    public func existingSessionIds() -> Set<UUID> {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: root.path)) ?? []
        return Set(names.compactMap(UUID.init(uuidString:)))
    }

    private func defaultExtension(for kind: CaptureKind) -> String {
        switch kind {
        case .photo: "jpg"
        case .video: "mov"
        case .audio: "m4a"
        }
    }
}
