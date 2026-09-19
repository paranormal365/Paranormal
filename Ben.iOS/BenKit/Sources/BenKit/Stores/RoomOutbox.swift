import Foundation
import UniformTypeIdentifiers
#if canImport(ImageIO)
import ImageIO
#endif

/// Posts for an event's room that are waiting to be sent (item 235 phase 14d).
///
/// **A photo taken in a cellar is sent when it can be.** A room post that cannot reach the server is kept here with
/// its photo, its words, the "also send to the organizers" choice and the agreement to the photo notice, and sent
/// later — when the app comes back to the front, when the network comes back, or when the room is opened.
///
/// **Shared with the Share Extension.** Photos shared to an event from the Photos app land here too. The extension
/// only ever adds; the app is what sends, so the extension needs no sign-in of its own. The folder lives in the App
/// Group container, and every read and write of the index goes through a file coordinator so the two processes
/// never overwrite each other.
///
/// **The photo is moved or copied in once and deleted once sent**, never held in memory: a 60 MB video in a queue
/// must not live on the heap.
public struct RoomOutbox: Sendable {
    /// The largest file sent in one post. The site sits behind a proxy that refuses request bodies over 100 MB.
    public static let largestFile: Int64 = 95 * 1_048_576

    private let directory: URL

    public init(directory: URL) { self.directory = directory }

    /// Which outbox on disk this is, so two senders of the same one take turns.
    var key: String { directory.standardizedFileURL.path }

    /// The shared App Group folder, or the app's own Application Support when there is no group (tests, previews).
    public static func shared() -> RoomOutbox { RoomOutbox(directory: SharedContainer.url().appendingPathComponent("RoomOutbox", isDirectory: true)) }

    public enum AddError: Error, Equatable, Sendable {
        case tooBig(String)
        case unreadable(String)

        public var sentence: String {
            switch self {
            case .tooBig(let s), .unreadable(let s): s
            }
        }
    }

    /// Keeps a post. A file is **moved** in when `moveFile` (the app's own scratch copy), **copied** otherwise (a file
    /// another process owns). A photo that isn't JPEG is converted first: the server decodes JPEG, and an iPhone's
    /// HEIC may not be readable there.
    @discardableResult
    public func add(hostedEventId: UUID, eventName: String, body: String, file: URL?, contentType: String?, originalName: String?,
                    sendToHosts: Bool, agreeToShow: Bool, moveFile: Bool, at now: Date = Date()) throws -> QueuedRoomPost {
        let id = UUID()
        var storedName: String?
        var storedType: String?
        var size: Int64 = 0

        if let file {
            try FileManager.default.createDirectory(at: filesFolder, withIntermediateDirectories: true)
            let type = contentType ?? Self.contentType(of: file)
            if type.hasPrefix("image/") && type != "image/jpeg" {
                let destination = filesFolder.appendingPathComponent("\(id.uuidString).jpg")
                guard Self.writeJPEG(from: file, to: destination) else {
                    throw AddError.unreadable("That photo couldn't be read. Try sharing it again, or add it from inside the app.")
                }
                if moveFile { try? FileManager.default.removeItem(at: file) }
                storedName = destination.lastPathComponent
                storedType = "image/jpeg"
            } else {
                let ext = file.pathExtension.isEmpty ? (type.hasPrefix("video/") ? "mov" : "bin") : file.pathExtension.lowercased()
                let destination = filesFolder.appendingPathComponent("\(id.uuidString).\(ext)")
                let bytes = (try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int64) ?? 0
                if bytes > Self.largestFile {
                    throw AddError.tooBig("That video is \(bytes / 1_048_576) MB. The most one post can carry is 95 MB — trim it, or share a shorter clip.")
                }
                if moveFile { try FileManager.default.moveItem(at: file, to: destination) } else { try FileManager.default.copyItem(at: file, to: destination) }
                storedName = destination.lastPathComponent
                storedType = type
            }
            size = (try? FileManager.default.attributesOfItem(atPath: filesFolder.appendingPathComponent(storedName!).path)[.size] as? Int64) ?? 0
        }

        let post = QueuedRoomPost(id: id, hostedEventId: hostedEventId, eventName: eventName, body: body, storedFileName: storedName,
                                  originalFileName: originalName, contentType: storedType, byteCount: size,
                                  sendToHosts: sendToHosts, agreeToShow: agreeToShow, createdUtc: now, refusal: nil)
        try update { $0.append(post) }
        return post
    }

