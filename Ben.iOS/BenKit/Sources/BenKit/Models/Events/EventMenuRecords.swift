import Foundation

// Designed menus (10/13/2026): ports of Ben.Service.Models/Entities/EventMenuDesignRecords.cs, and of
// the design catalog in Ben.Data.Common/Menus. Raw enum values are the C# ones — append-only on both
// sides. The words a menu prints are decided by `EventMenuWording`, a port of EventMenuPresentation.cs,
// so the app, the website and the PDF say the same things.

// ── a published menu ─────────────────────────────────────────────────────────

/// How an item on a menu is paid for.
public enum MenuPricing: Int, Sendable, Codable, Equatable {
    case includedInMeal = 1
    case includedInEvent = 2
    case pricedSeparately = 3
}

/// What a dish is. Flags, as the server sends them.
public struct MenuDietary: OptionSet, Sendable, Codable, Equatable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }
    public static let vegan = MenuDietary(rawValue: 1)
    public static let vegetarian = MenuDietary(rawValue: 2)
    public static let glutenFree = MenuDietary(rawValue: 4)
    public static let dairyFree = MenuDietary(rawValue: 8)
    public static let nutFree = MenuDietary(rawValue: 16)
    public static let spicy = MenuDietary(rawValue: 32)
    public static let halal = MenuDietary(rawValue: 64)
    public static let kosher = MenuDietary(rawValue: 128)
}

/// What a dish contains: the nine major US allergens. Flags.
public struct MenuAllergens: OptionSet, Sendable, Codable, Equatable, Hashable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }
    public static let milk = MenuAllergens(rawValue: 1)
    public static let eggs = MenuAllergens(rawValue: 2)
    public static let fish = MenuAllergens(rawValue: 4)
    public static let shellfish = MenuAllergens(rawValue: 8)
    public static let treeNuts = MenuAllergens(rawValue: 16)
    public static let peanuts = MenuAllergens(rawValue: 32)
    public static let wheat = MenuAllergens(rawValue: 64)
    public static let soy = MenuAllergens(rawValue: 128)
    public static let sesame = MenuAllergens(rawValue: 256)
}

/// One item as a menu shows it. (The server's record carries more, for its own builder; these are the
/// fields a reader needs.)
public struct EventMenuItem: Sendable, Codable, Equatable {
    public var id: UUID
    public var name: String
    public var description: String?
    public var imageUploadFileId: UUID?
    public var dietary: MenuDietary
    public var allergens: MenuAllergens
    public var dietaryNote: String?
}

public struct EventMenuEntry: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var pricing: MenuPricing
    public var price: Decimal?
    public var note: String?
    public var item: EventMenuItem
}

public struct EventMenuSection: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    /// What the section prints as: its own heading or its category's name.
    public var title: String
    public var entries: [EventMenuEntry]
}

public struct EventMenuPrice: Sendable, Codable, Equatable {
    public var label: String?
    public var amount: Decimal
}

/// A meal a menu is served at, on the venue's own clock.
public struct EventMenuMeal: Sendable, Codable, Equatable {
    public var hostedEventMenuId: UUID
    /// The night, a calendar date with no time.
    public var nightDate: Date
    public var nightTitle: String?
    public var mealTitle: String
    /// C# TimeSpan, "19:00:00", in the venue's own time.
    public var servedAtLocal: String?
}

/// `GET api/public/hosted-events/{id}/published-menus`: what anybody looking at the event may read.
public struct EventMenu: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var hostedEventId: UUID
    public var shownHeading: String
    public var intro: String?
    public var throughoutTheEvent: Bool
    public var designKey: String
    public var logoUploadFileId: UUID?
    public var priceNote: String?
    public var prices: [EventMenuPrice]
    public var sections: [EventMenuSection]
    public var meals: [EventMenuMeal]
    public var eventName: String
    public var venueName: String?
}

// ── the designs ──────────────────────────────────────────────────────────────

public enum MenuFontKind: Int, Sendable, Codable, Equatable { case serif = 0, sans = 1, typewriter = 2 }
public enum MenuFrame: Int, Sendable, Codable, Equatable { case none = 0, single = 1, double = 2, corners = 3 }

/// The small drawing above a menu's heading. Unknown values (a design added on the server later)
/// read as `.none`, so a newer design never stops an older app reading the catalog.
public enum MenuOrnament: Int, Sendable, Equatable {
    case none = 0, flourish, crescent, stars, candle, planchette, waveform, leaf, stamp, ghost, bat
}

