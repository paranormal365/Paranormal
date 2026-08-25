import Foundation

/// The document a session exports as — the top level of Device Data Format v1.
///
/// `ProjectNotes/specs/DeviceDataFormat-v1.md`. Readings are spliced in at export from the
/// session's append-only log rather than held here, so a five-hour session never has to fit in
/// memory to be written out.
public struct DeviceDataEnvelope: Codable, Sendable, Equatable {
    public var formatVersion: String
    public var device: Device
    public var session: Session
    /// Empty in the envelope itself; the exporter writes the real array by splicing log lines.
    public var readings: [FieldReading]

    public init(formatVersion: String = "1.0.0",
                device: Device, session: Session, readings: [FieldReading] = []) {
        self.formatVersion = formatVersion
        self.device = device
        self.session = session
        self.readings = readings
    }

    private enum CodingKeys: String, CodingKey {
        case formatVersion = "format_version"
        case device, session, readings
    }

    /// Identifies the INSTRUMENT, not the operator. An unattributed reading cannot be assessed
    /// for known quirks, and every meter has some — including this one.
    public struct Device: Codable, Sendable, Equatable {
        public var manufacturer: String
        public var model: String
        public var serialNumber: String?
        public var firmwareVersion: String?

        public init(manufacturer: String, model: String,
                    serialNumber: String? = nil, firmwareVersion: String? = nil) {
            self.manufacturer = manufacturer
            self.model = model
            self.serialNumber = serialNumber
            self.firmwareVersion = firmwareVersion
        }

        private enum CodingKeys: String, CodingKey {
            case manufacturer, model
            case serialNumber = "serial_number"
            case firmwareVersion = "firmware_version"
        }
    }

    public struct Session: Codable, Sendable, Equatable {
        public var startedAt: Date
        public var endedAt: Date?
        public var batteryPercentAtStart: Double?
        /// The operator's own words — "back bedroom, north wall".
        public var locationLabel: String?
        public var propertyArea: String?
        /// IANA zone. Timestamps stay UTC regardless; this is how a reviewer reads them back
        /// into the night they happened.
        public var timezone: String?
        public var trigger: Trigger

        public init(startedAt: Date, endedAt: Date? = nil,
                    batteryPercentAtStart: Double? = nil,
                    locationLabel: String? = nil, propertyArea: String? = nil,
                    timezone: String? = TimeZone.current.identifier,
                    trigger: Trigger) {
            self.startedAt = startedAt
            self.endedAt = endedAt
            self.batteryPercentAtStart = batteryPercentAtStart
            self.locationLabel = locationLabel
            self.propertyArea = propertyArea
            self.timezone = timezone
            self.trigger = trigger
        }

        private enum CodingKeys: String, CodingKey {
            case timezone, trigger
            case startedAt = "started_at"
            case endedAt = "ended_at"
            case batteryPercentAtStart = "battery_percent_at_start"
            case locationLabel = "location_label"
            case propertyArea = "property_area"
        }
    }

    /// How readings came to exist. This is what makes a GAP interpretable: under `interval` a
    /// gap means a missed sample; under `event` it means nothing happened, which is itself a
    /// finding. The phone runs `hybrid` — events plus a heartbeat — so silence is always
    /// distinguishable from a dead device.
    public struct Trigger: Codable, Sendable, Equatable {
        public enum Mode: String, Codable, Sendable { case interval, event, hybrid }
        public var mode: Mode
        public var intervalSeconds: Double?
        public var eventDescription: String?
        /// The minimum quiet period between event records. Null would tell a reviewer that a
        /// burst may be one event; the phone always debounces, so it is always written.
        public var debounceSeconds: Double?

        public init(mode: Mode, intervalSeconds: Double? = nil,
                    eventDescription: String? = nil, debounceSeconds: Double? = nil) {
            self.mode = mode
            self.intervalSeconds = intervalSeconds
            self.eventDescription = eventDescription
            self.debounceSeconds = debounceSeconds
        }

        private enum CodingKeys: String, CodingKey {
            case mode
            case intervalSeconds = "interval_seconds"
            case eventDescription = "event_description"
            case debounceSeconds = "debounce_seconds"
        }
    }
}

/// The encoder and decoder for device-data documents.
///
/// Kept apart from `BenJSON` (which speaks to the API in camelCase) because this format is a
/// PUBLISHED contract: snake_case keys are written explicitly on every type, dates are ISO-8601
/// with offset, and `nil` means "omit the key" — the spec treats null and absent identically,
/// and omitting is what makes "a device with no GPS simply has no position" true on the wire.
public enum DeviceDataJSON {
    public static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(iso8601.format(date))
        }
        // Slashes unescaped: `media\/audio-001.m4a` is valid JSON but nobody writes paths that
        // way, and it makes every file reference in the document harder to read and to match.
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return encoder
    }()

    public static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let text = try decoder.singleValueContainer().decode(String.self)
            if let date = try? iso8601.parse(text) { return date }
            if let date = try? iso8601NoFraction.parse(text) { return date }
            throw DecodingError.dataCorruptedError(
                in: try decoder.singleValueContainer(),
                debugDescription: "Not an ISO-8601 timestamp: \(text)")
        }
        return decoder
    }()

    /// Milliseconds, with an offset. The spec forbids local time without one.
    ///
    /// `Date.ISO8601FormatStyle` rather than `ISO8601DateFormatter` because the latter is not
    /// Sendable, and these are shared across the actors that write readings.
    static let iso8601 = Date.ISO8601FormatStyle(includingFractionalSeconds: true, timeZone: .gmt)

    /// Whole seconds — what another device's export may carry, and what we accept when reading
    /// somebody else's bundle back.
    static let iso8601NoFraction = Date.ISO8601FormatStyle(timeZone: .gmt)
}
