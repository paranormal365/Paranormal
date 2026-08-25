import Foundation

/// One stretch of recorded media inside a session.
///
/// Audio and video are CLIPS, not a continuous stream covering the night, so a replay playhead
/// spends much of its time over nothing. That is a fact about the recording, and the timeline
/// shows it rather than leaving somebody to conclude the player is broken.
public struct MediaSegment: Sendable, Equatable, Identifiable {
    public var id: UUID
    public var kind: CaptureKind
    public var relativePath: String
    public var startedAt: Date
    public var duration: TimeInterval

    public init(id: UUID = UUID(), kind: CaptureKind, relativePath: String,
                startedAt: Date, duration: TimeInterval) {
        self.id = id
        self.kind = kind
        self.relativePath = relativePath
        self.startedAt = startedAt
        self.duration = duration
    }

    public var endsAt: Date { startedAt.addingTimeInterval(duration) }

    public func covers(_ moment: Date) -> Bool {
        moment >= startedAt && moment < endsAt
    }

    /// How far into the file a given moment falls.
    public func offset(at moment: Date) -> TimeInterval? {
        guard covers(moment) else { return nil }
        return moment.timeIntervalSince(startedAt)
    }
}

/// Everything a session recorded, arranged on one clock so it can be replayed together.
public struct ReplayTimeline: Sendable, Equatable {
    public var startedAt: Date
    public var endedAt: Date
    public var readings: [FieldReading]
    public var markers: [FieldMarkerRecord]
    public var media: [MediaSegment]
    public var baselines: Baselines

    public init(startedAt: Date, endedAt: Date, readings: [FieldReading],
                markers: [FieldMarkerRecord], media: [MediaSegment], baselines: Baselines) {
        self.startedAt = startedAt
        self.endedAt = endedAt
        self.readings = readings
        self.markers = markers
        self.media = media
        self.baselines = baselines
    }

    public var duration: TimeInterval { max(0, endedAt.timeIntervalSince(startedAt)) }

    public static let empty = ReplayTimeline(
        startedAt: .distantPast, endedAt: .distantPast,
        readings: [], markers: [], media: [], baselines: Baselines())

    /// Where the playhead sits, 0…1.
    public func fraction(of moment: Date) -> Double {
        guard duration > 0 else { return 0 }
        return (moment.timeIntervalSince(startedAt) / duration).clampedToUnitInterval
    }

    public func moment(atFraction fraction: Double) -> Date {
        startedAt.addingTimeInterval(duration * fraction.clampedToUnitInterval)
    }

    /// Every position fix, in order — the path a movement map draws.
    public var track: [(at: Date, position: FieldReading.Position)] {
        readings.compactMap { reading in
            guard let position = reading.position,
                  position.latitude != nil, position.longitude != nil
            else { return nil }
            return (reading.at, position)
        }
    }
}

/// What the instruments read at one moment of a replay.
public struct ReplayFrame: Sendable, Equatable {
    public var at: Date
    public var magneticMicrotesla: Double?
    public var soundDbfs: Double?
    public var position: FieldReading.Position?
    public var headingDegrees: Double?
    public var relativeAltitudeMeters: Double?
    /// The clip covering this moment, and how far into it — nil when nothing was recorded here.
    public var activeMedia: (segment: MediaSegment, offset: TimeInterval)?
    /// A marker within a second or so of the playhead, for highlighting as it passes.
    public var nearestMarker: FieldMarkerRecord?

    public init(at: Date) { self.at = at }

    public static func == (lhs: ReplayFrame, rhs: ReplayFrame) -> Bool {
        lhs.at == rhs.at
            && lhs.magneticMicrotesla == rhs.magneticMicrotesla
            && lhs.soundDbfs == rhs.soundDbfs
            && lhs.position == rhs.position
            && lhs.headingDegrees == rhs.headingDegrees
            && lhs.activeMedia?.segment.id == rhs.activeMedia?.segment.id
            && lhs.nearestMarker?.id == rhs.nearestMarker?.id
    }

