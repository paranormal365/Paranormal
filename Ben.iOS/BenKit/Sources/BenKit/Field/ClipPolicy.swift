import Foundation

/// How a moment becomes a clip.
///
/// **The seconds before the trigger are the point.** Anything that detects movement, a magnetic
/// spike or a sound detects it after it began, so a clip that starts at the trigger opens on the
/// aftermath. The lead-in is what makes a clip worth watching, and it is the one thing a person
/// cannot add later if it was never recorded.
public struct ClipPolicy: Sendable, Equatable {

    /// Seconds of recording kept BEFORE the trigger.
    public var leadIn: TimeInterval
    /// Seconds kept after it.
    public var leadOut: TimeInterval
    /// The longest a single clip may run, however many triggers land inside it.
    ///
    /// A clip that keeps growing stops being a clip. On a night where something is detected every
    /// few seconds the merge below would otherwise produce one clip the length of the session,
    /// which is the whole recording under a different name.
    public var maximum: TimeInterval

    public init(leadIn: TimeInterval = 8, leadOut: TimeInterval = 15, maximum: TimeInterval = 90) {
        self.leadIn = leadIn
        self.leadOut = leadOut
        self.maximum = maximum
    }

    public static let `default` = ClipPolicy()
}

/// One clip to cut out of a recording.
public struct ClipRequest: Sendable, Equatable, Identifiable {
    /// Seconds into the recording where the clip starts.
    public var startOffset: TimeInterval
    public var duration: TimeInterval
    /// The moments that put this clip here, in order. More than one when they were close enough
    /// to be a single stretch of something happening.
    public var triggers: [Date]

    public var id: String { "\(startOffset)-\(duration)" }
    public var endOffset: TimeInterval { startOffset + duration }

    public init(startOffset: TimeInterval, duration: TimeInterval, triggers: [Date]) {
        self.startOffset = startOffset
        self.duration = duration
        self.triggers = triggers
    }
}

public enum ClipPlanner {

    /// Works out which clips a set of trigger moments asks for.
    ///
    /// Triggers whose windows touch become ONE clip rather than several overlapping ones: a door
    /// opening, a footstep and a temperature mark in the same ten seconds are one event to whoever
    /// watches it, and three near-identical clips of the same ten seconds waste both the export
    /// and the reviewer's time.
    ///
    /// - Parameters:
    ///   - triggers: when each detection happened, in any order.
    ///   - recordingStart: when the recording itself began.
    ///   - recordingDuration: how long it runs. Clips are clamped inside it, because a clip cannot
    ///     contain footage that was never recorded.
    public static func plan(triggers: [Date],
                            recordingStart: Date,
                            recordingDuration: TimeInterval,
                            policy: ClipPolicy = .default) -> [ClipRequest] {
        guard recordingDuration > 0 else { return [] }

        // Only triggers that fall inside the recording can be clipped out of it. One that fired
        // before the camera started, or after it stopped, has no footage behind it.
        let inside = triggers
            .map { $0.timeIntervalSince(recordingStart) }
            .filter { $0 >= 0 && $0 <= recordingDuration }
            .sorted()
        guard !inside.isEmpty else { return [] }

        var clips: [ClipRequest] = []
        var start = max(0, inside[0] - policy.leadIn)
        var end = min(recordingDuration, inside[0] + policy.leadOut)
        var moments = [inside[0]]

        for offset in inside.dropFirst() {
            let wouldStart = max(0, offset - policy.leadIn)
            let wouldEnd = min(recordingDuration, offset + policy.leadOut)

            // Touching, and still short enough to be one clip.
            if wouldStart <= end && (wouldEnd - start) <= policy.maximum {
                end = max(end, wouldEnd)
                moments.append(offset)
                continue
            }

            clips.append(ClipRequest(startOffset: start, duration: end - start,
                                     triggers: moments.map { recordingStart.addingTimeInterval($0) }))
            start = wouldStart
            end = wouldEnd
            moments = [offset]
        }

        clips.append(ClipRequest(startOffset: start, duration: end - start,
                                 triggers: moments.map { recordingStart.addingTimeInterval($0) }))

        // A clip of nothing is not a clip. Reachable when a trigger lands exactly on the last
        // frame and the lead-in is zero.
        return clips.filter { $0.duration > 0 }
    }
}
