import Foundation
import AVFoundation
import Testing
@testable import BenKit

/// The sound reading and the sound file, after Ben's 10/20/2026 test session: "the audio never
/// showed more than -60 dB" and "I didn't see that it played back the audio for the recording."
@Suite("Session recording: level math and the sound file")
struct SessionRecordingTests {

    // MARK: - Level math

    @Test func fullScaleIsZeroAndHalfScaleIsMinusSix() {
        #expect(SoundLevel.decibels(1) == 0)
        #expect(abs(SoundLevel.decibels(0.5) - (-6.0206)) < 0.001)
        #expect(abs(SoundLevel.decibels(0.1) - (-20)) < 0.0001)
    }

    @Test func silenceReadsAsTheFloorNotNegativeInfinity() {
        #expect(SoundLevel.decibels(0) == SoundLevel.floorDbfs)
        let silent = SoundLevel.levels(of: [Float](repeating: 0, count: 2048))
        #expect(silent?.average == SoundLevel.floorDbfs)
        #expect(silent?.peak == SoundLevel.floorDbfs)
        #expect(SoundLevel.decibels(.nan) == SoundLevel.floorDbfs)
    }

    /// The bug itself. A voice across a room, with the phone's own gain off, is a sine of about
    /// 0.001 of full scale: -60 dB peak, -63 dB RMS. The old floor of -60 reported that, and
    /// everything quieter, as exactly -60 — the number Ben saw all night.
    @Test func aQuietVoiceIsNotPinnedAtMinusSixty() throws {
        let voice = Self.sine(amplitude: 0.001, frames: 2048)
        let levels = try #require(SoundLevel.levels(of: voice))
        #expect(levels.average < -60)
        #expect(abs(levels.average - (-63)) < 0.2)   // 20·log10(0.001/√2) ≈ -63
        #expect(abs(levels.peak - (-60)) < 0.2)

        // And a quiet room reads lower than a voice: the meter moves when somebody speaks.
        let room = try #require(SoundLevel.levels(of: Self.sine(amplitude: 0.000_1, frames: 2048)))
        #expect(room.average < levels.average - 19)
        #expect(SoundLevel.floorDbfs <= -90)
    }

    @Test func aLoudSoundCannotReadAboveFullScale() throws {
        let clipped = [Float](repeating: 1.5, count: 512)
        let levels = try #require(SoundLevel.levels(of: clipped))
        #expect(levels.average == 0)
        #expect(levels.peak == 0)
    }

    @Test func interleavedStereoIsMeasuredOnTheFirstChannelOnly() throws {
        // Left loud, right silent, interleaved L R L R.
        var samples: [Float] = []
        for frame in 0..<1024 { samples += [frame.isMultiple(of: 2) ? 0.5 : -0.5, 0] }
        let left = try #require(SoundLevel.levels(of: samples, stride: 2))
        #expect(abs(left.average - SoundLevel.decibels(0.5)) < 0.01)
        let mixed = try #require(SoundLevel.levels(of: samples))
        #expect(mixed.average < left.average)
    }

    @Test func nothingToReadIsNil() {
        #expect(SoundLevel.levels(of: []) == nil)
    }

    // MARK: - The recording's clock

