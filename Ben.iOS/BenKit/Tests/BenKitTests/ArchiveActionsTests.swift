import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Publishing a session to a place's archive, from the phone.
@Suite("Archive — publishing where you were")
@MainActor
struct ArchiveActionsTests {

    private static func actions(_ transport: MockTransport) -> ArchiveActions {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return ArchiveActions(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    /// Captured from the running API's own serializer, not invented — the shape the picker reads.
    nonisolated static let candidatesJSON = """
    [{"id":"11111111-1111-1111-1111-111111111111","name":"Bell Witch Cave","city":"Adams",
      "state":"TN","miles":0.06,"publishedSessions":3}]
    """

    @Test func candidatesDecodeWithTheCountThatMakesOneWorthPicking() async {
        let transport = MockTransport { request in
            (Data(Self.candidatesJSON.utf8), MockTransport.response(for: request, status: 200))
        }
        let found = await Self.actions(transport).candidates(latitude: 36.5806, longitude: -87.0644)

        #expect(found.count == 1)
        #expect(found.first?.name == "Bell Witch Cave")
        // The number that tells somebody there is already an archive here.
        #expect(found.first?.publishedSessions == 3)
        #expect(found.first?.where_ == "Adams, TN")
        // Feet, not fractions of a mile, at the distances that decide "same place or not".
        #expect(found.first?.distanceText == "317 ft away")
    }

    @Test func withoutCoordinatesItAsksNothingAtAll() async {
        // A session recorded with location declined must not send a query with null coordinates
        // and must not be an error — its owner simply names the place instead.
        let transport = MockTransport { request in
            (Data("[]".utf8), MockTransport.response(for: request, status: 200))
        }
        let found = await Self.actions(transport).candidates(latitude: nil, longitude: nil)

        #expect(found.isEmpty)
        #expect(transport.requests.isEmpty)
    }

    @Test func publishingToAnExistingPlaceSendsThePlaceId() async {
        let transport = MockTransport { request in
            (Data(), MockTransport.response(for: request, status: 204))
        }
        let session = UUID(), place = UUID()
        let result = await Self.actions(transport).publish(sessionId: session, toExisting: place)

        #expect(result.isSuccess)
        let sent = transport.requests.first
        #expect(sent?.url?.path == "/api/field-sessions/\(session.uuidString.lowercased())/publish")
        // Case-insensitively: Swift's encoder writes UUIDs uppercase in a JSON body while the
        // PATH is lower-cased by hand, and ASP.NET parses either. Asserting one casing tests the
        // encoder's preference rather than the contract.
        let body = String(decoding: sent?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.lowercased().contains(place.uuidString.lowercased()))
        #expect(!body.contains("newPlace"))
    }

    @Test func namingAPlaceCarriesTheCoordinatesSoTheServerCanMatchBeforeItCreates() async {
        let transport = MockTransport { request in
            (Data(), MockTransport.response(for: request, status: 204))
        }
        _ = await Self.actions(transport).publish(
            sessionId: UUID(),
            naming: NewArchivePlace(name: "Bell Witch Cave", city: "Adams", state: "TN",
                                    latitude: 36.5806, longitude: -87.0644))

        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("newPlace"))
        // Without these the server cannot tell one cave from a second page for the same cave.
        #expect(body.contains("36.5806"))
        #expect(body.contains("-87.0644"))
    }

    @Test func aRefusalKeepsTheServersOwnSentence() async {
        // "Only public locations have an open archive" tells somebody what to do about it;
        // "couldn't publish" tells them nothing.
        let sentence = "Only public locations have an open archive."
        let transport = MockTransport { request in
            (Data("\"\(sentence)\"".utf8), MockTransport.response(for: request, status: 400))
        }
        let result = await Self.actions(transport).publish(sessionId: UUID(), toExisting: UUID())

        guard case .failure(let error) = result else { Issue.record("expected refusal"); return }
        #expect(error.message == sentence)
    }

    @Test func retractingIsADeleteOnTheSameAddress() async {
        let transport = MockTransport { request in
            (Data(), MockTransport.response(for: request, status: 204))
        }
        let session = UUID()
        #expect(await Self.actions(transport).retract(sessionId: session).isSuccess)
        #expect(transport.requests.first?.httpMethod == "DELETE")
        #expect(transport.requests.first?.url?.path
                == "/api/field-sessions/\(session.uuidString.lowercased())/publish")
    }
}

private extension Result where Success == Void {
    var isSuccess: Bool { if case .success = self { true } else { false } }
}
