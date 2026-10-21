import Foundation
import os
import CoreMotion
import CoreLocation
import AVFoundation
import UIKit
import BenKit

/// ONE device-motion stream for everything in the app that reads one.
///
/// **Why this exists (1.1.1, 2026-10-02).** Ben, after updating: "the magnetometer quit tracking."
/// The magnetometer and the movement watcher each had their own `CMMotionManager` and each started
/// device motion — the magnetometer in the magnetic-north frame, the watcher in the default
/// arbitrary frame. Device motion is one service on the phone: two managers asking it for two
/// frames is exactly what Apple's "create only one instance" warning is about, and the magnetic
/// field is only computed in a frame that uses the magnetometer. Until 1.1.1 the watcher only ran
/// when a sentry asked for it; 1.1.1 made Watch for Motion default to on, so every session started
/// the second stream, and the field stopped updating.
///
/// Now there is one manager and one stream, always in a frame that computes the field, handed to
/// every reader at the rate it asked for. It starts with the first reader and stops with the last.
final class SharedDeviceMotion: @unchecked Sendable {
    static let shared = SharedDeviceMotion()

    private struct Reader {
        let interval: TimeInterval
        var lastDelivered: TimeInterval
        let handler: (CMDeviceMotion) -> Void
    }

    private let manager = CMMotionManager()
    private let lock = NSLock()
    private var readers: [UUID: Reader] = [:]

    var isAvailable: Bool { manager.isDeviceMotionAvailable }

    /// Magnetic north when the phone offers it (it needs the compass); otherwise the corrected
    /// arbitrary frame, which still uses the magnetometer and so still computes the field.
    static var referenceFrame: CMAttitudeReferenceFrame {
        let frames = CMMotionManager.availableAttitudeReferenceFrames()
        return frames.contains(.xMagneticNorthZVertical) ? .xMagneticNorthZVertical : .xArbitraryCorrectedZVertical
    }

    /// Starts delivering to `handler`, on the main queue, about `hz` times a second.
    func subscribe(hz: Double, _ handler: @escaping (CMDeviceMotion) -> Void) -> UUID {
        let id = UUID()
        lock.lock()
        readers[id] = Reader(interval: 1 / max(1, hz), lastDelivered: 0, handler: handler)
        let fastest = readers.values.map(\.interval).min() ?? 1
        lock.unlock()

        DispatchQueue.main.async { [self] in
            manager.deviceMotionUpdateInterval = fastest
            guard !manager.isDeviceMotionActive else { return }
            manager.startDeviceMotionUpdates(using: Self.referenceFrame, to: .main) { [weak self] motion, _ in
                guard let self, let motion else { return }
                self.deliver(motion)
            }
        }
        return id
    }

    func unsubscribe(_ id: UUID) {
        lock.lock()
        readers[id] = nil
        let remaining = readers.values.map(\.interval).min()
        lock.unlock()

        DispatchQueue.main.async { [self] in
            if let remaining {
                manager.deviceMotionUpdateInterval = remaining
            } else {
                manager.stopDeviceMotionUpdates()
            }
        }
    }

    /// Hands one reading to every reader whose own interval has passed — so a reader that asked
    /// for 10 Hz is not flooded because another asked for 50.
    private func deliver(_ motion: CMDeviceMotion) {
        let now = motion.timestamp
        var due: [(CMDeviceMotion) -> Void] = []
        lock.lock()
        for (id, reader) in readers where now - reader.lastDelivered >= reader.interval * 0.9 {
            readers[id]?.lastDelivered = now
            due.append(reader.handler)
        }
        lock.unlock()
        for handler in due { handler(motion) }
    }
}

/// The magnetometer, through CoreMotion.
///
/// Uses `CMDeviceMotion.magneticField` rather than the raw magnetometer: the raw sensor includes
/// the device's OWN magnetic interference — its speaker and vibration motor — and reports it as
/// field. Device motion gives the calibrated value plus an honest accuracy, and that accuracy is
/// what stops an uncalibrated swing being reported as a finding. Read from the app's one shared
/// device-motion stream (`SharedDeviceMotion`), never a manager of its own.
final class LiveMagnetometer: MagnetometerSource, @unchecked Sendable {
    private let motion: SharedDeviceMotion

    init(motion: SharedDeviceMotion = .shared) { self.motion = motion }

    var isAvailable: Bool { motion.isAvailable }

    func samples(hz: Double) -> AsyncStream<MagneticFieldSample> {
        AsyncStream { continuation in
            guard motion.isAvailable else { continuation.finish(); return }
            let id = motion.subscribe(hz: hz) { reading in
                let field = reading.magneticField
                continuation.yield(MagneticFieldSample(
                    at: Date(),
                    x: field.field.x, y: field.field.y, z: field.field.z,
                    calibration: Self.accuracy(field.accuracy)))
            }
            continuation.onTermination = { [motion] _ in motion.unsubscribe(id) }
        }
    }

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

// The microphone lives in LiveAudioCapture.swift.

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
            speedMps: location.speed >= 0 ? location.speed : nil,
            // A negative course means the fix could not work out a direction — standing still
            // does that. Passed on as unknown rather than as due north.
            courseDegrees: location.course >= 0 ? location.course : nil)

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

/// The battery level, readable from any thread.
///
/// `UIDevice` is main-actor-isolated and `SensorSuite.batteryPercent` is a synchronous
/// `@Sendable` closure the engine calls from its own actor on every heartbeat — it cannot hop
/// to the main actor and must not block waiting for it. So the level is cached: primed on the
/// main actor, kept fresh by the system's own `batteryLevelDidChange` notification (monitoring
/// is enabled in `suite()`, which is what makes that notification fire), and read through a
/// lock. A heartbeat therefore reports the level as of the last change, which for a battery is
/// the same thing as "now".
private final class BatteryLevelCache: Sendable {
    @MainActor static let shared = BatteryLevelCache()

    /// -1 mirrors UIDevice's own "unknown" sentinel.
    private let level = OSAllocatedUnfairLock<Float>(initialState: -1)

    @MainActor
    private init() {
        let read: @Sendable (Float) -> Void = { [level] value in level.withLock { $0 = value } }
        read(UIDevice.current.batteryLevel)
        NotificationCenter.default.addObserver(
            forName: UIDevice.batteryLevelDidChangeNotification, object: nil, queue: .main
        ) { _ in
            // The .main queue IS the main actor; assumeIsolated states that rather than
            // silencing it. Never deallocated — one battery, one cache, app lifetime.
            MainActor.assumeIsolated { read(UIDevice.current.batteryLevel) }
        }
    }

    func percent() -> Double? {
        let value = level.withLock { $0 }
        return value < 0 ? nil : Double(value) * 100
    }
}

enum LiveSensors {
    /// The real instruments, plus the battery reading that rides on every heartbeat.
    @MainActor
    static func suite() -> SensorSuite {
        UIDevice.current.isBatteryMonitoringEnabled = true
        let battery = BatteryLevelCache.shared
        // One object for both audio jobs — see LiveAudioCapture on why that matters.
        let audio = LiveAudioCapture()
        return SensorSuite(
            magnetometer: LiveMagnetometer(),
            audio: audio,
            recorder: audio,
            location: LiveLocation(),
            altitude: LiveAltimeter(),
            deviceMovement: LiveDeviceMovement(),
            dictation: LiveDictation(sharing: audio),
            batteryPercent: { battery.percent() })
    }
}
