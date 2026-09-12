import Foundation
import Testing
@testable import BenKit

/// Choosing the stretch of a session worth sending (item 210).
///
/// The plan is what a person reads before deciding, so every number in it has to be the truth
/// about what will actually be uploaded. The rules that matter are about the failure direction:
/// where the plan is unsure, it sends more rather than less, because an upload that carries too
/// much is a nuisance and one that silently drops evidence is not.
@Suite("Trimming a session to the window worth keeping")
struct SessionTrimTests {

    private let start = Date(timeIntervalSince1970: 1_800_000_000)   // the session's start
    private func at(_ minutes: Double) -> Date { start.addingTimeInterval(minutes * 60) }

    private func window(_ from: Double, _ to: Double) -> SessionWindow {
        SessionWindow(start: at(from), end: at(to))
    }

    // MARK: - The window itself

    @Test func aReversedWindowIsOrderedRatherThanEmpty() {
        // Either handle of a slider can be dragged past the other. A reversed window that stayed
        // reversed would contain nothing and silently upload an empty session.
        let w = SessionWindow(start: at(40), end: at(10))
        #expect(w.start == at(10))
        #expect(w.end == at(40))
        #expect(w.duration == 30 * 60)
    }

    @Test func aWindowCoveringEverythingIsNotATrim() {
        let ended = at(60)
        #expect(window(0, 60).isWholeSession(startedAt: start, endedAt: ended))
        #expect(!window(10, 60).isWholeSession(startedAt: start, endedAt: ended))
        #expect(!window(0, 50).isWholeSession(startedAt: start, endedAt: ended))
    }

    @Test func aSessionWithNoEndIsWholeWhenTheWindowStartsAtItsBeginning() {
        // An interrupted session has no honest end time. Whether it is being trimmed can only be
        // decided from its start.
        #expect(window(0, 30).isWholeSession(startedAt: start, endedAt: nil))
        #expect(!window(5, 30).isWholeSession(startedAt: start, endedAt: nil))
    }

    // MARK: - Readings and marks

    @Test func onlyTheReadingsAndMarksInsideTheWindowAreCounted() {
        let plan = SessionTrimPlan.plan(
            window: window(20, 30),
            startedAt: start, endedAt: at(60),
            readingTimes: [at(5), at(21), at(25), at(29), at(45)],
            markerTimes: [at(5), at(25)],
            media: [])

        #expect(plan.readingCount == 3)
        #expect(plan.markerCount == 1)
        #expect(!plan.isWholeSession)
    }

    // MARK: - Photographs are moments

    @Test func aPhotographIsInOrOut() {
        let inside  = TrimmableMedia(relativePath: "media/p1.jpg", kind: .photo, startedAt: at(25), duration: nil)
        let outside = TrimmableMedia(relativePath: "media/p2.jpg", kind: .photo, startedAt: at(50), duration: nil)

        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [inside, outside])

