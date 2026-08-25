import Foundation

// MARK: - Samples

/// One magnetometer reading.
///
/// **This is a magnetic field, not "EMF".** The phone's magnetometer measures the DC magnetic
/// field — the Earth's, plus whatever local iron, wiring or motors are doing to it. It cannot
/// see the AC electromagnetic fields a K-II style meter responds to. Calling it EMF in the UI
/// without saying that would be selling somebody an instrument they do not have.
///
/// Stored in microtesla because that is the unit the export format uses; shown in milligauss
/// because that is the unit this field talks in. 1 uT = 10 mG.
public struct MagneticFieldSample: Sendable, Equatable {
    public var at: Date
    public var x: Double
    public var y: Double
    public var z: Double
    /// How much the device trusts its own calibration. A spike read while uncalibrated is not
    /// evidence of anything, and the reading carries this so a reviewer can tell.
    public var calibration: CalibrationAccuracy

    public init(at: Date, x: Double, y: Double, z: Double,
                calibration: CalibrationAccuracy = .medium) {
        self.at = at
        self.x = x
        self.y = y
        self.z = z
        self.calibration = calibration
    }

    /// Total field strength, microtesla.
    public var magnitudeMicrotesla: Double { (x * x + y * y + z * z).squareRoot() }
    /// Total field strength in the unit the field talks in.
    public var magnitudeMilligauss: Double { magnitudeMicrotesla * 10 }

    public enum CalibrationAccuracy: Int, Sendable, Codable, Comparable {
        case uncalibrated = -1, low = 0, medium = 1, high = 2

        public static func < (lhs: Self, rhs: Self) -> Bool { lhs.rawValue < rhs.rawValue }

        /// The ± a reading deserves, in microtesla, given how well calibrated it is. Written
        /// into `accuracy` on the exported measurement.
        public var microteslaTolerance: Double? {
            switch self {
            case .uncalibrated: nil    // unknown, and saying "±0" would be a lie
            case .low: 5.0
            case .medium: 1.5
            case .high: 0.5
            }
        }

        public var isTrustworthy: Bool { self >= .medium }
    }
}

/// One audio level reading, in dBFS — decibels relative to full scale, so values are negative
/// and 0 is the loudest the hardware can represent.
public struct AudioLevelSample: Sendable, Equatable {
    public var at: Date
    public var averageDbfs: Double
    public var peakDbfs: Double

    public init(at: Date, averageDbfs: Double, peakDbfs: Double) {
        self.at = at
        self.averageDbfs = averageDbfs
        self.peakDbfs = peakDbfs
    }
}

/// One position fix.
public struct PositionSample: Sendable, Equatable {
    public var at: Date
    public var latitude: Double
    public var longitude: Double
    public var altitudeMeters: Double?
    /// Horizontal accuracy in metres. Indoors this is routinely 20–50 m, which is why it
    /// travels with every reading rather than being assumed away.
    public var accuracyMeters: Double?
    public var speedMps: Double?

    public init(at: Date, latitude: Double, longitude: Double,
                altitudeMeters: Double? = nil, accuracyMeters: Double? = nil,
                speedMps: Double? = nil) {
        self.at = at
        self.latitude = latitude
        self.longitude = longitude
        self.altitudeMeters = altitudeMeters
        self.accuracyMeters = accuracyMeters
        self.speedMps = speedMps
    }
}

public struct HeadingSample: Sendable, Equatable {
    public var at: Date
    /// Degrees from true north, 0–360.
    public var degrees: Double
    public init(at: Date, degrees: Double) {
        self.at = at
        self.degrees = degrees
    }
}

/// Barometric altitude CHANGE since the session began. Absolute GPS altitude is coarse; this is
/// precise to a few centimetres, which is what actually answers "did they go upstairs".
public struct RelativeAltitudeSample: Sendable, Equatable {
    public var at: Date
    public var metersSinceStart: Double
    public init(at: Date, metersSinceStart: Double) {
        self.at = at
        self.metersSinceStart = metersSinceStart
    }
}

public enum LocationAuthorization: Sendable, Equatable {
    case notDetermined, denied, restricted, authorized

    public var canLocate: Bool { self == .authorized }
}

// MARK: - Sources

/// Everything below is a protocol so the ENGINE can be tested on a Mac. CoreMotion does not
/// exist on the test host and a simulator has no magnetometer at all, so the only way any of
/// this logic gets exercised is through scripted streams.
public protocol MagnetometerSource: Sendable {
    var isAvailable: Bool { get }
    func samples(hz: Double) -> AsyncStream<MagneticFieldSample>
}

public protocol AudioLevelSource: Sendable {
    var isAvailable: Bool { get }
    func levels() -> AsyncStream<AudioLevelSample>
}

public protocol LocationSource: Sendable {
    var isAvailable: Bool { get }
    func authorizationState() async -> LocationAuthorization
    func requestWhenInUse() async -> LocationAuthorization
    func positions() -> AsyncStream<PositionSample>
    func headings() -> AsyncStream<HeadingSample>
}

public protocol AltitudeSource: Sendable {
    var isAvailable: Bool { get }
    func relativeAltitudes() -> AsyncStream<RelativeAltitudeSample>
}

/// The instruments a session runs on. Any of them may be missing — an iPad without cellular has
/// no GPS, a device may refuse the barometer, a person may decline location. A missing sensor
/// narrows what a reading says; it never stops the session.
public struct SensorSuite: Sendable {
    public var magnetometer: MagnetometerSource?
    public var audio: AudioLevelSource?
    public var location: LocationSource?
    public var altitude: AltitudeSource?
    /// Battery percentage, 0–100. Logged on heartbeats: low battery is a documented cause of
    /// spurious readings, and a reviewer seeing 8% reads a spike differently.
    public var batteryPercent: @Sendable () -> Double?

    public init(magnetometer: MagnetometerSource? = nil,
                audio: AudioLevelSource? = nil,
                location: LocationSource? = nil,
                altitude: AltitudeSource? = nil,
                batteryPercent: @escaping @Sendable () -> Double? = { nil }) {
        self.magnetometer = magnetometer
        self.audio = audio
        self.location = location
        self.altitude = altitude
        self.batteryPercent = batteryPercent
    }
}
