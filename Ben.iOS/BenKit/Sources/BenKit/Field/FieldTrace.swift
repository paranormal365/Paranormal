import Foundation

/// The field line a chart draws for a session, thinned to what a screen can show.
///
/// A five-hour session is tens of thousands of readings and a chart cannot draw more points than
/// it has pixels. Each bucket keeps its lowest and highest reading rather than an average, because
/// averaging is exactly what would erase the spike somebody opened the session to look at.
///
/// Worked out ONCE, when a replay loads, and handed to the chart as a value. It used to be derived
/// inside the chart from every reading on every body evaluation — ten times a second while playing
/// — with a fresh identity for every point, so the whole chart was re-laid on every tick of a long
/// night. Here every point keeps its place as its identity, and an unchanged trace costs nothing.
public struct FieldTrace: Sendable, Equatable {

    public struct Point: Sendable, Equatable, Identifiable {
        /// Its place in the trace — stable for the life of the timeline, which is what lets the
        /// chart tell an unchanged point from a new one.
        public var id: Int
        public var at: Date
        /// Milligauss above (or below) the base level.
        public var milligauss: Double

        public init(id: Int, at: Date, milligauss: Double) {
            self.id = id
            self.at = at
            self.milligauss = milligauss
        }
    }

    public var points: [Point]

    /// At most this many points reach a chart. Beyond it the line is slower to draw than to read.
    public static let mostPoints = 400

    public static let empty = FieldTrace(points: [])

    public init(points: [Point]) {
        self.points = points
    }

    /// The trace of `readings` against `baselineMicrotesla`. No base level, no line: the chart is
    /// drawn as a change from the level taken at the start, and without one there is nothing to
    /// compare against — the chart says so itself.
    public init(readings: [FieldReading], baselineMicrotesla: Double?,
                mostPoints: Int = FieldTrace.mostPoints) {
        guard let base = baselineMicrotesla else {
            points = []
            return
        }
        let all: [(at: Date, milligauss: Double)] = readings.compactMap { reading in
            guard let field = reading.measurements?["emf"]?.numberValue else { return nil }
            return (reading.at, (field - base) * 10)
        }

        var kept: [(at: Date, milligauss: Double)] = []
        if all.count <= mostPoints {
            kept = all
        } else {
            // Two survivors per bucket, so the bucket count is half the budget.
            let bucketSize = Int((Double(all.count) / Double(max(1, mostPoints / 2))).rounded(.up))
            kept.reserveCapacity(mostPoints + 2)
            for start in stride(from: 0, to: all.count, by: bucketSize) {
                let bucket = all[start..<min(start + bucketSize, all.count)]
                guard let low = bucket.indices.min(by: { bucket[$0].milligauss < bucket[$1].milligauss }),
                      let high = bucket.indices.max(by: { bucket[$0].milligauss < bucket[$1].milligauss })
                else { continue }
                // A flat bucket — or one reading — has one extreme, not the same point twice.
                if low == high {
                    kept.append(bucket[low])
                } else {
                    // In time order, so the line never doubles back on itself.
                    kept.append(bucket[min(low, high)])
                    kept.append(bucket[max(low, high)])
                }
            }
        }

        points = kept.enumerated().map { Point(id: $0.offset, at: $0.element.at, milligauss: $0.element.milligauss) }
    }
}
