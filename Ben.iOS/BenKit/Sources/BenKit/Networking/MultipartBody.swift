import Foundation

/// Builds a `multipart/form-data` body. Field names are PASCALCASE (`Body`,
/// `ParentMessageId`, file part `media`) because ASP.NET's form binding matches
/// the C# parameter names — unlike JSON responses, which are camelCase.
public struct MultipartBody: Sendable {
    public struct Part: Sendable {
        public var name: String
        public var filename: String?
        public var contentType: String?
        public var payload: Payload

        public enum Payload: Sendable {
            case data(Data)
            case file(URL)
        }

        public static func field(_ name: String, _ value: String) -> Part {
            Part(name: name, filename: nil, contentType: nil, payload: .data(Data(value.utf8)))
        }

        public static func file(_ name: String, filename: String, contentType: String, data: Data) -> Part {
            Part(name: name, filename: filename, contentType: contentType, payload: .data(data))
        }

        public static func file(_ name: String, filename: String, contentType: String, url: URL) -> Part {
            Part(name: name, filename: filename, contentType: contentType, payload: .file(url))
        }
    }

    public var parts: [Part]
    public let boundary: String

    public init(parts: [Part], boundary: String = "BenKit-\(UUID().uuidString)") {
        self.parts = parts
        self.boundary = boundary
    }

    public var contentTypeHeader: String { "multipart/form-data; boundary=\(boundary)" }

    /// Composes the body. Large file parts are streamed from disk into the
    /// output rather than loaded whole; the composed body itself is written to
    /// `destination` so uploads can use `upload(fromFile:)` and keep memory flat.
    public func write(to destination: URL) throws {
        FileManager.default.createFile(atPath: destination.path, contents: nil)
        let handle = try FileHandle(forWritingTo: destination)
        defer { try? handle.close() }

        for part in parts {
            var header = "--\(boundary)\r\n"
            header += "Content-Disposition: form-data; name=\"\(part.name)\""
            if let filename = part.filename { header += "; filename=\"\(filename)\"" }
            header += "\r\n"
            if let contentType = part.contentType { header += "Content-Type: \(contentType)\r\n" }
            header += "\r\n"
            try handle.write(contentsOf: Data(header.utf8))

            switch part.payload {
            case .data(let data):
                try handle.write(contentsOf: data)
            case .file(let url):
                let input = try FileHandle(forReadingFrom: url)
                defer { try? input.close() }
                while let chunk = try input.read(upToCount: 1 << 20), !chunk.isEmpty {
                    try handle.write(contentsOf: chunk)
                }
            }
            try handle.write(contentsOf: Data("\r\n".utf8))
        }
        try handle.write(contentsOf: Data("--\(boundary)--\r\n".utf8))
    }

    /// In-memory compose — for tests and small bodies.
    public func composedData() throws -> Data {
        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("multipart-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: scratch) }
        try write(to: scratch)
        return try Data(contentsOf: scratch)
    }
}
