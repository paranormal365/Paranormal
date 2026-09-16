import SwiftUI
import AVFoundation
import BenKit

/// Photo, video and the audio recording, from inside a running session.
///
/// Files go straight into the session's own directory — never the temporary one the rest of the
/// app stages uploads in. A field capture may sit on the phone for a week before anyone reviews
/// it, and the system empties tmp whenever it likes.
struct FieldCaptureBar: View {
    @Environment(AppDependencies.self) private var dependencies

    let session: ActiveFieldSession
    /// The session's own camera — the same one the viewfinder shows. Photos and clips are taken
    /// by it rather than by the system camera, so nothing takes this session's camera or its
    /// microphone away mid-recording.
    let camera: FieldCameraSession

    @State private var showingCamera = false
    @State private var cameraKind: CaptureKind = .photo
    /// Whether the camera was already running when the capture screen was opened, read once at
    /// the tap — so closing the screen puts the camera back exactly as it was found.
    @State private var cameraWasRunning = false
    @State private var errorMessage: String?

    private var files: SessionFileStore { dependencies.fieldKit.files }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Capture").font(.caption).foregroundStyle(Theme.fog)
                Spacer()
                if let recording = session.recording {
                    // A recording running is a fact worth keeping on screen: the whole failure
                    // mode here is believing you are recording when you are not.
                    TimelineView(.periodic(from: .now, by: 1)) { context in
                        Label(SessionClock.elapsed(from: recording.startedAt, to: context.date),
                              systemImage: "record.circle")
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(Theme.danger)
                    }
                }
            }

            HStack(spacing: 10) {
                captureButton("Photo", icon: "camera", kind: .photo)
                // Only when this session is set up for video — one fewer thing to fumble past
                // at 3am when it is not what you came to do.
                if session.channels.contains(.video) {
                    captureButton("Video", icon: "video", kind: .video)
                }

                Button {
                    Task {
                        if session.recording == nil {
                            await session.startRecording()
                        } else {
                            await session.stopRecording()
                        }
                    }
                } label: {
                    Label(audioButtonTitle,
                          systemImage: camera.isRecordingClip ? "video"
                                     : (session.recording == nil ? "mic" : "mic.slash"))
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 6)
                }
                .buttonStyle(.bordered)
                .tint(session.recording == nil ? Theme.ecto : Theme.danger)
                // While a clip holds the microphone there is nothing here to start or stop: the
                // video is recording the sound, and pressing this would be two things fighting
                // over one microphone — which is the whole bug this was built to end.
                .disabled(camera.isRecordingClip)
                .accessibilityIdentifier("toggle-audio-recording")
            }

            if let problem = session.recordingProblem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.danger)
                    .accessibilityIdentifier("recording-problem")
            }

            // The camera borrowing the microphone is worth saying and is nobody's fault, so it is a note rather than
            // a red warning: warnings about things that fixed themselves teach people to ignore warnings.
            if let note = session.audioNote {
                Label(note, systemImage: "info.circle")
                    .font(.caption).foregroundStyle(Theme.fog)
                    .accessibilityIdentifier("audio-note")
            }

            if session.captures.isEmpty {
                Text("Nothing captured yet. Anything you take is stamped with where you were.")
                    .font(.caption).foregroundStyle(Theme.fog)
            } else {
                ForEach(session.captures.prefix(6)) { capture in
                    HStack(spacing: 10) {
                        Image(systemName: icon(for: capture.kind))
                            .font(.caption).foregroundStyle(Theme.ecto)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(capture.relativePath
                                    .replacingOccurrences(of: "media/", with: ""))
                                .font(.caption).foregroundStyle(Theme.bone)
                            Text(detail(for: capture))
                                .font(.caption2).foregroundStyle(Theme.fog)
                        }
                        Spacer()
                        Text(capture.at, format: .dateTime.hour().minute().second())
                            .font(.caption2.monospacedDigit()).foregroundStyle(Theme.fog)
                    }
                    .accessibilityIdentifier("capture-row")
                }
            }
        }
        .padding(12)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        .fullScreenCover(isPresented: $showingCamera) {
            FieldCameraCaptureView(session: session,
                                   camera: camera,
                                   wasRunning: cameraWasRunning,
                                   allowsVideo: session.channels.contains(.video),
                                   initialKind: cameraKind,
                                   onCaptured: { url, kind, duration in
                                       await adopt(url, kind: kind, duration: duration)
                                   })
        }
        .alert("Couldn't save that capture",
               isPresented: Binding(get: { errorMessage != nil },
                                    set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) { errorMessage = nil }
        } message: { Text(errorMessage ?? "") }
    }

    private var audioButtonTitle: String {
        if camera.isRecordingClip { return "On the clip" }
        return session.recording == nil ? "Record" : "Stop audio"
    }

    @ViewBuilder
    private func captureButton(_ title: String, icon: String, kind: CaptureKind) -> some View {
        Button {
            cameraKind = kind
            cameraWasRunning = camera.isRunning
            showingCamera = true
        } label: {
            Label(title, systemImage: icon)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
        }
        .buttonStyle(.bordered)
        // Never disabled: a device with no camera says so on the capture screen, in a sentence.
        // A dead button explains nothing.
        .accessibilityIdentifier("capture-\(kind.rawValue)")
    }

    /// Moves the captured file into the session and records what it is.
    private func adopt(_ url: URL, kind: CaptureKind, duration: Double?) async {
        do {
            let adopted = try files.adopt(url, for: session.sessionId, kind: kind)

            // A clip with no length never reaches the replay's timeline, and the review screen then says nothing was
            // recorded — which is what Ben saw after filming ten seconds (2026-09-16). The picker's answer is used
            // when it has one; otherwise the length is read from the file now in the session's own directory.
            var length = duration
            if length == nil, kind == .video {
                let moved = files.fileURL(for: session.sessionId, relativePath: adopted.relativePath)
                let seconds = try? await AVURLAsset(url: moved).load(.duration).seconds
                length = (seconds?.isFinite == true && (seconds ?? 0) > 0) ? seconds : nil
            }

            await session.noteCapture(kind: kind, relativePath: adopted.relativePath,
                                      byteCount: adopted.byteCount, durationSeconds: length)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func icon(for kind: CaptureKind) -> String {
        switch kind {
        case .photo: "photo"
        case .video: "video"
        case .audio: "waveform"
        }
    }

    private func detail(for capture: ActiveFieldSession.CaptureRecord) -> String {
        var parts: [String] = []
        if let duration = capture.durationSeconds {
            parts.append(SessionClock.elapsed(from: .now, to: .now.addingTimeInterval(duration)))
        }
        parts.append(ByteCountFormatter.string(fromByteCount: capture.byteCount,
                                               countStyle: .file))
        if capture.latitude != nil { parts.append("located") }
        return parts.joined(separator: " · ")
    }
}
