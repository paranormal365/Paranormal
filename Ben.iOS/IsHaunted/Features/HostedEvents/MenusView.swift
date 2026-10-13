import SwiftUI
import BenKit

/// The event's published menus, each drawn in the design its host chose (10/13/2026; first built in item 235
/// phase 14b as plain lists).
///
/// **Anyone can read them.** A published menu is public on the website, so the app shows it signed out
/// too, the same as the event page. Each one can be saved or shared as the same PDF the website gives.
struct MenusView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var menus: [EventMenu] = []
    @State private var loaded = false
    @State private var failure: String?
    @State private var fetching: UUID?
    @State private var preview: URL?
    @State private var pdfFailure: String?

    private var fonts: MenuFontLibrary { MenuFontLibrary.shared }

    var body: some View {
        Group {
            if !menus.isEmpty {
                ScrollView {
                    LazyVStack(spacing: 22) {
                        if let included = EventMenuWording.includedWithTheEvent(menus) {
                            Label { Text("**Included with the event:** \(included).") } icon: { Image(systemName: "checkmark.seal") }
                                .font(.subheadline)
                                .foregroundStyle(Theme.bone)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .accessibilityIdentifier("menus-included")
                        }
                        ForEach(menus) { menu in
                            VStack(alignment: .trailing, spacing: 8) {
                                DesignedMenuView(menu: menu, design: fonts.catalog?.design(menu.designKey))
                                    .accessibilityIdentifier("menu-\(menu.id.uuidString)")
                                Button {
                                    Task { await openPdf(menu) }
                                } label: {
                                    if fetching == menu.id {
                                        ProgressView()
                                    } else {
                                        Label("PDF", systemImage: "doc.richtext")
                                    }
                                }
                                .buttonStyle(.bordered)
                                .disabled(fetching != nil)
                                .accessibilityLabel("Open the \(menu.shownHeading) menu as a PDF")
                            }
                        }
                    }
                    // A menu is a sheet of paper: on an iPad it stays a menu's width, not the screen's.
                    .frame(maxWidth: 640)
                    .padding(16)
                    .frame(maxWidth: .infinity)
                }
                .background(Theme.ink)
            } else if !loaded {
                ProgressView("Loading the menus…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let failure {
                ContentUnavailableView {
                    Label("Couldn't load the menus", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure)
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.signalPrimary)
                }
            } else {
                ContentUnavailableView("No menus yet", systemImage: "fork.knife",
                                       description: Text("Menus appear here once the organizers publish them."))
            }
        }
        .navigationTitle("Menus")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
        .quickLookPreview($preview)
        .alert("Couldn't open the PDF", isPresented: Binding(get: { pdfFailure != nil }, set: { if !$0 { pdfFailure = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(pdfFailure ?? "")
        }
    }

    private func load() async {
        await fonts.loadCatalog(dependencies.api)
        switch await HostedEventsStore(api: dependencies.api).loadPublishedMenus(hostedEventId) {
        case .ok(let value):
            menus = value
            failure = nil
        case .failed(let reason, _):
            failure = reason ?? "Check your connection and try again."
        case .sessionEnded:
            failure = "Sign in again to see the menus."
        case .rateLimited:
            failure = "Too many requests. Try again shortly."
        }
        loaded = true
    }

    private func openPdf(_ menu: EventMenu) async {
        fetching = menu.id
        defer { fetching = nil }
        switch await HostedEventsStore(api: dependencies.api).downloadMenuPdf(menu) {
        case .ok(let url): preview = url
        case .failed(let reason, _): pdfFailure = reason ?? "Check your connection and try again."
        case .sessionEnded, .rateLimited: pdfFailure = "Try again shortly."
        }
    }
}