        #expect(plan.media[0].outcome == .sentWhole)
        #expect(plan.media[1].outcome == .leftOut)
        #expect(plan.includedPaths == ["media/p1.jpg"])
    }

    // MARK: - Recordings are spans

    @Test func aRecordingWhollyInsideIsSentAsItIs() {
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: at(22), duration: 4 * 60)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        // No export, no risk, no CPU: a file already inside the window is uploaded untouched.
        #expect(plan.media[0].outcome == .sentWhole)
    }

    @Test func aRecordingWhollyOutsideIsNotSent() {
        let before = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                    startedAt: at(1), duration: 60)
        let after  = TrimmableMedia(relativePath: "media/b.m4a", kind: .audio,
                                    startedAt: at(45), duration: 60)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [before, after])

        #expect(plan.media.allSatisfy { $0.outcome == .leftOut })
        #expect(plan.includedPaths.isEmpty)
    }

    @Test func aRecordingSpanningTheWindowIsCutToIt() {
        // The hour of audio the whole feature exists for: begins at the session's start, runs an
        // hour, and only ten minutes of it matter.
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: start, duration: 60 * 60)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        // Offsets are seconds INTO THE ORIGINAL FILE — what AVFoundation wants, and what somebody
        // can check against the copy still on the phone.
        #expect(plan.media[0].outcome == .cut(from: 20 * 60, duration: 10 * 60))
    }

    @Test func aRecordingOverlappingOnlyTheStartIsCutToTheOverlap() {
        // Began before the window, ended inside it.
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: at(15), duration: 10 * 60)   // 15 → 25
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        #expect(plan.media[0].outcome == .cut(from: 5 * 60, duration: 5 * 60))
    }

    @Test func aRecordingThatFillsTheWindowExactlyIsNotCutForNothing() {
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: at(20), duration: 10 * 60)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        // Cutting a file to 100% of itself costs an export and an export can fail. The second of
        // slack is what stops a slider landing a hair inside the ends from triggering one.
        #expect(plan.media[0].outcome == .sentWhole)
    }

    // MARK: - The failure direction

    @Test func aRecordingOfUnknownLengthIsSentWholeRatherThanGuessedAt() {
        // Nothing has measured this file. It might have ended before the window opened, or it
        // might span the whole thing — and dropping evidence on a guess is the one outcome worth
        // ruling out.
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: at(5), duration: nil)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        #expect(plan.media[0].outcome == .sentWhole)
    }

    @Test func aRecordingOfUnknownLengthThatBeganAfterTheWindowIsStillDropped() {
        // The one case a missing duration cannot hide: a recording that started after the window
        // closed cannot possibly overlap it, however long it ran.
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: at(45), duration: nil)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        #expect(plan.media[0].outcome == .leftOut)
    }

    @Test func aZeroLengthRecordingIsTreatedAsUnmeasured() {
        let item = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                  startedAt: at(5), duration: 0)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [item])

        #expect(plan.media[0].outcome == .sentWhole)
    }

    // MARK: - The whole picture the screen shows

    @Test func theCountsTheScreenShowsAddUp() {
        let media = [
            TrimmableMedia(relativePath: "media/whole.m4a", kind: .audio, startedAt: at(22), duration: 60),
            TrimmableMedia(relativePath: "media/cut.mov",   kind: .video, startedAt: start,  duration: 60 * 60),
            TrimmableMedia(relativePath: "media/gone.jpg",  kind: .photo, startedAt: at(50), duration: nil),
            TrimmableMedia(relativePath: "media/kept.jpg",  kind: .photo, startedAt: at(24), duration: nil),
        ]
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [at(21), at(26)], markerTimes: [at(26)], media: media)

        #expect(plan.sentWhole.count == 2)
        #expect(plan.cut.count == 1)
        #expect(plan.leftOut.count == 1)
        #expect(plan.includedPaths == ["media/whole.m4a", "media/cut.mov", "media/kept.jpg"])
        #expect(plan.readingCount == 2)
        #expect(plan.markerCount == 1)
    }

    // MARK: - What one upload may carry (2026-09-12)

    /// The phone is not limited: it records for as long as the night needs, at whatever the
    /// camera gives. These are about the UPLOAD, where video is the only thing heavy enough to
    /// need rationing by the clock and everything else is rationed by weight.

    @Test func videoIsCountedAtItsCutLengthNotItsWholeLength() {
        // The whole point of the window: a twenty-minute clip trimmed to four minutes sends four
        // minutes. Counting the original would refuse an upload that is well inside the rule.
        let clip = TrimmableMedia(relativePath: "media/v1.mov", kind: .video,
                                  startedAt: at(20), duration: 20 * 60)
        let plan = SessionTrimPlan.plan(
            window: window(21, 25), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [clip])

        #expect(plan.videoSecondsSent == 4 * 60)
        #expect(!plan.exceedsVideoAllowance)
    }

    @Test func moreThanFiveMinutesOfVideoIsRefusedAndSaysByHowMuch() {
        let clip = TrimmableMedia(relativePath: "media/v1.mov", kind: .video,
                                  startedAt: at(10), duration: 8 * 60)
        let plan = SessionTrimPlan.plan(
            window: window(0, 60), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [clip])

        #expect(plan.videoSecondsSent == 8 * 60)
        #expect(plan.exceedsVideoAllowance)
        // The number the screen tells somebody to drag off, so it has to be the real shortfall.
        #expect(plan.videoSecondsOverAllowance == 3 * 60)
    }

    @Test func severalClipsAreAddedUpRatherThanJudgedOneByOne() {
        // Three four-minute clips are each inside the rule and together are twelve minutes. The
        // allowance is about what the upload carries, not about the largest thing in it.
        let clips = (0..<3).map {
            TrimmableMedia(relativePath: "media/v\($0).mov", kind: .video,
                           startedAt: at(Double($0) * 10), duration: 4 * 60)
        }
        let plan = SessionTrimPlan.plan(
            window: window(0, 60), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: clips)

        #expect(plan.videoSecondsSent == 12 * 60)
        #expect(plan.exceedsVideoAllowance)
    }

    @Test func aClipLeftOutOfTheWindowCostsNothing() {
        let inside  = TrimmableMedia(relativePath: "media/v1.mov", kind: .video,
                                     startedAt: at(21), duration: 60)
        let outside = TrimmableMedia(relativePath: "media/v2.mov", kind: .video,
                                     startedAt: at(45), duration: 30 * 60)
        let plan = SessionTrimPlan.plan(
            window: window(20, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [inside, outside])

        #expect(plan.videoSecondsSent == 60)
        #expect(!plan.exceedsVideoAllowance)
    }

    @Test func audioAndPhotographsAreNotRationedByTheClock() {
        // A whole night of sound is a fraction of one minute of video. Rationing it by time would
        // punish the cheap channels for video's sins.
        let audio = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                   startedAt: at(0), duration: 5 * 60 * 60,
                                   byteCount: 300 * 1024 * 1024)
        let plan = SessionTrimPlan.plan(
            window: window(0, 5 * 60), startedAt: start, endedAt: at(5 * 60),
            readingTimes: [], markerTimes: [], media: [audio])

        #expect(plan.videoSecondsSent == 0)
        #expect(!plan.exceedsVideoAllowance)
        #expect(!plan.exceedsSizeAllowance)   // five hours of audio still goes in one send
    }

    @Test func aCutFileIsWeighedAtTheFractionActuallySent() {
        // Bitrate is near enough constant inside one recording, so half the length is half the
        // bytes. Weighing the original would refuse an upload most of which is never sent.
        let audio = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio,
                                   startedAt: at(0), duration: 60 * 60,
                                   byteCount: 600 * 1024 * 1024)
        let plan = SessionTrimPlan.plan(
            window: window(0, 30), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [audio])

        #expect(plan.approximateBytesSent == 300 * 1024 * 1024)
        #expect(!plan.exceedsSizeAllowance)
    }

    @Test func tooHeavyIsRefusedEvenWithNoVideoInIt() {
        let photos = (0..<200).map {
            TrimmableMedia(relativePath: "media/p\($0).jpg", kind: .photo,
                           startedAt: at(Double($0) / 10), duration: nil,
                           byteCount: 4 * 1024 * 1024)
        }
        let plan = SessionTrimPlan.plan(
            window: window(0, 60), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: photos)

        #expect(plan.videoSecondsSent == 0)
        #expect(!plan.exceedsVideoAllowance)
        #expect(plan.exceedsSizeAllowance)     // 800 MB of photographs is still too much at once
        #expect(plan.exceedsAnAllowance)
    }

    @Test func aFileNothingWeighedCountsAsNothingRatherThanBlockingTheUpload() {
        // Refusing an upload over a number we do not have would strand somebody over a file that
        // may be four seconds long. Erring towards letting it go matches every other unknown here.
        let unmeasured = TrimmableMedia(relativePath: "media/v1.mov", kind: .video,
                                        startedAt: at(10), duration: nil, byteCount: nil)
        let plan = SessionTrimPlan.plan(
            window: window(0, 60), startedAt: start, endedAt: at(60),
            readingTimes: [], markerTimes: [], media: [unmeasured])

        #expect(plan.media[0].outcome == .sentWhole)
        #expect(plan.videoSecondsSent == 0)
        #expect(!plan.exceedsAnAllowance)
    }


    // MARK: - Sending the video smaller instead of sending less of it

    private func clip(_ minutes: Double, height: Int = 2160, fps: Double = 60,
                      bytesPerSecond: Int64 = 3_000_000) -> TrimmableMedia {
        TrimmableMedia(relativePath: "media/v.mov", kind: .video, startedAt: at(0),
                       duration: minutes * 60,
                       byteCount: Int64(minutes * 60) * bytesPerSecond,
                       videoHeight: height, videoFrameRate: fps)
    }

    @Test func halvingTheLinesQuartersTheBytes() {
        // Area, not height. Treating 4K to 1080p as a half would promise a saving twice as small
        // as the real one and send somebody to a harsher setting than they needed.
        let quality = VideoQuality(resolution: .hd1080, frameRate: .asRecorded)
        #expect(quality.approximateBytes(from: 4000, sourceHeight: 2160, sourceFrameRate: 60) == 1000)
    }

    @Test func droppingTheFrameRateScalesWithIt() {
        let quality = VideoQuality(resolution: .asRecorded, frameRate: .fps24)
        #expect(quality.approximateBytes(from: 6000, sourceHeight: 1080, sourceFrameRate: 60) == 2400)
    }

    @Test func nothingIsEverEstimatedLargerThanItStarted() {
        // A 720p original "sent at 1080p" is the same picture with more pixels of nothing. An
        // estimate that grew would offer an upscale as a way to fit under a ceiling.
        let quality = VideoQuality(resolution: .hd1080, frameRate: .fps60)
        #expect(quality.approximateBytes(from: 1000, sourceHeight: 720, sourceFrameRate: 24) == 1000)
    }

    @Test func aWindowTooHeavyAtFullQualityCanFitAtASmallerOne() {
        // Four minutes of 4K60: inside the video allowance, far outside the byte one. This is the
        // case the quality offer exists for — the footage is worth keeping, all of it.
        let plan = SessionTrimPlan.plan(
            window: window(0, 10), startedAt: start, endedAt: at(10),
            readingTimes: [], markerTimes: [], media: [clip(4)])

        #expect(!plan.exceedsVideoAllowance)
        #expect(plan.exceedsSizeAllowance)

        let smaller = plan.smallestQualityThatFits()
        #expect(smaller != nil)
        #expect(smaller?.changesAnything == true)
        #expect(plan.fits(atVideoQuality: smaller!))
    }

    @Test func tooLongIsNotSomethingQualityCanSolve() {
        // Eight minutes is over the video allowance however small the picture is. The answer is
        // clips, and the screen must not offer a re-encode that would still be refused.
        let plan = SessionTrimPlan.plan(
            window: window(0, 20), startedAt: start, endedAt: at(20),
            readingTimes: [], markerTimes: [], media: [clip(8, height: 720, fps: 24,
                                                           bytesPerSecond: 100_000)])

        #expect(plan.exceedsVideoAllowance)
        #expect(!plan.exceedsSizeAllowance)
        // Small enough to send; still too long, so quality changes nothing about the refusal.
        #expect(plan.fits(atVideoQuality: VideoQuality(resolution: .hd720, frameRate: .fps24)))
    }

    @Test func qualityLeavesPhotographsAndSoundAlone() {
        let photo = TrimmableMedia(relativePath: "media/p.jpg", kind: .photo, startedAt: at(1),
                                   duration: nil, byteCount: 4 * 1024 * 1024)
        let audio = TrimmableMedia(relativePath: "media/a.m4a", kind: .audio, startedAt: at(0),
                                   duration: 600, byteCount: 8 * 1024 * 1024)
        let plan = SessionTrimPlan.plan(
            window: window(0, 20), startedAt: start, endedAt: at(20),
            readingTimes: [], markerTimes: [], media: [photo, audio])

        let at720 = plan.approximateBytesSent(atVideoQuality: VideoQuality(resolution: .hd720,
                                                                          frameRate: .fps24))
        #expect(at720 == plan.approximateBytesSent)
    }

    @Test func theFirstOfferedQualityChangesNothing() {
        // The list has to open with "as recorded": a screen whose first row silently degrades
        // evidence is a screen that degrades evidence.
        #expect(VideoQuality.offered.first?.changesAnything == false)
    }

}

