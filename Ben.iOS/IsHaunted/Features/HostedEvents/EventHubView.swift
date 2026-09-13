import SwiftUI
import BenKit

/// Everything for one hosted event, in one place (item 235 phase 14b): the pass, the programme, the menus, the
/// downloads and the room.
///
/// **A row appears only when there is something behind it.** No programme published, menus not yet shared with
/// this guest, no files for guests, not one of the people in the room — each of those leaves its row out rather
/// than leading to an empty screen or a refusal. A server that cannot be reached is said as such.
struct EventHubView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var store: HostedEventsStore?
    @State private var event: PublicHostedEvent?
    @State private var booking: MyHostedEventBooking?
    @State private var programme: HostedEventProgramme?
    @State private var menus: HostedEventMenus?
    @State private var files: [HostedEventFile] = []
    @State private var room: EventRoom?
    @State private var loaded = false
    @State private var unreachable = false
    @State private var openOnWebsite: URL?

    var body: some View {
        List {
            if let title = event?.name ?? booking?.eventName {
                Section { header(title) }
            }

            if !loaded {
                Section { ProgressView("Loading the event…") }
            } else {
                Section {
                    if booking?.status == .confirmed {
                        NavigationLink(value: AppRoute.eventPass(hostedEventId)) {
                            row("Your pass", detail: "The code for the door — it opens with no signal", icon: "qrcode")
                        }
                        .accessibilityIdentifier("hub-pass")
                    }
                    if let programme {
                        NavigationLink(value: AppRoute.eventProgramme(hostedEventId)) {
                            HStack {
                                row("Programme", detail: programmeDetail(programme), icon: "calendar")
                                if programme.changedSinceSeen { StatusBadge(text: "Changed", colour: Theme.warning) }
                            }
                        }
                        .accessibilityIdentifier("hub-programme")
                    }
                    if let menus, !menus.menus.isEmpty {
                        NavigationLink(value: AppRoute.eventMenus(hostedEventId)) {
                            row("Menus", detail: menus.menus.count == 1 ? "1 meal" : "\(menus.menus.count) meals", icon: "fork.knife")
                        }
                        .accessibilityIdentifier("hub-menus")
                    }
                    if !files.isEmpty {
                        NavigationLink(value: AppRoute.eventDownloads(hostedEventId)) {
                            row("Downloads", detail: files.count == 1 ? "1 file" : "\(files.count) files", icon: "arrow.down.doc")
                        }
                        .accessibilityIdentifier("hub-downloads")
                    }
                    if let room {
                        NavigationLink(value: AppRoute.eventRoom(hostedEventId)) {
                            row("The room", detail: room.canAddPhotos
                                ? "Posts and photos from the people at the event"
                                : "Posts from the people at the event", icon: "bubble.left.and.bubble.right")
                        }
                        .accessibilityIdentifier("hub-room")
                    }
                } footer: {
                    if unreachable {
                        Text("Some of this couldn't be fetched just now. Pull down to try again.")
                            .foregroundStyle(Theme.warning)
                    } else if booking?.status != .confirmed && programme == nil && room == nil && files.isEmpty {
                        Text("The programme, menus, downloads and the event's room appear here once your place is confirmed and the organizers add them.")
                    }
                }

                if let event {
                    Section {
                        Button {
                            openOnWebsite = dependencies.environment.websiteURL(path: event.pagePath)
                        } label: {
                            Label("The event's page", systemImage: "safari")
                        }
                    }
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle(event?.name ?? booking?.eventName ?? "Event")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task(id: dependencies.session.me?.userId) {
            if store == nil { store = HostedEventsStore(api: dependencies.api) }
            await load()
        }
        .sheet(item: $openOnWebsite) { url in SafariSheet(url: url).ignoresSafeArea() }
    }

    @ViewBuilder
    private func header(_ title: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(title).font(.title3.weight(.semibold))
                Spacer()
                if let booking {
                    let words = HostedEventWords.status(booking)
                    StatusBadge(text: words.text, colour: words.colour)
                }
            }
            let place = [event?.organizationName ?? booking?.organizationName, event?.venueName ?? booking?.venueName]
                .compactMap { $0 }.joined(separator: " · ")
            if !place.isEmpty { Text(place).font(.subheadline).foregroundStyle(Theme.fog) }
            if let starts = event?.startsOn ?? booking?.startsOn, let ends = event?.endsOn ?? booking?.endsOn {
                Text(HostedEventWords.dates(starts, ends)).font(.subheadline).foregroundStyle(Theme.fog)
            }
            if let booking { Text(HostedEventWords.what(booking)).font(.caption).foregroundStyle(Theme.fog) }
        }
        .padding(.vertical, 4)
    }

    private func row(_ title: String, detail: String, icon: String) -> some View {
        Label {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(detail).font(.caption).foregroundStyle(Theme.fog)
            }
        } icon: {
            Image(systemName: icon).foregroundStyle(Theme.ecto)
        }
    }

    private func programmeDetail(_ programme: HostedEventProgramme) -> String {
        let live = programme.sessions.filter { !$0.isCancelled }
        let signedUp = live.filter { $0.mine?.waiting == false }.count
        let waiting = live.filter { $0.mine?.waiting == true }.count
        return ([live.count == 1 ? "1 session" : "\(live.count) sessions",
                 signedUp > 0 ? "signed up for \(signedUp)" : nil,
                 waiting > 0 ? "waiting for \(waiting)" : nil] as [String?])
            .compactMap { $0 }.joined(separator: " · ")
    }

    private func load() async {
        guard let store else { return }
        async let eventRead = store.loadEvent(hostedEventId)
        async let bookingRead = store.loadMyBooking(hostedEventId)
        async let programmeRead = store.loadProgramme(hostedEventId)
        async let menusRead = store.loadMenus(hostedEventId)
        async let filesRead = store.loadFiles(hostedEventId)
        async let roomRead = store.loadRoom(hostedEventId)

        var missed = false
        switch await eventRead { case .ok(let value): event = value; default: missed = true }
        switch await bookingRead { case .ok(let value): booking = value; default: missed = true }
        switch await programmeRead { case .ok(let value): programme = value; default: missed = true }
        switch await menusRead { case .ok(let value): menus = value; default: missed = true }
        switch await filesRead { case .ok(let value): files = value; default: missed = true }
        switch await roomRead { case .ok(let value): room = value; default: missed = true }
        unreachable = missed
        loaded = true
    }
}
