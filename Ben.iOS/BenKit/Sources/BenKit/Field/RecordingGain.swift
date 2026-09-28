import Foundation

/// The steady boost a session's own sound recording is written with.
///
/// Ben, 2026-09-27: "when recording audio it is super quiet like the mic is turned way down." It
/// was, in effect. The microphone runs in the audio session's `.measurement` mode, which turns off
/// the phone's automatic gain and voice processing so the sound meter reads what the room is
/// really doing. That is right for the meter and wrong for the file: a voice across a room lands
/// far below where anybody can hear it back.
///
/// **A fixed boost, not the phone's automatic gain.** Ben chose this (2026-09-27). Automatic gain
/// turns the hiss up in every quiet stretch and down again the moment something is said, and that
/// swell in the noise floor is exactly the kind of thing that gets played back and called a voice.
/// A fixed boost raises everything by the same amount for the whole night, so a sound heard at
/// 3:12 and one heard at 3:40 can be compared honestly. The meter keeps reading the raw input; only
/// what is written to the file is boosted.
///
/// Loud sounds are rounded off rather than clipped: below `knee` the boost is exactly linear, and
/// above it the curve bends towards full scale and never goes past it. A door slam comes back as a
/// loud, slightly softened thud instead of a burst of distortion.
public enum RecordingGain {
    /// How much louder the file is than the raw microphone. The first thing to adjust if a real
    /// phone still records too quietly or too hot: it is the only number here.
    public static let decibels: Float = 18

    /// `decibels` as a multiplier.
    public static var linear: Float { pow(10, decibels / 20) }

    /// Where the limiter starts bending, as a fraction of full scale.
    public static let knee: Float = 0.8

    /// One sample, boosted and limited. Never outside -1...1.
    public static func apply(_ sample: Float, gain: Float = linear) -> Float {
        let boosted = sample * gain
        let magnitude = abs(boosted)
        guard magnitude > knee else { return boosted }
        // Past the knee: tanh has slope 1 at zero, so the curve joins the linear part without a
        // corner, and it approaches the remaining headroom without crossing it.
        let headroom = 1 - knee
        let limited = knee + headroom * tanh((magnitude - knee) / headroom)
        return boosted < 0 ? -limited : limited
    }

    /// A run of samples, in place.
    public static func apply(to samples: UnsafeMutablePointer<Float>, count: Int,
                             gain: Float = linear) {
        for index in 0..<count {
            samples[index] = apply(samples[index], gain: gain)
        }
    }
}
