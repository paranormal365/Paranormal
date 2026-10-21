import Foundation
import AVFoundation
import os

/// Writes the session's sound to an AAC file, buffer by buffer, from the microphone's tap.
///
/// **Why this is its own type (10/21/2026).** Ben recorded a session with video, sound and
/// questions, and the sound didn't play back in the review. Writing had three ways to go wrong
/// without saying anything, and all three are handled here where a test can reach them:
///
/// 1. **The microphone's format changing under an open file.** The camera starting, or dictation
///    opening the microphone, can change the audio hardware's sample rate. The file keeps the format
///    it was opened with, and `AVAudioFile.write` refuses a buffer in any other — the old code used
///    `try?`, so every buffer after the change was dropped and the file held a second or two of
///    sound. Buffers are now converted to the file's own format when they differ.
/// 2. **Gaps.** When the microphone stops and comes back, the next sound used to be written straight
///    after the last, so everything after the gap played early against the session's clock. The
///    gap is written as silence (`RecordingClock`).
/// 3. **The length reported.** A recording was listed with the time it was open, not the sound it
///    held, so the review could place minutes of "recording" over a file of a few seconds and
///    play nothing. `close()` reports the sound actually in the file.
public final class SessionAudioWriter: @unchecked Sendable {
    private static let log = Logger(subsystem: "com.ishaunted.ios", category: "microphone")

    private let lock = NSLock()
    private var file: AVAudioFile?
    private var converter: AVAudioConverter?
    private var framesWritten: Int64 = 0
    /// Buffers that could not be written, kept for the log.
    private var failures = 0

    public let startedAt: Date
    /// The format the file is written in: float samples at the microphone's rate when it opened.
    public let format: AVAudioFormat
    /// How loud the file is written relative to the microphone. See `RecordingGain`.
    private let gain: Float

