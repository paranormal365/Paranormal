import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// What the engine decides is worth writing down.
///
/// This is where the feature is either honest or not: whether a spike is real, whether one event
/// is one record, whether a reading claims a position it does not have. None of it can be tested
/// on a device — the simulator has no magnetometer — so scripted streams and a hand-driven clock
/// are the only way any of it is exercised at all.
@Suite("Field session engine — what gets recorded")
struct FieldSessionEngineTests {

    private let start = Date(timeIntervalSince1970: 1_787_600_000)

    private func makeEngine(
        magnetometer: MagnetometerSource? = nil,
        audio: AudioLevelSource? = nil,
        location: LocationSource? = nil,
        altitude: AltitudeSource? = nil,
        battery: Double? = nil,
        policy: SamplingPolicy = .default,
        channels: CaptureChannels = .default,
        clock: ManualClock
    ) -> (FieldSessionEngine, ReadingLog, URL) {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("engine-\(UUID().uuidString)")
            .appendingPathComponent("readings.jsonl")
        let log = ReadingLog(fileURL: url)
        let sensors = SensorSuite(magnetometer: magnetometer, audio: audio,
                                  location: location, altitude: altitude,
                                  batteryPercent: { battery })
        let engine = FieldSessionEngine(sessionId: UUID(), log: log, sensors: sensors,
                                        policy: policy, channels: channels,
                                        now: clock.nowProvider)
        return (engine, log, url.deletingLastPathComponent())
    }

    // MARK: - Heartbeats

    @Test func heartbeatsAreWrittenOnTheIntervalNotOnEverySample() async throws {
        // 10 Hz to the gauge, one reading every 2 s to the log. Logging every sample would put
        // 180,000 records in a five-hour session and buy nothing.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(gaugeHz: 10, heartbeatSeconds: 2), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        // 100 samples at 10 Hz spans t=0.0 through t=9.9.
        for index in 0..<100 {
            await engine.ingest(magnetic: MagneticFieldSample(
                at: start.addingTimeInterval(Double(index) * 0.1), x: 50, y: 0, z: 0))
        }
        await engine.stop()

        let readings = try await log.readings()
        // Heartbeats at t = 0, 2, 4, 6, 8 — five records standing in for a hundred samples.
        #expect(readings.count == 5)
        #expect(readings.allSatisfy { $0.triggeredBy == .interval })
    }

