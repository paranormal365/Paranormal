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

    /// How far BEFORE its first second a clip still counts as covering the playhead.
    ///
    /// The session's clock starts at Start and its sound recording a few milliseconds later, so
    /// every review opened at 0:00 said "No recording at this moment" about a recording that began
    /// there (found walking the review, 2026-09-27). Within a second, the clip is the answer; it is
    /// played from its own start.
    public static let leadTolerance: TimeInterval = 1

    public func covers(_ moment: Date) -> Bool {
        moment >= startedAt.addingTimeInterval(-Self.leadTolerance) && moment < endsAt
    }

    /// How far into the file a given moment falls — never before its start.
    public func offset(at moment: Date) -> TimeInterval? {
        guard covers(moment) else { return nil }
        return max(0, moment.timeIntervalSince(startedAt))
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
    /// Which way they were walking at this moment, from the fix rather than the compass.
    public var courseDegrees: Double?
    /// The direction between the fixes either side of this moment, when they are far enough apart
    /// to mean a walk rather than GPS drift — for the stretches with neither compass nor course
    /// (rule P6, `ProjectNotes/specs/replay-semantics.md`).
    public var trackBearingDegrees: Double?
    public var relativeAltitudeMeters: Double?
    /// The magnetic base in force at this moment: the base on the reading at the playhead, or the
    /// last one written before it (rule I3). A base set again partway through a night applies from
    /// there on; one figure for the whole session was wrong either side of the change.
    public var magneticBaseMicrotesla: Double?

    /// Which way to point the arrow on the map, and what that direction actually means.
    ///
    /// Ben, 2026-09-16: "See if the map is tracked using an arrow to point the direction and how
    /// the person walks." Two different facts answer that — the compass says where they were
    /// looking, the fix says where they were going — and a night usually has only one of them,
    /// because a compass is useless indoors and a course needs a moving fix. Whichever exists is
    /// drawn, and the screen says which it is rather than letting one be read as the other.
    public enum Facing: Sendable, Equatable {
        case looking(Double)
        case walking(Double)
        /// Worked out from the track itself: the bearing from one fix to the next.
        case moving(Double)

        public var degrees: Double {
            switch self {
            case .looking(let value), .walking(let value), .moving(let value): value
            }
        }

        /// What the arrow means, for the label beside it.
        public var what: String {
            switch self {
            case .looking: "looking"
            case .walking: "walking"
            case .moving: "moving"
            }
        }
    }

    /// The compass first: where somebody was pointed is what they were investigating. The
    /// direction of travel stands in when there is no compass reading, which is most of a night,
    /// and the track's own bearing when there is no course either — every session recorded before
    /// the app wrote one.
    public var facing: Facing? {
        if let headingDegrees { return .looking(headingDegrees) }
        if let courseDegrees { return .walking(courseDegrees) }
        if let trackBearingDegrees { return .moving(trackBearingDegrees) }
        return nil
    }
    /// The video covering this moment, and how far into it.
    public var activeVideo: (segment: MediaSegment, offset: TimeInterval)?
    /// The sound recording covering this moment, and how far into it.
    ///
    /// Kept apart from the video because the two now overlap. Ben, 2026-09-27: "Even when
    /// recording video, it doesn't play back." One `activeMedia` took the FIRST clip covering the
    /// playhead, and the session's sound recording starts at Start — so it began first, covered
    /// every video recorded inside it, and the video never reached the screen. A whole-session
    /// video carries no sound of its own, so both play: the picture from one, the sound from the
    /// other.
    public var activeAudio: (segment: MediaSegment, offset: TimeInterval)?
    /// The clip covering this moment — the video when there is one, otherwise the sound. Nil when
    /// nothing was recorded here.
    public var activeMedia: (segment: MediaSegment, offset: TimeInterval)? { activeVideo ?? activeAudio }
    /// A marker within a second or so of the playhead, for highlighting as it passes.
    public var nearestMarker: FieldMarkerRecord?
    /// The "Motion detected" mark whose sign is showing at this moment. See `ReplayMotion`.
    public var motionDetected: FieldMarkerRecord?
    /// The room the operator said they were in at this moment, if they said.
    public var room: String?

    public init(at: Date) { self.at = at }

    public static func == (lhs: ReplayFrame, rhs: ReplayFrame) -> Bool {
        lhs.at == rhs.at
            && lhs.magneticMicrotesla == rhs.magneticMicrotesla
            && lhs.soundDbfs == rhs.soundDbfs
            && lhs.position == rhs.position
            && lhs.headingDegrees == rhs.headingDegrees
            && lhs.courseDegrees == rhs.courseDegrees
            && lhs.trackBearingDegrees == rhs.trackBearingDegrees
            && lhs.magneticBaseMicrotesla == rhs.magneticBaseMicrotesla
            && lhs.activeVideo?.segment.id == rhs.activeVideo?.segment.id
            && lhs.activeAudio?.segment.id == rhs.activeAudio?.segment.id
            && lhs.nearestMarker?.id == rhs.nearestMarker?.id
            && lhs.motionDetected?.id == rhs.motionDetected?.id
            && lhs.room == rhs.room
    }

    /// mG from the base in force at this moment, falling back to the session's base for a
    /// reading that carried none.
    public func magneticDeviationMilligauss(from baselines: Baselines) -> Double? {
        guard let now = magneticMicrotesla,
              let base = magneticBaseMicrotesla ?? baselines.magneticMicrotesla else { return nil }
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
    /// The field line, thinned for drawing — see `FieldTrace`. Worked out once here rather than
    /// by the chart on every tick.
    public private(set) var fieldTrace: FieldTrace = .empty
    /// Every fix in order, the path the map draws. The same once-only rule as the trace: a long
    /// night is thousands of fixes, and the map used to walk every reading for them ten times a
    /// second.
    public private(set) var walkedPath: [FieldReading.Position] = []
    /// The fixes with their times, worked out once at load. `timeline.track` builds the list
    /// afresh, and the frame needs it ten times a second.
    private var track: [(at: Date, position: FieldReading.Position)] = []
    /// The magnetic base in force at each reading, by index (rule I3).
    private var magneticBaseInForce: [Double?] = []
    public private(set) var playhead: Date = .distantPast
    public private(set) var frame = ReplayFrame(at: .distantPast)
    public private(set) var isPlaying = false
    public private(set) var isLoaded = false
    public private(set) var problem: String?
    /// Goes up by one on every seek — a jump, a drag, a mark. The map turns its arrow smoothly
    /// while playing and snaps it on a seek; this is how a view tells the two apart.
    public private(set) var seekCount = 0

    /// Playback speed. A five-hour vigil is not watched at 1×.
    ///
    /// Clamped through a method rather than a `didSet`: under `@Observable` the property becomes
    /// computed, so a `didSet` that assigns to its own property recurses until the stack gives
    /// out. That is a crash on the very first speed change, and it was found here rather than
    /// on a phone because these tests drive it.
    public private(set) var rate: Double = 1

    public static let rateRange: ClosedRange<Double> = 0.5...64

    public func setRate(_ value: Double) {
        // Anchored BEFORE the change, so the time already played keeps the speed it was played
        // at. Anchoring after would re-scale it.
        //
        // From the MEASURED position, not the stored one. The stored playhead is as old as the
        // last tick, and re-anchoring from it rewinds by that much — at 64x, a tenth of a second
        // of ticking is over six seconds of the night, dropped silently on every speed change.
        if isPlaying {
            let here = measuredPlayhead
            moveTo(here)
            reanchor(at: here)
        }
        rate = min(max(value, Self.rateRange.lowerBound), Self.rateRange.upperBound)
    }

    private var ticker: Task<Void, Never>?
    private let tickHz: Double = 10

    // ── Where the playhead is measured FROM ──────────────────────────────────
    //
    // Playback used to advance the playhead by a fixed `step * rate` on every tick, which counts
    // TICKS rather than measuring time. `Task.sleep` promises "at least", never "exactly", so on
    // a busy device the ticks arrive late and the playhead falls behind — a replay labelled 8x
    // silently runs slower than 8x, and the busier the phone the slower it goes. It is
    // one-directional: playback can only ever lag, never race.
    //
    // Found by a test that starved for 400ms under a full parallel suite and saw the playhead
    // not move at all. That was the extreme of the same fault, not a different one.
    //
    // So the playhead is computed from an anchor instead: where it was, when that was, and how
    // fast we are going. However long a tick actually takes, the answer is the same.
    private var anchorPlayhead: Date = .distantPast
    private var anchorAt: ContinuousClock.Instant?

    /// Where the playhead should be, given how much real time has passed since the anchor.
    ///
    /// Static and pure so the arithmetic can be checked without a scheduler — the scheduler is
    /// exactly what cannot be relied on here.
    static func playhead(
        from anchor: Date, elapsed: Duration, rate: Double, endingAt end: Date
    ) -> Date {
        min(anchor.addingTimeInterval(elapsed.inSeconds * rate), end)
    }

    /// Where the playhead is *right now* — further along than the last tick left it, by however
    /// much real time has passed since. Between ticks the stored `playhead` is stale by design;
    /// this is what anything re-anchoring must measure from.
    private var measuredPlayhead: Date {
        guard isPlaying, let anchoredAt = anchorAt else { return playhead }
        return Self.playhead(from: anchorPlayhead, elapsed: ContinuousClock.now - anchoredAt,
                             rate: rate, endingAt: timeline.endedAt)
    }

    /// Starts measuring again from a given moment.
    ///
    /// Called whenever something makes the elapsed time since the last anchor meaningless: play,
    /// a person seeking, or a change of speed. Without the last of those, changing to 8x would
    /// retroactively re-scale every second already played.
    ///
    /// The moment is passed in rather than read from `playhead`, because the two callers want
    /// different ones: a seek anchors where the person just dragged to, and a speed change
    /// anchors where the measurement says we actually are.
    private func reanchor(at moment: Date) {
        anchorPlayhead = moment
        anchorAt = ContinuousClock.now
    }

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
            fieldTrace = FieldTrace(readings: timeline.readings,
                                    baselineMicrotesla: baselines.magneticMicrotesla)
            track = timeline.track
            walkedPath = track.map(\.position)
            var inForce: Double?
            magneticBaseInForce = timeline.readings.map { reading in
                if let base = reading.measurements?["emf"]?.baseline { inForce = base }
                return inForce
            }
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
        // Play where nothing was recorded, with a recording still to come: start at that recording
        // (rule T6, ProjectNotes/specs/replay-semantics.md — the website's rule since 09/16/2026).
        // Pressing Play at 0:00 on a night whose sound begins at 0:04 means "play the sound".
        if frame.activeAudio == nil, frame.activeVideo == nil,
           let next = timeline.media.filter({ $0.startedAt > playhead }).min(by: { $0.startedAt < $1.startedAt }) {
            seek(to: next.startedAt)
        }
        isPlaying = true

        reanchor(at: playhead)

        ticker = Task { [weak self] in
            let step = 1 / (self?.tickHz ?? 10)
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(step))
                guard let self, self.isPlaying, let anchoredAt = self.anchorAt else { return }

                // Measured, not counted. A tick that arrives late moves the playhead further,
                // which is what keeps a replay at the speed its label claims.
                let next = Self.playhead(
                    from: self.anchorPlayhead,
                    elapsed: ContinuousClock.now - anchoredAt,
                    rate: self.rate,
                    endingAt: self.timeline.endedAt)

                if next >= self.timeline.endedAt {
                    self.moveTo(self.timeline.endedAt)
                    self.pause()
                    return
                }
                self.moveTo(next)
            }
        }
    }

    public func pause() {
        isPlaying = false
        ticker?.cancel()
        ticker = nil
        anchorAt = nil
    }

    public func togglePlaying() { isPlaying ? pause() : play() }

    public func seek(to moment: Date) {
        seekCount += 1
        moveTo(moment)
        // A person dragging the scrubber mid-playback restarts the measurement; without this the
        // next tick would drag the playhead straight back to where the anchor says it should be.
        if isPlaying { reanchor(at: playhead) }
    }

    /// Moves the playhead without disturbing the anchor — what the ticker uses.
    private func moveTo(_ moment: Date) {
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
        if let index = lastReadingIndex(at: moment) {
            let reading = timeline.readings[index]
            result.magneticBaseMicrotesla = index < magneticBaseInForce.count ? magneticBaseInForce[index] : nil
            result.magneticMicrotesla = reading.measurements?["emf"]?.numberValue
            result.soundDbfs = reading.measurements?["sound_level"]?.numberValue
            result.relativeAltitudeMeters = reading.measurements?["relative_altitude"]?.numberValue
            result.headingDegrees = reading.motion?.headingDegrees
            result.courseDegrees = reading.motion?.courseDegrees
            // Read from THIS reading rather than held forever: readings carry the room for as
            // long as one is set, so its absence here means they stopped saying, not that the
            // label was lost.
            result.room = reading.measurements?["room"]?.labelValue
        }

        result.position = interpolatedPosition(at: moment)
        result.trackBearingDegrees = trackBearing(at: moment)
        // The LATEST-starting clip of each kind wins where two of a kind overlap: it is the one
        // somebody started most recently, and so the one they are most likely looking for.
        func covering(_ kind: CaptureKind) -> (segment: MediaSegment, offset: TimeInterval)? {
            timeline.media
                .last(where: { $0.kind == kind && $0.covers(moment) })
                .flatMap { segment in segment.offset(at: moment).map { (segment, $0) } }
        }
        result.activeVideo = covering(.video)
        result.activeAudio = covering(.audio)
        result.nearestMarker = timeline.markers.first {
            abs($0.at.timeIntervalSince(moment)) < 1.0
        }
        result.motionDetected = ReplayMotion.showing(timeline.markers, at: moment)
        return result
    }

    private func lastReadingIndex(at moment: Date) -> Int? {
        // Binary search: a five-hour session is tens of thousands of readings and this runs ten
        // times a second.
        var low = 0, high = timeline.readings.count - 1, best: Int?
        while low <= high {
            let mid = (low + high) / 2
            if timeline.readings[mid].at <= moment {
                best = mid
                low = mid + 1
            } else {
                high = mid - 1
            }
        }
        return best
    }

    /// The fixes either side of a moment: the last at or before it and the first after it.
    private func fixesAround(_ moment: Date) -> (before: Int?, after: Int?) {
        var low = 0, high = track.count - 1, before: Int?
        while low <= high {
            let mid = (low + high) / 2
            if track[mid].at <= moment { before = mid; low = mid + 1 } else { high = mid - 1 }
        }
        let after = (before ?? -1) + 1
        return (before, after < track.count ? after : nil)
    }

    /// How far apart two fixes must be before the line between them is a walk rather than drift.
    static let trackBearingMinimumMeters: Double = 5

    /// The bearing from the fix before the moment to the fix after it (rule P6): only across a
    /// stretch short enough to be a walk (30 s, as the glide) and long enough to be one (5 m).
    private func trackBearing(at moment: Date) -> Double? {
        let (beforeIndex, afterIndex) = fixesAround(moment)
        guard let beforeIndex, let afterIndex else { return nil }
        let before = track[beforeIndex], after = track[afterIndex]
        let span = after.at.timeIntervalSince(before.at)
        guard span > 0, span <= 30,
              let lat1 = before.position.latitude, let lon1 = before.position.longitude,
              let lat2 = after.position.latitude, let lon2 = after.position.longitude
        else { return nil }
        guard Self.metres(lat1, lon1, lat2, lon2) >= Self.trackBearingMinimumMeters else { return nil }
        return Self.bearing(lat1, lon1, lat2, lon2)
    }

    /// Equirectangular distance — plenty for "did they leave the spot".
    static func metres(_ lat1: Double, _ lon1: Double, _ lat2: Double, _ lon2: Double) -> Double {
        let earth = 6_371_000.0
        let dLat = (lat2 - lat1) * .pi / 180
        let dLon = (lon2 - lon1) * .pi / 180 * cos((lat1 + lat2) / 2 * .pi / 180)
        return earth * (dLat * dLat + dLon * dLon).squareRoot()
    }

    /// Initial bearing from one point to another, clockwise from north, 0..<360.
    static func bearing(_ lat1: Double, _ lon1: Double, _ lat2: Double, _ lon2: Double) -> Double {
        let φ1 = lat1 * .pi / 180, φ2 = lat2 * .pi / 180
        let Δλ = (lon2 - lon1) * .pi / 180
        let y = sin(Δλ) * cos(φ2)
        let x = cos(φ1) * sin(φ2) - sin(φ1) * cos(φ2) * cos(Δλ)
        let degrees = atan2(y, x) * 180 / .pi
        return (degrees + 360).truncatingRemainder(dividingBy: 360)
    }

    /// Position IS interpolated: somebody walking between two fixes really was somewhere in
    /// between, and a map that teleports them corner to corner is less true, not more.
    private func interpolatedPosition(at moment: Date) -> FieldReading.Position? {
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

    /// The text value, when this measurement is a label rather than a quantity.
    public var labelValue: String? {
        if case .string(let value) = value { return value }
        return nil
    }
}

extension Double {
    var clampedToUnitInterval: Double { min(max(self, 0), 1) }
}
