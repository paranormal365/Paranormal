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
    case failed(reason: String?)
    case sessionEnded
    case rateLimited(retryAfter: TimeInterval?)

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
        case .failed(let reason): .failed(reason: reason)
        case .sessionEnded: .sessionEnded
        case .rateLimited(let retryAfter): .rateLimited(retryAfter: retryAfter)
        }
    }
}

extension LoadResult: Equatable where T: Equatable {}
