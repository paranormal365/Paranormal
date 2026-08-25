import SwiftUI
import PDFKit
import BenKit

/// The reports a group has published on this case, and reading one.
struct CaseReportsView: View {
    @Environment(AppDependencies.self) private var dependencies

    let caseId: UUID
    @State private var store: CaseReportsStore?
    @State private var opening: UUID?
    @State private var openedPDF: URL?
    @State private var errorMessage: String?

    var body: some View {
        content
            .navigationTitle("Reports")
            .navigationBarTitleDisplayMode(.inline)
            .alert("Couldn't open that report",
                   isPresented: Binding(get: { errorMessage != nil },
                                        set: { if !$0 { errorMessage = nil } })) {
                Button("OK", role: .cancel) { errorMessage = nil }
            } message: {
                Text(errorMessage ?? "")
            }
            .sheet(item: Binding(get: { openedPDF.map(IdentifiedURL.init) },
                                 set: { openedPDF = $0?.url })) { identified in
                ReportPDFView(url: identified.url)
            }
            .task {
                let store = CaseReportsStore(caseId: caseId, api: dependencies.api)
                self.store = store
                await store.load()
            }
            .onChange(of: dependencies.session.me?.userId) { _, _ in
                Task { await store?.load() }
            }
            .refreshable { await store?.load() }
    }

    @ViewBuilder
    private var content: some View {
        switch store?.state {
        case .loading, nil:
            ProgressView().frame(maxWidth: .infinity).padding(24)

        case .signedOut:
            ContentUnavailableView("Sign in to read your reports",
                                   systemImage: "person.crop.circle.badge.questionmark")

        case .failed(let reason):
            // A refusal is not an empty list, and must not look like one.
            ContentUnavailableView {
                Label("Couldn't load your reports", systemImage: "exclamationmark.triangle")
            } description: {
                Text(reason ?? "The server couldn't be reached.")
            } actions: {
                Button("Try again") { Task { await store?.load() } }
            }

        case .loaded:
            if store?.reports.isEmpty == true {
                ContentUnavailableView(
                    "No reports yet", systemImage: "doc.text",
                    description: Text("When your group publishes a report on this case, it appears here."))
            } else {
                reportList
            }
        }
    }

    private var reportList: some View {
        List(store?.reports ?? []) { report in
            Button {
                Task { await open(report) }
            } label: {
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(report.title).foregroundStyle(Theme.bone)
                        Text(report.readerDate.formatted(date: .abbreviated, time: .omitted))
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                    Spacer()
                    if opening == report.id {
                        ProgressView()
                    } else {
                        Image(systemName: "doc.text").foregroundStyle(Theme.ecto)
                    }
                }
            }
            .buttonStyle(.plain)
            .disabled(opening != nil)
            .accessibilityIdentifier("report-row")
        }
        .listStyle(.insetGrouped)
    }

    private func open(_ report: MyCaseReport) async {
        opening = report.id
        defer { opening = nil }
        switch await store?.downloadPDF(report) {
        case .success(let url): openedPDF = url
        case .failure(let error): errorMessage = error.message
        case nil: break
        }
    }
}

/// `sheet(item:)` needs identity, and a URL has none of its own.
private struct IdentifiedURL: Identifiable {
    let url: URL
    var id: String { url.absoluteString }
}

/// A downloaded report, on screen and shareable.
private struct ReportPDFView: View {
    let url: URL
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            PDFKitView(url: url)
                .ignoresSafeArea(edges: .bottom)
                .navigationTitle("Report")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) {
                        Button("Done") { dismiss() }
                    }
                    ToolbarItem(placement: .primaryAction) {
                        ShareLink(item: url) { Image(systemName: "square.and.arrow.up") }
                    }
                }
        }
    }
}

private struct PDFKitView: UIViewRepresentable {
    let url: URL

    func makeUIView(context: Context) -> PDFView {
        let view = PDFView()
        view.autoScales = true
        view.document = PDFDocument(url: url)
        return view
    }

    func updateUIView(_ view: PDFView, context: Context) {
        if view.document?.documentURL != url {
            view.document = PDFDocument(url: url)
        }
    }
}