/// What the exported document actually contains when a window is chosen (item 210).
///
/// The plan above decides; this proves the decision reaches the bytes that are uploaded. They are
/// separate suites because a plan that is right and an export that ignores it is exactly the shape
/// of bug that ships.
@Suite("Exporting a trimmed session")
@MainActor
struct TrimmedExportTests {

    private let start = Date(timeIntervalSince1970: 1_787_600_000)

    private func fixture() async throws -> (DeviceDataExporter.Request, ReadingLog, SessionFileStore, UUID) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("trim-\(UUID().uuidString)", isDirectory: true)
        let files = SessionFileStore(root: root)
        let id = UUID()
        try files.createDirectories(for: id)

        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        // Four readings a minute apart, so a window can take the middle two.
        for (index, offset) in [0.0, 60.0, 120.0, 180.0].enumerated() {
            try await log.append(FieldReading(
                at: start.addingTimeInterval(offset), precision: .millisecond,
                sequence: index + 1, triggeredBy: .interval,
                measurements: ["emf": .number(40 + Double(index), unit: "uT")]))
        }
        try await log.close()

        let request = DeviceDataExporter.Request(
            sessionId: id, startedAt: start, endedAt: start.addingTimeInterval(180),
            locationLabel: "Back bedroom", deviceModel: "iPhone17,1",
            timezone: "America/Chicago", batteryPercentAtStart: 82,
            trigger: SamplingPolicy.default.trigger(), includedMedia: [])