    /// Everything kept, oldest first — waiting and refused alike.
    public func all() -> [QueuedRoomPost] { (try? read()) ?? [] }

    public func waiting(for eventId: UUID? = nil) -> [QueuedRoomPost] {
        all().filter { $0.refusal == nil && (eventId == nil || $0.hostedEventId == eventId) }
    }

    public func refused(for eventId: UUID? = nil) -> [QueuedRoomPost] {
        all().filter { $0.refusal != nil && (eventId == nil || $0.hostedEventId == eventId) }
    }

    /// The kept photo, as the multipart upload wants it.
    public func media(for post: QueuedRoomPost) -> MediaUpload? {
        guard let stored = post.storedFileName, let type = post.contentType else { return nil }
        return MediaUpload(fileURL: filesFolder.appendingPathComponent(stored),
                           filename: post.originalFileName.map(Self.jpegName(for: type)) ?? stored,
                           contentType: type, byteCount: post.byteCount)
    }

    /// Takes a post out, and its photo with it.
    public func remove(_ id: UUID) {
        let removed = try? update { posts in
            let gone = posts.filter { $0.id == id }
            posts.removeAll { $0.id == id }
            return gone
        }
        for post in removed ?? [] {
            if let stored = post.storedFileName { try? FileManager.default.removeItem(at: filesFolder.appendingPathComponent(stored)) }
        }
    }

    /// The server would not take it: kept, with its sentence, until somebody removes it.
    public func markRefused(_ id: UUID, because sentence: String) {
        _ = try? update { posts in
            if let index = posts.firstIndex(where: { $0.id == id }) { posts[index].refusal = sentence }
        }
    }

    /// Everything goes — on sign-out, because the photos waiting are the person's who took them.
    public func removeAll() { try? FileManager.default.removeItem(at: directory) }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private var index: URL { directory.appendingPathComponent("posts.json") }
    private var filesFolder: URL { directory.appendingPathComponent("files", isDirectory: true) }

    private func read() throws -> [QueuedRoomPost] {
        var result: [QueuedRoomPost] = []
        var coordinationError: NSError?
        NSFileCoordinator().coordinate(readingItemAt: index, options: [], error: &coordinationError) { url in
            if let data = try? Data(contentsOf: url) { result = (try? Self.decoder.decode([QueuedRoomPost].self, from: data)) ?? [] }
        }
        if let coordinationError { throw coordinationError }
        return result
    }

