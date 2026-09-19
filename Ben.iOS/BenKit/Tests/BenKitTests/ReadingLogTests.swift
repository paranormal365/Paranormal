import Foundation
import Testing
@testable import BenKit

/// The reading log is the reason a session is a file and not a database table. These tests are
/// about the night the phone dies at 3am: what survives, and whether anyone can still read it.
@Suite("Reading log — crash safety")
struct ReadingLogTests {

    private func scratchURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("log-\(UUID().uuidString)")
            .appendingPathComponent("readings.jsonl")
    }

    private func reading(_ offset: TimeInterval, sequence: Int) -> FieldReading {
        FieldReading(at: Date(timeIntervalSince1970: 1_787_600_000 + offset),
                     precision: .millisecond, sequence: sequence, triggeredBy: .interval,
                     measurements: ["emf": .number(3.0 + offset, unit: "uT")])
    }

    @Test func readingsAppendAsOneLineEachAndComeBackInOrder() async throws {
        let url = scratchURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }

        let log = ReadingLog(fileURL: url)
        for index in 0..<5 { try await log.append(reading(Double(index), sequence: index + 1)) }
        try await log.close()

        let raw = try String(contentsOf: url, encoding: .utf8)
        #expect(raw.split(separator: "\n").count == 5)
        #expect(raw.hasSuffix("\n"))   // a complete file ends complete

        let readings = try await log.readings()
        #expect(readings.count == 5)
        #expect(readings.map(\.sequence) == [1, 2, 3, 4, 5])
        // Oldest first, which is what the spec requires of the exported array.
        #expect(readings.map(\.at) == readings.map(\.at).sorted())
    }

    @Test func aTornFinalLineIsTruncatedAndEverythingBeforeItSurvives() async throws {
        // The 3am case: the write of reading six was interrupted halfway. Five whole readings
        // are on disk and must be recoverable — a single JSON array would be unparseable here,
        // which is exactly why this is JSONL.
        let url = scratchURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }

        let log = ReadingLog(fileURL: url)
        for index in 0..<5 { try await log.append(reading(Double(index), sequence: index + 1)) }
        try await log.close()

        let torn = #"{"at":"2026-08-25T00:10:00.000Z","measurements":{"emf":{"val"#
        let handle = try FileHandle(forWritingTo: url)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data(torn.utf8))
        try handle.close()

        let survivors = try await log.recover()
        #expect(survivors == 5)

        let readings = try await log.readings()
        #expect(readings.count == 5)
        #expect(readings.last?.sequence == 5)

        // And the log is writable again afterwards — a recovered session can keep going.
        // This is the assertion that proves truncation HAPPENED: without it the next append
        // would weld itself onto the torn tail, and the merged line would decode as nothing.
        try await log.append(reading(99, sequence: 6))
        let afterResuming = try await log.readings()
        #expect(afterResuming.count == 6)
        #expect(afterResuming.last?.sequence == 6)
    }

    @Test func aFileThatIsNothingButATornWriteRecoversToEmpty() async throws {
        let url = scratchURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        try Data(#"{"at":"2026-08-2"#.utf8).write(to: url)

        let log = ReadingLog(fileURL: url)
        #expect(try await log.recover() == 0)
        #expect(try await log.readings().isEmpty)
    }

    @Test func oneUnreadableLineDoesNotCostTheOtherTwentyThousand() async throws {
        // A record we cannot decode is skipped, not fatal. Failing the whole read would hand a
        // reviewer nothing because of one bad row.
        let url = scratchURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }

        let log = ReadingLog(fileURL: url)
        try await log.append(reading(0, sequence: 1))
        try await log.close()

        let handle = try FileHandle(forWritingTo: url)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data("{\"nonsense\":true}\n".utf8))
        try handle.close()

        try await log.append(reading(2, sequence: 3))
        try await log.close()

        let readings = try await log.readings()
        #expect(readings.count == 2)
        #expect(readings.map(\.sequence) == [1, 3])
    }

    @Test func recoveringAnAbsentLogIsNotAnError() async throws {
        let log = ReadingLog(fileURL: scratchURL())
        #expect(try await log.recover() == 0)
        #expect(try await log.readings().isEmpty)
        #expect(try await log.rawLines().isEmpty)
    }

    @Test func rawLinesAreTheExactBytesExportWillSplice() async throws {
        // Export writes these verbatim between `"readings": [` and `]` — no decode/re-encode
        // round trip, so a 20,000-reading session never has to fit in memory.
        let url = scratchURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }

        let log = ReadingLog(fileURL: url)
        try await log.append(reading(0, sequence: 1))
        try await log.append(reading(1, sequence: 2))
        try await log.close()

        let lines = try await log.rawLines()
        #expect(lines.count == 2)
        for line in lines {
            let object = try JSONSerialization.jsonObject(with: line) as? [String: Any]
            #expect(object?["at"] != nil)
            #expect(!line.contains(0x0A))   // no stray newlines inside a record
        }
    }
}
