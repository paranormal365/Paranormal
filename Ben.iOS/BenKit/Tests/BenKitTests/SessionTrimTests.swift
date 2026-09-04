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
