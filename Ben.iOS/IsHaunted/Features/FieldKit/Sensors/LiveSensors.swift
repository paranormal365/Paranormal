import Foundation
import CoreMotion
import CoreLocation
import AVFoundation
import UIKit
import BenKit

/// The magnetometer, through CoreMotion.
///
/// Uses `CMDeviceMotion.magneticField` rather than the raw magnetometer: the raw sensor includes
/// the device's OWN magnetic interference — its speaker and vibration motor — and reports it as
/// field. Device motion gives the calibrated value plus an honest accuracy, and that accuracy is
/// what stops an uncalibrated swing being reported as a finding.
final class LiveMagnetometer: MagnetometerSource, @unchecked Sendable {
    private let manager = CMMotionManager()

    var isAvailable: Bool { manager.isDeviceMotionAvailable }

    func samples(hz: Double) -> AsyncStream<MagneticFieldSample> {
        AsyncStream { continuation in
            guard manager.isDeviceMotionAvailable else { continuation.finish(); return }

            manager.deviceMotionUpdateInterval = 1 / max(1, hz)
            manager.startDeviceMotionUpdates(
                using: .xMagneticNorthZVertical, to: .main
            ) { motion, _ in
                guard let motion else { return }
                let field = motion.magneticField
                continuation.yield(MagneticFieldSample(
                    at: Date(),
                    x: field.field.x, y: field.field.y, z: field.field.z,
                    calibration: Self.accuracy(field.accuracy)))
            }

            // Captures self, which is @unchecked Sendable and owns the manager's lifetime;
            // capturing the CMMotionManager directly is not allowed and would also let it
            // outlive the object responsible for stopping it.
            continuation.onTermination = { [weak self] _ in
                self?.stop()
            }
        }
    }

    private func stop() { manager.stopDeviceMotionUpdates() }

    private static func accuracy(_ value: CMMagneticFieldCalibrationAccuracy)
        -> MagneticFieldSample.CalibrationAccuracy {
        switch value {
        case .uncalibrated: .uncalibrated
        case .low: .low
        case .medium: .medium
        case .high: .high
        @unknown default: .uncalibrated
        }
    }
}

/// Sound level, from a metering-only audio tap.
///
/// Slice 2 measures; it does not record. The recording rides on the same engine in the next
/// slice, which is why this owns the session rather than a recorder object — two things fighting
/// over one audio session is how a night's recording ends up silent.
final class LiveAudioLevel: AudioLevelSource, @unchecked Sendable {
    private let engine = AVAudioEngine()

    var isAvailable: Bool { true }

    func levels() -> AsyncStream<AudioLevelSample> {
        AsyncStream { continuation in
            do {
                let session = AVAudioSession.sharedInstance()
                try session.setCategory(.playAndRecord, mode: .measurement,
                                        options: [.mixWithOthers, .defaultToSpeaker])
                try session.setActive(true)

                let input = engine.inputNode
                let format = input.outputFormat(forBus: 0)
                input.installTap(onBus: 0, bufferSize: 2048, format: format) { buffer, _ in
                    guard let levels = Self.levels(of: buffer) else { return }
                    continuation.yield(AudioLevelSample(at: Date(),
                                                        averageDbfs: levels.average,
                                                        peakDbfs: levels.peak))
                }
                try engine.start()
            } catch {
                // No microphone, or permission refused: the session carries on without sound
                // rather than failing. A missing channel narrows a reading; it never stops one.
                continuation.finish()
                return
            }

            continuation.onTermination = { [weak self] _ in
                self?.stop()
            }
        }
    }

    private func stop() {
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        try? AVAudioSession.sharedInstance().setActive(false)
    }

    /// RMS and peak, in dBFS. A silent buffer is reported at the floor rather than negative
    /// infinity, which would break every scale that touches it.
    private static func levels(of buffer: AVAudioPCMBuffer) -> (average: Double, peak: Double)? {
        guard let channel = buffer.floatChannelData?[0] else { return nil }
        let count = Int(buffer.frameLength)
        guard count > 0 else { return nil }

        var sumOfSquares: Float = 0
        var peak: Float = 0
        for index in 0..<count {
            let value = channel[index]
            sumOfSquares += value * value
            peak = max(peak, abs(value))
        }

        let rms = (sumOfSquares / Float(count)).squareRoot()
        return (decibels(rms), decibels(peak))
    }

    private static func decibels(_ amplitude: Float) -> Double {
        amplitude > 0 ? max(-60, Double(20 * log10(amplitude))) : -60
    }
}

/// Position and heading.
final class LiveLocation: NSObject, LocationSource, CLLocationManagerDelegate, @unchecked Sendable {
    private let manager = CLLocationManager()
    private var positionContinuations: [UUID: AsyncStream<PositionSample>.Continuation] = [:]
    private var headingContinuations: [UUID: AsyncStream<HeadingSample>.Continuation] = [:]
    private var authorizationWaiters: [CheckedContinuation<LocationAuthorization, Never>] = []
    private let lock = NSLock()

    override init() {
        super.init()
        manager.delegate = self
        manager.desiredAccuracy = kCLLocationAccuracyBest
        manager.distanceFilter = 5      // metres — enough to trace a walk, not every shuffle
    }

