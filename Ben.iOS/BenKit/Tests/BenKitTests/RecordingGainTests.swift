import Foundation
import Testing
@testable import BenKit

/// The boost the session's sound file is written with (Ben, 2026-09-27: "super quiet").
///
/// What matters: quiet sound comes back louder by exactly the stated amount, silence stays
/// silence, and nothing — however loud — is written past full scale.
@Suite("Recording gain")
struct RecordingGainTests {

    @Test func quietSoundIsBoostedByExactlyTheStatedAmount() {
        let quiet: Float = 0.01
        let boosted = RecordingGain.apply(quiet)
        let decibels = 20 * log10(boosted / quiet)
        #expect(abs(decibels - RecordingGain.decibels) < 0.001)
    }

    @Test func silenceStaysSilent() {
        #expect(RecordingGain.apply(0) == 0)
    }

    @Test func signIsKept() {
        #expect(RecordingGain.apply(-0.01) == -RecordingGain.apply(0.01))
        #expect(RecordingGain.apply(-0.5) == -RecordingGain.apply(0.5))
    }

    @Test func nothingGoesPastFullScale() {
        for sample: Float in [0.1, 0.2, 0.5, 1, 4, 1_000, .greatestFiniteMagnitude] {
            let out = RecordingGain.apply(sample)
            #expect(out <= 1, "\(sample) came out at \(out)")
            #expect(RecordingGain.apply(-sample) >= -1)
        }
        // Loud but not absurd stays clear of the ceiling, so it is shaped rather than flattened.
        #expect(RecordingGain.apply(0.11) < 0.9)
    }

    @Test func louderInStaysLouderOut() {
        // A limiter that folded back over would turn a slam quieter than a knock.
        var previous: Float = 0
        for step in 1...200 {
            let out = RecordingGain.apply(Float(step) / 100)
            #expect(out >= previous)
            previous = out
        }
    }

    @Test func theCurveHasNoStepAtTheKnee() {
        let atKnee = RecordingGain.knee / RecordingGain.linear
        let justBelow = RecordingGain.apply(atKnee * 0.9999)
        let justAbove = RecordingGain.apply(atKnee * 1.0001)
        #expect(abs(justAbove - justBelow) < 0.001)
    }

    @Test func aBufferIsBoostedInPlace() {
        var samples: [Float] = [0, 0.01, -0.01, 2]
        samples.withUnsafeMutableBufferPointer { buffer in
            RecordingGain.apply(to: buffer.baseAddress!, count: buffer.count)
        }
        #expect(samples[0] == 0)
        #expect(samples[1] == RecordingGain.apply(0.01))
        #expect(samples[2] == RecordingGain.apply(-0.01))
        #expect(samples[3] <= 1)
    }
}