        return (request, log, files, id)
    }

    @Test func aWindowKeepsOnlyTheReadingsInsideIt() async throws {
        var (request, log, files, _) = try await fixture()
        request.window = SessionWindow(start: start.addingTimeInterval(50),
                                       end: start.addingTimeInterval(130))

        let document = try await DeviceDataExporter(files: files).buildDocument(request, log: log)
        let json = try JSONSerialization.jsonObject(with: document) as! [String: Any]
        let readings = json["readings"] as! [[String: Any]]

        #expect(readings.count == 2)
    }

    @Test func aTrimmedDocumentDeclaresTheWindowAsItsSpan() async throws {
        var (request, log, files, _) = try await fixture()
        let from = start.addingTimeInterval(50)
        let to   = start.addingTimeInterval(130)
        request.window = SessionWindow(start: from, end: to)

        let document = try await DeviceDataExporter(files: files).buildDocument(request, log: log)
        let json = try JSONSerialization.jsonObject(with: document) as! [String: Any]
        let session = json["session"] as! [String: Any]

        // Keeping the ORIGINAL span would tell every reader the session ran three minutes and
        // then hand them two readings — which reads as missing data rather than as an excerpt.
        let started = try #require(session["started_at"] as? String)
        let ended = try #require(session["ended_at"] as? String)
        #expect(started == DeviceDataJSON.iso8601.format(from))
        #expect(ended == DeviceDataJSON.iso8601.format(to))
    }

    @Test func noWindowSendsEverything() async throws {
        let (request, log, files, _) = try await fixture()

        // The default. Every existing caller passes no window and must be unaffected.
        let document = try await DeviceDataExporter(files: files).buildDocument(request, log: log)
        let json = try JSONSerialization.jsonObject(with: document) as! [String: Any]

        #expect((json["readings"] as! [[String: Any]]).count == 4)
        let session = json["session"] as! [String: Any]
        #expect(session["started_at"] as? String == DeviceDataJSON.iso8601.format(start))
    }

    @Test func theTrimmedDocumentIsStillValidForTheFormat() async throws {
        var (request, log, files, _) = try await fixture()
        request.window = SessionWindow(start: start.addingTimeInterval(50),
                                       end: start.addingTimeInterval(130))

        let document = try await DeviceDataExporter(files: files).buildDocument(request, log: log)

        // An excerpt is still a Device Data Format v1 document — the server parses it with the
        // same reader, and a trim that produced something subtly unreadable would fail at upload
        // rather than here.
        let decoded = try JSONSerialization.jsonObject(with: document) as? [String: Any]
        #expect(decoded?["format_version"] as? String == "1.0.0")
        #expect(decoded?["device"] != nil)
        #expect(decoded?["readings"] != nil)
    }

    @Test func aWindowThatCatchesNothingProducesAnEmptyReadingsArray() async throws {
        var (request, log, files, _) = try await fixture()
        request.window = SessionWindow(start: start.addingTimeInterval(1_000),
                                       end: start.addingTimeInterval(2_000))

        // Not an error, and not a crash: the screen refuses to send an empty window before it gets
        // here, but the exporter must still produce something the format accepts rather than a
        // document with a torn readings array.
        let document = try await DeviceDataExporter(files: files).buildDocument(request, log: log)
        let json = try JSONSerialization.jsonObject(with: document) as! [String: Any]
        #expect((json["readings"] as! [[String: Any]]).isEmpty)
    }
}

