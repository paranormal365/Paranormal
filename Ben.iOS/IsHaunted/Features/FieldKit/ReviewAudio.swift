import AVFoundation

/// The app's audio session, set for LISTENING before a finished session is played back.
///
/// Ben, 2026-09-27: "The playback of audio is super quiet." Nothing on the review screens ever
/// set the audio session, so playback inherited whatever the recorder left behind: the
/// record-and-play category in `.measurement` mode, which is tuned for a microphone, not for a
/// speaker. `.playback` is the category built for listening — full speaker volume, and it plays
/// with the ring/silent switch on, which is how a phone is usually carried into a building.
enum ReviewAudio {
    /// Switches to playback, unless a session is open on this phone.
    ///
    /// An open session — recording, or set up and waiting for Start — owns the microphone, and
    /// changing the category underneath it would stop its recording. Playback in that case stays
    /// as it was; the recording matters more than the volume of the review.
    @MainActor
    static func prepareForPlayback(sessionIsOpen: Bool) {
        guard !sessionIsOpen else { return }
        let session = AVAudioSession.sharedInstance()
        try? session.setCategory(.playback, mode: .default)
        try? session.setActive(true)
    }
}
