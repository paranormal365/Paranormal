import SwiftUI
import BenKit

/// Choosing what leaves the phone.
///
/// Everything recorded stays on the device; this builds a copy to hand over. Files are picked
/// rather than all sent, because a night of video is gigabytes and most of it is nothing — and
/// because what an investigator shares is their decision, not the app's.
struct ExportSessionView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    let sessionId: UUID

    @State private var captures: [CaptureMark] = []
    @State private var chosen: Set<String> = []
    @State private var result: DeviceDataExporter.Result?
    @State private var busy = false
    @State private var errorMessage: String?

    private var store: FieldSessionStore { dependencies.fieldKit }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("The readings, the marks and where you were are always included. Choose which recordings go with them.")
                        .font(.callout).foregroundStyle(Theme.fog)
                }

                if captures.isEmpty {
                    Section {
                        Text("Nothing was captured in this session — the bundle will be the readings alone.")
                            .font(.callout).foregroundStyle(Theme.fog)
                    }
                } else {
                    Section {
                        ForEach(captures) { capture in
                            Toggle(isOn: Binding(
                                get: { chosen.contains(capture.relativePath) },
                                set: { isOn in
                                    if isOn { chosen.insert(capture.relativePath) }
                                    else { chosen.remove(capture.relativePath) }
                                })
                            ) {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(capture.relativePath
                                            .replacingOccurrences(of: "media/", with: ""))
                                    Text(capture.at.formatted(date: .omitted, time: .standard))
                                        .font(.caption2).foregroundStyle(Theme.fog)
                                }
                            }
                            .tint(Theme.ecto)
                            .accessibilityIdentifier("export-file-toggle")
                        }
                    } header: {
                        Text("Recordings")
                    } footer: {
                        Text("Anything left out is still named in the document, so whoever reads it knows it exists.")
                    }
                }

                if let result {
                    Section {
                        LabeledContent("Readings", value: "\(result.readingCount)")
                        LabeledContent("Files", value: "\(result.mediaCount)")
                        LabeledContent("Size", value: ByteCountFormatter.string(
                            fromByteCount: result.byteCount, countStyle: .file))
                        if !result.omittedMedia.isEmpty {
                            Label("\(result.omittedMedia.count) referenced but not included",
                                  systemImage: "info.circle")
                                .font(.caption).foregroundStyle(Theme.fog)
                        }
                        ShareLink(item: result.url) {
                            Label("Share the bundle", systemImage: "square.and.arrow.up")
                        }
                        .accessibilityIdentifier("share-export")
                    } header: {
                        Text("Ready")
                    } footer: {
                        Text("A .zip holding data.json and the files you chose, in the IsHaunted device data format.")
                    }
                }

                if let errorMessage {
                    Section {
                        Label(errorMessage, systemImage: "exclamationmark.triangle")
                            .font(.callout).foregroundStyle(Theme.danger)
                    }
                }

                Section {
                    Button {
                        Task { await build() }
                    } label: {
                        if busy { ProgressView().frame(maxWidth: .infinity) }
                        else { Text(result == nil ? "Build the bundle" : "Build it again")
                                .frame(maxWidth: .infinity) }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(busy)
                    .accessibilityIdentifier("build-export")
                }
            }
            .navigationTitle("Export")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }.disabled(busy)
                }
            }
            .onAppear {
                captures = store.captures(for: sessionId)
                // Everything on by default: leaving a recording out should be a decision, not
                // an oversight.
                chosen = Set(captures.map(\.relativePath))
            }
        }
    }

    private func build() async {
        busy = true
        errorMessage = nil
        defer { busy = false }
        do {
            result = try await store.export(sessionId, includedMedia: Array(chosen))
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
