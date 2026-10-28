import SwiftUI
import BenKit

/// Every screen the scan flow raises, attached once to the live screen that owns the flow — never
/// to a section, whose rows changing can take an enclosing sheet down with it.
struct ScanPresentations: ViewModifier {
    let flow: ScanFlow?
    let sessionId: UUID
    let active: ActiveFieldSession?
    let scans: ScanStore

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: Binding(get: { flow?.step == .choosingPlace },
                                        set: { if !$0 { flow?.cancelChoosingPlace() } })) {
                if let flow {
                    ChoosePlaceSheet(latitude: active?.sample.position?.latitude,
                                     longitude: active?.sample.position?.longitude) { id, name in
                        await flow.placeChosen(id: id, name: name)
                    }
                }
            }
            .fullScreenCover(isPresented: Binding(get: { flow?.scanningRoom != nil }, set: { _ in })) {
                if let flow, let room = flow.scanningRoom {
                    RoomScanView(room: room, placeId: flow.placeId, sessionId: sessionId, scans: scans,
                                 onFinished: { flow.scanned($0) },
                                 onCancel: { Task { await flow.cancelScanning() } })
                }
            }
            .fullScreenCover(isPresented: Binding(get: { flow?.comparing != nil }, set: { _ in })) {
                if let flow, let comparing = flow.comparing {
                    CompareRoomView(flow: flow, room: comparing.room, baseline: comparing.baseline,
                                    sessionId: sessionId, scans: scans)
                }
            }
            .sheet(isPresented: Binding(get: { flow?.isSending == true },
                                        set: { if !$0 { flow?.dismissSending() } })) {
                if let flow {
                    ScanSendSheet(flow: flow, storageLeftBytes: flow.rooms?.storageLeftBytes)
                }
            }
            .fullScreenCover(item: Binding(get: { flow?.viewing }, set: { flow?.viewing = $0 })) { source in
                if let flow { ScanViewerView(source: source, flow: flow) }
            }
            .sheet(item: Binding(get: { flow?.decidingOffers }, set: { flow?.decidingOffers = $0 })) { room in
                if let flow { ScanOffersView(flow: flow, room: room) }
            }
    }
}
