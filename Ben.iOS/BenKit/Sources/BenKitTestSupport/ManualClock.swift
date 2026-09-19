import Foundation

/// A clock a test drives by hand.
///
/// Field-session logic is almost entirely about time — heartbeat intervals, debounce windows,
/// how long somebody waited after asking a question. Testing that against the real clock means
/// sleeping, which makes a suite slow and flaky in exactly the places precision matters. Every
/// component that needs "now" takes a `@Sendable () -> Date`, and this is what tests pass.
public final class ManualClock: @unchecked Sendable {

    private let lock = NSLock()
    private var current: Date

    public init(_ start: Date = Date(timeIntervalSince1970: 1_787_600_000)) {
        self.current = start
    }

    public var now: Date {
        lock.lock(); defer { lock.unlock() }
        return current
    }

    /// The closure to hand to whatever is under test.
    public var nowProvider: @Sendable () -> Date {
        { [self] in now }
    }

    @discardableResult
    public func advance(by seconds: TimeInterval) -> Date {
        lock.lock(); defer { lock.unlock() }
        current = current.addingTimeInterval(seconds)
        return current
    }

    public func set(_ date: Date) {
        lock.lock(); defer { lock.unlock() }
        current = date
    }
}