    @discardableResult
    private func update<T>(_ change: (inout [QueuedRoomPost]) -> T) throws -> T {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        var answer: T?
        var failure: Error?
        var coordinationError: NSError?
        NSFileCoordinator().coordinate(writingItemAt: index, options: .forMerging, error: &coordinationError) { url in
            var posts = (try? Data(contentsOf: url)).flatMap { try? Self.decoder.decode([QueuedRoomPost].self, from: $0) } ?? []
            answer = change(&posts)
            do {
                let data = try Self.encoder.encode(posts)
                #if os(iOS)
                try data.write(to: url, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
                #else
                try data.write(to: url, options: .atomic)
                #endif
            } catch { failure = error }
        }
        if let coordinationError { throw coordinationError }
        if let failure { throw failure }
        return answer!
    }

    static func contentType(of file: URL) -> String {
        UTType(filenameExtension: file.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
    }

    /// "IMG_0042.HEIC" sent as JPEG is named "IMG_0042.jpg".
    private static func jpegName(for type: String) -> (String) -> String {
        { original in
            guard type == "image/jpeg" else { return original }
            let stem = (original as NSString).deletingPathExtension
            return (stem.isEmpty ? "photo" : stem) + ".jpg"
        }
    }

    /// Re-encodes any image ImageIO can read as a JPEG, streaming from file to file.
    public static func writeJPEG(from source: URL, to destination: URL) -> Bool {
        #if canImport(ImageIO)
        guard let input = CGImageSourceCreateWithURL(source as CFURL, nil),
              let output = CGImageDestinationCreateWithURL(destination as CFURL, UTType.jpeg.identifier as CFString, 1, nil)
        else { return false }
        // Keep the orientation; drop the location a phone photo carries — the server strips it too, but it need not travel.
        let options: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: 0.85, kCGImagePropertyGPSDictionary: kCFNull as Any]
        CGImageDestinationAddImageFromSource(output, input, 0, options as CFDictionary)
        return CGImageDestinationFinalize(output)
        #else
        return false
        #endif
    }

    private static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()

    private static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }()
}

/// One post waiting for a signal.
public struct QueuedRoomPost: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var hostedEventId: UUID
    public var eventName: String
    public var body: String
    /// The photo or video in the outbox's own folder; nil for words only.
    public var storedFileName: String?
    public var originalFileName: String?
    public var contentType: String?
    public var byteCount: Int64
    public var sendToHosts: Bool
    public var agreeToShow: Bool
    public var createdUtc: Date
    /// Why the server would not take it, in its own words. Nil while it is still waiting.
    public var refusal: String?

    public var isVideo: Bool { contentType?.hasPrefix("video/") == true }
}

/// The App Group container both the app and its Share Extension can reach.
public enum SharedContainer {
    public static let appGroup = "group.com.ishaunted.ios"

    public static func url() -> URL {
        if let group = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup) { return group }
        let base = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true))
            ?? FileManager.default.temporaryDirectory
        return base.appendingPathComponent("Shared", isDirectory: true)
    }
}

/// The last read of each event's room, kept so the room still opens with no signal and a photo can be added to the
/// outbox from it (phase 14d). Its rules — whether photos are taken, the notice to agree to, who "send to the hosts"
/// reaches — are the ones the server last gave; the server checks them again when the post is sent.
public struct RoomCache: Sendable {
    public struct Saved: Codable, Sendable {
        public var room: EventRoom
        public var savedAt: Date
    }

    private let directory: URL

    public init(directory: URL) { self.directory = directory }

    public static func applicationSupport() -> RoomCache {
        let base = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true))
            ?? FileManager.default.temporaryDirectory
        return RoomCache(directory: base.appendingPathComponent("EventRooms", isDirectory: true))
    }

    public func save(_ room: EventRoom, for eventId: UUID, at now: Date = Date()) {
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.dateEncodingStrategy = .custom { date, encoder in
                var container = encoder.singleValueContainer()
                try container.encode(date.formatted(Date.ISO8601FormatStyle(includingFractionalSeconds: true)))
            }
            let data = try encoder.encode(Saved(room: room, savedAt: now))
            #if os(iOS)
            try data.write(to: file(eventId), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            #else
            try data.write(to: file(eventId), options: .atomic)
            #endif
        } catch {}
    }

    public func load(_ eventId: UUID) -> Saved? {
        guard let data = try? Data(contentsOf: file(eventId)) else { return nil }
        return try? BenJSON.decoder.decode(Saved.self, from: data)
    }

    public func remove(_ eventId: UUID) { try? FileManager.default.removeItem(at: file(eventId)) }
    public func removeAll() { try? FileManager.default.removeItem(at: directory) }

    private func file(_ eventId: UUID) -> URL { directory.appendingPathComponent("\(eventId.uuidString.lowercased()).json") }
}
