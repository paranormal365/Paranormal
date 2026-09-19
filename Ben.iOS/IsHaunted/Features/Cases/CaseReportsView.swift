import SwiftUI
import PDFKit
import BenKit

/// The reports a group has published on this case, and reading one.
struct CaseReportsView: View {
    @Environment(AppDependencies.self) private var dependencies

    let caseId: UUID
    @Environment(Router.self) private var router
    @State private var store: CaseReportsStore?

    var body: some View {
        content
            .navigationTitle("Reports")
            .navigationBarTitleDisplayMode(.inline)
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
            // A pushed screen rather than a sheet: on iPad a sheet raised from inside the detail
            // column of a NavigationSplitView did not present at all — the row was tapped and
            // nothing happened. Pushing also matches how the rest of the app moves, and reading a
            // report is a place you GO, not a thing you glance at.
            NavigationLink(value: AppRoute.caseReportPDF(caseId: caseId, reportId: report.id)) {
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(report.title).foregroundStyle(Theme.bone)
                        Text(report.readerDate.formatted(date: .abbreviated, time: .omitted))
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                    Spacer()
                    Image(systemName: "doc.text").foregroundStyle(Theme.ecto)
                }
            }
            .accessibilityIdentifier("report-row")
        }
        .listStyle(.insetGrouped)
    }

}

/// One report, downloaded and on screen.
///
/// The download happens HERE rather than before navigating, so the wait has somewhere to live: a
/// spinner on this screen, and a refusal that can be read and retried, instead of a row that sits
/// there doing nothing while a file arrives.
struct CaseReportPDFView: View {
    @Environment(AppDependencies.self) private var dependencies

    let caseId: UUID
    let reportId: UUID

    @State private var url: URL?
    @State private var errorMessage: String?

    var body: some View {
        Group {
            if let url {
                PDFKitView(url: url).ignoresSafeArea(edges: .bottom)
            } else if let errorMessage {
                ContentUnavailableView {
                    Label("Couldn't open that report", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(errorMessage)
                } actions: {
                    Button("Try again") { Task { await fetch() } }
                }
            } else {
                ProgressView("Fetching the report")
            }
        }
        .navigationTitle("Report")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let url {
                ToolbarItem(placement: .primaryAction) {
                    ShareLink(item: url) { Image(systemName: "square.and.arrow.up") }
                }
            }
        }
        .task { await fetch() }
    }

    private func fetch() async {
        errorMessage = nil
        let store = CaseReportsStore(caseId: caseId, api: dependencies.api)
        await store.load()
        guard let report = store.reports.first(where: { $0.id == reportId }) else {
            errorMessage = "That report isn't on this case any more."
            return
        }
        switch await store.downloadPDF(report) {
        case .success(let downloaded): url = downloaded
        case .failure(let error): errorMessage = error.message
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
