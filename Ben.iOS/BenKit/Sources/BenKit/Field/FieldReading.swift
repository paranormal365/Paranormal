import Foundation

/// One reading, shaped exactly as `readings[]` in the IsHaunted Device Data Format v1.
///
/// See `ProjectNotes/specs/DeviceDataFormat-v1.md` and its JSON Schema. This app is the FIRST
/// device implementing that spec, so these types are a contract with a published document rather
/// than an internal convenience — the encoded bytes are asserted against the schema by tests.
///
/// The spec's rules that shape this file:
/// - **A bare number is not evidence**: a numeric `value` without a `unit` is INVALID. Enforced
///   here by `Measurement.number(_:unit:)`, which cannot be called without one.
/// - **Everything except `at` is optional**: a device reports what it can. A missing GPS fix
///   omits `position` ENTIRELY — it never writes zeros, which would be a lie about a location.
/// - **Say how precise the clock is**: `precision` is declared rather than implied by trailing
///   digits.
/// - **Extend through `measurements`**, never through new top-level fields.
public struct FieldReading: Codable, Sendable, Equatable {

    /// UTC, ISO-8601 with offset. The one required field: a measurement with no time cannot be
    /// placed against anything else.
    public var at: Date

    /// The meaningful resolution of `at` — a closed enum in the schema.
    public enum Precision: String, Codable, Sendable { case second, millisecond, microsecond }
    public var precision: Precision?

    /// Device-assigned counter. Its value is detecting DROPPED records: a gap in timestamps
    /// might be silence or might be loss, and only the counter tells them apart.
    public var sequence: Int?

    /// Why this record exists. **A closed enum in the schema** — `interval` (a heartbeat),
    /// `event` (something crossed a threshold), `manual` (a person did it). The specific
    /// flavour of a manual or automatic mark rides in the `marker` measurements channel
    /// instead, because inventing values here produces a document the spec's own validator
    /// rejects.
    public enum Trigger: String, Codable, Sendable { case interval, event, manual }
    public var triggeredBy: Trigger?

    public var measurements: [String: Measurement]?
    public var position: Position?
    public var motion: Motion?
    public var audioRef: FileRef?

    /// The operator's remark about this specific moment.
    public var note: String?

    public init(at: Date,
                precision: Precision? = .millisecond,
                sequence: Int? = nil,
                triggeredBy: Trigger? = nil,
                measurements: [String: Measurement]? = nil,
                position: Position? = nil,
                motion: Motion? = nil,
                audioRef: FileRef? = nil,
                note: String? = nil) {
        self.at = at
        self.precision = precision
        self.sequence = sequence
        self.triggeredBy = triggeredBy
        self.measurements = measurements
        self.position = position
        self.motion = motion
        self.audioRef = audioRef
        self.note = note
    }

    private enum CodingKeys: String, CodingKey {
        case at, precision, sequence, note, measurements, position, motion
        case triggeredBy = "triggered_by"
        case audioRef = "audio_ref"
    }

    // ── Nested types ──────────────────────────────────────────────────────────

    /// One channel's value. `value` may be a number, string, bool or null; `unit` is REQUIRED
    /// alongside a number.
    public struct Measurement: Codable, Sendable, Equatable {
        public var value: JSONValue
        public var unit: String?
        public var accuracy: Double?
        public var baseline: Double?
        public var outOfRange: Bool?

        /// Fields a channel carries beside its value — a room scan's id and version, where a
        /// change was found (spec 1.2). The schema lets every measurement carry more than `value`,
        /// so these are kept as they came: a document read and written again loses none of them,
        /// whether or not this build knows what they mean.
        public var details: [String: JSONValue]

        public init(value: JSONValue, unit: String? = nil, accuracy: Double? = nil,
                    baseline: Double? = nil, outOfRange: Bool? = nil,
                    details: [String: JSONValue] = [:]) {
            self.value = value
            self.unit = unit
            self.accuracy = accuracy
            self.baseline = baseline
            self.outOfRange = outOfRange
            self.details = details
        }

        private struct Key: CodingKey {
            var stringValue: String
            var intValue: Int? { nil }
            init(_ string: String) { stringValue = string }
            init?(stringValue: String) { self.stringValue = stringValue }
            init?(intValue: Int) { nil }
        }

        private static let named: Set<String> = ["value", "unit", "accuracy", "baseline", "out_of_range"]

