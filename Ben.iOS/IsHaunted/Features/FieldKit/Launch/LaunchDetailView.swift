import SwiftUI
import BenKit

/// One launch (item 252): what the group started, Join, and a QR code that opens it on somebody
/// else's phone. For the lead just after pressing Launch it also says who it reached.
struct LaunchDetailView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let launchId: UUID
    var sent: LaunchSent?

    @State private var launch: FieldLaunchRecord?
    @State private var problem: String?
    /// Who has asked to join by the code — only ever loaded for somebody who may manage the launch.
    @State private var requests: [JoinRequestRecord] = []
    @State private var deciding: UUID?

    var body: some View {
        List {
            if let problem, launch != nil {
                Section { Text(problem).foregroundStyle(Theme.warning) }
            }
            if let sent {
                Section {
                    Label(Self.sentSentence(sent), systemImage: "checkmark.circle.fill")
                        .foregroundStyle(Theme.success)
                        .accessibilityIdentifier("launch-sent")
                }
            }
            if let launch {
                Section {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(launch.title).font(.title3.weight(.semibold)).foregroundStyle(Theme.bone)
                        Text(launch.organizationName).font(.callout).foregroundStyle(Theme.fog)
                    }
                    .padding(.vertical, 4)
                    LabeledContent("Started by", value: launch.launchedByName)
                    LabeledContent("Started", value: launch.launchedUtc.readerFormatted(date: .omitted, time: .shortened))
                    LabeledContent("Open until", value: launch.expiresUtc.readerFormatted(date: .abbreviated, time: .shortened))
                    if let place = launch.locationLabel {
                        LabeledContent("Where", value: place)
                    }
                }
                Section {
                    Button {
                        router.push(.joinLaunch(launch.id))
                    } label: {
                        Label("Join the session", systemImage: "record.circle")
                            .font(.headline)
                    }
                    .accessibilityIdentifier("join-launch-button")
                } footer: {
                    Text("Opens Field Kit set up for this, with nothing to choose. Nothing records until you press Start, and every session you take stays on this phone until you send it.")
                }
                if let token = launch.joinToken {
                    if !requests.isEmpty { requestsSection }
                    Section {
                        QRCodeView(text: dependencies.environment.websiteURL(path: "field-kit/join/\(token)").absoluteString)
                            .frame(maxWidth: 240)
                            .frame(maxWidth: .infinity)
                            .accessibilityIdentifier("launch-qr")
                    } header: {
                        Text("Join by scanning")
                    } footer: {
                        Text("Hold this up. Somebody registered is straight in; anybody else signs in and asks, and you say yes or no here.")
                    }
                }
            } else if let problem {
                ContentUnavailableView {
                    Label("This session isn't open", systemImage: "clock.badge.xmark")
                } description: {
                    Text(problem)
                }
            } else {
                ProgressView().frame(maxWidth: .infinity)
            }
        }
        .navigationTitle("Group session")
        .navigationBarTitleDisplayMode(.inline)
        .task { await load() }
        // While the lead has this open, people asking appear without a pull.
        .task(id: launch?.joinToken) {
            guard launch?.joinToken != nil else { return }
            while !Task.isCancelled {
                await loadRequests()
                try? await Task.sleep(for: .seconds(8))
            }
        }
    }

    /// Who it reached, said the way a person would say it — singular or plural, all, some or none.
    static func sentSentence(_ sent: LaunchSent) -> String {
        let n = sent.people, app = sent.peopleWithTheApp
        guard n > 0 else { return "Launched. Nobody is registered yet, so the card is all there is." }
        let people = n == 1 ? "1 person" : "\(n) people"
        guard sent.pushConfigured else { return "Launched. The card is in the feed of the \(people) registered." }
        if app == 0 {
            return "Launched for \(people). \(n == 1 ? "They don't" : "None of them") have the app yet, so the card in the feed is how they'll see it."
        }
        if app == n {
            return n == 1 ? "Launched for 1 person, who has the app and was notified."
                          : "Launched for \(people), and all of them have the app and were notified."
        }
        let some = app == 1 ? "1 of them has the app and was notified" : "\(app) of them have the app and were notified"
        return "Launched for \(people). \(some); the rest will see the card in their feed."
    }

    private var requestsSection: some View {
        Section {
            ForEach(requests) { request in
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(request.displayName).font(.subheadline.weight(.semibold)).foregroundStyle(Theme.bone)
                        Text(request.status == "pending" ? "Asked \(request.requestedUtc.readerFormatted(date: .omitted, time: .shortened))"
                             : request.status == "approved" ? "Let in" : "Not added")
                            .font(.caption).foregroundStyle(Theme.fog)
                    }
                    Spacer()
                    if request.status == "pending" {
                        Button("No") { Task { await decide(request, approve: false) } }
                            .buttonStyle(.bordered)
                            .accessibilityIdentifier("decline-\(request.appUserId.uuidString.lowercased())")
                        Button("Let in") { Task { await decide(request, approve: true) } }
                            .buttonStyle(.borderedProminent)
                            .accessibilityIdentifier("approve-\(request.appUserId.uuidString.lowercased())")
                    }
                }
                .disabled(deciding == request.id)
            }
        } header: {
            Text("Asking to join")
        } footer: {
            Text("Letting somebody in registers them — a seat on a walk, a place at an event, a guest pass for an investigation — and tells their phone. A paid event is never booked this way.")
        }
    }

    private func loadRequests() async {
        if case .ok(let found) = await dependencies.fieldLaunches.requests(launchId: launchId) { requests = found }
    }

    private func decide(_ request: JoinRequestRecord, approve: Bool) async {
        deciding = request.id
        defer { deciding = nil }
        switch await dependencies.fieldLaunches.decide(launchId: launchId, requestId: request.id, approve: approve) {
        case .ok: await loadRequests()
        case .failed(let reason, _): problem = reason ?? "That couldn't be saved just now."
        case .sessionEnded: problem = "Your session ended — sign in again."
        case .rateLimited: problem = "Too many tries just now. Wait a moment and try again."
        }
    }

    private func load() async {
        switch await dependencies.fieldLaunches.launch(launchId) {
        case .ok(let record): launch = record
        case .failed(_, let status) where status == 404:
            problem = "It has ended, or it was sent to other people."
        case .failed(let reason, _): problem = reason ?? "The server couldn't be reached."
        case .sessionEnded: problem = "Sign in to see it."
        case .rateLimited: problem = "Too many tries just now. Wait a moment and try again."
        }
    }
}
