import Testing
@testable import BenKit

/// A meter feed ending never ends a recording (10/23/2026: Ben's 1.1.5 session lost all its sound
/// when the sensor feeds restarted mid-session).
@Suite struct MicrophoneDemandTests {
    /// The bug: a setting changes after Start, the sensors restart, the old feed ends.
    @Test func the_meter_restarting_keeps_a_recording_open() {
        var demand = MicrophoneDemand()
        let first = demand.meterStarted()
        demand.recordingStarted()

        #expect(demand.meterEnded(first) == false)
        #expect(demand.isRecording)
        _ = demand.meterStarted()
        #expect(demand.wantsMicrophone)
    }

    /// The old feed can end after the new one has begun. It must not take the new one's meter.
    @Test func an_old_feed_ending_late_leaves_the_new_feed_running() {
        var demand = MicrophoneDemand()
        let old = demand.meterStarted()
        let new = demand.meterStarted()

        #expect(demand.meterEnded(old) == false)
        #expect(demand.meterFeed == new)
    }

    @Test func the_microphone_goes_away_only_when_nothing_wants_it() {
        var demand = MicrophoneDemand()
        let feed = demand.meterStarted()
        demand.recordingStarted()

        #expect(demand.recordingEnded() == false)   // the meter still runs
        #expect(demand.meterEnded(feed) == true)
        #expect(demand.wantsMicrophone == false)
    }

    /// A session ending: the recording is closed first, then the feeds stop.
    @Test func a_recording_without_a_meter_puts_the_microphone_away_when_it_ends() {
        var demand = MicrophoneDemand()
        demand.recordingStarted()
        #expect(demand.recordingEnded() == true)
    }
}