        public init(from decoder: Decoder) throws {
            let container = try decoder.container(keyedBy: Key.self)
            value = try container.decode(JSONValue.self, forKey: Key("value"))
            unit = try container.decodeIfPresent(String.self, forKey: Key("unit"))
            accuracy = try container.decodeIfPresent(Double.self, forKey: Key("accuracy"))
            baseline = try container.decodeIfPresent(Double.self, forKey: Key("baseline"))
            outOfRange = try container.decodeIfPresent(Bool.self, forKey: Key("out_of_range"))
            var details: [String: JSONValue] = [:]
            for key in container.allKeys where !Self.named.contains(key.stringValue) {
                details[key.stringValue] = try container.decode(JSONValue.self, forKey: key)
            }
            self.details = details
        }

        public func encode(to encoder: Encoder) throws {
            var container = encoder.container(keyedBy: Key.self)
            try container.encode(value, forKey: Key("value"))
            try container.encodeIfPresent(unit, forKey: Key("unit"))
            try container.encodeIfPresent(accuracy, forKey: Key("accuracy"))
            try container.encodeIfPresent(baseline, forKey: Key("baseline"))
            try container.encodeIfPresent(outOfRange, forKey: Key("out_of_range"))
            for (name, detail) in details.sorted(by: { $0.key < $1.key }) where !Self.named.contains(name) {
                try container.encode(detail, forKey: Key(name))
            }
        }

        /// The only way to build a numeric measurement — the unit is not optional, because the
        /// schema rejects a number without one and 4.8 means nothing until you know the ambient.
        public static func number(_ value: Double, unit: String,
                                  accuracy: Double? = nil, baseline: Double? = nil,
                                  outOfRange: Bool? = nil) -> Measurement {
            Measurement(value: .number(value), unit: unit,
                        accuracy: accuracy, baseline: baseline, outOfRange: outOfRange)
        }

        /// A label rather than a quantity — how marker kinds travel, since a string value needs
        /// no unit.
        public static func label(_ value: String) -> Measurement {
            Measurement(value: .string(value), unit: nil)
        }

        /// A label with fields beside it — the spec 1.2 channels (`room_scan`, `scan`,
        /// `scan_change`, `figure`, `shape`). Numbers among the details carry their unit in their
        /// name (`distance_cm`), the way `position` does.
        public static func label(_ value: String, details: [String: JSONValue]) -> Measurement {
            Measurement(value: .string(value), unit: nil, details: details)
        }
    }

    /// Where the device was. Absent entirely when there is no fix — see the spec's rule 2.
    public struct Position: Codable, Sendable, Equatable {
        public var latitude: Double?
        public var longitude: Double?
        public var elevationMeters: Double?
        /// What tells a consumer whether to believe the rest. Indoors this is routinely 20–50 m.
        public var accuracyMeters: Double?
        /// Disambiguates vertically stacked rooms that GPS cannot separate.
        public var floor: Int?

        public init(latitude: Double? = nil, longitude: Double? = nil,
                    elevationMeters: Double? = nil, accuracyMeters: Double? = nil,
                    floor: Int? = nil) {
            self.latitude = latitude
            self.longitude = longitude
            self.elevationMeters = elevationMeters
            self.accuracyMeters = accuracyMeters
            self.floor = floor
        }

        private enum CodingKeys: String, CodingKey {
            case latitude, longitude, floor
            case elevationMeters = "elevation_meters"
            case accuracyMeters = "accuracy_meters"
        }
    }

    /// A field spike recorded while the meter was being swung is a different fact from one
    /// recorded while it sat still. Heading lives here, not in `measurements`.
    public struct Motion: Codable, Sendable, Equatable {
        public var headingDegrees: Double?
        /// Which way the fix says the device was MOVING, clockwise from true north.
        ///
        /// Separate from `headingDegrees` on purpose: heading is which way the phone was pointed
        /// (the compass), course is the direction it travelled between fixes. They disagree
        /// whenever somebody walks backwards down a hall, and only one of them exists indoors —
        /// a compass needs no fix, and a course needs no compass. The map draws whichever it has.
        public var courseDegrees: Double?
        public var speedMps: Double?
        public var accelXMps2: Double?
        public var accelYMps2: Double?
        public var accelZMps2: Double?
        public var isStationary: Bool?

        public init(headingDegrees: Double? = nil, courseDegrees: Double? = nil,
                    speedMps: Double? = nil,
                    accelXMps2: Double? = nil, accelYMps2: Double? = nil,
                    accelZMps2: Double? = nil, isStationary: Bool? = nil) {
            self.headingDegrees = headingDegrees
            self.courseDegrees = courseDegrees
            self.speedMps = speedMps
            self.accelXMps2 = accelXMps2
            self.accelYMps2 = accelYMps2
            self.accelZMps2 = accelZMps2
            self.isStationary = isStationary
        }