extension MenuOrnament: Codable {
    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(Int.self)
        self = MenuOrnament(rawValue: raw) ?? .none
    }
}

/// One look, as `GET api/public/menu-designs` sends it.
public struct MenuDesign: Sendable, Codable, Equatable, Identifiable {
    public var key: String
    public var name: String
    public var family: String
    public var description: String
    public var font: MenuFontKind
    public var paper: String
    public var ink: String
    public var accent: String
    public var muted: String
    public var frame: MenuFrame
    public var ornament: MenuOrnament
    public var upperHeadings: Bool
    public var twoColumns: Bool
    public var dottedLeaders: Bool
    public var headingFace: String?
    public var bodyFace: String?
    public var headingWeight: Int
    public var headingScale: Double
    public var isDark: Bool

    public var id: String { key }
}

/// One font file the designs are set in, served at `api/public/menu-fonts/{file}`.
public struct MenuFontFile: Sendable, Codable, Equatable, Hashable {
    public var family: String
    public var weight: Int
    public var italic: Bool
    public var file: String
}

/// `GET api/public/menu-designs`.
public struct MenuDesignCatalog: Sendable, Codable, Equatable {
    public var designs: [MenuDesign]
    public var fonts: [MenuFontFile]

    public func design(_ key: String) -> MenuDesign? { designs.first { $0.key == key } }

    /// The file closest to what's asked for — the same rule as MenuFonts.Closest on the server: the
    /// exact cut, else the nearest weight in the same style, else the upright one.
    public func closest(_ family: String?, weight: Int, italic: Bool) -> MenuFontFile? {
        guard let family else { return nil }
        let faces = fonts.filter { $0.family == family }
        if faces.isEmpty { return nil }
        let styled = faces.filter { $0.italic == italic }
        let pool = styled.isEmpty ? faces.filter { !$0.italic } : styled
        return pool.min { a, b in
            let da = abs(a.weight - weight), db = abs(b.weight - weight)
            return da != db ? da < db : a.weight > b.weight
        }
    }
}

// ── the words ────────────────────────────────────────────────────────────────

/// What a menu prints, decided the way EventMenuPresentation.cs decides it for the page and the PDF.
public enum EventMenuWording {
    private static let us = Locale(identifier: "en_US")

    public static func money(_ amount: Decimal) -> String {
        let formatter = NumberFormatter()
        formatter.locale = us
        formatter.numberStyle = .currency
        var whole = Decimal()
        var copy = amount
        NSDecimalRound(&whole, &copy, 0, .plain)
        formatter.minimumFractionDigits = amount == whole ? 0 : 2
        formatter.maximumFractionDigits = 2
        return formatter.string(from: amount as NSDecimalNumber) ?? "$\(amount)"
    }

    /// "Adults $25 · Children $12", or "$25 per person".
    public static func priceLine(_ menu: EventMenu) -> String? {
        if menu.prices.isEmpty { return nil }
        if menu.prices.count == 1, (menu.prices[0].label ?? "").trimmingCharacters(in: .whitespaces).isEmpty {
            return "\(money(menu.prices[0].amount)) per person"
        }
        return menu.prices.map { price in
            let label = (price.label ?? "").trimmingCharacters(in: .whitespaces)
            return label.isEmpty ? money(price.amount) : "\(label) \(money(price.amount))"
        }.joined(separator: "  ·  ")
    }

    private static func entries(_ menu: EventMenu) -> [EventMenuEntry] { menu.sections.flatMap(\.entries) }

    public static func allIncludedInEvent(_ menu: EventMenu) -> Bool {
        let all = entries(menu)
        return !all.isEmpty && all.allSatisfy { $0.pricing == .includedInEvent }
    }

    public static func includedLine(_ menu: EventMenu) -> String? {
        allIncludedInEvent(menu) ? "Included with the event" : nil
    }

    /// A price, "Included", or nothing, for the right of an item.
    public static func entryPrice(_ menu: EventMenu, _ entry: EventMenuEntry) -> String? {
        switch entry.pricing {
        case .pricedSeparately: return entry.price.map(money)
        case .includedInEvent: return allIncludedInEvent(menu) ? nil : "Included"
        case .includedInMeal: return nil
        }
    }

