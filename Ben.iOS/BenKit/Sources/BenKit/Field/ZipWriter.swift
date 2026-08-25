import Foundation

/// A minimal ZIP writer — stored entries only, no compression.
///
/// Written by hand rather than adding a dependency: the app has none, and a format this old and
/// this well specified does not justify starting. Stored rather than deflated on purpose too —
/// the payload is JPEG, H.264 and AAC, all of which are already compressed, so deflating them
/// would spend a phone's battery to make the file very slightly larger.
///
/// Entries stream from disk in chunks. A session with an hour of video must never have to fit
/// in memory to be exported.
public struct ZipWriter {

    public struct Entry: Sendable {
        public var path: String
        public var source: Source

        public enum Source: Sendable {
            case data(Data)
            case file(URL)
        }

        public init(path: String, data: Data) {
            self.path = path
            self.source = .data(data)
        }

        public init(path: String, file: URL) {
            self.path = path
            self.source = .file(file)
        }
    }

    public init() {}

    /// Writes the archive, returning the number of bytes.
    @discardableResult
    public func write(_ entries: [Entry], to destination: URL) throws -> Int64 {
        FileManager.default.createFile(atPath: destination.path, contents: nil)
        let handle = try FileHandle(forWritingTo: destination)
        defer { try? handle.close() }

        var directory = Data()
        var offset: UInt32 = 0
        var count: UInt16 = 0

        for entry in entries {
            let nameBytes = Array(entry.path.utf8)
            let (crc, size) = try checksum(entry.source)

            var local = Data()
            local.append(littleEndian: UInt32(0x0403_4B50))   // local file header
            local.append(littleEndian: UInt16(20))            // version needed
            local.append(littleEndian: UInt16(0))             // flags
            local.append(littleEndian: UInt16(0))             // stored
            local.append(littleEndian: UInt16(0))             // time
            local.append(littleEndian: UInt16(0))             // date
            local.append(littleEndian: crc)
            local.append(littleEndian: UInt32(size))
            local.append(littleEndian: UInt32(size))
            local.append(littleEndian: UInt16(nameBytes.count))
            local.append(littleEndian: UInt16(0))             // extra field length
            local.append(contentsOf: nameBytes)
            try handle.write(contentsOf: local)

            try writePayload(entry.source, to: handle)

            var central = Data()
            central.append(littleEndian: UInt32(0x0201_4B50)) // central directory header
            central.append(littleEndian: UInt16(20))          // version made by
            central.append(littleEndian: UInt16(20))          // version needed
            central.append(littleEndian: UInt16(0))
            central.append(littleEndian: UInt16(0))
            central.append(littleEndian: UInt16(0))
            central.append(littleEndian: UInt16(0))
            central.append(littleEndian: crc)
            central.append(littleEndian: UInt32(size))
            central.append(littleEndian: UInt32(size))
            central.append(littleEndian: UInt16(nameBytes.count))
            central.append(littleEndian: UInt16(0))           // extra
            central.append(littleEndian: UInt16(0))           // comment
            central.append(littleEndian: UInt16(0))           // disk number
            central.append(littleEndian: UInt16(0))           // internal attributes
            central.append(littleEndian: UInt32(0))           // external attributes
            central.append(littleEndian: offset)
            central.append(contentsOf: nameBytes)
            directory.append(central)

            offset += UInt32(local.count) + UInt32(size)
            count += 1
        }

        let directoryOffset = offset
        try handle.write(contentsOf: directory)

        var end = Data()
        end.append(littleEndian: UInt32(0x0605_4B50))         // end of central directory
        end.append(littleEndian: UInt16(0))
        end.append(littleEndian: UInt16(0))
        end.append(littleEndian: count)
        end.append(littleEndian: count)
        end.append(littleEndian: UInt32(directory.count))
        end.append(littleEndian: directoryOffset)
        end.append(littleEndian: UInt16(0))                   // comment length
        try handle.write(contentsOf: end)

        try handle.synchronize()
        return Int64(directoryOffset) + Int64(directory.count) + Int64(end.count)
    }

    // MARK: - Payloads

    private static let chunkSize = 1 << 20   // 1 MB

    private func checksum(_ source: Entry.Source) throws -> (crc: UInt32, size: Int) {
        switch source {
        case .data(let data):
            return (CRC32.checksum(data), data.count)
        case .file(let url):
            let handle = try FileHandle(forReadingFrom: url)
            defer { try? handle.close() }
            var crc = CRC32()
            var size = 0
            while let chunk = try handle.read(upToCount: Self.chunkSize), !chunk.isEmpty {
                crc.update(chunk)
                size += chunk.count
            }
            return (crc.value, size)
        }
    }

    private func writePayload(_ source: Entry.Source, to handle: FileHandle) throws {
        switch source {
        case .data(let data):
            try handle.write(contentsOf: data)
        case .file(let url):
            let reader = try FileHandle(forReadingFrom: url)
            defer { try? reader.close() }
            while let chunk = try reader.read(upToCount: Self.chunkSize), !chunk.isEmpty {
                try handle.write(contentsOf: chunk)
            }
        }
    }
}

/// CRC-32, as ZIP requires. Table-driven so a gigabyte of video does not take all day.
struct CRC32 {
    private static let table: [UInt32] = (0..<256).map { index -> UInt32 in
        var value = UInt32(index)
        for _ in 0..<8 {
            value = (value & 1) == 1 ? (value >> 1) ^ 0xEDB8_8320 : value >> 1
        }
        return value
    }

    private var current: UInt32 = 0xFFFF_FFFF

    mutating func update(_ data: Data) {
        var value = current
        for byte in data {
            value = (value >> 8) ^ Self.table[Int((value ^ UInt32(byte)) & 0xFF)]
        }
        current = value
    }

    var value: UInt32 { current ^ 0xFFFF_FFFF }

    static func checksum(_ data: Data) -> UInt32 {
        var crc = CRC32()
        crc.update(data)
        return crc.value
    }
}

extension Data {
    mutating func append(littleEndian value: UInt16) {
        append(contentsOf: [UInt8(value & 0xFF), UInt8((value >> 8) & 0xFF)])
    }

    mutating func append(littleEndian value: UInt32) {
        append(contentsOf: [
            UInt8(value & 0xFF),
            UInt8((value >> 8) & 0xFF),
            UInt8((value >> 16) & 0xFF),
            UInt8((value >> 24) & 0xFF),
        ])
    }
}
