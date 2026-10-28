import Foundation
import SwiftUI
import BenKit

/// One session's room scanning, from the room picker to the upload (LiDAR plan, phase 1).
///
/// Owned by the live screen and handed to the scan screens, so a presentation never has to ask a
/// section of a form to raise a sheet (that shape dismissed the enclosing sheet once and cost an
/// App Store review). Every step's state is here; the views only draw it.
@Observable
@MainActor
final class ScanFlow {

    /// Whether this DEBUG run stands a fixture room in for the sensor (`-scanFakeRoom`).
    static var usesFakeRoom: Bool {
        #if DEBUG
        ProcessInfo.processInfo.arguments.contains("-scanFakeRoom")
        #else
        false
        #endif
    }

    enum Step: Equatable {
        case idle
        /// Asking which place this session is at.
        case choosingPlace
        /// The scanner is open for this room.
        case scanning(room: String, holdId: UUID?)
        /// Scanned; asking what to keep.
        case choosingRetention(ScanCapture)
        case uploading(ScanCapture, ScanManifest.Retention)
        /// The room has a scan the person may not replace; the scan is still on the phone.
        case conflict(ScanCapture, ScanManifest.Retention, PlaceScanConflict)
        case done(PlaceScanUploadResult)
        /// Kept on the phone: the server could not be reached, or the person chose to.
        case kept(ScanCapture, String)
    }

    private(set) var step: Step = .idle
    /// The place's rooms, their scans and holds. Nil until asked, or when it could not be had.
    private(set) var rooms: PlaceScanRoomList?
    /// Why the rooms could not be had — offline is a state, not an error.
    private(set) var roomsProblem: String?
    private(set) var isOffline = false
    /// The last thing that went wrong, in the server's words when it sent some.
    var problem: String?
    /// The scan to open in the viewer.
    var viewing: ScanViewerSource?
    /// The room whose offers the custodian is deciding.
    var decidingOffers: PlaceScanRoom?
    /// The hold taken for the scan in hand, sent with its upload so the server lets it go.
    private var holdId: UUID?

    private let client: PlaceScanClient
    private let scans: ScanStore
    private let store: FieldSessionStore
    let sessionId: UUID

    init(client: PlaceScanClient, scans: ScanStore, store: FieldSessionStore, sessionId: UUID) {
        self.client = client
        self.scans = scans
        self.store = store
        self.sessionId = sessionId
    }

    private var active: ActiveFieldSession? {
        store.active?.sessionId == sessionId ? store.active : nil
    }

    var placeId: UUID? { active?.placeId ?? store.summary(for: sessionId)?.placeId }
    var placeName: String? { active?.placeName ?? store.summary(for: sessionId)?.placeName }

    /// The room the session is in, as the place's list knows it.
    var currentRoom: PlaceScanRoom? { rooms?.room(named: active?.room) }

    // MARK: The place and its rooms

    /// Asks the server for the place's rooms. Unreachable is remembered as offline, so the button
    /// can say a scan may become a comparison rather than pretending the room is unscanned.
    func refresh() async {
        guard let placeId else { rooms = nil; return }
        switch await client.rooms(placeId: placeId) {
        case .ok(let list):
            rooms = list
            roomsProblem = nil
            isOffline = false
        case .failed(let reason, let status):
            isOffline = status == nil
            roomsProblem = status == nil ? nil : reason
        case .sessionEnded:
            roomsProblem = "Your session ended — sign in again to scan rooms."
        case .rateLimited:
            roomsProblem = "Too many requests — try again in a moment."
        }
    }

    func choosePlace() { step = .choosingPlace }

    /// Keeps the chosen place with the session and loads its rooms.
    func placeChosen(id: UUID, name: String?) async {
        do {
            try store.setPlace(sessionId, placeId: id, placeName: name)
        } catch {
            problem = error.localizedDescription
        }
        step = .idle
        await refresh()
    }

    func cancelChoosingPlace() {
        if step == .choosingPlace { step = .idle }
    }

    // MARK: Scanning

    /// Takes the room for twenty minutes, then opens the scanner. A room somebody else is scanning,
    /// or one with a scan this person may not replace, is answered with the server's sentence.
    func scan(room: String) async {
        guard let placeId else { step = .choosingPlace; return }
        problem = nil
        // Offline, the scan goes ahead: the button already said it may become a comparison.
        if isOffline {
            step = .scanning(room: room, holdId: nil)
            return
        }
        switch await client.takeHold(placeId: placeId, room: room) {
        case .held(let hold):
            holdId = hold.holdId
            step = .scanning(room: room, holdId: hold.holdId)
        case .heldByOther(let held):
            problem = held.says
            await refresh()
        case .alreadyScanned(let conflict):
            problem = conflict.says
            await refresh()
        case .failed(let error):
            if case .failed(nil) = error {
                // Not reachable now: scan anyway, as offline.
                isOffline = true
                step = .scanning(room: room, holdId: nil)
            } else {
                problem = error.message
            }
        }
    }

    /// The scanner finished: ask what to keep.
    func scanned(_ capture: ScanCapture) {
        step = .choosingRetention(capture)
    }

    /// The scanner was closed without a scan; the room is let go.
    func cancelScanning() async {
        if case .scanning(_, let holdId) = step, let holdId, let placeId {
            await client.releaseHold(placeId: placeId, holdId: holdId)
        }
        holdId = nil
        step = .idle
    }

    /// What each choice would weigh, for the retention sheet.
    func estimatedBytes(_ capture: ScanCapture, _ retention: ScanManifest.Retention) -> Int64 {
        ScanPackage.estimatedBytes(of: scans.captureFolder(capture.id), retention: retention)
    }

