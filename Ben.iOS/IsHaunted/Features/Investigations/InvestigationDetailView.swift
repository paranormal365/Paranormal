import SwiftUI
import BenKit

/// One investigation, from the attendee's side — and the door into recording for it.
///
/// Everything shown here already travels on `MyInvestigation`; there is no detail endpoint to
/// call, so this screen works from whatever the roster loaded and never leaves somebody looking
/// at a spinner in a basement.
struct InvestigationDetailView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let investigationId: UUID

    @State private var store: InvestigationsStore?
    @State private var errorMessage: String?

    private var investigation: MyInvestigation? {
        store?.investigations.first { $0.investigationId == investigationId }
    }

    var body: some View {
        Group {
            if let investigation {
                detail(investigation)
            } else if store == nil {
                ProgressView().frame(maxWidth: .infinity).padding(40)
            } else {
                ContentUnavailableView {
                    Label("This investigation isn't on your list", systemImage: "binoculars")
                } description: {
                    Text("It may have been cancelled, or you may no longer be on it.")
                }
            }
        }
        .navigationTitle(investigation?.caseReference ?? "Investigation")
        .navigationBarTitleDisplayMode(.inline)
        .alert("Couldn't start the session",
               isPresented: Binding(get: { errorMessage != nil },
                                    set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) { errorMessage = nil }
        } message: { Text(errorMessage ?? "") }
        .task {
            let store = InvestigationsStore(api: dependencies.api)
            self.store = store
            await store.load()
        }
    }

    @ViewBuilder
    private func detail(_ investigation: MyInvestigation) -> some View {
        List {
            Section {
                Text(investigation.title).font(.headline)
                if let caseTitle = investigation.caseTitle, !caseTitle.isEmpty {
                    LabeledContent("Case", value: caseTitle)
                }
                LabeledContent("Group", value: investigation.orgName)
                if let start = investigation.scheduledDateTime {
                    LabeledContent("When",
                                   value: start.formatted(date: .abbreviated, time: .shortened))
                }
                if let location = investigation.location, !location.isEmpty {
                    LabeledContent("Where", value: location)
                }
                if let role = investigation.assignedRole, !role.isEmpty {
                    LabeledContent("Your role", value: role)
                }
                if let due = investigation.evidenceDueDate {
                    LabeledContent("Evidence due",
                                   value: due.formatted(date: .abbreviated, time: .omitted))
                }
            }

            // The reason this screen exists on a phone.
            Section {
                Button {
                    start(for: investigation)
                } label: {
                    Label("Start field session", systemImage: "record.circle")
                        .font(.headline)
                }
                .accessibilityIdentifier("start-session-for-investigation")
            } footer: {
                Text("Records magnetic field, sound and where you were, and holds your photos and recordings — on this device, linked to this investigation.")
            }

            if !fieldSessions(for: investigation).isEmpty {
                Section("Sessions on this investigation") {
                    ForEach(fieldSessions(for: investigation)) { summary in
                        Button {
                            router.push(summary.isRecording
                                        ? .fieldSession(summary.id)
                                        : .fieldSessionReview(summary.id))
                        } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(summary.title).foregroundStyle(Theme.bone)
                                    Text(summary.startedAt.formatted(date: .abbreviated,
                                                                     time: .shortened))
                                        .font(.caption).foregroundStyle(Theme.fog)
                                }
                                Spacer()
                                Image(systemName: "chevron.right")
                                    .font(.caption).foregroundStyle(Theme.fog)
                            }
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("field-session-row")
                    }
                }
            }
        }
    }

    private func fieldSessions(for investigation: MyInvestigation) -> [FieldSessionSummary] {
        dependencies.fieldKit.sessions.filter {
            $0.investigationId == investigation.investigationId
        }
    }

    private func start(for investigation: MyInvestigation) {
        do {
            let id = try dependencies.fieldKit.startSession(
                locationLabel: investigation.location,
                investigationId: investigation.investigationId,
                investigationTitle: investigation.title)
            router.push(.fieldSession(id))
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
