import Foundation

/// How loud a stretch of sound is, in dBFS: decibels below full scale, so 0 is the loudest a
/// microphone can report and every real room reads below it.
///
/// **Why the floor is -100 and not -60 (10/21/2026).** Ben recorded a session with video, sound
/// and questions, and the sound reading "never showed more than -60 dB". The meter used to stop
/// at -60: anything quieter was reported as -60. The microphone runs in the audio session's
/// `.measurement` mode, which switches off the phone's own gain so the reading is the room and
/// not the phone's guess at it, and in that mode a quiet room sits around -80 and an ordinary
/// voice across a room around -60 to -50. So the old floor swallowed nearly everything said in
/// the room. The floor is now low enough to be below any real room; it only exists so that true
/// silence (all zeros) gives a number instead of negative infinity.
public enum SoundLevel {
    /// What silence reads as. Below any room a phone microphone can hear.
    public static let floorDbfs: Double = -100

    /// One amplitude (0...1 of full scale) in dBFS, never below the floor.
    public static func decibels(_ amplitude: Float) -> Double {
        guard amplitude > 0, amplitude.isFinite else { return floorDbfs }
        return max(floorDbfs, min(0, Double(20 * log10(amplitude))))
    }

    /// RMS and peak of `count` samples, read every `stride` samples (the channel count, for
    /// interleaved audio, so only the first channel is measured). Nil when there is nothing to read.
    public static func levels(of samples: UnsafePointer<Float>, count: Int,
                              stride: Int = 1) -> (average: Double, peak: Double)? {
        let step = max(1, stride)
        guard count > 0 else { return nil }
        var sumOfSquares: Double = 0
        var peak: Float = 0
        var read = 0
        var index = 0
        while index < count {
            let value = samples[index]
            if value.isFinite {
                sumOfSquares += Double(value) * Double(value)
                peak = max(peak, abs(value))
            }
            read += 1
            index += step
        }
        guard read > 0 else { return nil }
        let rms = Float((sumOfSquares / Double(read)).squareRoot())
        return (decibels(rms), decibels(peak))
    }

    /// The same, for an array — what the tests and the fakes use.
    public static func levels(of samples: [Float], stride: Int = 1) -> (average: Double, peak: Double)? {
        samples.withUnsafeBufferPointer { buffer in
            guard let base = buffer.baseAddress else { return nil }
            return levels(of: base, count: buffer.count, stride: stride)
        }
    }
}

/// Keeps a sound file's own clock in step with the session's.
///
/// The replay places a recording at the moment it started and plays it from there, so a second
/// of audio has to be a second of the session. If the microphone stops delivering for a while
/// (the camera reconfiguring the audio hardware, the system restarting the engine) and then comes
/// back, writing the next sound straight after the last would pull everything after the gap
/// earlier than it happened. The gap is written as silence instead, so the file stays honest
/// about when each sound was heard.
public enum RecordingClock {
    /// How far behind the file may fall before silence is written: buffers arrive in bursts of a
    /// few tens of milliseconds, and that jitter is not a gap.
    public static let toleranceSeconds: Double = 0.5

    /// The most silence written for one gap. Past this the gap is still marked by the session's
    /// own notes; an hour of zeros is not worth writing.
    public static let longestGapSeconds: Double = 3_600

    /// How many silent frames to write before a buffer of `incomingFrames`, given how many frames
    /// the file already holds and how long ago it started.
    public static func silentFramesOwed(framesWritten: Int64, elapsedSeconds: Double,
                                        incomingFrames: Int, sampleRate: Double) -> Int64 {
        guard sampleRate > 0, elapsedSeconds.isFinite, elapsedSeconds > 0 else { return 0 }
        // The incoming buffer ends about now, so it starts `incomingFrames` before now.
        let expected = Int64(elapsedSeconds * sampleRate) - Int64(incomingFrames)
        let behind = expected - framesWritten
        guard Double(behind) > toleranceSeconds * sampleRate else { return 0 }
        return min(behind, Int64(longestGapSeconds * sampleRate))
    }

    /// How long a file holding `frames` frames plays for.
    public static func seconds(frames: Int64, sampleRate: Double) -> TimeInterval {
        guard sampleRate > 0, frames > 0 else { return 0 }
        return Double(frames) / sampleRate
    }

    /// Whether the microphone has gone quiet for long enough that it has stopped, rather than
    /// paused between buffers. A buffer comes about every 50 ms; three seconds without one is a
    /// stopped engine.
    public static let stallSeconds: Double = 3

    public static func hasStalled(lastBufferAt: Date?, now: Date) -> Bool {
        guard let lastBufferAt else { return false }
        return now.timeIntervalSince(lastBufferAt) >= stallSeconds
    }
}