    public func magneticDeviationMilligauss(from baselines: Baselines) -> Double? {
        guard let now = magneticMicrotesla, let base = baselines.magneticMicrotesla else { return nil }
        return (now - base) * 10
    }
}

/// Replays a finished session: one playhead driving the trace, the map, the compass and the
/// media at once.
///
/// The rule everywhere here is that a replay shows what was ACTUALLY recorded. A reading is
/// carried forward until the next one rather than invented in between — instruments hold their
/// last value, they do not interpolate — while POSITION is interpolated, because somebody
/// walking between two fixes really was somewhere in between. Getting that backwards would draw
/// a smooth field trace that never happened, or a person teleporting between corners.
@Observable
@MainActor
public final class SessionReplay {

    public private(set) var timeline: ReplayTimeline = .empty
    public private(set) var playhead: Date = .distantPast
    public private(set) var frame = ReplayFrame(at: .distantPast)
    public private(set) var isPlaying = false
    public private(set) var isLoaded = false
    public private(set) var problem: String?

    /// Playback speed. A five-hour vigil is not watched at 1×.
    ///
    /// Clamped through a method rather than a `didSet`: under `@Observable` the property becomes
    /// computed, so a `didSet` that assigns to its own property recurses until the stack gives
    /// out. That is a crash on the very first speed change, and it was found here rather than
    /// on a phone because these tests drive it.
    public private(set) var rate: Double = 1

    public static let rateRange: ClosedRange<Double> = 0.5...64

    public func setRate(_ value: Double) {
        rate = min(max(value, Self.rateRange.lowerBound), Self.rateRange.upperBound)
    }

    private var ticker: Task<Void, Never>?
    private let tickHz: Double = 10

    public init() {}

    // MARK: - Loading

    public func load(readingLog: ReadingLog, markers: [FieldMarkerRecord],
                     media: [MediaSegment], baselines: Baselines,
                     startedAt: Date, endedAt: Date?) async {
        do {
            let readings = try await readingLog.readings()

            // A session interrupted mid-recording has no honest end time, so the last thing it
            // actually recorded becomes the end of the replay rather than a guess.
            let lastMoment = [readings.last?.at,
                              markers.map(\.at).max(),
                              media.map(\.endsAt).max()].compactMap { $0 }.max()
            let end = endedAt ?? lastMoment ?? startedAt

            timeline = ReplayTimeline(
                startedAt: startedAt, endedAt: max(end, startedAt),
                readings: readings.sorted { $0.at < $1.at },
                markers: markers.sorted { $0.at < $1.at },
                media: media.sorted { $0.startedAt < $1.startedAt },
                baselines: baselines)
            playhead = timeline.startedAt
            frame = makeFrame(at: playhead)
            isLoaded = true
            problem = nil
        } catch {
            problem = "This session's readings couldn't be read: \(error.localizedDescription)"
            isLoaded = true
        }
    }

    // MARK: - Transport

