import Foundation

/// Swift port of `Ben.Web.Services/WebApi/LoadResult.cs` — the repo's doctrine
/// that a refusal must never render as "nothing here".
///
/// - `.ok` with an empty collection is *genuinely empty* and may say "nothing here".
/// - `.failed` is a refusal or an error; the UI must show it as such, with the
///   server's prose when the server wrote a sentence.
/// - `.sessionEnded` is a 401 exactly. A 403 is `.failed` — being signed in but
///   not allowed is not an expired session (item 133).
/// - `.rateLimited` is a 429; the Retry-After interval drives a countdown rather
///   than a generic failure.
public enum LoadResult<T: Sendable>: Sendable {
    case ok(T)
    /// `statusCode` is the raw HTTP status when the failure came from a response (nil for
    /// unreachable-network failures). Most callers ignore it; the feed reads 404 as "the
    /// feature is switched off sitewide" — the API 404s the whole controller in that case,
    /// and rendering that as an error would tell a visitor something broke when nothing did.
    case failed(reason: String?, statusCode: Int?)
    case sessionEnded
    case rateLimited(retryAfter: TimeInterval?)

    /// The common construction: an unreachable server or a mapped failure without a status.
    public static func failed(reason: String?) -> LoadResult<T> {
        .failed(reason: reason, statusCode: nil)
    }

    public var value: T? {
        if case .ok(let v) = self { return v }
        return nil
    }

    public var isOk: Bool {
        if case .ok = self { return true }
        return false
    }

    /// Carries every failure state across unchanged; only success is transformed.
    public func map<U: Sendable>(_ transform: (T) -> U) -> LoadResult<U> {
        switch self {
        case .ok(let v): .ok(transform(v))
        case .failed(let reason, let statusCode): .failed(reason: reason, statusCode: statusCode)
        case .sessionEnded: .sessionEnded
        case .rateLimited(let retryAfter): .rateLimited(retryAfter: retryAfter)
        }
    }
}

extension LoadResult: Equatable where T: Equatable {}