/// Moving a reading's offset into a recording that has been cut (item 210).
///
/// **Why this is not cosmetic.** `start_offset_seconds` says how far into the recording a moment
/// sits, and the player reconstructs where the recording begins by subtracting it from the
/// reading's time. Cut an hour to ten minutes and every offset still counts from a beginning
/// that is no longer in the file — so the audio lands an hour away from the readings it belongs
/// to, and hearing what happened at the spike, which is the entire point, is broken.
@Suite("Rebasing audio offsets after a cut")
struct AudioOffsetRebaseTests {

    private func rebased(_ json: String, path: String = "media/a.m4a", by seconds: TimeInterval) -> String {
        String(data: DeviceDataExporter.rebaseAudioOffsets(
            forFilename: path, by: seconds, in: Data(json.utf8)), encoding: .utf8)!
    }

    @Test func anOffsetMovesBackByTheAmountCutFromTheFront() {
        let json = #"{"readings":[{"at":"x","audio_ref":{"filename":"media/a.m4a","start_offset_seconds":1240}}]}"#
        #expect(rebased(json, by: 1200).contains(#""start_offset_seconds":40"#))
    }

    @Test func everyReadingNamingTheFileIsMoved() {
        let json = #"{"readings":[{"audio_ref":{"filename":"media/a.m4a","start_offset_seconds":1300}},{"audio_ref":{"filename":"media/a.m4a","start_offset_seconds":1400}}]}"#
        let out = rebased(json, by: 1200)
        #expect(out.contains(#""start_offset_seconds":100"#))
        #expect(out.contains(#""start_offset_seconds":200"#))
    }

