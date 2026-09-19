import Foundation

/// How the camera behaves for a whole session, when the video channel is on.
///
/// **Why this is a choice and not a default.** Ben, 2026-09-12: continuous video is what a phone
/// left watching a room is for, and clips around the moments something happened are what anybody
/// actually reviews. The two ways of producing those clips cost very different amounts of battery
/// in the field, and which trade is right depends on the night — a two-hour sit with a charger in
/// the bag is not a six-hour vigil in a cellar. So the person chooses, and each choice says what
/// it costs before it is made.
public enum VideoRecordingMode: String, Sendable, Equatable, CaseIterable, Codable, Identifiable {

    /// No continuous recording. The Video button records a clip while it is held on screen, which
    /// is what the app did before continuous recording existed.
    case clipsByHand

    /// One continuous recording. Every automatic trigger — movement seen, a magnetic spike, a
    /// sound — leaves a mark on the timeline, and a mark can be turned into a clip afterwards.
    case continuousClipLater

    /// One continuous recording, and a clip written as each trigger happens.
    case continuousClipLive

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .clipsByHand:        "Clips I record myself"
        case .continuousClipLater: "Record everything, clip it later"
        case .continuousClipLive:  "Record everything, clip as it happens"
        }
    }

    /// What it does, in one line.
    public var summary: String {
        switch self {
        case .clipsByHand:
            "The Video button records a clip. Nothing runs between clips."
        case .continuousClipLater:
            "The camera runs for the whole session. Anything detected leaves a mark, and you turn "
            + "the marks you care about into clips when you review it."
        case .continuousClipLive:
            "The camera runs for the whole session, and a clip is written the moment something is "
            + "detected — including the seconds before it."
        }
    }

    /// What it costs, said plainly, because this is the decision battery life turns on.
    ///
    /// The figures are deliberately given as a comparison rather than as hours: hours depend on
    /// the phone, its age, the cold, the screen and whether the torch is on, and a number that
    /// turns out to be wrong at 4am is worse than no number.
    public var batteryNote: String {
        switch self {
        case .clipsByHand:
            "Cheapest by a long way. The camera is off except while you are recording."
        case .continuousClipLater:
            "The camera and the encoder run all night, which is the largest single drain the app "
            + "has — expect roughly a quarter to a third of a charge an hour, and a warm phone. "
            + "Making the clips afterwards costs nothing in the field: that work happens when you "
            + "review, usually plugged in."
        case .continuousClipLive:
            "Everything above, plus a second encode each time something is detected — while the "
            + "camera is still running. On a busy night that is the heaviest thing the app can do, "
            + "and a hot phone throttles. Worth it when you need the clips on the phone before you "
            + "get home, or when you want to delete the long recording and keep only the moments."
        }
    }

    /// Whether the camera runs for the whole session.
    public var isContinuous: Bool { self != .clipsByHand }

    /// Whether a trigger writes its clip while the session is still running.
    public var clipsWhileRecording: Bool { self == .continuousClipLive }

    /// What a session does unless somebody says otherwise.
    ///
    /// The cheap one. A person who turned video on without reading anything gets the behaviour
    /// that cannot flatten their phone before the night is over.
    public static let `default`: VideoRecordingMode = .clipsByHand
}
