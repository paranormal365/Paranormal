import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Replaying a session: one playhead driving the trace, the map, the compass and the media.
///
/// The decisions worth testing are about what a replay is ALLOWED to invent. It may interpolate
/// a position, because somebody walking between two fixes really was somewhere in between. It
/// may not smooth a field trace, because that would draw a reading nobody took.
@Suite("Session replay")
@MainActor
struct SessionReplayTests {

    private let start = Date(timeIntervalSince1970: 1_787_600_000)

    private func log(_ readings: [FieldReading]) async throws -> ReadingLog {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("replay-\(UUID().uuidString)")
            .appendingPathComponent("readings.jsonl")
        let log = ReadingLog(fileURL: url)
        for reading in readings { try await log.append(reading) }
        try await log.close()
        return log
    }

    private func reading(_ offset: TimeInterval, emf: Double? = nil, sound: Double? = nil,
                         latitude: Double? = nil, longitude: Double? = nil,
                         accuracy: Double? = nil, heading: Double? = nil) -> FieldReading {
        var measurements: [String: FieldReading.Measurement] = [:]
        if let emf { measurements["emf"] = .number(emf, unit: "uT") }
        if let sound { measurements["sound_level"] = .number(sound, unit: "dBFS") }

        var position: FieldReading.Position?
        if let latitude, let longitude {
            position = .init(latitude: latitude, longitude: longitude, accuracyMeters: accuracy)
        }
        return FieldReading(
            at: start.addingTimeInterval(offset), triggeredBy: .interval,
            measurements: measurements.isEmpty ? nil : measurements,
            position: position,
            motion: heading.map { FieldReading.Motion(headingDegrees: $0) })
    }

    private func loaded(_ readings: [FieldReading],
                        markers: [FieldMarkerRecord] = [],
                        media: [MediaSegment] = [],
                        baselines: Baselines = Baselines(magneticMicrotesla: 48),
                        endedAt: Date? = nil) async throws -> SessionReplay {
        let replay = SessionReplay()
        await replay.load(readingLog: try await log(readings), markers: markers, media: media,
                          baselines: baselines, startedAt: start,
                          endedAt: endedAt ?? start.addingTimeInterval(60))
        return replay
    }

    // MARK: - The trace

    @Test func aReadingIsHeldUntilTheNextOneRatherThanSmoothedIntoIt() async throws {
        // Instruments hold their last value. Interpolating a field trace would draw a reading
        // nobody took, at a moment nobody measured.
        let replay = try await loaded([reading(0, emf: 48), reading(10, emf: 58)])

        replay.seek(to: start.addingTimeInterval(5))
        #expect(replay.frame.magneticMicrotesla == 48)

        replay.seek(to: start.addingTimeInterval(9.9))
        #expect(replay.frame.magneticMicrotesla == 48)

        replay.seek(to: start.addingTimeInterval(10))
        #expect(replay.frame.magneticMicrotesla == 58)
    }

    @Test func theTraceShowsDeviationAgainstTheBaseThatWasActuallySet() async throws {
        let replay = try await loaded([reading(0, emf: 53)],
                                      baselines: Baselines(magneticMicrotesla: 48))
        replay.seek(to: start)
        // +5 uT = +50 mG, measured against the base the investigator set that night.
        #expect(replay.frame.magneticDeviationMilligauss(from: replay.timeline.baselines) == 50)
    }

    @Test func beforeTheFirstReadingThereIsNothingToShow() async throws {
        let replay = try await loaded([reading(10, emf: 48)])
        replay.seek(to: start)
        #expect(replay.frame.magneticMicrotesla == nil)
    }

    // MARK: - The path

    @Test func positionIsInterpolatedBecauseSomebodyReallyWasInBetween() async throws {
        let replay = try await loaded([
            reading(0, latitude: 36.0, longitude: -86.0, accuracy: 20),
            reading(10, latitude: 36.001, longitude: -86.001, accuracy: 30),
        ])

        replay.seek(to: start.addingTimeInterval(5))
        let position = try #require(replay.frame.position)
        #expect(abs((position.latitude ?? 0) - 36.0005) < 0.000001)
        #expect(abs((position.longitude ?? 0) + 86.0005) < 0.000001)
        // An interpolated point is at best as trustworthy as the worse fix either side of it.
        #expect(position.accuracyMeters == 30)
    }

    @Test func aLongGapBetweenFixesHoldsTheLastPlaceRatherThanDrawingALine() async throws {
        // Two fixes four minutes apart say nothing about the middle. A straight line across a
        // building somebody walked around is less true than admitting the gap.
        let replay = try await loaded([
            reading(0, latitude: 36.0, longitude: -86.0),
            reading(240, latitude: 36.01, longitude: -86.01),
        ], endedAt: start.addingTimeInterval(300))

        replay.seek(to: start.addingTimeInterval(120))
        #expect(replay.frame.position?.latitude == 36.0)
    }

