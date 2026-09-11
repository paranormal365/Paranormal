import Foundation
import Testing
@testable import BenKit

/// What the Haunted Tours list says about a walk (item 234).
///
/// The labels are where a list like this goes wrong: a distance rounded to nothing, a length
/// printed as "90 min" when everybody says an hour and a half, a rating shown for a tour nobody
/// has rated. Each is one line of code and one line of judgement, so each has a test.
struct TourListTests {

    private static func tour(
        durationMinutes: Int? = 90, distanceMiles: Double? = nil,
        rating: Decimal? = nil, ratingCount: Int = 0,
        city: String? = "Nashville", state: String? = "TN"
    ) -> PublicTourListItem {
        PublicTourListItem(
            id: UUID(), name: "Printers Alley Ghost Walk", urlName: "printers-alley-ghost-walk",
            organizationId: UUID(), organizationName: "Printers Alley Walks",
            organizationUrlName: "printers-alley-walks", city: city, state: state,
            latitude: nil, longitude: nil, durationMinutes: durationMinutes,
            nextDateStartUtc: nil, upcomingDateCount: 0, rating: rating, ratingCount: ratingCount,
            distanceMiles: distanceMiles, coverUploadFileId: nil, timeZoneId: "America/Chicago")
    }

    @Test func lengthIsSaidTheWayPeopleSayIt() {
        #expect(Self.tour(durationMinutes: 90).lengthLabel == "1 hr 30 min")
        #expect(Self.tour(durationMinutes: 60).lengthLabel == "1 hr")
        #expect(Self.tour(durationMinutes: 120).lengthLabel == "2 hrs")
        #expect(Self.tour(durationMinutes: 45).lengthLabel == "45 min")
        // Not "0 min": a tour with no length recorded says nothing rather than something wrong.
        #expect(Self.tour(durationMinutes: nil).lengthLabel == nil)
        #expect(Self.tour(durationMinutes: 0).lengthLabel == nil)
    }

    @Test func aTourOnTopOfYouDoesNotSayZeroPointZeroMilesAway() {
        #expect(Self.tour(distanceMiles: 0.04).distanceLabel == "Right here")
        #expect(Self.tour(distanceMiles: 0.2).distanceLabel == "0.2 miles away")
        #expect(Self.tour(distanceMiles: 12.34).distanceLabel == "12.3 miles away")
    }

    @Test func aSearchWithNowhereToMeasureFromSaysNoDistance() {
        // The endpoint answers without distanceMiles when nobody gave it a point, and a row that
        // invented "0.0 miles away" for that would be telling somebody it is on their doorstep.
        #expect(Self.tour(distanceMiles: nil).distanceLabel == nil)
    }

    @Test func aTourNobodyHasRatedShowsNoStars() {
        #expect(Self.tour(rating: nil, ratingCount: 0).ratingLabel == nil)
        // The guard is on the COUNT as well: a rating with nobody behind it is not a rating.
        #expect(Self.tour(rating: 5, ratingCount: 0).ratingLabel == nil)
        #expect(Self.tour(rating: 5, ratingCount: 1).ratingLabel == "5.0 ★ (1)")
        // The server sends the average already rounded to one decimal (item 233), so this is the
        // shape that actually arrives rather than a half-way value nobody will ever see.
        #expect(Self.tour(rating: Decimal(string: "4.5"), ratingCount: 8).ratingLabel == "4.5 ★ (8)")
    }

    @Test func aPlaceIsOnlyPrintedWhenThereIsOne() {
        #expect(Self.tour().placeLabel == "Nashville, TN")
        #expect(Self.tour(city: "Nashville", state: nil).placeLabel == "Nashville")
        #expect(Self.tour(city: nil, state: nil).placeLabel == nil)
        // An empty string is not a place — it would print as a stray comma.
        #expect(Self.tour(city: "", state: "").placeLabel == nil)
    }
}
