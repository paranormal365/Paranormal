import Foundation
import Testing

/// Wait for a condition to become true, rather than for a fixed stretch of the clock.
///
/// **A test that sleeps a fixed time and then asserts is a test that fails on a busy machine.**
/// `Task.sleep` promises "at least", never "at most", and a suite running 49 others in parallel can
/// starve a background task for far longer than the sleep it was given. That is how
/// `playingAdvancesThePlayheadAndStopsAtTheEnd` failed once on 2026-09-11 and then passed every
/// time it was run on its own.
///
/// The deadline here is deliberately far longer than anything being waited on needs. It exists to
/// stop a hung test, not to assert a speed — asserting a speed on a shared machine is the same
/// mistake wearing a longer sleep.
///
/// Nothing here makes a *correctness* problem pass: a condition that will never hold still fails,
/// with the name of what it was waiting for.
func waitUntil(
    _ what: String,
    within seconds: Double = 5,
    sourceLocation: SourceLocation = #_sourceLocation,
    _ condition: @Sendable () async -> Bool
) async throws {
    let deadline = ContinuousClock.now + .seconds(seconds)
    while ContinuousClock.now < deadline {
        if await condition() { return }
        try await Task.sleep(for: .milliseconds(10))
    }
    Issue.record("Timed out after \(seconds)s waiting for: \(what)", sourceLocation: sourceLocation)
}