    private static let dietaryMarks: [(MenuDietary, String, String)] = [
        (.vegan, "VG", "vegan"), (.vegetarian, "V", "vegetarian"), (.glutenFree, "GF", "gluten free"),
        (.dairyFree, "DF", "dairy free"), (.nutFree, "NF", "nut free"), (.spicy, "S", "spicy"),
        (.halal, "H", "halal"), (.kosher, "K", "kosher"),
    ]

    private static let allergenWords: [(MenuAllergens, String)] = [
        (.milk, "milk"), (.eggs, "eggs"), (.fish, "fish"), (.shellfish, "shellfish"), (.treeNuts, "tree nuts"),
        (.peanuts, "peanuts"), (.wheat, "wheat"), (.soy, "soy"), (.sesame, "sesame"),
    ]

    /// "VG GF".
    public static func letters(_ dietary: MenuDietary) -> String? {
        let letters = dietaryMarks.filter { dietary.contains($0.0) }.map(\.1)
        return letters.isEmpty ? nil : letters.joined(separator: " ")
    }

    /// "Contains milk, eggs." with any note after it.
    public static func allergenLine(_ item: EventMenuItem) -> String? {
        let words = allergenWords.filter { item.allergens.contains($0.0) }.map(\.1)
        let contains = words.isEmpty ? nil : "Contains \(words.joined(separator: ", "))."
        switch (contains, item.dietaryNote) {
        case (nil, nil): return nil
        case (let c?, nil): return c
        case (nil, let n?): return n
        case (let c?, let n?): return "\(c) \(n)"
        }
    }

    /// The key to the letters this menu uses.
    public static func key(_ menu: EventMenu) -> String? {
        let used = entries(menu).reduce(MenuDietary()) { $0.union($1.item.dietary) }
        let parts = dietaryMarks.filter { used.contains($0.0) }.map { "\($0.1) \($0.2)" }
        return parts.isEmpty ? nil : parts.joined(separator: "  ·  ")
    }

    /// "Friday, October 30 · Dinner, 7:00 PM" per meal, or "Throughout the event".
    public static func whenLines(_ menu: EventMenu) -> [String] {
        if menu.throughoutTheEvent { return ["Throughout the event"] }
        let day = DateFormatter()
        day.locale = us
        day.timeZone = TimeZone(identifier: "UTC")
        day.dateFormat = "EEEE, MMMM d"
        return menu.meals.map { meal in
            let at = servedAt(meal.servedAtLocal).map { ", \($0)" } ?? ""
            return "\(day.string(from: meal.nightDate)) · \(meal.mealTitle)\(at)"
        }
    }

    /// "19:00:00" as "7:00 PM", never moved to the phone's clock.
    public static func servedAt(_ timeSpan: String?) -> String? {
        guard let timeSpan else { return nil }
        let parts = timeSpan.split(separator: ":").compactMap { Int($0) }
        guard parts.count >= 2, (0..<24).contains(parts[0]), (0..<60).contains(parts[1]) else { return nil }
        let hour12 = parts[0] % 12 == 0 ? 12 : parts[0] % 12
        return String(format: "%d:%02d %@", hour12, parts[1], parts[0] < 12 ? "AM" : "PM")
    }

    /// The credit at the foot of every menu, as the server writes it.
    public static func credit(year: Int) -> String { "Menu made with IsHaunted.com · © \(year) ISHAUNTED LLC" }

    /// What the event's price covers, for beside the booking: "Friday dinner and Snack table".
    public static func includedWithTheEvent(_ menus: [EventMenu]) -> String? {
        let day = DateFormatter()
        day.locale = us
        day.timeZone = TimeZone(identifier: "UTC")
        day.dateFormat = "EEEE"
        var names: [String] = []
        for menu in menus where entries(menu).contains(where: { $0.pricing == .includedInEvent }) {
            let what = menu.throughoutTheEvent ? menu.shownHeading
                : menu.meals.count == 1 ? "\(day.string(from: menu.meals[0].nightDate)) \(menu.meals[0].mealTitle.lowercased())"
                : menu.shownHeading
            let name = allIncludedInEvent(menu) ? what : "some of \(what)"
            if !names.contains(name) { names.append(name) }
        }
        switch names.count {
        case 0: return nil
        case 1: return names[0]
        default: return names.dropLast().joined(separator: ", ") + " and " + names.last!
        }
    }
}
