import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Designed menus in the app (10/13/2026): what the API really answers, the words a menu prints (which
/// must match the website's and the PDF's), and the fonts a design asks for.
@Suite("Designed menus")
@MainActor
struct EventMenuTests {

    private func fixture(_ name: String) throws -> Data { try Fixtures.data(name, in: Bundle.module) }

    private static let eventId = UUID(uuidString: "40000002-0000-0000-0000-000000000002")!

    private static func store(_ transport: MockTransport) -> HostedEventsStore {
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: nil),
            transport: transport, environment: { .dev })
        return HostedEventsStore(
            api: APIClient(environment: { .dev }, transport: transport, tokens: tokens),
            cache: PassCache(directory: FileManager.default.temporaryDirectory
                .appendingPathComponent("menus-\(UUID().uuidString)", isDirectory: true)))
    }

    // ── the contract, from what the API really answered ─────────────────────

    @Test func publishedMenusDecodeWithTheirMealsPricesAndMarks() throws {
        let menus = try BenJSON.decoder.decode([EventMenu].self, from: fixture("published-menus"))
        #expect(menus.count == 5)
        #expect(menus.allSatisfy { $0.hostedEventId == Self.eventId })

        let snacks = try #require(menus.first { $0.throughoutTheEvent })
        #expect(snacks.designKey == "chalkboard-cafe")
        #expect(EventMenuWording.whenLines(snacks) == ["Throughout the event"])

        let candy = try #require(snacks.sections.flatMap(\.entries).first { $0.item.name == "Full-size candy bars" })
        #expect(candy.pricing == .pricedSeparately)
        #expect(EventMenuWording.entryPrice(snacks, candy) == "$2")

        let water = try #require(snacks.sections.flatMap(\.entries).first { $0.item.name == "Bottled water" })
        #expect(EventMenuWording.entryPrice(snacks, water) == "Included")

        let gummies = try #require(snacks.sections.flatMap(\.entries).first { $0.item.name == "Gummy ghosts" })
        #expect(gummies.item.dietary.contains(.vegan))
        #expect(EventMenuWording.letters(gummies.item.dietary) == "VG")

        let breakfast = try #require(menus.first { $0.shownHeading == "Breakfast" })
        let meal = try #require(breakfast.meals.first)
        #expect(meal.servedAtLocal == "08:00:00")
        #expect(EventMenuWording.whenLines(breakfast).first?.hasSuffix("Breakfast, 8:00 AM") == true)
        // Everything on it is covered by the event: said once, not on every line.
        #expect(EventMenuWording.includedLine(breakfast) == "Included with the event")
    }

    @Test func theDesignCatalogDecodesWithEveryDesignAndItsFonts() throws {
        let catalog = try BenJSON.decoder.decode(MenuDesignCatalog.self, from: fixture("menu-designs"))
        #expect(catalog.designs.count == 25)

        let seance = try #require(catalog.design("seance-parlor"))
        #expect(seance.headingFace == "Cinzel")
        #expect(seance.bodyFace == "Cormorant Garamond")
        #expect(seance.frame == .double)
        #expect(seance.ornament == .flourish)
        #expect(seance.isDark)

        // Every face a design names is one the catalog has a file for.
        for design in catalog.designs {
            #expect(catalog.closest(design.headingFace, weight: design.headingWeight, italic: false) != nil, "\(design.key)")
            #expect(catalog.closest(design.bodyFace, weight: 400, italic: false) != nil, "\(design.key)")
        }
    }

    @Test func aFaceWithoutTheCutAskedForGivesTheOneItHas() throws {
        let catalog = try BenJSON.decoder.decode(MenuDesignCatalog.self, from: fixture("menu-designs"))
        #expect(catalog.closest("Pirata One", weight: 700, italic: false)?.file == "PirataOne-Regular.ttf")
        #expect(catalog.closest("Cinzel", weight: 400, italic: true)?.file == "Cinzel-Regular.ttf")
        #expect(catalog.closest("Lora", weight: 400, italic: true)?.file == "Lora-Italic.ttf")
        #expect(catalog.closest("Comic Sans", weight: 400, italic: false) == nil)
    }

    @Test func anOrnamentTheAppDoesNotKnowReadsAsNone() throws {
        let json = ##"{"key":"k","name":"n","family":"f","description":"d","font":0,"paper":"#000000","ink":"#FFFFFF","accent":"#FFFFFF","muted":"#FFFFFF","frame":0,"ornament":99,"upperHeadings":false,"twoColumns":false,"dottedLeaders":false,"headingWeight":700,"headingScale":1,"isDark":true}"##
        let design = try BenJSON.decoder.decode(MenuDesign.self, from: Data(json.utf8))
        #expect(design.ornament == .none)
    }

    // ── the words ────────────────────────────────────────────────────────────

    @Test func pricesReadTheWayTheWebsiteWritesThem() throws {
        let menus = try BenJSON.decoder.decode([EventMenu].self, from: fixture("published-menus"))
        var menu = try #require(menus.first)
        menu.prices = [EventMenuPrice(label: nil, amount: 25)]
        #expect(EventMenuWording.priceLine(menu) == "$25 per person")
        menu.prices = [EventMenuPrice(label: "Adults", amount: 25), EventMenuPrice(label: "Children", amount: 12.5)]
        #expect(EventMenuWording.priceLine(menu) == "Adults $25  ·  Children $12.50")
        #expect(EventMenuWording.servedAt("19:30:00") == "7:30 PM")
        #expect(EventMenuWording.servedAt("00:15:00") == "12:15 AM")
        #expect(EventMenuWording.servedAt("nonsense") == nil)
    }

    @Test func theEventSaysWhatItsPriceIncludes() throws {
        let menus = try BenJSON.decoder.decode([EventMenu].self, from: fixture("published-menus"))
        let line = try #require(EventMenuWording.includedWithTheEvent(menus))
        #expect(line.contains("Friday breakfast"))
        #expect(line.contains("some of Snack table"))
    }

    @Test func allergensAreSaidInWords() {
        let item = EventMenuItem(id: UUID(), name: "Pie", description: nil, imageUploadFileId: nil,
                                 dietary: [.vegetarian], allergens: [.milk, .treeNuts], dietaryNote: "Made in a kitchen that uses peanuts.")
        #expect(EventMenuWording.allergenLine(item) == "Contains milk, tree nuts. Made in a kitchen that uses peanuts.")
    }

    // ── the calls ────────────────────────────────────────────────────────────

    @Test func publishedMenusAreReadSignedOut() async throws {
        let transport = MockTransport(status: 200, body: try fixture("published-menus"), headers: ["Content-Type": "application/json"])
        let result = await Self.store(transport).loadPublishedMenus(Self.eventId)
        guard case .ok(let menus) = result else { Issue.record("expected menus, got \(result)"); return }
        #expect(menus.count == 5)
        let request = try #require(transport.requests.first)
        #expect(request.url?.path == "/api/public/hosted-events/40000002-0000-0000-0000-000000000002/published-menus")
        #expect(request.value(forHTTPHeaderField: "Authorization") == nil)
    }

    @Test func anEventWithNoMenusIsAnEmptyList() async {
        let result = await Self.store(MockTransport(status: 404)).loadPublishedMenus(Self.eventId)
        guard case .ok(let menus) = result else { Issue.record("expected an empty list, got \(result)"); return }
        #expect(menus.isEmpty)
    }

    @Test func theFontAndPictureAddressesAreTheApis() {
        #expect(HostedEventsStore.menuFontEndpoint("Cinzel-Bold.ttf").path == "api/public/menu-fonts/Cinzel-Bold.ttf")
        let id = UUID(uuidString: "11111111-2222-3333-4444-555555555555")!
        #expect(HostedEventsStore.menuImageEndpoint(id).path == "api/public/menu-image/11111111-2222-3333-4444-555555555555")
    }
}
