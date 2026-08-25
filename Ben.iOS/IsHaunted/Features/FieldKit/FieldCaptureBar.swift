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

    @State private var showingCamera = false
    @State private var cameraKind: CaptureKind = .photo
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
                    Label(session.recording == nil ? "Record" : "Stop audio",
                          systemImage: session.recording == nil ? "mic" : "mic.slash")
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 6)
                }
                .buttonStyle(.bordered)
                .tint(session.recording == nil ? Theme.ecto : Theme.danger)
                .accessibilityIdentifier("toggle-audio-recording")
            }

            if let problem = session.recordingProblem {
                Label(problem, systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Theme.danger)
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
            FieldCameraPicker(kind: cameraKind) { url, duration in
                await adopt(url, kind: cameraKind, duration: duration)
            }
        }
        .alert("Couldn't save that capture",
               isPresented: Binding(get: { errorMessage != nil },
                                    set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) { errorMessage = nil }
        } message: { Text(errorMessage ?? "") }
    }

    @ViewBuilder
    private func captureButton(_ title: String, icon: String, kind: CaptureKind) -> some View {
        Button {
            cameraKind = kind
            showingCamera = true
        } label: {
            Label(title, systemImage: icon)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
        }
        .buttonStyle(.bordered)
        .disabled(!UIImagePickerController.isSourceTypeAvailable(.camera))
        .accessibilityIdentifier("capture-\(kind.rawValue)")
    }

    /// Moves the captured file into the session and records what it is.
    private func adopt(_ url: URL, kind: CaptureKind, duration: Double?) async {
        do {
            let adopted = try files.adopt(url, for: session.sessionId, kind: kind)
            await session.noteCapture(kind: kind, relativePath: adopted.relativePath,
                                      byteCount: adopted.byteCount, durationSeconds: duration)
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

/// The camera, handing back a file rather than an image in memory.
///
/// Same shape as the feed's picker — a captured video arrives as a URL and is MOVED, never read
/// into memory, because a 200 MB clip must not sit on the heap of a phone that is also logging
/// a magnetometer.
struct FieldCameraPicker: UIViewControllerRepresentable {
    @Environment(\.dismiss) private var dismiss
    let kind: CaptureKind
    let onCaptured: (URL, Double?) async -> Void

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.sourceType = .camera
        picker.mediaTypes = kind == .video ? ["public.movie"] : ["public.image"]
        if kind == .video { picker.videoQuality = .typeHigh }
        picker.delegate = context.coordinator
        return picker
    }

    func updateUIViewController(_ controller: UIImagePickerController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, UIImagePickerControllerDelegate,
                             UINavigationControllerDelegate {
        private let parent: FieldCameraPicker
        init(_ parent: FieldCameraPicker) { self.parent = parent }

        func imagePickerController(
            _ picker: UIImagePickerController,
            didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]
        ) {
            if let movie = info[.mediaURL] as? URL {
                let duration = AVURLAsset(url: movie).duration.seconds
                Task { await parent.onCaptured(movie, duration.isFinite ? duration : nil) }
            } else if let image = info[.originalImage] as? UIImage,
                      let data = image.jpegData(compressionQuality: 0.85) {
                let scratch = FileManager.default.temporaryDirectory
                    .appendingPathComponent("field-\(UUID().uuidString).jpg")
                try? data.write(to: scratch)
                Task { await parent.onCaptured(scratch, nil) }
            }
            parent.dismiss()
        }

        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            parent.dismiss()
        }
    }
}
