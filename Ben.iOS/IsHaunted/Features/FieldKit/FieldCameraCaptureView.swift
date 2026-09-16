import SwiftUI
import AVFoundation
import BenKit

/// Taking a photo or a clip without leaving the app.
///
/// Ben, 2026-09-16: "I do not want the app to leave to use the iphone video or photo app. I want
/// it internal." Leaving was not only a preference: the system camera took this session's camera
/// and its microphone, and coming back left a frozen preview and a session recording nothing.
///
/// A clip carries no sound of its own — the session's audio track is already running and is the
/// record of what was heard. Both land on the same timeline in the review, so the sound is there;
/// it is simply not recorded twice, and the microphone never changes hands.
struct FieldCameraCaptureView: View {
    @Environment(\.dismiss) private var dismiss

    let camera: FieldCameraSession
    /// Whether the camera was already running for this session, so leaving puts it back as found.
    let wasRunning: Bool
    /// Whether this session records video at all — decides if the clip mode is offered.
    let allowsVideo: Bool
    /// Which button was pressed to get here, so Video opens ready to record.
    let initialKind: CaptureKind
    let onCaptured: (URL, CaptureKind, Double?) async -> Void

    @State private var kind: CaptureKind = .photo
    @State private var busy = false
    @State private var problem: String?

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            CameraPreview(session: camera.session)
                .ignoresSafeArea()

            VStack {
                topRow
                Spacer()
                bottomRow
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 16)
        }
        .task {
            kind = allowsVideo ? initialKind : .photo
            if !camera.isRunning { camera.start() }
        }
        .onDisappear {
            // Put the camera back the way it was found: a session that never asked for video
            // should not be left with the camera running behind it.
            if !wasRunning && !camera.isRecordingClip { camera.stop() }
        }
        .interactiveDismissDisabled(camera.isRecordingClip)
    }

    private var topRow: some View {
        HStack(alignment: .top) {
            Button {
                dismiss()
            } label: {
                Image(systemName: "xmark")
                    .font(.title3.bold())
                    .foregroundStyle(Theme.bone)
                    .frame(width: 44, height: 44)
                    .background(.black.opacity(0.45), in: Circle())
            }
            .disabled(camera.isRecordingClip)
            .accessibilityLabel("Close the camera")
            .accessibilityIdentifier("camera-close")

            Spacer()

            if let started = camera.clipStartedAt {
                TimelineView(.periodic(from: .now, by: 1)) { context in
                    Label(SessionClock.elapsed(from: started, to: context.date),
                          systemImage: "record.circle")
                        .font(.callout.monospacedDigit().bold())
                        .foregroundStyle(Theme.danger)
                        .padding(.horizontal, 12).padding(.vertical, 8)
                        .background(.black.opacity(0.45), in: Capsule())
                }
                .accessibilityIdentifier("camera-clip-elapsed")
            }
        }
    }

    private var bottomRow: some View {
        VStack(spacing: 14) {
            // The camera's own problem outranks the shutter's: "no camera on this device" is the
            // truth, and "give it a moment and try again" over the top of it is advice that will
            // never work.
            if let problem = camera.problem ?? problem {
                Text(problem)
                    .font(.caption)
                    .foregroundStyle(Theme.danger)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 12).padding(.vertical, 8)
                    .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 10))
                    .accessibilityIdentifier("camera-problem")
            }

            if kind == .video && allowsVideo {
                // Said plainly, because a silent clip looks like a fault to anybody who does not
                // know the session is recording the sound itself.
                Text("Sound stays on the session's own track — the clip itself is silent.")
                    .font(.caption2)
                    .foregroundStyle(Theme.bone.opacity(0.85))
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 12).padding(.vertical, 6)
                    .background(.black.opacity(0.45), in: RoundedRectangle(cornerRadius: 10))
            }

            if allowsVideo && !camera.isRecordingClip {
                Picker("What to take", selection: $kind) {
                    Text("Photo").tag(CaptureKind.photo)
                    Text("Video").tag(CaptureKind.video)
                }
                .pickerStyle(.segmented)
                .frame(maxWidth: 260)
                .accessibilityIdentifier("camera-kind")
            }

            Button {
                Task { await shutter() }
            } label: {
                ZStack {
                    Circle()
                        .strokeBorder(Theme.bone, lineWidth: 4)
                        .frame(width: 78, height: 78)
                    if camera.isRecordingClip {
                        RoundedRectangle(cornerRadius: 6)
                            .fill(Theme.danger)
                            .frame(width: 32, height: 32)
                    } else {
                        Circle()
                            .fill(kind == .video ? Theme.danger : Theme.bone)
                            .frame(width: 62, height: 62)
                    }
                }
            }
            // A shutter that cannot take anything is not offered: on a device with no camera it
            // would fail on every press, and the sentence above already says why.
            .disabled(busy || (camera.problem != nil && !camera.isRunning))
            .opacity(camera.problem != nil && !camera.isRunning ? 0.4 : 1)
            .accessibilityLabel(shutterLabel)
            .accessibilityIdentifier("camera-shutter")
        }
    }

    private var shutterLabel: String {
        if camera.isRecordingClip { return "Stop the clip" }
        return kind == .video ? "Start a clip" : "Take a photo"
    }

    private func shutter() async {
        problem = nil
        busy = true
        defer { busy = false }

        do {
            if kind == .video && allowsVideo {
                if camera.isRecordingClip {
                    let started = camera.clipStartedAt
                    let url = try await camera.finishClip()
                    let seconds = started.map { Date().timeIntervalSince($0) }
                    await onCaptured(url, .video, seconds)
                    dismiss()
                } else {
                    try camera.startClip()
                }
            } else {
                let url = try await camera.capturePhoto()
                await onCaptured(url, .photo, nil)
                dismiss()
            }
        } catch {
            problem = error.localizedDescription
        }
    }
}
