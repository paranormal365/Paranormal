import SwiftUI
import VisionKit

/// The camera, capturing a pass's QR code for the door (item 235 phase 14c).
///
/// Ben, 2026-09-13: *"The scanner screen is just the camera capturing the QR code to use."* It reads one code and
/// closes; the door screen looks the pass up and shows the reservation, which the door taps to check them in.
///
/// `DataScannerViewController` reads the code on the phone itself, so the camera works with no signal.
struct DoorScannerView: View {
    var onCode: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var captured = false

    var body: some View {
        ZStack(alignment: .bottom) {
            PassScanner(paused: captured) { code in
                guard !captured else { return }
                captured = true
                onCode(code)
                dismiss()
            }
            .ignoresSafeArea()

            Text("Point the camera at the pass's code")
                .font(.headline)
                .padding(.horizontal, 16).padding(.vertical, 10)
                .background(.regularMaterial, in: Capsule())
                .padding(.bottom, 40)
        }
        .overlay(alignment: .topTrailing) {
            Button {
                dismiss()
            } label: {
                Image(systemName: "xmark").font(.title3.weight(.semibold)).padding(12)
                    .background(.regularMaterial, in: Circle())
            }
            .padding()
            .accessibilityLabel("Close the camera")
        }
    }
}

/// The system code reader, reading QR codes only.
private struct PassScanner: UIViewControllerRepresentable {
    var paused: Bool
    let onCode: (String) -> Void

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(
            recognizedDataTypes: [.barcode(symbologies: [.qr])],
            qualityLevel: .balanced,
            recognizesMultipleItems: false,
            isHighFrameRateTrackingEnabled: false,
            isHighlightingEnabled: true)
        scanner.delegate = context.coordinator
        return scanner
    }

    func updateUIViewController(_ scanner: DataScannerViewController, context: Context) {
        context.coordinator.onCode = onCode
        if paused {
            if scanner.isScanning { scanner.stopScanning() }
        } else if !scanner.isScanning {
            try? scanner.startScanning()
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(onCode: onCode) }

    @MainActor
    final class Coordinator: NSObject, DataScannerViewControllerDelegate {
        var onCode: (String) -> Void

        init(onCode: @escaping (String) -> Void) { self.onCode = onCode }

        func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
            for item in addedItems {
                if case .barcode(let barcode) = item, let payload = barcode.payloadStringValue, !payload.isEmpty {
                    onCode(payload)
                    return
                }
            }
        }
    }
}