    @Test func aDifferentRecordingIsLeftAlone() {
        let json = #"{"readings":[{"audio_ref":{"filename":"media/a.m4a","start_offset_seconds":1240}},{"audio_ref":{"filename":"media/b.m4a","start_offset_seconds":1240}}]}"#
        let out = rebased(json, by: 1200)

        // Only the file that was cut moves. Moving the others would break the recordings that
        // were sent whole, which is most of them.
        #expect(out.contains(#""filename":"media/a.m4a","start_offset_seconds":40"#))
        #expect(out.contains(#""filename":"media/b.m4a","start_offset_seconds":1240"#))
    }

    @Test func anOffsetBeforeTheCutIsClampedToTheStart() {
        // The reading sits at or before the beginning of what was kept, which is where the cut
        // file now starts. A negative offset would be nonsense the player would silently misplace.
        let json = #"{"readings":[{"audio_ref":{"filename":"media/a.m4a","start_offset_seconds":30}}]}"#
        #expect(rebased(json, by: 1200).contains(#""start_offset_seconds":0"#))
    }

    @Test func escapedSlashesAreHandledToo() {
        // A log line written by an older build escapes its slashes and is spliced in verbatim.
        let json = #"{"readings":[{"audio_ref":{"filename":"media\/a.m4a","start_offset_seconds":1240}}]}"#
        #expect(rebased(json, by: 1200).contains(#""start_offset_seconds":40"#))
    }

