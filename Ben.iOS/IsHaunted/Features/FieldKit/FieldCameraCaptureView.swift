import SwiftUI
import AVFoundation
import BenKit

/// Taking a photo or a clip without leaving the app.
///
/// Ben, 2026-09-16: "I do not want the app to leave to use the iphone video or photo app. I want
/// it internal." Leaving was not only a preference: the system camera took this session's camera
/// and its microphone, and coming back left a frozen preview and a session recording nothing.
///
/// A clip records the sound too, and the session's own recording steps aside for exactly as long
/// as the clip runs. Ben, 2026-09-16: "the audio is just taken from the video file until stopped
/// and then back to audio — so there is no gap in audio recording, just video added to a part."
/// The microphone is lent before the clip starts and taken back the moment it stops.
struct FieldCameraCaptureView: View {
    @Environment(\.dismiss) private var dismiss

    /// The running session, which owns the microphone and lends it for the length of a clip.
    let session: ActiveFieldSession
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
                close()
            } label: {
                Image(systemName: "xmark")
                    .font(.title3.bold())
                    .foregroundStyle(Theme.bone)
                    .frame(width: 44, height: 44)
                    .background(.black.opacity(0.45), in: Circle())
            }
            // Never disabled. It used to be, for as long as a clip ran — so a clip that would not
            // finish was a screen that could not be left, which is what a freeze looks like from
            // the outside (Ben, 2026-09-17). Leaving with a clip running ends the clip properly
            // instead; see `close()`.
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
                // Said plainly, because the sound moving from one file to another and back is
                // exactly the kind of thing somebody would otherwise read as a gap.
                Text(camera.isRecordingClip
                     ? "The clip is recording the sound. Your own recording starts again when you stop."
                     : "The clip records the sound too — your recording picks it back up when you stop.")
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

    /// Leaves, ending a running clip on the way out rather than refusing to go.
    ///
    /// What was filmed is kept and listed, and the microphone goes back to the session whatever
    /// the clip did — the same order the shutter's own stop uses, for the same reason.
    private func close() {
        Task {
            if camera.isRecordingClip {
                let started = camera.clipStartedAt
                let finished = try? await camera.finishClip()
                await session.takeMicrophoneBackFromTheClip()
                if let url = finished {
                    await onCaptured(url, .video, started.map { Date().timeIntervalSince($0) })
                }
            }
            dismiss()
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
                    let finished: Result<URL, Error>
                    do { finished = .success(try await camera.finishClip()) }
                    catch { finished = .failure(error) }

                    // The microphone comes back before anything else, and WHATEVER the clip did.
                    // Before this it came back only after a clip that finished cleanly — a clip
                    // that could not be finalised left the session lent out for good, refusing to
                    // record for the rest of the night with no sentence saying why.
                    await session.takeMicrophoneBackFromTheClip()

                    let url = try finished.get()
                    let seconds = started.map { Date().timeIntervalSince($0) }
                    await onCaptured(url, .video, seconds)
                    dismiss()
                } else {
                    // Lent first, started second. The engine has to let go of the microphone
                    // before a capture session can take it.
                    await session.lendMicrophoneToTheClip()
                    do {
                        try camera.startClip(withSound: true)
                    } catch {
                        // Nothing is recording now, so the session takes its microphone straight
                        // back rather than leaving the night silent.
                        await session.takeMicrophoneBackFromTheClip()
                        throw error
                    }
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