    /// Opens `url` for writing, in the microphone's `inputFormat` (one or two channels).
    public init(url: URL, inputFormat: AVAudioFormat, startedAt: Date = Date(),
                gain: Float = RecordingGain.linear) throws {
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatMPEG4AAC,
            AVSampleRateKey: inputFormat.sampleRate,
            AVNumberOfChannelsKey: max(1, min(2, Int(inputFormat.channelCount))),
            AVEncoderAudioQualityKey: AVAudioQuality.high.rawValue,
        ]
        let created = try AVAudioFile(forWriting: url, settings: settings)
        file = created
        format = created.processingFormat
        self.startedAt = startedAt
        self.gain = gain
    }

    /// How much sound has been written so far.
    public var secondsWritten: TimeInterval {
        lock.lock(); defer { lock.unlock() }
        return RecordingClock.seconds(frames: framesWritten, sampleRate: format.sampleRate)
    }

    /// Writes one buffer heard at `now` (the moment its last sample arrived).
    public func write(_ buffer: AVAudioPCMBuffer, at now: Date = Date()) {
        lock.lock(); defer { lock.unlock() }
        guard let file else { return }

        guard let ready = conformed(buffer) else {
            failures += 1
            Self.log.error("A buffer couldn't be converted to the recording's format.")
            return
        }
        boost(ready)

        let owed = RecordingClock.silentFramesOwed(
            framesWritten: framesWritten, elapsedSeconds: now.timeIntervalSince(startedAt),
            incomingFrames: Int(ready.frameLength), sampleRate: format.sampleRate)
        if owed > 0 { framesWritten += writeSilence(owed, to: file) }

        do {
            try file.write(from: ready)
            framesWritten += Int64(ready.frameLength)
        } catch {
            failures += 1
            Self.log.error("A buffer couldn't be written: \(error.localizedDescription, privacy: .public)")
        }
    }

    /// Finishes the file — releasing it is what writes the container's index — and reports how
    /// many seconds of sound it holds. Nothing is written after this.
    @discardableResult
    public func close() -> TimeInterval {
        lock.lock(); defer { lock.unlock() }
        guard let file else { return 0 }
        let frames = max(file.length, framesWritten)
        self.file = nil
        converter = nil
        if failures > 0 {
            Self.log.error("\(self.failures) buffers couldn't be written to this recording.")
        }
        return RecordingClock.seconds(frames: frames, sampleRate: format.sampleRate)
    }

    // MARK: - Getting a buffer into the file's format

    /// A copy when the formats match — the boost must never touch the buffer the meter reads —
    /// and a conversion when the microphone's format has changed since the file opened.
    private func conformed(_ buffer: AVAudioPCMBuffer) -> AVAudioPCMBuffer? {
        guard buffer.frameLength > 0 else { return nil }
        if buffer.format == format { return Self.copy(buffer) }

        if converter?.inputFormat != buffer.format {
            converter = AVAudioConverter(from: buffer.format, to: format)
        }
        guard let converter else { return nil }
        let ratio = format.sampleRate / buffer.format.sampleRate
        let capacity = AVAudioFrameCount((Double(buffer.frameLength) * ratio).rounded(.up)) + 64
        guard let output = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else { return nil }

        let handedOver = OSAllocatedUnfairLock(initialState: false)
        var error: NSError?
        let status = converter.convert(to: output, error: &error) { _, inputStatus in
            let first = handedOver.withLock { done -> Bool in
                defer { done = true }
                return !done
            }
            inputStatus.pointee = first ? .haveData : .noDataNow
            return first ? buffer : nil
        }
        guard status != .error, output.frameLength > 0 else { return nil }
        return output
    }

    private static func copy(_ buffer: AVAudioPCMBuffer) -> AVAudioPCMBuffer? {
        guard let source = buffer.floatChannelData,
              let copy = AVAudioPCMBuffer(pcmFormat: buffer.format, frameCapacity: buffer.frameLength),
              let destination = copy.floatChannelData
        else { return nil }
        copy.frameLength = buffer.frameLength
        for run in 0..<runs(buffer.format) {
            destination[run].update(from: source[run], count: samplesPerRun(buffer.format, frames: buffer.frameLength))
        }
        return copy
    }

    /// `RecordingGain`, in place.
    ///
    /// Ben, 2026-09-27: recordings were "super quiet like the mic is turned way down". The
    /// `.measurement` mode that keeps the meter honest also switches off the phone's own gain, so
    /// the file is boosted instead. The meter keeps reading the raw room.
    private func boost(_ buffer: AVAudioPCMBuffer) {
        guard let data = buffer.floatChannelData else { return }
        let count = Self.samplesPerRun(buffer.format, frames: buffer.frameLength)
        for run in 0..<Self.runs(buffer.format) {
            RecordingGain.apply(to: data[run], count: count, gain: gain)
        }
    }

    /// Writes `frames` of silence in modest pieces, and reports how many were written.
    private func writeSilence(_ frames: Int64, to file: AVAudioFile) -> Int64 {
        let piece: AVAudioFrameCount = 8_192
        guard let zeros = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: piece),
              let data = zeros.floatChannelData else { return 0 }
        let perRun = Self.samplesPerRun(format, frames: piece)
        for run in 0..<Self.runs(format) { data[run].initialize(repeating: 0, count: perRun) }

        var remaining = frames
        var written: Int64 = 0
        while remaining > 0 {
            zeros.frameLength = AVAudioFrameCount(min(Int64(piece), remaining))
            do { try file.write(from: zeros) } catch { break }
            written += Int64(zeros.frameLength)
            remaining -= Int64(zeros.frameLength)
        }
        Self.log.notice("Wrote \(written) frames of silence over a gap in the microphone.")
        return written
    }

    /// Interleaved float audio is one run of samples; deinterleaved is one run per channel.
    private static func runs(_ format: AVAudioFormat) -> Int {
        format.isInterleaved ? 1 : Int(format.channelCount)
    }

    private static func samplesPerRun(_ format: AVAudioFormat, frames: AVAudioFrameCount) -> Int {
        Int(frames) * (format.isInterleaved ? Int(format.channelCount) : 1)
    }
}
