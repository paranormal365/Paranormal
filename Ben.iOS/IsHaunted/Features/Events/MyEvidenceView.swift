import SwiftUI
import BenKit

/// What this account has offered at other people's public events — and what it can do with it.
///
/// **The screen exists because the operator's gallery is not the only place a photograph
/// belongs.** They curate what their EVENT shows; the person who took the picture decides whether
/// it joins the public record of the PLACE. Before this, a guest whose submission was declined had
/// handed over the only copy the product would show them.
///
/// Two independent states per row, and the layout keeps them apart on purpose: the group's answer,
/// and whether it is in the place's archive. Showing them as one status would imply that accepting
/// publishes it, which is exactly the thing that is not true.
struct MyEvidenceView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var rows: [EvidenceSubmission] = []
    @State private var loading = true
    @State private var loadFailed = false
    @State private var busyId: UUID?
    @State private var messages: [UUID: (ok: Bool, text: String)] = [:]

    var body: some View {
        Group {
            if loading {
                ProgressView("Loading…")
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if loadFailed {
                // A failure is not an empty list. Telling somebody they have contributed nothing
                // when the server refused would be a lie they cannot see through.
                ContentUnavailableView(
                    "Couldn't load your evidence",
                    systemImage: "exclamationmark.triangle",
                    description: Text("Pull to try again."))
            } else if rows.isEmpty {
                ContentUnavailableView(
                    "Nothing offered yet",
                    systemImage: "camera",
                    description: Text("Evidence is offered from a public event you attended."))
            } else {
                List(rows) { row in
                    rowView(row)
                }
                .listStyle(.insetGrouped)
            }
        }
        .navigationTitle("My evidence")
        .navigationBarTitleDisplayMode(.large)
        .refreshable { await load() }
        .task { await load() }
    }

    @ViewBuilder
    private func rowView(_ row: EvidenceSubmission) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(row.eventTitle)
                .font(.headline)
            Text(row.fileName)
                .font(.subheadline)
                .foregroundStyle(Theme.fog)

            HStack(spacing: 8) {
                Label(row.status.summary, systemImage: statusIcon(row.status))
                    .font(.caption)
                    .foregroundStyle(Theme.fog)
                Text("·").foregroundStyle(Theme.fog)
                Label(row.archiveSummary, systemImage: row.isInArchive ? "building.columns.fill" : "building.columns")
                    .font(.caption)
                    .foregroundStyle(row.isInArchive ? Theme.success : Theme.fog)
            }

            if let reason = row.rejectionReason, !reason.isEmpty {
                Text(reason).font(.caption).foregroundStyle(Theme.fog)
            }

            // Offered only when there is an archive to contribute to. When there is not, the row
            // says why above rather than showing a button that would only ever refuse.
            if row.placeAcceptsArchive {
                Button {
                    Task { await toggle(row) }
                } label: {
                    if busyId == row.id {
                        ProgressView()
                    } else {
                        Label(row.isInArchive ? "Remove from archive" : "Add to place archive",
                              systemImage: row.isInArchive ? "minus.circle" : "plus.circle")
                    }
                }
                .buttonStyle(.bordered)
                .disabled(busyId == row.id)
                .padding(.top, 2)
            }

            if let message = messages[row.id] {
                Text(message.text)
                    .font(.caption)
                    .foregroundStyle(message.ok ? Theme.success : Theme.danger)
            }
        }
        .padding(.vertical, 4)
    }

    private func statusIcon(_ status: EvidenceStatus) -> String {
        switch status {
        case .pending:  "clock"
        case .accepted: "checkmark.circle"
        case .rejected: "xmark.circle"
        }
    }

    private func load() async {
        loading = rows.isEmpty
        let result = await dependencies.evidenceActions.mine()

        switch result {
        case .ok(let items):
            rows = items
            loadFailed = false
        default:
            loadFailed = rows.isEmpty
        }

        loading = false
    }

    private func toggle(_ row: EvidenceSubmission) async {
        busyId = row.id
        messages[row.id] = nil

        let outcome = row.isInArchive
            ? await dependencies.evidenceActions.retractFromPlace(
                eventId: row.orgCalendarEventId, submissionId: row.id)
            : await dependencies.evidenceActions.publishToPlace(
                eventId: row.orgCalendarEventId, submissionId: row.id)

        busyId = nil

        switch outcome {
        case .success:
            messages[row.id] = (true, row.isInArchive
                ? "Removed from the place's archive."
                : "Added — it's on the place's page now.")
            await load()
        case .failure(let error):
            // The server's own sentence, unchanged. On a free account the refusal explains that
            // keeping work private is part of a plan — the one message that tells somebody what
            // to do next, and the one a generic "couldn't do that" would throw away.
            messages[row.id] = (false, reason(from: error))
        }
    }

    private func reason(from error: FeedActionError) -> String {
        switch error {
        case .failed(let reason): reason ?? "Couldn't do that just now."
        case .sessionEnded:       "Please sign in again."
        case .rateLimited:        "Too many attempts — try again shortly."
        }
    }
}
