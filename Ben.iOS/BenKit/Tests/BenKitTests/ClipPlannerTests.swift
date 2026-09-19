import Foundation
import Testing
@testable import BenKit

/// Turning the moments something happened into the clips worth watching.
///
/// The rules that matter are all about not wasting the reviewer's night: a clip opens BEFORE the
/// trigger or it opens on the aftermath, several triggers in the same few seconds are one event
/// and not three near-identical clips, and no clip may quietly grow into the whole recording.
@Suite("Planning clips around what was detected")
struct ClipPlannerTests {

    private let recordingStart = Date(timeIntervalSince1970: 1_800_000_000)
    private func at(_ seconds: TimeInterval) -> Date { recordingStart.addingTimeInterval(seconds) }

    private func plan(_ triggers: [TimeInterval],
                      duration: TimeInterval = 3600,
                      policy: ClipPolicy = .default) -> [ClipRequest] {
        ClipPlanner.plan(triggers: triggers.map(at), recordingStart: recordingStart,
                         recordingDuration: duration, policy: policy)
    }

    @Test func aClipStartsBeforeTheThingItIsAbout() {
        // The whole reason continuous recording is worth its battery: anything that detects
        // movement detects it after it began.
        let clips = plan([100])

        #expect(clips.count == 1)
        #expect(clips[0].startOffset == 92)          // 8 seconds of lead-in
        #expect(clips[0].duration == 23)             // and 15 after
        #expect(clips[0].triggers == [at(100)])
    }

    @Test func aClipCannotStartBeforeTheRecordingDid() {
        // A trigger two seconds in has only two seconds of lead-in to give.
        let clips = plan([2])

        #expect(clips[0].startOffset == 0)
        #expect(clips[0].endOffset == 17)
    }

    @Test func aClipCannotRunPastTheEndOfTheRecording() {
        let clips = plan([595], duration: 600)

        #expect(clips[0].endOffset == 600)
        #expect(clips[0].startOffset == 587)
    }

    @Test func triggersInTheSameFewSecondsAreOneClip() {
        // A door, a footstep and a magnetic mark inside ten seconds are one event to whoever
        // watches it. Three overlapping clips of the same moment waste the export and the review.
        let clips = plan([100, 104, 110])

        #expect(clips.count == 1)
        #expect(clips[0].startOffset == 92)
        #expect(clips[0].endOffset == 125)
        #expect(clips[0].triggers.count == 3)
    }

    @Test func triggersFarApartAreSeparateClips() {
        let clips = plan([100, 300])

        #expect(clips.count == 2)
        #expect(clips[0].endOffset == 115)
        #expect(clips[1].startOffset == 292)
    }

    @Test func aRunOfTriggersStopsGrowingAtTheCap() {
        // Something detected every five seconds for ten minutes must not merge into one
        // ten-minute "clip", which is the recording under another name.
        let triggers = stride(from: 100.0, through: 700.0, by: 5).map { $0 }
        let clips = plan(triggers, policy: ClipPolicy(leadIn: 8, leadOut: 15, maximum: 90))

        #expect(clips.count > 1)
        #expect(clips.allSatisfy { $0.duration <= 90 })
        // And every trigger still ends up in one of them: a cap must not silently drop events.
        #expect(clips.reduce(0) { $0 + $1.triggers.count } == triggers.count)
    }

    @Test func aTriggerOutsideTheRecordingHasNoFootageBehindIt() {
        // Marks are dropped for a whole session; the camera may have been on for part of it.
        let clips = ClipPlanner.plan(
            triggers: [at(-30), at(100), at(9_999)],
            recordingStart: recordingStart, recordingDuration: 600)

        #expect(clips.count == 1)
        #expect(clips[0].triggers == [at(100)])
    }

    @Test func nothingDetectedMeansNoClips() {
        #expect(plan([]).isEmpty)
    }

    @Test func aRecordingOfNothingProducesNothing() {
        #expect(plan([10], duration: 0).isEmpty)
    }

    @Test func triggersOutOfOrderAreStillPlannedInOrder() {
        // They arrive from a log and from the UI, and nothing promises they are sorted.
        let clips = plan([300, 100, 104])

        #expect(clips.count == 2)
        #expect(clips[0].startOffset < clips[1].startOffset)
        #expect(clips[0].triggers.count == 2)
    }

    // MARK: - What each mode costs, said before it is chosen

    @Test func everyModeExplainsItsBatteryCost() {
        for mode in VideoRecordingMode.allCases {
            #expect(!mode.batteryNote.isEmpty, "\(mode) must say what it costs")
            #expect(!mode.summary.isEmpty)
            #expect(!mode.title.isEmpty)
        }
    }

    @Test func theDefaultIsTheCheapestOne() {
        // Somebody who turned video on without reading anything must not get the mode that can
        // flatten a phone before the night is over.
        #expect(VideoRecordingMode.default == .clipsByHand)
        #expect(!VideoRecordingMode.default.isContinuous)
        #expect(!VideoRecordingMode.default.clipsWhileRecording)
    }

    @Test func onlyOneModeWritesClipsWhileTheCameraIsStillRunning() {
        #expect(VideoRecordingMode.allCases.filter(\.clipsWhileRecording) == [.continuousClipLive])
        #expect(VideoRecordingMode.allCases.filter(\.isContinuous)
                == [.continuousClipLater, .continuousClipLive])
    }
}
