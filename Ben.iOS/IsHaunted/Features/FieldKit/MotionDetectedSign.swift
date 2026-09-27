import SwiftUI

/// "Motion detected" — the same sign on the live viewfinder and on the replay.
///
/// Ben, 2026-09-27: "It will highlight the video and maybe show a 'Motion Detected' sign when the
/// motion is detected and being played back during the .ben file playback."
struct MotionDetectedSign: View {
    var body: some View {
        Label("Motion detected", systemImage: "figure.walk.motion")
            .font(.caption.bold())
            .padding(.horizontal, 10).padding(.vertical, 5)
            .background(Theme.warning, in: Capsule())
            .foregroundStyle(.black)
            .shadow(color: Theme.warning.opacity(0.8), radius: 8)
            .transition(.scale.combined(with: .opacity))
            .accessibilityIdentifier("motion-detected")
    }
}
