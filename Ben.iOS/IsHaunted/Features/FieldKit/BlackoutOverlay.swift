import SwiftUI
import BenKit

/// The screen, off — while everything underneath keeps running.
///
/// A lit phone in a dark room reaches the recording, the room and everyone in it. This takes the
/// brightness to nothing and covers the screen in black, so the session can keep logging with no
/// light coming off the device at all.
///
/// It is deliberately not a lock: a single tap anywhere brings it back, because fumbling for a
/// specific control in the dark is exactly what somebody cannot do.
struct BlackoutOverlay: View {
    let session: ActiveFieldSession?
    var onWake: () -> Void

    /// The confirmation fades after a moment. Left on screen it would be the very light the
    /// blackout exists to remove.
    @State private var showingHint = true

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            VStack(spacing: 14) {
                // Kept at a whisper so somebody glancing over can tell the session is alive
                // without the screen becoming a lamp again.
                if session != nil {
                    HStack(spacing: 8) {
                        Circle()
                            .fill(Color.red)
                            .frame(width: 6, height: 6)
                            .opacity(0.35)
                        Text("recording")
                            .font(.caption2)
                            .foregroundStyle(.white.opacity(0.22))
                    }
                    .accessibilityElement(children: .combine)
                    .accessibilityLabel("Still recording")
                }

                if showingHint {
                    Text("Tap anywhere to bring the screen back")
                        .font(.caption)
                        .foregroundStyle(.white.opacity(0.30))
                        .transition(.opacity)
                }
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { onWake() }
        .accessibilityAddTraits(.isButton)
        .accessibilityLabel("Screen blacked out. Double tap to bring it back.")
        .accessibilityIdentifier("blackout-overlay")
        .task {
            try? await Task.sleep(for: .seconds(4))
            withAnimation(.easeOut(duration: 1.2)) { showingHint = false }
        }
    }
}