    public func play() {
        guard isLoaded, !isPlaying, timeline.duration > 0 else { return }
        if playhead >= timeline.endedAt { seek(to: timeline.startedAt) }
        isPlaying = true

        ticker = Task { [weak self] in
            let step = 1 / (self?.tickHz ?? 10)
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(step))
                guard let self, self.isPlaying else { return }
                let next = self.playhead.addingTimeInterval(step * self.rate)
                if next >= self.timeline.endedAt {
                    self.seek(to: self.timeline.endedAt)
                    self.pause()
                    return
                }
                self.seek(to: next)
            }
        }
    }

    public func pause() {
        isPlaying = false
        ticker?.cancel()
        ticker = nil
    }

    public func togglePlaying() { isPlaying ? pause() : play() }

    public func seek(to moment: Date) {
        let clamped = min(max(moment, timeline.startedAt), timeline.endedAt)
        playhead = clamped
        frame = makeFrame(at: clamped)
    }

    public func seek(fraction: Double) {
        seek(to: timeline.moment(atFraction: fraction))
    }

    /// Jumps to a marker — the reason most people open a replay at all.
    public func seek(to marker: FieldMarkerRecord) {
        // A moment BEFORE the marker, so whatever led up to it can be heard rather than
        // starting at the bang.
        seek(to: marker.at.addingTimeInterval(-Self.markerLeadIn))
    }

    /// The lead-in `seek(to: marker)` gives, so stepping can tell "parked before marker A" from
    /// "somewhere between A and B".
    private static let markerLeadIn: TimeInterval = 3

    public func stepMarker(forward: Bool) {
        // Seeking to a marker parks the playhead `markerLeadIn` seconds BEFORE it, so the marker
        // we are currently on sits at playhead + leadIn. Stepping forward must skip past it, or
        // it selects the same marker again and nothing moves.
        let current = playhead.addingTimeInterval(Self.markerLeadIn)
        let next = forward
            ? timeline.markers.first { $0.at > current.addingTimeInterval(0.01) }
            : timeline.markers.last { $0.at < current.addingTimeInterval(-0.01) }
        guard let next else { return }
        seek(to: next)
    }

    // MARK: - Deriving a frame

    func makeFrame(at moment: Date) -> ReplayFrame {
        var result = ReplayFrame(at: moment)

        // Instruments HOLD their last value; they do not interpolate. Smoothing a field trace
        // between samples would draw a reading nobody took.
        if let reading = lastReading(at: moment) {
            result.magneticMicrotesla = reading.measurements?["emf"]?.numberValue
            result.soundDbfs = reading.measurements?["sound_level"]?.numberValue
            result.relativeAltitudeMeters = reading.measurements?["relative_altitude"]?.numberValue
            result.headingDegrees = reading.motion?.headingDegrees
        }

        result.position = interpolatedPosition(at: moment)
        result.activeMedia = timeline.media
            .first(where: { $0.covers(moment) })
            .flatMap { segment in segment.offset(at: moment).map { (segment, $0) } }
        result.nearestMarker = timeline.markers.first {
            abs($0.at.timeIntervalSince(moment)) < 1.0
        }
        return result
    }

    private func lastReading(at moment: Date) -> FieldReading? {
        // Binary search: a five-hour session is tens of thousands of readings and this runs ten
        // times a second.
        var low = 0, high = timeline.readings.count - 1, best: FieldReading?
        while low <= high {
            let mid = (low + high) / 2
            let candidate = timeline.readings[mid]
            if candidate.at <= moment {
                best = candidate
                low = mid + 1
            } else {
                high = mid - 1
            }
        }
        return best
    }

    /// Position IS interpolated: somebody walking between two fixes really was somewhere in
    /// between, and a map that teleports them corner to corner is less true, not more.
    private func interpolatedPosition(at moment: Date) -> FieldReading.Position? {
        let track = timeline.track
        guard !track.isEmpty else { return nil }
        guard let firstAfter = track.firstIndex(where: { $0.at > moment }) else {
            return track.last?.position
        }
        guard firstAfter > 0 else { return track[0].position }

        let before = track[firstAfter - 1]
        let after = track[firstAfter]
        let span = after.at.timeIntervalSince(before.at)
        guard span > 0 else { return before.position }

        // Two fixes minutes apart say nothing about the middle — better to hold the last known
        // place than to draw a straight line across a building somebody walked around.
        guard span <= 30 else { return before.position }

        let t = moment.timeIntervalSince(before.at) / span
        var result = before.position
        if let a = before.position.latitude, let b = after.position.latitude {
            result.latitude = a + (b - a) * t
        }
        if let a = before.position.longitude, let b = after.position.longitude {
            result.longitude = a + (b - a) * t
        }
        // Accuracy is NOT interpolated — it takes the worse of the two, because an interpolated
        // point is at best as trustworthy as the fixes either side of it.
        result.accuracyMeters = [before.position.accuracyMeters, after.position.accuracyMeters]
            .compactMap { $0 }.max()
        return result
    }
}

extension FieldReading.Measurement {
    /// The numeric value, when this measurement carries one.
    public var numberValue: Double? {
        if case .number(let value) = value { return value }
        return nil
    }
}

extension Double {
    var clampedToUnitInterval: Double { min(max(self, 0), 1) }
}
