import Foundation

// Ports of the tour half of Ben.Service.Models/Entities/PublicEventRecords.cs — what a visitor
// may know about a walking tour (item 233, read by the phone for item 234's Haunted Tours tab).
//
// Captured against the running API on 2026-09-11 rather than written from the C#: a field the
// server does not actually send is a field that decodes to nil on a real device and nowhere else.

/// One tour as it appears in a list — near me, or near a place looked up.
public struct PublicTourListItem: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var name: String
    public var urlName: String
    public var organizationId: UUID
    public var organizationName: String
    public var organizationUrlName: String
    public var city: String?
    public var state: String?
    /// The meeting point. A tour's start is PUBLIC by nature — it is where a guest is told to
    /// stand — so unlike an event's, this is the real thing and not a snapped approximation.
    public var latitude: Decimal?
    public var longitude: Decimal?
    public var durationMinutes: Int?
    public var nextDateStartUtc: Date?
    public var upcomingDateCount: Int
    public var rating: Decimal?
    public var ratingCount: Int
    /// How far from where the search was run. Nil when nobody gave a place to measure from.
    public var distanceMiles: Double?
    public var coverUploadFileId: UUID?
    /// The IANA zone the walk runs on. See `EventClock`.
    public var timeZoneId: String?
}

extension PublicTourListItem {
    public var placeLabel: String? {
        let parts = [city, state].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    /// "1 hr 30 min", the way the website says it.
    public var lengthLabel: String? {
        guard let minutes = durationMinutes, minutes > 0 else { return nil }
        let hours = minutes / 60, rest = minutes % 60
        return switch (hours, rest) {
        case (0, let m): "\(m) min"
        case (let h, 0): h == 1 ? "1 hr" : "\(h) hrs"
        case (let h, let m): "\(h) hr \(m) min"
        }
    }

    /// "0.2 miles away". Nil when the search had no place to measure from.
    public var distanceLabel: String? {
        guard let miles = distanceMiles else { return nil }
        return miles < 0.1 ? "Right here" : String(format: "%.1f miles away", miles)
    }

    /// "5.0 ★ (1)", or nil when nobody has rated it.
    public var ratingLabel: String? {
        guard let rating, ratingCount > 0 else { return nil }
        return String(format: "%.1f ★ (%d)", Double(truncating: rating as NSNumber), ratingCount)
    }
}

/// One image in a tour's gallery.
public struct PublicTourImage: Sendable, Codable, Equatable {
    public var uploadFileId: UUID
    public var caption: String?
}

/// Somewhere else the business can be found — its own site, or a social account.
public struct PublicTourLink: Sendable, Codable, Equatable {
    public var platform: Int
    public var name: String
    public var url: String
}

/// One tour, everything a guest may see (`GET api/public/organizations/{org}/tours/{slug}`).
public struct PublicTourRecord: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var name: String
    public var urlName: String
    public var description: String?
    public var organizationId: UUID
    public var organizationName: String
    public var organizationUrlName: String
    /// Where you stand. A tour's start is public by nature.
    public var meetingPoint: String?
    public var city: String?
    public var state: String?
    public var latitude: Decimal?
    public var longitude: Decimal?
    public var durationMinutes: Int?
    public var defaultCapacity: Int?
    public var timeZoneId: String?
    /// Free text the business wrote — a phone number, how to pay. **This site never takes money.**
    public var contactLine: String?
    /// False for a closed season. Dates already set still stand.
    public var isBookable: Bool
    public var allowReviews: Bool
    public var guides: [PublicGuide]?
    public var upcomingDates: [PublicEventListItem]?
    public var rating: Decimal?
    public var ratingCount: Int
    public var gallery: [PublicTourImage]?
    public var links: [PublicTourLink]?
}

extension PublicTourRecord {
    public var placeLabel: String? {
        let parts = [city, state].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    public var ratingLabel: String? {
        guard let rating, ratingCount > 0 else { return nil }
        return String(format: "%.1f ★ (%d)", Double(truncating: rating as NSNumber), ratingCount)
    }
}
