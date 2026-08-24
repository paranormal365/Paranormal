import Foundation

/// One shared coder pair. ASP.NET Core serializes PascalCase C# records as
/// camelCase JSON, which is Swift's default property naming — so no key
/// strategy is applied. (Verified at runtime against `GET api/public/events`
/// and locked by the committed fixtures.)
public enum BenJSON {
    /// C# `DateTime` arrives in three shapes: ISO8601 with fractional seconds,
    /// ISO8601 without, and a naked `yyyy-MM-dd'T'HH:mm:ss[.fffffff]` with no
    /// offset (e.g. `NotificationBucket.OldestUnreadUtc`) which is UTC by
    /// contract. Parsing uses the Sendable `FormatStyle` APIs — the classic
    /// formatter classes trip Swift 6 strict concurrency as shared statics.
    public static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let raw = try container.decode(String.self)
            if let date = parseDate(raw) { return date }
            throw DecodingError.dataCorruptedError(
                in: container, debugDescription: "Unrecognized date: \(raw)")
        }
        return decoder
    }()

    public static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }()

    public static func parseDate(_ raw: String) -> Date? {
        if let date = try? Date(raw, strategy: Date.ISO8601FormatStyle(includingFractionalSeconds: true)) { return date }
        if let date = try? Date(raw, strategy: .iso8601) { return date }
        return nakedUTCDate(from: raw)
    }

    /// C# emits 0–7 fractional digits with no offset. Parse the whole-second
    /// part and add the fraction back arithmetically — fixed-width fraction
    /// patterns would only match one digit count.
    private static func nakedUTCDate(from raw: String) -> Date? {
        let parts = raw.split(separator: ".", maxSplits: 1)
        // Treat the naked value as UTC by appending Z and reusing the ISO parser.
        guard let base = try? Date("\(parts[0])Z", strategy: .iso8601) else { return nil }
        guard parts.count == 2 else { return base }
        let digits = parts[1].prefix(while: \.isNumber)
        guard !digits.isEmpty, let fraction = Double("0.\(digits)") else { return base }
        return base.addingTimeInterval(fraction)
    }
}

extension UUID {
    /// .NET's `Guid.Empty` sentinel — e.g. `MeResponse.userId` uses it to mark
    /// an Entra-only identity with no linked local account.
    public static let emptyGuid = UUID(uuidString: "00000000-0000-0000-0000-000000000000")!
    public var isEmptyGuid: Bool { self == .emptyGuid }
}
