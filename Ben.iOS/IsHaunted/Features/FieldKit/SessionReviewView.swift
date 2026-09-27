import SwiftUI
import CoreLocation
import AVKit
import BenKit

/// A finished session, played back.
///
/// One playhead drives everything: the trace, the map, the compass and the media. That is the
/// whole point — a spike means little on its own, and a great deal alongside where somebody was
/// standing, which way they were facing, and what the microphone heard at that second.
struct SessionReviewView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router
    @Environment(\.horizontalSizeClass) private var sizeClass

    let sessionId: UUID

    @State private var replay = SessionReplay()
    /// The picture. A whole-session video carries no sound of its own, so it plays beside the
    /// session's recording rather than instead of it — two players, one playhead.
    @State private var videoPlayer = AVPlayer()
    @State private var loadedVideoId: UUID?
    /// The sound.
    @State private var audioPlayer = AVPlayer()
    @State private var loadedAudioId: UUID?
    /// The photograph grown to full size from the strip, and whether playback was running when
    /// it was tapped — so tapping it again carries on exactly as before.
    @State private var expandedPhoto: CaptureMark?
    @State private var resumeAfterPhoto = false
    @Namespace private var photoSpace
    @State private var source: ReplaySource?
    /// The walked path, converted once after the replay loads — not on every tick.
    @State private var track: [CLLocationCoordinate2D] = []
    @State private var exporting = false
    @State private var uploading = false
    @State private var choosingPhoto = false
    /// What the place's archive says about this night. Nil until asked, or when it has nothing.
    @State private var insights: SessionInsights?

    private var store: FieldSessionStore { dependencies.fieldKit }
    private var summary: FieldSessionSummary? { store.summary(for: sessionId) }

    /// Recorded on this phone and ended in the last hour — the review somebody lands on straight
    /// from Stop, which is where the next session is most likely wanted.
    private var justEnded: Bool {
        guard let summary, !summary.isImported, summary.outcome == .ended,
              let ended = summary.endedAt else { return false }
        return Date().timeIntervalSince(ended) < 3_600
    }

    /// Ben, 2026-09-27: "It is not super obvious what to do after you end a session and want to
    /// start another one." Said at the top of the review, with the one button that does it.
    private var nextSessionCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            Label("Session saved", systemImage: "checkmark.circle.fill")
                .font(.subheadline.bold())
                .foregroundStyle(Theme.success)
            Text("It's on this phone. Play it back here, or send it from the share menu when you're ready.")
                .font(.caption).foregroundStyle(Theme.fog)
            Button {
                replay.pause()
                router.startAnotherSession(like: sessionId)
            } label: {
                Label("Start another session", systemImage: "record.circle")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(Theme.ecto)
            .accessibilityIdentifier("start-another-session")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
    }

    var body: some View {
        Group {
            if let problem = replay.problem {
                ContentUnavailableView {
                    Label("This session can't be replayed", systemImage: "waveform.slash")
                } description: {
                    Text(problem)
                }
            } else if !replay.isLoaded {
                ProgressView("Reading the session")
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if sizeClass == .regular {
                HStack(alignment: .top, spacing: 18) {
                    ScrollView { leftColumn }
                    ScrollView { rightColumn }
                }
                .padding(.horizontal, 16)
            } else {
                ScrollView {
                    VStack(spacing: 16) {
                        leftColumn
                        rightColumn
                    }
                    .padding(.horizontal, 16)
                    // Clears the floating tab bar. At 24 the last card — usually the map and
                    // what it says about position — ended up underneath it and unreadable.
                    .padding(.bottom, 88)
                }
            }
        }
        .background(Theme.ink)
        .navigationTitle(summary?.title ?? "Session")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    // Somebody else's published night is theirs to send, not this reader's.
                    if summary?.isPublicArchiveCopy != true {
                        Button {
                            uploading = true
                        } label: {
                            Label("Send to the server", systemImage: "icloud.and.arrow.up")
                        }
                    }
                    Button {
                        exporting = true
                    } label: {
                        Label("Export a bundle", systemImage: "square.and.arrow.up")
                    }
                    Button {
                        choosingPhoto = true
                    } label: {
                        Label("Property photos", systemImage: "photo.on.rectangle")
                    }
                } label: {
                    Image(systemName: "square.and.arrow.up")
                }
                .accessibilityLabel("Send or export this session")
                .accessibilityIdentifier("open-share-menu")
            }
        }
        .sheet(isPresented: $exporting) {
            ExportSessionView(sessionId: sessionId).environment(dependencies)
        }
        .sheet(isPresented: $uploading) {
            UploadSessionView(sessionId: sessionId).environment(dependencies)
        }
        .sheet(isPresented: $choosingPhoto) {
            PropertyPhotosView(sessionId: sessionId).environment(dependencies)
        }
        .onChange(of: replay.frame) { _, frame in followMedia(frame) }
        .overlay {
            if let photo = expandedPhoto { expandedPhotoView(photo) }
        }
        .onDisappear {
            replay.pause()
            videoPlayer.pause()
            audioPlayer.pause()
        }
        .task { await load() }
    }

    // MARK: - Columns

    @ViewBuilder
    private var leftColumn: some View {
        VStack(spacing: 14) {
            if justEnded { nextSessionCard }
            mediaPane
            photoStrip
            ReplayTransport(replay: replay)
            instrumentsAtPlayhead
            archiveVerdict
        }
    }

    /// What the place's archive says about this night.
    ///
    /// **The one question somebody recording alone cannot answer for themselves:** was that
    /// unusual, or does this building do it to everybody? Shown under the instruments because it
    /// is read AFTER the numbers, as the thing that gives them meaning.
    ///
    /// Absent entirely when nobody else has recorded there — a panel saying "no comparison
    /// available" is a worse answer than no panel, and the first person at a place must never be
    /// told their night was unremarkable against no evidence at all.
    @ViewBuilder
    private var archiveVerdict: some View {
        if let insights, insights.othersWhoRecordedHere > 0 {
            VStack(alignment: .leading, spacing: 8) {
                Text("What \(insights.placeName)'s archive says")
                    .font(.caption)
                    .foregroundStyle(Theme.fog)

                Text(headline(insights))
                    .font(.subheadline)

                if insights.detailed {
                    if let mine = insights.yourMarkersPerHour,
                       let median = insights.placeMedianMarkersPerHour {
                        HStack(spacing: 16) {
                            figure(String(format: "%.1f", mine), "your flags/hr")
                            figure(String(format: "%.1f", median), "typical here")
                        }
                        Text(insights.standsOut == true
                             ? "Busier than this place usually is."
                             // The deflating answer is the archive's most valuable one, so it is
                             // said as plainly as the exciting one.
                             : "About what this place usually gives people.")
                            .font(.caption)
                            .foregroundStyle(insights.standsOut == true ? Theme.warning : Theme.fog)
                    } else {
                        Text("Not enough comparable sessions here yet to say whether this one was unusual.")
                            .font(.caption)
                            .foregroundStyle(Theme.fog)
                    }
                } else {
                    // Names exactly what it withholds. Hiding the SHAPE of what you would get
                    // teaches people to assume it is nothing.
                    //
                    // It deliberately does NOT say "a paid plan". Nothing in this app sells
                    // anything, and App Review reads wording that points at a purchase made
                    // outside the app under Guideline 3.1.1 — so the sentence describes what this
                    // account does, and says nothing about how that could change.
                    Text("This account doesn't compare your session with everyone else's here: "
                       + "your flagged moments per hour against what this place typically gives "
                       + "people, and whether this night stood out.")
                        .font(.caption)
                        .foregroundStyle(Theme.fog)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(14)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    private func figure(_ value: String, _ label: String) -> some View {
        VStack(spacing: 2) {
            Text(value).font(.title3).bold()
            Text(label).font(.caption2).foregroundStyle(Theme.fog)
        }
    }

    private func headline(_ i: SessionInsights) -> String {
        let people = i.othersWhoRecordedHere == 1 ? "1 other person has" : "\(i.othersWhoRecordedHere) other people have"
        let flagged = i.othersWhoFlaggedSomething > 0
            ? "\(i.othersWhoFlaggedSomething) of them flagged something."
            : "none of them flagged anything."
        let yours = i.yourSessionsHere > 1 ? " This is your \(ordinal(i.yourSessionsHere)) session here." : ""
        return "\(people) recorded here, and \(flagged)\(yours)"
    }

    /// "3rd", "12th". The teens are why this is a function — every naive version ships "11st".
    private func ordinal(_ n: Int) -> String {
        if (11...13).contains(n % 100) { return "\(n)th" }
        switch n % 10 {
        case 1:  return "\(n)st"
        case 2:  return "\(n)nd"
        case 3:  return "\(n)rd"
        default: return "\(n)th"
        }
    }

    @ViewBuilder
    private var rightColumn: some View {
        VStack(spacing: 14) {
            ReadingsChart(timeline: replay.timeline, trace: replay.fieldTrace,
                          playhead: replay.playhead) { moment in
                replay.pause()
                replay.seek(to: moment)
            }
            .padding(12)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))

            MovementMap(timeline: replay.timeline, frame: replay.frame,
                        stills: source?.stills ?? [], track: track)

            markerList
            sessionFacts
        }
    }

    /// What was seen and heard at the playhead: the video, else the sound — or a plain statement
    /// that nothing was recorded at this moment. Photographs live in the strip under this.
    @ViewBuilder
    private var mediaPane: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12).fill(Theme.mist)

            if replay.frame.activeVideo != nil {
                VideoPlayer(player: videoPlayer)
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                    .accessibilityIdentifier("replay-video")
            } else if let sound = replay.frame.activeAudio {
                VStack(spacing: 6) {
                    Image(systemName: "waveform")
                        .font(.largeTitle).foregroundStyle(Theme.ecto)
                    Text(sound.segment.relativePath
                            .replacingOccurrences(of: "media/", with: ""))
                        .font(.caption).foregroundStyle(Theme.fog)
                }
            } else {
                // Audio and video are clips, so most of a night has neither. Saying so beats
                // a black rectangle somebody reads as a broken player.
                VStack(spacing: 6) {
                    Image(systemName: "moon.stars")
                        .font(.title2).foregroundStyle(Theme.fog)
                    // Ben, 2026-09-16: a session with ten seconds of video said "Nothing was recorded", because this
                    // asked about the timeline's clips and a video whose length could not be read never reaches it.
                    // The sentence now says only what it knows: no clip runs through the playhead.
                    Text(replay.timeline.media.isEmpty && photos.isEmpty
                         ? "No sound, video or photos were recorded in this session."
                         : "No recording at this moment.")
                        .font(.caption).foregroundStyle(Theme.fog)
                }
            }
        }
        .frame(height: 220)
        // Motion the camera saw while the phone was still: the video is outlined and the sign is
        // up for a few seconds from the moment, exactly as it was on the live viewfinder.
        .overlay {
            if replay.frame.motionDetected != nil {
                RoundedRectangle(cornerRadius: 12)
                    .stroke(Theme.warning, lineWidth: 3)
                    .shadow(color: Theme.warning.opacity(0.8), radius: 10)
            }
        }
        .overlay(alignment: .top) {
            if replay.frame.motionDetected != nil {
                MotionDetectedSign().padding(.top, 10)
            }
        }
        .animation(.easeInOut(duration: 0.25), value: replay.frame.motionDetected?.id)
        .accessibilityIdentifier("replay-media")
    }

    /// Every photograph taken during the session, in order. Tapping one moves the playhead to the
    /// moment it was taken, so the readings and the sound around it are right there.
    ///
    /// Ben, 2026-09-27: "the photo never shows either when you take it during a session." They were
    /// only map pins, and only when the phone had a position fix — so a photo taken indoors was
    /// nowhere on this screen at all.
    @ViewBuilder
    private var photoStrip: some View {
        if !photos.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                Text(photos.count == 1 ? "1 photo" : "\(photos.count) photos")
                    .font(.caption).foregroundStyle(Theme.fog)
                ScrollViewReader { proxy in
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 8) {
                            ForEach(photos) { photo in
                                photoThumbnail(photo).id(photo.id)
                            }
                        }
                        .padding(4)   // room for the glow
                    }
                    // Keeps the photo the playhead last passed in view, so the glow is seen.
                    .onChange(of: ReplayPhotos.lastPassed(photos, at: replay.playhead)?.id) { _, id in
                        guard let id, replay.isPlaying else { return }
                        withAnimation { proxy.scrollTo(id, anchor: .center) }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(12)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    /// One thumbnail. As playback passes the moment it was taken its border glows and it grows a
    /// little, for three seconds (Ben, 2026-09-27), so a photo is noticed at the second it belongs
    /// to without taking the player over. Tapping it pauses playback and grows it to full size.
    private func photoThumbnail(_ photo: CaptureMark) -> some View {
        let glowing = ReplayPhotos.isGlowing(photo, at: replay.playhead)
        let url = store.files.fileURL(for: sessionId, relativePath: photo.relativePath)
        return Button { expand(photo) } label: {
            VStack(spacing: 3) {
                ZStack {
                    if expandedPhoto?.id == photo.id {
                        // Its place in the strip is kept while it is out, so it has somewhere to
                        // shrink back to.
                        Color.clear
                    } else {
                        SessionPhotoTile(url: url, maxPixels: 240, contentMode: .fill)
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                            .matchedGeometryEffect(id: photo.id, in: photoSpace)
                    }
                }
                .frame(width: 72, height: 72)
                .overlay {
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(glowing ? Theme.ecto : Theme.fog.opacity(0.3),
                                lineWidth: glowing ? 3 : 1)
                }
                .shadow(color: glowing ? Theme.ecto.opacity(0.9) : .clear, radius: glowing ? 8 : 0)
                .scaleEffect(glowing ? 1.15 : 1)
                .animation(.spring(duration: 0.35), value: glowing)
                Text(SessionClock.elapsed(from: replay.timeline.startedAt, to: photo.at))
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(glowing ? Theme.ecto : Theme.fog)
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Photo at \(photoCaption(photo))")
        .accessibilityValue(glowing ? "taken now" : "")
        .accessibilityHint("Pauses playback and shows it full size")
        .accessibilityIdentifier(glowing ? "replay-photo-thumbnail-glowing" : "replay-photo-thumbnail")
    }

    /// The tapped photograph at full size, over the whole screen. Tapping it again puts it back in
    /// the strip and carries on playing if it was playing.
    private func expandedPhotoView(_ photo: CaptureMark) -> some View {
        ZStack {
            Color.black.opacity(0.92).ignoresSafeArea()
            VStack(spacing: 12) {
                SessionPhotoTile(url: store.files.fileURL(for: sessionId,
                                                          relativePath: photo.relativePath),
                                 maxPixels: 2800, contentMode: .fit)
                    .background(Color.clear)
                    .matchedGeometryEffect(id: photo.id, in: photoSpace)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                Text(photoCaption(photo))
                    .font(.caption)
                    .foregroundStyle(.white)
                Text(resumeAfterPhoto ? "Tap to keep playing" : "Tap to close")
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.7))
            }
            .padding(16)
        }
        .contentShape(Rectangle())
        .onTapGesture { collapsePhoto() }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
        .accessibilityLabel("Photo, \(photoCaption(photo))")
        .accessibilityHint(resumeAfterPhoto ? "Closes it and carries on playing" : "Closes it")
        .accessibilityIdentifier("replay-photo-expanded")
    }

    private func expand(_ photo: CaptureMark) {
        resumeAfterPhoto = replay.isPlaying
        replay.pause()
        withAnimation(.spring(duration: 0.4)) { expandedPhoto = photo }
    }

    private func collapsePhoto() {
        withAnimation(.spring(duration: 0.4)) { expandedPhoto = nil }
        if resumeAfterPhoto { replay.play() }
        resumeAfterPhoto = false
    }

    private var photos: [CaptureMark] { ReplayPhotos.taken(source?.stills ?? []) }

    /// "00:12:40 into the session · Back bedroom" — when, and where if they said.
    private func photoCaption(_ photo: CaptureMark) -> String {
        let when = SessionClock.elapsed(from: replay.timeline.startedAt, to: photo.at) + " into the session"
        guard let room = photo.room, !room.isEmpty else { return when }
        return "\(when) · \(room)"
    }

    private var instrumentsAtPlayhead: some View {
        HStack(spacing: 10) {
            // iOS-5: "—" here means one of two very different things — no reading at this
            // instant, or no base level for the whole session — and the second one was silent.
            readout("Field",
                    value: replay.timeline.baselines.magneticMicrotesla == nil
                        ? "no base"
                        : replay.frame.magneticDeviationMilligauss(from: replay.timeline.baselines)
                            .map { String(format: "%+.0f mG", $0) } ?? "—",
                    icon: "gauge.with.needle")
            readout("Sound",
                    value: replay.frame.soundDbfs.map { String(format: "%.0f dB", $0) } ?? "—",
                    icon: "waveform")
            // Titled by what the number IS at this moment: the compass says which way they were
            // looking, and with no compass — which is most of a night indoors — the fix says
            // which way they were walking. Calling both "Heading" would let one be read as the
            // other, and they are different facts.
            readout(replay.frame.facing?.what.capitalized ?? "Heading",
                    value: replay.frame.facing
                        .map { "\(PositionReadout.compass($0.degrees)) \(Int($0.degrees))°" } ?? "—",
                    icon: replay.frame.facing.map {
                        $0.what == "walking" ? "figure.walk" : "safari"
                    } ?? "safari")
        }
    }

    private func readout(_ title: String, value: String, icon: String) -> some View {
        VStack(spacing: 3) {
            Label(title, systemImage: icon)
                .font(.caption2).foregroundStyle(Theme.fog)
            Text(value)
                .font(.callout.monospacedDigit()).foregroundStyle(Theme.bone)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
    }

    @ViewBuilder
    private var markerList: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Marked").font(.caption).foregroundStyle(Theme.fog)

            if replay.timeline.markers.isEmpty {
                Text("Nothing was marked during this session.")
                    .font(.caption).foregroundStyle(Theme.fog)
            } else {
                ForEach(replay.timeline.markers) { marker in
                    Button {
                        replay.pause()
                        replay.seek(to: marker)
                    } label: {
                        HStack(spacing: 10) {
                            Image(systemName: marker.kind.isAutomatic ? "bolt.fill" : "flag.fill")
                                .font(.caption)
                                .foregroundStyle(marker.kind.isAutomatic ? Theme.warning : Theme.haunt)
                            VStack(alignment: .leading, spacing: 1) {
                                HStack(spacing: 4) {
                                    Text(marker.kind.title).font(.caption).foregroundStyle(Theme.bone)
                                    if let room = marker.room {
                                        Text("· \(room)").font(.caption2).foregroundStyle(Theme.ecto)
                                    }
                                }
                                if let note = marker.note {
                                    Text(note).font(.caption2).foregroundStyle(Theme.fog)
                                        .lineLimit(2)
                                }
                            }
                            Spacer()
                            Text(SessionClock.elapsed(from: replay.timeline.startedAt, to: marker.at))
                                .font(.caption2.monospacedDigit()).foregroundStyle(Theme.fog)
                        }
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("replay-marker-row")
                }
            }
        }
        .padding(12)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
    }

    @ViewBuilder
    private var sessionFacts: some View {
        if let summary {
            VStack(alignment: .leading, spacing: 6) {
                LabeledContent("Started",
                               value: summary.startedAt.formatted(date: .abbreviated,
                                                                  time: .shortened))
                if let duration = summary.duration {
                    LabeledContent("Ran for", value: SessionReviewView.durationText(duration))
                } else if summary.outcome == .interrupted {
                    // Said plainly rather than guessed: the app went away mid-session.
                    LabeledContent("Ended", value: "Interrupted — end time unknown")
                }
                LabeledContent("Readings", value: "\(summary.readingCount)")
                if let investigation = summary.investigationTitle, !investigation.isEmpty {
                    LabeledContent("Investigation", value: investigation)
                }
                // A session that arrived as a .ben plays exactly as one recorded here, and says
                // so here — the seal's whole point is that a night is not quietly re-attributed
                // to whoever happens to be holding the phone.
                if summary.isPublicArchiveCopy {
                    // Said, because its map is not a walked path: every position is the place's
                    // public point, deliberately.
                    LabeledContent("Source", value: "The public archive — positions are the place's public point")
                } else if summary.wasRecordedElsewhere(thisDeviceId: DeviceModel.vendorIdentifier()) {
                    LabeledContent("Source", value: "Shared with you — recorded on another device")
                } else if summary.isImported {
                    LabeledContent("Source", value: summary.serverSessionId != nil
                                   ? "Downloaded from the server"
                                   : "Opened from a file recorded on this device")
                }
            }
            .font(.callout)
            .padding(12)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    // MARK: - Loading and media

    private func load() async {
        store.load()
        guard let source = store.replayData(for: sessionId) else { return }
        self.source = source
        await replay.load(readingLog: source.log, markers: source.markers,
                          media: source.media, baselines: source.baselines,
                          startedAt: source.startedAt, endedAt: source.endedAt)
        track = replay.walkedPath.compactMap(\.coordinate)

        // Asked only once the session exists on the server: a recording still sitting on the
        // phone has nothing to compare against, and there is no id to ask about. Failing quietly
        // is right — the comparison is an addition to this screen, not a precondition for it, and
        // a session that would not replay because the archive was slow is a worse screen.
        if let serverId = summary?.serverSessionId {
            insights = await dependencies.archiveActions.insights(serverSessionId: serverId)
        }
    }

    /// Keeps both players on the playhead.
    ///
    /// The playhead is the clock, not a player — otherwise scrubbing the chart and scrubbing
    /// the video would fight each other. Small drift is left alone; a real jump re-seeks.
    private func followMedia(_ frame: ReplayFrame) {
        guard let source else { return }
        follow(videoPlayer, clip: frame.activeVideo, loaded: &loadedVideoId, source: source)
        follow(audioPlayer, clip: frame.activeAudio, loaded: &loadedAudioId, source: source)
        // An older clip filmed with its own sound plays it; a video with the session's recording
        // running underneath it stays quiet, so nothing is heard twice.
        videoPlayer.isMuted = frame.activeAudio != nil
    }

    private func follow(_ player: AVPlayer,
                        clip: (segment: MediaSegment, offset: TimeInterval)?,
                        loaded: inout UUID?, source: ReplaySource) {
        guard let clip else {
            if loaded != nil {
                player.pause()
                player.replaceCurrentItem(with: nil)
                loaded = nil
            }
            return
        }

        if loaded != clip.segment.id {
            let url = store.files.fileURL(for: source.sessionId,
                                          relativePath: clip.segment.relativePath)
            player.replaceCurrentItem(with: AVPlayerItem(url: url))
            loaded = clip.segment.id
        }

        let target = CMTime(seconds: clip.offset, preferredTimescale: 600)
        let drift = abs(player.currentTime().seconds - clip.offset)
        if drift > 0.35 || !replay.isPlaying {
            player.seek(to: target, toleranceBefore: .zero, toleranceAfter: .zero)
        }

        // Media follows the replay's own speed, so 8× review really is eight times through.
        if replay.isPlaying {
            if player.rate == 0 {
                ReviewAudio.prepareForPlayback(sessionIsOpen: store.active != nil)
                player.play()
            }
            player.rate = Float(min(replay.rate, 2))   // beyond 2x audio is not worth hearing
        } else if player.rate != 0 {
            player.pause()
        }
    }

    static func durationText(_ seconds: TimeInterval) -> String {
        let total = Int(seconds.rounded())
        let hours = total / 3600, minutes = (total % 3600) / 60, secs = total % 60
        if hours > 0 { return "\(hours)h \(minutes)m" }
        if minutes > 0 { return "\(minutes)m \(secs)s" }
        return "\(secs)s"
    }
}