    @Test func readingsWithoutAFixAreLeftOutOfTheTrackEntirely() async throws {
        let replay = try await loaded([
            reading(0, emf: 48),
            reading(1, latitude: 36.0, longitude: -86.0),
            reading(2, emf: 49),
        ])
        #expect(replay.timeline.track.count == 1)
    }

    // MARK: - Media

    @Test func thePlayheadKnowsWhichClipCoversItAndHowFarIn() async throws {
        let clip = MediaSegment(kind: .audio, relativePath: "media/audio-001.m4a",
                                startedAt: start.addingTimeInterval(10), duration: 30)
        let replay = try await loaded([reading(0, emf: 48)], media: [clip])

        replay.seek(to: start.addingTimeInterval(25))
        let active = try #require(replay.frame.activeMedia)
        #expect(active.segment.relativePath == "media/audio-001.m4a")
        #expect(active.offset == 15)
    }

    @Test func aPlayheadOverAGapHasNoMediaAndSaysSo() async throws {
        // Audio and video are clips, not a continuous stream — most of a night has neither, and
        // a replay that looked broken there would be lying about what was recorded.
        let clip = MediaSegment(kind: .audio, relativePath: "media/audio-001.m4a",
                                startedAt: start.addingTimeInterval(10), duration: 5)
        let replay = try await loaded([reading(0, emf: 48)], media: [clip])

        replay.seek(to: start.addingTimeInterval(30))
        #expect(replay.frame.activeMedia == nil)

        replay.seek(to: start.addingTimeInterval(12))
        #expect(replay.frame.activeMedia != nil)
    }

    // MARK: - Markers

    @Test func jumpingToAMarkerStartsJustBeforeItSoTheRunUpCanBeHeard() async throws {
        let marker = FieldMarkerRecord(at: start.addingTimeInterval(40), kind: .sentryEmf)
        let replay = try await loaded([reading(0, emf: 48)], markers: [marker])

        replay.seek(to: marker)
        // Landing exactly on the bang means never hearing what led to it.
        #expect(replay.playhead == start.addingTimeInterval(37))
    }

    @Test func steppingThroughMarkersGoesForwardAndBack() async throws {
        let markers = [
            FieldMarkerRecord(at: start.addingTimeInterval(10), kind: .manual),
            FieldMarkerRecord(at: start.addingTimeInterval(30), kind: .sentryEmf),
        ]
        let replay = try await loaded([reading(0, emf: 48)], markers: markers)

        replay.stepMarker(forward: true)
        #expect(replay.playhead == start.addingTimeInterval(7))
        replay.stepMarker(forward: true)
        #expect(replay.playhead == start.addingTimeInterval(27))
        replay.stepMarker(forward: false)
        #expect(replay.playhead == start.addingTimeInterval(7))
    }

    // MARK: - Transport

    @Test func theSeekBarMapsOntoTheWholeSession() async throws {
        let replay = try await loaded([reading(0, emf: 48)],
                                      endedAt: start.addingTimeInterval(100))
        replay.seek(fraction: 0.5)
        #expect(replay.playhead == start.addingTimeInterval(50))
        #expect(abs(replay.timeline.fraction(of: replay.playhead) - 0.5) < 0.0001)
    }

    @Test func seekingPastEitherEndStaysInsideTheSession() async throws {
        let replay = try await loaded([reading(0, emf: 48)],
                                      endedAt: start.addingTimeInterval(100))
        replay.seek(to: start.addingTimeInterval(-500))
        #expect(replay.playhead == start)
        replay.seek(to: start.addingTimeInterval(9_999))
        #expect(replay.playhead == replay.timeline.endedAt)
    }

    @Test func anInterruptedSessionReplaysToItsLastRecordedMoment() async throws {
        // No honest end time exists, so the replay runs to the last thing actually recorded
        // rather than to a guess.
        let replay = SessionReplay()
        await replay.load(readingLog: try await log([reading(0, emf: 48), reading(42, emf: 49)]),
                          markers: [], media: [], baselines: Baselines(),
                          startedAt: start, endedAt: nil)
        #expect(replay.timeline.endedAt == start.addingTimeInterval(42))
    }

    @Test func playingAdvancesThePlayheadAndStopsAtTheEnd() async throws {
        let replay = try await loaded([reading(0, emf: 48)],
                                      endedAt: start.addingTimeInterval(1))
        replay.setRate(8)
        replay.play()
        #expect(replay.isPlaying)

        try await Task.sleep(for: .milliseconds(400))

        #expect(replay.playhead > start)
        replay.pause()
        #expect(!replay.isPlaying)
    }

    @Test func playbackSpeedIsClampedToSomethingUsable() async throws {
        let replay = try await loaded([reading(0, emf: 48)])
        // Guards the crash this found: under @Observable a self-assigning didSet recurses
        // forever, so clamping goes through a method.
        replay.setRate(1_000)
        #expect(replay.rate == 64)      // a five-hour vigil is not watched at 1x
        replay.setRate(0.001)
        #expect(replay.rate == 0.5)
    }
}