    @Test func buffersArrivingOnTimeOweNoSilence() {
        // 48 kHz, 10 s in, 480,000 - 2,048 frames written already: right on time.
        #expect(RecordingClock.silentFramesOwed(framesWritten: 477_952, elapsedSeconds: 10,
                                                incomingFrames: 2048, sampleRate: 48_000) == 0)
        // A little jitter is not a gap.
        #expect(RecordingClock.silentFramesOwed(framesWritten: 470_000, elapsedSeconds: 10,
                                                incomingFrames: 2048, sampleRate: 48_000) == 0)
    }

    @Test func aGapIsOwedAsSilence() {
        // Five seconds of nothing, then a buffer.
        let owed = RecordingClock.silentFramesOwed(framesWritten: 48_000, elapsedSeconds: 6,
                                                   incomingFrames: 2048, sampleRate: 48_000)
        #expect(owed == 6 * 48_000 - 2048 - 48_000)
        // Capped, and never negative.
        #expect(RecordingClock.silentFramesOwed(framesWritten: 0, elapsedSeconds: 100_000,
                                                incomingFrames: 0, sampleRate: 1_000)
                == Int64(RecordingClock.longestGapSeconds * 1_000))
        #expect(RecordingClock.silentFramesOwed(framesWritten: 1_000_000, elapsedSeconds: 1,
                                                incomingFrames: 0, sampleRate: 48_000) == 0)
    }

    @Test func aStoppedMicrophoneIsNoticedAfterThreeSeconds() {
        let now = Date()
        #expect(!RecordingClock.hasStalled(lastBufferAt: now.addingTimeInterval(-1), now: now))
        #expect(RecordingClock.hasStalled(lastBufferAt: now.addingTimeInterval(-3.5), now: now))
        #expect(!RecordingClock.hasStalled(lastBufferAt: nil, now: now))
    }

    // MARK: - The sound file

    /// What the session saw on 10/20/2026: the microphone's format changed while a recording was
    /// open (the camera starting), and after a gap the sound carried on at the new rate. The file
    /// must still hold all of it, at the right length, and play.
    @Test func aRecordingSurvivesTheMicrophoneChangingFormatAndAGap() throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("writer-\(UUID().uuidString).m4a")
        defer { try? FileManager.default.removeItem(at: url) }

        let first = try #require(AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 1))
        let second = try #require(AVAudioFormat(standardFormatWithSampleRate: 44_100, channels: 1))
        let start = Date(timeIntervalSince1970: 1_000_000)
        let writer = try SessionAudioWriter(url: url, inputFormat: first, startedAt: start)

        // Two seconds at 48 kHz, delivered on time.
        var clock = 0.0
        for _ in 0..<(2 * 48_000 / 4_800) {
            clock += 0.1
            writer.write(try Self.buffer(format: first, frames: 4_800, amplitude: 0.01),
                         at: start.addingTimeInterval(clock))
        }
        // Three seconds of nothing — the engine stopped — then two seconds at 44.1 kHz.
        clock += 3
        for _ in 0..<(2 * 44_100 / 4_410) {
            clock += 0.1
            writer.write(try Self.buffer(format: second, frames: 4_410, amplitude: 0.01),
                         at: start.addingTimeInterval(clock))
        }
        let seconds = writer.close()

        // Seven seconds of session: 2 + 3 of silence + 2, give or take the converter's edges.
        #expect(abs(seconds - 7) < 0.15, "the file should hold \(seconds)s ≈ 7s")

        let read = try AVAudioFile(forReading: url)
        let played = Double(read.length) / read.processingFormat.sampleRate
        #expect(abs(played - 7) < 0.2, "the finished file plays for \(played)s")

        // And it is not silence: the last stretch, after the format change, has sound in it.
        let tail = try #require(AVAudioPCMBuffer(pcmFormat: read.processingFormat, frameCapacity: 22_050))
        read.framePosition = read.length - 30_000
        try read.read(into: tail, frameCount: 22_050)
        let level = try #require(SoundLevel.levels(of: tail.floatChannelData![0],
                                                   count: Int(tail.frameLength)))
        #expect(level.average > -50, "the sound after the change was written (read \(level.average) dB)")
    }

    @Test func aRecordingThatHeardNothingReportsNoSound() throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("writer-\(UUID().uuidString).m4a")
        defer { try? FileManager.default.removeItem(at: url) }
        let format = try #require(AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 1))
        let writer = try SessionAudioWriter(url: url, inputFormat: format)
        // The engine never delivered: the review must not be told there are minutes of sound.
        #expect(writer.close() == 0)
        // And nothing more is written once it is closed.
        writer.write(try Self.buffer(format: format, frames: 4_800, amplitude: 0.5))
        #expect(writer.secondsWritten == 0)
    }

    @Test func theMetersBufferIsNeverBoosted() throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("writer-\(UUID().uuidString).m4a")
        defer { try? FileManager.default.removeItem(at: url) }
        let format = try #require(AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 1))
        let writer = try SessionAudioWriter(url: url, inputFormat: format)
        let buffer = try Self.buffer(format: format, frames: 4_800, amplitude: 0.01)
        let before = Array(UnsafeBufferPointer(start: buffer.floatChannelData![0], count: 4_800))
        writer.write(buffer)
        let after = Array(UnsafeBufferPointer(start: buffer.floatChannelData![0], count: 4_800))
        #expect(before == after)
        writer.close()
    }

    // MARK: - Helpers

    static func sine(amplitude: Float, frames: Int, hz: Float = 440, rate: Float = 48_000) -> [Float] {
        (0..<frames).map { amplitude * sin(2 * .pi * hz * Float($0) / rate) }
    }

    static func buffer(format: AVAudioFormat, frames: Int, amplitude: Float) throws -> AVAudioPCMBuffer {
        let buffer = try #require(AVAudioPCMBuffer(pcmFormat: format,
                                                   frameCapacity: AVAudioFrameCount(frames)))
        buffer.frameLength = AVAudioFrameCount(frames)
        let samples = sine(amplitude: amplitude, frames: frames, rate: Float(format.sampleRate))
        for channel in 0..<Int(format.channelCount) {
            buffer.floatChannelData![channel].update(from: samples, count: frames)
        }
        return buffer
    }
}
