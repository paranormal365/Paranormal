import Foundation
import Testing
@testable import BenKit

/// The other half of the closure contract.
///
/// The literal below is byte-for-byte what `AccountClosureWireShapeTests` on the server asserts
/// it emits. Neither side can be renamed without one of the two failing — which is the only
/// thing that catches a rename here, because both sides go on compiling perfectly and the screen
/// just starts telling an owner that nothing is blocking them.
@Suite("Account closure — the wire contract")
struct AccountClosureContractTests {

    /// Copied from the server test. Do not "tidy" it.
    static let blockedJSON = """
    {"canClose":false,"ownedOrganizations":[{"organizationId":"11111111-1111-1111-1111-111111111111","name":"Paranormal 365","urlName":"paranormal365"}]}
    """

    @Test func aBlockedCheckDecodesWithTheGroupNamed() throws {
        let check = try BenJSON.decoder.decode(
            AccountClosureCheck.self, from: Data(Self.blockedJSON.utf8))

        #expect(check.canClose == false)
        #expect(check.ownedOrganizations.count == 1)
        // The name is the whole point — a refusal that cannot say which group is a dead end.
        #expect(check.ownedOrganizations.first?.name == "Paranormal 365")
        #expect(check.ownedOrganizations.first?.urlName == "paranormal365")
        #expect(check.ownedOrganizations.first?.organizationId
                == UUID(uuidString: "11111111-1111-1111-1111-111111111111"))
    }

    @Test func aClearCheckDecodesAsDeletable() throws {
        let json = #"{"canClose":true,"ownedOrganizations":[]}"#
        let check = try BenJSON.decoder.decode(AccountClosureCheck.self, from: Data(json.utf8))

        #expect(check.canClose)
        #expect(check.ownedOrganizations.isEmpty)
    }

    /// The server rejects any other word with "Type DELETE to confirm.", so this constant is
    /// part of the API contract rather than a label.
    @Test func theConfirmationWordMatchesWhatTheServerRequires() {
        #expect(AccountClosureCheck.confirmationWord == "DELETE")
    }
}