    var isAvailable: Bool { CLLocationManager.locationServicesEnabled() }

    func authorizationState() async -> LocationAuthorization { Self.map(manager.authorizationStatus) }

    func requestWhenInUse() async -> LocationAuthorization {
        let current = Self.map(manager.authorizationStatus)
        guard current == .notDetermined else { return current }

        return await withCheckedContinuation { continuation in
            lock.lock()
            authorizationWaiters.append(continuation)
            lock.unlock()
            manager.requestWhenInUseAuthorization()
        }
    }

    func positions() -> AsyncStream<PositionSample> {
        AsyncStream { continuation in
            let id = UUID()
            lock.lock(); positionContinuations[id] = continuation; lock.unlock()
            manager.startUpdatingLocation()
            continuation.onTermination = { [weak self] _ in
                guard let self else { return }
                lock.lock(); positionContinuations[id] = nil
                let empty = positionContinuations.isEmpty
                lock.unlock()
                if empty { manager.stopUpdatingLocation() }
            }
        }
    }

    func headings() -> AsyncStream<HeadingSample> {
        AsyncStream { continuation in
            let id = UUID()
            lock.lock(); headingContinuations[id] = continuation; lock.unlock()
            if CLLocationManager.headingAvailable() { manager.startUpdatingHeading() }
            continuation.onTermination = { [weak self] _ in
                guard let self else { return }
                lock.lock(); headingContinuations[id] = nil
                let empty = headingContinuations.isEmpty
                lock.unlock()
                if empty { manager.stopUpdatingHeading() }
            }
        }
    }

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let location = locations.last else { return }
        // A negative horizontal accuracy means the fix is invalid — pass it on as unknown rather
        // than as a very precise minus-one metres.
        let accuracy = location.horizontalAccuracy >= 0 ? location.horizontalAccuracy : nil
        let sample = PositionSample(
            at: location.timestamp,
            latitude: location.coordinate.latitude,
            longitude: location.coordinate.longitude,
            altitudeMeters: location.verticalAccuracy >= 0 ? location.altitude : nil,
            accuracyMeters: accuracy,
            speedMps: location.speed >= 0 ? location.speed : nil)

        lock.lock(); let targets = Array(positionContinuations.values); lock.unlock()
        for continuation in targets { continuation.yield(sample) }
    }

    func locationManager(_ manager: CLLocationManager, didUpdateHeading heading: CLHeading) {
        guard heading.headingAccuracy >= 0 else { return }
        let sample = HeadingSample(at: heading.timestamp, degrees: heading.trueHeading >= 0
                                   ? heading.trueHeading : heading.magneticHeading)
        lock.lock(); let targets = Array(headingContinuations.values); lock.unlock()
        for continuation in targets { continuation.yield(sample) }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        let state = Self.map(manager.authorizationStatus)
        guard state != .notDetermined else { return }
        lock.lock()
        let waiters = authorizationWaiters
        authorizationWaiters = []
        lock.unlock()
        for waiter in waiters { waiter.resume(returning: state) }
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        // Nothing to do: no fix is a state the readings already express by omitting position.
    }

    private static func map(_ status: CLAuthorizationStatus) -> LocationAuthorization {
        switch status {
        case .notDetermined: .notDetermined
        case .denied: .denied
        case .restricted: .restricted
        case .authorizedAlways, .authorizedWhenInUse: .authorized
        @unknown default: .notDetermined
        }
    }
}

/// Barometric altitude, relative to where the session began.
///
/// GPS altitude is coarse — tens of metres — while the barometer resolves centimetres. It cannot
/// say how high above sea level you are, but it can say you went up a floor, which is the
/// question worth answering in a building.
final class LiveAltimeter: AltitudeSource, @unchecked Sendable {
    fileprivate let altimeterHandle = CMAltimeter()
    private var altimeter: CMAltimeter { altimeterHandle }

    var isAvailable: Bool { CMAltimeter.isRelativeAltitudeAvailable() }

    func relativeAltitudes() -> AsyncStream<RelativeAltitudeSample> {
        AsyncStream { continuation in
            guard CMAltimeter.isRelativeAltitudeAvailable() else { continuation.finish(); return }
            altimeter.startRelativeAltitudeUpdates(to: .main) { data, _ in
                guard let data else { return }
                continuation.yield(RelativeAltitudeSample(
                    at: Date(), metersSinceStart: data.relativeAltitude.doubleValue))
            }
            continuation.onTermination = { [weak self] _ in
                self?.stop()
            }
        }
    }
}

extension LiveAltimeter {
    fileprivate func stop() { altimeterHandle.stopRelativeAltitudeUpdates() }
}

enum LiveSensors {
    /// The real instruments, plus the battery reading that rides on every heartbeat.
    @MainActor
    static func suite() -> SensorSuite {
        UIDevice.current.isBatteryMonitoringEnabled = true
        return SensorSuite(
            magnetometer: LiveMagnetometer(),
            audio: LiveAudioLevel(),
            location: LiveLocation(),
            altitude: LiveAltimeter(),
            batteryPercent: {
                let level = UIDevice.current.batteryLevel
                return level < 0 ? nil : Double(level) * 100
            })
    }
}
