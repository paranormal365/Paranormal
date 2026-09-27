import Foundation

/// "Motion detected" on the replay.
///
/// Ben, 2026-09-27: "It will highlight the video and maybe show a 'Motion Detected' sign when the
/// motion is detected and being played back during the .ben file playback." The detection happens
/// while recording — Watch for Motion, the camera judged only while the phone is still — and is
/// written into the session as a mark. Playback reads the mark; nothing is re-analysed, so the
/// phone and the website show the same moments.
public enum ReplayMotion {
    /// How long the sign stays up, and the video stays highlighted, after the moment.
    public static let signSeconds: TimeInterval = 3

    /// The motion mark whose sign is up at `moment`, if any — the latest one within `signSeconds`.
    public static func showing(_ markers: [FieldMarkerRecord], at moment: Date) -> FieldMarkerRecord? {
        markers
            .filter { $0.kind == .sceneMotion && $0.at <= moment
                      && moment.timeIntervalSince($0.at) < signSeconds }
            .max { $0.at < $1.at }
    }
}
