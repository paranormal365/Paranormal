import SwiftUI
import AVFoundation
import BenKit

/// What is under the handle, and what the window sounds like (item 210).
///
/// Ben: *"couldn't they preview what they have while trimming it or what they are going to
/// submit?"* This is the same replay the review screen uses, pointed at the trimmer: dragging
/// either handle parks the playhead there and shows the field and sound at that moment, and Play
/// runs from the in point and stops at the out point, with the recording following — so what is
/// heard IS what will be sent, and nothing else.
struct TrimPreview: View {
    let replay: SessionReplay
    let source: ReplaySource
    @Binding var range: SessionTrimRange
    let fileURL: (String) -> URL

    @State private var player = AVPlayer()
    @State private var loadedMediaId: UUID?

    var body: some View {
        VStack(spacing: 10) {
            HStack(spacing: 8) {
                readout("Field",
                        value: replay.frame.magneticDeviationMilligauss(from: source.baselines)
                            .map { String(format: "%+.0f mG", $0) }
                          ?? replay.frame.magneticMicrotesla.map { String(format: "%.0f mG", $0 * 10) }
                          ?? "—",
                        icon: "gauge.with.needle")
                readout("Sound",
                        value: replay.frame.soundDbfs.map { String(format: "%.0f dB", $0) } ?? "—",
                        icon: "waveform")
                readout("At",
                        value: SessionTrimPlan.clock(replay.playhead.timeIntervalSince(source.startedAt)),
                        icon: "clock")
            }

            if replay.timeline.readings.count > 1 {
                ReadingsChart(timeline: replay.timeline, playhead: replay.playhead) { moment in
                    // Scrubbing the chart is allowed anywhere; PLAYING stays inside the window.
                    replay.pause()
                    replay.seek(to: moment)
                }
                // Tall enough for its own axis titles, and clipped: at 90 pt the chart drew its
                // "mG from base" title over the readouts above it and its time axis under the
                // Play button below — the App Store capture on 2026-09-04 is what showed it.
                .frame(height: 170)
                .padding(.vertical, 6)
                .clipped()
            }

            HStack(spacing: 16) {
                Button {
                    if replay.isPlaying {
                        replay.pause()
                    } else {
                        // Play begins at the in point unless the playhead is already inside the
                        // window — a handle was just dragged to somewhere worth hearing from.
                        if !range.window.contains(replay.playhead) { replay.seek(to: range.inPoint) }
                        replay.play()
                    }
                } label: {
                    Label(replay.isPlaying ? "Pause" : "Play what will be sent",
                          systemImage: replay.isPlaying ? "pause.fill" : "play.fill")
                }
                .buttonStyle(.bordered)
                .tint(Theme.ecto)
                .disabled(!replay.isLoaded)
                .accessibilityIdentifier("trim-preview-play")

                if replay.frame.activeMedia == nil, replay.isPlaying {
                    Text("no recording here").font(.caption2).foregroundStyle(Theme.fog)
                }
            }
        }
        // The out point is the end of what will be sent, so it is the end of the preview too.
        .onChange(of: replay.playhead) { _, playhead in
            if replay.isPlaying, playhead >= range.outPoint {
                replay.pause()
                replay.seek(to: range.outPoint)
            }
        }
        .onChange(of: replay.frame) { _, frame in followMedia(frame) }
        .onDisappear { player.pause() }
    }

    private func readout(_ title: String, value: String, icon: String) -> some View {
        VStack(spacing: 2) {
            Label(title, systemImage: icon).font(.caption2).foregroundStyle(Theme.fog)
            Text(value).font(.callout.monospacedDigit()).foregroundStyle(Theme.bone)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 6)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: 8))
    }

    /// Keeps the recording on the playhead. The same rule as the review screen: the playhead is
    /// the clock, small drift is left alone, a real jump re-seeks.
    private func followMedia(_ frame: ReplayFrame) {
        guard let active = frame.activeMedia else {
            if loadedMediaId != nil {
                player.pause()
                player.replaceCurrentItem(with: nil)
                loadedMediaId = nil
            }
            return
        }
        if loadedMediaId != active.segment.id {
            player.replaceCurrentItem(with: AVPlayerItem(url: fileURL(active.segment.relativePath)))
            loadedMediaId = active.segment.id
        }
        let target = CMTime(seconds: active.offset, preferredTimescale: 600)
        if abs(player.currentTime().seconds - active.offset) > 0.35 || !replay.isPlaying {
            player.seek(to: target, toleranceBefore: .zero, toleranceAfter: .zero)
        }
        if replay.isPlaying {
            if player.rate == 0 { player.play() }
        } else if player.rate != 0 {
            player.pause()
        }
    }
}