    @Test func aReadingCarriesTheFieldItsBaselineAndItsAccuracy() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(battery: 82, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.arm(.default)
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(2.5),
                                                          x: 49, y: 0, z: 0, calibration: .high))
        await engine.stop()

        let reading = try #require(try await log.readings().last)
        let emf = try #require(reading.measurements?["emf"])
        #expect(emf.unit == "uT")
        #expect(emf.value == .number(49))
        // 48 means nothing until you know the ambient was 48.
        #expect(emf.baseline == 48)
        #expect(emf.accuracy == 0.5)   // what a high-calibration reading deserves
        #expect(reading.measurements?["battery"]?.value == .number(82))
    }

    @Test func aReadingWithNoFixHasNoPositionAtAll() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 50, y: 0, z: 0))
        await engine.stop()

        let reading = try #require(try await log.readings().first)
        // (0, 0) is a real place in the Gulf of Guinea. Absence is the only honest answer.
        #expect(reading.position == nil)
    }

    @Test func aFixTravelsWithItsAccuracyBecauseIndoorsItIsTerrible() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(position: PositionSample(
            at: start, latitude: 36.1627, longitude: -86.7816,
            altitudeMeters: 182, accuracyMeters: 42, speedMps: 0.4))
        await engine.ingest(heading: HeadingSample(at: start, degrees: 271.5))
        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 50, y: 0, z: 0))
        await engine.stop()

        let reading = try #require(try await log.readings().first)
        let position = try #require(reading.position)
        #expect(position.latitude == 36.1627)
        #expect(position.accuracyMeters == 42)     // 42 m is the difference between rooms
        // Heading is motion, not a measurement — the schema decides where it goes.
        #expect(reading.motion?.headingDegrees == 271.5)
        #expect(reading.motion?.speedMps == 0.4)
        #expect(reading.measurements?["heading"] == nil)
    }

    // MARK: - The report level

    @Test func crossingTheReportLevelWritesExactlyOneEvent() async throws {
        // One door slamming must be one record. Without debounce a single event at 10 Hz writes
        // thirty, and a reviewer cannot tell how many things happened.
        let clock = ManualClock(start)
        let policy = SamplingPolicy(gaugeHz: 10, heartbeatSeconds: 60,
                                    reportAtMilligauss: 20, debounceSeconds: 3)
        let (engine, log, directory) = makeEngine(policy: policy, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.arm(.default)

        // A three-second excursion of +5 uT (= +50 mG), sampled at 10 Hz: thirty samples over
        // the line.
        for index in 0..<30 {
            await engine.ingest(magnetic: MagneticFieldSample(
                at: start.addingTimeInterval(1 + Double(index) * 0.1),
                x: 53, y: 0, z: 0, calibration: .high))
        }
        await engine.stop()

        let events = try await log.readings().filter { $0.triggeredBy == .event }
        #expect(events.count == 1)
        #expect(events.first?.measurements?["marker"]?.value == .string("sentry_emf"))
        #expect(events.first?.note?.contains("50 mG") == true)
    }

    @Test func aSecondEventLandsOnceTheQuietPeriodHasPassed() async throws {
        let clock = ManualClock(start)
        let policy = SamplingPolicy(gaugeHz: 10, heartbeatSeconds: 60,
                                    reportAtMilligauss: 20, debounceSeconds: 3)
        let (engine, log, directory) = makeEngine(policy: policy, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.arm(.default)

        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(1),
                                                          x: 53, y: 0, z: 0, calibration: .high))
        // Four seconds later — past the quiet period, so this is a second thing happening.
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(5),
                                                          x: 53, y: 0, z: 0, calibration: .high))
        await engine.stop()

        #expect(try await log.readings().filter { $0.triggeredBy == .event }.count == 2)
    }

    @Test func noiseBelowTheReportLevelIsNeverAnEvent() async throws {
        let clock = ManualClock(start)
        let policy = SamplingPolicy(gaugeHz: 10, heartbeatSeconds: 60, reportAtMilligauss: 20)
        let (engine, log, directory) = makeEngine(policy: policy, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.arm(.default)

        // ±1.5 uT = ±15 mG, under the 20 mG line. A meter that fires on this is a meter nobody
        // trusts by midnight.
        for index in 0..<40 {
            let wobble = index.isMultiple(of: 2) ? 1.5 : -1.5
            await engine.ingest(magnetic: MagneticFieldSample(
                at: start.addingTimeInterval(1 + Double(index) * 0.1),
                x: 48 + wobble, y: 0, z: 0, calibration: .high))
        }
        await engine.stop()

        #expect(try await log.readings().allSatisfy { $0.triggeredBy != .event })
    }

    @Test func nothingIsReportedBeforeABaseLevelIsSet() async throws {
        // Absolute field means nothing: the Earth alone is around 500 mG and every building
        // moves it. Until somebody says "this room is normal", there is nothing to depart from.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(heartbeatSeconds: 60, reportAtMilligauss: 20), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        // ARMED, deliberately: without this the test would pass because nothing was watching,
        // which proves nothing about base levels.
        await engine.arm(.default)

        for index in 0..<20 {
            await engine.ingest(magnetic: MagneticFieldSample(
                at: start.addingTimeInterval(Double(index) * 0.1),
                x: Double(index) * 20, y: 0, z: 0, calibration: .high))
        }
        await engine.stop()

        #expect(try await log.readings().allSatisfy { $0.triggeredBy != .event })
    }

    @Test func anUncalibratedSpikeIsLoggedButNeverReportedAsAnEvent() async throws {
        // A reading the magnetometer itself does not trust is not evidence. It still reaches the
        // log — hiding it would be its own dishonesty — but it does not ring a bell.
        let clock = ManualClock(start)
        let policy = SamplingPolicy(gaugeHz: 10, heartbeatSeconds: 2, reportAtMilligauss: 20)
        let (engine, log, directory) = makeEngine(policy: policy, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.arm(.default)
        await engine.ingest(magnetic: MagneticFieldSample(
            at: start.addingTimeInterval(3), x: 90, y: 0, z: 0, calibration: .uncalibrated))
        await engine.stop()

        let readings = try await log.readings()
        #expect(readings.allSatisfy { $0.triggeredBy != .event })
        let spike = try #require(readings.last)
        #expect(spike.measurements?["emf"]?.value == .number(90))
        // No accuracy claimed, because none is known.
        #expect(spike.measurements?["emf"]?.accuracy == nil)
    }

    @Test func soundRisingAboveBaseIsItsOwnKindOfEvent() async throws {
        let clock = ManualClock(start)
        let policy = SamplingPolicy(heartbeatSeconds: 60, reportAtDecibels: 12, debounceSeconds: 3)
        let (engine, log, directory) = makeEngine(policy: policy, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(audio: AudioLevelSample(at: start, averageDbfs: -54, peakDbfs: -50))
        _ = await engine.setBaselines()
        await engine.arm(.default)
        await engine.ingest(audio: AudioLevelSample(at: start.addingTimeInterval(1),
                                                    averageDbfs: -38, peakDbfs: -30))
        await engine.stop()

        let events = try await log.readings().filter { $0.triggeredBy == .event }
        #expect(events.count == 1)
        #expect(events.first?.measurements?["marker"]?.value == .string("sentry_sound"))
    }

    @Test func resettingTheBaseLevelForgetsTheOldQuietPeriod() async throws {
        // A new base describes a different room. Carrying the old debounce forward would
        // swallow the first event under the new one.
        let clock = ManualClock(start)
        let policy = SamplingPolicy(heartbeatSeconds: 60, reportAtMilligauss: 20,
                                    debounceSeconds: 30)
        let (engine, log, directory) = makeEngine(policy: policy, clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.arm(.default)
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(1),
                                                          x: 53, y: 0, z: 0, calibration: .high))

        clock.advance(by: 2)
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(2),
                                                          x: 60, y: 0, z: 0, calibration: .high))
        _ = await engine.setBaselines()   // this room is normal now
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(3),
                                                          x: 65, y: 0, z: 0, calibration: .high))
        await engine.stop()

        // Two events, three seconds apart, despite a thirty-second quiet period — because the
        // base was reset in between.
        #expect(try await log.readings().filter { $0.triggeredBy == .event }.count == 2)
    }

    // MARK: - Marking by hand

    @Test func aManualMarkerRecordsWhatTheInstrumentsSaidAtThatMoment() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0))
        await engine.ingest(position: PositionSample(at: start, latitude: 36.1, longitude: -86.7))
        clock.advance(by: 10)

        let marker = await engine.mark(kind: .manual, note: "Cold spot by the window")
        await engine.stop()

        #expect(marker.kind == .manual)
        #expect(marker.magneticMicrotesla == 48)
        #expect(marker.latitude == 36.1)

        let written = try #require(try await log.readings().last)
        #expect(written.triggeredBy == .manual)
        #expect(written.measurements?["marker"]?.value == .string("manual_marker"))
        #expect(written.note == "Cold spot by the window")
    }

    @Test func aMarkerDuringARecordingKnowsWhereItLandedInTheFile() async throws {
        // This is what lets somebody tap a marker in review and hear that moment — and it is
        // the same shape the server's audio markers use, so it can travel later.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.setRecording(filename: "media/audio-001.m4a", startedAt: start)
        clock.advance(by: 42.5)
        let marker = await engine.mark(kind: .evpQuestion, note: "Is anyone here?")
        await engine.stop()

        #expect(marker.audioFilename == "media/audio-001.m4a")
        #expect(marker.audioOffsetSeconds == 42.5)

        let written = try #require(try await log.readings().last)
        #expect(written.audioRef?.filename == "media/audio-001.m4a")
        #expect(written.audioRef?.startOffsetSeconds == 42.5)
        #expect(written.audioRef?.mediaType == "audio/mp4")
    }

    // MARK: - Channels

    @Test func onlyTheChannelsSwitchedOnAreEvenRead() async throws {
        // Somebody switching video off at 2am wants the battery back, not a stream running
        // quietly in the background.
        let clock = ManualClock(start)
        let magnetometer = ScriptedMagnetometer.steady(50, from: start, count: 5)
        let audio = ScriptedAudio.steady(-50, from: start, count: 5)
        let (engine, log, directory) = makeEngine(
            magnetometer: magnetometer, audio: audio,
            policy: SamplingPolicy(heartbeatSeconds: 0), channels: [.magnetic], clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.start()
        try await Task.sleep(for: .milliseconds(120))
        await engine.stop()

        let readings = try await log.readings()
        #expect(!readings.isEmpty)
        // Audio was switched off, so nothing anywhere claims a sound level.
        #expect(readings.allSatisfy { $0.measurements?["sound_level"] == nil })
        #expect(readings.contains { $0.measurements?["emf"] != nil })
    }

    @Test func theTriggerBlockDescribesTheRulesInTheOperatorsOwnUnits() {
        // A reviewer reads this sentence in the exported file to know what a gap means.
        let policy = SamplingPolicy(heartbeatSeconds: 2, reportAtMilligauss: 20,
                                    reportAtDecibels: 12, debounceSeconds: 3)
        let trigger = policy.trigger()
        #expect(trigger.mode == .hybrid)
        #expect(trigger.intervalSeconds == 2)
        #expect(trigger.debounceSeconds == 3)
        #expect(trigger.eventDescription?.contains("20 mG") == true)
        #expect(trigger.eventDescription?.contains("12 dB") == true)
    }

    // MARK: - Captures

    @Test func aCaptureNamesItsFileByARelativePathTheBundleCanCarry() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(position: PositionSample(at: start, latitude: 36.1, longitude: -86.7,
                                                     accuracyMeters: 30))
        await engine.noteCapture(kind: .photo, relativePath: "media/photo-001.jpg")
        await engine.stop()

        let reading = try #require(try await log.readings().last)
        #expect(reading.triggeredBy == .manual)
        #expect(reading.measurements?["marker"]?.value == .string("photo"))
        // Photos have no home in the v1 format's audio_ref, so the path travels in the note —
        // and it is a RELATIVE path, which is what the bundle's own rules require.
        #expect(reading.note == "photo: media/photo-001.jpg")
        #expect(FieldReading.FileRef.relative("media/photo-001.jpg") != nil)
        // Stamped with where it was taken, which is the whole reason to capture inside a session.
        #expect(reading.position?.latitude == 36.1)
    }

    @Test func anAudioCaptureCarriesItsDurationInTheFileReference() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.noteCapture(kind: .audio, relativePath: "media/audio-001.m4a",
                                 durationSeconds: 128.5)
        await engine.stop()

        let reading = try #require(try await log.readings().last)
        let ref = try #require(reading.audioRef)
        #expect(ref.filename == "media/audio-001.m4a")
        #expect(ref.durationSeconds == 128.5)
        #expect(ref.mediaType == "audio/mp4")
    }

    // MARK: - A device left in a room

    @Test func nothingAutomaticFiresUntilTheSessionIsArmed() async throws {
        // "Armed" has to be a real state, not a label. A phone in somebody's hand should not be
        // filling the log with events every time they walk past a fridge.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(heartbeatSeconds: 60, reportAtMilligauss: 20), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.ingest(magnetic: MagneticFieldSample(at: start, x: 48, y: 0, z: 0,
                                                          calibration: .high))
        _ = await engine.setBaselines()
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(1),
                                                          x: 60, y: 0, z: 0, calibration: .high))
        #expect(try await log.readings().allSatisfy { $0.triggeredBy != .event })

        await engine.arm(.default)
        await engine.ingest(magnetic: MagneticFieldSample(at: start.addingTimeInterval(10),
                                                          x: 60, y: 0, z: 0, calibration: .high))
        await engine.stop()

        #expect(try await log.readings().contains { $0.triggeredBy == .event })
    }

    @Test func theDeviceBeingMovedIsItsOwnKindOfEvent() async throws {
        // Different question from "did anything in view move": this is whether the tripod got
        // knocked, or somebody picked the phone up.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(heartbeatSeconds: 60, debounceSeconds: 3), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.arm(SentryConfig(watchDeviceMovement: true,
                                      deviceMovementThresholdG: 0.05))

        // Sitting still.
        for index in 0..<10 {
            await engine.ingest(movement: DeviceMovementSample(
                at: start.addingTimeInterval(Double(index) * 0.1), magnitudeG: 0.004))
        }
        #expect(try await log.readings().allSatisfy { $0.triggeredBy != .event })

        // Knocked.
        await engine.ingest(movement: DeviceMovementSample(
            at: start.addingTimeInterval(2), magnitudeG: 0.18))
        await engine.stop()

        let events = try await log.readings().filter { $0.triggeredBy == .event }
        #expect(events.count == 1)
        #expect(events.first?.measurements?["marker"]?.value == .string("device_moved"))
    }

    @Test func aDeviceMovementSwitchedOffIsNotWatchedAtAll() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(heartbeatSeconds: 60), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.arm(SentryConfig(watchDeviceMovement: false))
        await engine.ingest(movement: DeviceMovementSample(at: start, magnitudeG: 2.0))
        await engine.stop()

        #expect(try await log.readings().allSatisfy { $0.triggeredBy != .event })
    }

    @Test func movementSeenThroughTheCameraIsRecordedWithHowMuchChanged() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(heartbeatSeconds: 60, debounceSeconds: 3), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.arm(SentryConfig(watchSceneMotion: true, sceneMotionThreshold: 0.08))

        // Grain and a flickering light are not somebody walking through.
        await engine.ingest(scene: SceneMotionSample(at: start, changedFraction: 0.02))
        #expect(try await log.readings().allSatisfy { $0.triggeredBy != .event })

        await engine.ingest(scene: SceneMotionSample(at: start.addingTimeInterval(1),
                                                     changedFraction: 0.31))
        await engine.stop()

        let events = try await log.readings().filter { $0.triggeredBy == .event }
        #expect(events.count == 1)
        #expect(events.first?.measurements?["marker"]?.value == .string("scene_motion"))
        #expect(events.first?.note?.contains("31%") == true)
    }

    @Test func armingAgainForgetsWhatTheLastRoomWasDoing() async throws {
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(
            policy: SamplingPolicy(heartbeatSeconds: 60, debounceSeconds: 60), clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.arm(SentryConfig(watchDeviceMovement: true))
        await engine.ingest(movement: DeviceMovementSample(at: start, magnitudeG: 0.2))
        // Re-armed — a new room, a new resting state, a new quiet period.
        await engine.arm(SentryConfig(watchDeviceMovement: true))
        await engine.ingest(movement: DeviceMovementSample(at: start.addingTimeInterval(1),
                                                           magnitudeG: 0.2))
        await engine.stop()

        #expect(try await log.readings().filter { $0.triggeredBy == .event }.count == 2)
    }

    @Test func theExportedTriggerSaysWhatWasActuallyBeingWatched() {
        // A reviewer reads this sentence to know what a gap in the log means. It has to list
        // what was armed, not everything the app can do.
        let policy = SamplingPolicy(reportAtMilligauss: 20, reportAtDecibels: 12)
        let sentry = SentryConfig(watchMagnetic: true, watchSound: false,
                                  watchDeviceMovement: true, watchSceneMotion: false)
        let description = policy.trigger(sentry: sentry).eventDescription ?? ""

        #expect(description.contains("20 mG"))
        #expect(description.contains("moved"))
        #expect(!description.contains("dB"))        // sound was not being watched
        #expect(!description.contains("view"))
    }

    // MARK: - EVP question and answer

    @Test func askingAQuestionBracketsTheSilenceThatFollowsIt() async throws {
        // What a reviewer needs is the SILENCE bracketed, not just the moment somebody spoke —
        // the answer, if there is one, is in the gap.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.setRecording(filename: "media/audio-001.m4a", startedAt: start)
        clock.advance(by: 30)
        await engine.askQuestion("Is anyone here with us?")
        clock.advance(by: 12)
        let end = await engine.endWait()
        await engine.stop()

        #expect(end != nil)
        let readings = try await log.readings()
        let question = try #require(readings.first {
            $0.measurements?["marker"]?.value == .string("evp_question")
        })
        let waitEnd = try #require(readings.first {
            $0.measurements?["marker"]?.value == .string("evp_wait_end")
        })

        // Both point into the SAME file, so a reviewer can play the gap between them.
        #expect(question.audioRef?.filename == "media/audio-001.m4a")
        #expect(waitEnd.audioRef?.filename == "media/audio-001.m4a")
        #expect(question.audioRef?.startOffsetSeconds == 30)
        #expect(waitEnd.audioRef?.startOffsetSeconds == 42)
        #expect(waitEnd.note?.contains("12s") == true)
        #expect(waitEnd.note?.contains("Is anyone here with us?") == true)
    }

    @Test func endingAWaitThatNeverStartedDoesNothing() async throws {
        // A mark with nothing on the other end of it is worse than no mark.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        #expect(await engine.endWait() == nil)
        await engine.stop()
        #expect(try await log.readings().isEmpty)
    }

    @Test func askingAgainClosesTheWaitYouWereAlreadyOn() async throws {
        // Moving straight to the next question ends the last silence by speaking. Leaving it
        // open forever would describe a wait that never finished.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        await engine.askQuestion("First")
        clock.advance(by: 8)
        await engine.askQuestion("Second")
        await engine.stop()

        let readings = try await log.readings()
        let ends = readings.filter { $0.measurements?["marker"]?.value == .string("evp_wait_end") }
        #expect(ends.count == 1)
        #expect(ends.first?.note?.contains("First") == true)
        #expect(readings.filter {
            $0.measurements?["marker"]?.value == .string("evp_question")
        }.count == 2)
    }

    @Test func aQuestionAskedWithNoRecordingRunningStillMarksTheMoment() async throws {
        // Somebody may be recording on a separate device entirely. The mark is still the useful
        // thing; it simply has no file to point into.
        let clock = ManualClock(start)
        let (engine, log, directory) = makeEngine(clock: clock)
        defer { try? FileManager.default.removeItem(at: directory) }

        let marker = await engine.askQuestion("Anyone?")
        await engine.stop()

        #expect(marker.audioFilename == nil)
        #expect(try await log.readings().contains {
            $0.measurements?["marker"]?.value == .string("evp_question")
        })
    }
}