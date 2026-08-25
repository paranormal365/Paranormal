import Foundation
import BenKit

/// Instruments a test writes the script for.
///
/// The simulator has no magnetometer and the macOS test host has no CoreMotion at all, so
/// nothing about baselines, thresholds or debounce could be tested without these. They are also
/// what drives the app's `-fieldKitFakeSensors` demo mode, which is the only way to show the
/// gauges moving on a simulator.
public struct ScriptedMagnetometer: MagnetometerSource {
    public let isAvailable: Bool
    private let script: [MagneticFieldSample]

    public init(_ script: [MagneticFieldSample], isAvailable: Bool = true) {
        self.script = script
        self.isAvailable = isAvailable
    }

    public func samples(hz: Double) -> AsyncStream<MagneticFieldSample> {
        AsyncStream { continuation in
            for sample in script { continuation.yield(sample) }
            continuation.finish()
        }
    }

    /// A steady field of `microtesla`, with named excursions spliced in.
    public static func steady(_ microtesla: Double, from start: Date, count: Int,
                              everySeconds: TimeInterval = 0.1,
                              calibration: MagneticFieldSample.CalibrationAccuracy = .high,
                              spikes: [Int: Double] = [:]) -> ScriptedMagnetometer {
        ScriptedMagnetometer((0..<count).map { index in
            let value = spikes[index] ?? microtesla
            return MagneticFieldSample(
                at: start.addingTimeInterval(Double(index) * everySeconds),
                x: value, y: 0, z: 0, calibration: calibration)
        })
    }
}

public struct ScriptedAudio: AudioLevelSource {
    public let isAvailable: Bool
    private let script: [AudioLevelSample]

    public init(_ script: [AudioLevelSample], isAvailable: Bool = true) {
        self.script = script
        self.isAvailable = isAvailable
    }

    public func levels() -> AsyncStream<AudioLevelSample> {
        AsyncStream { continuation in
            for sample in script { continuation.yield(sample) }
            continuation.finish()
        }
    }

    public static func steady(_ dbfs: Double, from start: Date, count: Int,
                              everySeconds: TimeInterval = 0.1,
                              spikes: [Int: Double] = [:]) -> ScriptedAudio {
        ScriptedAudio((0..<count).map { index in
            let value = spikes[index] ?? dbfs
            return AudioLevelSample(at: start.addingTimeInterval(Double(index) * everySeconds),
                                    averageDbfs: value, peakDbfs: value + 3)
        })
    }
}

public struct ScriptedLocation: LocationSource {
    public let isAvailable: Bool
    private let positionScript: [PositionSample]
    private let headingScript: [HeadingSample]
    private let authorization: LocationAuthorization

    public init(positions: [PositionSample] = [], headings: [HeadingSample] = [],
                authorization: LocationAuthorization = .authorized,
                isAvailable: Bool = true) {
        self.positionScript = positions
        self.headingScript = headings
        self.authorization = authorization
        self.isAvailable = isAvailable
    }

    public func authorizationState() async -> LocationAuthorization { authorization }
    public func requestWhenInUse() async -> LocationAuthorization { authorization }

    public func positions() -> AsyncStream<PositionSample> {
        AsyncStream { continuation in
            for sample in positionScript { continuation.yield(sample) }
            continuation.finish()
        }
    }

    public func headings() -> AsyncStream<HeadingSample> {
        AsyncStream { continuation in
            for sample in headingScript { continuation.yield(sample) }
            continuation.finish()
        }
    }
}

public struct ScriptedAltitude: AltitudeSource {
    public let isAvailable: Bool
    private let script: [RelativeAltitudeSample]

    public init(_ script: [RelativeAltitudeSample], isAvailable: Bool = true) {
        self.script = script
        self.isAvailable = isAvailable
    }

    public func relativeAltitudes() -> AsyncStream<RelativeAltitudeSample> {
        AsyncStream { continuation in
            for sample in script { continuation.yield(sample) }
            continuation.finish()
        }
    }
}
