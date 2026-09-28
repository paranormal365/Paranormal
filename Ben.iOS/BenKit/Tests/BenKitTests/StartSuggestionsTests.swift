import Foundation
import Testing
@testable import BenKit

/// What the new-session sheet offers from where the phone is (Ben, 2026-09-27).
@Suite("Start suggestions")
struct StartSuggestionsTests {
    private let now = Date(timeIntervalSince1970: 1_800_000_000)
    // The Old Mill, and a point 0.1 mi away from it.
    private let mill = (lat: 36.1627, lon: -86.7816)
    private let standing = (lat: 36.1640, lon: -86.7816)

    private func investigation(_ title: String, at point: (lat: Double, lon: Double)?,
                               startsIn hours: Double) -> MyInvestigation {
        var item = MyInvestigation(
            attendeeId: UUID(), investigationId: UUID(), caseId: nil, caseReference: nil,
            caseTitle: nil, orgId: UUID(), orgName: "Org", orgUrlName: nil, title: title,
            scheduledDateTime: now.addingTimeInterval(hours * 3600), endDateTime: nil,
            location: nil, status: 1, assignedRole: nil, rsvp: .going, didAttend: nil,
            evidenceDueDate: nil)
        item.latitude = point?.lat
        item.longitude = point?.lon
        return item
    }

    @Test func theInvestigationHereTonightIsChosenForThem() {
        let here = investigation("The Old Mill", at: mill, startsIn: 1)
        let elsewhere = investigation("Waverly", at: (38.13, -85.84), startsIn: 2)
        #expect(StartSuggestions.preselect([elsewhere, here], latitude: standing.lat,
                                           longitude: standing.lon, now: now)?.title == "The Old Mill")
    }

    @Test func nothingIsChosenWithoutAPositionOrWhenTwoWouldFit() {
        let here = investigation("The Old Mill", at: mill, startsIn: 1)
        #expect(StartSuggestions.preselect([here], latitude: nil, longitude: nil, now: now) == nil)
        let alsoHere = investigation("Mill, second team", at: mill, startsIn: 2)
        #expect(StartSuggestions.preselect([here, alsoHere], latitude: standing.lat,
                                           longitude: standing.lon, now: now) == nil)
    }

    @Test func somewhereHereButNextMonthIsListedFirstButNotChosen() {
        let nextMonth = investigation("The Old Mill again", at: mill, startsIn: 24 * 30)
        let tonightElsewhere = investigation("Waverly", at: (38.13, -85.84), startsIn: 2)
        let ordered = StartSuggestions.order([tonightElsewhere, nextMonth], latitude: standing.lat,
                                             longitude: standing.lon, now: now)
        #expect(ordered.map(\.title) == ["The Old Mill again", "Waverly"])
        #expect(StartSuggestions.preselect(ordered, latitude: standing.lat, longitude: standing.lon,
                                           now: now) == nil)
    }

    @Test func anInvestigationWithNoCoordinatesIsNeverHere() {
        let unknown = investigation("Somewhere", at: nil, startsIn: 1)
        #expect(!StartSuggestions.isHere(unknown, latitude: standing.lat, longitude: standing.lon))
    }

    @Test func aKnownPlaceCloseByNamesTheSessionBeforeAnAddress() {
        let close = ArchivePlaceCandidate(id: UUID(), name: "The Old Mill", city: "Nashville",
                                          state: "TN", miles: 0.05, publishedSessions: 3)
        let far = ArchivePlaceCandidate(id: UUID(), name: "The Other Mill", city: nil, state: nil,
                                        miles: 0.9, publishedSessions: 1)
        #expect(StartSuggestions.placeName(candidates: [far, close], address: "12 Elm St") == "The Old Mill")
        #expect(StartSuggestions.placeName(candidates: [far], address: "12 Elm St") == "12 Elm St")
        #expect(StartSuggestions.placeName(candidates: [], address: "  ") == nil)
    }

    @Test func aPublicSessionSaysWhereAndHowFar() {
        var row = PublicArchiveSession(
            id: UUID(), placeId: UUID(), placeName: "The Old Mill", placeCity: "Nashville",
            placeState: "TN", publicLatitude: 36.15, publicLongitude: -86.75, recordedBy: "Ada",
            locationLabel: nil, startedAt: now, endedAt: nil, readingCount: 10, markerCount: 1,
            mediaCount: 2, canDownload: true, distanceMiles: 1.24)
        #expect(row.placeTitle == "The Old Mill")
        #expect(row.whereLine == "Nashville, TN · 1.2 mi")
        row.placeName = nil
        #expect(row.placeTitle == "Nashville, TN")
    }

    @Test func theLookupDecodesWhatTheServerSends() throws {
        // Shape of api/public/field-sessions/nearby, camelCase, as ASP.NET writes it.
        let json = """
        [{"id":"7a3b1c52-0d1e-4f2a-9b8c-1234567890ab","placeId":"1a3b1c52-0d1e-4f2a-9b8c-1234567890ab",
          "placeName":"The Old Mill","placeCity":"Nashville","placeState":"TN",
          "publicLatitude":36.15,"publicLongitude":-86.78,"recordedBy":"Ada Recorder",
          "locationLabel":null,"startedAt":"2026-09-26T01:00:00","endedAt":null,
          "readingCount":120,"markerCount":3,"mediaCount":2,"canDownload":true,"distanceMiles":0.4}]
        """
        let rows = try BenJSON.decoder.decode([PublicArchiveSession].self, from: Data(json.utf8))
        #expect(rows.first?.placeName == "The Old Mill")
        #expect(rows.first?.distanceMiles == 0.4)
    }
}
