import Foundation
import AVFoundation
import BenKit

/// Whole-session video, and the camera's eye for Watch for Motion.
///
/// Ben, 2026-09-27: "Even when recording video, it doesn't play back when you end the session."
/// Nothing was being recorded. Since build 7 the Video channel ran the viewfinder and nothing else:
/// the in-session clip button had frozen the app (the microphone was handed from the session's
/// recorder to the clip and back, on the main actor, while both were busy with it), and it was
/// taken away rather than fixed. Ben chose to record the whole session instead.
///
/// **Picture only.** The clip carries no sound of its own; the session's audio recording keeps
/// running underneath it and the replay plays both. Nothing changes hands, so the handover that
/// froze build 6 never happens.
///
/// **While the live screen is open.** The camera belongs to that screen, and iOS takes it from any
/// app that is not on screen. So a clip closes when the session stops, when video is switched off,
/// when the app is put away and when the screen is left — and a new one starts when it can again.
/// Each is noted with the moment it STARTED, so the replay lays them end to end on its one clock.
@MainActor
@Observable
final class SessionVideoRecorder {
    private(set) var problem: String?

    /// True while a clip this recorder started is running.
    private(set) var isRecording = false
    /// Serialises start and finish: a scene change and a Stop arriving together must not both try
    /// to close the same clip.
    private var busy = false
    private var motionFeed: Task<Void, Never>?
    private var watchedMotionMarks: Date?
    private var takingMotionPhoto = false

    /// Brings the camera's recording and its motion feed into line with the session.
    func sync(session: ActiveFieldSession, camera: FieldCameraSession,
              files: SessionFileStore) async {
        feedMotion(session: session, camera: camera)

        let wanted = session.isRecording && session.channels.contains(.video) && camera.isRunning
        guard !busy else { return }
        if wanted, !camera.isRecordingClip {
            busy = true
            defer { busy = false }
            do {
                try camera.startClip(withSound: false)
                isRecording = true
                problem = nil
            } catch {
                problem = error.localizedDescription
            }
        } else if !wanted, isRecording {
            await finish(session: session, camera: camera, files: files)
        }
    }

    /// Closes the running clip, if this recorder started one, and files it in the session.
    func finish(session: ActiveFieldSession, camera: FieldCameraSession,
                files: SessionFileStore) async {
        guard isRecording, !busy else { return }
        busy = true
        defer { busy = false }
        isRecording = false

        let startedAt = camera.clipStartedAt
        do {
            let url = try await camera.finishClip()
            let adopted = try files.adopt(url, for: session.sessionId, kind: .video)
            let seconds = try? await AVURLAsset(url: adopted.url).load(.duration).seconds
            let length = (seconds?.isFinite == true && (seconds ?? 0) > 0) ? seconds : nil
            await session.noteCapture(kind: .video, relativePath: adopted.relativePath,
                                      byteCount: adopted.byteCount, durationSeconds: length,
                                      startedAt: startedAt)
        } catch {
            // The clip is lost, and that is said: the session carries on, and the next clip
            // starts as soon as the camera can.
            problem = "The last stretch of video couldn't be saved: \(error.localizedDescription)"
        }
    }

    /// Hands the camera's frames to the session while the camera runs, and stops when it does not.
    private func feedMotion(session: ActiveFieldSession, camera: FieldCameraSession) {
        let wanted = session.channels.contains(.video) && camera.isRunning
        if wanted, motionFeed == nil {
            let frames = camera.sceneMotion()
            motionFeed = Task { [weak session] in
                for await sample in frames {
                    guard let session else { return }
                    await session.noteSceneMotion(sample)
                }
            }
        } else if !wanted, let feed = motionFeed {
            feed.cancel()
            motionFeed = nil
        }
    }

    /// Motion was detected with the phone still: a photograph of it, then and there.
    ///
    /// Ben, 2026-09-27: "during the recording if the camera is not moving and motion is detected,
    /// you capture a photo." The video has the stretch; the photo is the moment, and it lands in
    /// the replay's strip where it glows as playback passes it.
    func motionDetected(at moment: Date, session: ActiveFieldSession,
                        camera: FieldCameraSession, files: SessionFileStore) async {
        guard session.watchForMotion, camera.isRunning, !takingMotionPhoto,
              watchedMotionMarks != moment else { return }
        watchedMotionMarks = moment
        takingMotionPhoto = true
        defer { takingMotionPhoto = false }
        do {
            let url = try await camera.capturePhoto()
            let adopted = try files.adopt(url, for: session.sessionId, kind: .photo)
            await session.noteCapture(kind: .photo, relativePath: adopted.relativePath,
                                      byteCount: adopted.byteCount)
        } catch {
            // A missed photo is not worth a warning: the mark is in the log and the video has it.
        }
    }

    func stopFeeding() {
        motionFeed?.cancel()
        motionFeed = nil
    }
}
