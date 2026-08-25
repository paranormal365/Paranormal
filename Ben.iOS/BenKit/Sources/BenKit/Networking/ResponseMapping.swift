import Foundation

/// Maps an HTTP response into a `LoadResult`, replicating the exact behavior of
/// `WebApiClient.SendListAsync` / `SendExpectingReasonAsync` on the web side.
public enum ResponseMapping {
    /// The C# prose test, byte for byte: a refusal we wrote is a sentence; a
    /// framework error is a ProblemDetails blob (`{…`) or an HTML page (`<…`),
    /// and showing either to a person is worse than saying nothing useful.
    public static func prose(fromBody body: String?) -> String? {
        guard let body, !body.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        guard body.count < 400 else { return nil }
        let trimmedStart = body.drop(while: \.isWhitespace)
        guard !trimmedStart.hasPrefix("{"), !trimmedStart.hasPrefix("<") else { return nil }
        return body.trimmingCharacters(in: CharacterSet(charactersIn: "\" \n"))
    }

    /// The non-prose fallback the web client uses: the status itself is the
    /// single most useful thing a person debugging a deployment can be told.
    public static func statusFallback(_ statusCode: Int) -> String {
        let phrase = HTTPURLResponse.localizedString(forStatusCode: statusCode)
        return "The server answered \(statusCode) (\(phrase))."
    }

    /// Parses a Retry-After header value: either delta-seconds or an HTTP-date.
    public static func retryAfter(_ headerValue: String?, now: Date = Date()) -> TimeInterval? {
        guard let headerValue, !headerValue.isEmpty else { return nil }
        if let seconds = TimeInterval(headerValue) { return max(0, seconds) }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(identifier: "GMT")
        formatter.dateFormat = "EEE',' dd MMM yyyy HH:mm:ss 'GMT'"
        guard let date = formatter.date(from: headerValue) else { return nil }
        return max(0, date.timeIntervalSince(now))
    }

    /// Maps a failure status. Precondition: `statusCode` is NOT 2xx.
    public static func failure<T: Sendable>(statusCode: Int, data: Data, headers: [AnyHashable: Any]) -> LoadResult<T> {
        switch statusCode {
        case 401:
            // 401 before anything else. A dead token is not a broken list.
            return .sessionEnded
        case 429:
            let header = (headers["Retry-After"] as? String) ?? (headers["retry-after"] as? String)
            return .rateLimited(retryAfter: retryAfter(header))
        default:
            let body = String(data: data, encoding: .utf8)
            return .failed(reason: prose(fromBody: body) ?? statusFallback(statusCode),
                           statusCode: statusCode)
        }
    }

    /// Maps a full response into a decoded `LoadResult`. An empty 2xx body maps
    /// to `.ok(nil)`-style behavior via `EmptyBody`; callers wanting a value get
    /// `.failed` on an undecodable body (a contract break, not "nothing here").
    public static func decode<T: Decodable & Sendable>(
        _ type: T.Type, statusCode: Int, data: Data, headers: [AnyHashable: Any]
    ) -> LoadResult<T> {
        guard (200..<300).contains(statusCode) else {
            return failure(statusCode: statusCode, data: data, headers: headers)
        }
        // Ok(null) from a controller becomes 204 WITH AN EMPTY BODY — decoding
        // an empty stream must not detonate (the Price Bands lesson).
        if data.isEmpty {
            if let empty = EmptyBody() as? T { return .ok(empty) }
            return .failed(reason: "The server answered \(statusCode) with an empty body.")
        }
        do {
            return .ok(try BenJSON.decoder.decode(T.self, from: data))
        } catch {
            return .failed(reason: "The server's answer couldn't be read.")
        }
    }
}

/// Decodes from anything (including an empty body) — the `Void`-like success
/// payload for endpoints that answer 204/200 with nothing to say.
public struct EmptyBody: Decodable, Sendable, Equatable {
    public init() {}
    public init(from decoder: Decoder) throws {}
}