    @Test func aRefWithNoOffsetIsLeftUntouched() {
        let json = #"{"readings":[{"audio_ref":{"filename":"media/a.m4a","media_type":"audio/mp4"}}]}"#
        #expect(rebased(json, by: 1200) == json)
    }

    @Test func cuttingNothingChangesNothing() {
        let json = #"{"readings":[{"audio_ref":{"filename":"media/a.m4a","start_offset_seconds":1240}}]}"#
        #expect(rebased(json, by: 0) == json)
    }

    @Test func theResultIsStillValidJSON() throws {
        let json = #"{"readings":[{"at":"2026-01-01T00:00:00.000Z","audio_ref":{"filename":"media/a.m4a","start_offset_seconds":1240.5},"note":"a filename in prose: media/a.m4a"}]}"#
        let out = rebased(json, by: 1200)

        // The rewrite edits raw text, so "it still parses" is the assertion that matters most —
        // a document broken here fails at upload with nothing useful to say.
        let parsed = try JSONSerialization.jsonObject(with: Data(out.utf8)) as? [String: Any]
        #expect(parsed != nil)
        #expect(out.contains("40.5"))
    }

    @Test func aDocumentWithNoRefsAtAllComesBackUnchanged() {
        let json = #"{"readings":[{"at":"x","measurements":{}}]}"#
        #expect(rebased(json, by: 1200) == json)
    }
}

/// Dragging the in and out points (item 210).
///
/// Ben asked for this to behave like a video trimmer, and the awkward parts of one are all about
/// what happens when a finger goes somewhere the model has to refuse: past the other handle, off
/// the end of the track, or onto a session that never recorded an end time.
@Suite("The in and out points of a trim")
struct SessionTrimRangeTests {

    private let start = Date(timeIntervalSince1970: 1_800_000_000)
    private var end: Date { start.addingTimeInterval(600) }   // ten minutes

    private func range() -> SessionTrimRange {
        SessionTrimRange(startedAt: start, endedAt: end, lastReadingAt: nil)
    }

    @Test func itOpensWithTheWholeSessionSelected() {
        let r = range()

        // The first thing somebody sees is the whole night selected — a trimmer that opened with
        // a guess at the interesting part would be guessing about evidence.
        #expect(r.inPoint == start)
        #expect(r.outPoint == end)
        #expect(r.isWholeSession)
        #expect(r.keptDuration == 600)
    }

    @Test func draggingTheInPointMovesTheStartOnly() {
        var r = range()
        r.moveIn(toFraction: 0.5)

        #expect(r.inPoint == start.addingTimeInterval(300))
        #expect(r.outPoint == end)
        #expect(!r.isWholeSession)
    }

    @Test func draggingTheOutPointMovesTheEndOnly() {
        var r = range()
        r.moveOut(toFraction: 0.25)

        #expect(r.inPoint == start)
        #expect(r.outPoint == start.addingTimeInterval(150))
    }

    @Test func theInPointCannotBeDraggedPastTheOutPoint() {
        var r = range()
        r.moveOut(toFraction: 0.5)
        r.moveIn(toFraction: 0.9)

        // It stops one second short rather than crossing. A crossed pair would select a negative
        // window and upload nothing at all.
        #expect(r.inPoint == r.outPoint.addingTimeInterval(-SessionTrimRange.minimumDuration))
        #expect(r.keptDuration == SessionTrimRange.minimumDuration)
    }

    @Test func theOutPointCannotBeDraggedBeforeTheInPoint() {
        var r = range()
        r.moveIn(toFraction: 0.5)
        r.moveOut(toFraction: 0.1)

        #expect(r.outPoint == r.inPoint.addingTimeInterval(SessionTrimRange.minimumDuration))
    }

