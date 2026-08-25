import SwiftUI
import AVKit
import BenKit

/// A finished session, played back.
///
/// One playhead drives everything: the trace, the map, the compass and the media. That is the
/// whole point — a spike means little on its own, and a great deal alongside where somebody was
/// standing, which way they were facing, and what the microphone heard at that second.
struct SessionReviewView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.horizontalSizeClass) private var sizeClass

    let sessionId: UUID

    @State private var replay = SessionReplay()
    @State private var player = AVPlayer()
    @State private var loadedMediaId: UUID?
    @State private var source: ReplaySource?
    @State private var exporting = false

    private var store: FieldSessionStore { dependencies.fieldKit }
    private var summary: FieldSessionSummary? { store.summary(for: sessionId) }

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
                    .padding(.bottom, 24)
                }
            }
        }
        .background(Theme.ink)
        .navigationTitle(summary?.title ?? "Session")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { exporting = true } label: {
                    Image(systemName: "square.and.arrow.up")
                }
                .accessibilityLabel("Export this session")
                .accessibilityIdentifier("open-export")
            }
        }
        .sheet(isPresented: $exporting) {
            ExportSessionView(sessionId: sessionId).environment(dependencies)
        }
        .onChange(of: replay.frame) { _, frame in followMedia(frame) }
        .onDisappear {
            replay.pause()
            player.pause()
        }
        .task { await load() }
    }

    // MARK: - Columns

    @ViewBuilder
    private var leftColumn: some View {
        VStack(spacing: 14) {
            mediaPane
            ReplayTransport(replay: replay)
            instrumentsAtPlayhead
        }
    }

    @ViewBuilder
    private var rightColumn: some View {
        VStack(spacing: 14) {
            ReadingsChart(timeline: replay.timeline, playhead: replay.playhead) { moment in
                replay.pause()
                replay.seek(to: moment)
            }
            .padding(12)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))

            MovementMap(timeline: replay.timeline, frame: replay.frame,
                        stills: source?.stills ?? [])

            markerList
            sessionFacts
        }
    }

    /// The video, or a plain statement that nothing was recorded at this moment.
    @ViewBuilder
    private var mediaPane: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12).fill(Theme.mist)

            if let active = replay.frame.activeMedia {
                if active.segment.kind == .video {
                    VideoPlayer(player: player)
                        .clipShape(RoundedRectangle(cornerRadius: 12))
                } else {
                    VStack(spacing: 6) {
                        Image(systemName: "waveform")
                            .font(.largeTitle).foregroundStyle(Theme.ecto)
                        Text(active.segment.relativePath
                                .replacingOccurrences(of: "media/", with: ""))
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                }
            } else {
                // Audio and video are clips, so most of a night has neither. Saying so beats
                // a black rectangle somebody reads as a broken player.
                VStack(spacing: 6) {
                    Image(systemName: "moon.stars")
                        .font(.title2).foregroundStyle(Theme.fog)
                    Text(replay.timeline.media.isEmpty
                         ? "Nothing was recorded in this session."
                         : "No recording at this moment.")
                        .font(.caption).foregroundStyle(Theme.fog)
                }
            }
        }
        .frame(height: 200)
        .accessibilityIdentifier("replay-media")
    }

    private var instrumentsAtPlayhead: some View {
        HStack(spacing: 10) {
            readout("Field",
                    value: replay.frame.magneticDeviationMilligauss(from: replay.timeline.baselines)
                        .map { String(format: "%+.0f mG", $0) } ?? "—",
                    icon: "gauge.with.needle")
            readout("Sound",
                    value: replay.frame.soundDbfs.map { String(format: "%.0f dB", $0) } ?? "—",
                    icon: "waveform")
            readout("Heading",
                    value: replay.frame.headingDegrees
                        .map { "\(PositionReadout.compass($0)) \(Int($0))°" } ?? "—",
                    icon: "safari")
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
                                Text(marker.kind.title).font(.caption).foregroundStyle(Theme.bone)
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
    }

    /// Keeps the player on the playhead.
    ///
    /// The playhead is the clock, not the player — otherwise scrubbing the chart and scrubbing
    /// the video would fight each other. Small drift is left alone; a real jump re-seeks.
    private func followMedia(_ frame: ReplayFrame) {
        guard let active = frame.activeMedia, let source else {
            if loadedMediaId != nil {
                player.pause()
                player.replaceCurrentItem(with: nil)
                loadedMediaId = nil
            }
            return
        }

        if loadedMediaId != active.segment.id {
            let url = store.files.fileURL(for: source.sessionId,
                                          relativePath: active.segment.relativePath)
            player.replaceCurrentItem(with: AVPlayerItem(url: url))
            loadedMediaId = active.segment.id
        }

        let target = CMTime(seconds: active.offset, preferredTimescale: 600)
        let drift = abs(player.currentTime().seconds - active.offset)
        if drift > 0.35 || !replay.isPlaying {
            player.seek(to: target, toleranceBefore: .zero, toleranceAfter: .zero)
        }

        // Media follows the replay's own speed, so 8× review really is eight times through.
        if replay.isPlaying {
            if player.rate == 0 { player.play() }
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
