import Foundation

/// What a session is actually recording.
///
/// An investigator decides this per session and can change it mid-session: magnetic field alone
/// while walking a property, add audio when they settle in a room, add video when something is
/// worth watching. Every channel costs battery and storage, so nothing runs that was not asked
/// for — a five-hour session with video on is a different proposition from one logging field
/// strength every two seconds.
public struct CaptureChannels: OptionSet, Sendable, Codable, Equatable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    /// The magnetometer. Cheap, and the reason most sessions exist.
    public static let magnetic = CaptureChannels(rawValue: 1 << 0)
    /// Sound level metering, and the audio recording that rides on the same tap.
    public static let audio = CaptureChannels(rawValue: 1 << 1)
    /// Continuous video.
    public static let video = CaptureChannels(rawValue: 1 << 2)
    /// Position and heading, stamped onto every reading.
    public static let location = CaptureChannels(rawValue: 1 << 3)

    /// What a session starts with: field, sound and position. Video is opt-in because it is the
    /// one that will drain a battery before the night is over.
    public static let `default`: CaptureChannels = [.magnetic, .audio, .location]

    public static let all: CaptureChannels = [.magnetic, .audio, .video, .location]

    public var title: String {
        switch self {
        case .magnetic: "Magnetic field"
        case .audio: "Audio"
        case .video: "Video"
        case .location: "Location"
        default: "Channels"
        }
    }

    public var icon: String {
        switch self {
        case .magnetic: "gauge.with.needle"
        case .audio: "waveform"
        case .video: "video"
        case .location: "location"
        default: "dot.radiowaves.left.and.right"
        }
    }

    /// What switching it on actually costs, said plainly rather than left for somebody to
    /// discover at 4am with a dead phone.
    public var costNote: String {
        switch self {
        case .magnetic: "Barely touches the battery."
        case .audio: "Records sound and lets you mark EVP questions."
        case .video: "Heavy — expect a few hours of battery, and a lot of storage."
        case .location: "Stamps where you were on every reading. Poor indoors."
        default: ""
        }
    }

    public static let orderedForDisplay: [CaptureChannels] = [.magnetic, .audio, .video, .location]
}