    // MARK: Sending

    /// Packs the scan as chosen and sends it.
    func send(_ capture: ScanCapture, retention: ScanManifest.Retention, intent: PlaceScanClient.Intent = .baseline) async {
        guard let placeId = capture.placeId ?? placeId else { step = .choosingPlace; return }
        step = .uploading(capture, retention)
        problem = nil

        let file = scans.packageURL(for: capture.id)
        do {
            try ScanPackage.write(capture, from: scans.captureFolder(capture.id), retention: retention, to: file)
        } catch {
            problem = error.localizedDescription
            step = .choosingRetention(capture)
            return
        }

        switch await client.upload(placeId: placeId, file: file, room: capture.room, retention: retention,
                                   holdId: holdId, intent: intent) {
        case .uploaded(let result):
            holdId = nil
            // The phone keeps a copy as the place's scan, so it opens without downloading it again.
            try? scans.keepAsPlaceScan(file, scanId: result.scan.id)
            scans.remove(capture.id)
            await active?.noteRoomScan(room: result.scan.roomName, placeScanId: result.scan.id,
                                       version: result.scan.version, outcome: result.outcome,
                                       durationSeconds: capture.durationSeconds)
            step = .done(result)
            await refresh()
        case .conflict(let conflict):
            step = .conflict(capture, retention, conflict)
        case .failed(let error):
            if case .failed(nil) = error {
                step = .kept(capture, "The server couldn't be reached. The scan is kept on this phone; send it again when you have a signal.")
            } else {
                problem = error.message
                step = .choosingRetention(capture)
            }
        }
    }

    /// The person's answer to a 409: offer it, replace it when allowed, keep it, or let it go.
    func resolve(_ choice: String) async {
        guard case .conflict(let capture, let retention, _) = step else { return }
        switch choice {
        case "offer": await send(capture, retention: retention, intent: .offer)
        case "replace": await send(capture, retention: retention, intent: .replace)
        case "compare":
            // Kept on the phone with the session; comparing it against the room's scan reads it from here.
            step = .kept(capture, "Kept on this phone as a comparison with the room's scan.")
        default:
            await discard(capture)
        }
    }

    /// Lets a scan go without sending it.
    func discard(_ capture: ScanCapture) async {
        if let holdId, let placeId {
            await client.releaseHold(placeId: placeId, holdId: holdId)
        }
        holdId = nil
        scans.remove(capture.id)
        step = .idle
    }

    /// A scan kept on the phone, sent again.
    func sendAgain(_ capture: ScanCapture) async {
        await send(capture, retention: .modelAndPictures)
    }

    func finish() { step = .idle }

    /// The room the scanner is open for, while it is.
    var scanningRoom: String? {
        if case .scanning(let room, _) = step { room } else { nil }
    }

    /// Whether the sheet after the scanner is up.
    var isSending: Bool {
        switch step {
        case .choosingRetention, .uploading, .conflict, .done, .kept: true
        default: false
        }
    }

    /// The sending sheet was swiped away. A scan not yet sent stays on the phone (it is listed
    /// as waiting), and an upload under way is not abandoned by a swipe.
    func dismissSending() {
        if case .uploading = step { return }
        step = .idle
    }

    // MARK: Viewing and offers

    /// Opens a place's scan: the phone's copy when it has one, the server's otherwise.
    func view(_ summary: PlaceScanSummary, room: String) {
        guard let placeId else { return }
        viewing = ScanViewerSource(placeId: placeId, scanId: summary.id, room: room,
                                   others: rooms?.rooms.compactMap { r in r.current.map { (r.roomName, $0.id) } } ?? [])
    }

    func localFile(for scanId: UUID) -> URL? {
        let url = scans.placeScanURL(scanId)
        return FileManager.default.fileExists(atPath: url.path) ? url : nil
    }

    /// A scan fetched to the phone, or why it could not be.
    enum Fetched {
        case file(URL)
        case problem(String)
    }

    /// Fetches a place's scan to the phone, once.
    func fetch(placeId: UUID, scanId: UUID) async -> Fetched {
        if let local = localFile(for: scanId) { return .file(local) }
        switch await client.download(placeId: placeId, scanId: scanId, to: scans.placeScanURL(scanId)) {
        case .ok(let url): return .file(url)
        case .failed(let reason, _): return .problem(reason ?? "The scan couldn't be fetched. Check the signal and try again.")
        case .sessionEnded: return .problem("Your session ended — sign in again to see the scan.")
        case .rateLimited: return .problem("Too many requests — try again in a moment.")
        }
    }

    func versions(of room: PlaceScanRoom) async -> LoadResult<[PlaceScanSummary]> {
        guard let placeId else { return .failed(reason: nil) }
        return await client.versions(placeId: placeId, roomKey: room.roomKey)
    }

    func decide(_ scanId: UUID, accept: Bool, note: String?) async -> String? {
        guard let placeId else { return nil }
        let outcome = accept
            ? await client.accept(placeId: placeId, scanId: scanId, note: note)
            : await client.decline(placeId: placeId, scanId: scanId, note: note)
        await refresh()
        switch outcome {
        case .success: return nil
        case .failure(let error): return error.message
        }
    }
}

/// Which scan the viewer opens, and the place's other rooms it can switch to.
struct ScanViewerSource: Identifiable, Equatable {
    let placeId: UUID
    let scanId: UUID
    let room: String
    let others: [(String, UUID)]

    var id: UUID { scanId }

    static func == (a: ScanViewerSource, b: ScanViewerSource) -> Bool { a.scanId == b.scanId }
}
