import Foundation
import Testing
@testable import BenKit

/// The line the chart draws is worked out once, and it keeps the moment worth looking at.
@Suite("Field trace — the chart's line")
struct FieldTraceTests {

    private let start = Date(timeIntervalSince1970: 1_750_000_000)

    private func readings(count: Int, spikeAt spike: Int? = nil) -> [FieldReading] {
        (0..<count).map { index in
            // A slow wave around the base level, and one spike where asked.
            var field = 50 + sin(Double(index) / 40) * 0.3
            if index == spike { field = 50 + 12 }
            return FieldReading(at: start.addingTimeInterval(Double(index) * 0.5),
                                triggeredBy: .interval,
                                measurements: ["emf": .number(field, unit: "uT", baseline: 50)])
        }
    }

    @Test("a long night is thinned to the budget, and the spike survives the thinning")
    func thinsAndKeepsTheSpike() {
        let trace = FieldTrace(readings: readings(count: 20_000, spikeAt: 7_777), baselineMicrotesla: 50)

        #expect(trace.points.count <= FieldTrace.mostPoints + 2)
        #expect(trace.points.count > FieldTrace.mostPoints / 2)
        // +12 µT is +120 mG from base; averaging a 100-reading bucket would have left ~1 mG of it.
        #expect(trace.points.contains { abs($0.milligauss - 120) < 0.01 })
        #expect(trace.points.map(\.at) == trace.points.map(\.at).sorted())
        #expect(Set(trace.points.map(\.id)).count == trace.points.count)
        #expect(trace.points.map(\.id) == Array(trace.points.indices))
    }

    @Test("a short session is drawn whole")
    func shortSessionIsWhole() {
        let trace = FieldTrace(readings: readings(count: 50), baselineMicrotesla: 50)
        #expect(trace.points.count == 50)
        #expect(trace.points[0].milligauss == 0)   // 50 − 50, times ten
    }

    @Test("with no base level there is no line")
    func noBaseLevelNoLine() {
        #expect(FieldTrace(readings: readings(count: 50), baselineMicrotesla: nil).points.isEmpty)
    }

    @Test("a flat bucket gives one point, not the same reading twice")
    func flatBucketGivesOnePoint() {
        let flat = (0..<1_000).map { index in
            FieldReading(at: start.addingTimeInterval(Double(index)), triggeredBy: .interval,
                         measurements: ["emf": .number(50, unit: "uT")])
        }
        let trace = FieldTrace(readings: flat, baselineMicrotesla: 50, mostPoints: 100)
        // 50 buckets of 20 identical readings: one survivor each.
        #expect(trace.points.count == 50)
        #expect(Set(trace.points.map(\.id)).count == 50)
    }

    @Test("readings without a field measurement are left out, not drawn as zero")
    func readingsWithoutFieldAreSkipped() {
        var mixed = readings(count: 10)
        mixed.append(FieldReading(at: start.addingTimeInterval(100), triggeredBy: .interval,
                                  measurements: ["sound_level": .number(-40, unit: "dBFS")]))
        #expect(FieldTrace(readings: mixed, baselineMicrotesla: 50).points.count == 10)
    }
}