    @Test func aFingerDraggedOffTheEndOfTheTrackStopsAtTheEnd() {
        var r = range()
        r.moveIn(toFraction: -3)
        #expect(r.inPoint == start)

        r.moveOut(toFraction: 42)
        #expect(r.outPoint == end)
    }

    @Test func aSessionWithNoEndUsesItsLastReading() {
        // An interrupted session has no honest end time, and a trimmer with no right-hand end has
        // nothing to drag.
        let last = start.addingTimeInterval(120)
        let r = SessionTrimRange(startedAt: start, endedAt: nil, lastReadingAt: last)

        #expect(r.sessionEnd == last)
        #expect(r.outPoint == last)
    }

    @Test func aSessionWithNothingAtAllStillHasATrack() {
        // No end, no readings. The track must still have width or every fraction divides by zero.
        let r = SessionTrimRange(startedAt: start, endedAt: nil, lastReadingAt: nil)

        #expect(r.duration >= SessionTrimRange.minimumDuration)
        #expect(r.fraction(of: r.sessionEnd) == 1)
    }

    @Test func aSessionThatEndedBeforeItBeganStillHasATrack() {
        let r = SessionTrimRange(startedAt: start, endedAt: start.addingTimeInterval(-60),
                                 lastReadingAt: nil)
        #expect(r.duration >= SessionTrimRange.minimumDuration)
    }

    @Test func fractionsAndDatesAgreeWithEachOther() {
        let r = range()
        for fraction in [0.0, 0.25, 0.5, 0.75, 1.0] {
            #expect(abs(r.fraction(of: r.date(atFraction: fraction)) - fraction) < 0.0001)
        }
    }

    @Test func resettingPutsBothHandlesBack() {
        var r = range()
        r.moveIn(toFraction: 0.3)
        r.moveOut(toFraction: 0.6)
        r.reset()

        #expect(r.isWholeSession)
        #expect(r.window.duration == 600)
    }
}

/// Naming a clip (item 210).
@Suite("What a trimmed session is called")
struct ClipLabelTests {
    private let start = Date(timeIntervalSince1970: 1_800_000_000)

    @Test func aClipCarriesItsInAndOutTimes() {
        let window = SessionWindow(start: start.addingTimeInterval(20 * 60),
                                   end: start.addingTimeInterval(30 * 60))
        let label = SessionTrimPlan.clipLabel(base: "back bedroom", window: window,
                                              sessionStart: start, isWholeSession: false)
        // The times say what the clip is; a counter would only say it is the third of something.
        #expect(label == "back bedroom (20:00–30:00)")
    }

    @Test func aWholeSessionKeepsItsNameUntouched() {
        let window = SessionWindow(start: start, end: start.addingTimeInterval(3600))
        #expect(SessionTrimPlan.clipLabel(base: "back bedroom", window: window,
                                          sessionStart: start, isWholeSession: true) == "back bedroom")
        #expect(SessionTrimPlan.clipLabel(base: nil, window: window,
                                          sessionStart: start, isWholeSession: true) == nil)
    }

    @Test func aClipOfAnUnnamedSessionStillSaysWhatItIs() {
        let window = SessionWindow(start: start.addingTimeInterval(5),
                                   end: start.addingTimeInterval(65))
        #expect(SessionTrimPlan.clipLabel(base: nil, window: window,
                                          sessionStart: start, isWholeSession: false) == "Clip (0:05–1:05)")
        #expect(SessionTrimPlan.clipLabel(base: "   ", window: window,
                                          sessionStart: start, isWholeSession: false) == "Clip (0:05–1:05)")
    }

    @Test func hoursAppearOnlyWhenNeeded() {
        #expect(SessionTrimPlan.clock(59) == "0:59")
        #expect(SessionTrimPlan.clock(600) == "10:00")
        #expect(SessionTrimPlan.clock(3661) == "1:01:01")
    }
}