        /// Nothing worth saying — so the whole object is omitted rather than written empty.
        public var isEmpty: Bool {
            headingDegrees == nil && courseDegrees == nil && speedMps == nil && accelXMps2 == nil
                && accelYMps2 == nil && accelZMps2 == nil && isStationary == nil
        }

        private enum CodingKeys: String, CodingKey {
            case headingDegrees = "heading_degrees"
            case courseDegrees = "course_degrees"
            case speedMps = "speed_mps"
            case accelXMps2 = "accel_x_mps2"
            case accelYMps2 = "accel_y_mps2"
            case accelZMps2 = "accel_z_mps2"
            case isStationary = "is_stationary"
        }
    }

    /// A companion file inside the delivered bundle.
    ///
    /// `filename` is a RELATIVE path and the schema rejects absolute paths, backslashes and
    /// `..`. That is a security boundary, not a style rule: an importer expanding a bundle must
    /// never be steered outside its own directory. `FileRef.relative(_:)` refuses to build one
    /// that would be rejected, so a bad path fails here rather than at somebody else's importer.
    public struct FileRef: Codable, Sendable, Equatable {
        public var filename: String
        public var mediaType: String?
        public var startOffsetSeconds: Double?
        public var durationSeconds: Double?
        /// Lets a consumer prove the pairing survived transit. Audio attached to the wrong
        /// reading is worse than no audio.
        public var sha256: String?

        public init(filename: String, mediaType: String? = nil,
                    startOffsetSeconds: Double? = nil, durationSeconds: Double? = nil,
                    sha256: String? = nil) {
            self.filename = filename
            self.mediaType = mediaType
            self.startOffsetSeconds = startOffsetSeconds
            self.durationSeconds = durationSeconds
            self.sha256 = sha256
        }

        /// Nil when the path could never be delivered — leading separator, backslash anywhere,
        /// or an upward traversal.
        public static func relative(_ path: String, mediaType: String? = nil,
                                    startOffsetSeconds: Double? = nil,
                                    durationSeconds: Double? = nil,
                                    sha256: String? = nil) -> FileRef? {
            guard !path.isEmpty,
                  !path.hasPrefix("/"), !path.hasPrefix("\\"),
                  !path.contains("\\"),
                  !path.contains("..")
            else { return nil }
            return FileRef(filename: path, mediaType: mediaType,
                           startOffsetSeconds: startOffsetSeconds,
                           durationSeconds: durationSeconds, sha256: sha256)
        }

        private enum CodingKeys: String, CodingKey {
            case filename, sha256
            case mediaType = "media_type"
            case startOffsetSeconds = "start_offset_seconds"
            case durationSeconds = "duration_seconds"
        }
    }
}

/// The handful of JSON shapes a measurement value may take — and, among a measurement's details,
/// a list (a position, a size) or an object.
public enum JSONValue: Codable, Sendable, Equatable {
    case number(Double)
    case string(String)
    case bool(Bool)
    case null
    case array([JSONValue])
    case object([String: JSONValue])

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() { self = .null; return }
        // Bool before Double: JSONDecoder will happily read `true` as 1 otherwise, turning a
        // motion flag into a unitless number the schema then rejects.
        if let bool = try? container.decode(Bool.self) { self = .bool(bool); return }
        if let number = try? container.decode(Double.self) { self = .number(number); return }
        if let string = try? container.decode(String.self) { self = .string(string); return }
        if let array = try? container.decode([JSONValue].self) { self = .array(array); return }
        if let object = try? container.decode([String: JSONValue].self) { self = .object(object); return }
        throw DecodingError.dataCorruptedError(
            in: container, debugDescription: "Not a measurement value this format allows.")
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .number(let value): try container.encode(value)
        case .string(let value): try container.encode(value)
        case .bool(let value): try container.encode(value)
        case .null: try container.encodeNil()
        case .array(let values): try container.encode(values)
        case .object(let fields): try container.encode(fields)
        }
    }

    /// The number, when this is one.
    public var number: Double? { if case .number(let value) = self { value } else { nil } }
    /// The text, when this is some.
    public var string: String? { if case .string(let value) = self { value } else { nil } }
    /// The numbers, when this is a list of only numbers — a position, a size.
    public var numbers: [Double]? {
        guard case .array(let values) = self else { return nil }
        let numbers = values.compactMap(\.number)
        return numbers.count == values.count ? numbers : nil
    }
}
